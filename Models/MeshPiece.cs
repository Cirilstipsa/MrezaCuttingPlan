namespace MrezaCuttingPlan.Models
{
    /// <summary>
    /// Predstavlja jedan komad armaturne mreže iz Revit modela.
    /// </summary>
    public class MeshPiece
    {
        /// <summary>Revit element ID.</summary>
        public int RevitId { get; set; }

        /// <summary>Naziv tipa mreže, npr. "Q335", "R484".</summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>Širina komada u centimetrima.</summary>
        public double Width { get; set; }

        /// <summary>Dužina komada u centimetrima.</summary>
        public double Length { get; set; }

        /// <summary>Naziv particije iz FabricArea host elementa.</summary>
        public string Partition { get; set; } = string.Empty;

        /// <summary>
        /// Težina mreže u kg/m² pročitana iz Revit FabricSheetType.
        /// 0 = parametar nije pronađen, koristiti aproksimativnu formulu.
        /// </summary>
        public double WeightKgPerM2 { get; set; }

        /// <summary>
        /// True za Q mreže (mogu se rotirati 90°),
        /// False za R mreže (imaju smjer žica, ne smiju se rotirati).
        /// </summary>
        public bool CanRotate { get; set; }

        public double Area => Width * Length;

        public override string ToString() =>
            $"{TypeName} [{RevitId}] {Width:F0}×{Length:F0} cm" +
            (CanRotate ? " [Q]" : " [R]");
    }
}
