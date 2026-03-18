using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace PilotMCP;

/// <summary>
/// Извлекает текст с координатами из XPS-документов.
/// XPS — это ZIP-архив с XAML-страницами, содержащими элементы Glyphs.
/// </summary>
public static class XpsTextExtractor
{
    private static readonly XNamespace XpsNs = "http://schemas.microsoft.com/xps/2005/06";
    private static readonly XNamespace OpenXpsNs = "http://schemas.openxps.org/oxps/v1.0";

    public record TextBlock(string Text, double X, double Y, double Width, double FontSize, int Page, double[]? Advances = null);

    /// <summary>
    /// Извлекает все текстовые блоки из XPS-файла.
    /// Поддерживает как обычные .fpage файлы, так и interleaved (piece) формат.
    /// </summary>
    public static List<TextBlock> ExtractText(string xpsPath)
    {
        var result = new List<TextBlock>();

        using var zip = ZipFile.OpenRead(xpsPath);

        var pageXmls = LoadFixedPages(zip);

        for (int pageIdx = 0; pageIdx < pageXmls.Count; pageIdx++)
        {
            var doc = pageXmls[pageIdx];
            if (doc.Root == null) continue;

            var ns = doc.Root.Name.Namespace;
            ExtractGlyphsFromElement(doc.Root, ns, pageIdx, result);
        }

        return result;
    }

    /// <summary>
    /// Загружает FixedPage XML из XPS-архива.
    /// Поддерживает два формата:
    /// 1) Обычный: Documents/1/Pages/1.fpage (один файл = одна страница)
    /// 2) Interleaved (piece): Documents/1/Pages/1.fpage/[0].piece, [1].last.piece
    ///    (страница разбита на куски, которые нужно склеить)
    /// </summary>
    private static List<XDocument> LoadFixedPages(ZipArchive zip)
    {
        // Сначала пробуем обычный формат
        var directPages = zip.Entries
            .Where(e => e.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName)
            .ToList();

        if (directPages.Count > 0)
        {
            var docs = new List<XDocument>();
            foreach (var entry in directPages)
            {
                using var stream = entry.Open();
                docs.Add(XDocument.Load(stream));
            }
            return docs;
        }

        // Interleaved (piece) формат: группируем куски по имени fpage
        var pieceGroups = zip.Entries
            .Where(e => e.FullName.Contains(".fpage/") &&
                        e.FullName.EndsWith(".piece", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e =>
            {
                var idx = e.FullName.IndexOf(".fpage/", StringComparison.OrdinalIgnoreCase);
                return e.FullName[..(idx + 6)]; // "Documents/1/Pages/1.fpage"
            })
            .OrderBy(g => g.Key)
            .ToList();

        var result = new List<XDocument>();
        foreach (var group in pieceGroups)
        {
            var sb = new StringBuilder();
            foreach (var piece in group.OrderBy(e => e.FullName))
            {
                using var stream = piece.Open();
                using var reader = new StreamReader(stream);
                sb.Append(reader.ReadToEnd());
            }
            var xml = sb.ToString();
            if (!string.IsNullOrWhiteSpace(xml))
                result.Add(XDocument.Parse(xml));
        }

        return result;
    }

    /// <summary>
    /// Ищет текст в XPS-файле и возвращает координаты всех вхождений.
    /// </summary>
    public static List<TextBlock> FindText(string xpsPath, string searchText)
    {
        var allBlocks = ExtractText(xpsPath);
        var found = new List<TextBlock>();

        // Exact match in individual blocks — compute precise offset using per-character advances
        foreach (var block in allBlocks)
        {
            var idx = block.Text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                double offsetX = 0;
                double matchWidth = 0;
                if (block.Advances != null && block.Advances.Length >= idx + searchText.Length)
                {
                    for (int i = 0; i < idx; i++)
                        offsetX += block.Advances[i];
                    for (int i = idx; i < idx + searchText.Length; i++)
                        matchWidth += block.Advances[i];
                }
                else
                {
                    var charWidth = block.Width / Math.Max(block.Text.Length, 1);
                    offsetX = idx * charWidth;
                    matchWidth = searchText.Length * charWidth;
                }
                found.Add(block with
                {
                    Text = block.Text,
                    X = block.X + offsetX,
                    Width = matchWidth
                });
            }
        }

        // If not found in individual blocks, try concatenating adjacent blocks on the same page/line
        if (found.Count == 0)
        {
            found = FindInConcatenatedBlocks(allBlocks, searchText);
        }

        return found;
    }

