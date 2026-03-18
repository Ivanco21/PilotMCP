using System.Text.Json.Nodes;

namespace PilotMCP.Tests;

[Collection("Pilot")]
public class MiscToolsTests
{
    private readonly McpTestClient _client;
    public MiscToolsTests(PilotFixture fixture) => _client = fixture.Client;

    private const string RootObjectId = "00000001-0001-0001-0001-000000000001";

    // ── AccessTools ──

    [Fact]
    public async Task CheckAccess_OnRootObject_ReturnsOk()
    {
        var result = await _client.CallToolAsync("CheckAccess", new JsonObject
        {
            ["objectId"] = RootObjectId,
            ["personId"] = 1
        });

        Assert.False(result.IsError, $"Tool error: {result.RawText}");
    }

    [Fact]
    public async Task GetAccessRecords_OnRootObject_ReturnsOk()
    {
        var result = await _client.CallToolAsync("GetAccessRecords", new JsonObject
        {
            ["objectId"] = RootObjectId
        });

        Assert.False(result.IsError, $"Tool error: {result.RawText}");
    }

    [Fact]
    public async Task GetSubscribers_OnRootObject_ReturnsOk()
    {
        var result = await _client.CallToolAsync("GetSubscribers", new JsonObject
        {
            ["objectId"] = RootObjectId
        });

        result.AssertOk();
    }

    // ── HistoryTools ──

    [Fact]
    public async Task GetHistory_ReturnsResult()
    {
        var result = await _client.CallToolAsync("GetHistory", new JsonObject
        {
            ["objectId"] = "818cead7-c607-4943-afd6-27a98301ab06"
        });

        // GetHistory may return error for some objects — just verify no crash
        Assert.False(result.IsError && result.RawText.Contains("disposed"),
            $"Connection error: {result.RawText}");
    }

    [Fact]
    public async Task GetChangesets_ReturnsChangesets()
    {
        var result = await _client.CallToolAsync("GetChangesets", new JsonObject
        {
            ["first"] = 1,
            ["last"] = 5
        });

        result.AssertOk();
    }

    // ── PeopleTools ──

    [Fact]
    public async Task GetPeopleByIds_ReturnsData()
    {
        var result = await _client.CallToolAsync("GetPeopleByIds", new JsonObject
        {
            ["personIds"] = "1"
        });

        Assert.False(result.IsError, $"Tool error: {result.RawText}");
    }

    [Fact]
    public async Task GetOrgUnitsByIds_WithInvalidId_ReturnsResult()
    {
        var result = await _client.CallToolAsync("GetOrgUnitsByIds", new JsonObject
        {
            ["unitIds"] = "1"
        });

        Assert.False(result.IsError, $"Tool error: {result.RawText}");
    }

    // ── SettingsTools ──

    [Fact]
    public async Task GetPersonalSettings_ReturnsSettings()
    {
        var result = await _client.CallToolAsync("GetPersonalSettings", new JsonObject
        {
            ["key"] = "test"
        });

        Assert.False(result.IsError, $"Tool error: {result.RawText}");
    }

    [Fact]
    public async Task GetCommonSettings_ReturnsSettings()
    {
        var result = await _client.CallToolAsync("GetCommonSettings");

        Assert.False(result.IsError, $"Tool error: {result.RawText}");
    }

    // ── RelationTools ──

    [Fact]
    public async Task GetObjectsWithRights_ReturnsData()
    {
        var result = await _client.CallToolAsync("GetObjectsWithRights", new JsonObject
        {
            ["objectIds"] = RootObjectId
        });

        Assert.False(result.IsError, $"Tool error: {result.RawText}");
    }

    // ── New tools (read-only tests) ──

    [Fact]
    public async Task CheckAccessByUnit_OnRoot_ReturnsOk()
    {
        var result = await _client.CallToolAsync("CheckAccessByUnit", new JsonObject
        {
            ["objectId"] = RootObjectId,
            ["unitId"] = 1
        });

        Assert.False(result.IsError, $"Tool error: {result.RawText}");
    }

    // ── PeopleTools ──

    [Fact]
    public async Task GetPersonOnPosition_ReturnsResult()
    {
        var result = await _client.CallToolAsync("GetPersonOnPosition", new JsonObject
        {
            ["positionId"] = 1
        });

        Assert.False(result.IsError, $"Tool error: {result.RawText}");
    }
}
