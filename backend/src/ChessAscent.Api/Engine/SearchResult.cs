namespace ChessAscent.Api.Engine;

/// <summary>What a finished search answered: a move, or that there is none.</summary>
/// <remarks>
/// A position with no legal move (checkmate or stalemate) is a normal
/// outcome, so it is a case here rather than an exception. Engines report it
/// in two ways, <c>bestmove (none)</c> and <c>bestmove 0000</c>; both become
/// <see cref="NoLegalMove"/>.
/// </remarks>
public union SearchResult(BestMove, NoLegalMove)
{
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>The engine's move.</summary>
/// <param name="Move">
/// The move exactly as the engine wrote it, normally in UCI coordinate form
/// such as <c>e2e4</c>. It is not checked: the caller must confirm it is
/// legal in the position before using it.
/// </param>
public readonly record struct BestMove(string Move);

/// <summary>The engine says the side to move has no legal move.</summary>
public readonly record struct NoLegalMove;
