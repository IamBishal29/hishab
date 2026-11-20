using System.Linq.Expressions;
using Hishab.Application.Common.Tenancy;
using Hishab.Domain.Common;
using Hishab.Domain.Customers;
using Hishab.Domain.Orders;
using Hishab.Domain.Products;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Identity;
using Hishab.Infrastructure.Persistence.Tenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Hishab.Infrastructure.Persistence;

/// <summary>
/// The application database. Built on Identity's user-only context (no role tables: one
/// role per user, ADR 0006).
/// </summary>
public class HishabDbContext : IdentityUserContext<HishabUser, Guid>
{
    /// <summary>Name of the global query filter that isolates tenants.</summary>
    public const string TenantFilter = "Tenant";

    /// <summary>Name of the global query filter that hides soft-deleted rows.</summary>
    public const string SoftDeleteFilter = "SoftDelete";

    private readonly ITenantContext _tenantContext;

    public HishabDbContext(DbContextOptions<HishabDbContext> options, ITenantContext tenantContext)
        : this((DbContextOptions)options, tenantContext)
    {
    }

    /// <summary>For derived contexts (tests) that add their own entity types.</summary>
    protected HishabDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Read by the tenant query filter. EF Core evaluates it per query on the context
    /// instance running that query, so each request sees only its own tenant.
    /// </summary>
    public Guid? CurrentTenantId => _tenantContext.TenantId;

    public DbSet<Shop> Shops => Set<Shop>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Identity's own mapping first, ours on top
        builder.ApplyConfigurationsFromAssembly(typeof(HishabDbContext).Assembly);
        ApplyTenantConventions(builder);
        ApplySoftDeleteConvention(builder);

        // The shop is the tenant itself (not ITenantOwned): the same named filter keeps every
        // shop query to the caller's own shop, so no endpoint can read another shop by id.
        builder.Entity<Shop>().HasQueryFilter(TenantFilter, s => s.Id == CurrentTenantId);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every Money column: numeric(12,2), up to 9,999,999,999.99 BDT.
        configurationBuilder.Properties<Money>()
            .HaveConversion<MoneyConverter>()
            .HavePrecision(12, 2);
    }

    /// <summary>Unique index that also catches two concurrent status changes of one order.</summary>
    internal const string OrderHistorySequenceIndex = "ix_order_status_changes_order_id_sequence";

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantRules();
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException e) when (IsConcurrentHistoryAppend(e))
        {
            throw AsConcurrencyConflict(e);
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantRules();
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException e) when (IsConcurrentHistoryAppend(e))
        {
            throw AsConcurrencyConflict(e);
        }
    }

    /// <summary>
    /// Two concurrent transitions of one order collide on the history sequence index or on the
    /// order's row version, whichever statement runs first. Both mean "someone else changed this
    /// order", so callers always get one signal: <see cref="DbUpdateConcurrencyException"/>.
    /// </summary>
    private static bool IsConcurrentHistoryAppend(DbUpdateException e) =>
        e is not DbUpdateConcurrencyException
        && e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation, ConstraintName: OrderHistorySequenceIndex };

    private static DbUpdateConcurrencyException AsConcurrencyConflict(DbUpdateException e) =>
        new("The order was changed by someone else. Reload it and try again.", e);

    /// <summary>
    /// Every root entity implementing <see cref="ITenantOwned"/> gets the named tenant
    /// filter and an index on TenantId. No per-entity code, so it cannot be forgotten.
    /// </summary>
    private void ApplyTenantConventions(ModelBuilder modelBuilder)
    {
        var tenantOwned = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType) && t.BaseType is null && !t.IsOwned())
            .ToList();

        foreach (var entityType in tenantOwned)
        {
            // e => (Guid?)e.TenantId == this.CurrentTenantId
            // A null CurrentTenantId matches nothing: no tenant means no rows (fail closed).
            var entity = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Convert(Expression.Property(entity, nameof(ITenantOwned.TenantId)), typeof(Guid?)),
                Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));

            var builder = modelBuilder.Entity(entityType.ClrType);
            builder.HasQueryFilter(TenantFilter, Expression.Lambda(body, entity));
            builder.Property(nameof(ITenantOwned.TenantId)).IsRequired();
            builder.HasIndex(nameof(ITenantOwned.TenantId));
        }
    }

    /// <summary>Every root entity implementing <see cref="ISoftDeletable"/> hides deleted rows unless a query opts out by name.</summary>
    private static void ApplySoftDeleteConvention(ModelBuilder modelBuilder)
    {
        var softDeletable = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(ISoftDeletable).IsAssignableFrom(t.ClrType) && t.BaseType is null && !t.IsOwned())
            .ToList();

        foreach (var entityType in softDeletable)
        {
            // e => e.DeletedAt == null
            var entity = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(entity, nameof(ISoftDeletable.DeletedAt)),
                Expression.Constant(null, typeof(DateTimeOffset?)));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(SoftDeleteFilter, Expression.Lambda(body, entity));
        }
    }

    /// <summary>
    /// Stamps the current tenant on new rows and refuses any write that would touch
    /// another tenant's row or move a row between tenants.
    /// </summary>
    private void ApplyTenantRules()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var entityName = entry.Metadata.ClrType.Name;
            var tenantId = _tenantContext.TenantId
                ?? throw new CrossTenantWriteException($"Cannot save {entityName}: no tenant is set for this unit of work.");

            var property = entry.Property(e => e.TenantId);

            if (entry.State == EntityState.Added)
            {
                if (property.CurrentValue == Guid.Empty)
                {
                    property.CurrentValue = tenantId;
                }
                else if (property.CurrentValue != tenantId)
                {
                    throw new CrossTenantWriteException($"Cannot add {entityName} for another tenant.");
                }

                continue;
            }

            if (property.OriginalValue != tenantId || property.CurrentValue != tenantId)
            {
                throw new CrossTenantWriteException($"Cannot change {entityName}: it belongs to another tenant.");
            }
        }
    }
}
