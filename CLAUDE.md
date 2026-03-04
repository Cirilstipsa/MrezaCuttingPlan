# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

Requires **.NET 8 SDK** and **Revit 2026** installed at `C:\Program Files\Autodesk\Revit 2026\`.

```bash
dotnet restore
dotnet build -c Release
```

The `CopyToRevitAddins` MSBuild target automatically copies `MrezaCuttingPlan.dll` and all NuGet deps (QuestPDF + transitive) into `%APPDATA%\Autodesk\Revit\Addins\2026\MrezaCuttingPlan\` after each build.

**Manual deployment** (one-time, not automated by build):
```
MrezaCuttingPlan.addin → %APPDATA%\Autodesk\Revit\Addins\2026\
```

There are no automated tests. Verification is done by loading the plugin in Revit 2026.

## Architecture

The plugin is a single-assembly Revit add-in (`OutputType=Library`, `net8.0-windows`). Revit API DLLs are referenced with `Private=false` (not copied to output). WPF is enabled (`UseWPF=true`) for the settings dialog.

**Execution flow** (`GenerateCuttingPlanCommand.Execute`):
1. `SettingsDialog` → user sets standard sheet dimensions (default 215×600 cm) and PDF output path
2. `FabricSheetExtractor.Extract(doc)` → queries all `FabricSheet` instances via `FilteredElementCollector`, reads dimensions via `BuiltInParameter.FABRIC_SHEET_WIDTH/LENGTH` (fallback to BoundingBox), strips type name suffix (e.g. `"Q335 A"` → `"Q335"`)
3. Pieces grouped by `TypeName`; pieces larger than the standard sheet are reported and skipped
4. `CuttingOptimizer.Optimize(pieces, sheetW, sheetL)` — FFD (sorted by area descending), one `GuillotinePacker` per open sheet, opens a new sheet when no existing one fits the piece
5. `PdfExporter.Export(resultsByType, projectName, outputPath)` — QuestPDF 2024 Community: title page → per-type summary page → per-sheet visual canvas pages

**Packing algorithm** (`GuillotinePacker`):
- Maintains a list of free rectangles within one standard sheet
- `TryPack` uses **BSSF (Best Short Side Fit)**: scores candidates by `Min(leftoverW, leftoverL)`, lower = better
- After placement, free rect is split into 2 using **Longer Axis Split Rule**
- Rotation is attempted only when `piece.CanRotate == true` (Q-type meshes); R-type meshes are never rotated

**Q vs R mesh distinction**: determined solely by `TypeName.StartsWith("Q")`. Q-meshes may rotate 90°; R-meshes have a wire direction and must not rotate.

**Units**: Revit stores all lengths internally in **feet**. `UnitConverter.FeetToCm(feet)` (`× 30.48`) converts to cm. All `MeshPiece`, `PlacedPiece`, and `CuttingSheet` fields use **centimetres**.

**PDF canvas**: `PdfExporter.DrawSheetOnCanvas` uses `ICanvas.DrawRectangle(Position, Size, string color)` and `ICanvas.DrawLine(Position, Position, float, string)` from QuestPDF 2024. Colors are 6-digit hex strings; `Colors.*` constants from `QuestPDF.Helpers` are plain strings. Piece colors are deterministic pastel hashes by `RevitId % 16`.

## Key Revit API notes

- `FabricSheet` elements placed as part of a `FabricArea` are still enumerable as individual instances; no special handling needed
- `FabricSheet.HostId != ElementId.InvalidElementId` → placed inside a FabricArea; `get_BoundingBox(null) != null` → directly placed
- `FabricSheetType` (element type) is retrieved via `doc.GetElement(sheet.GetTypeId())`
- The command is `[Transaction(TransactionMode.ReadOnly)]` — no model modifications occur
