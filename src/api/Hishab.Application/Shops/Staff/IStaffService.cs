using Hishab.Domain.Shops;

namespace Hishab.Application.Shops.Staff;

/// <summary>The current shop's team. Every call is scoped to the caller's own shop.</summary>
public interface IStaffService
{
    Task<IReadOnlyList<StaffMember>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a person by phone with a staff role. They join the shop the next time they sign
    /// in with OTP; until then they are listed as <see cref="StaffStatus.Invited"/>.
    /// </summary>
    /// <exception cref="Domain.Common.ConflictException">The phone already belongs to a shop.</exception>
    /// <exception cref="PlanLimitExceededException">The plan's user limit is reached.</exception>
    Task<StaffMember> InviteAsync(string phone, ShopRole role, CancellationToken cancellationToken);

    /// <exception cref="Domain.Common.NotFoundException">No such member in this shop.</exception>
    /// <exception cref="Domain.Common.ConflictException">The target is the owner.</exception>
    Task<StaffMember> ChangeRoleAsync(Guid userId, ShopRole role, CancellationToken cancellationToken);

    /// <summary>Removes the member and their sessions; the phone becomes free to sign up elsewhere.</summary>
    /// <exception cref="Domain.Common.NotFoundException">No such member in this shop.</exception>
    /// <exception cref="Domain.Common.ConflictException">The target is the owner.</exception>
    Task RemoveAsync(Guid userId, CancellationToken cancellationToken);
}

public enum StaffStatus
{
    /// <summary>Added by the owner; has not signed in yet.</summary>
    Invited,
    Active,
}

/// <summary>A team member. The phone is shown to the shop's owner only (the Owner-only staff endpoints); never logged.</summary>
public sealed record StaffMember(Guid Id, string Phone, ShopRole Role, StaffStatus Status, DateTimeOffset CreatedAt);
