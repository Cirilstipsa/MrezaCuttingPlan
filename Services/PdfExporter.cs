using MrezaCuttingPlan.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MrezaCuttingPlan.Services
{
    /// <summary>
    /// Generira PDF izvještaj s vizualnim prikazom plana rezanja mreža.
    /// </summary>
    public class PdfExporter
    {
        private const float PageMarginMm = 15f;

        /// <summary>
        /// Generira PDF s planom rezanja za sve tipove mreža.
        /// </summary>
        public void Export(
            Dictionary<string, List<CuttingSheet>> resultsByType,
            string projectName,
            string outputPath)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            Document.Create(container =>
            {
                // NASLOVNA STRANICA
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(PageMarginMm, Unit.Millimetre);
                    page.Content().Column(col =>
                    {
                        col.Item().PaddingTop(40).Text("PLAN REZANJA ARMATURNIH MREŽA")
                            .FontSize(22).Bold().AlignCenter();

                        col.Item().PaddingTop(10).Text(projectName)
                            .FontSize(14).AlignCenter().FontColor(Colors.Grey.Darken2);

                        col.Item().PaddingTop(5)
                            .Text($"Datum: {DateTime.Now:dd.MM.yyyy}")
                            .FontSize(11).AlignCenter();

                        col.Item().PaddingTop(30).LineHorizontal(1);
                        col.Item().PaddingTop(20)
                            .Text("SAŽETAK PO TIPOVIMA MREŽA")
                            .FontSize(14).Bold();

                        col.Item().PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(1);
                                cols.RelativeColumn(1);
                                cols.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten2)
                                    .Padding(5).Text("Tip mreže").Bold();
                                header.Cell().Background(Colors.Grey.Lighten2)
                                    .Padding(5).Text("Komada").Bold().AlignCenter();
                                header.Cell().Background(Colors.Grey.Lighten2)
                                    .Padding(5).Text("Limova").Bold().AlignCenter();
                                header.Cell().Background(Colors.Grey.Lighten2)
                                    .Padding(5).Text("Avg. otpad").Bold().AlignCenter();
                            });

                            foreach (var kvp in resultsByType)
                            {
                                int totalPieces = kvp.Value.Sum(s => s.PlacedPieces.Count);
                                int totalSheets = kvp.Value.Count;
                                double avgWaste = kvp.Value.Average(s => s.WastePercent);

                                table.Cell().Padding(4).Text(kvp.Key);
                                table.Cell().Padding(4).AlignCenter()
                                    .Text(totalPieces.ToString());
                                table.Cell().Padding(4).AlignCenter()
                                    .Text(totalSheets.ToString());
                                table.Cell().Padding(4).AlignCenter()
                                    .Text($"{avgWaste:F1}%");
                            }
                        });
                    });
                });

                // STRANICE PO TIPU MREŽE
                foreach (var kvp in resultsByType)
                {
                    string meshType = kvp.Key;
                    var sheets = kvp.Value;

                    // Sažetak tipa
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(PageMarginMm, Unit.Millimetre);
                        page.Content().Column(col =>
                        {
                            col.Item().Text($"Tip mreže: {meshType}")
                                .FontSize(18).Bold();
                            col.Item().PaddingTop(5).LineHorizontal(1);

                            int totalPieces = sheets.Sum(s => s.PlacedPieces.Count);
                            double avgWaste = sheets.Average(s => s.WastePercent);
                            double avgEff = sheets.Average(s => s.EfficiencyPercent);

                            col.Item().PaddingTop(15).Row(row =>
                            {
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text($"Ukupno komada: {totalPieces}").FontSize(12);
                                    c.Item().Text($"Ukupno limova: {sheets.Count}").FontSize(12);
                                });
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text($"Prosj. iskorištenost: {avgEff:F1}%").FontSize(12);
                                    c.Item().Text($"Prosj. otpad: {avgWaste:F1}%").FontSize(12);
                                });
                            });

                            col.Item().PaddingTop(20).Text("Pregled limova:").Bold().FontSize(13);

                            foreach (var s in sheets)
                            {
                                col.Item().PaddingTop(5).Text(
                                    $"  Lim {s.SheetIndex}: {s.PlacedPieces.Count} komada, " +
                                    $"iskorištenost {s.EfficiencyPercent:F1}%, " +
                                    $"otpad {s.WastePercent:F1}%").FontSize(11);
                            }
                        });
                    });

                    // Vizualni prikaz svakog lima
                    foreach (var sheet in sheets)
                    {
                        var capturedSheet = sheet; // closure capture
                        var capturedType = meshType;
                        int totalCount = sheets.Count;

                        container.Page(page =>
                        {
                            page.Size(PageSizes.A4);
                            page.Margin(PageMarginMm, Unit.Millimetre);
                            page.Content().Column(col =>
                            {
                                // Naslov
                                col.Item().Row(row =>
                                {
                                    row.RelativeItem()
                                        .Text($"{capturedType} – Lim {capturedSheet.SheetIndex}/{totalCount}")
                                        .FontSize(14).Bold();
                                    row.ConstantItem(120)
                                        .Text($"Otpad: {capturedSheet.WastePercent:F1}%")
                                        .FontSize(11).AlignRight();
                                });

                                col.Item().PaddingTop(3)
                                    .Text($"Standardni lim: {capturedSheet.SheetWidth:F0}×{capturedSheet.SheetLength:F0} cm  |  " +
                                          $"Iskorištenost: {capturedSheet.EfficiencyPercent:F1}%")
                                    .FontSize(10).FontColor(Colors.Grey.Darken2);

                                col.Item().PaddingTop(8)
                                    .AspectRatio(
                                        (float)(capturedSheet.SheetWidth / capturedSheet.SheetLength),
                                        AspectRatioOption.FitWidth)
                                    .Canvas((canvas, size) =>
                                    {
                                        DrawSheetOnCanvas(canvas, size, capturedSheet);
                                    });

                                // Legenda
                                col.Item().PaddingTop(8).Row(row =>
                                {
                                    row.RelativeItem().Text("Legenda:").FontSize(9).Bold();
                                });

                                int legendCols = Math.Min(capturedSheet.PlacedPieces.Count, 4);
                                if (legendCols > 0)
                                {
                                    col.Item().PaddingTop(4).Table(table =>
                                    {
                                        table.ColumnsDefinition(cols =>
                                        {
                                            for (int i = 0; i < legendCols; i++)
                                                cols.RelativeColumn();
                                        });

                                        foreach (var pp in capturedSheet.PlacedPieces)
                                        {
                                            string color = GetPieceColor(pp.Piece.RevitId);
                                            string label = pp.IsRotated
                                                ? $"#{pp.Piece.RevitId}: {pp.Piece.Length:F0}×{pp.Piece.Width:F0}R"
                                                : $"#{pp.Piece.RevitId}: {pp.Piece.Width:F0}×{pp.Piece.Length:F0}";

                                            table.Cell().Row(row =>
                                            {
                                                row.ConstantItem(12)
                                                    .Height(10)
                                                    .Background(color);
                                                row.RelativeItem()
                                                    .PaddingLeft(3)
                                                    .Text(label).FontSize(7);
                                            });
                                        }
                                    });
                                }
                            });
                        });
                    }
                }
            }).GeneratePdf(outputPath);
        }

        private static void DrawSheetOnCanvas(
            ICanvas canvas,
            Size size,
            CuttingSheet sheet)
        {
            float scaleX = size.Width / (float)sheet.SheetWidth;
            float scaleY = size.Height / (float)sheet.SheetLength;
            float scale = Math.Min(scaleX, scaleY);

            float actualW = (float)sheet.SheetWidth * scale;
            float actualH = (float)sheet.SheetLength * scale;

            // Pozadina lima – otpadni materijal (tamno sivo)
            canvas.DrawRectangle(
                new Position(0, 0),
                new Size(actualW, actualH),
                Colors.Grey.Lighten3);

            // Crtaj svaki komad (ispunjen pravokutnik)
            foreach (var pp in sheet.PlacedPieces)
            {
                float x = (float)pp.X * scale;
                float y = (float)pp.Y * scale;
                float w = (float)pp.PlacedWidth * scale;
                float h = (float)pp.PlacedLength * scale;

                // Ispuna komada pastelnom bojom
                canvas.DrawRectangle(
                    new Position(x, y),
                    new Size(w, h),
                    GetPieceColor(pp.Piece.RevitId));

                // Unutarnji bijeli okvir (označava rez liniju)
                float gap = 1.5f;
                if (w > 6 && h > 6)
                {
                    canvas.DrawRectangle(
                        new Position(x + gap, y + gap),
                        new Size(w - 2 * gap, h - 2 * gap),
                        Colors.White);
                    // Ponovo nacrtaj ispunu (bijeli okvir samo kao border efekt)
                    canvas.DrawRectangle(
                        new Position(x + gap * 2, y + gap * 2),
                        new Size(w - 4 * gap, h - 4 * gap),
                        GetPieceColor(pp.Piece.RevitId));
                }
            }

            // Rub cijelog lima – tanka crna linija
            // Gornji rub
            canvas.DrawLine(
                new Position(0, 0),
                new Position(actualW, 0),
                1f, Colors.Black);
            // Desni rub
            canvas.DrawLine(
                new Position(actualW, 0),
                new Position(actualW, actualH),
                1f, Colors.Black);
            // Donji rub
            canvas.DrawLine(
                new Position(actualW, actualH),
                new Position(0, actualH),
                1f, Colors.Black);
            // Lijevi rub
            canvas.DrawLine(
                new Position(0, actualH),
                new Position(0, 0),
                1f, Colors.Black);
        }

        /// <summary>
        /// Deterministička pastelna boja po RevitId.
        /// </summary>
        private static string GetPieceColor(int revitId)
        {
            string[] palette =
            {
                "#AED6F1", "#A9DFBF", "#F9E79F", "#F5CBA7",
                "#D7BDE2", "#A3E4D7", "#FAD7A0", "#D5DBDB",
                "#ABEBC6", "#F8C471", "#85C1E9", "#C39BD3",
                "#F1948A", "#76D7C4", "#F8BBD9", "#BBDEFB"
            };

            int idx = Math.Abs(revitId) % palette.Length;
            return palette[idx];
        }
    }
}
