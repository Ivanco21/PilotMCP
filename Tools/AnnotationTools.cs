using Ascon.Pilot.DataClasses;
using Ascon.Pilot.DataModifier.Objects;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PilotMCP;

// =====================================================
// Annotation-инструменты (создание замечаний)
// =====================================================

[McpServerToolType]
public class AnnotationTools
{
    private readonly PilotConnection _connection;
    public AnnotationTools(PilotConnection connection) => _connection = connection;

    private string DownloadFile(Guid bodyId, string ext)
        => Helpers.DownloadToTemp(_connection.FileApi, bodyId, ext);

    [McpServerTool, Description(
        "Создать замечание к тексту на документе. Работает с любыми документами (PDF, XPS). " +
        "Автоматически находит слово/фразу и выделяет её. " +
        "Используйте для рецензирования и пометки ошибок. " +
        "Пример: searchText='квартыры', comment='Опечатка: должно быть квартиры'")]
    public Task<string> AnnotateText(
        [Description("GUID документа")] string documentId,
        [Description("Слово или фраза из документа для выделения (копируйте из текста документа, включая опечатки)")] string searchText,
        [Description("Пояснение к замечанию (что не так, как исправить)")] string comment,
        [Description("Номер страницы (с 0). Если -1, ищет на всех страницах.")] int page = -1,
        [Description("Цвет выделения")] string color = "red",
        [Description("Какое вхождение использовать, если найдено несколько (с 0)")] int occurrenceIndex = 0)
    {
        return ToolRunner.RunWriteAsync(_connection, "AnnotateText",
            $"doc={documentId}, text={searchText}, comment={comment}, page={page}", async () =>
        {
            if (!Guid.TryParse(documentId, out var docGuid))
                return ToolResult.Error($"Некорректный GUID: {documentId}");

            var objects = await _connection.ServerApi.GetObjectsAsync(new[] { docGuid });
            if (objects == null || objects.Count == 0)
                return ToolResult.Error($"Документ {documentId} не найден.");

            var doc = objects[0];

            var xpsFile = doc.ActualFileSnapshot.Files
                .FirstOrDefault(f => f.Name.EndsWith(".xps", StringComparison.OrdinalIgnoreCase));
            var pdfFile = doc.ActualFileSnapshot.Files
                .FirstOrDefault(f => f.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

            if (xpsFile == null && pdfFile == null)
                return ToolResult.Error("Нет XPS или PDF файла.");

            List<XpsTextExtractor.TextBlock> results;

            if (xpsFile != null)
            {
                var tempPath = DownloadFile(xpsFile.Body.Id, ".xps");
                results = XpsTextExtractor.FindText(tempPath, searchText);
                try { File.Delete(tempPath); } catch { }
            }
            else
            {
                var tempPath = DownloadFile(pdfFile!.Body.Id, ".pdf");
                var pdfBlocks = PdfTextExtractor.ExtractTextBlocks(tempPath);
                try { File.Delete(tempPath); } catch { }

                const double scale = 96.0 / 72.0;
                results = new List<XpsTextExtractor.TextBlock>();
                foreach (var block in pdfBlocks)
                {
                    var idx = block.Text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var charWidth = block.Width / Math.Max(block.Text.Length, 1);
                        var offsetX = idx * charWidth;
                        var matchWidth = searchText.Length * charWidth;
                        results.Add(new XpsTextExtractor.TextBlock(
                            Text: block.Text,
                            X: (block.X + offsetX) * scale,
                            Y: block.Y * scale,
                            Width: matchWidth * scale,
                            FontSize: block.FontSize * scale,
                            Page: block.Page
                        ));
                    }
                }
                if (results.Count == 0)
                {
                    var lineGroups = pdfBlocks.GroupBy(b => (b.Page, YLine: Math.Round(b.Y / 5) * 5));
                    foreach (var group in lineGroups)
                    {
                        var lineBlocks = group.OrderBy(b => b.X).ToList();
                        var lineText = string.Join(" ", lineBlocks.Select(b => b.Text));
                        var idx2 = lineText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                        if (idx2 >= 0 && lineBlocks.Count > 0)
                        {
                            var first = lineBlocks[0];
                            results.Add(new XpsTextExtractor.TextBlock(
                                Text: searchText,
                                X: first.X * scale,
                                Y: first.Y * scale,
                                Width: searchText.Length * first.FontSize * 0.5 * scale,
                                FontSize: first.FontSize * scale,
                                Page: first.Page
                            ));
                        }
                    }
                }
            }

            if (page >= 0)
                results = results.Where(r => r.Page == page).ToList();

            if (results.Count == 0)
                return ToolResult.Error(
                    $"Текст \"{searchText}\" не найден. Убедитесь, что ищете текст ТОЧНО как написан в документе.");

            if (occurrenceIndex >= results.Count)
                occurrenceIndex = 0;

            var match = results[occurrenceIndex];
            var matchPage = match.Page;

            var snapshotVersion = doc.ActualFileSnapshot.Created.ToUniversalTime();
            var remarkId = Guid.NewGuid();

            var x1 = match.X;
            var yBaseline = match.Y + match.FontSize;
            var x2 = match.X + match.Width;
            var posY = yBaseline + 10;

            var remarkType = _connection.Metadata.Types.FirstOrDefault(t =>
                t.Name.Equals("doc_issue", StringComparison.OrdinalIgnoreCase));
            if (remarkType == null)
                return ToolResult.Error("Тип 'doc_issue' не найден.");

            var container = new AnnotationContainer
            {
                AnnotationId = remarkId,
                Version = snapshotVersion,
                PositionX = x1,
                PositionY = posY,
                PageNumber = matchPage,
                Kind = "TextStickyNote"
            };
            container.WriteData(new TextStickyNoteData
            {
                FixedTextRange = $"{F(x1)},{F(yBaseline)},{F(x2)},{F(yBaseline)}"
            });

            var annotationModifier = new AnnotationModifier(
                _connection.Backend, ChangesetDataSource.Native, MergeChangePolicy.Allow);
            var builder = annotationModifier.CreateAnnotation(remarkId, doc, container, remarkType.Id, snapshotVersion);
            builder.SetAttribute("text", new DValue { StrValue = comment });
            annotationModifier.Apply(null);

            return ToolResult.Ok(
                $"Замечание к тексту создано. ID: {remarkId}\n" +
                $"Текст: \"{searchText}\" (стр. {matchPage}, x={match.X:F0}..{match.X + match.Width:F0})\n" +
                $"Комментарий: {comment}");
        });
    }

