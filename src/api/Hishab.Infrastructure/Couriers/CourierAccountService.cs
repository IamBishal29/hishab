using Hishab.Application.Common.Tenancy;
using Hishab.Application.Couriers;
using Hishab.Application.Shops;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hishab.Infrastructure.Couriers;

internal sealed partial class CourierAccountService(
    HishabDbContext db,
    ICourierProviderRegistry registry,
    ICourierCredentialProtector protector,
    IPlanLimitService limits,
    ITenantContext tenant,
    TimeProvider clock,
    ILogger<CourierAccountService> logger) : ICourierAccountService
{
    private const int MaxCredentialLength = 500;

    private Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant is set.");

    public async Task<IReadOnlyList<CourierConnection>> ListAsync(CancellationToken cancellationToken)
    {
        var accounts = await db.CourierAccounts.AsNoTracking().ToDictionaryAsync(a => a.Provider, cancellationToken);
        return [.. registry.All.Select(p => ToConnection(p, accounts.GetValueOrDefault(p.Code)))];
    }

    public async Task<CourierConnection> ConnectAsync(
        CourierProvider provider, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken)
    {
        var adapter = registry.Find(provider)
            ?? throw new DomainValidationException(new Dictionary<string, string[]> { ["provider"] = [$"{provider} is not available yet."] });
        var keys = CleanAndCheck(adapter, credentials);

        // Ask the courier before taking any lock: a slow courier must not hold up the shop's other writes.
        var check = await adapter.ValidateCredentialsAsync(keys, cancellationToken);
        if (!check.IsValid)
        {
            throw new DomainValidationException(new Dictionary<string, string[]>
            {
                ["credentials"] = [check.Message ?? "The courier rejected these keys."],
            });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await limits.EnsureCanConnectCourierAsync(provider, cancellationToken); // locks the shop row: connects run one at a time

        var ciphertext = protector.Protect(TenantId, keys);
        var hint = CourierAccount.HintFor(keys[adapter.CredentialFields[0].Name]);
        var now = clock.GetUtcNow();
        var account = await db.CourierAccounts.SingleOrDefaultAsync(a => a.Provider == provider, cancellationToken);
        if (account is null)
        {
            account = CourierAccount.Connect(provider, ciphertext, hint, now);
            db.CourierAccounts.Add(account);
        }
        else
        {
            account.ReplaceCredentials(ciphertext, hint, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogConnected(logger, provider, account.Id);
        return ToConnection(adapter, account);
    }

    public async Task DisconnectAsync(CourierProvider provider, CancellationToken cancellationToken)
    {
        var account = await FindAsync(provider, cancellationToken);
        db.CourierAccounts.Remove(account); // hard delete: the keys are gone, not hidden
        var shop = await db.Shops.SingleAsync(cancellationToken);
        shop.ForgetDefaultCourier(provider); // same SaveChanges: never a default the shop cannot book with
        await db.SaveChangesAsync(cancellationToken);
        LogDisconnected(logger, provider, account.Id);
    }

    public async Task<CourierCredentials> GetCredentialsAsync(CourierProvider provider, CancellationToken cancellationToken)
    {
        var account = await FindAsync(provider, cancellationToken);
        return protector.Unprotect(TenantId, account.ProtectedCredentials);
    }

    private async Task<CourierAccount> FindAsync(CourierProvider provider, CancellationToken cancellationToken) =>
        await db.CourierAccounts.SingleOrDefaultAsync(a => a.Provider == provider, cancellationToken)
            ?? throw new NotFoundException("Courier connection", provider);

    /// <summary>Exactly the courier's fields, each present, trimmed and of sane length; errors keyed "credentials.apiKey".</summary>
    private static CourierCredentials CleanAndCheck(ICourierProvider adapter, IReadOnlyDictionary<string, string> credentials)
    {
        var errors = new Dictionary<string, string[]>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in adapter.CredentialFields)
        {
            var value = credentials.GetValueOrDefault(field.Name)?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                errors[$"credentials.{field.Name}"] = [$"Enter the {field.Label}."];
            }
            else if (value.Length > MaxCredentialLength)
            {
                errors[$"credentials.{field.Name}"] = [$"{field.Label} is at most {MaxCredentialLength} characters."];
            }
            else
            {
                values[field.Name] = value;
            }
        }

        var unknown = credentials.Keys.Where(k => adapter.CredentialFields.All(f => f.Name != k)).ToList();
        if (unknown.Count > 0)
        {
            errors["credentials"] = [$"{adapter.Code} does not use: {string.Join(", ", unknown)}."];
        }

        return errors.Count > 0 ? throw new DomainValidationException(errors) : new CourierCredentials(values);
    }

    private static CourierConnection ToConnection(ICourierProvider adapter, CourierAccount? account) =>
        new(adapter.Code, account is not null, account?.CredentialHint, account?.ConnectedAt, adapter.CredentialFields);

    [LoggerMessage(Level = LogLevel.Information, Message = "Courier {Provider} connected ({CourierAccountId})")]
    private static partial void LogConnected(ILogger logger, CourierProvider provider, Guid courierAccountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Courier {Provider} disconnected ({CourierAccountId})")]
    private static partial void LogDisconnected(ILogger logger, CourierProvider provider, Guid courierAccountId);
}
