using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MrezaCuttingPlan.Models;
using MrezaCuttingPlan.Services;
using MrezaCuttingPlan.UI;
using System.Diagnostics;

namespace MrezaCuttingPlan.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
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
                string pdfPath = dialog.PdfOutputPath!;

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

                // Provjeri preglomazne komade
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

                // 3. Grupiraj po tipu mreže
                var grouped = allPieces
                    .Where(p => !oversized.Contains(p))
                    .GroupBy(p => p.TypeName)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // 4. Optimiziraj raspored za svaki tip
                var resultsByType = new Dictionary<string, List<CuttingSheet>>();

                foreach (var kvp in grouped)
                {
                    var sheets = optimizer.Optimize(kvp.Value, sheetW, sheetL, kvp.Key);
                    resultsByType[kvp.Key] = sheets;
                }

                // 5. Generiraj PDF
                string projectName = GetProjectName(doc);
                var exporter = new PdfExporter();
                exporter.Export(resultsByType, projectName, pdfPath);

                // 6. Sažetak i otvaranje PDF-a
                int totalPieces = grouped.Values.Sum(l => l.Count);
                int totalSheets = resultsByType.Values.Sum(l => l.Count);

                string summary = $"Plan rezanja uspješno generiran!\n\n" +
                                 $"Ukupno komada: {totalPieces}\n" +
                                 $"Ukupno standardnih limova: {totalSheets}\n\n";

                foreach (var kvp in resultsByType)
                {
                    double avgWaste = kvp.Value.Average(s => s.WastePercent);
                    summary += $"  {kvp.Key}: {kvp.Value.Sum(s => s.PlacedPieces.Count)} kom, " +
                               $"{kvp.Value.Count} limova, otpad {avgWaste:F1}%\n";
                }

                summary += $"\nPDF: {pdfPath}";

                var resultDialog = new TaskDialog("Plan Rezanja Mreža – Završeno")
                {
                    MainContent = summary,
                    CommonButtons = TaskDialogCommonButtons.Close
                };
                resultDialog.AddCommandLink(
                    TaskDialogCommandLinkId.CommandLink1,
                    "Otvori PDF izvještaj");

                var dlgResult = resultDialog.Show();
                if (dlgResult == TaskDialogResult.CommandLink1)
                {
                    Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Greška pri generiranju plana rezanja:\n{ex.Message}\n\n{ex.StackTrace}";
                TaskDialog.Show("Plan Rezanja – Greška", message);
                return Result.Failed;
            }
        }

        private static string GetProjectName(Document doc)
        {
            try
            {
                var info = doc.ProjectInformation;
                string name = info?.Name ?? string.Empty;
                string number = info?.Number ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(number) && !string.IsNullOrWhiteSpace(name))
                    return $"{number} – {name}";
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
                if (!string.IsNullOrWhiteSpace(number))
                    return number;
            }
            catch { }

            return Path.GetFileNameWithoutExtension(doc.PathName) ?? "Neimenovani projekt";
        }
    }
}
