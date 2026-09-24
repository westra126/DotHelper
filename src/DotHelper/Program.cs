using DotHelper.Cli;

using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("dh");
    config.Settings.ApplicationVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    config.AddCommand<AboutCommand>("about")
        .WithDescription("Show DotHelper version and information");

    config.AddBranch("list", list =>
    {
        list.AddCommand<ListTemplatesCommand>("templates")
            .WithDescription("List available dotnet new templates");
    });
});

return app.Run(args);