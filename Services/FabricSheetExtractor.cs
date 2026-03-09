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
                double widthCm  = GetCutDimension(sheet, doc, isWidth: true);
                double lengthCm = GetCutDimension(sheet, doc, isWidth: false);

                // Preskoči ako nema validnih dimenzija
                if (widthCm <= 0 || lengthCm <= 0)
                    continue;

                bool canRotate = typeName.StartsWith("Q", StringComparison.OrdinalIgnoreCase);
                string partition = GetPartition(sheet, doc);
                double weightKgSheet = GetSheetMassKg(sheet, doc);

                result.Add(new MeshPiece
                {
                    RevitId       = (int)sheet.Id.Value,
                    TypeName      = typeName,
                    Width         = widthCm,
                    Length        = lengthCm,
                    CanRotate     = canRotate,
                    Partition     = partition,
                    WeightKgSheet = weightKgSheet,
                    FabricNumber  = GetFabricNumber(sheet)
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
        /// Čita "Sheet Mass" iz FabricSheetType type properties.
        /// Vraća ukupnu težinu jednog standardnog lima u kg, ili 0 ako nije pronađen.
        /// </summary>
        private static double GetSheetMassKg(FabricSheet sheet, Document doc)
        {
            try
            {
                var sheetType = doc.GetElement(sheet.GetTypeId()) as FabricSheetType;
                if (sheetType == null) return 0;

                double? val = ReadDouble(sheetType, "Sheet Mass")
                           ?? ReadDouble(sheet, "Sheet Mass");
                return val ?? 0;
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Čita "Fabric Number" instance parametar. Vraća prazno ako nije postavljen.
        /// Redoslijed pokušaja: instance "Fabric Number" → instance "Mark".
        /// </summary>
        private static string GetFabricNumber(FabricSheet sheet)
        {
            try
            {
                // 1. Instance parametar "Fabric Number" (po imenu)
                var p = sheet.LookupParameter("Fabric Number");
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                {
                    string val = p.AsString();
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }

                // 2. Fallback: standardni "Mark" parametar
                var mark = sheet.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                if (mark != null && mark.HasValue && mark.StorageType == StorageType.String)
                {
                    string val = mark.AsString();
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
            }
            catch { }
            return string.Empty;
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
                // 1. Direktno na FabricSheet instanci (NUMBER_PARTITION_PARAM)
                var p = sheet.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                if (p != null && p.HasValue)
                {
                    string val = p.StorageType == StorageType.String
                        ? p.AsString()
                        : p.AsInteger().ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }

                // 2. Na host FabricArea elementu
                if (sheet.HostId != ElementId.InvalidElementId)
                {
                    var host = doc.GetElement(sheet.HostId);
                    if (host != null)
                    {
                        var ph = host.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                        if (ph != null && ph.HasValue)
                        {
                            string val = ph.StorageType == StorageType.String
                                ? ph.AsString()
                                : ph.AsInteger().ToString();
                            if (!string.IsNullOrWhiteSpace(val))
                                return val;
                        }
                    }
                }
            }
            catch { }
            return "Bez particije";
        }

        /// <summary>
        /// Čita stvarnu placed/cut dimenziju FabricSheet instance.
        /// Koristi "Cut Overall Width" / "Cut Overall Length" instance parametre.
        /// </summary>
        private static double GetCutDimension(FabricSheet sheet, Document doc, bool isWidth)
        {
            // 1. "Cut Overall Width" / "Cut Overall Length" – stvarne cut dimenzije instance
            string paramName = isWidth ? "Cut Overall Width" : "Cut Overall Length";
            try
            {
                var p = sheet.LookupParameter(paramName);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    double val = UnitConverter.FeetToCm(p.AsDouble());
                    if (val > 1) return val;
                }
            }
            catch { }

            // 2. Fallback: BoundingBox (za direktno placed sheets)
            try
            {
                var bb = sheet.get_BoundingBox(null);
                if (bb != null)
                {
                    double dx = UnitConverter.FeetToCm(bb.Max.X - bb.Min.X);
                    double dy = UnitConverter.FeetToCm(bb.Max.Y - bb.Min.Y);
                    if (dx > 5 && dy > 5)
                        return isWidth ? Math.Min(dx, dy) : Math.Max(dx, dy);
                }
            }
            catch { }

            // 3. Fallback: tip mreže (kataloška dimenzija)
            try
            {
                if (doc.GetElement(sheet.GetTypeId()) is FabricSheetType sheetType)
                {
                    var builtIn = isWidth
                        ? BuiltInParameter.FABRIC_SHEET_WIDTH
                        : BuiltInParameter.FABRIC_SHEET_LENGTH;
                    var tp = sheetType.get_Parameter(builtIn);
                    if (tp != null && tp.HasValue)
                    {
                        double val = UnitConverter.FeetToCm(tp.AsDouble());
                        if (val > 1) return val;
                    }
                }
            }
            catch { }

            return 0;
        }
    }
}
