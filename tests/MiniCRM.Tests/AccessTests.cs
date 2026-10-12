using System.Net;
using Xunit;

namespace MiniCRM.Tests;

public sealed class AccessTests : IDisposable
{
    private readonly CrmFactory factory = new();

    [Theory]
    [InlineData("/admin")]
    [InlineData("/reports")]
    [InlineData("/reports/export/pdf")]
    [InlineData("/reports/export/xls")]
    public async Task Sales_cannot_read_admin_or_reports(string path)
    {
        using var client = factory.CreateSession();
        await factory.SelectUser(client, 3);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/admin/users/add")]
    [InlineData("/admin/users/role")]
    [InlineData("/admin/users/toggle")]
    [InlineData("/admin/roles/toggle")]
    [InlineData("/reports/save-view")]
    [InlineData("/reset")]
    public async Task Sales_cannot_change_admin_or_report_data(string path)
    {
        using var client = factory.CreateSession();
        await factory.SelectUser(client, 3);
        var before = await File.ReadAllTextAsync(factory.DataPath);

        using var response = await factory.Post(client, path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
    }

    [Theory]
    [InlineData("/clients/add")]
    [InlineData("/clients/note")]
    [InlineData("/deals/add")]
    [InlineData("/deals/update")]
    [InlineData("/tasks/add")]
    [InlineData("/tasks/status")]
    [InlineData("/admin/users/add")]
    [InlineData("/admin/users/role")]
    [InlineData("/admin/users/toggle")]
    [InlineData("/admin/roles/toggle")]
    [InlineData("/reports/save-view")]
    [InlineData("/reset")]
    public async Task Blocked_user_cannot_mutate_any_module(string path)
    {
        using var client = factory.CreateSession();
        var token = await factory.Token(client);
        factory.Store.Change(data =>
        {
            data.Users.Single(user => user.Id == 1).Status = "Blocked";
            return true;
        });
        var before = await File.ReadAllTextAsync(factory.DataPath);

        using var response = await factory.Post(client, path, token: token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/reports/export/pdf")]
    [InlineData("/reports/export/xls")]
    public async Task Blocked_user_cannot_read_pages_or_exports(string path)
    {
        using var client = factory.CreateSession();
        await factory.Token(client);
        factory.Store.Change(data =>
        {
            data.Users.Single(user => user.Id == 1).Status = "Blocked";
            return true;
        });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/clients/add")]
    [InlineData("/clients/note")]
    public async Task Revoking_module_access_also_revokes_its_write_routes(string path)
    {
        using var client = factory.CreateSession();
        await factory.SelectUser(client, 3);
        var token = await factory.Token(client);
        factory.Store.Change(data => data.Roles.Single(role => role.Id == "sales").Modules.Remove("clients"));
        var before = await File.ReadAllTextAsync(factory.DataPath);

        using var response = await factory.Post(client, path, token: token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
    }

    [Theory]
    [InlineData("/clients/add")]
    [InlineData("/reset")]
    [InlineData("/set-user")]
    public async Task Post_without_antiforgery_token_cannot_change_state(string path)
    {
        using var client = factory.CreateSession();
        await factory.Token(client);
        var before = await File.ReadAllTextAsync(factory.DataPath);

        using var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["activeUserId"] = "3",
            ["companyName"] = "Injected client",
            ["email"] = "injected@example.test",
            ["owner"] = "Anna Sales"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
        using var adminPage = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, adminPage.StatusCode);
    }

    [Fact]
    public async Task User_switch_rejects_an_external_redirect()
    {
        using var client = factory.CreateSession();
        client.DefaultRequestHeaders.Referrer = new Uri("https://attacker.example/");

        using var response = await factory.Post(client, "/set-user", new Dictionary<string, string> { ["activeUserId"] = "3" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/dashboard", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Role_without_modules_receives_forbidden_instead_of_a_redirect_loop()
    {
        using var client = factory.CreateSession();
        await factory.SelectUser(client, 3);
        factory.Store.Change(data =>
        {
            data.Roles.Single(role => role.Id == "sales").Modules.Clear();
            return true;
        });

        using var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("Module unavailable", await response.Content.ReadAsStringAsync());
    }

    public void Dispose() => factory.Dispose();
}
