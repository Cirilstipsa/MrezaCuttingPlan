using MrezaCuttingPlan.Models;

namespace MrezaCuttingPlan.Algorithms
{
    /// <summary>
    /// 2D Guillotine Cut bin packing algoritam s BSSF (Best Short Side Fit) heuristikom.
    /// Drži listu slobodnih pravokutnih zona unutar jednog standardnog lima.
    /// </summary>
    public class GuillotinePacker
    {
        private readonly List<Rect> _freeRects;
        private readonly double _sheetW;
        private readonly double _sheetL;
        private readonly List<PlacedPiece> _placed;

        public IReadOnlyList<PlacedPiece> PlacedPieces => _placed;

        public GuillotinePacker(double sheetW, double sheetL)
        {
            _sheetW = sheetW;
            _sheetL = sheetL;
            _freeRects = new List<Rect>
            {
                new Rect(0, 0, sheetW, sheetL)
            };
            _placed = new List<PlacedPiece>();
        }

        /// <summary>
        /// Pokušaj smjestiti komad u lim.
        /// Vraća true i postavlja result ako je smještanje uspjelo.
        /// </summary>
        public bool TryPack(MeshPiece piece, out PlacedPiece? result)
        {
            result = null;

            // Pronađi best fit za normalnu orijentaciju
            var (normalRect, normalScore) = FindBestRect(piece.Width, piece.Length);

            Rect? bestRect = null;
            bool bestRotated = false;
            double bestScore = double.MaxValue;

            if (normalRect.HasValue && normalScore < bestScore)
            {
                bestRect = normalRect;
                bestScore = normalScore;
                bestRotated = false;
            }

            // Proba rotiranu verziju samo za Q mreže
            if (piece.CanRotate && Math.Abs(piece.Width - piece.Length) > 0.01)
            {
                var (rotRect, rotScore) = FindBestRect(piece.Length, piece.Width);
                if (rotRect.HasValue && rotScore < bestScore)
                {
                    bestRect = rotRect;
                    bestScore = rotScore;
                    bestRotated = true;
                }
            }

            if (!bestRect.HasValue)
                return false;

            double placedW = bestRotated ? piece.Length : piece.Width;
            double placedL = bestRotated ? piece.Width : piece.Length;

            var placed = new PlacedPiece
            {
                Piece = piece,
                X = bestRect.Value.X,
                Y = bestRect.Value.Y,
                IsRotated = bestRotated
            };

            _placed.Add(placed);

            // Podijeli slobodni pravokutnik koji smo koristili
            var usedRect = new Rect(bestRect.Value.X, bestRect.Value.Y, placedW, placedL);
            SplitFreeRect(usedRect, bestRect.Value);

            result = placed;
            return true;
        }

        /// <summary>
        /// Pronalazi slobodni pravokutnik koji može primiti komad dimenzija w×l.
        /// Koristi Best Short Side Fit (BSSF) heuristiku:
        /// minimizira max(leftoverW, leftoverL) za bolji fit.
        /// </summary>
        private (Rect? rect, double score) FindBestRect(double w, double l)
        {
            Rect? best = null;
            double bestScore = double.MaxValue;

            foreach (var fr in _freeRects)
            {
                if (fr.Width >= w - 0.5 && fr.Height >= l - 0.5)
                {
                    double leftoverW = fr.Width - w;
                    double leftoverL = fr.Height - l;
                    // BSSF: minimizira kraći ostatak (bolje popunjavanje)
                    double score = Math.Min(leftoverW, leftoverL);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = fr;
                    }
                }
            }

            return (best, bestScore);
        }

        /// <summary>
        /// Nakon postavljanja komada u slobodni pravokutnik, giljotinski reže
        /// preostali slobodni prostor na 2 nova slobodna pravokutnika.
        /// Koristi Longer Axis Split Rule: reže duž dulje osi za bolje rezultate.
        /// </summary>
        private void SplitFreeRect(Rect usedRect, Rect freeRect)
        {
            _freeRects.Remove(freeRect);

            double rightW = freeRect.Width - usedRect.Width;
            double topH = freeRect.Height - usedRect.Height;

            // Longer Axis Split Rule
            bool splitHorizontal = freeRect.Width > freeRect.Height;

            if (splitHorizontal)
            {
                // Desni pravokutnik (puna visina slobodnog)
                if (rightW > 0.5)
                {
                    _freeRects.Add(new Rect(
                        freeRect.X + usedRect.Width,
                        freeRect.Y,
                        rightW,
                        freeRect.Height
                    ));
                }
                // Gornji pravokutnik (samo širina korištenog komada)
                if (topH > 0.5)
                {
                    _freeRects.Add(new Rect(
                        freeRect.X,
                        freeRect.Y + usedRect.Height,
                        usedRect.Width,
                        topH
                    ));
                }
            }
            else
            {
                // Gornji pravokutnik (puna širina slobodnog)
                if (topH > 0.5)
                {
                    _freeRects.Add(new Rect(
                        freeRect.X,
                        freeRect.Y + usedRect.Height,
                        freeRect.Width,
                        topH
                    ));
                }
                // Desni pravokutnik (samo visina korištenog komada)
                if (rightW > 0.5)
                {
                    _freeRects.Add(new Rect(
                        freeRect.X + usedRect.Width,
                        freeRect.Y,
                        rightW,
                        usedRect.Height
                    ));
                }
            }
        }

        /// <summary>Interna struktura slobodnog/korištenog pravokutnika.</summary>
        private readonly struct Rect
        {
            public double X { get; }
            public double Y { get; }
            public double Width { get; }
            public double Height { get; }

            public Rect(double x, double y, double width, double height)
            {
                X = x; Y = y; Width = width; Height = height;
            }

            public override string ToString() =>
                $"[{X:F1},{Y:F1}] {Width:F1}×{Height:F1}";
        }
    }
}
