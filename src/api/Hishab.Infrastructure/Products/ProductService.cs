using Hishab.Application.Common.Auditing;
using Hishab.Application.Products;
using Hishab.Domain.Common;
using Hishab.Domain.Products;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hishab.Infrastructure.Products;

internal sealed class ProductService(HishabDbContext db, IRequestContext request, TimeProvider clock) : IProductService
{
    public const string CostForbiddenMessage = "Only the owner can set cost prices.";

    private bool CanSeeCost => request.Has(Permission.SeeProfit);

    public async Task<ProductPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking(); // tenant + soft-delete filters apply
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern, @"\")
                || p.Variants.Any(v => EF.Functions.ILike(v.Sku, pattern, @"\")));
        }

        var total = await query.CountAsync(cancellationToken);
        var products = await query
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(p => p.Variants)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new ProductPage([.. products.Select(ToDto)], page, pageSize, total);
    }

    public async Task<ProductDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToDto(await LoadAsync(id, cancellationToken));

    public async Task<ProductDto> CreateAsync(ProductInput input, CancellationToken cancellationToken)
    {
        EnsureMayWriteCost(input.Variants);
        var variants = input.Variants.Select((v, i) => ToDetails(v, $"variants[{i}]")).ToList();
        var product = Product.Create(input.Name, input.Description, variants, clock.GetUtcNow());

        await EnsureSkusFreeInShopAsync(product.Variants.Select(v => v.Sku), product.Id, cancellationToken);
        db.Products.Add(product);
        await SaveAsync(cancellationToken);
        return ToDto(product);
    }

    public Task<ProductDto> RenameAsync(Guid id, string name, string? description, CancellationToken cancellationToken) =>
        ChangeAsync(id, product => product.Rename(name, description), cancellationToken);

    public async Task<ProductDto> AddVariantAsync(Guid id, VariantInput input, CancellationToken cancellationToken)
    {
        EnsureMayWriteCost([input]);
        var details = ToDetails(input, field: "");
        await EnsureSkusFreeInShopAsync([ProductVariant.NormalizeSku(details.Sku)], id, cancellationToken);
        return await ChangeAsync(id, product => product.AddVariant(details), cancellationToken);
    }

    public async Task<ProductDto> UpdateVariantAsync(Guid id, Guid variantId, VariantInput input, CancellationToken cancellationToken)
    {
        EnsureMayWriteCost([input]);
        var details = ToDetails(input, field: "");
        await EnsureSkusFreeInShopAsync([ProductVariant.NormalizeSku(details.Sku)], id, cancellationToken);
        return await ChangeAsync(id, product => product.UpdateVariant(variantId, details), cancellationToken);
    }

    public Task<ProductDto> RemoveVariantAsync(Guid id, Guid variantId, CancellationToken cancellationToken) =>
        ChangeAsync(id, product => product.RemoveVariant(variantId, clock.GetUtcNow()), cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(id, cancellationToken);
        product.Delete(clock.GetUtcNow());
        await SaveAsync(cancellationToken);
    }

    private async Task<ProductDto> ChangeAsync(Guid id, Action<Product> change, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(id, cancellationToken);
        change(product);
        await SaveAsync(cancellationToken);
        return ToDto(product);
    }

    private async Task<Product> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Products.Include(p => p.Variants).SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product", id);

    /// <summary>Field-level authorization on writes: without SeeProfit a cost may not be sent at all (omitting it keeps the current one).</summary>
    private void EnsureMayWriteCost(IEnumerable<VariantInput> variants)
    {
        if (!CanSeeCost && variants.Any(v => v.CostPrice is not null))
        {
            throw new ForbiddenException(CostForbiddenMessage);
        }
    }

    /// <summary>A friendly 409 before saving; the partial unique index is the guarantee under concurrency.</summary>
    private async Task EnsureSkusFreeInShopAsync(IEnumerable<string> skus, Guid ownProductId, CancellationToken cancellationToken)
    {
        var wanted = skus.ToList();
        var taken = await db.Products
            .Where(p => p.Id != ownProductId)
            .SelectMany(p => p.Variants)
            .Where(v => wanted.Contains(v.Sku))
            .Select(v => v.Sku)
            .FirstOrDefaultAsync(cancellationToken);

        if (taken is not null)
        {
            throw new ConflictException($"SKU '{taken}' is already used by another product.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("That SKU is already used by another product."); // a parallel request took it
        }
    }

    private static VariantDetails ToDetails(VariantInput v, string field) => new(
        v.Sku,
        v.Size,
        v.Colour,
        Money.Bdt(v.Price, Key(field, "price")),
        v.CostPrice is { } cost ? Money.Bdt(cost, Key(field, "costPrice")) : null);

    private static string Key(string field, string name) => field.Length == 0 ? name : $"{field}.{name}";

    /// <summary>A search for "50%" must match the text "50%", not "50 anything".</summary>
    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    /// <summary>Field-level authorization on reads: cost prices only for callers with SeeProfit.</summary>
    private ProductDto ToDto(Product p) => new(
        p.Id,
        p.Name,
        p.Description,
        p.CreatedAt,
        [.. p.ActiveVariants.Select(v => new VariantDto(
            v.Id, v.Sku, v.Size, v.Colour, v.Price.Amount, CanSeeCost ? v.CostPrice?.Amount : null))]);
}
