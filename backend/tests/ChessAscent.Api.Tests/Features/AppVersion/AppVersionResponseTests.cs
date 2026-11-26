using ChessAscent.Api.Features.AppVersion;

namespace ChessAscent.Api.Tests.Features.AppVersion;

public sealed class AppVersionResponseTests
{
    [Fact]
    public void ParseSplitsTheCommitFromTheVersion()
    {
        var response = AppVersionResponse.Parse("0.1.0+bb0283cdd10894a80cb6fa46d8684d6034e8e05b");

        response.Should().Be(new AppVersionResponse("0.1.0", "bb0283cdd10894a80cb6fa46d8684d6034e8e05b"));
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.1.0+")]
    public void ParseReportsNoCommitWhenTheBuildHadNone(string informationalVersion)
    {
        var response = AppVersionResponse.Parse(informationalVersion);

        response.Should().Be(new AppVersionResponse("0.1.0", null));
    }
}
