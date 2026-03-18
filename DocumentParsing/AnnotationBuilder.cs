using System.Globalization;
using System.Text;

namespace PilotMCP;

/// <summary>
/// Генерирует XML-разметку аннотаций Pilot в формате ArrayOfAnnotationContainer.
/// </summary>
public static class AnnotationBuilder
{
    private const string XmlHeader = "<?xml version=\"1.0\" encoding=\"utf-16\"?>";
    private const string ArrayOpen = "<ArrayOfAnnotationContainer xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">";
    private const string ArrayClose = "</ArrayOfAnnotationContainer>";
    private const string PencilDataNs = "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"";

    /// <summary>
    /// Создать аннотацию-прямоугольник.
    /// </summary>
    public static string Rectangle(double x, double y, double width, double height, int page, string color, Guid? annotationId = null, DateTime? version = null)
    {
        var hex = ResolveColor(color);
        var x2 = x + width;
        var y2 = y + height;
        var geometry = $"M{F(x)},{F(y)}L{F(x2)},{F(y)} {F(x2)},{F(y2)} {F(x)},{F(y2)} {F(x)},{F(y)}z";
        return Wrap(x, y, page, PencilData(geometry, hex, false, "Rectangle"), annotationId: annotationId, version: version);
    }

    /// <summary>
    /// Создать аннотацию-эллипс.
    /// </summary>
    public static string Ellipse(double cx, double cy, double rx, double ry, int page, string color, Guid? annotationId = null, DateTime? version = null)
    {
        var hex = ResolveColor(color);
        var geometry = GenerateEllipseGeometry(cx, cy, rx, ry, 36);
        return Wrap(cx - rx, cy - ry, page, PencilData(geometry, hex, false, "Ellipse"), annotationId: annotationId, version: version);
    }

    /// <summary>
    /// Создать аннотацию-треугольник.
    /// </summary>
    public static string Triangle(double x, double y, double width, double height, int page, string color, Guid? annotationId = null, DateTime? version = null)
    {
        var hex = ResolveColor(color);
        var topX = x + width / 2;
        var topY = y;
        var blX = x;
        var blY = y + height;
        var brX = x + width;
        var brY = y + height;
        var geometry = $"M{F(blX)},{F(blY)}L{F(topX)},{F(topY)} {F(brX)},{F(brY)} {F(blX)},{F(blY)}z";
        return Wrap(x, y, page, PencilData(geometry, hex, false, "Triangle"), annotationId: annotationId, version: version);
    }

    /// <summary>
    /// Создать аннотацию-стрелку (линия).
    /// </summary>
    public static string Arrow(double x1, double y1, double x2, double y2, int page, string color, Guid? annotationId = null, DateTime? version = null)
    {
        var hex = ResolveColor(color);
        var dx = x2 - x1;
        var dy = y2 - y1;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) len = 1;
        var ux = dx / len;
        var uy = dy / len;
        const double arrowLen = 10;
        const double arrowAngle = 0.4;

        var ax1 = x2 - arrowLen * (ux * Math.Cos(arrowAngle) - uy * Math.Sin(arrowAngle));
        var ay1 = y2 - arrowLen * (uy * Math.Cos(arrowAngle) + ux * Math.Sin(arrowAngle));
        var ax2 = x2 - arrowLen * (ux * Math.Cos(-arrowAngle) - uy * Math.Sin(-arrowAngle));
        var ay2 = y2 - arrowLen * (uy * Math.Cos(-arrowAngle) + ux * Math.Sin(-arrowAngle));

