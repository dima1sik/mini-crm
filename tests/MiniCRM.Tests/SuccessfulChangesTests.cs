using System.Globalization;
using System.Net;
using System.Text.Json;
using MiniCRM.Models;
using Xunit;

namespace MiniCRM.Tests;

public sealed class SuccessfulChangesTests : IDisposable
{
    private readonly CrmFactory factory = new();

    [Theory]
    [InlineData("pl-PL")]
    [InlineData("lt-LT")]
    public async Task Browser_decimal_amount_is_preserved_under_comma_decimal_server_cultures(string cultureName)
    {
        using var client = factory.CreateSession();
        factory.Server.PreserveExecutionContext = true;
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var response = await factory.Post(client, "/deals/add", new Dictionary<string, string>
            {
                ["clientId"] = "10", ["title"] = "Culture-safe sale", ["stage"] = "New", ["value"] = "1234.56",
                ["deadline"] = "2026-10-31", ["owner"] = "Anna Sales"
            });

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var persisted = JsonSerializer.Deserialize<CrmData>(await File.ReadAllTextAsync(factory.DataPath))!;
            Assert.Equal(1234.56m, Assert.Single(persisted.Deals, deal => deal.Title == "Culture-safe sale").Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task Client_deal_and_follow_up_workflow_persists_links_history_and_ownership_audit()
    {
        using var client = factory.CreateSession();
        await Post(client, "/clients/add", new Dictionary<string, string>
        {
            ["companyName"] = "Portfolio client", ["contactName"] = "Example Contact", ["email"] = "portfolio@example.test", ["owner"] = "Anna Sales"
        });
        var customer = Assert.Single(factory.Snapshot.Clients, customer => customer.CompanyName == "Portfolio client");
        await Post(client, "/clients/note", new Dictionary<string, string>
        {
            ["clientId"] = customer.Id.ToString(), ["note"] = "Asked for a quote."
        });
        await Post(client, "/deals/add", new Dictionary<string, string>
        {
            ["clientId"] = customer.Id.ToString(), ["title"] = "Portfolio sale", ["stage"] = "New", ["value"] = "1700.25",
            ["deadline"] = "2026-10-31", ["owner"] = "Anna Sales"
        });
        var sale = Assert.Single(factory.Snapshot.Deals, sale => sale.Title == "Portfolio sale");
        await Post(client, "/tasks/add", new Dictionary<string, string>
        {
            ["dealId"] = sale.Id.ToString(), ["title"] = "Follow up quote", ["dueDate"] = "2026-10-10", ["priority"] = "Medium", ["owner"] = "Anna Sales"
        });
        var followUp = Assert.Single(factory.Snapshot.Tasks, task => task.Title == "Follow up quote");
        await Post(client, "/deals/update", new Dictionary<string, string>
        {
            ["dealId"] = sale.Id.ToString(), ["stage"] = "Won", ["owner"] = "Julia Manager"
        });
        await Post(client, "/tasks/status", new Dictionary<string, string>
        {
            ["taskId"] = followUp.Id.ToString(), ["status"] = "Done"
        });

        var persisted = JsonSerializer.Deserialize<CrmData>(await File.ReadAllTextAsync(factory.DataPath))!;
        var savedClient = persisted.Clients.Single(item => item.Id == customer.Id);
        var savedSale = persisted.Deals.Single(item => item.Id == sale.Id);
        var savedTask = persisted.Tasks.Single(item => item.Id == followUp.Id);
        var actorName = persisted.Users.Single(user => user.Id == 1).FullName;
        Assert.True(customer.Id > 0 && sale.Id > 0 && followUp.Id > 0);
        Assert.Equal(3, new[] { customer.Id, sale.Id, followUp.Id }.Distinct().Count());
        Assert.Equal(customer.Id, savedSale.ClientId);
        Assert.Equal(customer.Id, savedTask.ClientId);
        Assert.Equal(sale.Id, savedTask.DealId);
        Assert.Equal(1700.25m, savedSale.Value);
        Assert.Equal("Won", savedSale.Stage);
        Assert.Equal("Julia Manager", savedSale.Owner);
        Assert.Equal("Done", savedTask.Status);
        Assert.Equal("2026-10-09", savedSale.Created);
        Assert.Equal("2026-10-09", savedSale.LastActivityDate);
        Assert.Equal("Asked for a quote.", Assert.Single(savedClient.Notes).Text);
        Assert.Equal(actorName, savedClient.Notes[0].Author);
        Assert.Equal(3, savedSale.History.Count);
        Assert.Contains(savedSale.History, history => history.Text.Contains("New -> Won", StringComparison.Ordinal));
        Assert.Contains(savedSale.History, history => history.Text.Contains("Anna Sales -> Julia Manager", StringComparison.Ordinal));
        var audit = Assert.Single(persisted.Audit, entry => entry.Action == "Deal owner changed" && entry.Target == "Portfolio sale");
        Assert.Equal(actorName, audit.Admin);
        Assert.Equal("Anna Sales -> Julia Manager", audit.Details);
    }

    [Fact]
    public async Task Admin_can_manage_an_account_and_module_access_and_reset_demo_data()
    {
        using var client = factory.CreateSession();
        var originalUsers = factory.Snapshot.Users.Count;
        await Post(client, "/admin/users/add", new Dictionary<string, string>
        {
            ["firstName"] = "Example", ["lastName"] = "User", ["email"] = "admin-flow@example.test", ["roleId"] = "sales"
        });
        var user = Assert.Single(factory.Snapshot.Users, user => user.Email == "admin-flow@example.test");
        await Post(client, "/admin/users/role", new Dictionary<string, string> { ["userId"] = user.Id.ToString(), ["roleId"] = "manager" });
        Assert.Equal("manager", factory.Snapshot.Users.Single(item => item.Id == user.Id).RoleId);
        await Post(client, "/admin/users/toggle", new Dictionary<string, string> { ["userId"] = user.Id.ToString() });
        Assert.Equal("Blocked", factory.Snapshot.Users.Single(item => item.Id == user.Id).Status);
        await Post(client, "/admin/users/toggle", new Dictionary<string, string> { ["userId"] = user.Id.ToString() });
        Assert.Equal("Active", factory.Snapshot.Users.Single(item => item.Id == user.Id).Status);
        await Post(client, "/admin/roles/toggle", new Dictionary<string, string> { ["roleId"] = "manager", ["moduleId"] = "clients" });
        Assert.DoesNotContain("clients", factory.Snapshot.Roles.Single(role => role.Id == "manager").Modules);
        await Post(client, "/admin/roles/toggle", new Dictionary<string, string> { ["roleId"] = "manager", ["moduleId"] = "clients" });
        Assert.Contains("clients", factory.Snapshot.Roles.Single(role => role.Id == "manager").Modules);
        var audit = factory.Snapshot.Audit;
        Assert.Contains(audit, entry => entry.Action == "User added" && entry.Target == "Example User");
        Assert.Contains(audit, entry => entry.Action == "User role changed" && entry.Target == "Example User");
        Assert.Contains(audit, entry => entry.Action == "User blocked" && entry.Target == "Example User");
        Assert.Contains(audit, entry => entry.Action == "User unblocked" && entry.Target == "Example User");
        Assert.Equal(2, audit.Count(entry => entry.Action == "Role permissions changed"));

        await Post(client, "/reset");

        var persisted = JsonSerializer.Deserialize<CrmData>(await File.ReadAllTextAsync(factory.DataPath))!;
        Assert.Equal(originalUsers, persisted.Users.Count);
        Assert.DoesNotContain(persisted.Users, item => item.Email == "admin-flow@example.test");
        Assert.Single(persisted.Audit);
    }

    private async Task Post(HttpClient client, string path, IDictionary<string, string>? form = null)
    {
        using var response = await factory.Post(client, path, form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public void Dispose() => factory.Dispose();
}
