using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DotHelper.Core.Dotnet;

/// <summary>
/// Invokes the <c>dotnet</c> CLI via <see cref="Process"/> (PLAN.md §6a).
/// Always forces <c>DOTNET_CLI_UI_LANGUAGE=en</c> in the child environment,
/// logs to a file (never to the console, which would break a TUI), and supports
/// dry-run and verbose modes.
/// </summary>
public sealed class DotnetRunner : IDotnetRunner
{
    private readonly DotnetRunnerOptions _options;
    private readonly string _logDirectory;

    public DotnetRunner(DotnetRunnerOptions? options = null)
    {
        _options = options ?? new DotnetRunnerOptions();
        _logDirectory = _options.LogDirectory ?? DefaultLogDirectory();
    }

    /// <summary><c>~/.local/state/dothelper/logs</c> per PLAN.md §2 / §6a.</summary>
    public static string DefaultLogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "state",
            "dothelper",
            "logs");

    public async Task<DotnetResult> RunAsync(
        IEnumerable<string> args,
        string? workingDir = null,
        CancellationToken cancellationToken = default,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        List<string> argList = args as List<string> ?? args.ToList();
        string commandLine = BuildCommandLine(argList);
        string workDir = string.IsNullOrWhiteSpace(workDir = workingDir ?? Environment.CurrentDirectory)
            ? Environment.CurrentDirectory
            : workDir;

        if (_options.DryRun)
        {
            Log($"DRY-RUN {commandLine} | cwd={workDir}");
            return new DotnetResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty,
                CommandLine = commandLine,
                DryRun = true,
            };
        }

        Log($"RUN {commandLine} | cwd={workDir}");

        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string arg in argList)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        // PLAN.md §6a: locale pinned so parsed output is stable.
        process.StartInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

        process.Start();

        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state =>
            {
                Process p = (Process)state!;
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between the check and the kill.
                }
                catch (Win32Exception)
                {
                    // Best-effort kill; the wait below still observes cancellation.
                }
            },
            process);

        StringBuilder stdOut = new();
        StringBuilder stdErr = new();

        Task pumpOut = PumpAsync(process.StandardOutput, stdOut, onStdOutLine, cancellationToken);
        Task pumpErr = PumpAsync(process.StandardError, stdErr, onStdErrLine, cancellationToken);

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await Task.WhenAll(pumpOut, pumpErr).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        string stdout = stdOut.ToString();
        string stderr = stdErr.ToString();
        int exitCode = process.ExitCode;

        Log($"EXIT {commandLine} | code={exitCode} | out={stdout.Length} chars | err={stderr.Length} chars");
        if (_options.Verbose || exitCode != 0)
        {
            if (stdout.Length > 0)
            {
                Log($"STDOUT | {Truncate(stdout)}");
            }

            if (stderr.Length > 0)
            {
                Log($"STDERR | {Truncate(stderr)}");
            }
        }

        return new DotnetResult
        {
            ExitCode = exitCode,
            StdOut = stdout,
            StdErr = stderr,
            CommandLine = commandLine,
            DryRun = false,
        };
    }

    private static async Task PumpAsync(
        TextReader reader,
        StringBuilder sink,
        Action<string>? onLine,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            sink.AppendLine(line);
            onLine?.Invoke(line);
        }
    }

    private static string BuildCommandLine(List<string> args)
    {
        StringBuilder sb = new("dotnet");
        foreach (string arg in args)
        {
            sb.Append(' ');
            sb.Append(QuoteArg(arg));
        }

        return sb.ToString();
    }

    private static string QuoteArg(string arg)
    {
        bool needsQuotes = arg.Length == 0 || arg.Any(static c => char.IsWhiteSpace(c) || c == '"');
        if (!needsQuotes)
        {
            return arg;
        }

        return "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string Truncate(string value) =>
        value.Length <= 2000 ? value : value[..2000] + "…";

    /// <summary>
    /// Best-effort append-only file log. Logging must never fail the tool.
    /// The file is named after the <b>local</b> date (so an evening session lands in that day's
    /// file) while the timestamps inside stay in UTC for unambiguous ordering — documented
    /// decision (Fase 6).
    /// </summary>
    private void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            string path = Path.Combine(_logDirectory, $"dothelper-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(path, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Best-effort logging.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort logging.
        }
    }
}