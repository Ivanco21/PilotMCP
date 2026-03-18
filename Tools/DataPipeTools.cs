using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace PilotMCP;

// =====================================================
// Transform-инструменты (файл → файл)
// =====================================================

[McpServerToolType]
public class TransformTools
{
    [McpServerTool, Description("Извлечь указанные поля из файла результатов. Создаёт новый файл только с выбранными колонками.")]
    public string ExtractFields(
        [Description("Путь к файлу результатов (JSONL)")] string inputFile,
        [Description("Имена полей через запятую, например: name,id,created")] string fields,
        [Description("Путь к выходному файлу. Если пусто — создаётся автоматически.")] string? outputFile = null)
    {
        if (!File.Exists(inputFile))
            return ToolResult.Error($"Файл не найден: {inputFile}");

        var fieldNames = fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fieldNames.Length == 0)
            return ToolResult.Error("Не указаны поля для извлечения.");

        var outPath = outputFile ?? TempFiles.NewPath("extract");
        int count = 0;

        using var writer = new StreamWriter(outPath, false, Encoding.UTF8);
        foreach (var row in TempFiles.ReadJsonl(inputFile))
        {
            var extracted = new Dictionary<string, object>();
            foreach (var f in fieldNames)
            {
                if (row.TryGetValue(f, out var val))
                    extracted[f] = val;
            }
            writer.WriteLine(JsonSerializer.Serialize(extracted));
            count++;
        }

        return ToolResult.File(outPath, count, fieldNames,
            $"Извлечено {fieldNames.Length} полей из {count} записей");
    }

    [McpServerTool, Description("Отфильтровать записи из файла результатов по значению поля. Создаёт новый файл с подходящими записями.")]
    public string FilterObjects(
        [Description("Путь к файлу результатов (JSONL)")] string inputFile,
        [Description("Имя поля для фильтрации")] string field,
        [Description("Значение для сравнения (строковое сравнение, содержит)")] string value,
        [Description("Путь к выходному файлу. Если пусто — создаётся автоматически.")] string? outputFile = null)
    {
        if (!File.Exists(inputFile))
            return ToolResult.Error($"Файл не найден: {inputFile}");

        var outPath = outputFile ?? TempFiles.NewPath("filter");
        int count = 0;
        string[]? columns = null;

        using var writer = new StreamWriter(outPath, false, Encoding.UTF8);
        foreach (var row in TempFiles.ReadJsonl(inputFile))
        {
            columns ??= row.Keys.ToArray();
            if (row.TryGetValue(field, out var fieldVal))
            {
                var strVal = fieldVal.ToString();
                if (strVal.Contains(value, StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteLine(JsonSerializer.Serialize(row));
                    count++;
                }
            }
        }

        return ToolResult.File(outPath, count, columns ?? Array.Empty<string>(),
            $"Найдено {count} записей где {field} содержит \"{value}\"");
    }

    [McpServerTool, Description("Извлечь значения одного поля из файла результатов и записать построчно в текстовый файл.")]
    public string ExtractFieldToText(
        [Description("Путь к файлу результатов (JSONL)")] string inputFile,
        [Description("Имя поля")] string field,
        [Description("Путь к выходному текстовому файлу")] string outputFile)
    {
        if (!File.Exists(inputFile))
            return ToolResult.Error($"Файл не найден: {inputFile}");

        int count = 0;
        using var writer = new StreamWriter(outputFile, false, Encoding.UTF8);
        foreach (var row in TempFiles.ReadJsonl(inputFile))
        {
            if (row.TryGetValue(field, out var val))
            {
                writer.WriteLine(val.ToString());
                count++;
            }
        }

        return ToolResult.Ok($"Записано {count} значений поля \"{field}\" в {outputFile}");
    }
}

// =====================================================
// Inspect-инструменты (файл → маленький текст для модели)
// =====================================================

[McpServerToolType]
public class InspectTools
{
    [McpServerTool, Description("Просмотреть страницу записей из файла результатов. Позволяет заглянуть в данные без загрузки всего файла в контекст.")]
    public string PreviewFile(
        [Description("Путь к файлу результатов (JSONL)")] string inputFile,
        [Description("Пропустить первых N записей")] int skip = 0,
        [Description("Показать N записей (макс 20)")] int take = 10)
    {
        if (!File.Exists(inputFile))
            return ToolResult.Error($"Файл не найден: {inputFile}");

        take = Math.Clamp(take, 1, 20);
        var total = TempFiles.CountLines(inputFile);
        var sb = new StringBuilder();
        sb.AppendLine($"Записей в файле: {total}. Показано {skip + 1}–{Math.Min(skip + take, total)}:");
        sb.AppendLine();

        int index = 0;
        foreach (var row in TempFiles.ReadJsonl(inputFile))
        {
            if (index < skip) { index++; continue; }
            if (index >= skip + take) break;

            sb.AppendLine($"[{index}] {JsonSerializer.Serialize(row)}");
            index++;
        }

        if (skip + take < total)
            sb.AppendLine($"\n--- Ещё {total - skip - take} записей. Используйте skip={skip + take} ---");

        return sb.ToString();
    }

