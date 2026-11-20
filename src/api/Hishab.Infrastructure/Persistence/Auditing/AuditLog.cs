using System.Text.Json;
using Hishab.Application.Common.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Hishab.Infrastructure.Persistence.Auditing;

internal sealed class AuditLog(HishabDbContext db) : IAuditLog
{
    public async Task<AuditPage> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.AuditEntries.AsNoTracking(); // tenant filter: this shop only
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id) // stable order across pages
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(a => new AuditRecord(
            a.Id, a.UserId, a.EntityType, a.EntityId, a.Action,
            JsonDocument.Parse(a.Changes).RootElement.Clone(),
            a.CorrelationId, a.OccurredAt)).ToList();

        return new AuditPage(items, page, pageSize, total);
    }
}

/// <summary>Outside HTTP (migrations, tools, tests without a request): nobody is acting.</summary>
internal sealed class NoRequestContext : IRequestContext
{
    public Guid? UserId => null;

    public string? CorrelationId => null;
}
