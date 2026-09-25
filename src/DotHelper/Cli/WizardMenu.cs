using DotHelper.Ui;

namespace DotHelper.Cli;

/// <summary>
/// One entry of the root wizard menu (PLAN.md §5.2 / §5.3). A branch entry carries
/// <see cref="Children"/>; a leaf is executed by <see cref="WizardDispatcher"/>.
/// </summary>
public sealed record WizardItem
{
    public required string Id { get; init; }

    /// <summary>Menu label (Spanish, per PLAN.md §5.2).</summary>
    public required string Title { get; init; }

    /// <summary>One-line explanation shown in the picker detail (Tab).</summary>
    public required string Description { get; init; }

    /// <summary>Equivalent CLI command shown in the picker detail (PLAN.md §5.3 transparency).</summary>
    public required string Command { get; init; }

    /// <summary>Extra searchable words (aliases, English synonyms).</summary>
    public required string[] Keywords { get; init; }

    /// <summary>Sub-menu entries; empty for a leaf action.</summary>
    public IReadOnlyList<WizardItem> Children { get; init; } = [];
}

/// <summary>
/// Menu tree of the root wizard: six actions (PLAN.md §5.2) with the NuGet / Referencias /
/// Listar sub-menus. Pure data — rendering and ranking stay in <see cref="RootWizard"/>.
/// </summary>
public static class WizardMenu
{
    public static IReadOnlyList<WizardItem> Root { get; } =
    [
        new WizardItem
        {
            Id = "new.solution",
            Title = "Nueva solución",
            Description = "Crea un archivo .sln o .slnx",
            Command = "dh new solution",
            Keywords = ["solución", "solucion", "solution", "sln", "slnx"],
        },
        new WizardItem
        {
            Id = "new.project",
            Title = "Nuevo proyecto",
            Description = "Proyecto nuevo desde una plantilla dotnet new",
            Command = "dh new project",
            Keywords = ["proyecto", "project", "csproj", "fsproj", "plantilla", "template"],
        },
        new WizardItem
        {
            Id = "new.item",
            Title = "Nueva clase o item",
            Description = "Clase, record, interfaz… dentro de un proyecto",
            Command = "dh item",
            Keywords = ["clase", "class", "item", "record", "interfaz", "interface", "struct"],
        },
        new WizardItem
        {
            Id = "nuget",
            Title = "NuGet",
            Description = "Buscar, añadir, quitar y listar paquetes",
            Command = "dh nuget",
            Keywords = ["nuget", "paquete", "package", "paquetes", "dependencias", "dependencies"],
            Children =
            [
                Leaf("nuget.search", "search", "Buscar paquetes en nuget.org", "dh nuget search", "buscar", "search"),
                Leaf("nuget.add", "add", "Añadir un paquete al proyecto", "dh nuget add", "añadir", "add"),
                Leaf("nuget.remove", "remove", "Quitar un paquete del proyecto", "dh nuget remove", "quitar", "remove"),
                Leaf("nuget.list", "list", "Listar los paquetes del proyecto", "dh nuget list", "listar", "list"),
            ],
        },
        new WizardItem
        {
            Id = "references",
            Title = "Referencias",
            Description = "Añadir o quitar referencias entre proyectos",
            Command = "dh project",
            Keywords = ["referencias", "referencia", "references", "reference", "ref"],
            Children =
            [
                Leaf("references.add", "add-ref", "Añadir una referencia entre proyectos", "dh project add-ref", "añadir", "add"),
                Leaf("references.remove", "remove-ref", "Quitar una referencia entre proyectos", "dh project remove-ref", "quitar", "remove"),
            ],
        },
        new WizardItem
        {
            Id = "list",
            Title = "Listar",
            Description = "Plantillas, proyectos y soluciones",
            Command = "dh list",
            Keywords = ["listar", "list", "plantillas", "templates", "proyectos", "projects", "soluciones", "solutions"],
            Children =
            [
                Leaf("list.templates", "templates", "Plantillas disponibles de dotnet new", "dh list templates", "plantillas", "templates"),
                Leaf("list.projects", "projects", "Proyectos de la solución activa", "dh list projects", "proyectos", "projects"),
                Leaf("list.solutions", "solutions", "Soluciones bajo el directorio actual", "dh list solutions", "soluciones", "solutions"),
            ],
        },
    ];

    /// <summary>Ranking weights: keyword &gt; title &gt; description (same idea as templates).</summary>
    public static IReadOnlyList<WeightedField> Fields(WizardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        List<WeightedField> fields = new(item.Keywords.Length + 2);
        foreach (string keyword in item.Keywords)
        {
            fields.Add(new WeightedField(keyword, WeightedField.ShortNameWeight));
        }

        fields.Add(new WeightedField(item.Title, WeightedField.NameWeight));
        fields.Add(new WeightedField(item.Description, WeightedField.TagWeight));
        return fields;
    }

    /// <summary>Picker detail: description plus the equivalent command.</summary>
    public static IReadOnlyList<string> Detail(WizardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return
        [
            item.Description,
            $"equivalent: {item.Command}",
        ];
    }

    private static WizardItem Leaf(string id, string title, string description, string command, params string[] keywords) =>
        new()
        {
            Id = id,
            Title = title,
            Description = description,
            Command = command,
            Keywords = keywords,
        };
}

/// <summary>
/// Executes the flow behind a wizard leaf by delegating to the existing command flows
/// (no duplicated logic — PLAN.md Fase 5).
/// </summary>
public static class WizardDispatcher
{
    public static Task<int> RunAsync(WizardItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Id switch
        {
            "new.solution" => NewSolutionFlow.RunAsync(new NewSolutionSettings(), cancellationToken),
            "new.project" => NewProjectFlow.RunAsync(new NewProjectSettings(), cancellationToken),
            "new.item" => ItemFlow.RunAsync(new ItemCommandSettings(), cancellationToken),

            "nuget.search" => NugetSearchFlow.RunAsync(new NugetSearchSettings(), cancellationToken),
            "nuget.add" => NugetAddFlow.RunAsync(new NugetAddSettings(), cancellationToken),
            "nuget.remove" => NugetRemoveFlow.RunAsync(new NugetRemoveSettings(), cancellationToken),
            "nuget.list" => NugetListFlow.RunAsync(new NugetListSettings(), cancellationToken),

            "references.add" => ProjectRefFlow.RunAsync(new ProjectAddRefSettings(), remove: false, cancellationToken),
            "references.remove" => ProjectRefFlow.RunAsync(new ProjectAddRefSettings(), remove: true, cancellationToken),

            "list.templates" => ListTemplatesFlow.RunAsync(new ListTemplatesSettings(), cancellationToken),
            "list.projects" => ProjectListFlow.RunAsync(new ProjectListSettings(), listOnly: true, cancellationToken),
            "list.solutions" => Task.FromResult(ListSolutionsFlow.Run()),

            _ => throw new InvalidOperationException($"Unknown wizard action '{item.Id}'."),
        };
    }
}