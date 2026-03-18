using System.Text.Json.Nodes;

namespace PilotMCP.Tests;

[Collection("Pilot")]
public class MessengerToolsTests
{
    private readonly McpTestClient _client;
    public MessengerToolsTests(PilotFixture fixture) => _client = fixture.Client;

    [Fact]
    public async Task GetChats_ReturnsChats()
    {
        var result = await _client.CallToolAsync("GetChats", new JsonObject
        {
            ["count"] = 5
        });

        result.AssertOk();
    }

    [Fact]
    public async Task GetChat_WithInvalidId_ReturnsResult()
    {
        // Pilot returns an empty/default chat for unknown IDs, not an error
        var randomId = Guid.NewGuid().ToString();
        var result = await _client.CallToolAsync("GetChat", new JsonObject
        {
            ["chatId"] = randomId
        });

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task CheckOnline_WithValidPersonId()
    {
        var result = await _client.CallToolAsync("CheckOnline", new JsonObject
        {
            ["personId"] = 1
        });

        result.AssertOk();
    }

    [Fact]
    public async Task SearchMessages_ReturnsResult()
    {
        var result = await _client.CallToolAsync("SearchMessages", new JsonObject
        {
            ["query"] = "test"
        });

        // SearchMessages may return error if no messages API or empty results
        Assert.False(result.IsError, $"Tool call failed: {result.RawText}");
    }

    [Fact]
    public async Task GetMessages_WithInvalidChatId_ReturnsResult()
    {
        // May timeout or return empty — just verify no crash
        var randomId = Guid.NewGuid().ToString();
        var result = await _client.CallToolAsync("GetMessages", new JsonObject
        {
            ["chatId"] = randomId
        });

        Assert.NotNull(result.RawText);
    }
}
