using Hishab.Application.Common.Tenancy;
using Hishab.Application.Shops;
using Hishab.Domain.Couriers;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hishab.Infrastructure.Shops;

internal sealed class PlanLimitService(HishabDbContext db, ITenantContext tenant, TimeProvider clock) : IPlanLimitService
{
    public async Task ReserveOrderAsync(CancellationToken cancellationToken)
    {
        var shop = await db.Shops.AsNoTracking().SingleAsync(cancellationToken);
        var limit = shop.Limits.MaxOrdersPerMonth;
        var period = BillingPeriod.Containing(clock.GetUtcNow());
        var metric = nameof(UsageMetric.Orders);

        // Check and increment in one statement. The upsert locks the counter row, so two requests
        // at 99/100 run one after the other: the second matches no row (WHERE fails) and returns
        // nothing. Raw SQL, so the tenant comes from the tenant context explicitly.
        var counts = await db.Database.SqlQuery<int>($"""
            INSERT INTO usage_counters (tenant_id, period, metric, count)
            VALUES ({TenantId}, {period.Key}, {metric}, 1)
            ON CONFLICT (tenant_id, period, metric)
            DO UPDATE SET count = usage_counters.count + 1
            WHERE usage_counters.count < {limit ?? int.MaxValue}
            RETURNING count AS "Value"
            """).ToListAsync(cancellationToken);

        if (counts.Count == 0)
        {
            throw PlanLimitExceededException.Orders(shop.Plan, limit ?? int.MaxValue);
        }
    }

    public async Task EnsureCanAddStaffAsync(CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("EnsureCanAddStaffAsync must run inside the transaction that adds the user.");
        }

        // Row lock on this shop until the transaction ends: parallel invites wait here, so each
        // one counts the users the previous one added. Users can be removed, so the limit counts
        // real rows rather than a counter.
        var shop = await db.Shops
            .FromSql($"SELECT * FROM shops WHERE id = {TenantId} FOR UPDATE")
            .SingleAsync(cancellationToken);

        var users = await db.Users.CountAsync(cancellationToken);
        if (users >= shop.Limits.MaxUsers)
        {
            throw PlanLimitExceededException.Users(shop.Plan, shop.Limits.MaxUsers);
        }
    }

    public async Task EnsureCanConnectCourierAsync(CourierProvider provider, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("EnsureCanConnectCourierAsync must run inside the transaction that connects the courier.");
        }

        // Same lock as staff invites: parallel connects are checked one after another.
        var shop = await db.Shops
            .FromSql($"SELECT * FROM shops WHERE id = {TenantId} FOR UPDATE")
            .SingleAsync(cancellationToken);

        if (shop.Limits.MaxCouriers is not { } max
            || await db.CourierAccounts.AnyAsync(a => a.Provider == provider, cancellationToken))
        {
            return; // unlimited, or new keys for a courier the shop already has
        }

        if (await db.CourierAccounts.CountAsync(cancellationToken) >= max)
        {
            throw PlanLimitExceededException.Couriers(shop.Plan, max);
        }
    }

    public async Task<ShopUsage> GetUsageAsync(CancellationToken cancellationToken)
    {
        var shop = await db.Shops.AsNoTracking().SingleAsync(cancellationToken);
        var period = BillingPeriod.Containing(clock.GetUtcNow());

        var orders = await db.UsageCounters
            .Where(c => c.Period == period.Key && c.Metric == UsageMetric.Orders)
            .Select(c => c.Count)
            .SingleOrDefaultAsync(cancellationToken);
        var users = await db.Users.CountAsync(cancellationToken);

        return new ShopUsage(
            shop.Plan,
            period.Key,
            period.StartsAt,
            period.EndsAt,
            new UsageMeter(orders, shop.Limits.MaxOrdersPerMonth),
            new UsageMeter(users, shop.Limits.MaxUsers));
    }

    private Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("Plan limits need a tenant.");
}
