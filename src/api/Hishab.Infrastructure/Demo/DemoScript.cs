using Hishab.Application.Products;
using Hishab.Domain.Common;
using Hishab.Domain.Orders;
using Hishab.Domain.Shops;

namespace Hishab.Infrastructure.Demo;

/// <summary>
/// The fictional demo shop: who works there, what it sells, who buys, and what happened to each
/// order over the last four weeks. Pure data; <see cref="Timeline"/> turns it into dated steps.
/// Every phone number comes from <see cref="Phone.DemoBlock"/>, so none can reach a real person.
/// </summary>
internal static class DemoScript
{
    public const string ShopName = "Nodi Boutique (Demo)";
    public const string PickupAddress = "House 12, Road 5, Mirpur 10, Dhaka";
    public const string CourierApiKey = "demo-fake-courier-key";
    public const decimal ReturnCharge = 60m;

    /// <summary>Sign in to the demo shop with this number (development OTP codes are logged).</summary>
    public static Phone OwnerPhone { get; } = Phone.Demo(1);

    public static IReadOnlyList<(Phone Phone, ShopRole Role)> Staff { get; } =
    [
        (Phone.Demo(2), ShopRole.Manager),
        (Phone.Demo(3), ShopRole.Moderator),
        (Phone.Demo(4), ShopRole.Packer),
    ];

    /// <summary>Who the one-click demo sign-in can be: the owner and each seeded staff member.</summary>
    public static IReadOnlyList<(Phone Phone, ShopRole Role)> People { get; } = [(OwnerPhone, ShopRole.Owner), .. Staff];

    public static IReadOnlyList<ProductInput> Products { get; } =
    [
        new("Polo shirt", "Cotton pique, regular fit",
        [
            new("POLO-NV-M", "M", "Navy", 650m, 380m),
            new("POLO-NV-L", "L", "Navy", 650m, 380m),
            new("POLO-WH-L", "L", "White", 650m, 380m),
        ]),
        new("Cotton three-piece", "Unstitched, block print",
        [
            new("3PC-RED", null, "Red", 1850m, 1150m),
            new("3PC-BLU", null, "Blue", 1850m, 1150m),
        ]),
        new("Jamdani saree", "Handloom, half silk", [new("SAREE-JAM", null, "Off-white", 4200m, 2900m)]),
        new("Kids kurti", null,
        [
            new("KURTI-K-S", "S", "Yellow", 750m, 420m),
            new("KURTI-K-M", "M", "Yellow", 750m, 420m),
        ]),
        new("Leather sandal", null,
        [
            new("SANDAL-40", "40", "Brown", 1250m, 760m),
            new("SANDAL-42", "42", "Brown", 1250m, 760m),
        ]),
        new("Georgette hijab", null,
        [
            new("HIJAB-BLK", null, "Black", 350m, 160m),
            new("HIJAB-MRN", null, "Maroon", 350m, 160m),
        ]),
    ];

    public static IReadOnlyList<DemoCustomer> Customers { get; } =
    [
        new(1, "Rahima Begum", "House 4, Road 2, Mirpur 10, Dhaka", true),
        new(2, "Nusrat Jahan", "Flat 3B, Road 27, Dhanmondi, Dhaka", true),
        new(3, "Farhana Akter", "Agrabad C/A, Chattogram", false),
        new(4, "Tanvir Ahmed", "Sector 7, Uttara, Dhaka", true),
        new(5, "সুমাইয়া ইসলাম", "Zindabazar, Sylhet", false),
        new(6, "Mehedi Hasan", "Block C, Mohammadpur, Dhaka", true),
        new(7, "Sharmin Sultana", "Shibbari, Khulna", false), // returns most parcels: shows the Risky badge
        new(8, "Arif Hossain", "Road 11, Banani, Dhaka", true),
        new(9, "নাসরিন আক্তার", "Shaheb Bazar, Rajshahi", false),
        new(10, "Rakib Hasan", "Block D, Bashundhara R/A, Dhaka", true),
        new(11, "Jannatul Ferdous", "Section 2, Mirpur, Dhaka", true),
        new(12, "Shuvo Roy", "Kandirpar, Cumilla", false),
    ];

    private const OrderSource Fb = OrderSource.Facebook;
    private const OrderSource Ig = OrderSource.Instagram;
    private const OrderSource Wa = OrderSource.WhatsApp;

