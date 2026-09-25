# DotHelper

**Interactive .NET project helper for the terminal.**

DotHelper is a global `dotnet tool` (`dh`) that wraps the everyday parts of `dotnet new`, `dotnet sln`, project references and NuGet packages in an interactive wizard. Every list is a fuzzy picker in the style of fzf — start typing and the best match floats to the top — and every action prints the equivalent `dotnet` command it ran, so nothing is a black box.

## Requirements

- **.NET SDK 8.0 or newer.** DotHelper targets `net8.0` with `RollForward=LatestMajor`, so it runs on any newer SDK (tested with .NET 10).

## Installation

Once published on NuGet.org:

```bash
dotnet tool install -g DotHelper
```

Until then (or to test a local checkout), build and install it from source:

```bash
git clone https://github.com/westra126/DotHelper.git
cd DotHelper
dotnet pack src/DotHelper/DotHelper.csproj -c Release -o ./artifacts/nupkg
dotnet tool install -g --add-source ./artifacts/nupkg DotHelper
```

The tool installs the `dh` command. Optionally, alias it as `dothelper` in your shell:

```bash
alias dothelper='dh'
```

## Quick start

Run `dh` with no arguments to open the root wizard: a fuzzy menu with the main actions — new solution, new project, new item, NuGet, references and listing — each one delegating to the same flow as its direct command. In a directory with an active solution, creating a class looks like this:

```console
$ dh class                     # alias of `dh item`, pre-loads the "class" filter
? templates  class             # fuzzy picker: class, interface, record, struct, enum, ...
? Name: CustomerId
? project  /home/me/App/src/App.Domain/App.Domain.csproj
? Folder (Enter = project root): Domain/Customers
✔ Created /home/me/App/src/App.Domain/Domain/Customers/CustomerId.cs
  (dotnet new class -n CustomerId -o /home/me/App/src/App.Domain/Domain/Customers --project /home/me/App/src/App.Domain/App.Domain.csproj)
```

Prompts adapt to your workspace: the project picker proposes the projects of the nearest solution, folder prompts suggest `src/` or `tests/` based on the template, and Enter accepts the default on name/folder prompts.

## Commands

Most commands operate on the *active workspace*: the nearest `.sln`/`.slnx` found walking up from the current directory (falling back to the nearest `.csproj`/`.fsproj`). Run `dh --help` or `dh <command> --help` for the always-up-to-date surface.

| Command | Description |
| --- | --- |
| `dh [-q\|--query <QUERY>]` | Interactive wizard: pick an action with fuzzy search |
| `dh about` | Show DotHelper version and information |
| `dh new solution [name] [--format sln\|slnx]` | Create a new solution (offers to add the first project right after) |
| `dh new project [template] [--name <NAME>] [--output <DIR>] [--query <QUERY>] [--add-to-sln] [--no-add-to-sln]` | Create a new project from a `dotnet new` template |
| `dh item [template] [-n\|--name <NAME>] [--project <PROJ>] [--output <DIR>] [--query <QUERY>]` | Create a new item (class, record, interface, ...) inside a project |
| `dh class` | Create a new class (alias of `dh item` pre-loading the `class` query) |
| `dh sln list` | List projects of the active solution |
| `dh sln add [project] [--query <QUERY>]` | Add a project to the active solution |
| `dh sln remove [project] [--query <QUERY>]` | Remove a project from the active solution |
| `dh project list` | List projects of the active solution (or the closest project) |
| `dh project add-ref [--from <PROJ>] [--to <PROJ>] [--query <QUERY>]` | Add a project reference (source → target pickers) |
| `dh project remove-ref [--from <PROJ>] [--to <PROJ>] [--query <QUERY>]` | Remove a project reference |
| `dh list templates [--query <QUERY>] [--json] [--dry-run]` | Browse or list the available `dotnet new` templates |
| `dh list solutions` | List solution files under the current directory |
| `dh list projects` | List projects of the active solution |
| `dh nuget search [term] [--take <N>] [--prerelease] [--json]` | Search packages on nuget.org |
| `dh nuget add [package] [--project <PROJ>] [--version <VERSION>] [--query <QUERY>]` | Add a package reference to a project |
| `dh nuget remove [package] [--project <PROJ>] [--query <QUERY>]` | Remove a package reference from a project |
| `dh nuget list [--project <PROJ>] [--include-transitive] [--json]` | List the package references of a project |

Commands that operate on a workspace (`new`, `item`, `class`, `sln`, `project`, `nuget`, `list solutions`, `list projects`) also accept:

