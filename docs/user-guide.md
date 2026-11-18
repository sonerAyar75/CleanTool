# User guide

CleanTool adds two independent tools to AutoCAD 2025/2026. Each tool has its own commands and its own settings;
settings are kept for the AutoCAD session and are shared by a tool's dialog and its command-line version.

[TOC]

## Commands

### Clean – drawing cleanup

| Command | Description |
|---|---|
| `CT_CLEAN` (`TTC`) | Opens the dialog. Select objects with **Select objects** (the dialog hides, like HATCH), then **Preview** or **Run**. Objects selected before the command become the initial selection. |
| `-CT_CLEAN` (`-TTC`) | Select objects, then command-line options: `[Tolerance/Ignore/Blocks/OVerlap/Operations/Preview/Run] <Run>`. Suitable for scripts. |
| `CT_THICKNESS` | Sets the thickness of the selected objects to 0. |
| `CT_FLATTEN` | Moves the selected objects to Z = 0; floating blocks are moved down by their insertion point. |
| `CT_DUPLICATES` | Removes exact copies in the selection: lines, circles, arcs, polylines (LW/2D/3D) and blocks. |
| `CT_LAYERS` | Deletes unused layers. Applies to the whole drawing; no selection needed. |

### WallTrim – cutting walls at columns

| Command | Description |
|---|---|
| `CT_WALLTRIM` (`TTW`) | Opens the dialog. Select the wall lines **and** the column blocks together, then **Preview** or **Run**. |
| `-CT_WALLTRIM` (`-TTW`) | Select objects, then command-line options: `[Inside/Match/Junctions/Closed/Thickness/Keep/Layers/Preview/Run] <Run>`. |

### Shortcuts

Short aliases are built into the plug-in, so no `acad.pgp` change is needed:

| Alias | Command |
|---|---|
| `TTC` | `CT_CLEAN` |
| `-TTC` | `-CT_CLEAN` |
| `TTW` | `CT_WALLTRIM` |
| `-TTW` | `-CT_WALLTRIM` |

They are registered as real commands. Note that an `acad.pgp` alias with the same name takes precedence over them.

## The dialog

```
[Select objects]  24 objects selected
Include blocks    [Selected blocks   v]        (Clean only)
┌ Operations ─────────────────────────┐
│ [x] Thickness = 0                   │
│ [x] Flatten (Z = 0)                 │
│       Z tolerance [0.000001      ]  │
│ [x] Duplicate objects               │
│       Tolerance   [0.0001        ]  │
│       Ignore      [ ]Layer [ ]Color [ ]Linetype
│                   [ ]Lt scale [ ]Lineweight [ ]Transparency
│       Block overlap [Remove loose object v]
│       [ ] Match properties          │
│ [x] Unused layers (whole drawing)   │
│       Keep layers [A-*; TEMP?    ]  │
└─────────────────────────────────────┘
Results
[ ... report ... ]
status               [Preview] [Run] [Close]
```

- **Preview** runs everything without changing the drawing and shows what would change in the results panel.
- **Run** applies the changes. The report is shown in the panel and also written to the command line
  (F2 text window), so it stays in the command history.
- Tolerance fields accept both `0.001` and `0,001`.
- Hover over an operation or an option to see its description.

## Execution order

Operations always run in this order, regardless of the order in which they are checked:

| # | Stage | Operation | Why this order? |
|---|---|---|---|
| 1 | Normalize | Thickness = 0 | Thickness extrudes an object along Z. If it is not reset first, even a flat circle looks 3D and cannot be flattened. |
| 2 | Flatten | Z = 0 | Copies that differ only in Z start to overlap after this step. |
| 3 | Deduplicate | Duplicate objects | Must work on flattened geometry. |
| 4 | Purge | Unused layers | Layers emptied by erased objects are removed last. |
| 5 | – | Dynamic block update | Anonymous copies of dynamic blocks whose content changed are regenerated. |

Each operation runs in its own transaction: if one fails, only its changes are rolled back.

## Clean options

### Thickness = 0

Resets the thickness of lines, circles, arcs, polylines, texts, 2D solids, traces, points, shapes and block
attributes.

### Flatten (Z = 0)