    /// <summary>Every status appears; settled orders are old enough to be on a weekly payout.</summary>
    public static IReadOnlyList<DemoOrder> Orders { get; } =
    [
        new(1, 1, 27, Fb, [("POLO-NV-L", 2)], OrderStatus.CodSettled),
        new(2, 2, 26, Ig, [("3PC-RED", 1)], OrderStatus.CodSettled),
        new(3, 3, 25, Fb, [("SAREE-JAM", 1)], OrderStatus.CodSettled),
        new(4, 7, 24, Fb, [("SANDAL-42", 1)], OrderStatus.ReceivedBack),
        new(5, 4, 23, Wa, [("KURTI-K-M", 2)], OrderStatus.CodSettled),
        new(6, 5, 22, Fb, [("HIJAB-BLK", 3)], OrderStatus.Cancelled, Note: "Buyer changed her mind"),
        new(7, 1, 20, Fb, [("3PC-BLU", 1), ("HIJAB-MRN", 1)], OrderStatus.CodSettled),
        new(8, 6, 19, OrderSource.Phone, [("POLO-WH-L", 1)], OrderStatus.CodSettled),
        new(9, 7, 18, Ig, [("3PC-RED", 1)], OrderStatus.ReceivedBack),
        new(10, 8, 17, Fb, [("SANDAL-40", 1)], OrderStatus.CodSettled, ShortPaidBy: 50m),
        new(11, 9, 16, Fb, [("SAREE-JAM", 1)], OrderStatus.CodSettled, Advance: 500m),
        new(12, 10, 15, Ig, [("KURTI-K-S", 1), ("KURTI-K-M", 1)], OrderStatus.CodSettled),
        new(13, 2, 13, Fb, [("POLO-NV-M", 2)], OrderStatus.CodSettled),
        new(14, 7, 12, Fb, [("HIJAB-MRN", 2)], OrderStatus.ReceivedBack),
        new(15, 11, 11, Wa, [("3PC-BLU", 1)], OrderStatus.CodSettled),
        new(16, 3, 9, Fb, [("POLO-NV-L", 1)], OrderStatus.Delivered),
        new(17, 12, 8, Ig, [("SANDAL-42", 1)], OrderStatus.ReceivedBack),
        new(18, 7, 7, Fb, [("POLO-WH-L", 1)], OrderStatus.Delivered),
        new(19, 4, 6, Fb, [("3PC-RED", 1)], OrderStatus.Delivered),
        new(20, 5, 5, Ig, [("HIJAB-BLK", 2)], OrderStatus.Returned),
        new(21, 8, 4, Fb, [("KURTI-K-M", 1)], OrderStatus.Delivered),
        new(22, 9, 3, Wa, [("SAREE-JAM", 1)], OrderStatus.InTransit),
        new(23, 10, 2, Fb, [("POLO-NV-M", 1), ("HIJAB-BLK", 1)], OrderStatus.InTransit),
        new(24, 1, 1, Fb, [("3PC-BLU", 2)], OrderStatus.Booked),
        new(25, 11, 1, Ig, [("SANDAL-40", 1)], OrderStatus.Booked),
        new(26, 6, 1, OrderSource.Phone, [("KURTI-K-S", 2)], OrderStatus.Confirmed),
        new(27, 12, 0, Fb, [("POLO-NV-L", 1)], OrderStatus.Confirmed),
        new(28, 2, 0, Ig, [("HIJAB-MRN", 1)], OrderStatus.New, Note: "Gift wrap, please"),
        new(29, 3, 0, Fb, [("SAREE-JAM", 1)], OrderStatus.New),
        new(30, 5, 0, Wa, [("3PC-RED", 1)], OrderStatus.New),
    ];

    /// <summary>The courier pays cash-on-delivery money weekly, this many days ago.</summary>
    private static readonly int[] _payoutDaysAgo = [21, 14, 7];

    public static DemoCustomer CustomerOf(DemoOrder order) => Customers.Single(c => c.Index == order.Customer);

