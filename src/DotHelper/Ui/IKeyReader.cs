namespace DotHelper.Ui;

/// <summary>
/// Source of key presses for <see cref="FuzzyPicker{T}"/>. Injectable so tests can feed a
/// key sequence without a TTY (PLAN.md Fase 5); the default reads the real console.
/// </summary>
public interface IKeyReader
{
    /// <summary>Reads the next key press (not echoed to the console).</summary>
    ConsoleKeyInfo ReadKey();
}

/// <summary>Default reader over <see cref="Console.ReadKey(bool)"/> with <c>intercept: true</c>.</summary>
public sealed class ConsoleKeyReader : IKeyReader
{
    /// <summary>Shared stateless instance.</summary>
    public static ConsoleKeyReader Instance { get; } = new();

    public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);
}

/// <summary>
/// Reader over a fixed key sequence, for tests and scripted runs. Throws when exhausted so a
/// test that forgets a key fails loudly instead of hanging.
/// </summary>
public sealed class ScriptedKeyReader : IKeyReader
{
    private readonly Queue<ConsoleKeyInfo> _keys;

    public ScriptedKeyReader(IEnumerable<ConsoleKeyInfo> keys)
    {
        _keys = new Queue<ConsoleKeyInfo>(keys ?? throw new ArgumentNullException(nameof(keys)));
    }

    /// <summary>Convenience factory from characters and special keys.</summary>
    public static ScriptedKeyReader From(params object[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        List<ConsoleKeyInfo> mapped = new(keys.Length);
        foreach (object key in keys)
        {
            mapped.Add(key switch
            {
                char c => new ConsoleKeyInfo(c, MapKey(c), false, false, false),
                ConsoleKey k => new ConsoleKeyInfo('\0', k, false, false, false),
                ConsoleKeyInfo info => info,
                _ => throw new ArgumentException($"Unsupported key descriptor '{key}'.", nameof(keys)),
            });
        }

        return new ScriptedKeyReader(mapped);
    }

    public ConsoleKeyInfo ReadKey() =>
        _keys.Count > 0
            ? _keys.Dequeue()
            : throw new InvalidOperationException("ScriptedKeyReader: no more keys in the sequence.");

    private static ConsoleKey MapKey(char c) =>
        char.IsLetter(c)
            ? (ConsoleKey)char.ToUpperInvariant(c)
            : c == ' ' ? ConsoleKey.Spacebar : ConsoleKey.NoName;
}