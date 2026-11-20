using Hishab.Domain.Customers;
using Hishab.Infrastructure.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hishab.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        // The natural key: one customer per normalised phone per shop. The database enforces it,
        // so parallel order entries for a new phone cannot create two customers.
        builder.Property(c => c.Phone).HasMaxLength(16).IsPersonalData(PersonalDataKind.Phone);
        builder.HasIndex(c => new { c.TenantId, c.Phone }).IsUnique();

        builder.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength).IsPersonalData(PersonalDataKind.Text);
        builder.Property(c => c.Address).HasMaxLength(Customer.AddressMaxLength).IsPersonalData(PersonalDataKind.Text);
        builder.Property(c => c.Notes).HasMaxLength(Customer.NotesMaxLength).IsPersonalData(PersonalDataKind.Text);
        builder.Property(c => c.BlockedReason).HasMaxLength(Customer.BlockReasonMaxLength).IsPersonalData(PersonalDataKind.Text);

        builder.HasIndex(c => new { c.TenantId, c.CreatedAt }); // newest-first list
    }
}
