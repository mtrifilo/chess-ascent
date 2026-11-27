using System.Text;

namespace ChessAscent.Api.Engine;

/// <summary>A line read from an engine.</summary>
/// <param name="Text">The line without its ending, cut to the maximum length if it was longer.</param>
/// <param name="IsTruncated">The line was longer than the maximum, so <paramref name="Text"/> is only its start.</param>
internal readonly record struct EngineLine(string Text, bool IsTruncated);

/// <summary>
/// Reads lines of any length while keeping at most a fixed number of
/// characters of each. <see cref="TextReader.ReadLineAsync()"/> would hold a
/// whole line in memory, however long, so an engine that printed without
/// end would exhaust the server's memory.
/// </summary>
/// <remarks>A line ends at LF; a CR just before the LF is dropped too.</remarks>
internal sealed class BoundedLineReader(TextReader source, int maxLineLength)
{
    private readonly char[] _buffer = new char[4096];
    private readonly StringBuilder _line = new();
    private int _next;
    private int _end;

    /// <summary>The next line, or null at the end of the stream.</summary>
    public async ValueTask<EngineLine?> ReadLineAsync(CancellationToken cancellationToken)
    {
        _line.Clear();
        long length = 0;
        var last = '\0';
        while (true)
        {
            if (_next == _end)
            {
                _next = 0;
                _end = await source.ReadAsync(_buffer.AsMemory(), cancellationToken);
                if (_end == 0)
                {
                    // A last line with no ending still counts as a line.
                    return length == 0 ? null : Finish(length, last);
                }
            }

            var newline = Array.IndexOf(_buffer, '\n', _next, _end - _next);
            var stop = newline < 0 ? _end : newline;
            var count = stop - _next;
            if (count > 0)
            {
                // One character beyond the maximum is kept, so that a line of
                // exactly the maximum followed by CR is not taken as too long.
                var room = maxLineLength + 1 - _line.Length;
                _line.Append(_buffer, _next, Math.Min(Math.Max(room, 0), count));
                length += count;
                last = _buffer[stop - 1];
            }

            if (newline < 0)
            {
                _next = _end;
                continue;
            }

            _next = newline + 1;
            return Finish(length, last);
        }
    }

    private EngineLine Finish(long length, char last)
    {
        if (last == '\r')
        {
            length--;
        }

        var isTruncated = length > maxLineLength;
        var kept = (int)Math.Min(length, maxLineLength);
        return new EngineLine(_line.ToString(0, kept), isTruncated);
    }
}