    /// <summary>All steps in time order, every one in the past relative to <paramref name="now"/>.</summary>
    public static IReadOnlyList<DemoEvent> Timeline(DateTimeOffset now)
    {
        var today = DhakaTime.DayOf(now);
        var events = new List<DemoEvent>();
        var deliveries = new List<(DemoOrder Order, DateTimeOffset At)>();

        foreach (var order in Orders)
        {
            var inside = CustomerOf(order).IsInsideDhaka;
            var created = order.DaysAgo == 0
                ? now.AddHours(-3).AddMinutes((order.No - Orders.Count) * 10) // today: hours ago, in script order
                : DhakaTime.StartOfDay(today.AddDays(-order.DaysAgo)).AddHours(10).AddMinutes(order.No * 7);

            void Add(DemoStep step, TimeSpan after) => events.Add(new(created + after, step, order, null));

            Add(DemoStep.Create, TimeSpan.Zero);
            if (order.Outcome == OrderStatus.New)
            {
                continue;
            }

            if (order.Outcome == OrderStatus.Cancelled)
            {
                Add(DemoStep.Cancel, TimeSpan.FromHours(3));
                continue;
            }

            Add(DemoStep.Confirm, TimeSpan.FromHours(1));
            if (order.Outcome == OrderStatus.Confirmed)
            {
                continue;
            }

            Add(DemoStep.Book, TimeSpan.FromHours(5));
            if (order.Outcome == OrderStatus.Booked)
            {
                continue;
            }

            Add(DemoStep.PickUp, TimeSpan.FromDays(1));
            if (order.Outcome == OrderStatus.InTransit)
            {
                continue;
            }

            var travelDays = inside ? 2 : 3;
            if (order.Outcome is OrderStatus.Returned or OrderStatus.ReceivedBack)
            {
                Add(DemoStep.Return, TimeSpan.FromDays(travelDays + 1));
                if (order.Outcome == OrderStatus.ReceivedBack)
                {
                    Add(DemoStep.ReceiveBack, TimeSpan.FromDays(6));
                }

                continue;
            }

            Add(DemoStep.Deliver, TimeSpan.FromDays(travelDays).Add(TimeSpan.FromHours(6)));
            if (order.Outcome == OrderStatus.CodSettled)
            {
                deliveries.Add((order, created + TimeSpan.FromDays(travelDays).Add(TimeSpan.FromHours(6))));
            }
        }

        foreach (var daysAgo in _payoutDaysAgo)
        {
            var paidAt = DhakaTime.StartOfDay(today.AddDays(-daysAgo)).AddHours(15);
            var paid = deliveries.Where(d => d.At.AddDays(1) <= paidAt).Select(d => d.Order).ToList();
            deliveries.RemoveAll(d => paid.Contains(d.Order));
            if (paid.Count > 0)
            {
                var day = DhakaTime.DayOf(paidAt);
                events.Add(new(paidAt, DemoStep.Payout, null, new DemoPayout($"DEMO-PAY-{day:yyyyMMdd}", day, paid)));
            }
        }

        if (deliveries.Count > 0)
        {
            throw new InvalidOperationException("Demo script: a settled order is too recent for any payout.");
        }

        return [.. events.OrderBy(e => e.At).ThenBy(e => e.Order?.No ?? int.MaxValue)];
    }
}

internal sealed record DemoCustomer(int Index, string Name, string Address, bool IsInsideDhaka)
{
    /// <summary>Customers use 0100000 0101 onwards (staff use the first few numbers).</summary>
    public Phone Phone => Phone.Demo(100 + Index);
}

internal sealed record DemoOrder(
    int No,
    int Customer,
    int DaysAgo,
    OrderSource Source,
    IReadOnlyList<(string Sku, int Quantity)> Lines,
    OrderStatus Outcome,
    decimal Advance = 0m,
    decimal ShortPaidBy = 0m,
    string? Note = null)
{
    /// <summary>Fixed per order: a second run replays the first order instead of creating another.</summary>
    public string IdempotencyKey => $"demo-order-{No:00}";
}

internal enum DemoStep
{
    Create,
    Confirm,
    Cancel,
    Book,
    PickUp,
    Deliver,
    Return,
    ReceiveBack,
    Payout,
}

internal sealed record DemoPayout(string Reference, DateOnly PaidOn, IReadOnlyList<DemoOrder> Orders);

internal sealed record DemoEvent(DateTimeOffset At, DemoStep Step, DemoOrder? Order, DemoPayout? Payout);