    [McpServerTool, Description(
        "Прочитать текстовый файл (или его часть). Используйте для чтения файлов сохранённых другими инструментами " +
        "(GetAttribute, GetReport, ExportCsv и др.). Поддерживает пагинацию через offset/limit.")]
    public string ReadFile(
        [Description("Путь к файлу")] string filePath,
        [Description("Смещение в символах от начала (0 = с начала)")] int offset = 0,
        [Description("Макс. символов для чтения (0 = весь файл, по умолчанию 10000)")] int limit = 10000)
    {
        if (!File.Exists(filePath))
            return ToolResult.Error($"Файл не найден: {filePath}");

        var content = File.ReadAllText(filePath, Encoding.UTF8);
        var totalLength = content.Length;

        if (offset >= totalLength)
            return ToolResult.Ok($"Файл {totalLength} символов, смещение {offset} за пределами файла.");

        var chunk = limit > 0
            ? content.Substring(offset, Math.Min(limit, totalLength - offset))
            : content.Substring(offset);

        var sb = new StringBuilder();
        sb.AppendLine($"Файл: {Path.GetFileName(filePath)} ({totalLength} символов)");
        if (offset > 0 || (limit > 0 && offset + limit < totalLength))
            sb.AppendLine($"Показано: {offset}–{offset + chunk.Length} из {totalLength}");
        sb.AppendLine();
        sb.Append(chunk);

        if (limit > 0 && offset + limit < totalLength)
            sb.AppendLine($"\n\n--- Ещё {totalLength - offset - chunk.Length} символов. Используйте offset={offset + chunk.Length} ---");

        return ToolResult.Ok(sb.ToString());
    }

    [McpServerTool, Description("Посчитать количество записей в файле результатов.")]
    public string CountFile(
        [Description("Путь к файлу результатов (JSONL)")] string inputFile)
    {
        if (!File.Exists(inputFile))
            return ToolResult.Error($"Файл не найден: {inputFile}");

        var count = TempFiles.CountLines(inputFile);
        return ToolResult.Ok($"В файле {count} записей.");
    }
}

// =====================================================
// Export-инструменты (файл → итоговый формат)
// =====================================================

[McpServerToolType]
public class ExportTools
{
    [McpServerTool, Description("Экспортировать файл результатов в CSV.")]
    public string ExportCsv(
        [Description("Путь к файлу результатов (JSONL)")] string inputFile,
        [Description("Путь к выходному CSV файлу")] string outputFile,
        [Description("Колонки через запятую. Если пусто — все колонки.")] string? columns = null,
        [Description("Разделитель (по умолчанию ;)")] string separator = ";")
    {
        if (!File.Exists(inputFile))
            return ToolResult.Error($"Файл не найден: {inputFile}");

        var dir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string[]? columnNames = columns?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int count = 0;
        bool headerWritten = false;

        using var writer = new StreamWriter(outputFile, false, Encoding.UTF8);
        foreach (var row in TempFiles.ReadJsonl(inputFile))
        {
            columnNames ??= row.Keys.ToArray();

            if (!headerWritten)
            {
                writer.WriteLine(string.Join(separator, columnNames));
                headerWritten = true;
            }

            var values = columnNames.Select(c =>
            {
                if (row.TryGetValue(c, out var val))
                {
                    var s = val.ToString();
                    if (s.Contains(separator) || s.Contains('"') || s.Contains('\n'))
                        return $"\"{s.Replace("\"", "\"\"")}\"";
                    return s;
                }
                return "";
            });
            writer.WriteLine(string.Join(separator, values));
            count++;
        }

        return ToolResult.Ok($"Экспортировано {count} записей в CSV: {outputFile}");
    }

    [McpServerTool, Description("Скопировать файл результатов в указанное место (переименовать/сохранить).")]
    public string SaveResult(
        [Description("Путь к файлу результатов")] string inputFile,
        [Description("Путь для сохранения")] string outputFile)
    {
        if (!File.Exists(inputFile))
            return ToolResult.Error($"Файл не найден: {inputFile}");

        var dir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.Copy(inputFile, outputFile, overwrite: true);
        return ToolResult.Ok($"Файл сохранён: {outputFile}");
    }
}
