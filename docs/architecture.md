# Architecture

[TOC]

## Goals

- **Core is independent of AutoCAD.** Every rule and algorithm lives in `CleanTool.Core` and is unit tested without AutoCAD.
- **Extensible without modification.** A new operation, shape kind or tool is added by writing new classes and
  registering them; existing code does not change.
- **Independent tools.** Each tool (Clean, WallTrim) owns its commands, operations and settings and can evolve on its own.
- **Safe by default.** Previews change nothing, every operation is transactional, locked layers are respected and
  risky actions (editing block definitions, trimming inside blocks) are opt-in.

## Projects

```
CleanTool.sln
├─ src/CleanTool.Core          net8.0          NO AutoCAD reference
│   ├─ Hosting/                IToolModule, ModuleLoader (loads modules in isolation)
│   ├─ Abstractions/           ICleanOperation, CleanStage, IOperationScope, ICleanLogger
│   ├─ Geometry/               Point3, Segment3, Tolerance, Transform3
│   │   └─ Shapes/             IShape: LineShape, CircleShape, ArcShape, PolylineShape, BlockShape
│   ├─ Model/                  EntityHandle, DisplayProperties, ComparedProperty
│   ├─ Pipeline/               CleanPipeline, CleanContext, OperationResult(+Builder), CleanReport(+Formatter)
│   └─ Operations/
│       ├─ Thickness/          ThicknessOperation + IThicknessGateway
│       ├─ Flatten/            FlattenOperation + IFlattenGateway
│       ├─ Duplicates/         DuplicateFinder, shape comparers, BlockOverlapResolver, DuplicateOperation + IDuplicateGateway
│       ├─ BlockTrim/          BlockCoverage (trim decisions), WallJunctionOpener (T/+ junctions),
│       │                      WallOutlineBuilder (closed wall outlines),
│       │                      BlockTrimOperation + IBlockTrimGateway
│       └─ Layers/             LayerPurgePolicy, WildcardMatcher, LayerPurgeOperation + ILayerGateway
│
├─ src/CleanTool.AcadTool      net8.0-windows  depends on Core and AutoCAD
│   ├─ CleanToolApp.cs         Entry point (IExtensionApplication): loads the tool modules
│   ├─ Hosting/                ToolHost: how commands reach the loaded modules
│   ├─ Tools/                  ── TOOLS (independent modules) ──
│   │   ├─ Clean/              CleanModule, CleanCommands, CommandLineClean, operation factories
│   │   └─ WallTrim/           WallTrimModule (+ BlockTrimOperationFactory), WallTrimCommands
│   │                          ── SHARED INFRASTRUCTURE ──
│   ├─ Composition/            IOperationFactory, OperationRegistry, CleanRunner, SettingsStore, ToolSettings, AcadCleanSession
│   ├─ UI/                     CleanToolForm (built from the registry), options pages, NamePickerForm (block / layer lists), ICleanSession
│   ├─ Gateways/               AutoCAD implementations of the Core ports
│   ├─ ShapeReading/           Entity → IShape readers, block content reader, property reader
│   ├─ Flattening/             Per-type Z = 0 strategies
│   ├─ Infrastructure/         AcadSession, entity scopes, transactions, command line output, logger
│   └─ Bundle/                 PackageContents.xml (autoloader manifest)
│
└─ tests/CleanTool.Core.Tests  xUnit; references Core only
```

Dependencies point one way: `AcadTool → Core`, `Tests → Core`. Inside AcadTool, tools (`Tools/*`) depend on the
shared infrastructure but never on each other.

## Ports and adapters

Core defines what an operation needs from the drawing as small interfaces ("ports"), one per operation
(`IThicknessGateway`, `IFlattenGateway`, `IDuplicateGateway`, `IBlockTrimGateway`, `ILayerGateway`).
The AutoCAD layer implements them ("gateways"). Core works only with platform-independent data:

| Core type | Meaning | AutoCAD counterpart |
|---|---|---|
| `EntityHandle` | Stable object identity | `Handle.Value` |
| `Point3`, `Segment3`, `Transform3` | Geometry | `Point3d`, `Matrix3d` |
| `IShape` records | Comparable geometry of an object | read by `IShapeReader`s |
| `DisplayProperties` | Layer, color, linetype, ... as raw strings | read by `PropertyReader` |

