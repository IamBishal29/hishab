using Hishab.Application.Common.Tenancy;
using Hishab.Application.Shops.Auth;
using Hishab.Domain.Common;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Identity;
using Hishab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure.Demo;

/// <summary>
/// Signs a visitor in as one of the people in <see cref="DemoScript.People"/>. Only demo phone numbers can
/// be reached this way, so it can never open a real seller's account even if the switch were on.
/// </summary>
internal sealed class DemoLoginService(
    HishabDbContext db,
    UserManager<HishabUser> users,
    ITenantContext tenant,
    IOptions<DemoSeedOptions> options) : IDemoLoginService
{
    public IReadOnlyList<ShopRole> AvailableRoles()
    {
        EnsureOn();
        return [.. DemoScript.People.Select(p => p.Role)];
    }

    public async Task<OtpLogin> SignInAsync(ShopRole role, CancellationToken cancellationToken)
    {
        EnsureOn();
        var person = DemoScript.People.FirstOrDefault(p => p.Role == role);
        if (person == default)
        {
            throw new NotFoundException("Demo person", role);
        }

        // Like the OTP login lookup: the tenant is unknown before sign-in (the 6th allowed cross-tenant query).
        var normalized = users.NormalizeName(person.Phone.Value);
        var user = await db.Users
            .IgnoreQueryFilters([HishabDbContext.TenantFilter])
            .SingleOrDefaultAsync(u => u.NormalizedUserName == normalized, cancellationToken)
            ?? throw new NotFoundException("Demo person", role); // not seeded yet, or removed by a visitor until the next reset

        tenant.SetTenant(user.TenantId);
        if (!user.PhoneNumberConfirmed)
        {
            user.PhoneNumberConfirmed = true; // an invited staff member's first sign-in, as with an OTP
            await db.SaveChangesAsync(cancellationToken);
        }

        // The user's current role: a visitor may have changed it, and the token must match the database.
        return new OtpLogin(user.Id, user.TenantId, user.Role, IsNewShop: false);
    }

    private void EnsureOn()
    {
        if (!options.Value.Login)
        {
            throw new NotFoundException("Demo sign-in", "off");
        }
    }
}
