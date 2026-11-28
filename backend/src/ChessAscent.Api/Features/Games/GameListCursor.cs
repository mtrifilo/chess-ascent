using System.Buffers.Binary;
using System.Buffers.Text;

namespace ChessAscent.Api.Features.Games;

/// <summary>
/// Where a page of a player's games ends: the last activity time and id of
/// its last game. The next page holds the games that come after it in the
/// list's order, most recent activity first and then by id.
/// </summary>
/// <remarks>
/// The API sends it as an opaque string (<see cref="Encode"/>), which a client
/// passes back unchanged. It positions the next page among the caller's own
/// games only, so a cursor a client makes up shows nothing it could not list
/// anyway.
/// </remarks>
/// <param name="UpdatedAt">The last game's last activity time, as the database holds it.</param>
/// <param name="Id">The last game's id.</param>
public readonly record struct GameListCursor(DateTimeOffset UpdatedAt, Guid Id)
{
    // Eight bytes of UTC ticks, then the sixteen bytes of the id.
    private const int TicksLength = sizeof(long);
    private const int EncodedLength = TicksLength + 16;

    /// <summary>The cursor as the URL-safe text the API sends.</summary>
    public string Encode()
    {
        Span<byte> bytes = stackalloc byte[EncodedLength];
        BinaryPrimitives.WriteInt64BigEndian(bytes, UpdatedAt.UtcTicks);
        Id.TryWriteBytes(bytes[TicksLength..], bigEndian: true, out _);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>Reads a cursor that <see cref="Encode"/> wrote.</summary>
    /// <returns>Whether <paramref name="text"/> is one; anything else, including any other length, is not.</returns>
    public static bool TryDecode(string text, out GameListCursor cursor)
    {
        cursor = default;

        // TryDecodeFromChars returns false only when the bytes do not fit,
        // and throws on text that is not base64url, so the text is checked
        // first.
        if (!Base64Url.IsValid(text, out var decodedLength) || decodedLength != EncodedLength)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[EncodedLength];
        Base64Url.DecodeFromChars(text, bytes);
        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new GameListCursor(
            new DateTimeOffset(ticks, TimeSpan.Zero), new Guid(bytes[TicksLength..], bigEndian: true));
        return true;
    }
}
