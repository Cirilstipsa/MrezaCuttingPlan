namespace MrezaCuttingPlan.Models
{
    /// <summary>
    /// Jedan standardni lim s rasporedom izrezanih komada.
    /// </summary>
    public class CuttingSheet
    {
        public int SheetIndex { get; set; }
        public string MeshTypeName { get; set; } = string.Empty;
        public double SheetWidth { get; set; }   // cm
        public double SheetLength { get; set; }  // cm

        public List<PlacedPiece> PlacedPieces { get; set; } = new();

        public double UsedArea => PlacedPieces.Sum(p => p.PlacedWidth * p.PlacedLength);
        public double TotalArea => SheetWidth * SheetLength;
        public double EfficiencyPercent => TotalArea > 0 ? (UsedArea / TotalArea) * 100 : 0;
        public double WastePercent => 100 - EfficiencyPercent;
    }
}
