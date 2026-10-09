using System.Text;
using MiniCRM.Data;
using MiniCRM.Models;
using MiniCRM.Web;
using MiniCRM.Reporting;
using static MiniCRM.Web.CrmPages;

namespace MiniCRM.Web;

public static class CrmEndpoints
{
    public static void MapCrmEndpoints(this WebApplication app)
    {
app.MapGet("/", () => Results.Redirect("/dashboard"));

app.MapGet("/{screen:regex(^dashboard|clients|deals|tasks|reports|admin$)}", (string screen, HttpContext ctx, CrmStore store) =>
{
    var user = GetActiveUser(ctx, store);
    var role = store.Data.Roles.First(r => r.Id == user.RoleId);

    if (user.Status == "Blocked")
    {
        return Html(Layout(ctx, store, screen, RenderBlocked(user)));
    }

    if (!role.Modules.Contains(screen))
    {
        return Results.Redirect($"/{role.Modules.FirstOrDefault() ?? "dashboard"}");
    }

    var content = screen switch
    {
        "dashboard" => RenderDashboard(store),
        "clients" => RenderClients(ctx, store),
        "deals" => RenderDeals(ctx, store),
        "tasks" => RenderTasks(store),
        "reports" => RenderReports(ctx, store),
        "admin" => RenderAdmin(ctx, store),
        _ => RenderDashboard(store)
    };

    return Html(Layout(ctx, store, screen, content));
});

app.MapPost("/set-user", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    if (int.TryParse(form["activeUserId"], out var id) && store.Data.Users.Any(u => u.Id == id))
    {
        ctx.Session.SetInt32("activeUserId", id);
    }
    return Results.Redirect(ctx.Request.Headers.Referer.ToString().Length > 0 ? ctx.Request.Headers.Referer.ToString() : "/dashboard");
});

app.MapPost("/reset", (CrmStore store) =>
{
    store.Reset();
    return Results.Redirect("/dashboard");
});

app.MapPost("/clients/add", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var company = form["companyName"].ToString().Trim();
    var email = form["email"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(email))
    {
        return Results.Redirect("/clients");
    }

    var client = new Client
    {
        Id = store.NextId(),
        CompanyName = company,
        ContactName = form["contactName"].ToString().Trim(),
        Phone = form["phone"].ToString().Trim(),
        Email = email,
        Owner = form["owner"].ToString(),
        Created = CrmStore.Today,
        Notes = []
    };
    store.Data.Clients.Insert(0, client);
    store.Save();
    return Results.Redirect($"/clients?clientId={client.Id}");
});

app.MapPost("/clients/note", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var user = GetActiveUser(ctx, store);
    if (!int.TryParse(form["clientId"], out var clientId)) return Results.Redirect("/clients");
    var client = store.Data.Clients.FirstOrDefault(c => c.Id == clientId);
    if (client == null) return Results.Redirect("/clients");
    var text = form["note"].ToString().Trim();
    if (!string.IsNullOrWhiteSpace(text))
    {
        client.Notes.Insert(0, new Note { Id = store.NextId(), Date = CrmStore.Today, Author = user.FullName, Text = text });
        store.Save();
    }
    return Results.Redirect($"/clients?clientId={client.Id}");
});

app.MapPost("/deals/add", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var user = GetActiveUser(ctx, store);
    if (!int.TryParse(form["clientId"], out var clientId)) return Results.Redirect("/deals");
    var title = form["title"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(title)) return Results.Redirect("/deals");
    decimal.TryParse(form["value"], out var value);
    var deal = new Deal
    {
        Id = store.NextId(),
        ClientId = clientId,
        Title = title,
        Stage = form["stage"].ToString(),
        Value = value,
        Deadline = form["deadline"].ToString(),
        Owner = form["owner"].ToString(),
        Created = CrmStore.Today,
        LastActivityDate = CrmStore.Today,
        History = [new DealHistory { Date = CrmStore.Today, Author = user.FullName, Text = $"Deal created in {form["stage"]} stage." }]
    };
    store.Data.Deals.Insert(0, deal);
    store.Save();
    return Results.Redirect($"/deals?dealId={deal.Id}");
});

app.MapPost("/deals/update", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var user = GetActiveUser(ctx, store);
    var role = store.Data.Roles.First(r => r.Id == user.RoleId);
    if (!int.TryParse(form["dealId"], out var dealId)) return Results.Redirect("/deals");
    var deal = store.Data.Deals.FirstOrDefault(d => d.Id == dealId);
    if (deal == null) return Results.Redirect("/deals");

    var newStage = form["stage"].ToString();
    var newOwner = form["owner"].ToString();

    if (!string.IsNullOrWhiteSpace(newStage) && newStage != deal.Stage)
    {
        deal.History.Insert(0, new DealHistory { Date = CrmStore.Today, Author = user.FullName, Text = $"Stage changed: {deal.Stage} -> {newStage}." });
        deal.Stage = newStage;
        deal.LastActivityDate = CrmStore.Today;
    }

    if ((role.Id == "admin" || role.Id == "manager") && !string.IsNullOrWhiteSpace(newOwner) && newOwner != deal.Owner)
    {
        var old = deal.Owner;
        deal.Owner = newOwner;
        deal.History.Insert(0, new DealHistory { Date = CrmStore.Today, Author = user.FullName, Text = $"Owner changed: {old} -> {newOwner}." });
        store.AddAudit(user.FullName, "Deal owner changed", deal.Title, $"{old} -> {newOwner}");
    }

    store.Save();
    return Results.Redirect($"/deals?dealId={deal.Id}");
});

