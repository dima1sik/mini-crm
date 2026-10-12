using System.Net;
using Xunit;

namespace MiniCRM.Tests;

public sealed class ValidationTests : IDisposable
{
    private readonly CrmFactory factory = new();

    [Theory]
    [InlineData("clientId", "999999", HttpStatusCode.NotFound)]
    [InlineData("clientId", "not-an-id", HttpStatusCode.BadRequest)]
    [InlineData("stage", "Imaginary", HttpStatusCode.BadRequest)]
    [InlineData("value", "-1", HttpStatusCode.BadRequest)]
    [InlineData("value", "not-money", HttpStatusCode.BadRequest)]
    [InlineData("value", "1000000000001", HttpStatusCode.BadRequest)]
    [InlineData("deadline", "2026-02-30", HttpStatusCode.BadRequest)]
    [InlineData("deadline", "09/10/2026", HttpStatusCode.BadRequest)]
    [InlineData("owner", "Unknown owner", HttpStatusCode.BadRequest)]
    [InlineData("title", "Invalid\uFFFEtitle", HttpStatusCode.BadRequest)]
    [InlineData("title", "Invalid\uFFFFtitle", HttpStatusCode.BadRequest)]
    public async Task Invalid_deal_fields_do_not_persist_partial_data(string field, string value, HttpStatusCode expected)
    {
        using var client = factory.CreateSession();
        await factory.Token(client);
        var before = await File.ReadAllTextAsync(factory.DataPath);
        var form = ValidDeal();
        form[field] = value;

        using var response = await factory.Post(client, "/deals/add", form);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
    }

    [Fact]
    public async Task User_with_unknown_role_is_rejected_before_it_is_added()
    {
        using var client = factory.CreateSession();
        await factory.Token(client);
        var before = await File.ReadAllTextAsync(factory.DataPath);

        using var response = await factory.Post(client, "/admin/users/add", new Dictionary<string, string>
        {
            ["firstName"] = "Test", ["lastName"] = "User", ["email"] = "new-user@example.test", ["roleId"] = "unknown"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
        Assert.DoesNotContain(factory.Snapshot.Users, user => user.Email == "new-user@example.test");
    }

    [Fact]
    public async Task Duplicate_user_email_is_case_insensitive()
    {
        using var client = factory.CreateSession();
        await factory.Token(client);
        var before = await File.ReadAllTextAsync(factory.DataPath);
        var existingEmail = factory.Snapshot.Users.Single(user => user.Id == 3).Email.ToUpperInvariant();

        using var response = await factory.Post(client, "/admin/users/add", new Dictionary<string, string>
        {
            ["firstName"] = "Test", ["lastName"] = "User", ["email"] = existingEmail, ["roleId"] = "sales"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
    }

    [Theory]
    [InlineData("/admin/users/toggle", "userId", "1", null, null)]
    [InlineData("/admin/users/role", "userId", "1", "roleId", "sales")]
    public async Task Last_active_admin_cannot_be_blocked_or_demoted(string path, string idField, string id, string? extraField, string? extraValue)
    {
        using var client = factory.CreateSession();
        await factory.Token(client);
        factory.Store.Change(data =>
        {
            data.Users.Single(user => user.Id == 2).Status = "Blocked";
            return true;
        });
        var before = await File.ReadAllTextAsync(factory.DataPath);
        var form = new Dictionary<string, string> { [idField] = id };
        if (extraField is not null) form[extraField] = extraValue!;

        using var response = await factory.Post(client, path, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
    }

    [Fact]
    public async Task Unauthorized_owner_change_does_not_apply_a_stage_change_either()
    {
        using var client = factory.CreateSession();
        await factory.SelectUser(client, 3);
        var before = await File.ReadAllTextAsync(factory.DataPath);

        using var response = await factory.Post(client, "/deals/update", new Dictionary<string, string>
        {
            ["dealId"] = "201", ["stage"] = "Won", ["owner"] = "Julia Manager"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
    }

    [Theory]
    [InlineData("dealId", "999999", HttpStatusCode.NotFound)]
    [InlineData("dueDate", "2026-13-01", HttpStatusCode.BadRequest)]
    [InlineData("priority", "Urgent-ish", HttpStatusCode.BadRequest)]
    public async Task Invalid_follow_up_cannot_be_added(string field, string value, HttpStatusCode expected)
    {
        using var client = factory.CreateSession();
        await factory.Token(client);
        var before = await File.ReadAllTextAsync(factory.DataPath);
        var form = new Dictionary<string, string>
        {
            ["dealId"] = "201", ["title"] = "Call client", ["dueDate"] = "2026-10-10", ["priority"] = "High", ["owner"] = "Anna Sales"
        };
        form[field] = value;

        using var response = await factory.Post(client, "/tasks/add", form);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
    }

    [Fact]
    public async Task Unknown_task_status_cannot_replace_a_valid_status()
    {
        using var client = factory.CreateSession();
        await factory.Token(client);
        var before = await File.ReadAllTextAsync(factory.DataPath);

        using var response = await factory.Post(client, "/tasks/status", new Dictionary<string, string>
        {
            ["taskId"] = "301", ["status"] = "Anything"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
    }

    [Theory]
    [InlineData("/clients/note", "clientId")]
    [InlineData("/deals/update", "dealId")]
    [InlineData("/tasks/status", "taskId")]
    [InlineData("/admin/users/toggle", "userId")]
    [InlineData("/admin/users/role", "userId")]
    public async Task Missing_entity_identifiers_return_not_found_without_changing_data(string path, string field)
    {
        using var client = factory.CreateSession();
        await factory.Token(client);
        var before = await File.ReadAllTextAsync(factory.DataPath);

        using var response = await factory.Post(client, path, new Dictionary<string, string>
        {
            [field] = "999999", ["note"] = "Test note", ["stage"] = "Won", ["status"] = "Done", ["roleId"] = "sales"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.DataPath));
    }

    [Theory]
    [InlineData("/reports?from=2026-10-31&to=2026-10-01")]
    [InlineData("/reports/export/pdf?from=invalid")]
    [InlineData("/reports/export/xls?owner=Unknown")]
    public async Task Invalid_report_filters_are_rejected(string path)
    {
        using var client = factory.CreateSession();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Dictionary<string, string> ValidDeal() => new()
    {
        ["clientId"] = "10", ["title"] = "Test sale", ["stage"] = "New", ["value"] = "1234.56", ["deadline"] = "2026-10-31", ["owner"] = "Anna Sales"
    };

    public void Dispose() => factory.Dispose();
}
