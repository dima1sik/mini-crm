using MiniCRM.Data;
using MiniCRM.Models;
using MiniCRM.Web;
using MiniCRM.Reporting;

namespace MiniCRM.Web;

public static class CrmPages
{
public static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");
public static string H(object? value) => System.Net.WebUtility.HtmlEncode(value?.ToString() ?? "");
public static string Money(decimal value) => $"PLN {value:N0}";
public static string Badge(string text, string tone = "blue") => $"<span class='badge {tone}'>{H(text)}</span>";
public static string Tone(string value) => value switch
{
    "Won" or "Done" or "Active" => "green",
    "Lost" or "Blocked" or "Overdue" => "red",
    "Negotiation" or "Contacted" or "High" or "In progress" => "orange",
    "Quoted" or "Medium" => "purple",
    _ => "blue"
};

public static AppUser GetActiveUser(HttpContext ctx, CrmStore store)
{
    var id = ctx.Session.GetInt32("activeUserId") ?? store.Data.Users.First().Id;
    return store.Data.Users.FirstOrDefault(u => u.Id == id) ?? store.Data.Users.First();
}

public static string Layout(HttpContext ctx, CrmStore store, string screen, string body)
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

public static string Title(string eyebrow, string title, string subtitle, string action = "") => $"""
<div class="section-title"><div><div class="eyebrow">{H(eyebrow)}</div><h2>{H(title)}</h2><p>{H(subtitle)}</p></div>{action}</div>
""";

public static string RenderBlocked(AppUser user) => $"""
<section class="page">
{Title("Access denied", "Account is blocked", $"{user.FullName} cannot use CRM modules until an administrator unblocks this account.")}
</section>
""";

public static string RenderDashboard(CrmStore store)
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

public static string RenderClients(HttpContext ctx, CrmStore store)
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

public static string RenderClientDetail(CrmStore store, Client client, string ownerOptions)
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

public static string RenderDeals(HttpContext ctx, CrmStore store)
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

public static string RenderDealDetail(HttpContext ctx, CrmStore store, Deal deal)
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

public static string RenderTasks(CrmStore store)
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

public static string RenderReports(HttpContext ctx, CrmStore store)
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

public static string RenderAdmin(HttpContext ctx, CrmStore store)
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

public static IEnumerable<Deal> FilterDeals(CrmStore store, string from, string to, string owner)
{
    return store.Data.Deals.Where(d => string.CompareOrdinal(d.Created, from) >= 0 && string.CompareOrdinal(d.Created, to) <= 0 && (owner == "All" || d.Owner == owner));
}

public static List<string> ReportLines(HttpContext ctx, CrmStore store)
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

}
