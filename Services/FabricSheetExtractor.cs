using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using MrezaCuttingPlan.Helpers;
using MrezaCuttingPlan.Models;

namespace MrezaCuttingPlan.Services
{
    /// <summary>
    /// Čita FabricSheet elemente iz Revit dokumenta i pretvara ih u MeshPiece liste.
    /// </summary>
    public class FabricSheetExtractor
    {
        public List<MeshPiece> Extract(Document doc)
        {
            var result = new List<MeshPiece>();

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(FabricSheet))
                .WhereElementIsNotElementType();

            foreach (Element elem in collector)
            {
                if (elem is not FabricSheet sheet)
                    continue;

                // Provjeri da li je sheet placed (ima valjani host ili BoundingBox)
                if (!IsPlaced(sheet, doc))
                    continue;

                string typeName = GetTypeName(sheet, doc);
                double widthCm = GetDimension(sheet, BuiltInParameter.FABRIC_SHEET_WIDTH, doc);
                double lengthCm = GetDimension(sheet, BuiltInParameter.FABRIC_SHEET_LENGTH, doc);

                // Preskoči ako nema validnih dimenzija
                if (widthCm <= 0 || lengthCm <= 0)
                    continue;

                bool canRotate = typeName.StartsWith("Q", StringComparison.OrdinalIgnoreCase);
                string partition = GetPartition(sheet, doc);
                double weightKgPerM2 = GetWeightKgPerM2(sheet, doc, widthCm, lengthCm);

                result.Add(new MeshPiece
                {
                    RevitId = (int)sheet.Id.Value,
                    TypeName = typeName,
                    Width = widthCm,
                    Length = lengthCm,
                    CanRotate = canRotate,
                    Partition = partition,
                    WeightKgPerM2 = weightKgPerM2
                });
            }

            return result;
        }

        private static bool IsPlaced(FabricSheet sheet, Document doc)
        {
            // Ima host element (dio FabricArea)
            if (sheet.HostId != ElementId.InvalidElementId)
                return true;

            // Ili ima valjani BoundingBox (direktno placed)
            try
            {
                var bb = sheet.get_BoundingBox(null);
                return bb != null;
            }
            catch
            {
                return false;
            }
        }

        private static string GetTypeName(FabricSheet sheet, Document doc)
        {
            try
            {
                if (doc.GetElement(sheet.GetTypeId()) is FabricSheetType sheetType)
                {
                    string fullName = sheetType.Name;
                    // Uzmi samo prvi dio do razmaka (npr. "Q335 A" → "Q335")
                    int spaceIdx = fullName.IndexOf(' ');
                    return spaceIdx > 0 ? fullName[..spaceIdx] : fullName;
                }
            }
            catch { }

            return "NEPOZNATO";
        }

        /// <summary>
        /// Čita težinu mreže iz Revit parametara FabricSheetType ili FabricSheet instance.
        ///
        /// Prioritet:
        ///   1. "Sheet Mass"              → ukupna težina lima (kg), dijeli s površinom
        ///   2. "Sheet mass per unit area"→ već kg/m², koristi direktno
        ///   3. Ostali uobičajeni nazivi  → heuristika za kg vs kg/m²
        ///
        /// Vraća kg/m² ili 0 ako parametar nije pronađen (tada se koristi formula).
        /// </summary>
        private static double GetWeightKgPerM2(FabricSheet sheet, Document doc,
            double widthCm, double lengthCm)
        {
            double areaM2 = (widthCm / 100.0) * (lengthCm / 100.0);
            if (areaM2 <= 0) return 0;

            try
            {
                var sheetType = doc.GetElement(sheet.GetTypeId()) as FabricSheetType;

                // ── 1. "Sheet Mass" = ukupna težina cijelog lima ──────────────────
                // Gledamo na tipu i na instanci (user parametar može biti na oba mjesta)
                double sheetMass = ReadDouble(sheetType, "Sheet Mass")
                               ?? ReadDouble(sheet, "Sheet Mass")
                               ?? 0;
                if (sheetMass > 0)
                {
                    // "Sheet Mass" je ukupna kg – trebamo kg/m²
                    // Ako je Revit interni (lb → kg konverzija 0.4536): val < 5 za tipičnu mrežu
                    // U praksi user unosi u kg kao Number ili Mass param → val ≈ 30-500
                    double kg = sheetMass > 5
                        ? sheetMass                    // Number parametar, već kg
                        : sheetMass * 453.592;         // slug ili lb – konvertuj u kg (edge case)
                    return kg / areaM2;
                }

                // ── 2. "Sheet mass per unit area" = kg/m² (Revit ga računa) ───────
                double perM2 = ReadDouble(sheetType, "Sheet mass per unit area")
                            ?? ReadDouble(sheet, "Sheet mass per unit area")
                            ?? 0;
                if (perM2 > 0)
                {
                    // Ako je Revit interni (lb/ft² → kg/m² faktor 4.882): val < 3 za tipičnu mrežu
                    return perM2 > 3 ? perM2 : perM2 * 4.88243;
                }

                // ── 3. Fallback: ostali uobičajeni nazivi ─────────────────────────
                string[] totalKgNames = { "Weight", "Težina", "Masa", "Mass", "Sheet Weight", "Total Weight" };
                foreach (string name in totalKgNames)
                {
                    double val = ReadDouble(sheetType, name) ?? ReadDouble(sheet, name) ?? 0;
                    if (val <= 0) continue;

                    if (val > 15) return val / areaM2;   // ukupna kg
                    if (val > 1)  return val;             // kg/m²
                    if (val > 0.1) return val * 4.88243; // lb/ft²
                }
            }
            catch { }

            return 0;
        }

        /// <summary>Čita Double parametar s elementa po nazivu. Null ako nije pronađen ili je 0.</summary>
        private static double? ReadDouble(Element? elem, string paramName)
        {
            if (elem == null) return null;
            var p = elem.LookupParameter(paramName);
            if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return null;
            double val = p.AsDouble();
            return val > 0 ? val : null;
        }

        private static string GetPartition(FabricSheet sheet, Document doc)
        {
            try
            {
                if (sheet.HostId != ElementId.InvalidElementId)
                {
                    var host = doc.GetElement(sheet.HostId);
                    var p = host?.LookupParameter("PARTITION");
                    if (p != null && p.HasValue)
                        return p.AsString() ?? string.Empty;
                }
            }
            catch { }
            return "Bez particije";
        }

        private static double GetDimension(FabricSheet sheet, BuiltInParameter param, Document doc)
        {
            try
            {
                var p = sheet.get_Parameter(param);
                if (p != null && p.HasValue)
                    return UnitConverter.FeetToCm(p.AsDouble());
            }
            catch { }

            // Fallback na BoundingBox dimenzije
            try
            {
                var bb = sheet.get_BoundingBox(null);
                if (bb != null)
                {
                    double dx = UnitConverter.FeetToCm(bb.Max.X - bb.Min.X);
                    double dy = UnitConverter.FeetToCm(bb.Max.Y - bb.Min.Y);

                    if (param == BuiltInParameter.FABRIC_SHEET_WIDTH)
                        return Math.Min(dx, dy);
                    else
                        return Math.Max(dx, dy);
                }
            }
            catch { }

            return 0;
        }
    }
}
