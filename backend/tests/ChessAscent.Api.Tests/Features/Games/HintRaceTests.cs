using System.Net;
using ChessAscent.Api.Database;
using ChessAscent.Api.Tests.Features.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using AccountData = ChessAscent.Api.Tests.Features.Accounts.Accounts;

namespace ChessAscent.Api.Tests.Features.Games;

// Two requests for a hint in the same position at once, as a double click or
// two browser tabs can send them, where both have found the hint before
// either has counted it. Sent side by side they would usually not overlap,
// so the first request is held just after its update has counted the hint
// and locked the game's row, and let go only once PostgreSQL reports the
// second's update waiting for that lock.
public sealed class HintRaceTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoRequestsForAHintInTheSamePositionAtOnceBothGetItAndItIsCountedOnce()
    {
        var hold = new HoldTheFirstCommand(command =>
            HoldTheFirstCommand.IsGameUpdate(command) && command.CommandText.Contains("\"HintCount\"", StringComparison.Ordinal));
        var hints = FakeHintProvider.Hinting("e2e4");
        using var held = factory.WithFakes(
            new FakeOpponent(),
            hints,
            configure: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(hold)));
        using var client = held.CreateClient();
        await client.RegisterNewAsync(AccountData.NewEmail());
        var game = await client.CreateComputerGameAsync("white");

        var first = client.PostHintAsync(game.Id, 1);
        await hold.FirstRan.WaitAsync(Patience, Cancellation);
        var second = client.PostHintAsync(game.Id, 1);
        var secondWaitedOnTheLock = await HintCountWaitsOnAsync(hold.HeldBackend);
        hold.Release();
        using var firstResponse = await first;
        using var secondResponse = await second;

        secondWaitedOnTheLock.Should().BeTrue("the second count must have waited for the first one's row lock");
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await secondResponse.ReadHintAsync()).Should().Be(await firstResponse.ReadHintAsync());
        hints.Calls.Should().HaveCount(2);
        (await client.ReadGameAsync(game.Id)).HintCount.Should().Be(1);
    }

    // Asks PostgreSQL, from a connection of its own, whether a hint count is
    // blocked by the backend that holds the first request's lock, polling
    // until one is or patience runs out.
    private async Task<bool> HintCountWaitsOnAsync(int heldBackend)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_stat_activity
            WHERE @held = ANY(pg_blocking_pids(pid)) AND query LIKE '%"HintCount"%'
            """,
            connection);
        command.Parameters.AddWithValue("held", heldBackend);

        var giveUpAt = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < giveUpAt)
        {
            if (await command.ExecuteScalarAsync(Cancellation) is > 0L)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), Cancellation);
        }

        return false;
    }
}
