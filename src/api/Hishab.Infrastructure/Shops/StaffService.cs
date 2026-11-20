using Hishab.Application.Common.Tenancy;
using Hishab.Application.Shops;
using Hishab.Application.Shops.Staff;
using Hishab.Domain.Common;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Identity;
using Hishab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hishab.Infrastructure.Shops;

internal sealed partial class StaffService(
    HishabDbContext db,
    UserManager<HishabUser> users,
    ITenantContext tenant,
    IPlanLimitService limits,
    TimeProvider clock,
    ILogger<StaffService> logger) : IStaffService
{
    public const string PhoneTakenMessage = "This phone number already belongs to a Hishab shop.";

    public async Task<IReadOnlyList<StaffMember>> ListAsync(CancellationToken cancellationToken)
    {
        var members = await db.Users.OrderBy(u => u.CreatedAt).ToListAsync(cancellationToken);
        return [.. members.Select(ToMember)];
    }

    public async Task<StaffMember> InviteAsync(string phone, ShopRole role, CancellationToken cancellationToken)
    {
        var parsed = Phone.Parse(phone);
        EnsureAssignable(role);

        // The limit check locks the shop row until this transaction ends, so concurrent invites
        // cannot both pass at the limit; the user is created inside the same transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await limits.EnsureCanAddStaffAsync(cancellationToken);

        // Phones are unique across shops (ADR 0006), so look beyond this tenant: the filtered
        // query would miss another shop's user and the unique index would turn it into a 500.
        var normalized = users.NormalizeName(parsed.Value);
        if (await db.Users.IgnoreQueryFilters([HishabDbContext.TenantFilter]).AnyAsync(u => u.NormalizedUserName == normalized, cancellationToken))
        {
            throw new ConflictException(PhoneTakenMessage);
        }

        var member = new HishabUser
        {
            Id = Guid.NewGuid(),
            UserName = parsed.Value,
            PhoneNumber = parsed.Value,
            PhoneNumberConfirmed = false, // becomes true at their first OTP sign-in
            Role = role,
            CreatedAt = clock.GetUtcNow(),
        };

        IdentityResult created;
        try
        {
            created = await users.CreateAsync(member);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException(PhoneTakenMessage); // a parallel invite or sign-up of the same phone won
        }

        if (!created.Succeeded)
        {
            throw new InvalidOperationException("Creating the staff member failed: " + string.Join(", ", created.Errors.Select(e => e.Code)));
        }

        await transaction.CommitAsync(cancellationToken);
        LogInvited(logger, member.Id, role);
        return ToMember(member);
    }

    public async Task<StaffMember> ChangeRoleAsync(Guid userId, ShopRole role, CancellationToken cancellationToken)
    {
        EnsureAssignable(role);
        var member = await FindNonOwnerAsync(userId, "The owner's role cannot be changed.", cancellationToken);

        member.Role = role;
        await db.SaveChangesAsync(cancellationToken);
        LogRoleChanged(logger, member.Id, role);
        return ToMember(member);
    }

    public async Task RemoveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var member = await FindNonOwnerAsync(userId, "The owner cannot be removed from the shop.", cancellationToken);

        // Hard delete: with unique phones, a soft-deleted row would stop this person from ever
        // signing up or joining another shop. Refresh tokens go with it (FK cascade), so their
        // session ends at the next refresh; the current access token lives at most 15 minutes.
        db.Users.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        LogRemoved(logger, member.Id, tenant.TenantId);
    }

    /// <summary>The tenant filter makes another shop's member "not found" (404), never "forbidden": no hint that the id exists.</summary>
    private async Task<HishabUser> FindNonOwnerAsync(Guid userId, string ownerMessage, CancellationToken cancellationToken)
    {
        var member = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("Staff member", userId);

        return member.Role == ShopRole.Owner ? throw new ConflictException(ownerMessage) : member;
    }

    private static void EnsureAssignable(ShopRole role)
    {
        if (!ShopStaffRules.IsAssignable(role))
        {
            throw new DomainValidationException("role", "Choose Manager, Moderator or Packer.");
        }
    }

    private static StaffMember ToMember(HishabUser u) => new(
        u.Id,
        u.PhoneNumber ?? string.Empty,
        u.Role,
        u.PhoneNumberConfirmed ? StaffStatus.Active : StaffStatus.Invited,
        u.CreatedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Staff member {UserId} invited as {Role}")]
    private static partial void LogInvited(ILogger logger, Guid userId, ShopRole role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Staff member {UserId} is now {Role}")]
    private static partial void LogRoleChanged(ILogger logger, Guid userId, ShopRole role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Staff member {UserId} removed from shop {ShopId}")]
    private static partial void LogRemoved(ILogger logger, Guid userId, Guid? shopId);
}
