using Hishab.Domain.Common;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Identity;
using Hishab.Infrastructure.Persistence.Auditing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hishab.Infrastructure.Persistence.Configurations;

internal sealed class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    public void Configure(EntityTypeBuilder<Shop> builder)
    {
        builder.ToTable("shops");
        builder.Property(s => s.Name).HasMaxLength(Shop.NameMaxLength);
        builder.Property(s => s.LogoUrl).HasMaxLength(Shop.LogoUrlMaxLength);
        builder.Property(s => s.PickupAddress).HasMaxLength(Shop.PickupAddressMaxLength).IsPersonalData(PersonalDataKind.Text);
        builder.Property(s => s.DefaultCourier).HasConversion<string>().HasMaxLength(16);
    }
}

internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges");
        builder.Property(c => c.Phone)
            .HasConversion(p => p.Value, v => Phone.Parse(v, "phone"))
            .HasMaxLength(16);
        builder.Property(c => c.CodeHash).HasMaxLength(64);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);

        // Two simultaneous verifies of the same code: the second save fails instead of
        // creating a second shop. Npgsql maps a uint row version to PostgreSQL's xmin.
        builder.Property<uint>("Version").IsRowVersion();

        // Serves "latest pending challenge for this phone" and the per-phone send limit.
        builder.HasIndex(c => new { c.Phone, c.CreatedAt });
    }
}

internal sealed class HishabUserConfiguration : IEntityTypeConfiguration<HishabUser>
{
    public void Configure(EntityTypeBuilder<HishabUser> builder)
    {
        builder.ToTable("users");
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(16);

        // Audit: the phone lives in UserName and PhoneNumber; normalized copies add nothing.
        builder.Property(u => u.UserName).IsPersonalData(PersonalDataKind.Phone);
        builder.Property(u => u.PhoneNumber).IsPersonalData(PersonalDataKind.Phone);
        builder.Property(u => u.Email).IsPersonalData(PersonalDataKind.Text);
        builder.Property(u => u.NormalizedUserName).IsNotAudited();
        builder.Property(u => u.NormalizedEmail).IsNotAudited();
        builder.HasOne<Shop>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique(); // the refresh lookup
        builder.HasIndex(t => t.FamilyId); // revoking a family
        builder.HasOne<HishabUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);

        // Two parallel refreshes with the same token: only one may redeem it, or the family forks.
        builder.Property<uint>("Version").IsRowVersion();
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.Property(a => a.EntityType).HasMaxLength(AuditEntry.EntityTypeMaxLength);
        builder.Property(a => a.EntityId).HasMaxLength(AuditEntry.EntityIdMaxLength);
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Changes).HasColumnType("jsonb");
        builder.Property(a => a.CorrelationId).HasMaxLength(AuditEntry.CorrelationIdMaxLength);
        builder.Property(a => a.Sequence).UseIdentityAlwaysColumn(); // assigned by PostgreSQL, never by the app
        builder.HasIndex(a => new { a.TenantId, a.Sequence }); // newest-first paging per shop (and keyset paging later)
    }
}

internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("user_claims");
}

internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("user_logins");
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("user_tokens");
}
