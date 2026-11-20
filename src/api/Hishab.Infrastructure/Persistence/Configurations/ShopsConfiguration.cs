using Hishab.Domain.Common;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Identity;
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
        builder.HasOne<Shop>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
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
