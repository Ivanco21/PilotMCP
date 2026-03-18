using Ascon.Pilot.DataClasses;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PilotMCP;

[McpServerToolType]
public class AdminTools
{
    private readonly PilotConnection _connection;
    public AdminTools(PilotConnection connection) => _connection = connection;

    [McpServerTool, Description(
        "Обновить данные пользователя: email, телефон, комментарий, статус активности. " +
        "Все параметры, кроме personId, необязательны — обновляются только указанные.")]
    public Task<string> UpdatePerson(
        [Description("ID пользователя (personId)")] int personId,
        [Description("Новый email (пусто = не менять)")] string email = "",
        [Description("Новый телефон (пусто = не менять)")] string phone = "",
        [Description("Новый комментарий (пусто = не менять)")] string comment = "",
        [Description("Сделать неактивным (true/false, пусто = не менять)")] string isInactive = "")
    {
        return ToolRunner.RunWriteAsync(_connection, "UpdatePerson",
            $"person={personId}, email={email}, phone={phone}, comment={comment}, isInactive={isInactive}", async () =>
        {
            var updateInfo = new DPersonUpdateInfo { Id = Guid.NewGuid() };

            bool hasChanges = false;
            if (!string.IsNullOrEmpty(email)) { updateInfo.Email = email; hasChanges = true; }
            if (!string.IsNullOrEmpty(phone)) { updateInfo.Phone = phone; hasChanges = true; }
            if (!string.IsNullOrEmpty(comment)) { updateInfo.Comment = comment; hasChanges = true; }
            if (!string.IsNullOrEmpty(isInactive))
            {
                updateInfo.IsInactive = isInactive.Equals("true", StringComparison.OrdinalIgnoreCase);
                hasChanges = true;
            }

            if (!hasChanges)
                return ToolResult.Error("Не указаны поля для обновления.");

            await _connection.ServerApi.UpdatePersonAsync(updateInfo);
            return ToolResult.Ok($"Данные пользователя #{personId} обновлены.");
        });
    }

    [McpServerTool, Description(
        "Изменить настройку пользователя или общую настройку базы. " +
        "Можно менять настройки ЛЮБОГО пользователя по его personId. " +
        "personId=0 — общая настройка, personId>0 — персональная настройка этого пользователя.")]
    public Task<string> ChangeSettings(
        [Description("Ключ настройки (раздел)")] string key,
        [Description("ID пользователя (personId). 0 = общая настройка")] int personId = 0,
        [Description("ID оргединицы (0 = без привязки к позиции)")] int orgUnitId = 0,
        [Description("Значение настройки (строка)")] string value = "")
    {
        return ToolRunner.RunWriteAsync(_connection, "ChangeSettings",
            $"key={key}, personId={personId}, orgUnitId={orgUnitId}", async () =>
        {
            var change = new DSettingsChange
            {
                Identity = Guid.NewGuid(),
                Key = key,
                Value = value,
                PersonId = personId,
                OrgUnitId = orgUnitId
            };
            await _connection.ServerApi.ChangeSettingsAsync(change);
            return ToolResult.Ok($"Настройка \"{key}\" обновлена.");
        });
    }
}
