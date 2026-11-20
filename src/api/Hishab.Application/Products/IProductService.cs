namespace Hishab.Application.Products;

/// <summary>The current shop's product catalogue. Cost prices are shown and accepted only for callers with SeeProfit.</summary>
public interface IProductService
{
    /// <summary>Active products whose name or a variant SKU contains <paramref name="search"/> (case-insensitive), ordered by name.</summary>
    Task<ProductPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    Task<ProductDto> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductDto> CreateAsync(ProductInput input, CancellationToken cancellationToken);

    Task<ProductDto> RenameAsync(Guid id, string name, string? description, CancellationToken cancellationToken);

    Task<ProductDto> AddVariantAsync(Guid id, VariantInput input, CancellationToken cancellationToken);

    /// <summary>A null <see cref="VariantInput.CostPrice"/> keeps the current cost.</summary>
    Task<ProductDto> UpdateVariantAsync(Guid id, Guid variantId, VariantInput input, CancellationToken cancellationToken);

    Task<ProductDto> RemoveVariantAsync(Guid id, Guid variantId, CancellationToken cancellationToken);

    /// <summary>Soft delete: hidden from the catalogue, kept for order history.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record ProductInput(string Name, string? Description, IReadOnlyList<VariantInput> Variants);

/// <summary>Prices in BDT.</summary>
public sealed record VariantInput(string Sku, string? Size, string? Colour, decimal Price, decimal? CostPrice);

public sealed record ProductDto(Guid Id, string Name, string? Description, DateTimeOffset CreatedAt, IReadOnlyList<VariantDto> Variants);

/// <summary><see cref="CostPrice"/> is null when unset or when the caller may not see costs.</summary>
public sealed record VariantDto(Guid Id, string Sku, string? Size, string? Colour, decimal Price, decimal? CostPrice);

public sealed record ProductPage(IReadOnlyList<ProductDto> Items, int Page, int PageSize, int TotalCount);