    /// <summary>
    /// Пытается извлечь ошибочное слово из комментария.
    /// </summary>
    private static string? TryExtractErrorWord(string content)
    {
        var lower = content.ToLowerInvariant();
        if (!lower.Contains("ошибк") && !lower.Contains("опечатк") &&
            !lower.Contains("должно быть") && !lower.Contains("орфограф"))
            return null;

        var match = System.Text.RegularExpressions.Regex.Match(content, @"[\u00AB\u201E\u201C""](\S+?)[\u00BB\u201D\u201F""]");
        if (match.Success)
            return match.Groups[1].Value;

        var quoteChars = new[] { '\u00AB', '\u00BB', '\u201E', '\u201C', '\u201D', '"' };
        var arrowMatch = System.Text.RegularExpressions.Regex.Match(content, @"(\S+)\s*(?:->|\u2192)\s*\S+");
        if (arrowMatch.Success)
            return arrowMatch.Groups[1].Value.Trim(quoteChars);

        return null;
    }

    [McpServerTool, Description(
        "Нарисовать графическую фигуру на документе (прямоугольник, эллипс, стрелку). " +
        "Для замечаний к тексту с выделением слова лучше подходит AnnotateText. " +
        "КООРДИНАТЫ: передавайте ПИКСЕЛЬНЫЕ координаты прямо с изображения от GetDocumentPageImage. " +
        "Инструмент сам пересчитает их в единицы документа по DPI. " +
        "Rectangle: x,y = левый верхний угол, width/height = размеры. " +
        "Ellipse: x,y = центр объекта, width/height = диаметры. " +
        "Arrow: x,y = куда указывает остриё, width/height = смещение хвоста от цели.")]
    public Task<string> CreateAnnotation(
        [Description("GUID документа, на котором создаётся замечание")] string documentId,
        [Description("Тип: rectangle, ellipse, triangle, arrow, point. Для текстовых замечаний используйте AnnotateText!")] string shape,
        [Description("Цвет: red, green, blue, yellow, pink, black или hex #RRGGBB")] string color,
        [Description("Координата X в пикселях изображения. Rectangle/triangle: левый верхний угол. Ellipse: центр. Arrow: цель остриём.")] double x,
        [Description("Координата Y в пикселях изображения.")] double y,
        [Description("Ширина в пикселях. Arrow: смещение хвоста по X. Ellipse: полный диаметр.")] double width = 100,
        [Description("Высота в пикселях. Arrow: смещение хвоста по Y. Ellipse: полный диаметр.")] double height = 60,
        [Description("Номер страницы (начинается с 0)")] int page = 0,
        [Description("Текст пояснения к замечанию. Заполняйте для рецензий и комментариев.")] string content = "",
        [Description("DPI изображения от GetDocumentPageImage (по умолчанию 150)")] int dpi = 150,
        [Description("Системное имя типа замечания в базе")] string remarkTypeName = "doc_issue",
        [Description("Системное имя атрибута для XML-разметки")] string attributeName = "annotation")
    {
        // Пересчёт пикселей → единицы документа (96 DPI)
        var scale = dpi / 96.0;
        x /= scale;
        y /= scale;
        width /= scale;
        height /= scale;

        return ToolRunner.RunWriteAsync(_connection, "CreateAnnotation",
            $"doc={documentId}, shape={shape}, color={color}, x={x:F1}, y={y:F1}, w={width:F1}, h={height:F1}, page={page}, dpi={dpi}", async () =>
        {
            // Перенаправляем текстовые замечания на AnnotateText
            var shapeLower = shape.Trim().ToLowerInvariant();
            if (shapeLower is "text" or "textstickynote" or "textnote")
                return ToolResult.Error(
                    "Для замечаний к тексту используйте AnnotateText(documentId, searchText, comment). " +
                    "CreateAnnotation — только для графических фигур.");

            // Автоматическое перенаправление: если content содержит исправление
            if (!string.IsNullOrEmpty(content))
            {
                var searchWord = TryExtractErrorWord(content);
                if (searchWord != null)
                {
                    Console.Error.WriteLine($"[PilotMCP] CreateAnnotation -> AnnotateText redirect: '{searchWord}'");
                    return await AnnotateTextImpl(documentId, searchWord, content, page);
                }
            }

            if (!Guid.TryParse(documentId, out var docGuid))
                return ToolResult.Error($"Некорректный GUID документа: {documentId}");

            var remarkType = _connection.Metadata.Types.FirstOrDefault(t =>
                t.Name.Equals(remarkTypeName, StringComparison.OrdinalIgnoreCase));
            if (remarkType == null)
                return ToolResult.Error(
                    $"Тип '{remarkTypeName}' не найден в базе. " +
                    "Используйте GetTypes чтобы найти правильное имя типа замечаний.");

            var docObjects = await _connection.ServerApi.GetObjectsAsync(new[] { docGuid });
            if (docObjects == null || docObjects.Count == 0)
                return ToolResult.Error($"Документ {documentId} не найден.");

            var doc = docObjects[0];
            var snapshotVersion = doc.ActualFileSnapshot.Created.ToUniversalTime();

            var remarkId = Guid.NewGuid();

            var container = new AnnotationContainer
            {
                AnnotationId = remarkId,
                Version = snapshotVersion,
                PositionX = x,
                PositionY = y,
                PageNumber = page,
            };

            var hex = AnnotationBuilder.ResolveColor(color);
            switch (shapeLower)
            {
                case "rectangle" or "rect":
                    container.Kind = "RedPencil";
                    container.WriteData(new PencilData
                    {
                        Geometry = $"M{F(x)},{F(y)}L{F(x + width)},{F(y)} {F(x + width)},{F(y + height)} {F(x)},{F(y + height)} {F(x)},{F(y)}z",
                        Color = hex,
                        IsStraightLine = false,
                        GeometryKind = GeometryKind.Rectangle,
                        IsTextNoteVisible = true
                    });
                    break;
                case "ellipse" or "circle":
                    // (x, y) = центр эллипса/круга, width/height = полные размеры
                    container.PositionX = x - width / 2;
                    container.PositionY = y - height / 2;
                    container.Kind = "RedPencil";
                    container.WriteData(new PencilData
                    {
                        Geometry = AnnotationBuilder.GenerateEllipseGeometryString(x, y, width / 2, height / 2),
                        Color = hex,
                        IsStraightLine = false,
                        GeometryKind = GeometryKind.Ellipse,
                        IsTextNoteVisible = true
                    });
                    break;
                case "triangle":
                    container.Kind = "RedPencil";
                    container.WriteData(new PencilData
                    {
                        Geometry = $"M{F(x)},{F(y + height)}L{F(x + width / 2)},{F(y)} {F(x + width)},{F(y + height)} {F(x)},{F(y + height)}z",
                        Color = hex,
                        IsStraightLine = false,
                        GeometryKind = GeometryKind.Triangle,
                        IsTextNoteVisible = true
                    });
                    break;
                case "arrow" or "line":
                    // (x, y) = цель (остриё стрелки)
                    // (x+width, y+height) = хвост (откуда идёт стрелка)
                    var tailX = x + width;
                    var tailY = y + height;
                    container.Kind = "RedPencil";
                    container.WriteData(new PencilData
                    {
                        Geometry = AnnotationBuilder.GenerateArrowGeometryString(tailX, tailY, x, y),
                        Color = hex,
                        IsStraightLine = true,
                        GeometryKind = GeometryKind.Line,
                        IsTextNoteVisible = true
                    });
                    container.PositionX = Math.Min(x, tailX);
                    container.PositionY = Math.Min(y, tailY);
                    break;
                case "point" or "textnote":
                    container.Kind = "TextNote";
                    break;
                default:
                    return ToolResult.Error($"Неизвестная фигура: {shape}. Используйте: rectangle, ellipse, triangle, arrow, point");
            }

            var annotationModifier = new AnnotationModifier(
                _connection.Backend, ChangesetDataSource.Native, MergeChangePolicy.Allow);
            var builder = annotationModifier.CreateAnnotation(remarkId, doc, container, remarkType.Id, snapshotVersion);
            if (!string.IsNullOrWhiteSpace(content))
                builder.SetAttribute("text", new DValue { StrValue = content });
            annotationModifier.Apply(null);

            return ToolResult.Ok(
                $"Замечание создано. ID: {remarkId}\n" +
                $"Фигура: {shape}, цвет: {AnnotationBuilder.ResolveColor(color)}, страница: {page}\n" +
                $"Позиция: ({x}, {y}), размер: {width}x{height}");
        });
    }

