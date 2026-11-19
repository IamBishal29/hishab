using Hishab.Domain.Common;
using Hishab.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Hishab.IntegrationTests.Tenancy;

/// <summary>
/// Reusable cross-tenant check for every tenant-owned entity: seed as tenant A, then prove
/// tenant B can neither read nor change the row, while tenant A still can.
/// </summary>
public static class CrossTenantAssert
{
    /// <param name="createContext">Creates a context whose tenant context is set to the given tenant.</param>
    /// <param name="newEntity">Creates a valid, unsaved entity (TenantId left empty).</param>
    public static async Task IsIsolatedAsync<TContext, TEntity>(
        Func<Guid, TContext> createContext,
        Func<TEntity> newEntity,
        CancellationToken cancellationToken)
        where TContext : DbContext
        where TEntity : class, ITenantOwned
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        object?[] key;
        TEntity seeded;
        await using (var db = createContext(tenantA))
        {
            seeded = newEntity();
            db.Add(seeded);
            await db.SaveChangesAsync(cancellationToken);
            seeded.TenantId.ShouldBe(tenantA, "SaveChanges should stamp the current tenant");

            var entry = db.Entry(seeded);
            key = [.. entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p.Name).CurrentValue)];
        }

        await using (var db = createContext(tenantB))
        {
            (await db.Set<TEntity>().AnyAsync(e => e.TenantId == tenantA, cancellationToken))
                .ShouldBeFalse($"tenant B must not list {typeof(TEntity).Name} rows of tenant A");
            (await db.Set<TEntity>().FindAsync(key, cancellationToken))
                .ShouldBeNull($"tenant B must not find tenant A's {typeof(TEntity).Name} by key");

            db.Remove(seeded);
            await Should.ThrowAsync<CrossTenantWriteException>(() => db.SaveChangesAsync(cancellationToken));
        }

        await using (var db = createContext(tenantA))
        {
            (await db.Set<TEntity>().FindAsync(key, cancellationToken))
                .ShouldNotBeNull("tenant A must still see its own row (guards against a vacuous pass)");
        }
    }
}
