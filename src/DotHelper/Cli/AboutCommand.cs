using System.Reflection;

using DotHelper.Ui;

using Spectre.Console;
using Spectre.Console.Cli;

namespace DotHelper.Cli;

public sealed class AboutCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        OutputChannel.WriteLine(AnsiConsole.Console, $"DotHelper {version}");
        OutputChannel.WriteLine(AnsiConsole.Console, "Interactive .NET project helper for the terminal");
        return 0;
    }
}