- Lines, LWPolylines, 3D polylines and splines are flattened point by point.
- Objects lying in a plane parallel to XY (circles, arcs, texts, hatches, ...) are moved down by their elevation.
- Other objects are projected onto XY; types that cannot be projected are reported as failed.
- **Floating blocks**: a block reference is measured by its insertion point and attributes, not by its content, so a
  block with 3D content is moved down as well. Blocks rotated out of the XY plane are not moved (their orientation
  would change) and are reported.
- **Z tolerance**: objects closer to zero than this are left unchanged.

### Duplicate objects

Two objects are copies when their geometry is equal within the **Tolerance** and – by default – their layer, color,
linetype, linetype scale, lineweight and transparency are equal too (like OVERKILL).

- **Ignore**: properties whose differences are ignored. Values are compared raw: a ByLayer color and the same color
  set explicitly are different.
- Supported kinds: lines, circles, arcs, polylines (LW, 2D and 3D polylines are compared with each other) and block
  references.
- Polylines: reversed drawing direction, a different start vertex of a closed polyline, a vertex clicked twice and
  "closed by snapping to the start point" vs. "Close" are all treated as the same polyline.
- Blocks: same definition, position, rotation, scale and attribute values. Two door tags at the same spot with
  different numbers are not copies; dynamic blocks with different parameters are not copies.
- Locked layers: copies on locked layers are never erased; if a group contains a locked object, that one is kept.

**Block overlap** – when a loose object is an exact copy of an object inside the block it lies on:

| Option | Result |
|---|---|
| **Remove loose object** (default) | The loose copy is erased; the block does not change. |
| Remove from block | The copy in the block definition is erased – only for blocks used once, not nested and not dynamic. Others are skipped and the reason is reported (e.g. "block is used 4 times"). |
| Don't compare | Block content and loose objects are not compared. |

**Match properties** (off by default): when off, only geometry is compared with block content. Block content on
layer 0 or with ByBlock properties takes the insert's layer and values. When on, the Ignore rules apply.

Lines that only partly overlap a block, or pass through it, are handled by **WallTrim**.

### Include blocks

| Option | What is processed |
|---|---|
| No | Only the selected objects. A block reference is treated as one object (only its insertion point is flattened). |
| **Selected blocks** (default) | Also the definitions of the selected block references, including nested and dynamic blocks. |
| All blocks in drawing | Also every eligible block definition in the drawing. |

- Changing a block definition changes **every insert** of that block.
- Copies are searched only **within the same space**: a line in model space and a line inside a block are not copies.
- Inside blocks, Thickness = 0, Z = 0 and Duplicates are applied.
- **Dynamic blocks**: the base definition is processed and its anonymous copies are regenerated at the end.
  Duplicates are **not** removed inside dynamic blocks: visibility states and actions reuse geometry on purpose.
- Skipped definitions: xrefs, other anonymous blocks (dimensions, tables), layouts.

### Unused layers

Deletes layers that no object uses (including objects inside block definitions). Layer 0, Defpoints, the current
layer and xref-dependent layers are always kept. **Keep layers** takes wildcard patterns separated by `;`
(e.g. `A-*; TEMP?`).

## WallTrim options

Select the wall lines and the column blocks **together**. For every loose line or polyline, the parts that lie on
blocks are removed:

- **Along edges** (every block): the part that overlaps a block edge on the same line is removed.
- **Inside** (only blocks listed in **Trim inside blocks**): the part inside the block's closed outline is removed,
  so the wall line is cut at the column face. Closed straight polylines and circles count as outlines.
- A line with nothing left is erased; otherwise it is replaced by the remaining pieces, keeping the original's
  layer, color and linetype (the pieces get new handles). Line → Line, LWPolyline/2D polyline → LWPolyline,
  3D polyline → 3D polyline. Arc segments are never cut.

**Trim inside blocks** expects **block names** with wildcards, separated by `;` – e.g. `K*; KOLON*`.
Use the **...** button to pick blocks from a list: blocks in the selection come first and, by default, only blocks
with a closed outline are shown. Hand-typed wildcard patterns are kept.