        var geometry = $"M{F(x1)},{F(y1)}L{F(x2)},{F(y2)} M{F(ax1)},{F(ay1)}L{F(x2)},{F(y2)} {F(ax2)},{F(ay2)}";
        var posX = Math.Min(x1, x2);
        var posY = Math.Min(y1, y2);
        return Wrap(posX, posY, page, PencilData(geometry, hex, true, "Line"), annotationId: annotationId, version: version);
    }

    /// <summary>
    /// Создать точечное замечание (текстовая заметка без графики).
    /// </summary>
    public static string TextNote(double x, double y, int page, Guid? annotationId = null, DateTime? version = null)
    {
        return Wrap(x, y, page, null, "TextNote", annotationId, version: version);
    }

    /// <summary>
    /// Создать замечание к тексту (TextStickyNote) с привязкой к диапазону текста.
    /// </summary>
    public static string TextStickyNote(double x1, double y1, double x2, double y2, int page, Guid? annotationId = null, DateTime? version = null)
    {
        var posX = Math.Min(x1, x2);
        var posY = Math.Max(y1, y2) + 10; // Позиция "булавки" чуть ниже выделенного текста

        var sb = new StringBuilder();
        sb.Append(XmlHeader);
        sb.Append($"<TextStickyNoteData {PencilDataNs}>");
        sb.Append($"<FixedTextRange>{F(x1)},{F(y1)},{F(x2)},{F(y2)}</FixedTextRange>");
        sb.Append("</TextStickyNoteData>");
        var escaped = EscapeXml(sb.ToString());

        return Wrap(posX, posY, page, escaped, "TextStickyNote", annotationId, version: version);
    }

    // --- внутренние методы ---

    private static string Wrap(double posX, double posY, int page, string? pencilDataEscaped, string kind = "RedPencil", Guid? annotationId = null, DateTime? version = null)
    {
        var ver = version ?? DateTime.UtcNow;
        var sb = new StringBuilder();
        sb.Append(XmlHeader);
        sb.Append(ArrayOpen);
        sb.Append("<AnnotationContainer>");
        sb.Append($"<AnnotationId>{annotationId ?? Guid.NewGuid()}</AnnotationId>");
        sb.Append($"<Version>{ver:yyyy-MM-ddTHH:mm:ss.fffffffZ}</Version>");
        sb.Append($"<PositionX>{F(posX)}</PositionX>");
        sb.Append($"<PositionY>{F(posY)}</PositionY>");
        sb.Append($"<PageNumber>{page}</PageNumber>");
        if (pencilDataEscaped != null)
            sb.Append($"<Data>{pencilDataEscaped}</Data>");
        sb.Append($"<Kind>{kind}</Kind>");
        sb.Append("</AnnotationContainer>");
        sb.Append(ArrayClose);
        return sb.ToString();
    }

    private static string PencilData(string geometry, string colorHex, bool isStraightLine, string geometryKind)
    {
        var sb = new StringBuilder();
        sb.Append(XmlHeader);
        sb.Append($"<PencilData {PencilDataNs}>");
        sb.Append($"<Geometry>{geometry}</Geometry>");
        sb.Append($"<Color>{colorHex}</Color>");
        sb.Append($"<IsStraightLine>{isStraightLine.ToString().ToLower()}</IsStraightLine>");
        sb.Append($"<GeometryKind>{geometryKind}</GeometryKind>");
        sb.Append("<IsTextNoteVisible>true</IsTextNoteVisible>");
        sb.Append("</PencilData>");
        // XML-escape the whole PencilData for embedding inside <Data>
        return EscapeXml(sb.ToString());
    }

    /// <summary>
    /// Генерирует строку геометрии эллипса для PencilData.Geometry.
    /// </summary>
    public static string GenerateEllipseGeometryString(double cx, double cy, double rx, double ry)
        => GenerateEllipseGeometry(cx, cy, rx, ry, 36);

    /// <summary>
    /// Генерирует строку геометрии стрелки для PencilData.Geometry.
    /// </summary>
    public static string GenerateArrowGeometryString(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) len = 1;
        var ux = dx / len;
        var uy = dy / len;
        const double arrowLen = 10;
        const double arrowAngle = 0.4;

        var ax1 = x2 - arrowLen * (ux * Math.Cos(arrowAngle) - uy * Math.Sin(arrowAngle));
        var ay1 = y2 - arrowLen * (uy * Math.Cos(arrowAngle) + ux * Math.Sin(arrowAngle));
        var ax2 = x2 - arrowLen * (ux * Math.Cos(-arrowAngle) - uy * Math.Sin(-arrowAngle));
        var ay2 = y2 - arrowLen * (uy * Math.Cos(-arrowAngle) + ux * Math.Sin(-arrowAngle));

        return $"M{F(x1)},{F(y1)}L{F(x2)},{F(y2)} M{F(ax1)},{F(ay1)}L{F(x2)},{F(y2)} {F(ax2)},{F(ay2)}";
    }

    private static string GenerateEllipseGeometry(double cx, double cy, double rx, double ry, int segments)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < segments; i++)
        {
            var angle = 2 * Math.PI * i / segments;
            var px = cx + rx * Math.Cos(angle);
            var py = cy + ry * Math.Sin(angle);
            sb.Append(i == 0 ? $"M{F(px)},{F(py)}" : $"L{F(px)},{F(py)} ");
        }
        // close to first point
        var firstAngle = 0.0;
        var fx = cx + rx * Math.Cos(firstAngle);
        var fy = cy + ry * Math.Sin(firstAngle);
        sb.Append($"{F(fx)},{F(fy)}z");
        return sb.ToString();
    }

    private static string EscapeXml(string xml)
    {
        // Только <, >, & нужно экранировать в текстовом содержимом XML.
        // Кавычки НЕ экранируются — &quot; нужен только внутри атрибутов.
        return xml
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static string F(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

    /// <summary>
    /// Преобразует имя цвета или hex в формат #AARRGGBB.
    /// </summary>
    public static string ResolveColor(string color)
    {
        var c = color.Trim().ToLowerInvariant();
        return c switch
        {
            "red" => "#FFFF0000",
            "green" => "#FF00FF00",
            "blue" => "#FF0000FF",
            "yellow" => "#FFFFFF00",
            "orange" => "#FFFF8C00",
            "purple" => "#FF800080",
            "pink" or "deeppink" => "#FFFF1493",
            "black" => "#FF000000",
            "white" => "#FFFFFFFF",
            "gray" or "grey" => "#FF808080",
            "cyan" => "#FF00FFFF",
            "magenta" => "#FFFF00FF",
            _ when c.StartsWith("#") && c.Length == 7 => $"#FF{c[1..].ToUpperInvariant()}", // #RRGGBB → #FFRRGGBB
            _ when c.StartsWith("#") && c.Length == 9 => c.ToUpperInvariant(), // already #AARRGGBB
            _ => "#FFFF0000" // fallback to red
        };
    }
}
