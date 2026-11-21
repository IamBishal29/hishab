using System.Collections.Concurrent;
using Hishab.Application.Couriers;
using Hishab.Domain.Couriers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure.Couriers;

/// <summary>
/// Sends many parcels to one courier without hammering it: chunks of the courier's bulk size when
/// it has a bulk endpoint, otherwise single bookings, and in both cases at most
/// <see cref="BookingOptions.MaxParallelBookings"/> calls in flight. It touches no DbContext (not
/// thread-safe), only the courier adapter, so running calls in parallel is safe.
/// </summary>
internal sealed partial class CourierBatchSender(IOptions<BookingOptions> options, ILogger<CourierBatchSender> logger)
{
    /// <summary>
    /// One entry per parcel, by merchant reference: the courier's answer, or null when there was
    /// none (call failed, courier skipped it, or the request was cancelled). A failed call affects
    /// only its own parcels, never the others.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, BookingResult?>> SendAsync(
        ICourierProvider courier, CourierCredentials keys, IReadOnlyList<ParcelRequest> parcels, CancellationToken cancellationToken)
    {
        var answers = new ConcurrentDictionary<string, BookingResult>(StringComparer.Ordinal);
        IEnumerable<ParcelRequest[]> calls = courier.MaxBulkBookingSize is { } size and > 0
            ? parcels.Chunk(size)
            : parcels.Select(p => new[] { p });
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Value.MaxParallelBookings,
            CancellationToken = cancellationToken,
        };

        try
        {
            await Parallel.ForEachAsync(calls, parallel, async (call, token) =>
            {
                foreach (var answer in await CallAsync(courier, keys, call, token))
                {
                    answers[answer.MerchantReference] = answer;
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away: stop sending, but hand back what already came in so it gets saved.
            LogCancelled(logger, courier.Code, answers.Count, parcels.Count);
        }

        return parcels.ToDictionary(p => p.MerchantReference, p => answers.GetValueOrDefault(p.MerchantReference), StringComparer.Ordinal);
    }

    private async Task<IReadOnlyList<BookingResult>> CallAsync(
        ICourierProvider courier, CourierCredentials keys, ParcelRequest[] call, CancellationToken cancellationToken)
    {
        try
        {
            if (courier.MaxBulkBookingSize is > 0)
            {
                var sent = call.Select(p => p.MerchantReference).ToHashSet(StringComparer.Ordinal);
                var results = await courier.BookManyAsync(keys, call, cancellationToken);
                return [.. results.Where(r => sent.Contains(r.MerchantReference))]; // match by reference, never by position
            }

            return [await courier.BookAsync(keys, call[0], cancellationToken)];
        }
        catch (CourierUnavailableException)
        {
            return []; // no answer for these parcels: they stay Requested and a retry is safe
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // An adapter bug must not lose the answers already received for other parcels.
            // Type only: the message may echo the courier's response (personal data).
            LogCallFailed(logger, courier.Code, call.Length, e.GetType().Name);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Provider} booking call for {Count} parcels failed with {ExceptionType}")]
    private static partial void LogCallFailed(ILogger logger, CourierProvider provider, int count, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bulk booking with {Provider} cancelled after {Answered} of {Total} answers")]
    private static partial void LogCancelled(ILogger logger, CourierProvider provider, int answered, int total);
}