```
 Command / Dialog ──► CleanRunner ──► CleanPipeline ──► ICleanOperation (Core)
                                                              │ uses
                                                              ▼
                                               I…Gateway (Core port)
                                                              ▲ implements
                                               Acad…Gateway (AutoCAD) ──► AcadSession ──► Transaction / scope
```

## Pipeline

`CleanPipeline` runs a list of `ICleanOperation`s:

1. Operations are sorted by `CleanStage` (Normalize → Flatten → Deduplicate → Purge). Sorting is stable, so
   operations in the same stage keep their given order. Registration order only affects the dialog.
2. Each operation runs in its own `IOperationScope` (an AutoCAD transaction). The scope is completed only when the
   operation succeeds and the run is not a preview; otherwise it is rolled back.
3. An exception in one operation is caught, reported as `OperationStatus.Failed` and does not stop the others
   (unless `PipelineOptions.StopOnError`).
4. Each operation returns an immutable `OperationResult` (examined / changed / skipped / failed + messages);
   `CleanReportFormatter` turns the `CleanReport` into text for the dialog and the command line.

After the pipeline, `CleanRunner` regenerates anonymous copies of dynamic blocks whose content may have changed.

## Scopes

`IEntityScope` decides which objects the gateways see. Core does not know about this concept.

- `SelectionScope` – the user's selection (pickfirst or a new selection).
- `CurrentSpaceScope` – everything in the current space.
- `BlockContentScope` – a decorator that adds the content of block definitions (selected blocks or all blocks),
  following nested blocks and resolving dynamic blocks to their base definitions.

## Tool modules

Each tool is an `IToolModule` (`CleanModule`, `WallTrimModule`) with its own `OperationRegistry`, `CleanRunner` and
`SettingsStore`. `CleanToolApp` loads them through `ToolHost`, which wraps Core's `ModuleLoader`:

- If a module throws in `Initialize`, only that tool is disabled. Its commands report the reason; the other tools work.
- AutoCAD creates command classes itself (no constructor injection), so commands fetch their module with
  `ToolHost.TryGet<TModule>(editor, out var module)`.
- Command classes register themselves with `[assembly: CommandClass(...)]` in their own files.
- All tools ship in one DLL and one bundle. When a tool grows its own Core logic, it can move to a separate DLL;
  a bundle can load several DLLs, so users notice no difference.

## User interface

- `CleanToolForm` is shared by all tools. Its operation sections are built from the tool's `IOperationFactory`
  list (name, description, options page), so adding an operation never changes the form.
- The form does not reference AutoCAD types: it talks to AutoCAD only through `ICleanSession`
  (select objects, run) and `IOptionsHost` (e.g. the block list for the picker).
- `ToolFormOptions` customizes the dialog per tool (title, whether the Include blocks row is shown).
- Command-line output always goes through `Infrastructure/CommandLine`, which writes multi-line text with a single
  `WriteMessage` call (separate calls produce double line spacing in recent AutoCAD versions).

## Design decisions (SOLID)

| Principle | Where |
|---|---|
| **S**ingle responsibility | Finding (`DuplicateFinder`, `BlockCoverage`), deciding (`LayerPurgePolicy`, `BlockOverlapResolver`) and applying (gateways) are separate classes. Commands contain no business logic. |
| **O**pen/closed | New operation = new `ICleanOperation` + factory + one `Register` line. New shape kind = `IShape` + `ShapeComparer` + `IShapeReader`. New flatten behavior = `IEntityFlattener`. New tool = new `IToolModule`. |
| **L**iskov substitution | Every operation enters the pipeline with the same contract; none changes anything in a preview. |
| **I**nterface segregation | Each operation has its own small port; there is no large "IDrawing" interface. The form sees only `ICleanSession` / `IOptionsHost`. |
| **D**ependency inversion | Core knows only interfaces. AutoCAD implementations are wired in the modules' composition code. |

Other decisions:

- **Handles, not ObjectIds**, identify objects in Core (`EntityHandle`), because handles are stable.
- **Exact copies vs. coverage**: Duplicate removes exact copies only; partial coverage and cutting belong to
  WallTrim, because they edit geometry.
- **Raw property comparison**: ByLayer and an explicit color are different, like OVERKILL, to keep results predictable.
- **Opt-in inside trimming**: `Trim inside blocks` must name the blocks, so title-block frames are never used as outlines.
