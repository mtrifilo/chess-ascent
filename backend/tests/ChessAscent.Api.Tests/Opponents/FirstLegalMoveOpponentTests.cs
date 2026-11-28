using ChessAscent.Api.Opponents;
using ChessAscent.Api.Tests.Features.Games;
using ChessAscent.Chess;

namespace ChessAscent.Api.Tests.Opponents;

public sealed class FirstLegalMoveOpponentTests
{
    private static readonly FirstLegalMoveOpponent Opponent = new(GameData.Rules);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // a2a3 comes before a2a4 and every other move of White's in ordinal order;
    // after 1.e4, a7a5 is first of Black's.
    [Theory]
    [InlineData(new string[0], "a2a3")]
    [InlineData(new[] { "e2e4" }, "a7a5")]
    public async Task PlaysTheFirstLegalMoveInOrdinalOrderOfItsUciText(string[] moves, string expected)
    {
        var reply = await Opponent.ChooseMoveAsync(moves, 800, Token);

        reply.Value.Should().Be(new OpponentMove(expected));
    }

    // It plays both sides here, so each reply comes from a position the one
    // before it led to, until the game ends.
    [Fact]
    public async Task PlaysALegalMoveInEveryPositionOfAGameUntilItEnds()
    {
        var moves = new List<string>();
        IChessGame game;
        while ((game = Rebuilt(moves)).Status is Ongoing && moves.Count < 1000)
        {
            var reply = await Opponent.ChooseMoveAsync(moves, 1400, Token);

            var move = reply.Value.Should().BeOfType<OpponentMove>($"after {string.Join(' ', moves)}").Subject;
            game.LegalMoves().Select(legal => legal.Uci).Should().Contain(move.Uci);
            moves.Add(move.Uci);
        }

        game.Status.Value.Should().NotBeOfType<Ongoing>($"the game should end, but is still going after {moves.Count} half-moves");
    }

    private static IChessGame Rebuilt(IReadOnlyList<string> moves) =>
        GameData.Rules.Load(GameData.StartingFen, moves).Value.Should().BeOfType<GameLoaded>().Subject.Game;

    [Fact]
    public async Task RefusesAStrengthThatIsNotOffered()
    {
        var reply = await Opponent.ChooseMoveAsync(["e2e4"], 700, Token);

        reply.Value.Should().Be(new StrengthNotOffered(700));
    }

    [Fact]
    public async Task RefusesAMoveListThatIsNotALegalGame()
    {
        var reply = await Opponent.ChooseMoveAsync(["e2e4", "e2e4"], 800, Token);

        reply.Value.Should().BeOfType<InvalidMoveInHistory>().Which.Index.Should().Be(1);
    }

    [Fact]
    public async Task RefusesAFinishedGame()
    {
        var reply = await Opponent.ChooseMoveAsync(GameRequests.FoolsMate, 800, Token);

        reply.Value.Should().BeOfType<GameAlreadyOver>();
    }
}