> The field is empty by default and `*` alone should not be used: a large closed block such as a title-block frame
> would remove everything inside it. If the pattern matches no block, the report says so and lists the blocks with
> closed outlines in the selection.

**Match properties** (off by default): when off, only geometry counts – a wall line on A-WALL is cut at a column on
A-COLUMN. When on, layer, color, linetype etc. must match; lines that match geometrically but differ in properties
are listed in the report.

### Wall layers

**Wall layers** limits **Open wall junctions** and **Create closed polylines** to the wall lines, e.g. `DUVAR*`
(wildcards `*` and `?`, separated by `;`; empty = every layer). Window, door and furniture lines on other layers are
then never cut or joined; the trim at blocks is not affected. Use the **...** button to pick layers from a list:
layers with lines or polylines in the selection come first and, by default, only those are shown. Hand-typed
wildcard patterns are kept.

### Open wall junctions (T, +)

When **Open wall junctions** is on, the parts of wall lines that run across another wall are removed, so a T or
cross junction looks like the faces of the rooms around it (MLEDIT "Open Tee" / "Open Cross" for lines):

- Two parallel lines on the same layer, at most **Max wall thickness** apart, form a wall when each is the other's
  nearest parallel line (so the gap between two neighbouring walls is not a wall).
- A line that crosses a wall from face to face and continues beyond both faces is cut between them – at the end
  of the wall (T) or in its middle (cross).
- Wall end lines, door and window jambs, L corners and short overshoots are never cut.
- It runs after the trim at blocks and before **Create closed polylines**, so an opened T becomes one polyline.

### Create closed polylines

When **Create closed polylines** is on, the wall lines left after trimming are turned into closed polylines,
ready for hatching. The trim result is computed first, so **Preview** shows the real outcome.

1. **End caps.** A free end of a wall face is closed with the free end of a parallel face that runs the same way
   from its end. The distance between the two faces is the wall thickness; it is measured from the lines and must
   be at most **Max wall thickness** (drawing units, default 60 – e.g. cm drawings). Each end takes its nearest
   matching face. If an end has two equally good faces nothing is guessed: it stays open and is reported.
2. **Corners.** At an L corner whose lines do not quite meet (a small gap, or lines running past each other),
   the two lines are extended or trimmed to their intersection, like FILLET with radius 0. Ends that are already
   aligned (a wall cut at a column face) are capped first, so a wall is never extended into a column.
3. **Joining.** Lines and caps on the **same layer** that meet end to end are joined, like PEDIT Join.
4. **Closed loops** become LWPolylines with the layer, color and linetype of one of their lines.

Results by layout:

| Layout | Result |
|---|---|
| Straight wall between two columns | one rectangle |
| L corner without a column | one L-shaped polyline |
| T junction, main wall face trimmed between the branch faces | one T-shaped polyline |
| T junction, main wall face continuous | two polylines: the main wall, and the branch closed along the main face |


- A door opening closes each side separately (the ends face each other, so they are not paired).
- A wall ring without columns gives an outer and an inner polyline; a hatch uses the inner one as an island.
- Lines that do not close (a single face, faces farther apart than the max thickness, other layers) are left
  alone and counted in the report.
- **Keep original lines** (off by default): when off, lines that became part of a polyline are erased; when on,
  they stay and the polylines are added on top. If a polyline cannot be created, its lines are always kept.
- Lines on locked layers and arc segments are not used.
- When a wall end touches a line on another layer, the report says so: lines on different layers are not joined,
  which usually means a wall line is on the wrong layer.

## Known limitations

- Duplicate detection finds exact copies only. Partial overlaps, a line vs. a single-segment polyline and polylines
  with an extra collinear vertex are not copies.
- Ellipses, splines and texts are not checked for duplicates.
- Duplicates are not removed inside dynamic blocks (deliberate, see above).
- Circles and arcs not parallel to XY cannot be projected onto XY; they are reported as failed.
- WallTrim cuts straight segments only; outlines must be closed straight polylines or circles (a column drawn with
  four separate lines is trimmed along its edges only).
- Settings are kept for the AutoCAD session only.
- Closed polylines are built from straight segments only; walls with arc faces and caps that would follow a round
  column face are not supported yet.
