using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Api.RateLimiting;
using Hishab.Application.Orders.Drafts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hishab.Api.Controllers;

public sealed class DraftFromChatRequest
{
    public const int ChatMaxLength = 4_000;

    /// <summary>The conversation with the buyer, pasted as text (Bangla, English or both).</summary>
    /// <example>Apa ami lal saree ta 2 ta nibo. Rahima, House 4 Road 2 Mirpur 10 Dhaka. 01712345678</example>
    public required string Chat { get; init; }
}

public sealed class DraftFromChatRequestValidator : AbstractValidator<DraftFromChatRequest>
{
    public DraftFromChatRequestValidator()
    {
        RuleFor(r => r.Chat).NotEmpty().WithMessage("Paste the conversation with the buyer.")
            .MaximumLength(DraftFromChatRequest.ChatMaxLength)
            .WithMessage($"Paste at most {DraftFromChatRequest.ChatMaxLength} characters: the part with the order.");
    }
}

[ApiController]
[Route("orders/drafts")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class OrderDraftsController(IOrderDraftService drafts) : ControllerBase
{
    /// <summary>
    /// Reads a pasted buyer chat and suggests the quick order form's fields. Nothing is saved: the person
    /// taking the order reviews every field and creates the order with POST /api/orders. The phone is always
    /// one found in the chat and every matched item is one of this shop's products; check the warnings.
    /// </summary>
    [HttpPost("from-chat")]
    [Authorize(Policy = Policies.CanEnterOrders)]
    [EnableRateLimiting(RateLimitingSetup.ChatDraftPolicy)]
    [ProducesResponseType<OrderDraft>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<OrderDraft> FromChat(DraftFromChatRequest request, CancellationToken cancellationToken) =>
        drafts.DraftFromChatAsync(request.Chat, cancellationToken);
}
