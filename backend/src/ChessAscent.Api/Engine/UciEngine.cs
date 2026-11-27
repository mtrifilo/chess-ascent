using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace ChessAscent.Api.Engine;

/// <summary>
/// One chess engine running as a child process, and the UCI conversation
/// with it over the process's standard input and output.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class does not check chess.</b> It sends the position it is given
/// and returns the engine's reply as text. Engines do not reject bad input:
/// given an illegal move or a malformed FEN, one keeps its previous position
/// and answers for that, another answers for the wrong side or crashes. So the
/// caller sends only positions the chess rules have accepted, and checks that
/// a returned move is legal before using it.
/// </para>
/// <para>
/// It knows no particular engine. Options are set by name and value, and
/// every reply is read the same way.
/// </para>
/// <para>
/// Calls may come from several threads; they are served one at a time,
/// because a UCI engine holds a single conversation. Every wait for the engine
/// is bounded by a timeout from <see cref="UciEngineOptions"/> and ends early
/// when the caller's token is cancelled. A call that is waiting its turn waits
/// for the calls ahead of it, each of which is bounded in the same way, or
/// until its own token is cancelled.
/// </para>
/// <para>
/// How calls fail, apart from argument checks:
/// <list type="bullet">
/// <item><see cref="UciEngineExitedException"/>: the process ended, or closed
/// its input or output. A wait that a dead engine ends is reported this way,
/// never as a timeout or as the caller's cancellation.</item>
/// <item><see cref="UciEngineTimeoutException"/>: a reply did not come in time.</item>
/// <item><see cref="OperationCanceledException"/>: the caller's token was cancelled.</item>
/// <item><see cref="UciProtocolException"/>: a reply could not be read.</item>
/// <item><see cref="ObjectDisposedException"/>: the engine was disposed, before or during the call.</item>
/// <item><see cref="InvalidOperationException"/>: the engine had already become unusable.</item>
/// </list>
/// After any failure, <see cref="IsUsable"/> says whether the engine can
/// still be used. A call that is cancelled or times out while a reply is owed
/// reads on to that reply and discards it, so that it is not later mistaken
/// for the answer to another command; for a search's <c>bestmove</c> it sends
/// <c>stop</c> first. The engine stays usable if the reply comes within the
/// stop timeout, and becomes unusable if it does not. The one exception is
/// the handshake in <see cref="StartAsync"/>: when it fails the engine is
/// disposed at once, so its owed reply is not waited for.
/// </para>
/// </remarks>
// LEARN: `sealed` is Java's `final` for classes: nothing may extend it.
// Java 17's own `sealed` keyword, which lists the permitted subclasses, is
// a different idea that C# expresses with union types. `partial` lets a
// class be split across several declarations that the compiler merges into
// one; here the logging methods at the bottom are completed by generated
// code (see the [LoggerMessage] note there).
//
// LEARN: IAsyncDisposable is AutoCloseable for clean-up that has to wait,
// here for the process to exit. `await using var engine = ...;` is the
// counterpart of try-with-resources: DisposeAsync() is awaited when the
// variable goes out of scope, however the block is left. (The synchronous
// IDisposable and `using` are the exact counterpart of AutoCloseable.)
public sealed partial class UciEngine : IAsyncDisposable
{
    // The longest line printed by the engines this project means to run
    // (Fairy-Stockfish 14 and Stockfish 19, answering `uci`) is 1,157
    // characters. A line over this limit cannot be one the client needs, so
    // it is skipped (and logged) rather than kept, which bounds memory
    // whatever the engine prints.
    private const int MaxLineLength = 16 * 1024;

    // Lines read but not yet consumed. When the buffer is full the reader
    // stops reading, and an engine that keeps printing blocks on its full
    // pipe until the client catches up. With the line limit, that holds at
    // most 4 Mi characters (8 MB) per engine.
    private const int LineBufferCapacity = 256;

    // How much of an over-long line the log shows.
    private const int LoggedLineStart = 80;

