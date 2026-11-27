using System.Diagnostics;
using System.Globalization;
using ChessAscent.FakeUciEngine;

// A fake UCI engine. It speaks just enough of the protocol for the client's
// tests, and its misbehaviour is chosen on the command line:
//
//   --bestmove <text>          what follows "bestmove " after a search (default "e2e4 ponder e7e5");
//                              a search ended by "stop" answers "a7a6" instead
//   --extra-bestmove <text>    print a second, unasked-for bestmove line with each search's own
//   --search-ms <n,n,...>      how long each search takes; the last value repeats (default 0)
//   --ready-ms <n,n,...>       how long each isready takes to answer, while still reading
//                              commands; the last value repeats (default 0)
//   --silent-on <command>      never answer this command; for "go", ignore "stop" as well
//   --silent-after <n>         answer the first n of those commands, then fall silent (default 0)
//   --exit-on <command>        print one info line, then exit
//   --exit-code <n>            the exit code for --exit-on (default 3)
//   --long-line <length>       print an info line of this many characters during each search; repeatable
//   --noise                    print junk lines, long and short, then "uciok" 200 ms later, while
//                              still reading commands; the transcript records "(uciok sent)"
//   --crlf                     end lines with CR LF instead of LF
//   --ignore-quit              stay running after "quit" and after standard input closes
//   --stderr-flood <n>         write n lines to standard error at start
//   --stderr <text>            write this line to standard error at start; repeatable
//   --stderr-on <command> <text>  write the line to standard error when the command arrives; repeatable
//   --transcript <path>        append every command received to this file
//   --pid-file <path>          write this process's id to the file
//   --pid-dir <path>           create a file in the directory named by this process's id, so
//                              that several processes started with the same arguments can be told apart
//   --env-dump <path>          write this process's environment variables to the file, one NAME=VALUE per line
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

if (settings.EnvironmentDump is { } environmentDump)
{
    var variables = Environment.GetEnvironmentVariables();
    var lines = variables.Keys.Cast<string>().Order(StringComparer.Ordinal).Select(name => $"{name}={variables[name]}");
    await File.WriteAllLinesAsync(environmentDump, lines);
}

if (settings.PidDirectory is { } pidDirectory)
{
    await File.WriteAllTextAsync(Path.Combine(pidDirectory, Environment.ProcessId.ToString(CultureInfo.InvariantCulture)), "");
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

for (var i = 0; i < settings.StandardErrorFlood; i++)
{
    await Console.Error.WriteLineAsync($"flood {i}");
}

// Console.Out is already synchronised, so a line written by a running
// search never interleaves with one written by the command loop, and it
// flushes after every write.
Console.Out.NewLine = settings.CrLf ? "\r\n" : "\n";
Console.Out.WriteLine("Fake UCI engine, for tests");

var transcript = new Transcript(settings.Transcript);
var searchCount = 0;
var readyCount = 0;
var silentCount = 0;
Search? search = null;

while (await Console.In.ReadLineAsync() is { } command)
{
    transcript.Record(command);

    var word = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    foreach (var (on, text) in settings.StandardErrorOn)
    {
        if (on == word)
        {
            await Console.Error.WriteLineAsync(text);
        }
    }

    if (word == settings.ExitOn)
    {
        Console.Out.WriteLine("info depth 1 score cp 0");
        return settings.ExitCode;
    }

    if (word == settings.SilentOn && ++silentCount > settings.SilentAfter)
    {
        continue;
    }

    switch (word)
    {
        case "uci":
            Console.Out.WriteLine("id name Fake Engine");
            Console.Out.WriteLine("id author Chess Ascent tests");
            Console.Out.WriteLine("option name Skill Level type spin default 20 min 0 max 20");
            if (settings.Noise)
            {
                // The real uciok comes later, while this loop goes on reading,
                // so a client that took a near miss for it would send its next
                // command before "(uciok sent)" appears in the transcript.
                SendNoise();
                _ = SendUciokLaterAsync(transcript);
                break;
            }

            Console.Out.WriteLine("uciok");
            break;

        case "isready":
            var readyDelay = settings.ReadyMilliseconds[Math.Min(readyCount, settings.ReadyMilliseconds.Count - 1)];
            readyCount++;
            if (readyDelay == 0)
            {
                Console.Out.WriteLine("readyok");
            }
            else
            {
                _ = AnswerReadyLaterAsync(readyDelay);
            }

            break;

        case "go":
            var delay = settings.SearchMilliseconds[Math.Min(searchCount, settings.SearchMilliseconds.Count - 1)];
            searchCount++;
            search = new Search(settings, delay);
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

static async Task AnswerReadyLaterAsync(int milliseconds)
{
    await Task.Delay(milliseconds);
    Console.Out.WriteLine("readyok");
}

static async Task SendUciokLaterAsync(Transcript transcript)
{
    await Task.Delay(200);
    transcript.Record("(uciok sent)");
    Console.Out.WriteLine("uciok");
}

static void SendNoise()
{
    Console.Out.WriteLine("");
    Console.Out.WriteLine("   ");
    Console.Out.WriteLine("\t \t");
    Console.Out.WriteLine("uciokay");
    Console.Out.WriteLine("info string uciok");
    Console.Out.WriteLine("  id   name   spaced out  ");
    Console.Out.WriteLine("option name Junk type string default <empty>");
    Console.Out.WriteLine("\u0007bell \u001b[31mred\u001b[0m");
    Console.Out.WriteLine("café ♞");
    Console.Out.WriteLine(new string('n', 100_000));
}
