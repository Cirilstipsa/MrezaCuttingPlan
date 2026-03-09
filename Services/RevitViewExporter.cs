using Autodesk.Revit.DB;
using System.Globalization;

namespace MrezaCuttingPlan.Services
{
    /// <summary>
    /// Kreira ili osvježava Drafting View "Plan Rezanja Mreža" s tablicom rezultata.
    /// Ukupna širina tablice je fiksna (185 mm ≈ A4 s marginama).
    /// </summary>
    public class RevitViewExporter
    {
        // ── Fiksne dimenzije tablice (mm) ────────────────────────────────────────
        private const double Col0Mm = 60;  // Particija
        private const double Col1Mm = 50;  // Tip mreže
        private const double Col2Mm = 25;  // Komada
        private const double Col3Mm = 50;  // Težina (kg)
        // Ukupno: 75+35+25+50 = 185 mm

        private const double PadXMm  = 2.0;
        private const double MinRowMm = 7.0;

        private static double MmToFt(double mm) => mm / 304.8;

        // ─────────────────────────────────────────────────────────────────────────

        public static void Export(
            Document doc,
            IEnumerable<(string Partition, string TypeName, int StandardSheets, int PieceCount, double TotalWeightKg)> rows,
            double sheetWidthCm, double sheetLengthCm)
        {
            var rowList = rows.ToList();

            var existingView = FindExistingDraftingView(doc);

            using var tx = new Transaction(doc, "Kreiraj Plan Rezanja Mreža view");
            tx.Start();

            ViewDrafting view;
            if (existingView != null)
            {
                view = existingView;
                if (view.Scale != 1) view.Scale = 1;

                var idsToDelete = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(e => e is TextNote || e is DetailCurve)
                    .Select(e => e.Id)
                    .ToList();
                if (idsToDelete.Count > 0)
                    doc.Delete(idsToDelete);
            }
            else
            {
                view = CreateDraftingView(doc);
            }

            DrawTable(doc, view, rowList, sheetWidthCm, sheetLengthCm);
            tx.Commit();
        }

        // ── View helpers ──────────────────────────────────────────────────────────

