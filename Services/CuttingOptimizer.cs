using MrezaCuttingPlan.Algorithms;
using MrezaCuttingPlan.Models;

namespace MrezaCuttingPlan.Services
{
    /// <summary>
    /// Koordinira bin packing za listu komada jednog tipa mreže.
    /// Koristi First Fit Decreasing (FFD) + GuillotinePacker.
    /// </summary>
    public class CuttingOptimizer
    {
        /// <summary>
        /// Optimizira raspored komada unutar standardnih limova.
        /// </summary>
        /// <param name="pieces">Lista komada istog tipa mreže.</param>
        /// <param name="sheetW">Širina standardnog lima u cm.</param>
        /// <param name="sheetL">Dužina standardnog lima u cm.</param>
        /// <param name="typeName">Naziv tipa mreže (za CuttingSheet).</param>
        /// <returns>Lista popunjenih standardnih limova.</returns>
        public List<CuttingSheet> Optimize(
            List<MeshPiece> pieces,
            double sheetW,
            double sheetL,
            string typeName)
        {
            var sheets = new List<CuttingSheet>();
            var packers = new List<GuillotinePacker>();
            var oversized = new List<MeshPiece>();

            // FFD: sortiraj po površini descending
            var sorted = pieces
                .OrderByDescending(p => p.Area)
                .ToList();

            foreach (var piece in sorted)
            {
                // Provjeri da li je komad veći od standardnog lima
                bool fitNormal = piece.Width <= sheetW + 0.5 && piece.Length <= sheetL + 0.5;
                bool fitRotated = piece.CanRotate &&
                                  piece.Length <= sheetW + 0.5 &&
                                  piece.Width <= sheetL + 0.5;

                if (!fitNormal && !fitRotated)
                {
                    oversized.Add(piece);
                    continue;
                }

                bool packed = false;

                // Pokušaj smjestiti u postojeće limove (po redu)
                for (int i = 0; i < packers.Count; i++)
                {
                    if (packers[i].TryPack(piece, out var placed))
                    {
                        sheets[i].PlacedPieces.Add(placed!);
                        packed = true;
                        break;
                    }
                }

                if (!packed)
                {
                    // Otvori novi lim
                    var newPacker = new GuillotinePacker(sheetW, sheetL);
                    var newSheet = new CuttingSheet
                    {
                        SheetIndex = sheets.Count + 1,
                        MeshTypeName = typeName,
                        SheetWidth = sheetW,
                        SheetLength = sheetL
                    };

                    if (newPacker.TryPack(piece, out var placed))
                    {
                        newSheet.PlacedPieces.Add(placed!);
                        packers.Add(newPacker);
                        sheets.Add(newSheet);
                    }
                    else
                    {
                        // Ne bi smjelo doći ovdje (već provjereno gore), ali za sigurnost
                        oversized.Add(piece);
                    }
                }
            }

            return sheets;
        }

        /// <summary>
        /// Vraća komade koji ne stanu u standardni lim.
        /// </summary>
        public List<MeshPiece> FindOversized(
            List<MeshPiece> pieces,
            double sheetW,
            double sheetL)
        {
            return pieces.Where(p =>
            {
                bool fitNormal = p.Width <= sheetW + 0.5 && p.Length <= sheetL + 0.5;
                bool fitRotated = p.CanRotate &&
                                  p.Length <= sheetW + 0.5 &&
                                  p.Width <= sheetL + 0.5;
                return !fitNormal && !fitRotated;
            }).ToList();
        }
    }
}
