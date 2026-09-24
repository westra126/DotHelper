using System.Reflection;

using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("dh");
    config.Settings.ApplicationVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    config.AddCommand<AboutCommand>("about")
        .WithDescription("Show DotHelper version and information");
});

return app.Run(args);

internal sealed class AboutCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        AnsiConsole.WriteLine($"DotHelper {version}");
        AnsiConsole.WriteLine("Interactive .NET project helper for the terminal");
        return 0;
    }
}