app.MapPost("/tasks/add", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    if (!int.TryParse(form["dealId"], out var dealId)) return Results.Redirect("/tasks");
    var deal = store.Data.Deals.FirstOrDefault(d => d.Id == dealId);
    if (deal == null) return Results.Redirect("/tasks");
    var title = form["title"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(title)) return Results.Redirect("/tasks");
    var due = form["dueDate"].ToString();
    var task = new FollowUpTask
    {
        Id = store.NextId(),
        Title = title,
        DealId = deal.Id,
        ClientId = deal.ClientId,
        DueDate = due,
        Priority = form["priority"].ToString(),
        Owner = form["owner"].ToString(),
        Status = string.CompareOrdinal(due, CrmStore.Today) < 0 ? "Overdue" : "Open"
    };
    store.Data.Tasks.Insert(0, task);
    store.Save();
    return Results.Redirect("/tasks");
});

app.MapPost("/tasks/status", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    if (int.TryParse(form["taskId"], out var taskId))
    {
        var task = store.Data.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task != null)
        {
            task.Status = form["status"].ToString();
            store.Save();
        }
    }
    return Results.Redirect("/tasks");
});

app.MapPost("/admin/users/add", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var admin = GetActiveUser(ctx, store);
    var first = form["firstName"].ToString().Trim();
    var last = form["lastName"].ToString().Trim();
    var email = form["email"].ToString().Trim();
    var roleId = form["roleId"].ToString();

    if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last) || !email.Contains('@'))
    {
        return Results.Redirect("/admin");
    }
    if (store.Data.Users.Any(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Redirect("/admin");
    }

    var user = new AppUser { Id = store.NextId(), FirstName = first, LastName = last, Email = email, RoleId = roleId, Status = "Active" };
    store.Data.Users.Insert(0, user);
    store.AddAudit(admin.FullName, "User added", user.FullName, $"Role: {store.Data.Roles.First(r => r.Id == roleId).Name}");
    store.Save();
    return Results.Redirect("/admin");
});

app.MapPost("/admin/users/role", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var admin = GetActiveUser(ctx, store);
    if (!int.TryParse(form["userId"], out var userId)) return Results.Redirect("/admin");
    var user = store.Data.Users.FirstOrDefault(u => u.Id == userId);
    var roleId = form["roleId"].ToString();
    if (user != null && store.Data.Roles.Any(r => r.Id == roleId) && user.RoleId != roleId)
    {
        var old = user.RoleId;
        user.RoleId = roleId;
        store.AddAudit(admin.FullName, "User role changed", user.FullName, $"{old} -> {roleId}");
        store.Save();
    }
    return Results.Redirect("/admin");
});

app.MapPost("/admin/users/toggle", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var admin = GetActiveUser(ctx, store);
    if (!int.TryParse(form["userId"], out var userId)) return Results.Redirect("/admin");
    var user = store.Data.Users.FirstOrDefault(u => u.Id == userId);
    if (user != null)
    {
        var old = user.Status;
        user.Status = user.Status == "Active" ? "Blocked" : "Active";
        store.AddAudit(admin.FullName, user.Status == "Blocked" ? "User blocked" : "User unblocked", user.FullName, $"{old} -> {user.Status}");
        store.Save();
    }
    return Results.Redirect("/admin");
});

app.MapPost("/admin/roles/toggle", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var admin = GetActiveUser(ctx, store);
    var roleId = form["roleId"].ToString();
    var moduleId = form["moduleId"].ToString();
    var role = store.Data.Roles.FirstOrDefault(r => r.Id == roleId);
    if (role != null && CrmStore.Modules.Any(m => m.Id == moduleId))
    {
        if (role.Id == "admin" && moduleId == "admin") return Results.Redirect("/admin");
        if (role.Modules.Contains(moduleId))
        {
            role.Modules.Remove(moduleId);
            store.AddAudit(admin.FullName, "Role permissions changed", role.Name, $"Removed module: {moduleId}");
        }
        else
        {
            role.Modules.Add(moduleId);
            store.AddAudit(admin.FullName, "Role permissions changed", role.Name, $"Added module: {moduleId}");
        }
        store.Save();
    }
    return Results.Redirect("/admin");
});

app.MapPost("/reports/save-view", async (HttpContext ctx, CrmStore store) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var name = form["name"].ToString().Trim();
    if (!string.IsNullOrWhiteSpace(name))
    {
        store.Data.SavedViews.Insert(0, new SavedView
        {
            Id = store.NextId(),
            Name = name,
            DateFrom = form["dateFrom"].ToString(),
            DateTo = form["dateTo"].ToString(),
            Owner = form["owner"].ToString()
        });
        store.Save();
    }
    return Results.Redirect($"/reports?from={System.Net.WebUtility.UrlEncode(form["dateFrom"])}&to={System.Net.WebUtility.UrlEncode(form["dateTo"])}&owner={System.Net.WebUtility.UrlEncode(form["owner"])}");
});

app.MapGet("/reports/export/pdf", (HttpContext ctx, CrmStore store) =>
{
    var lines = ReportLines(ctx, store);
    var pdf = SimplePdf.Build(lines);
    return Results.File(Encoding.ASCII.GetBytes(pdf), "application/pdf", "mini-crm-report.pdf");
});

app.MapGet("/reports/export/xls", (HttpContext ctx, CrmStore store) =>
{
    var rows = ReportLines(ctx, store).Select(line => $"<Row><Cell><Data ss:Type=\"String\">{H(line)}</Data></Cell></Row>");
    var xml = $"""
<?xml version="1.0"?>
<?mso-application progid="Excel.Sheet"?>
<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
<Worksheet ss:Name="Report"><Table>{string.Join("", rows)}</Table></Worksheet></Workbook>
""";
    return Results.File(Encoding.UTF8.GetBytes(xml), "application/vnd.ms-excel", "mini-crm-report.xls");
});

    }
}
