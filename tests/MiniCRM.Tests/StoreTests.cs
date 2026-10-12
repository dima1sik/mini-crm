using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using MiniCRM.Data;
using Xunit;

namespace MiniCRM.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mini-crm-store-tests", Guid.NewGuid().ToString("N"));
    private string DataPath => Path.Combine(directory, "crm-data.json");

    [Fact]
    public void Read_returns_a_detached_snapshot()
    {
        var store = CreateStore();
        var original = File.ReadAllText(DataPath);
        var snapshot = store.Read();

        snapshot.Clients[0].Notes.Clear();
        snapshot.Users.Clear();

        Assert.NotEmpty(store.Read().Users);
        Assert.NotEmpty(store.Read().Clients[0].Notes);
        Assert.Equal(original, File.ReadAllText(DataPath));
    }

    [Fact]
    public void Throwing_mutation_does_not_publish_or_persist_its_partial_changes()
    {
        var store = CreateStore();
        var original = File.ReadAllText(DataPath);
        var originalName = store.Read().Clients[0].CompanyName;

        Assert.Throws<InvalidOperationException>(() => store.Change<bool>(data =>
        {
            data.Clients[0].CompanyName = "Partial mutation";
            throw new InvalidOperationException("Simulated validation failure");
        }));

        Assert.Equal(originalName, store.Read().Clients[0].CompanyName);
        Assert.Equal(original, File.ReadAllText(DataPath));
    }

    [Fact]
    public void Failed_persistence_does_not_publish_the_mutation()
    {
        var store = CreateStore();
        var originalName = store.Read().Clients[0].CompanyName;
        File.Delete(DataPath);
        Directory.CreateDirectory(DataPath);

        Assert.ThrowsAny<IOException>(() => store.Change(data =>
        {
            data.Clients[0].CompanyName = "Unpersisted mutation";
            return true;
        }));

        Assert.Equal(originalName, store.Read().Clients[0].CompanyName);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task Concurrent_changes_are_not_lost_and_survive_a_restart()
    {
        var store = CreateStore();
        var originalValue = store.Read().Deals[0].Value;
        const int changes = 100;

        await Task.WhenAll(Enumerable.Range(0, changes).Select(_ => Task.Run(() => store.Change(data => ++data.Deals[0].Value))));

        Assert.Equal(originalValue + changes, store.Read().Deals[0].Value);
        Assert.Equal(originalValue + changes, CreateStore().Read().Deals[0].Value);
    }

    [Theory]
    [InlineData("{ definitely not JSON", typeof(JsonException))]
    [InlineData("null", typeof(InvalidDataException))]
    [InlineData("{}", typeof(InvalidDataException))]
    public void Invalid_persisted_data_is_reported_without_silently_replacing_it(string content, Type exceptionType)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(DataPath, content);

        Assert.Throws(exceptionType, () => CreateStore());

        Assert.Equal(content, File.ReadAllText(DataPath));
    }

    [Fact]
    public void Persisted_titles_that_cannot_be_exported_are_rejected_without_replacing_data()
    {
        var store = CreateStore();
        var snapshot = store.Read();
        snapshot.Deals[0].Title = "Invalid\uFFFEtitle";
        var content = JsonSerializer.Serialize(snapshot);
        File.WriteAllText(DataPath, content);

        Assert.Throws<InvalidDataException>(() => CreateStore());
        Assert.Equal(content, File.ReadAllText(DataPath));
    }

    private CrmStore CreateStore()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Crm:DataPath"] = DataPath
        }).Build();
        return new CrmStore(new TestEnvironment { ContentRootPath = directory }, configuration, TimeProvider.System);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MiniCRM";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
