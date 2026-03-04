namespace MrezaCuttingPlan.Models
{
    /// <summary>
    /// Komad mreže s pozicijom unutar standardnog lima.
    /// </summary>
    public class PlacedPiece
    {
        public MeshPiece Piece { get; set; } = null!;

        /// <summary>X pozicija (lijevi rub) unutar standardnog lima, u cm.</summary>
        public double X { get; set; }

        /// <summary>Y pozicija (gornji rub) unutar standardnog lima, u cm.</summary>
        public double Y { get; set; }

        /// <summary>True ako je komad rotiran za 90° (samo za Q mreže).</summary>
        public bool IsRotated { get; set; }

        /// <summary>Efektivna širina komada na limu (uzima u obzir rotaciju).</summary>
        public double PlacedWidth => IsRotated ? Piece.Length : Piece.Width;

        /// <summary>Efektivna dužina komada na limu (uzima u obzir rotaciju).</summary>
        public double PlacedLength => IsRotated ? Piece.Width : Piece.Length;
    }
}