- `--dry-run` — print the exact `dotnet ...` command instead of running mutating ones.
- `-y`, `--yes` — never prompt: use the provided values, defaults, or the top-ranked picker result.
- `--verbose` — log the stdout/stderr of every `dotnet` invocation to `~/.local/state/dothelper/logs/`.

For `dh nuget add`, `--project` accepts a full path, a file name (`App.Api.csproj`) or a project name fragment. For `dh item`, `--project` expects a path to the project file (relative to the current directory is fine).

## Fuzzy search

Every picker filters incrementally while you type, with matched characters highlighted fzf-style:

- **Ranking** — the best matching field wins: `ShortName` (weight 1.0) > `Name` (0.85) > `Tags` (0.5). Field matching uses `max(WeightedRatio, TokenSetRatio)`, case-insensitive, so `api web` still matches `Web API`.
- **Relevance cutoff** — 60 (default). Items below it are hidden; on ties the original order is kept, project templates rank before item templates, and NuGet results fall back to the most downloaded package.
- **Keys** — `↑`/`↓` move, `Enter` selects, `Esc` cancels, `Tab` toggles template/package detail, type to filter, `Backspace` deletes, `Ctrl+C` aborts (exit code 130).
- **`--query <q>`** — seeds the filter before the first frame. With stdin redirected there is no key I/O at all: the top-ranked match is returned directly, which is what makes scripting work. `dh list templates --query <q>` additionally prints a ranked table (top 15, with scores) instead of opening the picker.

## Non-interactive use, scripting and Neovim

`--query` seeds the fuzzy filter, `--yes` skips all prompts (provided values, defaults or the top-ranked candidate are used) and `--dry-run` prints the exact `dotnet ...` command instead of running mutating ones:

```bash
# scaffold a solution and a project with zero prompts (run from the repo root)
dh new solution App --yes
dh new project classlib --name Core --output src --yes

# create a class in a specific project
dh item --query class -n CustomerId --project src/App.Domain/App.Domain.csproj --yes

# preview what would run
dh nuget add --query serilog --project App.Api --dry-run

# JSON output for scripts
dh nuget search serilog --take 5 --json
dh nuget list --project App.Api --json
dh list templates --json
```

Notes:

- With `--yes`, missing values fall back to defaults (solution/project name `App`, item name `NewFile`, project root folder) and pickers resolve to the top-ranked candidate.
- Read-only discovery (`dotnet new list`, `dotnet sln list`) always runs, even under `--dry-run`, because it is needed to resolve pickers and defaults.
- Without a TTY, `dh` prints the help instead of hanging; with redirected stdin and a `--query`, pickers return the top-ranked match without waiting for keys.

Inside Neovim:

```vim
:terminal dh                                  " interactive wizard
:!dh item --query class -n CustomerId --yes   " direct scaffold, no prompts
:w !dh item --query class -n Foo --yes        " pipe the buffer through DotHelper
```

Every flow also prints the equivalent `dotnet` line, so you can copy it and run it later as `:!dotnet ...`.

## Development

```bash
git clone https://github.com/westra126/DotHelper.git
cd DotHelper

dotnet build -warnaserror
dotnet test --project tests/DotHelper.Tests.Unit/DotHelper.Tests.Unit.csproj
dotnet test --project tests/DotHelper.Tests.Integration/DotHelper.Tests.Integration.csproj --filter-trait "Category=Integration"
dotnet format --verify-no-changes

# local package for `dotnet tool install`
dotnet pack src/DotHelper/DotHelper.csproj -c Release -o ./artifacts/nupkg
```

Integration tests run real `dotnet` commands in temporary workspaces under `/tmp` (created and cleaned up by the `TempWorkspace` fixture).

```
DotHelper/
├── src/DotHelper/
│   ├── Program.cs        # command registration (Spectre.Console.Cli)
│   ├── Cli/              # commands, root wizard and shared CLI plumbing
│   ├── Core/
│   │   ├── Dotnet/       # dotnet CLI wrapper, template catalog and sln/project/item/nuget services
│   │   └── Workspace/    # .sln/.slnx/.csproj discovery
│   └── Ui/               # fuzzy picker, scorer, highlighter, prompts, theme
└── tests/
    ├── DotHelper.Tests.Unit/          # fuzzy ranking, parsers and services with fixtures
    └── DotHelper.Tests.Integration/   # temp workspaces + real dotnet CLI
```

## Disclaimer

DotHelper is a community tool and is not affiliated with, endorsed by, or sponsored by Microsoft. .NET is a trademark of Microsoft Corporation.
