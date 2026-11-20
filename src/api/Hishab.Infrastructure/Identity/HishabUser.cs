using Hishab.Domain.Common;
using Hishab.Domain.Shops;
using Microsoft.AspNetCore.Identity;

namespace Hishab.Infrastructure.Identity;

/// <summary>
/// A person who signs in with their phone (UserName and PhoneNumber hold the E.164 phone).
/// One user belongs to one shop (ADR 0006), so the user row is tenant-owned and the tenant
/// filter applies to it like to any other shop data.
/// </summary>
public sealed class HishabUser : IdentityUser<Guid>, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }

    public ShopRole Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
