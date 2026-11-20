namespace Hishab.Domain.Common;

/// <summary>
/// Rows are never physically deleted: <see cref="DeletedAt"/> is set instead, and a named
/// query filter hides them. History (orders, audit) keeps pointing at real rows.
/// </summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }
}
