using System.Diagnostics;

namespace DotHelper.Ui;

/// <summary>Outcome of a best-effort clipboard copy (PLAN.md §6e <c>--print-cmd</c>).</summary>
public enum ClipboardStatus
{
    /// <summary>Text is in the clipboard.</summary>
    Copied,

    /// <summary>No supported tool (<c>wl-copy</c>/<c>pbcopy</c>) is available.</summary>
    NoTool,

    /// <summary>A tool was found but the copy failed (e.g. no Wayland session).</summary>
    Failed,
}

/// <summary>Result of a clipboard attempt; the tool and error are informational.</summary>
public sealed record ClipboardResult(ClipboardStatus Status, string? Tool, string? Error)
{
    public bool Copied => Status == ClipboardStatus.Copied;
}

/// <summary>
/// Best-effort clipboard integration: pipes the text into <c>wl-copy</c> (Wayland) or
/// <c>pbcopy</c> (macOS), whichever is available first. Never throws — the clipboard is a
/// convenience on top of the always-printed equivalent <c>dotnet</c> command.
/// </summary>
public static class Clipboard
{
    /// <summary>Supported tools in priority order.</summary>
    public static readonly string[] CandidateTools = ["wl-copy", "pbcopy"];

    /// <summary>
    /// Pure: returns the first candidate present in <paramref name="availableTools"/>, or
    /// <c>null</c> when none is available.
    /// </summary>
    public static string? SelectTool(IEnumerable<string>? availableTools)
    {
        if (availableTools is null)
        {
            return null;
        }

        HashSet<string> available = new(availableTools, StringComparer.Ordinal);
        foreach (string candidate in CandidateTools)
        {
            if (available.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Copies <paramref name="text"/> using the first available tool. Never throws.</summary>
    public static ClipboardResult TryCopy(string text) =>
        TryCopy(text, IsOnPath, PipeToTool);

    /// <summary>Injectable core: <paramref name="toolExists"/> probes PATH, <paramref name="write"/> pipes the text.</summary>
    internal static ClipboardResult TryCopy(
        string text,
        Func<string, bool> toolExists,
        Func<string, string, bool> write)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(toolExists);
        ArgumentNullException.ThrowIfNull(write);

        string? tool = SelectTool(CandidateTools.Where(toolExists));
        if (tool is null)
        {
            return new ClipboardResult(ClipboardStatus.NoTool, null, null);
        }

        try
        {
            return write(tool, text)
                ? new ClipboardResult(ClipboardStatus.Copied, tool, null)
                : new ClipboardResult(ClipboardStatus.Failed, tool, "tool exited with a non-zero code");
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Best-effort: a broken clipboard (e.g. wl-copy without a Wayland session) must
            // never break the flow that requested the copy.
            return new ClipboardResult(ClipboardStatus.Failed, tool, ex.Message);
        }
    }

    /// <summary>True when <paramref name="tool"/> resolves inside one of the PATH directories.</summary>
    private static bool IsOnPath(string tool)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(dir, tool)))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry: skip.
            }
        }

        return false;
    }

    /// <summary>Pipes <paramref name="text"/> into the tool's stdin; true when it exits 0.</summary>
    private static bool PipeToTool(string tool, string text)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = tool,
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        process.Start();
        process.StandardInput.Write(text);
        process.StandardInput.Close();
        return process.WaitForExit(milliseconds: 5000) && process.ExitCode == 0;
    }
}