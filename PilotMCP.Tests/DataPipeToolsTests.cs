using System.Text.Json.Nodes;

namespace PilotMCP.Tests;

[Collection("Pilot")]
public class DataPipeToolsTests
{
    private readonly McpTestClient _client;
    public DataPipeToolsTests(PilotFixture fixture) => _client = fixture.Client;

    private async Task<string> GetSearchResultFile()
    {
        var search = await _client.CallToolAsync("Search", new JsonObject
        {
            ["query"] = "*",
            ["maxResults"] = 3
        });
        search.AssertOk();
        Assert.NotNull(search.ResultFile);
        return search.ResultFile!;
    }

    [Fact]
    public async Task ExtractFields_FromSearchResults()
    {
        var resultFile = await GetSearchResultFile();

        var result = await _client.CallToolAsync("ExtractFields", new JsonObject
        {
            ["inputFile"] = resultFile,
            ["fields"] = "id,typeName"
        });

        result.AssertOk();
    }

    [Fact]
    public async Task FilterObjects_ByType()
    {
        var resultFile = await GetSearchResultFile();

        // Preview the file first to find a type name to filter by
        var preview = await _client.CallToolAsync("PreviewFile", new JsonObject
        {
            ["inputFile"] = resultFile,
            ["take"] = 1
        });

        var result = await _client.CallToolAsync("FilterObjects", new JsonObject
        {
            ["inputFile"] = resultFile,
            ["field"] = "typeName",
            ["value"] = ""
        });

        result.AssertOk();
    }

    [Fact]
    public async Task PreviewFile_ShowsContent()
    {
        var resultFile = await GetSearchResultFile();

        var result = await _client.CallToolAsync("PreviewFile", new JsonObject
        {
            ["inputFile"] = resultFile
        });

        Assert.False(result.IsError, $"PreviewFile returned error: {result.RawText}");
        Assert.NotEmpty(result.RawText);
    }

    [Fact]
    public async Task CountFile_ReturnsCount()
    {
        var resultFile = await GetSearchResultFile();

        var result = await _client.CallToolAsync("CountFile", new JsonObject
        {
            ["inputFile"] = resultFile
        });

        result.AssertOk();
        Assert.Matches(@"\d+", result.RawText);
    }

    [Fact]
    public async Task ExportCsv_WritesFile()
    {
        var resultFile = await GetSearchResultFile();
        var csvPath = Path.Combine(Path.GetTempPath(), $"test_export_{Guid.NewGuid():N}.csv");

        try
        {
            var result = await _client.CallToolAsync("ExportCsv", new JsonObject
            {
                ["inputFile"] = resultFile,
                ["outputFile"] = csvPath,
                ["columns"] = "id,typeName"
            });

            result.AssertOk();
        }
        finally
        {
            if (File.Exists(csvPath))
                File.Delete(csvPath);
        }
    }
}
