using System.Text.Json.Nodes;

namespace PilotMCP.Tests;

[Collection("Pilot")]
public class ObjectToolsTests
{
    private readonly McpTestClient _client;
    public ObjectToolsTests(PilotFixture fixture) => _client = fixture.Client;

    [Fact]
    public async Task GetDatabaseInfo_ReturnsInfo()
    {
        var result = await _client.CallToolAsync("GetDatabaseInfo");

        result.AssertOk();
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task GetObject_WithValidId_ReturnsObject()
    {
        var result = await _client.CallToolAsync("GetObject", new JsonObject
        {
            ["objectId"] = "00000001-0001-0001-0001-000000000001"
        });

        Assert.False(result.IsError, $"Expected success but got: {result.RawText}");
        Assert.False(string.IsNullOrWhiteSpace(result.RawText));
        Assert.Contains("ID:", result.RawText);
    }

    [Fact]
    public async Task GetObject_WithInvalidId_ReturnsError()
    {
        var result = await _client.CallToolAsync("GetObject", new JsonObject
        {
            ["objectId"] = "not-a-guid"
        });

        result.AssertError();
    }

    [Fact]
    public async Task GetObjectsToFile_ReturnsFile()
    {
        var result = await _client.CallToolAsync("GetObjectsToFile", new JsonObject
        {
            ["objectIds"] = "00000001-0001-0001-0001-000000000001"
        });

        result.AssertOk();
        Assert.NotNull(result.ResultFile);
    }

    [Fact]
    public async Task CreateObject_WithInvalidType_ReturnsError()
    {
        var result = await _client.CallToolAsync("CreateObject", new JsonObject
        {
            ["parentId"] = "00000001-0001-0001-0001-000000000001",
            ["typeName"] = "NonExistentType_12345",
            ["attributeName"] = "name",
            ["attributeValue"] = "test"
        });

        result.AssertError();
    }

    [Fact]
    public async Task GetObject_Brief_ReturnsSummary()
    {
        var result = await _client.CallToolAsync("GetObject", new JsonObject
        {
            ["objectId"] = "00000001-0001-0001-0001-000000000001",
            ["brief"] = true
        });

        Assert.False(result.IsError, $"Error: {result.RawText}");
        Assert.Contains("ID:", result.RawText);
        // Brief mode should not contain "Атрибуты:"
        Assert.DoesNotContain("## Атрибуты:", result.RawText);
    }

    [Fact]
    public async Task GetAttribute_OnRoot_ReturnsValue()
    {
        var result = await _client.CallToolAsync("GetAttribute", new JsonObject
        {
            ["objectId"] = "00000001-0001-0001-0001-000000000001",
            ["attributeName"] = "name"
        });

        Assert.False(result.IsError, $"Error: {result.RawText}");
    }

    [Fact]
    public async Task GetAttribute_NonExistent_ReturnsError()
    {
        var result = await _client.CallToolAsync("GetAttribute", new JsonObject
        {
            ["objectId"] = "00000001-0001-0001-0001-000000000001",
            ["attributeName"] = "nonexistent_attribute_xyz"
        });

        result.AssertError();
    }

    [Fact]
    public async Task MoveObject_WithInvalidId_ReturnsError()
    {
        var result = await _client.CallToolAsync("MoveObject", new JsonObject
        {
            ["objectId"] = "not-a-guid",
            ["newParentId"] = "00000001-0001-0001-0001-000000000001"
        });

        result.AssertError();
    }

    [Fact]
    public async Task GetFileSize_WithInvalidId_ReturnsResult()
    {
        var result = await _client.CallToolAsync("GetFileSize", new JsonObject
        {
            ["fileBodyId"] = Guid.NewGuid().ToString()
        });

        // May return 0 for non-existent file or error
        Assert.False(result.IsError && result.RawText.Contains("disposed"),
            $"Connection error: {result.RawText}");
    }
}
