namespace Hishab.Application.Couriers;

/// <summary>Limits for bulk booking (section "Booking"). Checked at startup.</summary>
public sealed class BookingOptions
{
    public const string SectionName = "Booking";

    /// <summary>Most orders in one bulk booking request. Keeps one request well inside HTTP timeouts.</summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Most single-booking calls in flight at once for a courier without a bulk endpoint: fast
    /// enough for a packer, gentle enough not to trip the courier's rate limits.
    /// </summary>
    public int MaxParallelBookings { get; set; } = 4;

    public bool IsValid() => MaxBatchSize is >= 1 and <= 500 && MaxParallelBookings is >= 1 and <= 16;
}
