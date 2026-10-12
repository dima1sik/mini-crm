using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MiniCRM.Data;
using MiniCRM.Models;

namespace MiniCRM.Tests;

internal sealed class CrmFactory : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mini-crm-tests", Guid.NewGuid().ToString("N"));

    public string DataPath => Path.Combine(directory, "crm-data.json");

    public CrmStore Store => Services.GetRequiredService<CrmStore>();

    public CrmData Snapshot => Store.Read();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Crm:DataPath", DataPath);
        builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new FixedTimeProvider()));
    }

    public HttpClient CreateSession() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    public async Task<string> Token(HttpClient client)
    {
        using var response = await client.GetAsync("/dashboard");
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "<input[^>]*name=[\"']__RequestVerificationToken[\"'][^>]*value=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            throw new InvalidOperationException("The rendered page did not contain an antiforgery token.");
        }
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public async Task<HttpResponseMessage> Post(HttpClient client, string path, IDictionary<string, string>? values = null, string? token = null)
    {
        var fields = values is null ? new Dictionary<string, string>() : new Dictionary<string, string>(values);
        fields["__RequestVerificationToken"] = token ?? await Token(client);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    public async Task SelectUser(HttpClient client, int id)
    {
        using var response = await Post(client, "/set-user", new Dictionary<string, string> { ["activeUserId"] = id.ToString() });
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Cannot select user {id}: {response.StatusCode}.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    }
}
