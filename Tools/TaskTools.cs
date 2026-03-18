using Ascon.Pilot.DataModifier.NextTasks;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace PilotMCP;

[McpServerToolType]
public class TaskTools
{
    private readonly PilotConnection _connection;
    public TaskTools(PilotConnection connection) => _connection = connection;

    [McpServerTool, Description(
        "Создать задачу в Pilot. Тип задачи всегда начинается с 'task_' (получите через GetTypes). " +
        "Задача привязывается к объекту через связь TaskAttachments (не через parentId). " +
        "attachmentIds — GUID объектов, к которым относится задача.")]
    public Task<string> CreateTask(
        [Description("Системное имя типа задачи (начинается с 'task_', например 'task_simple').")] string typeName,
        [Description("GUID объектов-вложений через запятую (к каким объектам привязана задача).")] string attachmentIds = "",
        [Description("ID позиции инициатора (если 0 — текущий пользователь).")] int initiatorPositionId = 0,
        [Description("ID позиции исполнителя.")] int executorPositionId = 0,
        [Description("Дедлайн (формат: yyyy-MM-dd).")] string deadline = "",
        [Description("Атрибуты в формате JSON: {\"name\":\"value\"}.")] string attributes = "")
    {
        return ToolRunner.RunWriteAsync(_connection, "CreateTask", $"type={typeName}", async () =>
        {
            var type = _connection.Metadata.Types.FirstOrDefault(t =>
                t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (type == null)
                return ToolResult.Error($"Тип '{typeName}' не найден. Типы задач начинаются с 'task_'.");

            var taskModifier = new TaskModifier(_connection.Backend);
            var taskId = Guid.NewGuid();

            var builder = taskModifier.CreateTask(taskId, type, Guid.Empty);

            if (initiatorPositionId <= 0)
            {
                var person = _connection.DatabaseInfo.Person;
                if (person?.Positions != null && person.Positions.Count > 0)
                    initiatorPositionId = person.Positions[0];
            }
            if (initiatorPositionId > 0)
                builder.SetInitiator(initiatorPositionId);

            if (executorPositionId > 0)
                builder.SetExecutor(executorPositionId);
            if (!string.IsNullOrEmpty(deadline) && DateTime.TryParse(deadline, out var dl))
                builder.SetAttribute("deadlineDate", dl.ToUniversalTime());

            if (!string.IsNullOrEmpty(attachmentIds))
            {
                var guids = attachmentIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => Guid.TryParse(s, out _))
                    .Select(Guid.Parse)
                    .ToList();
                if (guids.Any())
                    builder.SetAttachments(guids);
            }

            var attrs = Helpers.ParseAttributes(attributes);
            if (attrs != null)
            {
                foreach (var kvp in attrs)
                    builder.SetAttribute(kvp.Key, kvp.Value);
            }

            taskModifier.Apply();

            var sb = new StringBuilder();
            sb.AppendLine($"Задача создана. ID: {taskId}");
            sb.AppendLine($"Тип: {typeName}");
            if (executorPositionId > 0) sb.AppendLine($"Исполнитель: positionId={executorPositionId}");
            if (!string.IsNullOrEmpty(deadline)) sb.AppendLine($"Дедлайн: {deadline}");
            if (!string.IsNullOrEmpty(attachmentIds)) sb.AppendLine($"Привязана к: {attachmentIds}");

            return ToolResult.Ok(sb.ToString());
        });
    }

    [McpServerTool, Description(
        "Создать workflow (процесс) со стадиями. " +
        "Workflow содержит стадии, каждая стадия содержит задачи.")]
    public Task<string> CreateWorkflow(
        [Description("GUID родительского объекта")] string parentId,
        [Description("Системное имя типа workflow")] string typeName,
        [Description("ID позиции инициатора (если 0 — текущий пользователь)")] int initiatorPositionId = 0,
        [Description("Атрибуты в формате JSON: {\"name\":\"value\"}")] string attributes = "")
    {
        return ToolRunner.RunWriteAsync(_connection, "CreateWorkflow", $"parent={parentId}, type={typeName}", async () =>
        {
            if (!Guid.TryParse(parentId, out var parentGuid))
                return ToolResult.Error($"Некорректный GUID: {parentId}");

            var type = _connection.Metadata.Types.FirstOrDefault(t =>
                t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (type == null)
                return ToolResult.Error($"Тип '{typeName}' не найден.");

            var taskModifier = new TaskModifier(_connection.Backend);
            var workflowId = Guid.NewGuid();
            var builder = taskModifier.CreateWorkflow(workflowId, type, parentGuid);

            if (initiatorPositionId > 0)
                builder.SetInitiator(initiatorPositionId);

            var attrs = Helpers.ParseAttributes(attributes);
            if (attrs != null)
            {
                var objBuilder = taskModifier.EditObject(workflowId);
                foreach (var kvp in attrs)
                    objBuilder.SetAttribute(kvp.Key, kvp.Value);
            }

            taskModifier.Apply();

            return ToolResult.Ok($"Workflow создан: {workflowId}\nТип: {typeName}");
        });
    }
}