    private static void ExtractGlyphsFromElement(XElement element, XNamespace ns, int pageIdx, List<TextBlock> result)
    {
        // Process Glyphs elements
        foreach (var glyph in element.Descendants(ns + "Glyphs"))
        {
            var text = glyph.Attribute("UnicodeString")?.Value;
            if (string.IsNullOrEmpty(text)) continue;

            var originX = ParseDouble(glyph.Attribute("OriginX")?.Value);
            var originY = ParseDouble(glyph.Attribute("OriginY")?.Value);
            var fontSize = ParseDouble(glyph.Attribute("FontRenderingEmSize")?.Value);

            // Compose full RenderTransform chain from Glyph to FixedPage root (inside→out)
            var chain = new List<double[]>();

            // Collect transforms from glyph → root (inside out order)
            var cur = glyph;
            while (cur != null && cur != element)
            {
                var rt = cur.Attribute("RenderTransform")?.Value;
                if (!string.IsNullOrEmpty(rt))
                    chain.Add(ParseMatrix(rt));
                cur = cur.Parent;
            }

            // Apply composed transform to the origin point
            var (px, py) = ApplyTransformChain(chain, originX, originY);

            // Compute effective scale for font size and width estimation
            var scaleX = ComputeScaleX(chain);
            var effectiveFontSize = fontSize * scaleX;

            // Parse Indices for precise per-character advance widths
            var indicesStr = glyph.Attribute("Indices")?.Value;
            var advances = ParseAdvances(indicesStr, text.Length, fontSize, scaleX);
            var totalWidth = advances.Sum();

            result.Add(new TextBlock(
                Text: text,
                X: px,
                Y: py - effectiveFontSize, // Adjust from baseline to top of text
                Width: totalWidth,
                FontSize: effectiveFontSize,
                Page: pageIdx,
                Advances: advances
            ));
        }
    }

    /// <summary>
    /// Parses a RenderTransform string "a,b,c,d,e,f" into a matrix array [a,b,c,d,e,f].
    /// Matrix transforms point (x,y) to: x' = a*x + c*y + e, y' = b*x + d*y + f
    /// </summary>
    private static double[] ParseMatrix(string rt)
    {
        var parts = rt.Split(',');
        if (parts.Length == 6)
        {
            return new[]
            {
                ParseDouble(parts[0]), ParseDouble(parts[1]),
                ParseDouble(parts[2]), ParseDouble(parts[3]),
                ParseDouble(parts[4]), ParseDouble(parts[5])
            };
        }
        return new double[] { 1, 0, 0, 1, 0, 0 }; // identity
    }

    /// <summary>
    /// Applies a chain of transform matrices to a point.
    /// </summary>
    private static (double x, double y) ApplyTransformChain(List<double[]> chain, double x, double y)
    {
        foreach (var m in chain)
        {
            var nx = m[0] * x + m[2] * y + m[4];
            var ny = m[1] * x + m[3] * y + m[5];
            x = nx;
            y = ny;
        }
        return (x, y);
    }

