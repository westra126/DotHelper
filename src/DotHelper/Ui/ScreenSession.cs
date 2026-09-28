using Spectre.Console;

namespace DotHelper.Ui;

/// <summary>
/// Capability gate of a <see cref="ScreenSession"/> (pure, testable): the alternate screen is
/// entered only when the real console drives the interaction on a terminal that actually
/// supports ANSI alternate buffers. Everything else — redirected stdin, piped stdout
/// (<c>--json</c> pipes), <c>--yes</c> runs, <see cref="IKeyReader"/> driven tests (TestConsole
/// reports <c>Interactive = false</c>) — is a total no-op: zero escape sequences.
/// </summary>
internal readonly record struct ScreenGate(bool InputRedirected, bool Ansi, bool AlternateBuffer, bool Interactive)
{
    /// <summary>
    /// True only when nothing has redirected stdin, the caller reads the real console (no
    /// injected <see cref="IKeyReader"/>), and the terminal supports interactive alternate
    /// buffers.
    /// </summary>
    internal static bool ShouldEnter(
        bool inputRedirected,
        bool hasInjectedKeyReader,
        bool ansi,
        bool alternateBuffer,
        bool interactive) =>
        !inputRedirected && !hasInjectedKeyReader && ansi && alternateBuffer && interactive;

    /// <summary>Applies the gate for a key source (an injected reader never touches the buffer).</summary>
    public bool CanEnter(bool hasInjectedKeyReader) =>
        ShouldEnter(InputRedirected, hasInjectedKeyReader, Ansi, AlternateBuffer, Interactive);

    /// <summary>Gate computed from the real process streams and <paramref name="console"/> capabilities.</summary>
    public static ScreenGate For(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        return new ScreenGate(
            Console.IsInputRedirected,
            console.Profile.Capabilities.Ansi,
            console.Profile.Capabilities.AlternateBuffer,
            console.Profile.Capabilities.Interactive);
    }

    /// <summary>Test seam: a fully capable interactive terminal.</summary>
    public static ScreenGate Capable =>
        new(InputRedirected: false, Ansi: true, AlternateBuffer: true, Interactive: true);
}

/// <summary>
/// Fullscreen session (user reports 1–3): exactly ONE alternate-screen entry per invocation.
/// The wizard/command wraps its whole interactive phase in <see cref="Run(IAnsiConsole, Action)"/>
/// (or the async overload); pickers and prompts then only <em>ensure</em> the screen is open
/// (<see cref="EnsureOpen"/>) on their first frame, so the menu, sub-menus, prompts and the
/// dispatched flows all live inside the same <c>?1049h</c>…<c>?1049l</c> pair. Reentrant: nested
/// scopes are depth-counted no-ops.
///
/// While the session is open, result messages must not be written to the alternate screen —
/// they would vanish when the primary screen is restored. They are deferred through
/// <see cref="OutputChannel"/> and flushed after the exit sequence (see <see cref="End"/>).
/// Pickers and prompts are interaction and write directly.
/// </summary>
public static class ScreenSession
{
    // Same byte sequences Spectre.Console 0.55 `AlternateScreen` emits (verified against the
    // upstream source and a probe): enter = alt buffer + cursor home, leave = primary buffer.
    private const string EnterSequence = "\u001b[?1049h\u001b[H";
    private const string ExitSequence = "\u001b[?1049l";

    private static int _depth;
    private static bool _entered;
    private static IAnsiConsole? _screen;
    private static ScreenGate _gate;
    private static readonly List<(IAnsiConsole Console, Action<IAnsiConsole> Write)> _pending = [];

    /// <summary>
    /// True while the alternate screen is open: result writes go to the deferred buffer
    /// instead of the console.
    /// </summary>
    public static bool IsActive => _entered;

    /// <summary>Runs <paramref name="body"/> inside a fullscreen session scope.</summary>
    public static void Run(IAnsiConsole console, Action body)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(body);

        Run(console, () =>
        {
            body();
            return true;
        }, ScreenGate.For(console));
    }

    /// <summary>Runs <paramref name="body"/> inside a fullscreen session scope.</summary>
    public static T Run<T>(IAnsiConsole console, Func<T> body)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(body);

        return Run(console, body, ScreenGate.For(console));
    }

    /// <summary>Async twin of <see cref="Run{T}(IAnsiConsole, Func{T})"/> for the command flows.</summary>
    public static Task<T> RunAsync<T>(IAnsiConsole console, Func<Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(body);

        return RunAsync(console, body, ScreenGate.For(console));
    }

    /// <summary>Scope with an explicit gate (tests force <see cref="ScreenGate.Capable"/>).</summary>
    internal static void Run(IAnsiConsole console, Action body, ScreenGate gate)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(body);

        Run(console, () =>
        {
            body();
            return true;
        }, gate);
    }

    /// <summary>Scope with an explicit gate (tests force <see cref="ScreenGate.Capable"/>).</summary>
    internal static T Run<T>(IAnsiConsole console, Func<T> body, ScreenGate gate)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(body);

        bool outermost = Begin(gate);
        try
        {
            return body();
        }
        finally
        {
            End(outermost);
        }
    }

    /// <summary>Scope with an explicit gate (tests force <see cref="ScreenGate.Capable"/>).</summary>
    internal static async Task<T> RunAsync<T>(IAnsiConsole console, Func<Task<T>> body, ScreenGate gate)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(body);

        bool outermost = Begin(gate);
        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            End(outermost);
        }
    }

    /// <summary>
    /// Opens the alternate screen on the first interactive frame inside an active scope.
    /// Reentrant: no-op when already open. Total no-op without an active scope, with an
    /// injected <see cref="IKeyReader"/> or when the gate rejects the terminal.
    /// </summary>
    internal static void EnsureOpen(IAnsiConsole console, bool hasInjectedKeyReader)
    {
        ArgumentNullException.ThrowIfNull(console);

        if (_depth == 0 || _entered || !_gate.CanEnter(hasInjectedKeyReader))
        {
            return;
        }

        console.WriteAnsi(EnterSequence);
        _screen = console;
        _entered = true;
    }

    /// <summary>Buffers a result write until the outermost session closes (see <see cref="OutputChannel"/>).</summary>
    internal static void Defer(IAnsiConsole console, Action<IAnsiConsole> write) =>
        _pending.Add((console, write));

    private static bool Begin(ScreenGate gate)
    {
        bool outermost = _depth == 0;
        if (outermost)
        {
            _gate = gate;
        }

        _depth++;
        return outermost;
    }

    private static void End(bool outermost)
    {
        _depth--;
        if (!outermost)
        {
            return;
        }

        // Restore the primary screen FIRST: deferred result messages belong there, after it.
        if (_entered && _screen is not null)
        {
            _screen.WriteAnsi(ExitSequence);
        }

        _entered = false;
        _screen = null;

        foreach ((IAnsiConsole console, Action<IAnsiConsole> write) in _pending)
        {
            write(console);
        }

        _pending.Clear();
    }
}