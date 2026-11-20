namespace Hishab.Domain.Common;

/// <summary>
/// Marks an entity whose changes are recorded in the audit log automatically on save.
/// Which fields are personal (masked) or never recorded is set in the EF Core mapping.
/// </summary>
public interface IAuditable;
