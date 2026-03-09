using System.IO;
using System.Globalization;
using SysColor     = System.Drawing.Color;
using SysBitmap    = System.Drawing.Bitmap;
using SysGraphics  = System.Drawing.Graphics;
using SysPen       = System.Drawing.Pen;
using SysBrush     = System.Drawing.SolidBrush;
using SysFont      = System.Drawing.Font;
using SysSmoothing = System.Drawing.Drawing2D.SmoothingMode;
using SysImgFormat = System.Drawing.Imaging.ImageFormat;
using SysStrFmt    = System.Drawing.StringFormat;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using MrezaCuttingPlan.Models;

namespace MrezaCuttingPlan.Services
{
    public class PdfExporter
    {
        // ── Records ───────────────────────────────────────────────────────────────

        private record SpecRow(
            string Partition, string TypeName, string Pozicija,
            string OznakaMreze, int WidthCm, int LengthCm,
            int Count, double UnitWeightKgM2, double TotalWeightKg);

        private record RecapRow(
            string OznakaMreze, int WidthCm, int LengthCm,
            int SheetCount, double UnitWeightKgM2, double TotalWeightKg);

        private class PlanGroup
        {
            public string Partition    { get; init; } = "";
            public string TypeName     { get; init; } = "";
            public string OznakaMreze  { get; init; } = "";
            public int    RepeatCount  { get; set; }
            public CuttingSheet Sheet  { get; init; } = null!;
            public List<(string Label, int W, int L)> PiecesInSheet { get; init; } = new();
        }

        // ── Public API ────────────────────────────────────────────────────────────

        public static void Export(
            Dictionary<(string Partition, string TypeName), List<CuttingSheet>> resultsByType,
            string projectName,
            string outputPath,
            double sheetWidthCm,
            double sheetLengthCm)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var (specRows, partitionTotals, pozicijaMap) =
                BuildSpecData(resultsByType, sheetWidthCm, sheetLengthCm);

            var recapRows  = BuildRecapRows(resultsByType, sheetWidthCm, sheetLengthCm);
            var planGroups = BuildPlanGroups(resultsByType, pozicijaMap);

            Document.Create(container =>
            {
                // 1. Specifikacija
                container.Page(page =>
                {
                    SetupPage(page);
                    page.Header().Element(c => BuildSectionHeader(c, "Mreže - specifikacija", projectName));
                    page.Content().Element(c => BuildSpecContent(c, specRows, partitionTotals));
                    page.Footer().Element(BuildFooter);
                });

                // 2. Rekapitulacija
                container.Page(page =>
                {
                    SetupPage(page);
                    page.Header().Element(c => BuildSectionHeader(c, "Mreže - rekapitulacija", projectName));
                    page.Content().Element(c => BuildRecapContent(c, recapRows));
                    page.Footer().Element(BuildFooter);
                });

                // 3. Plan rezanja
                container.Page(page =>
                {
                    SetupPage(page);
                    page.Header().Element(c => BuildSectionHeader(c, "Mreže - plan rezanja", projectName));
                    page.Content().Element(c => BuildPlanContent(c, planGroups, sheetWidthCm, sheetLengthCm));
                    page.Footer().Element(BuildFooter);
                });
            }).GeneratePdf(outputPath);
        }

        // ── Data Building ─────────────────────────────────────────────────────────

