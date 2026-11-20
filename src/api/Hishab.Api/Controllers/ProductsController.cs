using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Application.Products;
using Hishab.Domain.Common;
using Hishab.Domain.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Controllers;

public sealed class VariantRequest
{
    /// <summary>Stock-keeping unit; stored upper-case, unique among the shop's active variants.</summary>
    /// <example>POLO-M-RED</example>
    public required string Sku { get; init; }

    /// <example>M</example>
    public string? Size { get; init; }

    /// <example>Red</example>
    public string? Colour { get; init; }

    /// <summary>Selling price, BDT.</summary>
    /// <example>650</example>
    public required decimal Price { get; init; }

    /// <summary>What the shop paid, BDT. Owner only; omit it to keep the current cost.</summary>
    /// <example>400</example>
    public decimal? CostPrice { get; init; }

    internal VariantInput ToInput() => new(Sku, Size, Colour, Price, CostPrice);
}

public sealed class CreateProductRequest
{
    /// <example>Polo shirt</example>
    public required string Name { get; init; }

    /// <example>Cotton pique, regular fit</example>
    public string? Description { get; init; }

    /// <summary>At least one. A product without sizes or colours has one variant without them.</summary>
    public required IReadOnlyList<VariantRequest> Variants { get; init; }
}

public sealed class RenameProductRequest
{
    /// <example>Polo shirt</example>
    public required string Name { get; init; }

    public string? Description { get; init; }
}

public sealed class ProductListQuery
{
    /// <summary>Part of a product name or SKU, case-insensitive.</summary>
    /// <example>polo</example>
    public string? Search { get; init; }

    /// <example>1</example>
    public int Page { get; init; } = 1;

    /// <example>20</example>
    public int PageSize { get; init; } = 20;
}

public sealed class VariantRequestValidator : AbstractValidator<VariantRequest>
{
    public const decimal MaxPrice = 10_000_000m;

    public VariantRequestValidator()
    {
        RuleFor(v => v.Sku).NotEmpty().MaximumLength(ProductVariant.SkuMaxLength)
            .Must(s => !s.Trim().Any(char.IsWhiteSpace)).WithMessage("SKU cannot contain spaces.");
        RuleFor(v => v.Size).MaximumLength(ProductVariant.OptionMaxLength);
        RuleFor(v => v.Colour).MaximumLength(ProductVariant.OptionMaxLength);
        RuleFor(v => v.Price).InclusiveBetween(0m, MaxPrice).Must(Money.HasValidScale).WithMessage("Price can have at most 2 decimal places.");
        RuleFor(v => v.CostPrice!.Value).InclusiveBetween(0m, MaxPrice).Must(Money.HasValidScale).WithMessage("Cost price can have at most 2 decimal places.")
            .OverridePropertyName(nameof(VariantRequest.CostPrice))
            .When(v => v.CostPrice is not null);
    }
}

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public const int MaxVariants = 100;

    public CreateProductRequestValidator()
    {
        RuleFor(p => p.Name).NotEmpty().MaximumLength(Product.NameMaxLength);
        RuleFor(p => p.Description).MaximumLength(Product.DescriptionMaxLength);
        RuleFor(p => p.Variants).NotEmpty().WithMessage("Add at least one variant.")
            .Must(v => v.Count <= MaxVariants).WithMessage($"A product can have at most {MaxVariants} variants.");
        RuleForEach(p => p.Variants).SetValidator(new VariantRequestValidator());
    }
}

public sealed class RenameProductRequestValidator : AbstractValidator<RenameProductRequest>
{
    public RenameProductRequestValidator()
    {
        RuleFor(p => p.Name).NotEmpty().MaximumLength(Product.NameMaxLength);
        RuleFor(p => p.Description).MaximumLength(Product.DescriptionMaxLength);
    }
}

public sealed class ProductListQueryValidator : AbstractValidator<ProductListQuery>
{
    public ProductListQueryValidator()
    {
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

/// <summary>
/// The product catalogue. Every staff role can read it; Owner and Manager edit it. Cost prices
/// are returned and accepted only for the Owner (CanSeeProfit).
/// </summary>
[ApiController]
[Route("products")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class ProductsController(IProductService products) : ControllerBase
{
    /// <summary>Searches active products by name or SKU, ordered by name.</summary>
    [HttpGet]
    [ProducesResponseType<ProductPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductPage>> List([FromQuery] ProductListQuery query, CancellationToken cancellationToken) =>
        Ok(await products.ListAsync(query.Search, query.Page, query.PageSize, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await products.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.CanManageProducts)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var created = await products.CreateAsync(
            new ProductInput(request.Name, request.Description, [.. request.Variants.Select(v => v.ToInput())]),
            cancellationToken);
        return Created($"/api/products/{created.Id}", created);
    }

    /// <summary>Changes the name and description.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CanManageProducts)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> Rename(Guid id, RenameProductRequest request, CancellationToken cancellationToken) =>
        Ok(await products.RenameAsync(id, request.Name, request.Description, cancellationToken));

    /// <summary>Soft delete: the product leaves the catalogue; past orders keep it.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.CanManageProducts)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await products.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/variants")]
    [Authorize(Policy = Policies.CanManageProducts)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> AddVariant(Guid id, VariantRequest request, CancellationToken cancellationToken) =>
        Ok(await products.AddVariantAsync(id, request.ToInput(), cancellationToken));

    /// <summary>Changes a variant. Omit costPrice to keep the current cost.</summary>
    [HttpPut("{id:guid}/variants/{variantId:guid}")]
    [Authorize(Policy = Policies.CanManageProducts)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> UpdateVariant(Guid id, Guid variantId, VariantRequest request, CancellationToken cancellationToken) =>
        Ok(await products.UpdateVariantAsync(id, variantId, request.ToInput(), cancellationToken));

    /// <summary>Soft-deletes a variant. The last active variant cannot be removed (delete the product instead).</summary>
    [HttpDelete("{id:guid}/variants/{variantId:guid}")]
    [Authorize(Policy = Policies.CanManageProducts)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> RemoveVariant(Guid id, Guid variantId, CancellationToken cancellationToken) =>
        Ok(await products.RemoveVariantAsync(id, variantId, cancellationToken));
}
