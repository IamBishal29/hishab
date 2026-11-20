using Hishab.Domain.Couriers;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hishab.Infrastructure.Persistence.Configurations;

internal sealed class CourierAccountConfiguration : IEntityTypeConfiguration<CourierAccount>
{
    public void Configure(EntityTypeBuilder<CourierAccount> builder)
    {
        builder.ToTable("courier_accounts");
        builder.Property(a => a.Provider).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(a => new { a.TenantId, a.Provider }).IsUnique(); // one connection per courier per shop

        // Ciphertext only (ADR 0007), and kept out of the audit log entirely: the hint is enough there.
        builder.Property(a => a.ProtectedCredentials).HasMaxLength(4_000).IsNotAudited();
        builder.Property(a => a.CredentialHint).HasMaxLength(16);

        // Deleting a shop deletes its courier keys with it.
        builder.HasOne<Shop>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
