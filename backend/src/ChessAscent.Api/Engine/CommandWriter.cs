namespace ChessAscent.Api.Engine;

/// <summary>How writing one command to the engine ended, when it did not throw.</summary>
internal enum CommandWriteOutcome
{
    /// <summary>The whole line was written and flushed.</summary>
    Written,

    /// <summary>The time ran out before anything was written; the conversation is still in step.</summary>
    NotStarted,

    /// <summary>The time ran out while the line was being written; part of it may be in the pipe.</summary>
    CutOff,
}

/// <summary>
/// Writes one command line to the engine's input, and says whether a
/// cancellation came before the write began or during it. The difference
/// matters: a write cut off half-way leaves the next command to be read as its
/// continuation, while one that never began leaves nothing behind.
/// </summary>
internal static class CommandWriter
{
    /// <exception cref="IOException">The pipe is closed.</exception>
    public static async Task<CommandWriteOutcome> WriteAsync(TextWriter input, string command, CancellationToken cancellationToken)
    {
        // TextWriter's asynchronous methods return a cancelled task without
        // writing anything when the token is already cancelled, which is
        // indistinguishable from a cancellation part-way, so it is checked here.
        if (cancellationToken.IsCancellationRequested)
        {
            return CommandWriteOutcome.NotStarted;
        }

        try
        {
            await input.WriteLineAsync(command.AsMemory(), cancellationToken);
            await input.FlushAsync(cancellationToken);
            return CommandWriteOutcome.Written;
        }
        catch (OperationCanceledException)
        {
            return CommandWriteOutcome.CutOff;
        }
    }
}