    /// <summary>
    /// Computes the effective horizontal scale from a chain of transforms.
    /// </summary>
    private static double ComputeScaleX(List<double[]> chain)
    {
        // Transform unit vector (1,0) and measure its length
        var (x1, y1) = ApplyTransformChain(chain, 1, 0);
        var (x0, y0) = ApplyTransformChain(chain, 0, 0);
        var dx = x1 - x0;
        var dy = y1 - y0;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Parses the Indices attribute to extract per-character advance widths in FixedPage coordinates.
    /// XPS Indices format: each entry separated by ';', advance is after the comma: "[cluster:]glyph,advance[,uOff,vOff]"
    /// Advance is in hundredths of FontRenderingEmSize.
    /// </summary>
    private static double[] ParseAdvances(string? indices, int charCount, double fontSize, double scaleX)
    {
        var advances = new double[charCount];
        var defaultAdvance = fontSize * 0.5 * scaleX; // fallback

        if (string.IsNullOrEmpty(indices))
        {
            Array.Fill(advances, defaultAdvance);
            return advances;
        }

        var entries = indices.Split(';');
        for (int i = 0; i < charCount; i++)
        {
            if (i < entries.Length)
            {
                var entry = entries[i];
                // Entry format: [clusterMap:]glyphIndex,advanceWidth[,uOffset[,vOffset]]
                // Often just ",advanceWidth" (comma-prefixed, no glyph index)
                var commaIdx = entry.IndexOf(',');
                if (commaIdx >= 0)
                {
                    // Get advance width (second comma-separated value, or first after leading comma)
                    var afterComma = entry[(commaIdx + 1)..];
                    var nextComma = afterComma.IndexOf(',');
                    var advStr = nextComma >= 0 ? afterComma[..nextComma] : afterComma;
                    var advRaw = ParseDouble(advStr);
                    if (advRaw > 0)
                    {
                        // Advance is in hundredths of em size, convert to local units then scale
                        advances[i] = advRaw / 100.0 * fontSize * scaleX;
                        continue;
                    }
                }
            }
            advances[i] = defaultAdvance;
        }

        return advances;
    }

    private static List<TextBlock> FindInConcatenatedBlocks(List<TextBlock> blocks, string searchText)
    {
        var found = new List<TextBlock>();

        // Group by page and approximate Y position (same line = similar Y)
        var grouped = blocks
            .GroupBy(b => (b.Page, YLine: Math.Round(b.Y / 5) * 5))
            .OrderBy(g => g.Key.Page)
            .ThenBy(g => g.Key.YLine);

        foreach (var group in grouped)
        {
            var lineBlocks = group.OrderBy(b => b.X).ToList();
            var lineText = string.Join("", lineBlocks.Select(b => b.Text));

            var idx = lineText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // Find which block(s) contain the match
                int charPos = 0;
                for (int i = 0; i < lineBlocks.Count; i++)
                {
                    var block = lineBlocks[i];
                    var blockEnd = charPos + block.Text.Length;
                    if (charPos <= idx && blockEnd > idx)
                    {
                        // Calculate approximate X offset within this block
                        var charOffset = idx - charPos;
                        var charWidth = block.FontSize * 0.5;
                        var matchX = block.X + charOffset * charWidth;
                        var matchWidth = searchText.Length * charWidth;

                        found.Add(new TextBlock(
                            Text: searchText,
                            X: matchX,
                            Y: block.Y - block.FontSize,
                            Width: matchWidth,
                            FontSize: block.FontSize,
                            Page: block.Page
                        ));
                        break;
                    }
                    charPos = blockEnd;
                }
            }
        }

        return found;
    }

    private static double ParseDouble(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result);
        return result;
    }

    /// <summary>
    /// Форматирует результат поиска текста для модели.
    /// </summary>
    public static string FormatSearchResults(List<TextBlock> results, string searchText)
    {
        if (results.Count == 0)
            return $"Текст \"{searchText}\" не найден в документе.";

        var sb = new StringBuilder();
        sb.AppendLine($"Найдено {results.Count} вхождений \"{searchText}\":");
        sb.AppendLine();
        foreach (var r in results)
        {
            sb.AppendLine($"  Страница {r.Page}: x={r.X:F1}, y={r.Y:F1}, ширина={r.Width:F1}, шрифт={r.FontSize:F1}");
            sb.AppendLine($"    Текст: \"{r.Text}\"");
        }
        return sb.ToString();
    }
}
