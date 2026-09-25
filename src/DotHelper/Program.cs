using DotHelper.Cli;

using Spectre.Console.Cli;

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

    config.AddCommand<AboutCommand>("about")
        .WithDescription("Show DotHelper version and information");

    config.AddCommand<ItemCommand>("item")
        .WithDescription("Create a new item (class, record, interface, ...) inside a project");

    config.AddCommand<ClassCommand>("class")
        .WithDescription("Create a new class (alias of 'dh item' pre-loading the class query)");

    config.AddBranch("new", nw =>
    {
        nw.SetDescription("Create a new solution or project");
        nw.AddCommand<NewSolutionCommand>("solution")
            .WithDescription("Create a new solution");
        nw.AddCommand<NewProjectCommand>("project")
            .WithDescription("Create a new project from a template");
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