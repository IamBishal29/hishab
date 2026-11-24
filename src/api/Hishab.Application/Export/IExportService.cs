using Hishab.Application.Orders;

namespace Hishab.Application.Export;

/// <summary>The shop's data as CSV files it can take elsewhere (PRD "Data export"), streamed row by row.</summary>
public interface IExportService
{
    /// <summary>Writes every order matching <paramref name="filter"/> (as on the order list), newest first.</summary>
    Task WriteOrdersAsync(OrderListFilter filter, Stream output, CancellationToken cancellationToken);

    /// <summary>Writes every customer matching <paramref name="search"/> (as on the customer list), newest first.</summary>
    Task WriteCustomersAsync(string? search, Stream output, CancellationToken cancellationToken);
}
