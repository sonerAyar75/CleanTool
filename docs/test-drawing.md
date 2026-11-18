# Test drawing: wall scenarios

`tests/data/wall_scenarios.dxf` is a manual test drawing for **WallTrim** (`TTW`): 20 scenarios in a 5 × 4 grid,
each labelled with what it checks and the expected result. `wall_scenarios_expected.png` shows the expected
closed polylines (filled).

- Units: cm. Walls are 20 thick; column block `kolon` is 40 × 40.
- Layers: `DUVAR` (walls), `KOLON` (column inserts), `PENCERE` (window lines), `MOBILYA` (furniture),
  `0` (a wall on the wrong layer, scenario 18), `TEST-LABEL` (texts).
- DXF R12, so it opens in any AutoCAD version.

## Settings

Select everything, then in `TTW`:

| Setting | Value |
|---|---|
| Trim inside blocks | `kolon` |
| Open wall junctions (T, +) | on (scenario 6 also off) |
| Create closed polylines | on |
| Max wall thickness | 60 |
| Wall layers | `DUVAR` (scenario 18: empty) |

Use **Preview** first, then **Run**; undo (`U`) between runs to try other settings.

## Scenarios

| # | Scenario | Expected |
|---|---|---|
| 1 | Straight wall into 2 columns | Trim inside: kolon; Wall lines end inside the columns; -> 1 rectangle between the columns |
| 2 | Wall faces on column edges | Wall 40 thick = column width; -> faces cut along column edges; -> 1 rectangle between columns |
| 3 | L corner, exact | -> 1 L-shaped polyline |
| 4 | L corner, 0.5 gap | Lines stop short at the corner; -> joined like FILLET; -> 1 L-shaped polyline |
| 5 | L corner, 3 overshoot | Lines run past the corner; -> trimmed to the corner; -> 1 L-shaped polyline |
| 6 | T junction, not open | Open junctions OFF: 2 polylines; Open junctions ON:  1 T polyline |
| 7 | T junction, open | -> 1 T polyline |
| 8 | Cross (+), not open | Open junctions ON: all 4 faces cut; -> 1 cross polyline |
| 9 | Door opening with jambs (90) | Jamb lines close the wall ends; -> 2 rectangles, jambs not cut |
| 10 | Door opening, open ends (90) | No jamb lines; -> ends capped: 2 rectangles |
| 11 | Window opening (120) | Window lines on PENCERE layer; Wall layers = DUVAR:; -> 2 wall rectangles, window untouched |
| 12 | 2 walls 30 apart + crossing wall | Open junctions ON: -> 1 polyline; crossing wall kept in the 30 gap; (the gap is not a wall) |
| 13 | Corridor 120 wide | Faces 120 apart: not one wall; -> 2 separate wall rectangles |
| 14 | Closed wall ring, no columns | -> outer + inner polyline; (inner = hatch island) |
| 15 | L corner at a column | Walls run into the column; -> 2 rectangles, capped; at the column faces |
| 16 | U wall drawn with polylines | Two open polylines (faces); -> 1 U-shaped polyline |
| 17 | Oblique wall (60 deg) on a face | Faces end at different points; Open junctions OFF: 2 polylines; Open junctions ON:  1 polyline |
| 18 | L corner, one wall on layer 0 | Wall layers empty:; -> 2 polylines + report note; (move both to DUVAR) |
| 19 | Three faces 20 apart | Middle line has 2 partners; -> nothing guessed, reported |
| 20 | Small plan: columns, door, window, T | Trim inside: kolon, Wall layers: DUVAR; Open junctions + Closed polylines ON; -> 6 polylines (partition + wall = T) |

## Known limits shown by the drawing

- Scenario 19: three equally spaced lines are ambiguous; nothing is guessed.
