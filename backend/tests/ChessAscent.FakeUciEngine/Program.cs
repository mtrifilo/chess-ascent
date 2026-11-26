using System.Diagnostics;
using System.Globalization;
using ChessAscent.FakeUciEngine;

// A fake UCI engine. It speaks just enough of the protocol for the client's
// tests, and its misbehaviour is chosen on the command line:
//
//   --bestmove <text>          what follows "bestmove " after a search (default "e2e4 ponder e7e5")
//   --stopped-bestmove <text>  the same, when the search was ended by "stop" (default "a7a6")
//   --extra-bestmove <text>    print a second, unasked-for bestmove line after each search
//   --search-ms <n,n,...>      how long each search takes; the last value repeats (default 0)
//   --silent-on <command>      never answer this command; for "go", ignore "stop" as well
//   --silent-after <n>         answer the first n of those commands, then fall silent (default 0)
//   --exit-on <command>        print one info line, then exit with --exit-code (default 3)
//   --long-line <length>       print an info line of this many characters during each search; repeatable
//   --noise                    print junk lines, long and short, before "uciok"
//   --crlf                     end lines with CR LF instead of LF
//   --ignore-quit              stay running after "quit" and after standard input closes
//   --stderr <text>            write this line to standard error at start; repeatable
//   --transcript <path>        append every command received to this file
//   --pid-file <path>          write this process's id to the file
//   --child-pid-file <path>    start a child process that sleeps, and write its id to the file
//   --sleep                    do nothing but sleep (the child's mode)
var settings = Settings.Parse(args);

if (settings.Sleep)
{
    await Task.Delay(Timeout.Infinite);
    return 0;
}

if (settings.PidFile is { } pidFile)
{
    await File.WriteAllTextAsync(pidFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
}

if (settings.ChildPidFile is { } childPidFile)
{
    // The child inherits this process's standard streams, as a program
    // started by a wrapper script would, so the client's pipes stay open
    // until the child is gone too.
    using var child = StartSleepingChild();
    await File.WriteAllTextAsync(childPidFile, child.Id.ToString(CultureInfo.InvariantCulture));
}

foreach (var text in settings.StandardError)
{
    await Console.Error.WriteLineAsync(text);
}

var output = new Output(settings.CrLf);
output.Send("Fake UCI engine, for tests");

var searchCount = 0;
var silentCount = 0;
Search? search = null;

while (await Console.In.ReadLineAsync() is { } command)
{
    if (settings.Transcript is { } transcript)
    {
        await File.AppendAllTextAsync(transcript, command + "\n");
    }

    var word = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    if (word == settings.ExitOn)
    {
        output.Send("info depth 1 score cp 0");
        return settings.ExitCode;
    }

    if (word == settings.SilentOn && ++silentCount > settings.SilentAfter)
    {
        continue;
    }

    switch (word)
    {
        case "uci":
            output.Send("id name Fake Engine");
            output.Send("id author Chess Ascent tests");
            output.Send("option name Skill Level type spin default 20 min 0 max 20");
            if (settings.Noise)
            {
                SendNoise(output);
            }

            output.Send("uciok");
            break;

        case "isready":
            output.Send("readyok");
            break;

        case "go":
            var delay = settings.SearchMilliseconds[Math.Min(searchCount, settings.SearchMilliseconds.Count - 1)];
            searchCount++;
            search = Search.Start(output, settings, delay);
            break;

        case "stop":
            if (search is not null)
            {
                await search.StopAsync();
                search = null;
            }

            break;

        case "quit":
            if (settings.IgnoreQuit)
            {
                break;
            }

            // Real engines print the bestmove of a running search before they exit.
            if (search is not null)
            {
                await search.StopAsync();
            }

            return 0;

        default:
            // position, setoption, ucinewgame and anything unknown: nothing to say.
            break;
    }
}

if (settings.IgnoreQuit)
{
    await Task.Delay(Timeout.Infinite);
}

return 0;

static Process StartSleepingChild()
{
    var self = Environment.ProcessPath ?? throw new InvalidOperationException("The process path is unknown.");
    var start = new ProcessStartInfo(self) { UseShellExecute = false };

    // Started through the dotnet host, the program is an argument to it;
    // started through its own launcher, it is not.
    if (Path.GetFileNameWithoutExtension(self) == "dotnet")
    {
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(typeof(Settings).Assembly.Location);
    }

    start.ArgumentList.Add("--sleep");
    return Process.Start(start) ?? throw new InvalidOperationException("The child process did not start.");
}

static void SendNoise(Output output)
{
    output.Send("");
    output.Send("   ");
    output.Send("\t \t");
    output.Send("uciokay");
    output.Send("info string uciok");
    output.Send("  id   name   spaced out  ");
    output.Send("option name Junk type string default <empty>");
    output.Send("\u0007bell \u001b[31mred\u001b[0m");
    output.Send("café ♞");
    output.Send(new string('n', 100_000));
}
