using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ChessAscent.Api.Tests.Features.Games;

/// <summary>
/// Holds the first of the commands <paramref name="holds"/> picks once it has
/// run, until the test releases it, and reports when a second is about to
/// run. For a game update, the first has then left its transaction holding
/// the game's row lock. A hold that is never released ends after
/// <see cref="Patience"/>, so a broken test fails instead of hanging.
/// </summary>
/// <param name="holds">Picks the commands to count and hold.</param>
internal sealed class HoldTheFirstCommand(Func<DbCommand, bool> holds) : DbCommandInterceptor
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly TaskCompletionSource _firstRan = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _secondStarting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;

    /// <summary>Holds the first update of a game, whatever it sets.</summary>
    public static HoldTheFirstCommand GameUpdates() => new(IsGameUpdate);

    /// <summary>Completes once the first command has run and is being held.</summary>
    public Task FirstRan => _firstRan.Task;

    /// <summary>
    /// The PostgreSQL backend process of the connection the first command
    /// ran on, which holds whatever locks it took; valid once
    /// <see cref="FirstRan"/> has completed.
    /// </summary>
    public int HeldBackend { get; private set; }

    /// <summary>Completes when a second command is about to run.</summary>
    public Task SecondStarting => _secondStarting.Task;

    public void Release() => _released.TrySetResult();

    /// <summary>Whether the command is an update of the Games table.</summary>
    public static bool IsGameUpdate(DbCommand command) =>
        command.CommandText.StartsWith("UPDATE \"Games\"", StringComparison.Ordinal);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (holds(command) && Interlocked.Increment(ref _started) == 2)
        {
            _secondStarting.TrySetResult();
        }

        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (holds(command) && !_firstRan.Task.IsCompleted)
        {
            HeldBackend = ((NpgsqlConnection)command.Connection!).ProcessID;
            _firstRan.TrySetResult();
            await Task.WhenAny(_released.Task, Task.Delay(Patience, cancellationToken));
        }

        return result;
    }
}
