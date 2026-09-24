using System.Reflection;

using Spectre.Console;
using Spectre.Console.Cli;

namespace DotHelper.Cli;

public sealed class AboutCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        AnsiConsole.WriteLine($"DotHelper {version}");
        AnsiConsole.WriteLine("Interactive .NET project helper for the terminal");
        return 0;
    }
}