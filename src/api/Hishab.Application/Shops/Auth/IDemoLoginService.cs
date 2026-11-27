using Hishab.Domain.Shops;

namespace Hishab.Application.Shops.Auth;

/// <summary>
/// One-click sign-in to the seeded demo shop (<c>Demo:Login</c>), because a hosted demo has no SMS gateway.
/// Both members throw <see cref="Domain.Common.NotFoundException"/> while the switch is off, so the endpoint
/// looks the same as one that does not exist.
/// </summary>
public interface IDemoLoginService
{
    /// <summary>The roles a visitor can try, owner first.</summary>
    IReadOnlyList<ShopRole> AvailableRoles();

    /// <summary>Signs in as the demo shop's person with this role, as a verified OTP would.</summary>
    Task<OtpLogin> SignInAsync(ShopRole role, CancellationToken cancellationToken);
}
