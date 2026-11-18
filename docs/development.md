# Development

[TOC]

## Requirements

- .NET 8 SDK and Visual Studio 2022 (17.8 or newer) or `dotnet` CLI
- AutoCAD 2025 or 2026 (to run the plug-in; not needed for the unit tests)
- The `AutoCAD.NET` 25.1.0 NuGet package is restored automatically
- Optional: Doxygen 1.10+ and Graphviz for the API reference

## Build, test, deploy

```bash
dotnet test  tests/CleanTool.Core.Tests     # unit tests (Core only), no AutoCAD needed
dotnet build src/CleanTool.AcadTool         # builds and deploys the bundle
```

After every build of `CleanTool.AcadTool` the `DeployCleanToolBundle` target copies the manifest and our own DLLs to
the autoloader folder:

```
%AppData%\Autodesk\ApplicationPlugins\CleanTool.bundle\
├─ PackageContents.xml              (src/CleanTool.AcadTool/Bundle)
└─ Contents\  CleanTool.AcadTool.dll, CleanTool.Core.dll (+ pdb, deps.json)
```

- AutoCAD loads the bundle at startup; no NETLOAD needed. The manifest covers R25.0 (2025) and R25.1 (2026).
- **Close AutoCAD before building** – the DLL is locked while AutoCAD runs and the copy fails.
- Disable deployment with `-p:DeployBundle=false`.
- Do not NETLOAD the DLL from `bin` at the same time (the same assembly would be loaded twice).
- Uninstall by deleting the `CleanTool.bundle` folder (or from *Installed apps* when installed with the setup).

### Release package (zip + installer)

```bash
dotnet build CleanTool.sln -c Release -p:DeployBundle=false
```

A Release build of `CleanTool.AcadTool` (target `PackCleanTool`) writes the release package to `artifacts\`:

| File | Use |
|---|---|
| `CleanTool-<version>.zip` | Contains the `CleanTool.bundle` folder; extract it into `%AppData%\Autodesk\ApplicationPlugins`. Unblock the zip first (Properties → Unblock) when it was downloaded, otherwise Windows may block the DLLs. |
| `CleanTool-Setup-<version>.exe` | Per-user installer (no administrator rights): installs into `%AppData%\Autodesk\ApplicationPlugins\CleanTool.bundle`, asks to close AutoCAD if it is running, replaces an older version and adds an uninstaller to *Installed apps*. English and Turkish. |

- The installer is built only when [Inno Setup](https://jrsoftware.org) 7, or 6.3 or newer, is installed. It is
  looked up in the Inno Setup 7 per-user and all-users folders, then in the Inno Setup 6 folders; another
  location: `-p:InnoSetupCompiler="…\ISCC.exe"`. Without it only the zip is created. The script is
  `installer/CleanTool.iss`.
- Both the zip and the installer contain `LICENSE.txt` (MIT) in the bundle folder; the installer also shows it on a
  license page.
- **Version**: change `<Version>` in `Directory.Build.props` – the DLLs, `PackageContents.xml` (the `@VERSION@`
  tokens), the zip and the installer names all take it from there. Keep the installer's `AppId`: it is how a new
  version replaces the old one.
- The package contains only our own DLLs and `deps.json` (no pdb); the AutoCAD DLLs are never shipped.

### Debugging

In the AcadTool project properties set *Debug → Launch profile* to start `acad.exe` as an executable, then press F5;
breakpoints are hit once AutoCAD loads the bundle. Alternatively use *Debug → Attach to Process* on a running AutoCAD.

## Adding a cleanup operation

Example: deleting zero-length objects in the Clean tool.

1. **Core** – `Operations/ZeroLength/`:
   - a record with the data the operation needs (`ZeroLengthCandidate`),
   - a port `IZeroLengthGateway`,
   - options (`ZeroLengthOptions`),
   - `ZeroLengthOperation : ICleanOperation` with an `OperationId`, a `DisplayName` and a `CleanStage`.
2. **Tests** – a fake gateway in `tests/.../Fakes` and tests for the operation (preview, locked layers, failures).
3. **AcadTool** – `Gateways/AcadZeroLengthGateway` implementing the port, and a factory
   `ZeroLengthOperationFactory : IOperationFactory` in `Tools/Clean/CleanOperationFactories.cs`
   (name, description, command keyword, `UsesSelection`, optional `IOptionsPage`).
4. **Settings** – add a section to `ToolSettings` if the operation has options.
5. **Register** – one line in `CleanModule.Initialize`: `.Register(new ZeroLengthOperationFactory())`.
   Optionally add a quick command (`CT_ZEROLEN`) to `CleanCommands`.

The pipeline, report, transactions, preview, dialog section and `-CT_CLEAN` Operations option pick up the new
operation automatically.

## Adding a tool

1. `Tools/<Name>/<Name>Module.cs` – an `IToolModule` that builds its own `OperationRegistry`, `CleanRunner` and
   `SettingsStore` in `Initialize`.
2. `Tools/<Name>/<Name>Commands.cs` – the commands, registered with `[assembly: CommandClass(...)]`. Start every
   command with `ToolHost.TryGet<<Name>Module>(editor, out var module)`.
3. Add `new <Name>Module()` to the list in `CleanToolApp.Initialize`.
4. Reuse `CleanToolForm` with a `ToolFormOptions` for the dialog.

A tool never references another tool. If something is needed by two tools, move it to Core or to the shared
infrastructure.

## Adding a shape kind (duplicates / trimming)

1. Core: a new `IShape` record in `Geometry/Shapes` (with `SortKey`, `Kind`, `TryTransform`).
2. Core: a `ShapeComparer<TShape>` and an entry in `ShapeComparerRegistry.Default`.
3. AcadTool: an `IShapeReader` and an entry in `ShapeReaderRegistry.CreateDefault`.
4. Tests for the comparer (equal, reversed, within/outside tolerance, transformed).

## Adding flatten support for an entity type

Write an `IEntityFlattener` in `Flattening/` and add it to `FlattenerRegistry.CreateDefault`. Specific flatteners
are tried in order; `GenericFlattener` is the fallback.

## Coding conventions

- Code, identifiers, comments and documentation are in **English**.
- Every public and internal type and member has an XML documentation comment. Interface implementations use
  `/// <inheritdoc/>`.
