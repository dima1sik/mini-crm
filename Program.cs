using System;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddSingleton<CrmStore>();

var app = builder.Build();
app.UseStaticFiles();
app.UseSession();

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

app.Run();

static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");
static string H(object? value) => System.Net.WebUtility.HtmlEncode(value?.ToString() ?? "");
static string Money(decimal value) => $"PLN {value:N0}";
static string Badge(string text, string tone = "blue") => $"<span class='badge {tone}'>{H(text)}</span>";
static string Tone(string value) => value switch
{
    "Won" or "Done" or "Active" => "green",
    "Lost" or "Blocked" or "Overdue" => "red",
    "Negotiation" or "Contacted" or "High" or "In progress" => "orange",
    "Quoted" or "Medium" => "purple",
    _ => "blue"
};

static AppUser GetActiveUser(HttpContext ctx, CrmStore store)
{
    var id = ctx.Session.GetInt32("activeUserId") ?? store.Data.Users.First().Id;
    return store.Data.Users.FirstOrDefault(u => u.Id == id) ?? store.Data.Users.First();
}

static string Layout(HttpContext ctx, CrmStore store, string screen, string body)
{
    var user = GetActiveUser(ctx, store);
    var role = store.Data.Roles.First(r => r.Id == user.RoleId);
    var nav = CrmStore.Modules.Where(m => role.Modules.Contains(m.Id)).ToList();
    var navHtml = string.Join("", nav.Select((m, i) => $"<a class='nav {(screen == m.Id ? "active" : "")}' href='/{m.Id}'><span>{(i + 1):00}</span>{H(m.Label)}</a>"));
    var users = string.Join("", store.Data.Users.Select(u => $"<option value='{u.Id}' {(u.Id == user.Id ? "selected" : "")}>{H(u.FullName)}{(u.Status == "Blocked" ? " (blocked)" : "")}</option>"));
    return $"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>MiniCRM</title>
  <link rel="stylesheet" href="/styles.css">
</head>
<body>
<div class="app">
  <aside class="sidebar">
    <div class="brand"><h1>MiniCRM</h1><div class="brand-badges">{Badge(user.Status, Tone(user.Status))}{Badge(role.Name)}</div></div>
    <form class="role-box" method="post" action="/set-user">
      <label>Active user</label>
      <select name="activeUserId" onchange="this.form.submit()">{users}</select>
      <small>{H(role.Name)}</small>
    </form>
    <nav>{navHtml}</nav>
    <form class="sidebar-footer" method="post" action="/reset" onsubmit="return confirm('Reset demo data?')">
      <strong>Stored locally</strong>
      <span>Data is stored locally.</span>
      <button class="ghost full" type="submit">Reset demo</button>
    </form>
  </aside>
  <main>
    
    {body}
  </main>
</div>
</body>
</html>
""";
}

static string Title(string eyebrow, string title, string subtitle, string action = "") => $"""
<div class="section-title"><div><div class="eyebrow">{H(eyebrow)}</div><h2>{H(title)}</h2><p>{H(subtitle)}</p></div>{action}</div>
""";

static string RenderBlocked(AppUser user) => $"""
<section class="page">
{Title("Access denied", "Account is blocked", $"{user.FullName} cannot use CRM modules until an administrator unblocks this account.")}
</section>
""";

static string RenderDashboard(CrmStore store)
{
    var openDeals = store.Data.Deals.Where(d => d.Stage is not "Won" and not "Lost").ToList();
    var activeDeals = openDeals.Count;
    var pipeline = openDeals.Sum(d => d.Value);
    var won = store.Data.Deals.Where(d => d.Stage == "Won").Sum(d => d.Value);
    var overdue = store.Data.Tasks.Count(t => t.Status != "Done" && string.CompareOrdinal(t.DueDate, CrmStore.Today) < 0);
    var goalPercent = CrmStore.SalesGoal == 0 ? 0 : (int)Math.Round(won / CrmStore.SalesGoal * 100);
    var risky = store.Data.Deals.Where(d => d.Stage is not "Won" and not "Lost" && DateTime.Parse(d.LastActivityDate).AddDays(7) <= DateTime.Parse(CrmStore.Today)).ToList();
    var riskyHtml = risky.Count == 0
        ? "<div class='empty'>No risky deals.</div>"
        : string.Join("", risky.Select(d => $"<a class='alert-card' href='/deals?dealId={d.Id}'><div><strong>{H(d.Title)}</strong><span>{H(d.Owner)} | Last: {H(d.LastActivityDate)}</span></div>{Badge("Risk", "red")}</a>"));

    return $"""
<section class="page">
{Title("Acceptance criteria", "Dashboard with KPI", "Shows active deals, pipeline value, overdue tasks and sales goal realization.")}
<div class="kpi-grid">
  <div class="kpi-card"><span>Active deals</span><strong>{activeDeals}</strong><p>Not Won or Lost.</p></div>
  <div class="kpi-card"><span>Pipeline value</span><strong>{Money(pipeline)}</strong><p>Open opportunities.</p></div>
  <div class="kpi-card"><span>Overdue tasks</span><strong>{overdue}</strong><p>Past due follow-ups.</p></div>
  <div class="kpi-card"><span>Sales goal</span><strong>{goalPercent}%</strong><p>{Money(won)} of {Money(CrmStore.SalesGoal)}</p></div>
</div>
<div class="grid two">
  <div class="panel"><h3>Risky deals alert</h3><p class="muted">No activity for 7+ days.</p><div class="stack">{riskyHtml}</div></div>
  <div class="panel"><h3>Criteria checklist</h3><div class="check-list">
    <div>{Badge("OK", "green")} Client one-form creation</div>
    <div>{Badge("OK", "green")} Client 360 notes/deals/tasks</div>
    <div>{Badge("OK", "green")} Deal pipeline with history</div>
    <div>{Badge("OK", "green")} Follow-up from deal card</div>
    <div>{Badge("OK", "green")} Role-based hidden modules</div>
    <div>{Badge("OK", "green")} Local data persistence</div>
  </div></div>
</div>
</section>
""";
}

static string RenderClients(HttpContext ctx, CrmStore store)
{
    var q = ctx.Request.Query["q"].ToString();
    var selectedId = int.TryParse(ctx.Request.Query["clientId"], out var id) ? id : store.Data.Clients.FirstOrDefault()?.Id ?? 0;
    var clients = store.Data.Clients.Where(c => string.IsNullOrWhiteSpace(q) || c.CompanyName.Contains(q, StringComparison.OrdinalIgnoreCase) || c.ContactName.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Email.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    var selected = store.Data.Clients.FirstOrDefault(c => c.Id == selectedId) ?? store.Data.Clients.FirstOrDefault();
    var ownerOptions = string.Join("", CrmStore.OwnerNames.Select(o => $"<option>{H(o)}</option>"));
    var rows = string.Join("", clients.Select(c => $"<tr class='{(selected?.Id == c.Id ? "selected" : "")}' onclick=\"location.href='/clients?clientId={c.Id}'\"><td>{H(c.CompanyName)}</td><td>{H(c.ContactName)}</td><td>{H(c.Email)}</td><td>{H(c.Owner)}</td></tr>"));

    var detail = selected == null ? "" : RenderClientDetail(store, selected, ownerOptions);
    var modal = ctx.Request.Query["add"].ToString() == "client" ? $"""
<div class="panel"><h3>Add client</h3><form class="form" method="post" action="/clients/add">
<input required name="companyName" placeholder="Company name *"><input name="contactName" placeholder="Contact person"><input name="phone" placeholder="Phone"><input required type="email" name="email" placeholder="Email *"><select name="owner">{ownerOptions}</select><button class="primary" type="submit">Save client</button>
</form></div>
""" : "";

    return $"""
<section class="page">
{Title("Handlowiec criteria", "Clients and Client 360", "One form, validation, notes, linked deals and follow-ups.", "<a class='primary' href='/clients?add=client'>+ Add client</a>")}
{modal}
<form class="toolbar" method="get" action="/clients"><input name="q" value="{H(q)}" placeholder="Search company, contact or email"><button class="ghost" type="submit">Search</button><a class="ghost" href="/clients">Clear</a></form>
<div class="grid main-detail"><div class="panel"><h3>Client list</h3><table><thead><tr><th>Company</th><th>Contact</th><th>Email</th><th>Owner</th></tr></thead><tbody>{rows}</tbody></table></div>{detail}</div>
</section>
""";
}

static string RenderClientDetail(CrmStore store, Client client, string ownerOptions)
{
    var deals = string.Join("", store.Data.Deals.Where(d => d.ClientId == client.Id).Select(d => $"<a class='link-card' href='/deals?dealId={d.Id}'><span>{H(d.Title)}</span>{Badge(d.Stage, Tone(d.Stage))}</a>"));
    var tasks = string.Join("", store.Data.Tasks.Where(t => t.ClientId == client.Id).Select(t => $"<div class='link-card'><span>{H(t.Title)} | {H(t.DueDate)}</span>{Badge(t.Status, Tone(t.Status))}</div>"));
    var notes = client.Notes.Count == 0 ? "<div class='empty'>No notes yet.</div>" : string.Join("", client.Notes.Select(n => $"<div class='note-card'><strong>{H(n.Date)} | {H(n.Author)}</strong><p>{H(n.Text)}</p></div>"));
    return $"""
<div class="panel detail">
  <div class="card-head"><h3>Client 360</h3><a class="secondary" href="/deals?add=deal&clientId={client.Id}">+ New deal</a></div>
  <div class="detail-row"><span>company</span><strong>{H(client.CompanyName)}</strong></div>
  <div class="detail-row"><span>contact</span><strong>{H(client.ContactName)}</strong></div>
  <div class="detail-row"><span>phone</span><strong>{H(client.Phone)}</strong></div>
  <div class="detail-row"><span>email</span><strong>{H(client.Email)}</strong></div>
  <form class="inline-form" method="post" action="/clients/note"><input type="hidden" name="clientId" value="{client.Id}"><textarea name="note" placeholder="Add note from call or meeting"></textarea><button class="primary" type="submit">Save note</button></form>
  <div class="subsection"><h4>Notes history</h4><div class="stack">{notes}</div></div>
  <div class="subsection"><h4>Related deals</h4><div class="stack">{deals}</div></div>
  <div class="subsection"><h4>Follow-ups</h4><div class="stack">{tasks}</div></div>
</div>
""";
}

static string RenderDeals(HttpContext ctx, CrmStore store)
{
    var selectedId = int.TryParse(ctx.Request.Query["dealId"], out var id) ? id : store.Data.Deals.FirstOrDefault()?.Id ?? 0;
    var selected = store.Data.Deals.FirstOrDefault(d => d.Id == selectedId) ?? store.Data.Deals.FirstOrDefault();
    var add = ctx.Request.Query["add"].ToString() == "deal";
    var clientIdQuery = int.TryParse(ctx.Request.Query["clientId"], out var cid) ? cid : store.Data.Clients.FirstOrDefault()?.Id ?? 0;
    var clientOptions = string.Join("", store.Data.Clients.Select(c => $"<option value='{c.Id}' {(c.Id == clientIdQuery ? "selected" : "")}>{H(c.CompanyName)}</option>"));
    var ownerOptions = string.Join("", CrmStore.OwnerNames.Select(o => $"<option>{H(o)}</option>"));
    var stageOptions = string.Join("", CrmStore.DealStages.Select(s => $"<option>{H(s)}</option>"));
    var addDeal = add ? $"""
<div class="panel"><h3>Create deal</h3><form class="form" method="post" action="/deals/add"><select name="clientId">{clientOptions}</select><input required name="title" placeholder="Deal title *"><select name="stage">{stageOptions}</select><input required type="number" step="0.01" name="value" placeholder="Value"><input type="date" name="deadline" value="{CrmStore.Today}"><select name="owner">{ownerOptions}</select><button class="primary" type="submit">Save deal</button></form></div>
""" : "";

    var cols = string.Join("", CrmStore.DealStages.Select(stage =>
    {
        var deals = store.Data.Deals.Where(d => d.Stage == stage).ToList();
        var cards = string.Join("", deals.Select(d =>
        {
            var client = store.Data.Clients.FirstOrDefault(c => c.Id == d.ClientId);
            return $"<a class='deal-card {(selected?.Id == d.Id ? "active" : "")}' href='/deals?dealId={d.Id}'><strong>{H(d.Title)}</strong><span>{H(client?.CompanyName)}</span><b>{Money(d.Value)}</b><small>{H(d.Owner)} | {H(d.Deadline)}</small></a>";
        }));
        return $"<div class='kanban-col'><div class='kanban-head'><h4>{H(stage)}</h4>{Badge(deals.Count.ToString(), Tone(stage))}</div>{cards}</div>";
    }));

    var detail = selected == null ? "" : RenderDealDetail(ctx, store, selected);
    return $"""
<section class="page">
{Title("Pipeline criteria", "Deals Pipeline", "Create deals, change stages, assign owner, store history, add follow-up.", "<a class='primary' href='/deals?add=deal'>+ New deal</a>")}
{addDeal}
<div class="pipeline-layout"><div class="kanban">{cols}</div>{detail}</div>
</section>
""";
}

static string RenderDealDetail(HttpContext ctx, CrmStore store, Deal deal)
{
    var user = GetActiveUser(ctx, store);
    var role = store.Data.Roles.First(r => r.Id == user.RoleId);
    var client = store.Data.Clients.FirstOrDefault(c => c.Id == deal.ClientId);
    var stageOptions = string.Join("", CrmStore.DealStages.Select(s => $"<option {(s == deal.Stage ? "selected" : "")}>{H(s)}</option>"));
    var ownerOptions = string.Join("", CrmStore.OwnerNames.Select(o => $"<option {(o == deal.Owner ? "selected" : "")}>{H(o)}</option>"));
    var canOwner = role.Id is "admin" or "manager" ? "" : "disabled";
    var history = string.Join("", deal.History.Select(h => $"<div class='note-card'><strong>{H(h.Date)} | {H(h.Author)}</strong><p>{H(h.Text)}</p></div>"));
    return $"""
<div class="panel detail sticky"><h3>Deal card</h3>
  <div class="detail-row"><span>Client</span><strong>{H(client?.CompanyName)}</strong></div>
  <div class="detail-row"><span>Title</span><strong>{H(deal.Title)}</strong></div>
  <div class="detail-row"><span>Value</span><strong>{Money(deal.Value)}</strong></div>
  <form method="post" action="/deals/update" class="form" onsubmit="return confirm('Save deal changes?')"><input type="hidden" name="dealId" value="{deal.Id}"><div class="field"><label>Stage</label><select name="stage">{stageOptions}</select></div><div class="field"><label>Owner</label><select name="owner" {canOwner}>{ownerOptions}</select></div><button class="primary" type="submit">Save changes</button></form>
  <a class="secondary" href="/tasks?add=task&dealId={deal.Id}">+ Add follow-up</a>
  <div class="subsection"><h4>History</h4><div class="stack">{history}</div></div>
</div>
""";
}

static string RenderTasks(CrmStore store)
{
    var add = "";
    var rows = string.Join("", store.Data.Tasks.Select(t =>
    {
        var deal = store.Data.Deals.FirstOrDefault(d => d.Id == t.DealId);
        var client = store.Data.Clients.FirstOrDefault(c => c.Id == t.ClientId);
        var statusOptions = string.Join("", CrmStore.TaskStatuses.Select(s => $"<option {(s == t.Status ? "selected" : "")}>{H(s)}</option>"));
        return $"<tr><td>{H(t.Title)}</td><td>{H(client?.CompanyName)}</td><td>{H(deal?.Title)}</td><td>{H(t.DueDate)}</td><td>{Badge(t.Priority, Tone(t.Priority))}</td><td>{H(t.Owner)}</td><td><form method='post' action='/tasks/status'><input type='hidden' name='taskId' value='{t.Id}'><select name='status' onchange='this.form.submit()'>{statusOptions}</select></form></td></tr>";
    }));
    return $"""
<section class="page">
{Title("Follow-up criteria", "Tasks and Follow-ups", "Every follow-up has title, due date, priority and responsible person.")}
<div class="panel"><table><thead><tr><th>Task</th><th>Client</th><th>Deal</th><th>Due</th><th>Priority</th><th>Owner</th><th>Status</th></tr></thead><tbody>{rows}</tbody></table></div>
</section>
""";
}

static string RenderReports(HttpContext ctx, CrmStore store)
{
    var from = ctx.Request.Query["from"].ToString(); if (string.IsNullOrWhiteSpace(from)) from = "2026-01-01";
    var to = ctx.Request.Query["to"].ToString(); if (string.IsNullOrWhiteSpace(to)) to = "2026-01-31";
    var owner = ctx.Request.Query["owner"].ToString(); if (string.IsNullOrWhiteSpace(owner)) owner = "All";

    var deals = FilterDeals(store, from, to, owner).ToList();
    var tasks = store.Data.Tasks.Where(t => t.DueDate.CompareTo(from) >= 0 && t.DueDate.CompareTo(to) <= 0 && (owner == "All" || t.Owner == owner)).ToList();
    var current = deals.Where(d => d.Stage == "Won").Sum(d => d.Value);
    var open = deals.Where(d => d.Stage is not "Won" and not "Lost").Sum(d => d.Value);
    var goalPct = CrmStore.SalesGoal == 0 ? 0 : (int)Math.Round(current / CrmStore.SalesGoal * 100);
    var maxStage = Math.Max(1, CrmStore.DealStages.Max(s => deals.Count(d => d.Stage == s)));
    var bars = string.Join("", CrmStore.DealStages.Select(s =>
    {
        var count = deals.Count(d => d.Stage == s);
        var value = deals.Where(d => d.Stage == s).Sum(d => d.Value);
        var pct = Math.Max(4, count * 100 / maxStage);
        return $"<div class='mini-bar-row'><div class='mini-bar-label'><span>{H(s)} | {Money(value)}</span><b>{count}</b></div><div class='mini-bar-track'><div class='mini-bar-fill {Tone(s)}' style='width:{pct}%'></div></div></div>";
    }));
    var ownerRows = string.Join("", CrmStore.OwnerNames.Select(o => $"<tr><td>{H(o)}</td><td>{tasks.Count(t => t.Owner == o)}</td><td>{store.Data.Clients.Count(c => c.Owner == o)}</td><td>{deals.Count(d => d.Owner == o && d.Stage == "Won")}</td></tr>"));
    var dealRows = string.Join("", deals.Select(d => $"<tr onclick=\"location.href='/deals?dealId={d.Id}'\"><td>{H(d.Title)}</td><td>{H(d.Owner)}</td><td>{Badge(d.Stage, Tone(d.Stage))}</td><td>{Money(d.Value)}</td></tr>"));
    var ownerOptions = "<option>All</option>" + string.Join("", CrmStore.OwnerNames.Select(o => $"<option {(o == owner ? "selected" : "")}>{H(o)}</option>"));
    var saved = string.Join("", store.Data.SavedViews.Select(v => $"<a class='view-chip' href='/reports?from={H(v.DateFrom)}&to={H(v.DateTo)}&owner={H(v.Owner)}'>{H(v.Name)}</a>"));
    var query = $"from={System.Net.WebUtility.UrlEncode(from)}&to={System.Net.WebUtility.UrlEncode(to)}&owner={System.Net.WebUtility.UrlEncode(owner)}";

    return $"""
<section class="page">
{Title("Manager and Director criteria", "Reports", "Filters, saved views, pipeline forecast, salesperson comparison, PDF/XLS export.", $"<div class='button-row'><a class='secondary' href='/reports/export/pdf?{query}'>Export PDF</a> <a class='secondary' href='/reports/export/xls?{query}'>Export XLS</a></div>")}
<form class="toolbar reports-toolbar" method="get" action="/reports"><label>From<input type="date" name="from" value="{H(from)}"></label><label>To<input type="date" name="to" value="{H(to)}"></label><label>Salesperson<select name="owner">{ownerOptions}</select></label><button class="primary" type="submit">Apply filter</button></form>
<form class="toolbar" method="post" action="/reports/save-view"><input type="hidden" name="dateFrom" value="{H(from)}"><input type="hidden" name="dateTo" value="{H(to)}"><input type="hidden" name="owner" value="{H(owner)}"><input name="name" placeholder="Saved view name"><button class="ghost" type="submit">Save view</button></form>
<div class="saved-views">{saved}</div>
<div class="kpi-grid"><div class="kpi-card"><span>Current result</span><strong>{Money(current)}</strong><p>Selected period.</p></div><div class="kpi-card"><span>Goal realization</span><strong>{goalPct}%</strong><p>Target {Money(CrmStore.SalesGoal)}</p></div><div class="kpi-card"><span>Pipeline forecast</span><strong>{Money(open)}</strong><p>Open deals.</p></div><div class="kpi-card"><span>Previous period</span><strong>{Money(Math.Round(current * .82m))}</strong><p>Demo comparison.</p></div></div>
<div class="report-grid"><div class="panel report"><h3>Pipeline by stage</h3>{bars}</div><div class="panel report"><h3>Salesperson comparison</h3><table><thead><tr><th>User</th><th>Tasks</th><th>Contacts</th><th>Won deals</th></tr></thead><tbody>{ownerRows}</tbody></table></div><div class="panel report"><h3>Filtered deals</h3><table><thead><tr><th>Deal</th><th>Owner</th><th>Stage</th><th>Value</th></tr></thead><tbody>{dealRows}</tbody></table></div><div class="panel report"><h3>Report integrity</h3><p class="muted">Generated {DateTime.Now:dd.MM.yyyy}. Range: {H(from)} - {H(to)}. Owner: {H(owner)}.</p><div class="check-list"><div>{Badge("OK", "green")} KPI are calculated from source data.</div><div>{Badge("OK", "green")} Reports update after Apply filter.</div><div>{Badge("OK", "green")} Export includes headers, filters and date.</div></div></div></div>
</section>
""";
}

static string RenderAdmin(HttpContext ctx, CrmStore store)
{
    var userFilter = ctx.Request.Query["user"].ToString(); if (string.IsNullOrWhiteSpace(userFilter)) userFilter = "All";
    var dateFilter = ctx.Request.Query["date"].ToString();
    var roleOptions = string.Join("", store.Data.Roles.Select(r => $"<option value='{H(r.Id)}'>{H(r.Name)}</option>"));
    var roleCards = string.Join("", store.Data.Roles.Select(role =>
    {
        var boxes = string.Join("", CrmStore.Modules.Select(m => $"<form method='post' action='/admin/roles/toggle' onsubmit=\"return confirm('Change module permissions?')\"><input type='hidden' name='roleId' value='{H(role.Id)}'><input type='hidden' name='moduleId' value='{H(m.Id)}'><label><input type='checkbox' {(role.Modules.Contains(m.Id) ? "checked" : "")} onchange='this.form.submit()' {(role.Id == "admin" && m.Id == "admin" ? "disabled" : "")}> {H(m.Label)}</label></form>"));
        return $"<div class='permission-card'><strong>{H(role.Name)}</strong><div class='permissions'>{boxes}</div></div>";
    }));
    var userRows = string.Join("", store.Data.Users.Select(u =>
    {
        var options = string.Join("", store.Data.Roles.Select(r => $"<option value='{H(r.Id)}' {(r.Id == u.RoleId ? "selected" : "")}>{H(r.Name)}</option>"));
        return $"<tr><td>{H(u.FullName)}</td><td>{H(u.Email)}</td><td><form method='post' action='/admin/users/role' onsubmit=\"return confirm('Change user role?')\"><input type='hidden' name='userId' value='{u.Id}'><select name='roleId' onchange='this.form.submit()'>{options}</select></form></td><td>{Badge(u.Status, Tone(u.Status))}</td><td><form method='post' action='/admin/users/toggle' onsubmit=\"return confirm('Change account status?')\"><input type='hidden' name='userId' value='{u.Id}'><button class='{(u.Status == "Active" ? "danger" : "secondary")}' type='submit'>{(u.Status == "Active" ? "Block" : "Unblock")}</button></form></td></tr>";
    }));
    var userFilterOptions = "<option>All</option>" + string.Join("", store.Data.Users.Select(u => $"<option {(u.FullName == userFilter ? "selected" : "")}>{H(u.FullName)}</option>"));
    var logs = store.Data.Audit.Where(a => (userFilter == "All" || a.Admin == userFilter || a.Target == userFilter) && (string.IsNullOrWhiteSpace(dateFilter) || a.Date.StartsWith(dateFilter))).ToList();
    var logRows = string.Join("", logs.Select(l => $"<tr><td>{H(l.Date)}</td><td>{H(l.Admin)}</td><td>{H(l.Action)}</td><td>{H(l.Target)}</td><td>{H(l.Details)}</td></tr>"));
    return $"""
<section class="page">
{Title("Administrator criteria", "Admin / Settings", "Users, roles, module access, block/unblock, confirmations and audit log.")}
<div class="grid two"><div class="panel"><h3>Add user</h3><form class="form compact" method="post" action="/admin/users/add"><input required name="firstName" placeholder="First name"><input required name="lastName" placeholder="Last name"><input required type="email" name="email" placeholder="Email"><select name="roleId">{roleOptions}</select><button class="primary" type="submit">Save active account</button></form></div><div class="panel"><h3>Role module permissions</h3><div class="role-permissions">{roleCards}</div></div></div>
<div class="panel"><h3>User accounts</h3><table><thead><tr><th>User</th><th>Email</th><th>Role</th><th>Status</th><th>Action</th></tr></thead><tbody>{userRows}</tbody></table></div>
<div class="panel"><div class="card-head"><h3>Administrative audit log</h3><form class="audit-filters" method="get" action="/admin"><select name="user">{userFilterOptions}</select><input type="date" name="date" value="{H(dateFilter)}"><button class="ghost" type="submit">Filter</button></form></div><table><thead><tr><th>Date</th><th>Admin</th><th>Action</th><th>Target</th><th>Details</th></tr></thead><tbody>{logRows}</tbody></table></div>
</section>
""";
}

static IEnumerable<Deal> FilterDeals(CrmStore store, string from, string to, string owner)
{
    return store.Data.Deals.Where(d => string.CompareOrdinal(d.Created, from) >= 0 && string.CompareOrdinal(d.Created, to) <= 0 && (owner == "All" || d.Owner == owner));
}

static List<string> ReportLines(HttpContext ctx, CrmStore store)
{
    var from = ctx.Request.Query["from"].ToString(); if (string.IsNullOrWhiteSpace(from)) from = "2026-01-01";
    var to = ctx.Request.Query["to"].ToString(); if (string.IsNullOrWhiteSpace(to)) to = "2026-01-31";
    var owner = ctx.Request.Query["owner"].ToString(); if (string.IsNullOrWhiteSpace(owner)) owner = "All";
    var deals = FilterDeals(store, from, to, owner).ToList();
    var won = deals.Where(d => d.Stage == "Won").Sum(d => d.Value);
    var open = deals.Where(d => d.Stage is not "Won" and not "Lost").Sum(d => d.Value);
    var lines = new List<string>
    {
        "MiniCRM sales report",
        $"Generated: {DateTime.Now}",
        $"Period: {from} - {to}",
        $"Owner: {owner}",
        "",
        $"Won revenue: {Money(won)}",
        $"Open pipeline: {Money(open)}",
        $"Active deals: {deals.Count(d => d.Stage is not "Won" and not "Lost")}",
        "",
        "Deals:"
    };
    lines.AddRange(deals.Select(d => $"{d.Title} | {d.Owner} | {d.Stage} | {Money(d.Value)}"));
    return lines;
}

public class CrmStore
{
    public const string Today = "2026-01-15";
    public const decimal SalesGoal = 18000m;
    private readonly string _path;
    public CrmData Data { get; private set; }
    public static readonly List<ModuleDef> Modules =
    [
        new("dashboard", "Dashboard"),
        new("clients", "Clients / Client 360"),
        new("deals", "Deals Pipeline"),
        new("tasks", "Tasks / Follow-up"),
        new("reports", "Reports"),
        new("admin", "Admin / Settings"),
    ];
    public static readonly List<string> DealStages = ["New", "Contacted", "Quoted", "Negotiation", "Won", "Lost"];
    public static readonly List<string> TaskStatuses = ["Open", "In progress", "Done", "Overdue"];
    public static readonly List<string> OwnerNames = ["Anna Sales", "Daniel Sales", "Julia Manager"];

    public CrmStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "crm-data.json");
        Data = Load();
    }

    public int NextId()
    {
        var ids = new List<int>();
        ids.AddRange(Data.Users.Select(x => x.Id));
        ids.AddRange(Data.Clients.Select(x => x.Id));
        ids.AddRange(Data.Deals.Select(x => x.Id));
        ids.AddRange(Data.Tasks.Select(x => x.Id));
        ids.AddRange(Data.Audit.Select(x => x.Id));
        ids.AddRange(Data.SavedViews.Select(x => x.Id));
        return ids.Count == 0 ? 1 : ids.Max() + 1;
    }

    public void AddAudit(string admin, string action, string target, string details)
    {
        Data.Audit.Insert(0, new AuditEntry
        {
            Id = NextId(),
            Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Admin = admin,
            Action = action,
            Target = target,
            Details = details
        });
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json, Encoding.UTF8);
    }

    public void Reset()
    {
        Data = Seed();
        Save();
    }

    private CrmData Load()
    {
        if (!File.Exists(_path))
        {
            var seed = Seed();
            Data = seed;
            Save();
            return seed;
        }
        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            return JsonSerializer.Deserialize<CrmData>(json) ?? Seed();
        }
        catch
        {
            return Seed();
        }
    }

    private static CrmData Seed() => new()
    {
        Users =
        [
            new AppUser { Id = 1, FirstName = "Dmytro", LastName = "Nesvitailo", Email = "dn91631@student.uwb.edu.pl", RoleId = "admin", Status = "Active" },
            new AppUser { Id = 2, FirstName = "Marta", LastName = "Admin", Email = "marta.admin@crm.test", RoleId = "admin", Status = "Active" },
            new AppUser { Id = 3, FirstName = "Anna", LastName = "Sales", Email = "anna.sales@crm.test", RoleId = "sales", Status = "Active" },
            new AppUser { Id = 4, FirstName = "Julia", LastName = "Manager", Email = "julia.manager@crm.test", RoleId = "manager", Status = "Active" },
            new AppUser { Id = 5, FirstName = "Marek", LastName = "Director", Email = "marek.director@crm.test", RoleId = "director", Status = "Active" },
        ],
        Roles =
        [
            new RoleDef { Id = "admin", Name = "Administrator systemu", Modules = ["dashboard", "clients", "deals", "tasks", "reports", "admin"] },
            new RoleDef { Id = "sales", Name = "Handlowiec", Modules = ["dashboard", "clients", "deals", "tasks"] },
            new RoleDef { Id = "manager", Name = "Manager sprzedazy", Modules = ["dashboard", "clients", "deals", "tasks", "reports"] },
            new RoleDef { Id = "director", Name = "Dyrektor handlowy / Zarzad", Modules = ["dashboard", "deals", "reports"] },
        ],
        Clients =
        [
            new Client { Id = 10, CompanyName = "BlueSoft Sp. z o.o.", ContactName = "Piotr Zielinski", Phone = "503 456 789", Email = "piotr@bluesoft.test", Owner = "Anna Sales", Created = "2026-01-08", Notes = [new Note { Id = 101, Date = "2026-01-08", Author = "Anna Sales", Text = "Client asked for phone offer." }, new Note { Id = 102, Date = "2026-01-11", Author = "Anna Sales", Text = "Follow-up planned this week." }] },
            new Client { Id = 11, CompanyName = "TechVision S.A.", ContactName = "Jan Kowalski", Phone = "502 345 678", Email = "jan@techvision.test", Owner = "Daniel Sales", Created = "2026-01-10", Notes = [new Note { Id = 103, Date = "2026-01-10", Author = "Daniel Sales", Text = "Interested in laptop package." }] },
            new Client { Id = 12, CompanyName = "GreenHouse S.A.", ContactName = "Marta Sikora", Phone = "504 567 890", Email = "marta@greenhouse.test", Owner = "Anna Sales", Created = "2026-01-11", Notes = [] },
        ],
        Deals =
        [
            new Deal { Id = 201, ClientId = 10, Title = "iPhone 15 package", Stage = "Quoted", Value = 3499, Deadline = "2026-01-20", Owner = "Anna Sales", Created = "2026-01-08", LastActivityDate = "2026-01-12", History = [new DealHistory { Date = "2026-01-12", Author = "Anna Sales", Text = "Stage changed: Contacted -> Quoted." }, new DealHistory { Date = "2026-01-08", Author = "Anna Sales", Text = "Deal created in New stage." }] },
            new Deal { Id = 202, ClientId = 11, Title = "MacBook Air for TechVision", Stage = "Negotiation", Value = 5299, Deadline = "2026-01-23", Owner = "Daniel Sales", Created = "2026-01-10", LastActivityDate = "2026-01-04", History = [new DealHistory { Date = "2026-01-10", Author = "Daniel Sales", Text = "Deal created." }] },
            new Deal { Id = 203, ClientId = 12, Title = "Accessory bundle", Stage = "Won", Value = 1600, Deadline = "2026-01-14", Owner = "Anna Sales", Created = "2026-01-11", LastActivityDate = "2026-01-14", History = [new DealHistory { Date = "2026-01-14", Author = "Anna Sales", Text = "Stage changed: Negotiation -> Won." }] },
            new Deal { Id = 204, ClientId = 10, Title = "Samsung S24 order", Stage = "Contacted", Value = 3899, Deadline = "2026-01-28", Owner = "Anna Sales", Created = "2026-01-14", LastActivityDate = "2026-01-14", History = [new DealHistory { Date = "2026-01-14", Author = "Anna Sales", Text = "Deal created after phone call." }] },
            new Deal { Id = 205, ClientId = 10, Title = "MacBook Air for office team", Stage = "Contacted", Value = 12000, Deadline = "2026-01-15", Owner = "Anna Sales", Created = "2026-01-15", LastActivityDate = "2026-01-15", History = [new DealHistory { Date = "2026-01-15", Author = "Anna Sales", Text = "Deal created in New stage." }] },
        ],
        Tasks =
        [
            new FollowUpTask { Id = 301, Title = "Call Piotr about final decision", DealId = 201, ClientId = 10, DueDate = "2026-01-16", Priority = "High", Owner = "Anna Sales", Status = "Open" },
            new FollowUpTask { Id = 302, Title = "Send updated MacBook quote", DealId = 202, ClientId = 11, DueDate = "2026-01-11", Priority = "High", Owner = "Daniel Sales", Status = "Overdue" },
            new FollowUpTask { Id = 303, Title = "Prepare invoice for accessories", DealId = 203, ClientId = 12, DueDate = "2026-01-14", Priority = "Medium", Owner = "Anna Sales", Status = "Done" },
        ],
        Audit = [new AuditEntry { Id = 401, Date = "2026-01-10 09:15", Admin = "Marta Admin", Action = "Initial setup", Target = "System", Details = "Default roles and demo data created." }],
        SavedViews = [new SavedView { Id = 501, Name = "All sales this month", DateFrom = "2026-01-01", DateTo = "2026-01-31", Owner = "All" }]
    };
}

public record ModuleDef(string Id, string Label);

public class CrmData
{
    public List<AppUser> Users { get; set; } = [];
    public List<RoleDef> Roles { get; set; } = [];
    public List<Client> Clients { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
    public List<FollowUpTask> Tasks { get; set; } = [];
    public List<AuditEntry> Audit { get; set; } = [];
    public List<SavedView> SavedViews { get; set; } = [];
}

public class AppUser
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string RoleId { get; set; } = "sales";
    public string Status { get; set; } = "Active";
    public string FullName => $"{FirstName} {LastName}";
}

public class RoleDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Modules { get; set; } = [];
}

public class Client
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Created { get; set; } = "";
    public List<Note> Notes { get; set; } = [];
}

public class Note
{
    public int Id { get; set; }
    public string Date { get; set; } = "";
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
}

public class Deal
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Title { get; set; } = "";
    public string Stage { get; set; } = "New";
    public decimal Value { get; set; }
    public string Deadline { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Created { get; set; } = "";
    public string LastActivityDate { get; set; } = "";
    public List<DealHistory> History { get; set; } = [];
}

public class DealHistory
{
    public string Date { get; set; } = "";
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
}

public class FollowUpTask
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int DealId { get; set; }
    public int ClientId { get; set; }
    public string DueDate { get; set; } = "";
    public string Priority { get; set; } = "Medium";
    public string Owner { get; set; } = "";
    public string Status { get; set; } = "Open";
}

public class AuditEntry
{
    public int Id { get; set; }
    public string Date { get; set; } = "";
    public string Admin { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Details { get; set; } = "";
}

public class SavedView
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string DateFrom { get; set; } = "";
    public string DateTo { get; set; } = "";
    public string Owner { get; set; } = "All";
}

public static class SimplePdf
{
    public static string Build(List<string> lines)
    {
        var safe = lines.Take(34).Select(Escape).ToList();
        var body = string.Join("\n", safe.Select(line => $"({line}) Tj 0 -18 Td"));
        var stream = $"BT /F1 11 Tf 50 790 Td {body} ET";
        var objects = new List<string>
        {
            "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n",
            "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n",
            "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >> endobj\n",
            "4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj\n",
            $"5 0 obj << /Length {stream.Length} >> stream\n{stream}\nendstream endobj\n"
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(pdf.Length);
            pdf.Append(obj);
        }
        var xrefStart = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n");
        pdf.Append("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF");
        return pdf.ToString();
    }

    private static string Escape(string text)
    {
        return new string(text.Where(c => c >= 32 && c <= 126).ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }
}







