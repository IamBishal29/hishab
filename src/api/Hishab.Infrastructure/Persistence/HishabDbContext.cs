using System.Linq.Expressions;
using Hishab.Application.Common.Tenancy;
using Hishab.Domain.Common;
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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Identity's own mapping first, ours on top
        builder.ApplyConfigurationsFromAssembly(typeof(HishabDbContext).Assembly);
        ApplyTenantConventions(builder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

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
