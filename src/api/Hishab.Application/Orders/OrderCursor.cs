using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace Hishab.Application.Orders;

/// <summary>
/// A keyset bookmark: the (CreatedAt, Number) of the last order on a page. The next page is
/// "everything older than this", which an index answers directly however deep the reader
/// scrolls, and new orders arriving meanwhile never shift or duplicate rows. Opaque to
/// clients (base64url) so its shape can change without breaking them.
/// </summary>
public readonly record struct OrderCursor(DateTimeOffset CreatedAt, int Number)
{
    private const int Size = sizeof(long) + sizeof(int);

    public string Encode()
    {
        Span<byte> bytes = stackalloc byte[Size];
        BinaryPrimitives.WriteInt64BigEndian(bytes, CreatedAt.UtcTicks);
        BinaryPrimitives.WriteInt32BigEndian(bytes[sizeof(long)..], Number);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? value, [NotNullWhen(true)] out OrderCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value) || value.Length > 64)
        {
            return false;
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        Span<byte> bytes = stackalloc byte[48];
        if (!Convert.TryFromBase64String(base64, bytes, out var written) || written != Size)
        {
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new OrderCursor(new DateTimeOffset(ticks, TimeSpan.Zero), BinaryPrimitives.ReadInt32BigEndian(bytes[sizeof(long)..Size]));
        return true;
    }
}