        private static ViewDrafting? FindExistingDraftingView(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .FirstOrDefault(v => v.Name == "Plan Rezanja Mreža");

        private static ViewDrafting CreateDraftingView(Document doc)
        {
            var typeId = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .First(vft => vft.ViewFamily == ViewFamily.Drafting)
                .Id;
            var v = ViewDrafting.Create(doc, typeId);
            v.Name = "Plan Rezanja Mreža";
            v.Scale = 1;
            return v;
        }

        // ── Table drawing ─────────────────────────────────────────────────────────

        private static void DrawTable(
            Document doc,
            ViewDrafting view,
            List<(string Partition, string TypeName, int StandardSheets, int PieceCount, double TotalWeightKg)> rows,
            double sheetWidthCm, double sheetLengthCm)
        {
            ElementId textTypeId = GetTextTypeId(doc);
            double th   = GetTextHeight(doc, textTypeId);
            double rowH = Math.Max(th * 2.5, MmToFt(MinRowMm));
            double padY = (rowH - th) / 2.0;
            double padX = MmToFt(PadXMm);

            double x0 = 0;
            double x1 = x0 + MmToFt(Col0Mm);
            double x2 = x1 + MmToFt(Col1Mm);
            double x3 = x2 + MmToFt(Col2Mm);
            double x4 = x3 + MmToFt(Col3Mm);

            double tableH = rowH * (rows.Count + 1);

            // ── Header ───────────────────────────────────────────────────────────
            DrawHLine(doc, view, x0, x4, 0);
            PlaceText(doc, view, textTypeId, x0 + padX, -padY, "Particija");
            PlaceText(doc, view, textTypeId, x1 + padX, -padY, "Tip mreže");
            PlaceText(doc, view, textTypeId, x2 + padX, -padY, "Br. komada");
            PlaceText(doc, view, textTypeId, x3 + padX, -padY, "Težina (kg)");
            DrawHLine(doc, view, x0, x4, -rowH);

            // ── Data redovi ───────────────────────────────────────────────────────
            for (int i = 0; i < rows.Count; i++)
            {
                var (partition, typeName, _, pieceCount, totalWeightKg) = rows[i];
                double rowTop = -(i + 1) * rowH;
                double textY  = rowTop - padY;

                string typeDisplay = $"{typeName} ({sheetLengthCm:F0}×{sheetWidthCm:F0} cm)";

                PlaceText(doc, view, textTypeId, x0 + padX, textY, partition);
                PlaceText(doc, view, textTypeId, x1 + padX, textY, typeDisplay);
                PlaceText(doc, view, textTypeId, x2 + padX, textY, pieceCount.ToString());
                PlaceText(doc, view, textTypeId, x3 + padX, textY, FormatWeight(totalWeightKg));

                DrawHLine(doc, view, x0, x4, rowTop - rowH);
            }

            // ── Vertikalne linije ─────────────────────────────────────────────────
            DrawVLine(doc, view, x0, 0, -tableH);
            DrawVLine(doc, view, x1, 0, -tableH);
            DrawVLine(doc, view, x2, 0, -tableH);
            DrawVLine(doc, view, x3, 0, -tableH);
            DrawVLine(doc, view, x4, 0, -tableH);

            // ── Rekapitulacijska tablica ──────────────────────────────────────────
            DrawRecapTable(doc, view, textTypeId, rows, rowH, padX, padY, -tableH - rowH, sheetWidthCm, sheetLengthCm);
        }

        private static void DrawRecapTable(
            Document doc, ViewDrafting view, ElementId textTypeId,
            List<(string Partition, string TypeName, int StandardSheets, int PieceCount, double TotalWeightKg)> rows,
            double rowH, double padX, double padY,
            double startY, double sheetWidthCm, double sheetLengthCm)
        {
            double rx0 = 0;
            double rx1 = rx0 + MmToFt(Col0Mm + Col1Mm); // 110 mm – Tip mreže
            double rx2 = rx1 + MmToFt(Col2Mm);           // 25 mm  – Br. komada
            double rx3 = rx2 + MmToFt(Col3Mm);           // 50 mm  – Težina

            // Grupiraj po tipu mreže – zbroji sve particije
            var recap = rows
                .GroupBy(r => r.TypeName)
                .Select(g => (
                    TypeName: g.Key,
                    PieceCount: g.Sum(r => r.PieceCount),
                    TotalWeightKg: g.Sum(r => r.TotalWeightKg)
                ))
                .OrderBy(r => r.TypeName)
                .ToList();

            double totalWeight    = recap.Sum(r => r.TotalWeightKg);
            int    totalPieces    = recap.Sum(r => r.PieceCount);
            double recapH         = rowH * (recap.Count + 2); // header + data + ukupno

            // Naslov
            PlaceText(doc, view, textTypeId, rx0 + padX, startY - padY, "REKAPITULACIJA – NARUDŽBA MREŽA");

            double tableStartY = startY - rowH;

            // Header
            DrawHLine(doc, view, rx0, rx3, tableStartY);
            PlaceText(doc, view, textTypeId, rx0 + padX, tableStartY - padY, "Tip mreže");
            PlaceText(doc, view, textTypeId, rx1 + padX, tableStartY - padY, "Br. komada");
            PlaceText(doc, view, textTypeId, rx2 + padX, tableStartY - padY, "Težina (kg)");
            DrawHLine(doc, view, rx0, rx3, tableStartY - rowH);

            // Data redovi
            for (int i = 0; i < recap.Count; i++)
            {
                var (typeName, pieceCount, weightKg) = recap[i];
                double rowTop = tableStartY - (i + 1) * rowH;
                string typeDisplay = $"{typeName} ({sheetLengthCm:F0}×{sheetWidthCm:F0} cm)";
                PlaceText(doc, view, textTypeId, rx0 + padX, rowTop - padY, typeDisplay);
                PlaceText(doc, view, textTypeId, rx1 + padX, rowTop - padY, pieceCount.ToString());
                PlaceText(doc, view, textTypeId, rx2 + padX, rowTop - padY, FormatWeight(weightKg));
                DrawHLine(doc, view, rx0, rx3, rowTop - rowH);
            }

            // Ukupno red
            double totalTop = tableStartY - (recap.Count + 1) * rowH;
            PlaceText(doc, view, textTypeId, rx0 + padX, totalTop - padY, "UKUPNO");
            PlaceText(doc, view, textTypeId, rx1 + padX, totalTop - padY, totalPieces.ToString());
            PlaceText(doc, view, textTypeId, rx2 + padX, totalTop - padY, FormatWeight(totalWeight));
            DrawHLine(doc, view, rx0, rx3, totalTop - rowH);

            // Vertikalne linije
            DrawVLine(doc, view, rx0, tableStartY, tableStartY - recapH);
            DrawVLine(doc, view, rx1, tableStartY, tableStartY - recapH);
            DrawVLine(doc, view, rx2, tableStartY, tableStartY - recapH);
            DrawVLine(doc, view, rx3, tableStartY, tableStartY - recapH);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void PlaceText(
            Document doc, ViewDrafting view, ElementId typeId,
            double x, double y, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var opts = new TextNoteOptions(typeId)
            {
                HorizontalAlignment = HorizontalTextAlignment.Left
            };
            TextNote.Create(doc, view.Id, new XYZ(x, y, 0), text, opts);
        }

        private static void DrawHLine(Document doc, ViewDrafting view,
            double x1, double x2, double y) =>
            doc.Create.NewDetailCurve(view,
                Line.CreateBound(new XYZ(x1, y, 0), new XYZ(x2, y, 0)));

        private static void DrawVLine(Document doc, ViewDrafting view,
            double x, double yTop, double yBottom) =>
            doc.Create.NewDetailCurve(view,
                Line.CreateBound(new XYZ(x, yTop, 0), new XYZ(x, yBottom, 0)));

        private static ElementId GetTextTypeId(Document doc)
        {
            var types = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .ToList();

            if (types.Count == 0)
                throw new InvalidOperationException("Dokument ne sadrži nijedan TextNoteType.");

            return types
                .OrderBy(t =>
                {
                    var p = t.get_Parameter(BuiltInParameter.TEXT_SIZE);
                    return p != null && p.HasValue ? p.AsDouble() : double.MaxValue;
                })
                .First()
                .Id;
        }

        private static double GetTextHeight(Document doc, ElementId typeId)
        {
            if (doc.GetElement(typeId) is TextNoteType tnt)
            {
                var p = tnt.get_Parameter(BuiltInParameter.TEXT_SIZE);
                if (p != null && p.HasValue && p.AsDouble() > 0)
                    return p.AsDouble();
            }
            return MmToFt(2.5);
        }

        private static string FormatWeight(double kg) =>
            kg.ToString("N2", new CultureInfo("hr-HR"));
    }
}
