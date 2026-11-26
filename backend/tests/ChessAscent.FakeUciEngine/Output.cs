namespace ChessAscent.FakeUciEngine;

/// <summary>
/// Writes protocol lines to standard output. A search runs alongside the
/// command loop, so each line is written whole under a lock.
/// </summary>
internal sealed class Output(bool crLf)
{
    private readonly Lock _lock = new();
    private readonly string _newLine = crLf ? "\r\n" : "\n";

    public void Send(string line)
    {
        lock (_lock)
        {
            Console.Out.Write(line + _newLine);
            Console.Out.Flush();
        }
    }
}
