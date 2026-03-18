using System.Text;

namespace PilotMCP;

/// <summary>
/// Логирует все вызовы MCP-инструментов в файл для отладки.
/// Лог пишется в %TEMP%/pilot-mcp/tools.log
/// </summary>
public static class ToolLogger
{
    private static readonly string LogDir = Path.Combine(Path.GetTempPath(), "pilot-mcp");
    private static readonly string LogPath = Path.Combine(LogDir, "tools.log");
    private static readonly object Lock = new();

    static ToolLogger()
    {
        Directory.CreateDirectory(LogDir);
    }

    public static void Log(string toolName, string args, string result)
    {
        var entry = new StringBuilder();
        entry.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {toolName}");
        entry.AppendLine($"  Args: {args}");
        // Truncate long results for readability
        var resultPreview = result.Length > 500 ? result[..500] + "..." : result;
        entry.AppendLine($"  Result: {resultPreview}");
        entry.AppendLine();

        lock (Lock)
        {
            File.AppendAllText(LogPath, entry.ToString(), Encoding.UTF8);
        }

        // Also write to stderr for MCP client visibility
        Console.Error.WriteLine($"[PilotMCP] {toolName}({args}) → {(result.Length > 200 ? result[..200] + "..." : result)}");
    }

    public static void LogError(string toolName, string args, Exception ex)
    {
        Log(toolName, args, $"ERROR: {ex.Message}\n{ex.StackTrace}");
    }
}
