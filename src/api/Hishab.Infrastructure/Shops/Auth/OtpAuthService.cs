using Hishab.Application.Common.Tenancy;
using Hishab.Application.Shops.Auth;
using Hishab.Domain.Common;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Identity;
using Hishab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hishab.Infrastructure.Shops.Auth;

internal sealed partial class OtpAuthService(
    HishabDbContext db,
    UserManager<HishabUser> users,
    ITenantContext tenant,
    IOtpHasher hasher,
    IOtpSender sender,
    TimeProvider clock,
    ILogger<OtpAuthService> logger) : IOtpAuthService
{
    public async Task<OtpSent> RequestOtpAsync(string phone, CancellationToken cancellationToken)
    {
        var parsed = Phone.Parse(phone);
        var now = clock.GetUtcNow();

        // Per-phone limits (unknown phones too, so this reveals nothing about accounts).
        var windowStart = now - OtpSendPolicy.Window;
        var recent = await db.OtpChallenges
            .Where(c => c.Phone == parsed && c.CreatedAt > windowStart)
            .Select(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        if (recent.Count >= OtpSendPolicy.MaxPerWindow)
        {
            throw TooMany(recent.Min() + OtpSendPolicy.Window - now);
        }

        if (recent.Count > 0 && now - recent.Max() < OtpSendPolicy.MinInterval)
        {
            throw TooMany(OtpSendPolicy.MinInterval - (now - recent.Max()));
        }

        var code = OtpCode.Generate();
        db.OtpChallenges.Add(OtpChallenge.Issue(parsed, hasher.Hash(parsed, code), now));
        await db.SaveChangesAsync(cancellationToken);
        await sender.SendAsync(parsed, code, cancellationToken);
        LogOtpIssued(logger, parsed.Masked);

        return new OtpSent((int)OtpChallenge.Lifetime.TotalSeconds, (int)OtpSendPolicy.MinInterval.TotalSeconds);
    }

    public async Task<OtpLogin> VerifyOtpAsync(string phone, string code, CancellationToken cancellationToken)
    {
        var parsed = Phone.Parse(phone);
        if (code is not { Length: OtpCode.Length } || !code.All(char.IsAsciiDigit))
        {
            throw InvalidCode();
        }

        var now = clock.GetUtcNow();
        var challenge = await db.OtpChallenges
            .Where(c => c.Phone == parsed && c.Status == OtpChallengeStatus.Pending)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw InvalidCode();

        var result = challenge.Verify(hasher.Hash(parsed, code), now);
        try
        {
            await db.SaveChangesAsync(cancellationToken); // persists attempts / lockout / single use
        }
        catch (DbUpdateConcurrencyException)
        {
            throw InvalidCode(); // a parallel verify of the same challenge won the race
        }

        if (result != OtpVerificationResult.Success)
        {
            LogOtpRejected(logger, parsed.Masked, result);
            throw InvalidCode(); // one answer for every failure reason
        }

        return await SignInOrSignUpAsync(parsed, now, cancellationToken);
    }

    private async Task<OtpLogin> SignInOrSignUpAsync(Phone phone, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Before login we do not know the tenant, and phones are unique across shops
        // (ADR 0006): the one place that searches users across tenants. Only the tenant
        // filter is switched off, by name.
        var normalized = users.NormalizeName(phone.Value);
        var existing = await db.Users
            .IgnoreQueryFilters([HishabDbContext.TenantFilter])
            .SingleOrDefaultAsync(u => u.NormalizedUserName == normalized, cancellationToken);

        if (existing is not null)
        {
            tenant.SetTenant(existing.TenantId);
            if (!existing.PhoneNumberConfirmed)
            {
                // An invited staff member's first sign-in: they have now proven the phone.
                existing.PhoneNumberConfirmed = true;
                await db.SaveChangesAsync(cancellationToken);
            }

            return new OtpLogin(existing.Id, existing.TenantId, existing.Role, IsNewShop: false);
        }

        // First login: shop and Owner are saved by one SaveChanges inside CreateAsync,
        // so we never get a shop without an owner (or the reverse).
        var shop = Shop.Create(now);
        tenant.SetTenant(shop.Id);
        db.Shops.Add(shop);

        var owner = new HishabUser
        {
            Id = Guid.NewGuid(),
            UserName = phone.Value,
            PhoneNumber = phone.Value,
            PhoneNumberConfirmed = true,
            Role = ShopRole.Owner,
            CreatedAt = now,
        };

        var created = await users.CreateAsync(owner);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                "Creating the owner failed: " + string.Join(", ", created.Errors.Select(e => e.Code)));
        }

        LogShopCreated(logger, shop.Id);
        return new OtpLogin(owner.Id, shop.Id, owner.Role, IsNewShop: true);
    }

    private static DomainValidationException InvalidCode() => new("code", OtpSendPolicy.InvalidCodeMessage);

    private static TooManyRequestsException TooMany(TimeSpan retryAfter) =>
        new("Please wait before requesting another code.", retryAfter);

    [LoggerMessage(Level = LogLevel.Information, Message = "OTP issued for {Phone}")]
    private static partial void LogOtpIssued(ILogger logger, string phone);

    [LoggerMessage(Level = LogLevel.Information, Message = "OTP rejected for {Phone}: {Result}")]
    private static partial void LogOtpRejected(ILogger logger, string phone, OtpVerificationResult result);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shop {ShopId} created on first login")]
    private static partial void LogShopCreated(ILogger logger, Guid shopId);
}