    /// <summary>
    /// Внутренняя реализация AnnotateText для вызова из CreateAnnotation (без повторного оборачивания в ToolRunner).
    /// </summary>
    private Task<string> AnnotateTextImpl(string documentId, string searchText, string comment, int page)
    {
        // Делегируем в публичный метод — он уже обёрнут в ToolRunner
        return AnnotateText(documentId, searchText, comment, page);
    }

    private static string F(double value) => value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);

    [McpServerTool, Description(
        "Получить XML-разметку аннотации без создания объекта. " +
        "Полезно для проверки или ручной вставки в атрибут.")]
    public string BuildAnnotationXml(
        [Description("Тип фигуры: rectangle, ellipse, triangle, arrow, point")] string shape,
        [Description("Цвет: red, green, blue, yellow, pink, black или hex #RRGGBB")] string color,
        [Description("Координата X")] double x,
        [Description("Координата Y")] double y,
        [Description("Ширина (для arrow: смещение по X)")] double width = 100,
        [Description("Высота (для arrow: смещение по Y)")] double height = 60,
        [Description("Номер страницы")] int page = 0)
    {
        try
        {
            var xml = shape.ToLowerInvariant() switch
            {
                "rectangle" or "rect" => AnnotationBuilder.Rectangle(x, y, width, height, page, color),
                "ellipse" or "circle" => AnnotationBuilder.Ellipse(x, y, width / 2, height / 2, page, color), // x,y = center
                "triangle" => AnnotationBuilder.Triangle(x, y, width, height, page, color),
                "arrow" or "line" => AnnotationBuilder.Arrow(x + width, y + height, x, y, page, color),
                "point" or "textnote" => AnnotationBuilder.TextNote(x, y, page),
                "textstickynote" or "stickynote" or "text" => AnnotationBuilder.TextStickyNote(x, y, width, height, page),
                _ => throw new ArgumentException($"Неизвестная фигура: {shape}")
            };
            return ToolResult.Ok($"XML-разметка:\n{xml}");
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Error(ex.Message);
        }
    }
}
