using System.Text.Json.Nodes;

namespace PilotMCP.Tests;

[Collection("Pilot")]
public class DocumentToolsTests
{
    private readonly McpTestClient _client;
    public DocumentToolsTests(PilotFixture fixture) => _client = fixture.Client;

    [Fact]
    public async Task GetDocumentText_WithInvalidId_ReturnsError()
    {
        var fakeId = Guid.NewGuid().ToString();

        var result = await _client.CallToolAsync("GetDocumentText", new JsonObject
        {
            ["documentId"] = fakeId
        });

        result.AssertError();
    }

    [Fact]
    public async Task ExtractPageText_WithInvalidId_ReturnsError()
    {
        var fakeId = Guid.NewGuid().ToString();

        var result = await _client.CallToolAsync("ExtractPageText", new JsonObject
        {
            ["documentId"] = fakeId,
            ["page"] = 1
        });

        result.AssertError();
    }

    [Fact]
    public async Task GetDocumentPageImage_WithInvalidId_ReturnsError()
    {
        var fakeId = Guid.NewGuid().ToString();

        var result = await _client.CallToolAsync("GetDocumentPageImage", new JsonObject
        {
            ["documentId"] = fakeId,
            ["page"] = 1
        });

        // Tool returns error text but not necessarily isError=true
        Assert.Contains("не найден", result.RawText, StringComparison.OrdinalIgnoreCase);
    }
}
