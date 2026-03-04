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

                result.Add(new MeshPiece
                {
                    RevitId = sheet.Id.IntegerValue,
                    TypeName = typeName,
                    Width = widthCm,
                    Length = lengthCm,
                    CanRotate = canRotate
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
