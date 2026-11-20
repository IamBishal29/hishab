using Hishab.Application.Customers;
using Hishab.Domain.Common;
using Hishab.Domain.Customers;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hishab.Infrastructure.Customers;

internal sealed partial class CustomerService(HishabDbContext db, TimeProvider clock, ILogger<CustomerService> logger) : ICustomerService
{
    public async Task<CustomerPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim();
            var digits = new string([.. text.Where(char.IsAsciiDigit)]);
            var namePattern = $"%{EscapeLike(text)}%";
            var phonePattern = $"%{digits}%";
            query = digits.Length >= 3 // "0171" finds +8801712345678; short digit runs would match everyone
                ? query.Where(c => EF.Functions.ILike(c.Name!, namePattern, @"\") || EF.Functions.Like(c.Phone, phonePattern))
                : query.Where(c => EF.Functions.ILike(c.Name!, namePattern, @"\"));
        }

        var total = await query.CountAsync(cancellationToken);
        var customers = await query
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new CustomerPage([.. customers.Select(ToDto)], page, pageSize, total);
    }

    public async Task<CustomerDto?> FindByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        var normalized = Phone.Parse(phone).Value;
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Phone == normalized, cancellationToken);
        return customer is null ? null : ToDto(customer);
    }

    public async Task<(CustomerDto Customer, bool Created)> FindOrCreateAsync(string phone, string? name, string? address, CancellationToken cancellationToken)
    {
        var parsed = Phone.Parse(phone);

        // Fast path: most order phones belong to returning customers.
        var existing = await db.Customers.SingleOrDefaultAsync(c => c.Phone == parsed.Value, cancellationToken);
        if (existing is not null)
        {
            if (existing.FillMissing(name, address))
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return (ToDto(existing), false);
        }

        var customer = Customer.Create(parsed, name, address, clock.GetUtcNow());
        db.Customers.Add(customer);
        try
        {
            // Inside an open transaction (order entry), EF wraps this in a savepoint, so a lost
            // race rolls back only this insert and the caller's transaction stays usable.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request created this phone's customer between our SELECT and INSERT: the
            // unique index (tenant_id, phone) let only one in. Use the winner.
            db.Entry(customer).State = EntityState.Detached;
            var winner = await db.Customers.SingleAsync(c => c.Phone == parsed.Value, cancellationToken);
            LogLostRace(logger, winner.Id);
            return (ToDto(winner), false);
        }

        LogCreated(logger, customer.Id);
        return (ToDto(customer), true);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToDto(await LoadAsync(id, cancellationToken));

    public Task<CustomerDto> UpdateAsync(Guid id, string? name, string? address, string? notes, CancellationToken cancellationToken) =>
        ChangeAsync(id, c => c.UpdateDetails(name, address, notes), cancellationToken);

    public Task<CustomerDto> BlockAsync(Guid id, string? reason, CancellationToken cancellationToken) =>
        ChangeAsync(id, c => c.Block(reason, clock.GetUtcNow()), cancellationToken);

    public Task<CustomerDto> UnblockAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, c => c.Unblock(), cancellationToken);

    private async Task<CustomerDto> ChangeAsync(Guid id, Action<Customer> change, CancellationToken cancellationToken)
    {
        var customer = await LoadAsync(id, cancellationToken);
        change(customer);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(customer);
    }

    private async Task<Customer> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new NotFoundException("Customer", id);

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    private static CustomerDto ToDto(Customer c) =>
        new(c.Id, c.Phone, c.Name, c.Address, c.Notes, c.IsBlocked, c.BlockedReason, c.BlockedAt, c.CreatedAt);

    // IDs only: the phone is personal data.
    [LoggerMessage(Level = LogLevel.Information, Message = "Customer {CustomerId} created")]
    private static partial void LogCreated(ILogger logger, Guid customerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Parallel create resolved to existing customer {CustomerId}")]
    private static partial void LogLostRace(ILogger logger, Guid customerId);
}
