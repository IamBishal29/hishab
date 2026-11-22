namespace Hishab.Application.Orders;

/// <summary>Paper for invoices; one invoice per page either way.</summary>
public enum InvoiceFormat
{
    /// <summary>148 × 210 mm: folds once into a parcel (the default).</summary>
    A5,

    /// <summary>210 × 297 mm: office printers, long orders.</summary>
    A4,
}

/// <summary>
/// Invoices for the buyer, as one PDF. They go inside the parcel, so they carry selling prices only:
/// the template is built from an allow-list (<c>InvoiceLine</c> has no cost field), never from the order.
/// </summary>
public interface IInvoiceService
{
    public const int MaxOrders = 100;

    /// <summary>One invoice per order, in the order asked for (duplicates once).</summary>
    /// <exception cref="Domain.Common.NotFoundException">An order is not in this shop.</exception>
    /// <exception cref="Domain.Common.DomainValidationException">An order is cancelled.</exception>
    /// <exception cref="PdfRenderingException">The PDF could not be made (retry later).</exception>
    Task<InvoiceDocument> RenderAsync(IReadOnlyList<Guid> orderIds, InvoiceFormat format, CancellationToken cancellationToken);
}

public sealed record InvoiceDocument(byte[] Pdf, int Invoices);
