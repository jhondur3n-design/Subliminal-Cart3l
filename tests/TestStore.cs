using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using SubliminalCart3lBot.Services;

namespace SubliminalCart3lBot.Tests;

internal sealed class TestStore : IAsyncDisposable
{
    private readonly string _directory;

    private TestStore(string directory, StoreDatabase database)
    {
        _directory = directory;
        Database = database;
        Catalog = new ProductCatalog(database);
    }

    public StoreDatabase Database { get; }

    public ProductCatalog Catalog { get; }

    public static async Task<TestStore> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"subliminal-cart-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var database = new StoreDatabase(directory, "test.db");
        await database.InitializeAsync();
        return new TestStore(directory, database);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await Task.Yield();
        Directory.Delete(_directory, recursive: true);
    }
}

internal sealed record TelegramCall(string Method, string Json);

internal sealed class FakeTelegramHttpHandler : HttpMessageHandler
{
    public List<TelegramCall> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var method = request.RequestUri!.AbsolutePath.Split('/').Last();
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        Calls.Add(new TelegramCall(method, body));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { ok = true })
        };
    }
}