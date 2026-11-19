namespace Hishab.Infrastructure.Persistence.Tenancy;

/// <summary>
/// Thrown when a save would write a tenant-owned row without a current tenant, or for a
/// tenant other than the current one. Indicates a bug, so it is never mapped to a 4xx.
/// </summary>
public sealed class CrossTenantWriteException(string message) : InvalidOperationException(message);
