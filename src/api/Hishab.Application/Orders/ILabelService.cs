namespace Hishab.Application.Orders;

/// <summary>Paper for shipping labels.</summary>
public enum LabelFormat
{
    /// <summary>A4 sheet, 8 labels per page (office printers, sticker sheets).</summary>
    A4,

    /// <summary>100 × 150 mm (4 × 6 inch), one label per page (thermal label printers).</summary>
    Thermal,
}

/// <summary>Shipping labels for booked orders, as one PDF.</summary>
public interface ILabelService
{
    public const int MaxOrders = 100;

    /// <summary>One label per order, in the order asked for (duplicates once).</summary>
    /// <exception cref="Domain.Common.NotFoundException">An order is not in this shop.</exception>
    /// <exception cref="Domain.Common.DomainValidationException">An order is not booked with a courier yet.</exception>
    Task<LabelDocument> RenderAsync(IReadOnlyList<Guid> orderIds, LabelFormat format, CancellationToken cancellationToken);
}

public sealed record LabelDocument(byte[] Pdf, int Labels);

/// <summary>The PDF could not be produced (renderer missing or crashed). Retrying later may work (HTTP 503).</summary>
public sealed class PdfRenderingException() : Domain.Common.DomainException("The PDF could not be made right now. Try again in a moment.");
