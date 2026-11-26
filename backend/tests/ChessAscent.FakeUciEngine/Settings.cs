using System.Globalization;

namespace ChessAscent.FakeUciEngine;

/// <summary>The fake engine's behaviour, from its command line. Program.cs lists the options.</summary>
internal sealed class Settings
{
    public string BestMove { get; private set; } = "e2e4 ponder e7e5";

    public string StoppedBestMove { get; private set; } = "a7a6";

    public string? ExtraBestMove { get; private set; }

    public List<int> SearchMilliseconds { get; } = [];

    public string? SilentOn { get; private set; }

    public int SilentAfter { get; private set; }

    public string? ExitOn { get; private set; }

    public int ExitCode { get; private set; } = 3;

    public List<int> LongLines { get; } = [];

    public bool Noise { get; private set; }

    public bool CrLf { get; private set; }

    public bool IgnoreQuit { get; private set; }

    public List<string> StandardError { get; } = [];

    public string? Transcript { get; private set; }

    public string? PidFile { get; private set; }

    public string? ChildPidFile { get; private set; }

    public bool Sleep { get; private set; }

    public static Settings Parse(string[] args)
    {
        var settings = new Settings();
        var position = 0;
        string Next() => position < args.Length
            ? args[position++]
            : throw new ArgumentException($"{args[position - 1]} needs a value.", nameof(args));

        while (position < args.Length)
        {
            var name = args[position++];
            switch (name)
            {
                case "--bestmove": settings.BestMove = Next(); break;
                case "--stopped-bestmove": settings.StoppedBestMove = Next(); break;
                case "--extra-bestmove": settings.ExtraBestMove = Next(); break;
                case "--search-ms": settings.SearchMilliseconds.AddRange(Next().Split(',').Select(ParseInt)); break;
                case "--silent-on": settings.SilentOn = Next(); break;
                case "--silent-after": settings.SilentAfter = ParseInt(Next()); break;
                case "--exit-on": settings.ExitOn = Next(); break;
                case "--exit-code": settings.ExitCode = ParseInt(Next()); break;
                case "--long-line": settings.LongLines.Add(ParseInt(Next())); break;
                case "--noise": settings.Noise = true; break;
                case "--crlf": settings.CrLf = true; break;
                case "--ignore-quit": settings.IgnoreQuit = true; break;
                case "--stderr": settings.StandardError.Add(Next()); break;
                case "--transcript": settings.Transcript = Next(); break;
                case "--pid-file": settings.PidFile = Next(); break;
                case "--child-pid-file": settings.ChildPidFile = Next(); break;
                case "--sleep": settings.Sleep = true; break;
                default: throw new ArgumentException($"Unknown option {name}.", nameof(args));
            }
        }

        if (settings.SearchMilliseconds.Count == 0)
        {
            settings.SearchMilliseconds.Add(0);
        }

        return settings;
    }

    private static int ParseInt(string text) => int.Parse(text, CultureInfo.InvariantCulture);
}