- Core must not reference AutoCAD. Tests reference Core only.
- Commands contain no business logic; dialogs never use AutoCAD types directly.
- Multi-line command-line output goes through `Infrastructure/CommandLine` (one `WriteMessage` call).
- Catch exceptions only at boundaries (pipeline, gateways, module loader) and turn them into reported failures.

## Documentation

The API reference is generated from the XML documentation comments with Doxygen. `Directory.Build.props` also
enables `GenerateDocumentationFile`, so the same comments appear in IntelliSense.

```bash
doxygen Doxyfile
```

| Setting (Doxyfile) | Value | Why |
|---|---|---|
| `INPUT` | `README.md docs src` | Sources plus the Markdown pages; tests are not part of the API reference |
| `USE_MDFILE_AS_MAINPAGE` | `README.md` | The README is the start page |
| `OUTPUT_DIRECTORY` | `docs/api` | Generated output (ignored by git) |
| `OPTIMIZE_OUTPUT_JAVA` | `YES` | Recommended for C# (namespaces with `.`) |
| `EXTRACT_ALL`, `EXTRACT_PACKAGE` | `YES` | Include internal types (most AcadTool types are internal) |
| `INHERIT_DOCS` | `YES` | Interface implementations show the interface's documentation |
| `HAVE_DOT`, `UML_LOOK` | `YES` | Class and collaboration diagrams (requires Graphviz) |
| `WARN_LOGFILE` | `docs/api/doxygen-warnings.txt` | Review undocumented items and broken references here |

Namespace descriptions are in `docs/namespaces.dox`. When adding a namespace, add a `@namespace` entry there.

Open `docs/api/html/index.html` after generation. If Graphviz is not installed, set `HAVE_DOT = NO`.