    // Standard error and over-long lines are logged, but an engine that
    // prints them without end would flood the log, so each has a budget of
    // entries per window; the rest are counted and the count is logged.
    private const int LogEntriesPerWindow = 100;
    private static readonly TimeSpan LogWindow = TimeSpan.FromSeconds(10);

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // LEARN: ProcessStartInfo and Process are the counterparts of
    // ProcessBuilder and Process. Redirecting a stream gives this side a
    // pipe to it (StandardInput, StandardOutput, StandardError), as
    // ProcessBuilder.Redirect.PIPE does. StartAsync shows the set-up.
    private readonly Process _process;
    private readonly UciEngineOptions _options;
    private readonly ILogger _logger;

    // LEARN: A Channel<T> is an asynchronous queue, the counterpart of a
    // java.util.concurrent BlockingQueue. The difference is in how a consumer
    // waits: BlockingQueue.take() parks a thread until an item arrives, while
    // `await reader.ReadAsync(token)` gives the thread back and resumes the
    // method when an item arrives or the token is cancelled. A bounded channel
    // in Wait mode makes a writer wait the same way when it is full, which is
    // ArrayBlockingQueue.put() without the blocked thread.
    private readonly Channel<string> _lines = Channel.CreateBounded<string>(
        new BoundedChannelOptions(LineBufferCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    // LEARN: SemaphoreSlim is java.util.concurrent.Semaphore with an
    // asynchronous acquire. `await _gate.WaitAsync(token)` suspends the caller
    // without holding a thread, which Java's acquire() cannot do, and gives
    // up if the token is cancelled. With one permit it is a lock that can be
    // held across `await`s; C#'s `lock` statement cannot, because the code
    // after an `await` may resume on a different thread from the one that
    // took the lock. It promises no order among waiters.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // LEARN: A CancellationToken is how .NET asks a running operation to stop.
    // It is passed explicitly, not attached to a thread as Thread.interrupt()
    // is, and it is cooperative: each wait the operation makes is given the
    // token and ends with an OperationCanceledException when it is cancelled.
    // Future.cancel(true) is the nearest Java has. The CancellationTokenSource
    // that issues a token can also cancel it after a delay, which is how most
    // of the timeouts here are made; a few APIs (SemaphoreSlim.WaitAsync,
    // Task.WaitAsync) take a TimeSpan directly and report expiry their own way.
    //
    // This one is cancelled when disposal starts, so that calls in progress
    // stop waiting.
    private readonly CancellationTokenSource _lifetime = new();

    private readonly Lock _disposeLock = new();

    // LEARN: A Task is a running operation that completes later, the
    // counterpart of a CompletableFuture. An `async` method returns one and is
    // written as straight-line code: each `await` is a thenCompose(), the rest
    // of the method becomes the continuation, and an exception from the
    // awaited task is rethrown at the `await`, so ordinary try/catch/finally
    // works across it. Two differences bite. A Task starts running as soon as
    // the method is called; there is no separate "start" step. And `async`
    // spreads up the call stack: the caller must await the Task too, because
    // blocking on it with `.Result` or `.Wait()` holds a thread while it waits
    // and can deadlock. Nothing in this class blocks on a task. Task.Run(...)
    // hands CPU work to the shared thread pool, as ExecutorService.submit()
    // does; this class never needs it, because all its waiting is for I/O,
    // and an `await` on I/O holds no thread at all.
    private readonly Task _outputReader;
    private readonly Task _errorReader;
    private readonly Task _exitWatcher;
    private Task? _disposal;

    // Completed when the engine ends or disposal begins; see Ended.
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // How the process ended, once it has: set once, by whichever notices
    // first (the exit watcher, the output reader, or a failed write).
    //
    // LEARN: `volatile` in C# makes each read and write of the field go to
    // memory, with acquire and release ordering, so other threads see the
    // latest value. That is weaker than Java's `volatile`, which also makes
    // all volatile accesses sequentially consistent; for a field written once
    // and then only read, as here, the two behave alike.
    private volatile ProcessEnd? _end;

    // Why the conversation is out of step. Set only while holding the gate.
    private volatile string? _fault;

    // 1 once the error about another process holding the output is logged.
    private int _outputHeldOpenReported;

    // Set by the output reader the moment the output reaches its end, before
    // it waits for the exit code, so that a call that gives up meanwhile
    // reports the engine as gone rather than as slow.
    private volatile bool _outputClosed;

    private UciEngine(Process process, UciEngineOptions options, ILogger logger)
    {
        _process = process;
        _options = options;
        _logger = logger;
        ProcessId = process.Id;

        // UCI accepts LF or CR LF. A StreamWriter would otherwise end lines
        // the platform's way, which differs on Windows.
        process.StandardInput.NewLine = "\n";

        // LEARN: Calling an async method without awaiting it starts it and
        // hands back its Task, the way CompletableFuture.supplyAsync() hands
        // back a future, except that no thread pool is involved: the method
        // runs on this thread until its first `await` of something not yet
        // finished, and resumes later on a pool thread. These run for the
        // life of the process; disposal awaits them.
        _outputReader = ReadOutputAsync();
        _errorReader = ReadStandardErrorAsync();
        _exitWatcher = WatchForExitAsync();
    }

    /// <summary>The engine's operating-system process id, for logs.</summary>
    public int ProcessId { get; }

    /// <summary>
    /// False once the process has ended, the conversation has fallen out of
    /// step, or disposal has begun. An unusable engine stays unusable: dispose
    /// it and start another.
    /// </summary>
    public bool IsUsable => !_lifetime.IsCancellationRequested && !HasEnded && _fault is null;

    /// <summary>
    /// Completes as soon as the process is seen to have exited or closed its
    /// output, which is when <see cref="IsUsable"/> turns false for that
    /// reason, or once disposal has begun. It never fails. An owner can wait
    /// on it to replace an engine that dies while nobody is using it.
    /// </summary>
    public Task Ended => _ended.Task;

    private bool HasEnded => _end is not null || _outputClosed;

    /// <summary>
    /// Starts the engine and completes the handshake: <c>uci</c> until
    /// <c>uciok</c>, then <c>isready</c> until <c>readyok</c>.
    /// </summary>
    /// <remarks>
    /// If the handshake fails, the process is stopped before the exception
    /// reaches the caller, without waiting for a reply the engine still owed
    /// (unlike other calls, which read on to it). The engine inherits the
    /// application's environment variables.
    /// </remarks>
    /// <exception cref="Win32Exception">The program could not be started, for example because it does not exist.</exception>
    // LEARN: There is no `.ConfigureAwait(false)` after any `await` in this
    // project. It exists for code that runs under a SynchronizationContext,
    // such as a desktop UI thread, where each continuation would otherwise
    // be queued back onto that one thread. ASP.NET Core has no
    // SynchronizationContext: continuations always run on the thread pool, so
    // the call would change nothing. Libraries meant for any kind of
    // application still add it.
    public static async Task<UciEngine> StartAsync(
        UciEngineOptions options,
        ILogger<UciEngine> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        options.Validate();

        // UseShellExecute = false, the default in .NET, starts the program
        // directly. True would hand the file to the desktop to open with its
        // associated application (like java.awt.Desktop.open()), and allows
        // no redirection. ArgumentList passes each argument as-is, with no
        // shell, quoting or splitting.
        var startInfo = new ProcessStartInfo(options.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = options.WorkingDirectory ?? "",
        };
        foreach (var argument in options.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"No process was started for {options.ExecutablePath}.");
        var engine = new UciEngine(process, options, logger);
        LogStarted(logger, engine.ProcessId, options.ExecutablePath);

        try
        {
            await engine.ExchangeAsync(
                options.HandshakeTimeout,
                async exchange =>
                {
                    await exchange.SendAsync("uci");
                    await exchange.ReadReplyAsync("uciok");
                    await exchange.SendAsync("isready");
                    await exchange.ReadReplyAsync("readyok");
                    return true;
                },
                cancellationToken,
                // A failed handshake disposes the engine, so a reply it owes
                // is not worth waiting for.
                collectOwedReply: false);
            return engine;
        }
        catch
        {
            await engine.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Sets an engine option, then waits until the engine answers
    /// <c>isready</c>, so that the option is in effect before the next call.
    /// </summary>
    /// <param name="name">The option's name as the engine declares it, such as <c>Skill Level</c>.</param>
    /// <param name="value">Its value, or null for an option that is a button.</param>
    /// <param name="cancellationToken">
    /// Ends the call. If it is cancelled once the option has been sent, the
    /// engine's <c>readyok</c> is still read, within the stop timeout, before
    /// the call ends; if it does not come, the engine becomes unusable.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The name is blank or holds the word <c>name</c> or <c>value</c>, which
    /// the engine would read as part of the command; or the name or value holds
    /// a control character.
    /// </exception>
    // This method and SearchAsync are not `async`: an `async` method would
    // put an argument exception into the returned Task, so it would surface
    // only at the `await`. Without `async` it is thrown at the call.
    public Task SetOptionAsync(string name, string? value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        UciText.ThrowIfControlCharacter(name, nameof(name));
        if (UciText.HasToken(name, "name") || UciText.HasToken(name, "value"))
        {
            throw new ArgumentException("An option name cannot contain the word \"name\" or \"value\".", nameof(name));
        }

        if (value is not null)
        {
            UciText.ThrowIfControlCharacter(value, nameof(value));
        }

        var command = value is null
            ? $"setoption name {name.Trim()}"
            : $"setoption name {name.Trim()} value {value}";

        return ExchangeAsync(
            _options.ReadyTimeout,
            async exchange =>
            {
                await exchange.SendAsync(command);
                await exchange.SendAsync("isready");
                await exchange.ReadReplyAsync("readyok");
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Sends <c>isready</c> and waits, within the ready timeout, for
    /// <c>readyok</c>: a check that the engine is still answering.
    /// </summary>
    /// <param name="cancellationToken">
    /// Ends the call. If it is cancelled once <c>isready</c> has been sent,
    /// the <c>readyok</c> is still read, within the stop timeout, before the
    /// call ends; if it does not come, the engine becomes unusable.
    /// </param>
    public Task PingAsync(CancellationToken cancellationToken) =>
        ExchangeAsync(
            _options.ReadyTimeout,
            async exchange =>
            {
                await exchange.SendAsync("isready");
                await exchange.ReadReplyAsync("readyok");
                return true;
            },
            cancellationToken);

    /// <summary>Sends the position, searches it to the limit and returns the engine's answer.</summary>
    /// <remarks>
    /// <para>
    /// Each search starts with <c>isready</c> and reads to <c>readyok</c>:
    /// anything the engine printed before it, such as a stray extra
    /// <c>bestmove</c>, comes first and is passed over, so it cannot be taken
    /// as this search's answer. The search timeout covers that exchange too.
    /// </para>
    /// <para>
    /// The whole position goes with every search, so nothing carries over
    /// from an earlier one. Options are not reset: set them again before the
    /// search if another caller may have changed them.
    /// </para>
    /// </remarks>
    /// <exception cref="UciProtocolException">The engine answered <c>bestmove</c> with no move.</exception>
    public Task<SearchResult> SearchAsync(UciPosition position, SearchLimit limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(limit);

        return ExchangeAsync(
            _options.SearchTimeout,
            async exchange =>
            {
                await exchange.SendAsync("isready");
                await exchange.ReadReplyAsync("readyok");
                await exchange.SendAsync(position.Command);
                await exchange.SendAsync(limit.Command);
                return ToSearchResult(await exchange.ReadReplyAsync("bestmove"));
            },
            cancellationToken);
    }

    /// <summary>
    /// Sends <c>quit</c> and waits up to the exit timeout for the process to
    /// exit; if it is still running, kills it and every process it started.
    /// Safe to call more than once, from several threads, and after the
    /// process has exited; a call in progress ends with
    /// <see cref="ObjectDisposedException"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It takes at most the stop timeout plus five times the exit timeout
    /// (<see cref="UciEngineOptions.LongestDisposal"/>):
    /// waiting for a call in progress to finish; for <c>quit</c> to be written
    /// and the process to exit (two waits if the write finds the input
    /// closed); for the process to go after it is killed; for its output to
    /// close; and, if another process holds the output open, for the readers to
    /// finish after this side of the pipes is closed. An engine that obeys
    /// <c>quit</c> takes only as long as it needs to exit.
    /// </para>
    /// <para>
    /// A process the engine started and left running when it exited is not
    /// killed: once its parent has gone, it is no longer part of the engine's
    /// process tree, and .NET offers no portable way to find it. If it holds
    /// the engine's output open, this is normally noticed, logged as an error,
    /// and this side of the pipes is closed; if it does not, nothing here can
    /// notice it. Start engines
    /// directly, or from a wrapper script that runs the engine with
    /// <c>exec</c>, so that no such process exists.
    /// </para>
    /// </remarks>
    // LEARN: ValueTask is a struct, not a Task: it holds either a finished
    // result or a Task to wait for, so a method that usually completes at
    // once can return without allocating. Unlike a Task, a ValueTask may be
    // awaited only once, and must not be stored and awaited again. It is
    // returned here because IAsyncDisposable.DisposeAsync is declared that
    // way; every call wraps the same disposal Task.
    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposal ??= DisposeOnceAsync();
        }

        return new ValueTask(_disposal);
    }

    private async Task DisposeOnceAsync()
    {
        // Calls in progress stop waiting, and read the reply they owe, which
        // takes at most the stop timeout.
        await _lifetime.CancelAsync();
        _ended.TrySetResult();
        var entered = await _gate.WaitAsync(_options.StopTimeout);
        try
        {
            // Without the gate a call may still be writing, so a quit could
            // be interleaved with its command; the process is killed instead.
            if (entered && _end is null)
            {
                await QuitAsync();
            }

            await KillAsync();

            // The output reader may be waiting for room in the line buffer,
            // which nothing will now read; completing the buffer releases it.
            _lines.Writer.TryComplete();
            await WaitForReadersAsync();
        }
        finally
        {
            if (entered)
            {
                _gate.Release();
            }

            _process.Dispose();
            LogDisposed(_logger, ProcessId);
        }
    }

    private async Task QuitAsync()
    {
        using var deadline = new CancellationTokenSource(_options.ExitTimeout);
        try
        {
            await SendAsync("quit", deadline.Token);
            await _process.WaitForExitAsync(deadline.Token);
        }
        // LEARN: `catch (...) when (condition)` is an exception filter, which
        // Java does not have. The condition is tested before the stack
        // unwinds, and when it is false the exception is not caught at all,
        // so there is no catch-and-rethrow.
        catch (Exception e) when (e is OperationCanceledException or UciEngineExitedException)
        {
            // Either way the process is killed next if it is still running.
        }
    }

    private async Task KillAsync()
    {
        try
        {
            // Kill(entireProcessTree) finds the children through their
            // parent, so it can only reach them while the engine runs.
            if (_process.HasExited)
            {
                return;
            }

            _process.Kill(entireProcessTree: true);
            LogKilled(_logger, ProcessId);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or AggregateException)
        {
            LogKillFailed(_logger, e, ProcessId);
        }

        if (!await WaitForExitAsync())
        {
            LogStillRunning(_logger, ProcessId);
        }
    }

    private async Task WaitForReadersAsync()
    {
        if (await WaitUpToExitTimeoutAsync(Task.WhenAll(_outputReader, _errorReader, _exitWatcher)))
        {
            return;
        }

        // The engine has gone, but another process, one it started, still
        // holds its output open. Closing this side of the pipes ends the
        // readers; Process.Dispose does not, because the streams are read
        // directly rather than through its events.
        ReportOutputHeldOpen();
        _process.StandardOutput.Dispose();
        _process.StandardError.Dispose();
        if (!await WaitUpToExitTimeoutAsync(Task.WhenAll(_outputReader, _errorReader)))
        {
            LogReadersStillRunning(_logger, ProcessId);
        }
    }

    // Records the exit as soon as the process has gone. Lines it printed just
    // before are still on their way, and the output reader completes the line
    // buffer once they are read. If after the exit timeout the reader has not
    // finished, either a process the engine started holds the output open,
    // or the buffer is full of lines no call is reading; in both cases a call
    // would otherwise wait for its reply until its timeout, so the buffer is
    // completed here.
    private async Task WatchForExitAsync()
    {
        try
        {
            await _process.WaitForExitAsync(_lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // Disposal is under way and waits for the exit itself.
            return;
        }

        RecordEnd(_process.ExitCode);
        if (!await WaitUpToExitTimeoutAsync(_outputReader))
        {
            // A full buffer means the reader is waiting for room, not for the pipe.
            if (_lines.Reader.Count < LineBufferCapacity)
            {
                ReportOutputHeldOpen();
            }

            _lines.Writer.TryComplete();
        }
    }

    // Logged once, by whichever notices first: the exit watcher or disposal.
    private void ReportOutputHeldOpen()
    {
        if (Interlocked.Exchange(ref _outputHeldOpenReported, 1) == 0)
        {
            LogOutputHeldOpen(_logger, ProcessId);
        }
    }

    private void RecordEnd(int? exitCode)
    {
        Interlocked.CompareExchange(ref _end, new ProcessEnd(exitCode), null);
        _ended.TrySetResult();
    }

    // Waits up to the exit timeout for the process to exit; false if it has not.
    private async Task<bool> WaitForExitAsync()
    {
        using var deadline = new CancellationTokenSource(_options.ExitTimeout);
        try
        {
            await _process.WaitForExitAsync(deadline.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    // Waits up to the exit timeout for a task that never fails; false if it
    // has not finished by then.
    private async Task<bool> WaitUpToExitTimeoutAsync(Task task)
    {
        try
        {
            // LEARN: Task.WaitAsync(timeout) returns a new task that fails
            // with TimeoutException if the first has not finished in time; the
            // first carries on regardless. CompletableFuture.orTimeout() looks
            // similar but fails the future itself.
            await task.WaitAsync(_options.ExitTimeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // Runs one exchange with the engine, holding the gate. If the exchange
    // ends early while a reply is owed, that reply would later be mistaken
    // for the reply to another command, so it is collected first.
    // `collectOwedReply: false` is only for an exchange whose failure
    // disposes the engine (the handshake): waiting for a reply nobody will use
    // would only delay that failure, so the conversation is marked out of
    // step instead.
    private async Task<T> ExchangeAsync<T>(
        TimeSpan timeout,
        Func<Exchange, Task<T>> conversation,
        CancellationToken cancellationToken,
        bool collectOwedReply = true)
    {
        await EnterAsync(cancellationToken);
        try
        {
            ThrowIfUnusable();
            using var deadline = new CancellationTokenSource(timeout);
            using var reply = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token, _lifetime.Token);
            var exchange = new Exchange(this, reply.Token, deadline.Token);
            try
            {
                reply.Token.ThrowIfCancellationRequested();
                return await conversation(exchange);
            }
            catch (OperationCanceledException) when (reply.IsCancellationRequested)
            {
                if (!HasEnded && exchange.Awaiting is { } owed)
                {
                    if (collectOwedReply)
                    {
                        await CollectOwedReplyAsync(owed);
                    }
                    else
                    {
                        _fault ??= $"it stopped waiting for {owed}";
                    }
                }

                throw Interrupted(exchange.Awaiting, timeout, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_lifetime.IsCancellationRequested, this);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await _gate.WaitAsync(wait.Token);
        }
        catch (OperationCanceledException)
        {
            throw cancellationToken.IsCancellationRequested
                ? new OperationCanceledException(cancellationToken)
                : new ObjectDisposedException(nameof(UciEngine));
        }
    }

    // Reads the reply a call gave up on, which answers a command the caller
    // no longer wants and is dropped. A search still running is ended with
    // stop first. If the reply does not come in time, the conversation is out
    // of step.
    private async Task CollectOwedReplyAsync(string owed)
    {
        using var deadline = new CancellationTokenSource(_options.StopTimeout);
        try
        {
            if (owed == "bestmove")
            {
                await SendAsync("stop", deadline.Token);
            }

            await ReadReplyAsync(owed, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            _fault ??= $"it did not answer with the {owed} it owed in time";
        }
    }

    // The exception for an exchange that ended early, by its cause: a dead
    // process first, since whoever owns the engine must replace it; then the
    // caller's own cancellation; then disposal; and otherwise the timeout.
    private Exception Interrupted(string? awaited, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (HasEnded)
        {
            return EndedException();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new OperationCanceledException(cancellationToken);
        }

        return _lifetime.IsCancellationRequested
            ? new ObjectDisposedException(nameof(UciEngine))
            : new UciEngineTimeoutException(awaited, timeout, commandCutOff: _fault is not null);
    }

    private void ThrowIfUnusable()
    {
        ObjectDisposedException.ThrowIf(_lifetime.IsCancellationRequested, this);
        if (HasEnded)
        {
            throw EndedException();
        }

        if (_fault is { } fault)
        {
            throw new InvalidOperationException($"The engine is out of step with this client because {fault}. Dispose it and start another.");
        }
    }

    private UciEngineExitedException EndedException() =>
        _end is { } end ? UciEngineExitedException.Exited(end.ExitCode) : UciEngineExitedException.ClosedOutput();

    private async Task SendAsync(string command, CancellationToken cancellationToken)
    {
        CommandWriteOutcome outcome;
        try
        {
            outcome = await CommandWriter.WriteAsync(_process.StandardInput, command, cancellationToken);
        }
        catch (IOException)
        {
            // The pipe is closed: the process has exited, or has closed its
            // input while it runs. Either way the engine cannot be used. This
            // wait is bounded by the exit timeout, not by the caller's token.
            if (await WaitForExitAsync())
            {
                RecordEnd(_process.ExitCode);
            }

            _fault ??= "it closed its input";
            throw _end is { } end ? UciEngineExitedException.Exited(end.ExitCode) : UciEngineExitedException.ClosedInput();
        }

        switch (outcome)
        {
            case CommandWriteOutcome.Written:
                LogSent(_logger, ProcessId, command);
                return;

            case CommandWriteOutcome.CutOff:
                // Part of the command may already be in the pipe, and the
                // next command would be read as its continuation.
                _fault ??= "a command was cut off while it was being written";
                break;

            case CommandWriteOutcome.NotStarted:
            default:
                // Time ran out before anything was written: the conversation
                // is still in step.
                break;
        }

        throw new OperationCanceledException(cancellationToken);
    }

    // Reads lines until one starts with the awaited reply, and returns its
    // tokens. Every other line (info, id, option, anything unknown) is not
    // this wait's business and is skipped.
    private async Task<string[]> ReadReplyAsync(string reply, CancellationToken cancellationToken)
    {
        while (true)
        {
            string line;
            try
            {
                line = await _lines.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                // The output closed, or the exit watcher gave up on it.
                throw EndedException();
            }

            var tokens = UciText.Tokens(line);

            // LEARN: `tokens is ["info", "string", ..]` is a list pattern: it
            // matches an array whose first two elements are those strings,
            // followed by any number of others (`..`). Java's patterns cannot
            // take arrays or lists apart this way.
            if (tokens is ["info", "string", ..])
            {
                // Free text for people; never a reply, whatever it says.
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var text = UciText.ForLog(line);
                    LogInfoString(_logger, ProcessId, text);
                }

                continue;
            }

            if (tokens is [var first, ..] && first == reply)
            {
                return tokens;
            }
        }
    }

    private static SearchResult ToSearchResult(string[] tokens) => tokens switch
    {
        ["bestmove", "(none)" or "0000", ..] => new NoLegalMove(),
        ["bestmove", var move, ..] => new BestMove(move),
        _ => throw new UciProtocolException("The engine answered bestmove with no move."),
    };

    // Turns the engine's standard output into lines on the channel, for as
    // long as the process keeps it open.
    private async Task ReadOutputAsync()
    {
        var budget = new LogBudget(LogEntriesPerWindow, LogWindow);
        int? exitCode = null;
        try
        {
            var reader = new BoundedLineReader(_process.StandardOutput, MaxLineLength);
            while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
            {
                if (line.IsTruncated)
                {
                    LogLineTooLong(budget, line.Text[..LoggedLineStart]);
                    continue;
                }

                await _lines.Writer.WriteAsync(line.Text);
            }

            _outputClosed = true;
            _ended.TrySetResult();
            exitCode = await WaitForExitAsync() ? _process.ExitCode : null;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or ChannelClosedException)
        {
            // Disposal closed the pipe or the buffer, or the exit watcher
            // completed the buffer because another process held the output.
        }
        finally
        {
            RecordEnd(exitCode);
            _lines.Writer.TryComplete();
            LogSuppressed(_logger, ProcessId, "over-long output", budget.TakeSuppressed());
            LogOutputEnded(_logger, ProcessId, _end?.ExitCode);
        }
    }

    private void LogLineTooLong(LogBudget budget, string start)
    {
        if (budget.TryTake(out var suppressed))
        {
            LogSuppressed(_logger, ProcessId, "over-long output", suppressed);
            var text = UciText.ForLog(start);
            LogLineTooLong(_logger, ProcessId, MaxLineLength, text);
        }
    }

    // Standard error is for people: it goes to the log and is never parsed.
    private async Task ReadStandardErrorAsync()
    {
        var budget = new LogBudget(LogEntriesPerWindow, LogWindow);
        try
        {
            var reader = new BoundedLineReader(_process.StandardError, MaxLineLength);
            while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
            {
                if (budget.TryTake(out var suppressed) && _logger.IsEnabled(LogLevel.Information))
                {
                    LogSuppressed(_logger, ProcessId, "standard error", suppressed);
                    var text = UciText.ForLog(line.Text);
                    LogStandardError(_logger, ProcessId, text, line.IsTruncated);
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Disposal closed the pipe.
        }
        finally
        {
            LogSuppressed(_logger, ProcessId, "standard error", budget.TakeSuppressed());
        }
    }

    private static void LogSuppressed(ILogger logger, int processId, string stream, int count)
    {
        if (count > 0)
        {
            LogLinesSuppressed(logger, processId, count, stream);
        }
    }

    private sealed record ProcessEnd(int? ExitCode);

    // One exchange's view of the conversation. It binds each operation to
    // its token: replies are read with a token that the caller, the timeout
    // or disposal cancels; commands are written with one that only the
    // timeout cancels, so that a command is never cut off half-written
    // because the caller gave up. It also records which reply is owed.
    private sealed class Exchange(UciEngine engine, CancellationToken reply, CancellationToken write)
    {
        /// <summary>The reply being waited for (<c>uciok</c>, <c>readyok</c> or <c>bestmove</c>), or null between waits.</summary>
        public string? Awaiting { get; private set; }

        public Task SendAsync(string command) => engine.SendAsync(command, write);

        public async Task<string[]> ReadReplyAsync(string expected)
        {
            Awaiting = expected;
            var tokens = await engine.ReadReplyAsync(expected, reply);
            Awaiting = null;
            return tokens;
        }
    }

    // LEARN: [LoggerMessage] asks a source generator, a compiler plug-in, to
    // write the body of each partial method below at build time. The
    // generated code checks the log level before formatting anything and
    // keeps the message's named placeholders as structured fields, much as
    // SLF4J's parameterised messages do, without boxing the arguments.
    [LoggerMessage(Level = LogLevel.Information, Message = "Started engine process {ProcessId} from {ExecutablePath}")]
    private static partial void LogStarted(ILogger logger, int processId, string executablePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Engine {ProcessId} <- {Command}")]
    private static partial void LogSent(ILogger logger, int processId, string command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Engine {ProcessId} info string: {Line}")]
    private static partial void LogInfoString(ILogger logger, int processId, string line);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Engine {ProcessId} printed a line longer than {MaxLength} characters, which was skipped. It began: {Start}")]
    private static partial void LogLineTooLong(ILogger logger, int processId, int maxLength, string start);

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine {ProcessId} stderr: {Line} (truncated: {IsTruncated})")]
    private static partial void LogStandardError(ILogger logger, int processId, string line, bool isTruncated);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Engine {ProcessId}: {Count} lines of {Stream} were not logged")]
    private static partial void LogLinesSuppressed(ILogger logger, int processId, int count, string stream);

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine {ProcessId} closed its output; exit code {ExitCode}")]
    private static partial void LogOutputEnded(ILogger logger, int processId, int? exitCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Engine {ProcessId} was still running at disposal and was killed with its child processes")]
    private static partial void LogKilled(ILogger logger, int processId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Engine {ProcessId} could not be killed")]
    private static partial void LogKillFailed(ILogger logger, Exception exception, int processId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Engine {ProcessId} was still running after it was killed")]
    private static partial void LogStillRunning(ILogger logger, int processId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Engine {ProcessId} has exited, but a process it started still holds its output open and was not killed")]
    private static partial void LogOutputHeldOpen(ILogger logger, int processId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Engine {ProcessId}: its output readers did not finish after the pipes were closed")]
    private static partial void LogReadersStillRunning(ILogger logger, int processId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Engine {ProcessId} disposed")]
    private static partial void LogDisposed(ILogger logger, int processId);
}
