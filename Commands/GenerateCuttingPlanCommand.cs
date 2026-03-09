using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MrezaCuttingPlan.Models;
using MrezaCuttingPlan.Services;
using MrezaCuttingPlan.UI;
using System.IO;

namespace MrezaCuttingPlan.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateCuttingPlanCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiApp = commandData.Application;
            var doc = uiApp.ActiveUIDocument.Document;

            try
            {
                // 1. Otvori SettingsDialog
                var dialog = new SettingsDialog();
                bool? dialogResult = dialog.ShowDialog();

                if (dialogResult != true || !dialog.Confirmed)
                    return Result.Cancelled;

                double sheetW = dialog.SheetWidth;
                double sheetL = dialog.SheetLength;

                // 2. Ekstrahiraj FabricSheet elemente
                var extractor = new FabricSheetExtractor();
                List<MeshPiece> allPieces = extractor.Extract(doc);

                if (allPieces.Count == 0)
                {
                    TaskDialog.Show("Plan Rezanja Mreža",
                        "U modelu nisu pronađeni postavljeni FabricSheet elementi.\n\n" +
                        "Provjerite da li model sadrži armaturne mreže (FabricSheet/FabricArea).");
                    return Result.Succeeded;
                }

                // 3. Provjeri preglomazne komade
                var optimizer = new CuttingOptimizer();
                var oversized = optimizer.FindOversized(allPieces, sheetW, sheetL);

                if (oversized.Count > 0)
                {
                    string oversizedList = string.Join("\n",
                        oversized.Take(10).Select(p =>
                            $"  • {p.TypeName} [{p.RevitId}]: {p.Width:F0}×{p.Length:F0} cm"));

                    if (oversized.Count > 10)
                        oversizedList += $"\n  ... i još {oversized.Count - 10} komada";

                    var td = new TaskDialog("Plan Rezanja – Preglomazni komadi")
                    {
                        MainContent = $"Pronađeno {oversized.Count} komada koji su veći od " +
                                      $"standardnog lima ({sheetW:F0}×{sheetL:F0} cm):\n\n{oversizedList}\n\n" +
                                      "Ovi komadi biti će izostavljeni iz plana rezanja.",
                        CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                    };

                    if (td.Show() == TaskDialogResult.Cancel)
                        return Result.Cancelled;
                }

                // 4. Grupiraj po (Particija, Tip) i optimiziraj
                var grouped = allPieces
                    .Where(p => !oversized.Contains(p))
                    .GroupBy(p => (p.Partition, p.TypeName))
                    .ToDictionary(g => g.Key, g => g.ToList());

                var resultsByType = new Dictionary<(string Partition, string TypeName), List<CuttingSheet>>();
                foreach (var kvp in grouped)
                {
                    var sheets = optimizer.Optimize(kvp.Value, sheetW, sheetL, kvp.Key.TypeName);
                    resultsByType[kvp.Key] = sheets;
                }

                // 5. Kreiraj Drafting View tablicu
                var tableRows = resultsByType
                    .Select(kvp =>
                    {
                        var pieces = grouped[kvp.Key];
                        int standardSheets = kvp.Value.Count;
                        double weightKgSheet = pieces.Select(p => p.WeightKgSheet).FirstOrDefault(w => w > 0);

                        // Težina = broj standardnih limova × težina jednog standardnog lima
                        double totalWeightKg;
                        if (weightKgSheet > 0)
                        {
                            totalWeightKg = standardSheets * weightKgSheet;
                        }
                        else
                        {
                            // Fallback: aproksimativna formula
                            string typeName = kvp.Key.TypeName;
                            string digits = new string(typeName.Where(char.IsDigit).ToArray());
                            if (int.TryParse(digits, out int num))
                            {
                                double areaM2 = (sheetW / 100.0) * (sheetL / 100.0);
                                double kgPerM2 = typeName.StartsWith("Q", StringComparison.OrdinalIgnoreCase)
                                    ? num * 2 * 7.85 / 1000.0
                                    : num * 1.15 * 7.85 / 1000.0;
                                totalWeightKg = standardSheets * areaM2 * kgPerM2;
                            }
                            else totalWeightKg = 0;
                        }

                        return (kvp.Key.Partition, kvp.Key.TypeName,
                                StandardSheets: standardSheets,
                                PieceCount: pieces.Count,
                                TotalWeightKg: totalWeightKg);
                    })
                    .OrderBy(r => r.Partition)
                    .ThenBy(r => r.TypeName)
                    .ToList();

                RevitViewExporter.Export(doc, tableRows, sheetW, sheetL);

                // 6. Naziv projekta (potreban za PDF i summary)
                string projectName = Path.GetFileNameWithoutExtension(doc.PathName);
                if (string.IsNullOrWhiteSpace(projectName))
                    projectName = "Neimenovani projekt";

                // 7. PDF export (ako je putanja unesena)
                if (!string.IsNullOrWhiteSpace(dialog.PdfOutputPath))
                {
                    try
                    {
                        PdfExporter.Export(resultsByType, projectName, dialog.PdfOutputPath, sheetW, sheetL);
                    }
                    catch (Exception pdfEx)
                    {
                        TaskDialog.Show("PDF – Upozorenje",
                            $"Plan rezanja je kreiran u Revitu, ali PDF export nije uspio:\n\n{pdfEx.Message}");
                    }
                }

                int totalPieces = grouped.Values.Sum(l => l.Count);
                int totalSheets = resultsByType.Values.Sum(l => l.Count);

                string summary = $"Projekt: {projectName}\n" +
                                 $"Standardni lim: {sheetW:F0}×{sheetL:F0} cm\n\n" +
                                 $"Ukupno komada: {totalPieces}\n" +
                                 $"Ukupno standardnih limova: {totalSheets}\n" +
                                 $"──────────────────────────\n";

                foreach (var kvp in resultsByType.OrderBy(k => k.Key.Partition).ThenBy(k => k.Key.TypeName))
                {
                    double avgWaste = kvp.Value.Average(s => s.WastePercent);
                    int komada = kvp.Value.Sum(s => s.PlacedPieces.Count);
                    string label = string.IsNullOrEmpty(kvp.Key.Partition) || kvp.Key.Partition == "Bez particije"
                        ? kvp.Key.TypeName
                        : $"{kvp.Key.Partition} / {kvp.Key.TypeName}";
                    summary += $"{label}: {komada} kom → {kvp.Value.Count} limova " +
                               $"(otpad {avgWaste:F1}%)\n";
                }

                summary += "\nView 'Plan Rezanja Mreža' kreiran u Project Browser-u.";

                // Debug: prikaži sve ekstrahirane tipove (za dijagnostiku nestajućih tipova)
                var allTypes = allPieces
                    .GroupBy(p => (p.Partition, p.TypeName))
                    .Select(g => $"  {g.Key.Partition} / {g.Key.TypeName}: {g.Count()} kom (dim: {g.First().Width:F0}×{g.First().Length:F0})")
                    .OrderBy(s => s);
                string debugInfo = "SVI EKSTR. KOMADI:\n" + string.Join("\n", allTypes);
                TaskDialog.Show("Debug – Ekstrahirani komadi", debugInfo);

                TaskDialog.Show("Plan Rezanja Mreža – Rezultat", summary);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Greška pri generiranju plana rezanja:\n{ex.Message}\n\n{ex.StackTrace}";
                TaskDialog.Show("Plan Rezanja – Greška", message);
                return Result.Failed;
            }
        }
    }
}