        private static (
            List<SpecRow> Rows,
            Dictionary<string, double> PartitionTotals,
            Dictionary<(string Partition, string TypeName, int W, int L), string> PozicijaMap)
        BuildSpecData(
            Dictionary<(string Partition, string TypeName), List<CuttingSheet>> resultsByType,
            double sheetWidthCm, double sheetLengthCm)
        {
            var rows            = new List<SpecRow>();
            var partitionTotals = new Dictionary<string, double>();
            var pozicijaMap     = new Dictionary<(string, string, int, int), string>();

            foreach (var partGroup in resultsByType
                .GroupBy(kvp => kvp.Key.Partition)
                .OrderBy(g => g.Key))
            {
                string partition   = partGroup.Key;
                var    typeEntries = partGroup.OrderBy(k => k.Key.TypeName).ToList();
                double partTotal   = 0;

                for (int ti = 0; ti < typeEntries.Count; ti++)
                {
                    var    kvp      = typeEntries[ti];
                    string typeName = kvp.Key.TypeName;
                    string roman    = ToRoman(ti + 1);
                    string oznaka   = FormatTypeName(typeName);
                    double unitW    = GetUnitWeight(kvp.Value, typeName, sheetWidthCm, sheetLengthCm);

                    var allPieces = kvp.Value.SelectMany(s => s.PlacedPieces).ToList();

                    // Provjeri ima li FabricNumber postavljen
                    bool hasFabricNumber = allPieces.Any(pp =>
                        !string.IsNullOrWhiteSpace(pp.Piece.FabricNumber));

                    if (hasFabricNumber)
                    {
                        // Grupiraj po FabricNumber — svaki broj = jedan red u tablici
                        var byNumber = allPieces
                            .GroupBy(pp => pp.Piece.FabricNumber)
                            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        foreach (var ng in byNumber)
                        {
                            string poz   = ng.Key;
                            var    first = ng.First();
                            int    w     = (int)Math.Round(first.PlacedWidth);
                            int    l     = (int)Math.Round(first.PlacedLength);
                            int    count = ng.Count();
                            double total = count * (w * (double)l / 10000.0) * unitW;

                            pozicijaMap[(partition, typeName, w, l)] = poz;
                            rows.Add(new SpecRow(partition, typeName, poz, oznaka, w, l, count, unitW, total));
                            partTotal += total;
                        }
                    }
                    else
                    {
                        // Fallback: grupiraj po dimenzijama, označi I / I-1 / I-2...
                        var uniqueSizes = allPieces
                            .GroupBy(pp => (
                                W: (int)Math.Round(pp.PlacedWidth),
                                L: (int)Math.Round(pp.PlacedLength)))
                            .OrderByDescending(g => g.Count())
                            .ToList();

                        for (int si = 0; si < uniqueSizes.Count; si++)
                        {
                            var    sg    = uniqueSizes[si];
                            int    w     = sg.Key.W;
                            int    l     = sg.Key.L;
                            int    count = sg.Count();
                            double total = count * (w * (double)l / 10000.0) * unitW;
                            string poz   = si == 0 ? roman : $"{roman}-{si}";

                            pozicijaMap[(partition, typeName, w, l)] = poz;
                            rows.Add(new SpecRow(partition, typeName, poz, oznaka, w, l, count, unitW, total));
                            partTotal += total;
                        }
                    }
                }
                partitionTotals[partition] = partTotal;
            }

            return (rows, partitionTotals, pozicijaMap);
        }

