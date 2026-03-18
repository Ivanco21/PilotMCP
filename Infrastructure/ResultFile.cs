using System.Text.Json;
using System.Text.Json.Serialization;

namespace PilotMCP;

/// <summary>
/// Стандартизированный результат работы инструмента.
/// Модель получает только метаданные, данные лежат в файле.
/// </summary>
public class ToolResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "ok";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("resultFile")]
    public string? ResultFile { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("columns")]
    public string[]? Columns { get; set; }

    public override string ToString() => JsonSerializer.Serialize(this, JsonCtx.Default.ToolResult);

    public static string Ok(string message) =>
        new ToolResult { Message = message }.ToString();

    public static string Error(string message) =>
        new ToolResult { Status = "error", Message = message }.ToString();

    public static string File(string path, int count, string[] columns, string message) =>
        new ToolResult
        {
            ResultFile = path,
            Count = count,
            Columns = columns,
            Message = message
        }.ToString();
}

/// <summary>
/// Управление временными файлами результатов.
/// Формат: JSONL — одна JSON-строка на запись.
/// </summary>
public static class TempFiles
{
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "pilot-mcp");

    private static readonly TimeSpan MaxFileAge = TimeSpan.FromHours(24);

    static TempFiles()
    {
        Directory.CreateDirectory(TempDir);
        CleanupOldFiles();
    }

    /// <summary>
    /// Удаляет временные файлы старше MaxFileAge.
    /// </summary>
    private static void CleanupOldFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow - MaxFileAge;
            foreach (var file in Directory.EnumerateFiles(TempDir, "*.jsonl"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch { /* файл может быть занят */ }
            }
        }
        catch { /* не критично — очистка best-effort */ }
    }

    public static string NewPath(string prefix = "result")
    {
        return Path.Combine(TempDir, $"{prefix}_{Guid.NewGuid():N}.jsonl");
    }

    /// <summary>
    /// Пишет коллекцию объектов в JSONL-файл.
    /// objectMapper преобразует каждый объект в Dictionary для сериализации.
    /// Возвращает (путь, количество записей).
    /// </summary>
    public static (string path, int count) WriteJsonl<T>(
        IEnumerable<T> items,
        Func<T, Dictionary<string, object>> objectMapper,
        string prefix = "result")
    {
        var path = NewPath(prefix);
        int count = 0;

        using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        foreach (var item in items)
        {
            var dict = objectMapper(item);
            var json = JsonSerializer.Serialize(dict, JsonCtx.Default.DictionaryStringObject);
            writer.WriteLine(json);
            count++;
        }

        return (path, count);
    }

    /// <summary>
    /// Читает JSONL-файл построчно, возвращая Dictionary на каждую строку.
    /// </summary>
    public static IEnumerable<Dictionary<string, JsonElement>> ReadJsonl(string path)
    {
        foreach (var line in System.IO.File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);
            if (dict != null)
                yield return dict;
        }
    }

    /// <summary>
    /// Считает количество строк в JSONL-файле.
    /// </summary>
    public static int CountLines(string path)
    {
        if (!System.IO.File.Exists(path)) return 0;
        int count = 0;
        foreach (var line in System.IO.File.ReadLines(path))
        {
            if (!string.IsNullOrWhiteSpace(line)) count++;
        }
        return count;
    }
}

[JsonSerializable(typeof(ToolResult))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(Guid))]
internal partial class JsonCtx : JsonSerializerContext
{
}
