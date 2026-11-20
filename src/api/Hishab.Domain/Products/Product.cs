using Hishab.Domain.Common;

namespace Hishab.Domain.Products;

/// <summary>
/// A catalogue product and its variants: the aggregate. Variants change only through the
/// product, so the rules that span variants (unique SKUs, at least one active variant) are
/// checked in one place. A product without sizes or colours has one default variant.
/// Deleting is soft: orders keep pointing at the rows they were made from.
/// </summary>
public sealed class Product : ITenantOwned, ISoftDeletable, IAuditable
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2_000;

    private readonly List<ProductVariant> _variants = [];

    private Product()
    {
        Name = null!;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>All variants loaded with the product; the soft-delete filter hides deleted ones in queries.</summary>
    public IReadOnlyList<ProductVariant> Variants => _variants;

    public IEnumerable<ProductVariant> ActiveVariants => _variants.Where(v => v.DeletedAt is null);

    /// <exception cref="DomainValidationException">A rule is broken; errors are keyed by field.</exception>
    public static Product Create(string name, string? description, IReadOnlyList<VariantDetails> variants, DateTimeOffset now)
    {
        if (variants.Count == 0)
        {
            throw new DomainValidationException("variants", "Add at least one variant (a product without sizes or colours has one).");
        }

        var product = new Product { Id = Guid.NewGuid(), CreatedAt = now };
        product.Rename(name, description);
        for (var i = 0; i < variants.Count; i++)
        {
            product.AddVariant(variants[i], $"variants[{i}]");
        }

        return product;
    }

    public void Rename(string name, string? description)
    {
        EnsureNotDeleted();
        var errors = new Dictionary<string, string[]>();
        var trimmedName = name?.Trim() ?? string.Empty;
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (trimmedName.Length is 0 or > NameMaxLength)
        {
            errors["name"] = [$"Product name is required and at most {NameMaxLength} characters."];
        }

        if (description?.Length > DescriptionMaxLength)
        {
            errors["description"] = [$"Description is at most {DescriptionMaxLength} characters."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        Name = trimmedName;
        Description = description;
    }

    public ProductVariant AddVariant(VariantDetails details, string field = "")
    {
        EnsureNotDeleted();
        var variant = ProductVariant.Create(Id, details, field);
        EnsureSkuIsFree(variant.Sku, except: null, field);
        _variants.Add(variant);
        return variant;
    }

    /// <summary>Changes a variant. <paramref name="costPrice"/> null keeps the current cost (callers that cannot see cost never send it).</summary>
    public ProductVariant UpdateVariant(Guid variantId, VariantDetails details)
    {
        EnsureNotDeleted();
        var variant = FindActive(variantId);
        var sku = ProductVariant.NormalizeSku(details.Sku);
        EnsureSkuIsFree(sku, except: variant, field: "");
        variant.Update(details);
        return variant;
    }

    public void RemoveVariant(Guid variantId, DateTimeOffset now)
    {
        EnsureNotDeleted();
        var variant = FindActive(variantId);
        if (ActiveVariants.Count() == 1)
        {
            throw new ConflictException("A product needs at least one variant. Delete the product instead.");
        }

        variant.Delete(now);
    }

    /// <summary>Soft-deletes the product and its variants, so their SKUs can be used again.</summary>
    public void Delete(DateTimeOffset now)
    {
        if (DeletedAt is not null)
        {
            return;
        }

        DeletedAt = now;
        foreach (var variant in ActiveVariants.ToList())
        {
            variant.Delete(now);
        }
    }

    private ProductVariant FindActive(Guid variantId) =>
        ActiveVariants.SingleOrDefault(v => v.Id == variantId) ?? throw new NotFoundException("Variant", variantId);

    private void EnsureSkuIsFree(string sku, ProductVariant? except, string field)
    {
        if (ActiveVariants.Any(v => v != except && v.Sku == sku))
        {
            throw new DomainValidationException(ProductVariant.Key(field, "sku"), $"SKU '{sku}' is already used by another variant of this product.");
        }
    }

    private void EnsureNotDeleted()
    {
        if (DeletedAt is not null)
        {
            throw new ConflictException("This product was deleted.");
        }
    }
}

/// <summary>The editable values of a variant. Prices in BDT; a null cost means "not set" (or "unchanged" on update).</summary>
public sealed record VariantDetails(string Sku, string? Size, string? Colour, Money Price, Money? CostPrice);

/// <summary>One sellable version of a product (size/colour), with its own SKU and prices.</summary>
public sealed class ProductVariant : ITenantOwned, ISoftDeletable, IAuditable
{
    public const int SkuMaxLength = 64;
    public const int OptionMaxLength = 50;

    private ProductVariant()
    {
        Sku = null!;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; set; }

    public Guid ProductId { get; private set; }

    /// <summary>Stock-keeping unit, trimmed and upper-case; unique among a shop's active variants.</summary>
    public string Sku { get; private set; }

    public string? Size { get; private set; }

    public string? Colour { get; private set; }

    /// <summary>Selling price.</summary>
    public Money Price { get; private set; }

    /// <summary>What the shop paid. Owner-only (CanSeeProfit); null until set.</summary>
    public Money? CostPrice { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Selling price minus cost, when the cost is known. Negative means selling at a loss (allowed, e.g. clearance).</summary>
    public Money? Margin => CostPrice is { } cost ? Price - cost : null;

    public static string NormalizeSku(string? sku) => (sku ?? string.Empty).Trim().ToUpperInvariant();

    internal static ProductVariant Create(Guid productId, VariantDetails details, string field)
    {
        var variant = new ProductVariant { Id = Guid.NewGuid(), ProductId = productId };
        variant.Apply(details, field, keepCostWhenNull: false);
        return variant;
    }

    internal void Update(VariantDetails details) => Apply(details, field: "", keepCostWhenNull: true);

    /// <summary>Error key for a variant field: "sku" for single-variant calls, "variants[0].sku" inside a product create.</summary>
    internal static string Key(string field, string name) => field.Length == 0 ? name : $"{field}.{name}";

    internal void Delete(DateTimeOffset now) => DeletedAt ??= now;

    private void Apply(VariantDetails details, string field, bool keepCostWhenNull)
    {
        var errors = new Dictionary<string, string[]>();
        var sku = NormalizeSku(details.Sku);
        var size = Clean(details.Size);
        var colour = Clean(details.Colour);

        if (sku.Length is 0 or > SkuMaxLength || sku.Any(char.IsWhiteSpace))
        {
            errors[Key(field, "sku")] = [$"SKU is required, at most {SkuMaxLength} characters, without spaces."];
        }

        if (size?.Length > OptionMaxLength || colour?.Length > OptionMaxLength)
        {
            errors[Key(field, "size")] = [$"Size and colour are at most {OptionMaxLength} characters."];
        }

        if (details.Price.IsNegative)
        {
            errors[Key(field, "price")] = ["Price cannot be negative."];
        }

        if (details.CostPrice is { IsNegative: true })
        {
            errors[Key(field, "costPrice")] = ["Cost price cannot be negative."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        Sku = sku;
        Size = size;
        Colour = colour;
        Price = details.Price;
        if (details.CostPrice is not null || !keepCostWhenNull)
        {
            CostPrice = details.CostPrice;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