        private static List<RecapRow> BuildRecapRows(
            Dictionary<(string Partition, string TypeName), List<CuttingSheet>> resultsByType,
            double sheetWidthCm, double sheetLengthCm)
        {
            return resultsByType
                .GroupBy(kvp => kvp.Key.TypeName)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var sheets     = g.SelectMany(kvp => kvp.Value).ToList();
                    double unitW   = GetUnitWeight(sheets, g.Key, sheetWidthCm, sheetLengthCm);
                    double areaM2  = sheetWidthCm * sheetLengthCm / 10000.0;
                    return new RecapRow(
                        FormatTypeName(g.Key),
                        (int)Math.Round(sheetWidthCm),
                        (int)Math.Round(sheetLengthCm),
                        sheets.Count,
                        unitW,
                        sheets.Count * areaM2 * unitW);
                })
                .ToList();
        }

        private static List<PlanGroup> BuildPlanGroups(
            Dictionary<(string Partition, string TypeName), List<CuttingSheet>> resultsByType,
            Dictionary<(string Partition, string TypeName, int W, int L), string> pozicijaMap)
        {
            var result = new List<PlanGroup>();

            foreach (var partGroup in resultsByType
                .GroupBy(kvp => kvp.Key.Partition)
                .OrderBy(g => g.Key))
            {
                foreach (var kvp in partGroup.OrderBy(k => k.Key.TypeName))
                {
                    string partition = kvp.Key.Partition;
                    string typeName  = kvp.Key.TypeName;

                    foreach (var sigGroup in kvp.Value.GroupBy(SheetSignature).OrderByDescending(g => g.Count()))
                    {
                        var rep    = sigGroup.First();
                        var pieces = rep.PlacedPieces.Select(pp =>
                        {
                            int    w   = (int)Math.Round(pp.PlacedWidth);
                            int    l   = (int)Math.Round(pp.PlacedLength);
                            string lbl = !string.IsNullOrWhiteSpace(pp.Piece.FabricNumber)
                                ? pp.Piece.FabricNumber
                                : pozicijaMap.TryGetValue((partition, typeName, w, l), out var s) ? s : "?";
                            return (lbl, w, l);
                        }).ToList();

                        result.Add(new PlanGroup
                        {
                            Partition     = partition,
                            TypeName      = typeName,
                            OznakaMreze   = FormatTypeName(typeName),
                            RepeatCount   = sigGroup.Count(),
                            Sheet         = rep,
                            PiecesInSheet = pieces
                        });
                    }
                }
            }
            return result;
        }

        // ── Page Setup ────────────────────────────────────────────────────────────

        private static void SetupPage(PageDescriptor page)
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(12, Unit.Millimetre);
            page.MarginTop(8,  Unit.Millimetre);
            page.MarginBottom(10, Unit.Millimetre);
        }

        private static void BuildSectionHeader(IContainer container, string title, string projectName)
        {
            container
                .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1)
                .PaddingBottom(3)
                .Row(row =>
                {
                    row.RelativeItem()
                       .DefaultTextStyle(s => s.FontSize(9))
                       .Text(title);

                    row.RelativeItem()
                       .AlignCenter()
                       .DefaultTextStyle(s => s.FontSize(9))
                       .Text(projectName);

                    row.RelativeItem()
                       .AlignRight()
                       .DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Medium))
                       .Text(t => { t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
                });
        }

        private static void BuildFooter(IContainer container)
        {
            container
                .AlignCenter()
                .DefaultTextStyle(s => s.FontSize(7).FontColor(Colors.Grey.Medium))
                .Text(t => { t.Span("Stranica "); t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
        }

        // ── Specifikacija ─────────────────────────────────────────────────────────

        private static void BuildSpecContent(
            IContainer container,
            List<SpecRow> rows,
            Dictionary<string, double> partitionTotals)
        {
            container.Border(0.5f).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(30, Unit.Millimetre); // Pozicija
                    cols.ConstantColumn(30, Unit.Millimetre); // Oznaka mreže
                    cols.ConstantColumn(15, Unit.Millimetre); // B [cm]
                    cols.ConstantColumn(15, Unit.Millimetre); // L [cm]
                    cols.ConstantColumn(15, Unit.Millimetre); // n
                    cols.ConstantColumn(37, Unit.Millimetre); // Jed. težina
                    cols.RelativeColumn();                     // Ukupna težina
                });

                table.Header(h =>
                {
                    Action<string, bool> hCell = (text, right) =>
                    {
                        var c = h.Cell()
                                 .BorderBottom(0.5f)
                                 .PaddingVertical(3).PaddingHorizontal(3)
                                 .DefaultTextStyle(s => s.FontSize(7.5f).Bold());
                        (right ? c.AlignRight() : c).Text(text);
                    };
                    hCell("Pozicija",                   false);
                    hCell("Oznaka mreže",               false);
                    hCell("B\n[cm]",                    true);
                    hCell("L\n[cm]",                    true);
                    hCell("n",                          true);
                    hCell("Jedinična težina\n[kg/m2]",  true);
                    hCell("Ukupna težina\n[kg]",        true);
                });

                foreach (var partition in rows.Select(r => r.Partition).Distinct().OrderBy(p => p))
                {
                    // Partition banner
                    table.Cell().ColumnSpan(7)
                         .Background(Colors.Grey.Lighten3)
                         .PaddingVertical(3).PaddingHorizontal(5)
                         .AlignCenter()
                         .DefaultTextStyle(s => s.FontSize(8).Bold())
                         .Text(partition);

                    foreach (var row in rows.Where(r => r.Partition == partition))
                    {
                        Action<string, bool> dCell = (text, right) =>
                        {
                            var c = table.Cell()
                                         .BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2)
                                         .PaddingVertical(2).PaddingHorizontal(3)
                                         .DefaultTextStyle(s => s.FontSize(7.5f));
                            (right ? c.AlignRight() : c).Text(text);
                        };
                        dCell(row.Pozicija,                                             false);
                        dCell(row.OznakaMreze,                                          false);
                        dCell(row.WidthCm.ToString(),                                   true);
                        dCell(row.LengthCm.ToString(),                                  true);
                        dCell(row.Count.ToString(),                                     true);
                        dCell(row.UnitWeightKgM2.ToString("F2", CultureInfo.InvariantCulture), true);
                        dCell(row.TotalWeightKg.ToString("F2", CultureInfo.InvariantCulture),  true);
                    }

                    // Ukupno
                    table.Cell().ColumnSpan(6)
                         .BorderTop(0.5f)
                         .PaddingVertical(2).PaddingHorizontal(3)
                         .DefaultTextStyle(s => s.FontSize(7.5f))
                         .Text("Ukupno");
                    table.Cell()
                         .BorderTop(0.5f)
                         .PaddingVertical(2).PaddingHorizontal(3)
                         .AlignRight()
                         .DefaultTextStyle(s => s.FontSize(7.5f))
                         .Text(partitionTotals[partition].ToString("F2", CultureInfo.InvariantCulture));
                }
            });
        }

        // ── Rekapitulacija ────────────────────────────────────────────────────────

        private static void BuildRecapContent(IContainer container, List<RecapRow> rows)
        {
            container.Border(0.5f).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(35, Unit.Millimetre); // Oznaka mreže
                    cols.ConstantColumn(20, Unit.Millimetre); // B [cm]
                    cols.ConstantColumn(20, Unit.Millimetre); // L [cm]
                    cols.ConstantColumn(20, Unit.Millimetre); // n
                    cols.ConstantColumn(40, Unit.Millimetre); // Jed. težina
                    cols.RelativeColumn();                     // Ukupna težina
                });

                table.Header(h =>
                {
                    Action<string, bool> hCell = (text, right) =>
                    {
                        var c = h.Cell()
                                 .BorderBottom(0.5f)
                                 .PaddingVertical(3).PaddingHorizontal(3)
                                 .DefaultTextStyle(s => s.FontSize(7.5f).Bold());
                        (right ? c.AlignRight() : c).Text(text);
                    };
                    hCell("Oznaka mreže",              false);
                    hCell("B\n[cm]",                   true);
                    hCell("L\n[cm]",                   true);
                    hCell("n",                         true);
                    hCell("Jedinična težina\n[kg/m2]", true);
                    hCell("Ukupna težina\n[kg]",       true);
                });

                foreach (var row in rows)
                {
                    Action<string, bool> dCell = (text, right) =>
                    {
                        var c = table.Cell()
                                     .BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2)
                                     .PaddingVertical(2).PaddingHorizontal(3)
                                     .DefaultTextStyle(s => s.FontSize(7.5f));
                        (right ? c.AlignRight() : c).Text(text);
                    };
                    dCell(row.OznakaMreze,                                          false);
                    dCell(row.WidthCm.ToString(),                                   true);
                    dCell(row.LengthCm.ToString(),                                  true);
                    dCell(row.SheetCount.ToString(),                                true);
                    dCell(row.UnitWeightKgM2.ToString("F2", CultureInfo.InvariantCulture), true);
                    dCell(row.TotalWeightKg.ToString("F2", CultureInfo.InvariantCulture),  true);
                }

                // Ukupno
                double grand = rows.Sum(r => r.TotalWeightKg);
                table.Cell().ColumnSpan(5)
                     .BorderTop(0.5f)
                     .PaddingVertical(2).PaddingHorizontal(3)
                     .DefaultTextStyle(s => s.FontSize(7.5f))
                     .Text("Ukupno");
                table.Cell()
                     .BorderTop(0.5f)
                     .PaddingVertical(2).PaddingHorizontal(3)
                     .AlignRight()
                     .DefaultTextStyle(s => s.FontSize(7.5f))
                     .Text(grand.ToString("F2", CultureInfo.InvariantCulture));
            });
        }

        // ── Plan Rezanja ──────────────────────────────────────────────────────────

        private static void BuildPlanContent(
            IContainer container,
            List<PlanGroup> groups,
            double sheetW, double sheetL)
        {
            container.Column(col =>
            {
                foreach (var partGroup in groups
                    .GroupBy(g => g.Partition)
                    .OrderBy(g => g.Key))
                {
                    // Partition banner
                    col.Item()
                       .PaddingTop(6)
                       .Background(Colors.Grey.Darken3)
                       .PaddingVertical(3).PaddingHorizontal(5)
                       .AlignCenter()
                       .DefaultTextStyle(s => s.FontSize(9).Bold().FontColor(Colors.White))
                       .Text(partGroup.Key);

                    foreach (var typeGroup in partGroup
                        .GroupBy(g => g.TypeName)
                        .OrderBy(g => g.Key))
                    {
                        var typeList = typeGroup.ToList();
                        string oznaka = typeList[0].OznakaMreze;

                        // Type banner
                        col.Item()
                           .Background(Colors.Grey.Lighten2)
                           .PaddingVertical(2).PaddingHorizontal(5)
                           .AlignCenter()
                           .DefaultTextStyle(s => s.FontSize(8).Bold())
                           .Text($"{oznaka} ({sheetL:F0} cm x {sheetW:F0} cm)");

                        // 2 per row
                        for (int j = 0; j < typeList.Count; j += 2)
                        {
                            int jj = j;
                            col.Item().PaddingTop(5).Row(row =>
                            {
                                row.RelativeItem().Element(c =>
                                    DrawSheetCell(c, typeList[jj], sheetW, sheetL));

                                row.ConstantItem(10);

                                if (jj + 1 < typeList.Count)
                                    row.RelativeItem().Element(c =>
                                        DrawSheetCell(c, typeList[jj + 1], sheetW, sheetL));
                                else
                                    row.RelativeItem();
                            });
                        }

                        col.Item().Height(6);
                    }
                }
            });
        }

        private static void DrawSheetCell(
            IContainer container, PlanGroup group,
            double sheetW, double sheetL)
        {
            container.Column(col =>
            {
                // "Nx" label
                col.Item()
                   .DefaultTextStyle(s => s.FontSize(7).Bold())
                   .Text($"{group.RepeatCount}x");

                col.Item().Row(row =>
                {
                    byte[] img = RenderSheetToPng(group.Sheet, group.PiecesInSheet, sheetW, sheetL);
                    row.RelativeItem(6).Image(img);

                    row.ConstantItem(4);

                    // Piece list
                    row.RelativeItem(4).Column(listCol =>
                    {
                        foreach (var (label, w, l) in group.PiecesInSheet)
                        {
                            listCol.Item()
                                   .DefaultTextStyle(s => s.FontSize(6))
                                   .Text($"{label}  {w} x {l}");
                        }
                    });
                });
            });
        }

        // ── GDI+ Rendering ────────────────────────────────────────────────────────

        private static byte[] RenderSheetToPng(
            CuttingSheet sheet,
            List<(string Label, int W, int L)> pieceLabels,
            double sheetW, double sheetL)
        {
            const int imgW = 900;
            int   imgH  = Math.Max(1, (int)Math.Round(imgW * sheetW / sheetL));
            float scale = imgW / (float)sheetL;

            using var bmp = new SysBitmap(imgW, imgH);
            using var g   = SysGraphics.FromImage(bmp);
            g.SmoothingMode = SysSmoothing.AntiAlias;
            g.Clear(SysColor.White);

            float fontSize = Math.Max(8f, imgH * 0.09f);
            using var font   = new SysFont("Arial", fontSize);
            using var strFmt = new SysStrFmt
            {
                Alignment     = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center,
                Trimming      = System.Drawing.StringTrimming.Character
            };

            for (int i = 0; i < sheet.PlacedPieces.Count; i++)
            {
                var   pp = sheet.PlacedPieces[i];
                float cx = (float)pp.Y            * scale;
                float cy = (float)pp.X            * scale;
                float cw = (float)pp.PlacedLength * scale;
                float ch = (float)pp.PlacedWidth  * scale;

                // Fill
                using (var brush = new SysBrush(SysColor.FromArgb(0xE8, 0xE8, 0xE8)))
                    g.FillRectangle(brush, cx, cy, cw, ch);

                // Border
                using (var pen = new SysPen(SysColor.FromArgb(0x44, 0x44, 0x44), 1.5f))
                    g.DrawRectangle(pen, cx, cy, cw, ch);

                // Diagonal (bottom-left → top-right like ArmCAD)
                using (var pen = new SysPen(SysColor.FromArgb(0x88, 0x88, 0x88), 1f))
                    g.DrawLine(pen, cx, cy + ch, cx + cw, cy);

                // Label
                if (i < pieceLabels.Count && !string.IsNullOrEmpty(pieceLabels[i].Label))
                {
                    var rect = new System.Drawing.RectangleF(cx + 2, cy + 2,
                        Math.Max(1f, cw - 4), Math.Max(1f, ch - 4));
                    using var textBrush = new SysBrush(SysColor.FromArgb(0x22, 0x22, 0x22));
                    g.DrawString(pieceLabels[i].Label, font, textBrush, rect, strFmt);
                }
            }

            // Sheet border
            using (var pen = new SysPen(SysColor.Black, 2f))
                g.DrawRectangle(pen, 0, 0, imgW - 1, imgH - 1);

            using var ms = new MemoryStream();
            bmp.Save(ms, SysImgFormat.Png);
            return ms.ToArray();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static double GetUnitWeight(
            List<CuttingSheet> sheets, string typeName,
            double sheetWidthCm, double sheetLengthCm)
        {
            double wKgSheet = sheets
                .SelectMany(s => s.PlacedPieces)
                .Select(pp => pp.Piece.WeightKgSheet)
                .FirstOrDefault(w => w > 0);

            if (wKgSheet > 0)
                return wKgSheet / (sheetWidthCm * sheetLengthCm / 10000.0);

            // Formula fallback
            string digits = new string(typeName.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int num))
            {
                return typeName.StartsWith("Q", StringComparison.OrdinalIgnoreCase)
                    ? num * 2.0 * 7.85 / 1000.0
                    : num * 1.15 * 7.85 / 1000.0;
            }
            return 0;
        }

        private static string FormatTypeName(string name)
        {
            // "Q335" → "Q-335", already dashed names unchanged
            if (name.Length > 1 && char.IsLetter(name[0]) && !name.Contains('-') && char.IsDigit(name[1]))
                return $"{name[0]}-{name[1..]}";
            return name;
        }

        private static string SheetSignature(CuttingSheet sheet) =>
            string.Join("|", sheet.PlacedPieces
                .Select(p => $"{(int)Math.Round(p.PlacedWidth)}x{(int)Math.Round(p.PlacedLength)}")
                .OrderBy(x => x));

        private static string ToRoman(int n) => n switch
        {
            1 => "I",   2 => "II",   3 => "III",  4 => "IV",  5 => "V",
            6 => "VI",  7 => "VII",  8 => "VIII", 9 => "IX",  10 => "X",
            _ => n.ToString()
        };
    }
}
