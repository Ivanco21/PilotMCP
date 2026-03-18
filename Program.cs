using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PilotMCP;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var connection = new PilotConnection();
builder.Services.AddSingleton(connection);

// Автоподключение из переменных окружения
var serverUrl = Environment.GetEnvironmentVariable("PILOT_SERVER_URL");
var login = Environment.GetEnvironmentVariable("PILOT_LOGIN");
var password = Environment.GetEnvironmentVariable("PILOT_PASSWORD");
var database = Environment.GetEnvironmentVariable("PILOT_DATABASE");
var licenseStr = Environment.GetEnvironmentVariable("PILOT_LICENSE_TYPE");
int licenseType = 100;
if (!string.IsNullOrEmpty(licenseStr))
    int.TryParse(licenseStr, out licenseType);

var readonlyStr = Environment.GetEnvironmentVariable("PILOT_READONLY");
bool isReadOnly = string.Equals(readonlyStr, "true", StringComparison.OrdinalIgnoreCase)
                || readonlyStr == "1";
connection.IsReadOnly = isReadOnly;

var serverInstructions = isReadOnly
    ? "РЕЖИМ ТОЛЬКО ЧТЕНИЯ АКТИВЕН (PILOT_READONLY=true). " +
      "Все операции записи, создания, удаления и изменения данных заблокированы. " +
      "Доступны только инструменты чтения: GetObject, Search, GetChildren, GetDocumentText, GetHistory и т.д. " +
      "Если пользователь просит что-то изменить/создать/удалить — сообщи ему, что сервер работает в режиме только чтения " +
      "и попроси отключить PILOT_READONLY для выполнения операций записи."
    : null;

builder.Services.AddMcpServer(options =>
    {
        options.ServerInstructions = serverInstructions;
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

if (!string.IsNullOrEmpty(serverUrl) && !string.IsNullOrEmpty(login) && !string.IsNullOrEmpty(password))
{
    try
    {
        var msg = connection.Connect(serverUrl, login, password, database, licenseType);
        Console.Error.WriteLine($"[PilotMCP] {msg}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[PilotMCP] Ошибка автоподключения: {ex.Message}");
    }
}

await builder.Build().RunAsync();
