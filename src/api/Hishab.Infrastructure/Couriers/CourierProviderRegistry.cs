using Hishab.Application.Couriers;
using Hishab.Domain.Couriers;

namespace Hishab.Infrastructure.Couriers;

/// <summary>All registered adapters, by courier code. Two adapters for one code is a wiring bug: fail at startup.</summary>
internal sealed class CourierProviderRegistry : ICourierProviderRegistry
{
    private readonly Dictionary<CourierProvider, ICourierProvider> _byCode;

    public CourierProviderRegistry(IEnumerable<ICourierProvider> providers)
    {
        All = [.. providers.OrderBy(p => p.Code)];
        var duplicate = All.GroupBy(p => p.Code).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"More than one courier adapter is registered for {duplicate.Key}.");
        }

        _byCode = All.ToDictionary(p => p.Code);
    }

    public IReadOnlyList<ICourierProvider> All { get; }

    public ICourierProvider? Find(CourierProvider code) => _byCode.GetValueOrDefault(code);
}
