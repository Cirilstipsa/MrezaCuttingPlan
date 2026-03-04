using Autodesk.Revit.DB;

namespace MrezaCuttingPlan.Services
{
    /// <summary>
    /// Kreira ili osvježava Drafting View "Plan Rezanja Mreža" s tablicom rezultata.
    /// Ukupna širina tablice je fiksna (185 mm ≈ A4 s marginama).
    /// </summary>
    public class RevitViewExporter
    {
        // ── Fiksne dimenzije tablice (mm) ────────────────────────────────────────
        // Ukupno 185 mm (A4 210 mm − 2×12.5 mm margine)
        private const double Col0Mm = 75;  // Particija
        private const double Col1Mm = 35;  // Tip mreže
        private const double Col2Mm = 25;  // Limovi
        private const double Col3Mm = 50;  // Težina (kg)
        // Ukupno: 75+35+25+50 = 185 mm

        private const double PadXMm = 2.0; // horizontalni padding unutar ćelije
        private const double MinRowMm = 7.0; // minimalna visina reda

        private static double MmToFt(double mm) => mm / 304.8;

        // ─────────────────────────────────────────────────────────────────────────

        public static void Export(
            Document doc,
            IEnumerable<(string Partition, string TypeName, int SheetCount, double WeightKgPerM2)> rows,
            double sheetWCm, double sheetLCm)
        {
            var rowList = rows.ToList();

            // Pronađi postojeći view PRIJE transakcije –
            // FilteredElementCollector scoped na view.Id ne smije se koristiti
            // unutar iste transakcije u kojoj je view kreiran.
            var existingView = FindExistingDraftingView(doc);

            using var tx = new Transaction(doc, "Kreiraj Plan Rezanja Mreža view");
            tx.Start();

            ViewDrafting view;
            if (existingView != null)
            {
                view = existingView;
                // Postavi scale na 1:1 (model coords = paper coords)
                if (view.Scale != 1)
                    view.Scale = 1;

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

            DrawTable(doc, view, rowList, sheetWCm, sheetLCm);
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
            v.Scale = 1; // 1:1 → model koordinate = paper koordinate
            return v;
        }

        // ── Table drawing ─────────────────────────────────────────────────────────

        private static void DrawTable(
            Document doc,
            ViewDrafting view,
            List<(string Partition, string TypeName, int SheetCount, double WeightKgPerM2)> rows,
            double sheetWCm, double sheetLCm)
        {
            ElementId textTypeId = GetTextTypeId(doc);
            double th = GetTextHeight(doc, textTypeId); // visina teksta u feet

            // Visina reda: dovoljno za tekst + padding, ali ne manje od MinRowMm
            double rowH = Math.Max(th * 2.5, MmToFt(MinRowMm));
            // Vertikalni padding: centrira tekst unutar reda
            double padY = (rowH - th) / 2.0;
            double padX = MmToFt(PadXMm);

            // X granice kolona (u feet)
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
            PlaceText(doc, view, textTypeId, x2 + padX, -padY, "Limovi");
            PlaceText(doc, view, textTypeId, x3 + padX, -padY, "Težina (kg)");
            DrawHLine(doc, view, x0, x4, -rowH);

            // ── Data redovi ───────────────────────────────────────────────────────
            for (int i = 0; i < rows.Count; i++)
            {
                var (partition, typeName, sheetCount, weightKgPerM2) = rows[i];
                double rowTop = -(i + 1) * rowH;
                double textY  = rowTop - padY;
                double weight = CalcWeight(typeName, sheetCount, sheetWCm, sheetLCm, weightKgPerM2);

                PlaceText(doc, view, textTypeId, x0 + padX, textY, partition);
                PlaceText(doc, view, textTypeId, x1 + padX, textY, typeName);
                PlaceText(doc, view, textTypeId, x2 + padX, textY, sheetCount.ToString());
                PlaceText(doc, view, textTypeId, x3 + padX, textY, $"{weight:F0}");

                DrawHLine(doc, view, x0, x4, rowTop - rowH);
            }

            // ── Vertikalne linije ─────────────────────────────────────────────────
            DrawVLine(doc, view, x0, 0, -tableH);
            DrawVLine(doc, view, x1, 0, -tableH);
            DrawVLine(doc, view, x2, 0, -tableH);
            DrawVLine(doc, view, x3, 0, -tableH);
            DrawVLine(doc, view, x4, 0, -tableH);
        }

        /// <summary>
        /// Kreira TextNote koristeći 5-parametarski overload (bez rotacije i bez
        /// forsiranja širine). Y koordinata = gornji rub teksta.
        /// VAŽNO: ne koristiti overload s double parametrom – to je rotacija u
        /// radijanima, NE širina, što bi rotiralo tekst.
        /// </summary>
        private static void PlaceText(
            Document doc, ViewDrafting view, ElementId typeId,
            double x, double y, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var opts = new TextNoteOptions(typeId)
            {
                HorizontalAlignment = HorizontalTextAlignment.Left
            };

            // 5-param overload: Create(doc, viewId, origin, text, opts)
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

        // ── TextNoteType helpers ──────────────────────────────────────────────────

        private static ElementId GetTextTypeId(Document doc)
        {
            var types = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .ToList();

            if (types.Count == 0)
                throw new InvalidOperationException(
                    "Dokument ne sadrži nijedan TextNoteType.");

            // Najmanji dostupni tip
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
                    return p.AsDouble(); // interni Revit units (feet)
            }
            return MmToFt(2.5); // fallback
        }

        // ── Weight calculation ────────────────────────────────────────────────────

        private static double CalcWeight(
            string typeName, int sheetCount,
            double sheetWCm, double sheetLCm,
            double weightKgPerM2)
        {
            double areaM2 = (sheetWCm / 100.0) * (sheetLCm / 100.0);

            // Koristi podatak iz Revit famile ako je dostupan
            if (weightKgPerM2 > 0)
                return sheetCount * areaM2 * weightKgPerM2;

            // Fallback: aproksimativna formula (As × gustoća)
            string digits = new string(typeName.Where(char.IsDigit).ToArray());
            if (!int.TryParse(digits, out int num)) return 0;

            double kgPerM2 = typeName.StartsWith("Q", StringComparison.OrdinalIgnoreCase)
                ? num * 2 * 7.85 / 1000.0      // Q: oba smjera
                : num * 1.15 * 7.85 / 1000.0;  // R: glavni + distribucija

            return sheetCount * areaM2 * kgPerM2;
        }
    }
}
