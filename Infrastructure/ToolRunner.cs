namespace PilotMCP;

/// <summary>
/// Обёртка для типового шаблона вызова MCP-инструмента:
/// EnsureConnected → выполнение → логирование → обработка ошибок.
/// </summary>
public static class ToolRunner
{
    public static async Task<string> RunAsync(
        PilotConnection connection, string toolName, string logArgs,
        Func<Task<string>> action)
    {
        try
        {
            connection.EnsureConnected();
            var result = await action();
            ToolLogger.Log(toolName, logArgs, result);
            return result;
        }
        catch (Exception ex)
        {
            ToolLogger.LogError(toolName, logArgs, ex);
            return ToolResult.Error($"Ошибка: {ex.Message}");
        }
    }

    public static string Run(
        PilotConnection connection, string toolName, string logArgs,
        Func<string> action)
    {
        try
        {
            connection.EnsureConnected();
            var result = action();
            ToolLogger.Log(toolName, logArgs, result);
            return result;
        }
        catch (Exception ex)
        {
            ToolLogger.LogError(toolName, logArgs, ex);
            return ToolResult.Error($"Ошибка: {ex.Message}");
        }
    }

    /// <summary>
    /// Для инструментов, требующих проверки ReadOnly перед выполнением.
    /// </summary>
    public static async Task<string> RunWriteAsync(
        PilotConnection connection, string toolName, string logArgs,
        Func<Task<string>> action)
    {
        if (connection.ReadOnlyError is { } roErr) return ToolResult.Error(roErr);
        return await RunAsync(connection, toolName, logArgs, action);
    }

    public static string RunWrite(
        PilotConnection connection, string toolName, string logArgs,
        Func<string> action)
    {
        if (connection.ReadOnlyError is { } roErr) return ToolResult.Error(roErr);
        return Run(connection, toolName, logArgs, action);
    }
}
