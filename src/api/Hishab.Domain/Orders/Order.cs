using Hishab.Domain.Common;

namespace Hishab.Domain.Orders;

/// <summary>
/// An order and its lines and status history: one aggregate. Lines snapshot the product at
/// order time, so editing the catalogue never changes past orders. The status changes only
/// through <see cref="TransitionTo"/>, which enforces the lifecycle and records who moved it.
/// </summary>
public sealed class Order : ITenantOwned, IAuditable
{
    public const int AddressMaxLength = 300;
    public const int NoteMaxLength = 500;
    public const int MaxLines = 100;

    private readonly List<OrderLine> _lines = [];
    private readonly List<OrderStatusChange> _history = [];

    private Order()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; set; }

    public Guid CustomerId { get; private set; }

    /// <summary>The address this parcel goes to, copied at order time (the customer's address may change later).</summary>
    public string? DeliveryAddress { get; private set; }

    /// <summary>Picks the delivery charge rule (shop settings) and, later, the courier zone.</summary>
    public bool IsInsideDhaka { get; private set; }

    public OrderSource Source { get; private set; }

    public OrderStatus Status { get; private set; }

    public Money DeliveryCharge { get; private set; }

    /// <summary>Paid before delivery (bKash, Nagad...), a common way to cut fake orders.</summary>
    public Money AdvancePaid { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    /// <summary>Every status the order has been in, oldest first, starting with its creation.</summary>
    public IReadOnlyList<OrderStatusChange> History => _history;

    public Money Subtotal => _lines.Aggregate(Money.Zero, (sum, line) => sum + line.LineTotal);

    public Money Total => Subtotal + DeliveryCharge;

    /// <summary>Cash on delivery: what the courier must collect from the buyer.</summary>
    public Money CodAmount => Total - AdvancePaid;

    /// <exception cref="DomainValidationException">A rule is broken; errors keyed by field.</exception>
    public static Order Create(
        Guid customerId,
        string? deliveryAddress,
        bool isInsideDhaka,
        OrderSource source,
        IReadOnlyList<OrderLineDetails> lines,
        Money deliveryCharge,
        Money advancePaid,
        string? note,
        Guid? createdBy,
        DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        deliveryAddress = Clean(deliveryAddress);
        note = Clean(note);

        if (customerId == Guid.Empty)
        {
            errors["customerId"] = ["An order needs a customer."];
        }

        if (lines.Count is 0 or > MaxLines)
        {
            errors["lines"] = [$"An order needs 1 to {MaxLines} lines."];
        }

        if (deliveryAddress?.Length > AddressMaxLength)
        {
            errors["deliveryAddress"] = [$"Address is at most {AddressMaxLength} characters."];
        }

        if (note?.Length > NoteMaxLength)
        {
            errors["note"] = [$"Note is at most {NoteMaxLength} characters."];
        }

        if (deliveryCharge.IsNegative)
        {
            errors["deliveryCharge"] = ["Delivery charge cannot be negative."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            DeliveryAddress = deliveryAddress,
            IsInsideDhaka = isInsideDhaka,
            Source = source,
            DeliveryCharge = deliveryCharge,
            AdvancePaid = Money.Zero,
            Note = note,
            CreatedAt = now,
            Status = OrderStatus.New,
        };

        for (var i = 0; i < lines.Count; i++)
        {
            order._lines.Add(OrderLine.Create(lines[i], $"lines[{i}]"));
        }

        order.RecordAdvance(advancePaid);
        order._history.Add(new OrderStatusChange(null, OrderStatus.New, createdBy, now, note: null));
        return order;
    }

    /// <summary>Sets the amount paid in advance; it can never exceed the order total.</summary>
    public void RecordAdvance(Money advancePaid)
    {
        if (advancePaid.IsNegative || advancePaid > Total)
        {
            throw new DomainValidationException("advancePaid", $"Advance must be between 0 and the order total ({Total}).");
        }

        AdvancePaid = advancePaid;
    }

    /// <summary>
    /// Moves the order along its lifecycle and appends the change to the history.
    /// </summary>
    /// <exception cref="ConflictException">The lifecycle does not allow this move (HTTP 409); nothing changes.</exception>
    public void TransitionTo(OrderStatus to, Guid? by, DateTimeOffset now, string? note = null)
    {
        if (!OrderLifecycle.CanMove(Status, to))
        {
            throw new ConflictException($"An order cannot move from {Status} to {to}.");
        }

        note = Clean(note);
        if (note?.Length > NoteMaxLength)
        {
            throw new DomainValidationException("note", $"Note is at most {NoteMaxLength} characters.");
        }

        _history.Add(new OrderStatusChange(Status, to, by, now, note));
        Status = to;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>What a line is made of at order time: a snapshot of the catalogue, not a live link.</summary>
public sealed record OrderLineDetails(
    Guid? ProductVariantId,
    string ProductName,
    string? VariantLabel,
    string? Sku,
    Money UnitPrice,
    Money? UnitCost,
    int Quantity);

/// <summary>
/// One line of an order. Name, label, SKU, price and cost are copied when the order is placed:
/// a later price change or product deletion never rewrites this order's totals or profit.
/// </summary>
public sealed class OrderLine
{
    public const int NameMaxLength = 200;
    public const int LabelMaxLength = 110;
    public const int MaxQuantity = 1_000;

    private OrderLine() => ProductName = null!;

    public Guid Id { get; private set; }

    /// <summary>The catalogue variant it came from, if any (soft-deleted variants stay resolvable).</summary>
    public Guid? ProductVariantId { get; private set; }

    public string ProductName { get; private set; }

    /// <summary>"M / Red"; null for products without options.</summary>
    public string? VariantLabel { get; private set; }

    public string? Sku { get; private set; }

    public Money UnitPrice { get; private set; }

    /// <summary>Cost at order time, for profit (Owner only); null when the cost was unknown.</summary>
    public Money? UnitCost { get; private set; }

    public int Quantity { get; private set; }

    public Money LineTotal => Money.Bdt(UnitPrice.Amount * Quantity);

    internal static OrderLine Create(OrderLineDetails d, string field)
    {
        var errors = new Dictionary<string, string[]>();
        var name = d.ProductName?.Trim() ?? string.Empty;

        if (name.Length is 0 or > NameMaxLength)
        {
            errors[$"{field}.productName"] = [$"Product name is required and at most {NameMaxLength} characters."];
        }

        if (d.VariantLabel?.Length > LabelMaxLength)
        {
            errors[$"{field}.variantLabel"] = [$"Variant label is at most {LabelMaxLength} characters."];
        }

        if (d.Quantity is < 1 or > MaxQuantity)
        {
            errors[$"{field}.quantity"] = [$"Quantity must be 1 to {MaxQuantity}."];
        }

        if (d.UnitPrice.IsNegative)
        {
            errors[$"{field}.unitPrice"] = ["Price cannot be negative."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        return new OrderLine
        {
            Id = Guid.NewGuid(),
            ProductVariantId = d.ProductVariantId,
            ProductName = name,
            VariantLabel = d.VariantLabel,
            Sku = d.Sku,
            UnitPrice = d.UnitPrice,
            UnitCost = d.UnitCost,
            Quantity = d.Quantity,
        };
    }
}

/// <summary>One step in the order's history: from, to, who and when. Append-only.</summary>
public sealed class OrderStatusChange
{
    private OrderStatusChange()
    {
    }

    internal OrderStatusChange(OrderStatus? from, OrderStatus to, Guid? changedBy, DateTimeOffset changedAt, string? note)
    {
        Id = Guid.NewGuid();
        From = from;
        To = to;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
        Note = note;
    }

    public Guid Id { get; private set; }

    /// <summary>Null for the creation step.</summary>
    public OrderStatus? From { get; private set; }

    public OrderStatus To { get; private set; }

    /// <summary>The user who made the change; null for system work (e.g. courier status sync).</summary>
    public Guid? ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public string? Note { get; private set; }
}
