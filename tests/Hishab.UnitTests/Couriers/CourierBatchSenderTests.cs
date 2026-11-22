using Hishab.Application.Couriers;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Couriers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hishab.UnitTests.Couriers;

public class CourierBatchSenderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CourierCredentials _keys = new(new Dictionary<string, string> { ["apiKey"] = "k" });

    private static CourierBatchSender Sender(int maxParallel = 4) =>
        new(Options.Create(new BookingOptions { MaxParallelBookings = maxParallel }), NullLogger<CourierBatchSender>.Instance);

    private static List<ParcelRequest> Parcels(params string[] references) =>
        [.. references.Select(r => new ParcelRequest(r, "Rahima", "+8801712345678", "Mirpur", true, Money.Bdt(100m), 1, null))];

    [Fact]
    public async Task A_courier_with_a_bulk_endpoint_gets_chunks_of_its_size()
    {
        var courier = new RecordingCourier { BulkSize = 2 };

        var answers = await Sender().SendAsync(courier, _keys, Parcels("a", "b", "c", "d", "e"), Ct);

        courier.BulkCalls.Select(c => c.Count).Order().ShouldBe([1, 2, 2]);
        courier.SingleCalls.ShouldBe(0);
        answers.Values.ShouldAllBe(a => a != null && a.Succeeded);
    }

    [Fact]
    public async Task Without_a_bulk_endpoint_no_more_than_the_limit_run_at_once()
    {
        var courier = new RecordingCourier { Delay = TimeSpan.FromMilliseconds(40) };

        var answers = await Sender(maxParallel: 3).SendAsync(courier, _keys, Parcels("a", "b", "c", "d", "e", "f", "g"), Ct);

        courier.SingleCalls.ShouldBe(7);
        courier.MaxInFlight.ShouldBe(3);
        answers.Count.ShouldBe(7);
    }

    [Fact]
    public async Task A_call_without_an_answer_loses_only_its_own_parcels()
    {
        var courier = new RecordingCourier { BulkSize = 2, UnavailableWhenSent = "c" };

        var answers = await Sender().SendAsync(courier, _keys, Parcels("a", "b", "c", "d"), Ct);

        answers["a"].ShouldNotBeNull();
        answers["b"].ShouldNotBeNull();
        answers["c"].ShouldBeNull(); // unknown: stays Requested, retry is safe
        answers["d"].ShouldBeNull(); // same call as "c"
    }

    [Fact]
    public async Task Bulk_answers_are_matched_by_reference_and_missing_ones_count_as_no_answer()
    {
        var courier = new RecordingCourier
        {
            BulkSize = 10,
            BulkAnswer = parcels => [BookingResult.Booked("stranger", "X", "X", Money.Bdt(1m)), .. parcels.Skip(1).Reverse().Select(Booked)],
        };

        var answers = await Sender().SendAsync(courier, _keys, Parcels("a", "b", "c"), Ct);

        answers.Keys.Order().ShouldBe(["a", "b", "c"]); // "stranger" is ignored
        answers["a"].ShouldBeNull();
        answers["b"]!.ConsignmentId.ShouldBe("C-b");
        answers["c"]!.ConsignmentId.ShouldBe("C-c");
    }

    [Fact]
    public async Task An_adapter_bug_for_one_parcel_keeps_the_answers_for_the_others()
    {
        var courier = new RecordingCourier { ThrowWhenSent = "b" };

        var answers = await Sender().SendAsync(courier, _keys, Parcels("a", "b", "c"), Ct);

        answers["a"].ShouldNotBeNull();
        answers["b"].ShouldBeNull();
        answers["c"].ShouldNotBeNull();
    }

    private static BookingResult Booked(ParcelRequest p) => BookingResult.Booked(p.MerchantReference, "C-" + p.MerchantReference, "T", Money.Bdt(60m));

    private sealed class RecordingCourier : ICourierProvider
    {
        private readonly Lock _gate = new();
        private int _inFlight;

        public int? BulkSize { get; init; }

        public TimeSpan Delay { get; init; } = TimeSpan.Zero;

        public string? UnavailableWhenSent { get; init; }

        public string? ThrowWhenSent { get; init; }

        public Func<IReadOnlyList<ParcelRequest>, IReadOnlyList<BookingResult>>? BulkAnswer { get; init; }

        public List<IReadOnlyList<ParcelRequest>> BulkCalls { get; } = [];

        public int SingleCalls { get; private set; }

        public int MaxInFlight { get; private set; }

        public CourierProvider Code => CourierProvider.Fake;

        public int? MaxBulkBookingSize => BulkSize;

        public IReadOnlyList<CourierCredentialField> CredentialFields => [];

        public async Task<BookingResult> BookAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                SingleCalls++;
                MaxInFlight = Math.Max(MaxInFlight, ++_inFlight);
            }

            try
            {
                await Task.Delay(Delay, cancellationToken);
                Check([parcel]);
                return Booked(parcel);
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight--;
                }
            }
        }

        public Task<IReadOnlyList<BookingResult>> BookManyAsync(
            CourierCredentials credentials, IReadOnlyList<ParcelRequest> parcels, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                BulkCalls.Add(parcels);
            }

            Check(parcels);
            return Task.FromResult(BulkAnswer?.Invoke(parcels) ?? [.. parcels.Select(Booked)]);
        }

        private void Check(IReadOnlyList<ParcelRequest> parcels)
        {
            if (parcels.Any(p => p.MerchantReference == UnavailableWhenSent))
            {
                throw new CourierUnavailableException(Code);
            }

            if (parcels.Any(p => p.MerchantReference == ThrowWhenSent))
            {
                throw new InvalidOperationException("adapter bug");
            }
        }

        public Task<CredentialCheck> ValidateCredentialsAsync(CourierCredentials credentials, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public bool SupportsLocations => false;


        public Task<IReadOnlyList<CourierLocation>> GetLocationsAsync(

            CourierCredentials credentials, LocationLevel level, string? parentId, CancellationToken cancellationToken) =>

            throw new NotSupportedException();


        public Task<CourierQuote> QuoteAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ParcelStatusResult> GetStatusAsync(CourierCredentials credentials, string consignmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
