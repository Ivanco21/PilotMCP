using Ascon.Pilot.DataModifier;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PilotMCP;

[McpServerToolType]
public class FileUploadTools
{
    private readonly PilotConnection _connection;
    public FileUploadTools(PilotConnection connection) => _connection = connection;

    [McpServerTool, Description(
        "Загрузить файл в существующий объект Pilot (добавить/обновить вложение).")]
    public Task<string> UploadFile(
        [Description("GUID объекта, к которому прикрепить файл")] string objectId,
        [Description("Путь к файлу на диске")] string filePath)
    {
        return ToolRunner.RunWriteAsync(_connection, "UploadFile", $"obj={objectId}, file={filePath}", async () =>
        {
            if (!Guid.TryParse(objectId, out var objGuid))
                return ToolResult.Error($"Некорректный GUID: {objectId}");

            if (!File.Exists(filePath))
                return ToolResult.Error($"Файл не найден: {filePath}");

            var docInfo = new DocumentInfo(filePath);
            var modifier = _connection.CreateModifier();
            modifier.EditObject(objGuid)
                .CreateSnapshot("File upload via MCP")
                .AddFile(docInfo, _connection.StorageProvider);

            if (modifier.AnyChanges())
                modifier.Apply(null);

            return ToolResult.Ok(
                $"Файл загружен: {Path.GetFileName(filePath)} ({new FileInfo(filePath).Length / 1024} KB)\nОбъект: {objectId}");
        });
    }
}
