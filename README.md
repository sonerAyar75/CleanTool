# CleanTool – AutoCAD cleanup and wall-trim tools

CleanTool is an AutoCAD 2025/2026 (.NET 8) plug-in with two independent tools:

| Tool | Command | What it does |
|---|---|---|
| **Clean** | `CT_CLEAN` (`TTC`) | Drawing cleanup: thickness = 0, Z = 0 (incl. floating blocks), duplicate removal (lines, circles, arcs, polylines, blocks), unused layers. Optionally inside block definitions. |
| **WallTrim** | `CT_WALLTRIM` (`TTW`) | Cuts wall lines where they run over or through column blocks, at the column face. Optionally opens T/+ junctions and turns the remaining walls into closed polylines. |

Both tools open a compact OVERKILL-style dialog with **Select objects**, **Preview** and **Run**, and have
command-line versions (`-CT_CLEAN`, `-CT_WALLTRIM`) for scripts.

```
Before:              After CT_WALLTRIM (Trim inside blocks = K*):
 ───┬────┬───         ───┬────┬───
    │    │               │    │
────┼────┼────       ────┤    ├────
    │ K1 │               │ K1 │
────┼────┼────       ────┤    ├────
    │    │               │    │
 ───┴────┴───         ───┴────┴───
```

## Quick start

```bash
dotnet test  tests/CleanTool.Core.Tests     # unit tests, no AutoCAD needed
dotnet build src/CleanTool.AcadTool         # restores AutoCAD.NET 25.1.0 and deploys the bundle
```

The build copies the plug-in to `%AppData%\Autodesk\ApplicationPlugins\CleanTool.bundle\`; AutoCAD loads it
automatically at startup (no NETLOAD). **Close AutoCAD before building** – the DLL is locked while AutoCAD runs.

Then type `TTC` (`CT_CLEAN`) or `TTW` (`CT_WALLTRIM`) in AutoCAD.

## Installing on other computers

Run `CleanTool-Setup-<version>.exe` (per user, no administrator rights), or extract `CleanTool-<version>.zip` into
`%AppData%\Autodesk\ApplicationPlugins`. Both are produced by a Release build; see
[Development › Release package](docs/development.md#release-package-zip--installer).

## Documentation

| Document | Contents |
|---|---|
| [User guide](docs/user-guide.md) | All commands and options, execution order, block handling, known limitations |
| [Architecture](docs/architecture.md) | Projects and layers, tool modules, pipeline, ports and gateways, design decisions (SOLID) |
| [Development](docs/development.md) | Build, test, deploy, adding operations / tools / shape kinds, coding conventions |
| [Test drawing](docs/test-drawing.md) | `tests/data/wall_scenarios.dxf`: 20 WallTrim scenarios with expected results |
| API reference | Generated with Doxygen (see below) |

### Generating the API reference (Doxygen)

All public and internal types and members carry English XML documentation comments, which Doxygen reads directly.

```bash
doxygen Doxyfile          # from the repository root
```

Open `docs/api/html/index.html`. Requirements: [Doxygen](https://www.doxygen.nl) 1.10 or newer (for C# file-scoped
namespaces and records) and, for class diagrams, [Graphviz](https://graphviz.org) (`dot` on the PATH).
Details and options are in [Development › Documentation](docs/development.md#documentation).

## Repository layout

```
CleanTool.sln
├─ src/CleanTool.Core          net8.0          No AutoCAD reference: rules, algorithms, ports
├─ src/CleanTool.AcadTool      net8.0-windows  AutoCAD adapter: tool modules, commands, dialogs, gateways
├─ tests/CleanTool.Core.Tests  xUnit           Tests for Core; run without AutoCAD
├─ docs/                       User guide, architecture, development, Doxygen namespace pages
├─ installer/                  Inno Setup script of the per-user installer
└─ Doxyfile                    Doxygen configuration (output: docs/api)
```

Dependencies point one way: `AcadTool → Core` and `Tests → Core`. Tools never reference each other.

## License

CleanTool is released under the [MIT License](LICENSE.txt). The AutoCAD .NET API is referenced at build time only;
no Autodesk files are distributed.
