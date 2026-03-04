namespace MrezaCuttingPlan.Helpers
{
    /// <summary>
    /// Konverzija Revit internih jedinica (feet) u centimetre i obrnuto.
    /// </summary>
    public static class UnitConverter
    {
        private const double FeetPerCm = 1.0 / 30.48;
        private const double CmPerFoot = 30.48;

        /// <summary>Pretvara feet u centimetre.</summary>
        public static double FeetToCm(double feet) => feet * CmPerFoot;

        /// <summary>Pretvara centimetre u feet.</summary>
        public static double CmToFeet(double cm) => cm * FeetPerCm;

        /// <summary>Pretvara feet u metre.</summary>
        public static double FeetToMeters(double feet) => feet * 0.3048;
    }
}
