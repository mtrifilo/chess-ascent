namespace ChessAscent.FakeUciEngine;

/// <summary>
/// Appends each command received, and a few events, to a file the tests
/// read. Written from the command loop and from timers, so under a lock.
/// </summary>
internal sealed class Transcript(string? path)
{
    private readonly Lock _lock = new();

    public void Record(string line)
    {
        if (path is null)
        {
            return;
        }

        lock (_lock)
        {
            File.AppendAllText(path, line + "\n");
        }
    }
}
