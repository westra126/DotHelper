using DotHelper.Cli;
using DotHelper.Core;

using Spectre.Console.Cli;

// Ctrl+C interrupts at any moment (user report): cancel the process-wide token instead of
// killing the process with a stack trace. Flows and DotnetRunner observe it (the runner kills
// the whole `dotnet` tree), ScreenSession restores the primary screen, and the global handler
// reports "Cancelled." with exit code 130. Pickers/prompts treat Ctrl+C as a key and follow
// the same contract (see AppInterrupt).
AppInterrupt.Wire();

var app = new CommandApp();

// Fase 5: `dh` without arguments runs the root wizard (PLAN.md §5.2/§5.3). Registered through
// Spectre.Console.Cli 0.55 `CommandApp.SetDefaultCommand<T>()`; explicit commands/branches keep
// their routing. Without a TTY the wizard falls back to this same help screen.
app.SetDefaultCommand<RootWizardCommand>()
    .WithDescription("Interactive wizard: pick an action with fuzzy search");
RootWizardCommand.HelpFallback = () => app.Run(new[] { "--help" });

app.Configure(config =>
{
    config.SetApplicationName("dh");
    config.Settings.ApplicationVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    config.Settings.CancellationExitCode = 130;

    // Fase 6 review M2: one global handler for every command (and the wizard dispatcher):
    // cancellations exit 130, everything else prints a friendly Error line and exits 1.
    config.SetExceptionHandler((exception, _) => CommandErrors.Handle(exception));

    config.AddCommand<AboutCommand>("about")
        .WithDescription("Show DotHelper version and information");

    config.AddCommand<ItemCommand>("item")
        .WithDescription("Create a new item (class, record, interface, ...) inside a project");

    config.AddCommand<ClassCommand>("class")
        .WithDescription("Create a new class (alias of 'dh item' pre-loading the class query)");

    config.AddBranch("new", nw =>
    {
        nw.SetDescription("Create a new solution, project or item");
        nw.AddCommand<NewSolutionCommand>("solution")
            .WithDescription("Create a new solution");
        nw.AddCommand<NewProjectCommand>("project")
            .WithDescription("Create a new project from a template");
        nw.AddCommand<ItemCommand>("item")
            .WithDescription("Create a new item (class, record, interface, ...) inside a project");
        nw.AddCommand<ClassCommand>("class")
            .WithDescription("Create a new class (alias of 'dh new item' pre-loading the class query)");
    });

    config.AddBranch("sln", sln =>
    {
        sln.SetDescription("Manage the projects of the active solution");
        sln.AddCommand<SlnListCommand>("list")
            .WithDescription("List projects of the active solution");
        sln.AddCommand<SlnAddCommand>("add")
            .WithDescription("Add a project to the active solution");
        sln.AddCommand<SlnRemoveCommand>("remove")
            .WithDescription("Remove a project from the active solution");
    });

    config.AddBranch("project", proj =>
    {
        proj.SetDescription("List projects and wire project references");
        proj.AddCommand<ProjectListCommand>("list")
            .WithDescription("List projects of the active solution");
        proj.AddCommand<ProjectAddRefCommand>("add-ref")
            .WithDescription("Add a project reference");
        proj.AddCommand<ProjectRemoveRefCommand>("remove-ref")
            .WithDescription("Remove a project reference");
    });

    config.AddBranch("nuget", nuget =>
    {
        nuget.SetDescription("Search, add, remove and list NuGet packages");
        nuget.AddCommand<NugetSearchCommand>("search")
            .WithDescription("Search packages on nuget.org");
        nuget.AddCommand<NugetAddCommand>("add")
            .WithDescription("Add a package reference to a project");
        nuget.AddCommand<NugetRemoveCommand>("remove")
            .WithDescription("Remove a package reference from a project");
        nuget.AddCommand<NugetListCommand>("list")
            .WithDescription("List the package references of a project");
    });

    config.AddBranch("list", list =>
    {
        list.SetDescription("List templates, projects and solutions");
        list.AddCommand<ListTemplatesCommand>("templates")
            .WithDescription("List available dotnet new templates");
        list.AddCommand<ListSolutionsCommand>("solutions")
            .WithDescription("List solution files under the current directory");
        list.AddCommand<ListProjectsCommand>("projects")
            .WithDescription("List projects of the active solution");
    });
});

return app.Run(args);