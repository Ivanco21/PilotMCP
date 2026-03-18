namespace PilotMCP.Tests;

/// <summary>
/// Shared fixture — один процесс PilotMCP на всю коллекцию тестов.
/// Тесты внутри коллекции выполняются последовательно (один MCP-процесс).
/// </summary>
public class PilotFixture : IAsyncLifetime
{
    public McpTestClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Client = await McpTestClient.CreateAsync(15000);
    }

    public async Task DisposeAsync()
    {
        await Client.DisposeAsync();
    }
}

[CollectionDefinition("Pilot")]
public class PilotCollection : ICollectionFixture<PilotFixture> { }
