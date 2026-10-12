using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using MiniCRM.Services;
using static MiniCRM.Web.CrmEndpoints;
using MiniCRM.Models;
using MiniCRM.Reporting;

namespace MiniCRM.Web;

public static class CrmPages
{
    public static string H(object? value) => System.Net.WebUtility.HtmlEncode(value?.ToString() ?? "");
    public static string Money(decimal value) => ReportService.Money(value);
    public static string Badge(string text, string tone = "blue") => $"<span class='badge {tone}'>{H(text)}</span>";
    public static string Tone(string value) => value switch
    {
        "Won" or "Done" or "Active" => "green",
        "Lost" or "Blocked" or "Overdue" => "red",
        "Negotiation" or "Contacted" or "High" or "In progress" => "orange",
        "Quoted" or "Medium" => "purple",
        _ => "blue"
    };

    public static string Layout(HttpContext ctx, CrmData data, string screen, string body, AntiforgeryTokenSet token)
    {
        var user = ActiveUser(ctx, data);
        var role = data.Roles.First(r => r.Id == user.RoleId);
        var nav = CrmRules.Modules.Where(m => role.Modules.Contains(m.Id)).ToList();
        var navHtml = string.Join("", nav.Select((m, i) => $"<a class='nav {(screen == m.Id ? "active" : "")}' href='/{m.Id}'><span>{(i + 1):00}</span>{H(m.Label)}</a>"));
        var users = string.Join("", data.Users.Select(u => $"<option value='{u.Id}' {(u.Id == user.Id ? "selected" : "")}>{H(u.FullName)}{(u.Status == "Blocked" ? " (blocked)" : "")}</option>"));
        var html = $"""
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
      <label>Demo user</label>
      <select name="activeUserId" onchange="this.form.submit()">{users}</select>
      <small>Demo identity switch; no sign-in.</small>
    </form>
    <nav>{navHtml}</nav>
    {(CrmAccess.CanUse(data, user, "admin") ? $"""
    <form class="sidebar-footer" method="post" action="/reset" onsubmit="return confirm('Reset demo data?')">
      <strong>Stored locally</strong>
      <span>Local JSON demo; UTC dates.</span>
      <button class="ghost full" type="submit">Reset demo</button>
    </form>
    """ : "")}
  </aside>
  <main>
    
    {body}
  </main>
</div>
</body>
</html>
""";
        return Regex.Replace(html, @"<form\b[^>]*>", match =>
            Regex.IsMatch(match.Value, @"method\s*=\s*['""]?post", RegexOptions.IgnoreCase)
                ? match.Value + $"<input type='hidden' name='{H(token.FormFieldName)}' value='{H(token.RequestToken)}'>"
                : match.Value, RegexOptions.IgnoreCase);
    }

    public static string Title(string eyebrow, string title, string subtitle, string action = "") => $"""
<div class="section-title"><div><div class="eyebrow">{H(eyebrow)}</div><h2>{H(title)}</h2><p>{H(subtitle)}</p></div>{action}</div>
""";

    public static string RenderBlocked(AppUser user) => $"""
<section class="page">
{Title("Access denied", "Account is blocked", $"{user.FullName} cannot use CRM modules until an administrator unblocks this account.")}
</section>
""";

    public static string RenderDashboard(CrmData data, string today)
    {
        var openDeals = data.Deals.Where(d => d.Stage is not "Won" and not "Lost").ToList();
        var activeDeals = openDeals.Count;
        var pipeline = openDeals.Sum(d => d.Value);
        var won = data.Deals.Where(d => d.Stage == "Won").Sum(d => d.Value);
        var overdue = data.Tasks.Count(t => t.Status != "Done" && string.CompareOrdinal(t.DueDate, today) < 0);
        var goalPercent = CrmRules.SalesGoal == 0 ? 0 : Math.Round(won / CrmRules.SalesGoal * 100);
        var risky = data.Deals.Where(d => d.Stage is not "Won" and not "Lost" && DateOnly.ParseExact(today, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).DayNumber - DateOnly.ParseExact(d.LastActivityDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).DayNumber >= 7).ToList();
        var riskyHtml = risky.Count == 0
            ? "<div class='empty'>No risky deals.</div>"
            : string.Join("", risky.Select(d => $"<a class='alert-card' href='/deals?dealId={d.Id}'><div><strong>{H(d.Title)}</strong><span>{H(d.Owner)} | Last: {H(d.LastActivityDate)}</span></div>{Badge("Risk", "red")}</a>"));

        return $"""
<section class="page">
{Title("Overview", "Dashboard with KPI", "Shows active deals, pipeline value, overdue tasks and sales goal realization.")}
<div class="kpi-grid">
  <div class="kpi-card"><span>Active deals</span><strong>{activeDeals}</strong><p>Not Won or Lost.</p></div>
  <div class="kpi-card"><span>Pipeline value</span><strong>{Money(pipeline)}</strong><p>Open opportunities.</p></div>
  <div class="kpi-card"><span>Overdue tasks</span><strong>{overdue}</strong><p>Past due follow-ups.</p></div>
  <div class="kpi-card"><span>Sales goal</span><strong>{goalPercent.ToString(System.Globalization.CultureInfo.InvariantCulture)}%</strong><p>{Money(won)} of {Money(CrmRules.SalesGoal)}</p></div>
</div>
<div class="grid two">
  <div class="panel"><h3>Risky deals alert</h3><p class="muted">No activity for 7+ days.</p><div class="stack">{riskyHtml}</div></div>
  <div class="panel"><h3>Workflow overview</h3><div class="check-list">
    <div>Clients and contact notes</div>
    <div>Client 360 links deals and tasks</div>
    <div>Deal stages and activity history</div>
    <div>Follow-ups from deal cards</div>
    <div>Server-checked demo role permissions</div>
    <div>Local JSON persistence</div>
  </div></div>
</div>
</section>
""";
    }

    public static string RenderClients(HttpContext ctx, CrmData data, string today)
    {
        var q = ctx.Request.Query["q"].ToString();
        var selectedId = int.TryParse(ctx.Request.Query["clientId"], out var id) ? id : data.Clients.FirstOrDefault()?.Id ?? 0;
        var clients = data.Clients.Where(c => string.IsNullOrWhiteSpace(q) || c.CompanyName.Contains(q, StringComparison.OrdinalIgnoreCase) || c.ContactName.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Email.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        var selected = data.Clients.FirstOrDefault(c => c.Id == selectedId) ?? data.Clients.FirstOrDefault();
        var ownerOptions = string.Join("", CrmRules.OwnerNames.Select(o => $"<option>{H(o)}</option>"));
        var rows = string.Join("", clients.Select(c => $"<tr class='{(selected?.Id == c.Id ? "selected" : "")}' onclick=\"location.href='/clients?clientId={c.Id}'\"><td>{H(c.CompanyName)}</td><td>{H(c.ContactName)}</td><td>{H(c.Email)}</td><td>{H(c.Owner)}</td></tr>"));

        var detail = selected == null ? "" : RenderClientDetail(data, selected, today);
        var modal = ctx.Request.Query["add"].ToString() == "client" ? $"""
<div class="panel"><h3>Add client</h3><form class="form" method="post" action="/clients/add">
<input required name="companyName" placeholder="Company name *"><input name="contactName" placeholder="Contact person"><input name="phone" placeholder="Phone"><input required type="email" name="email" placeholder="Email *"><select name="owner">{ownerOptions}</select><button class="primary" type="submit">Save client</button>
</form></div>
""" : "";

        return $"""
<section class="page">
{Title("Clients", "Clients and Client 360", "One form, validation, notes, linked deals and follow-ups.", "<a class='primary' href='/clients?add=client'>+ Add client</a>")}
{modal}
<form class="toolbar" method="get" action="/clients"><input name="q" value="{H(q)}" placeholder="Search company, contact or email"><button class="ghost" type="submit">Search</button><a class="ghost" href="/clients">Clear</a></form>
<div class="grid main-detail"><div class="panel"><h3>Client list</h3><table><thead><tr><th>Company</th><th>Contact</th><th>Email</th><th>Owner</th></tr></thead><tbody>{rows}</tbody></table></div>{detail}</div>
</section>
""";
    }

    public static string RenderClientDetail(CrmData data, Client client, string today)
    {
        var deals = string.Join("", data.Deals.Where(d => d.ClientId == client.Id).Select(d => $"<a class='link-card' href='/deals?dealId={d.Id}'><span>{H(d.Title)}</span>{Badge(d.Stage, Tone(d.Stage))}</a>"));
        var tasks = string.Join("", data.Tasks.Where(t => t.ClientId == client.Id).Select(t => $"<div class='link-card'><span>{H(t.Title)} | {H(t.DueDate)}</span>{Badge(CrmRules.TaskStatus(t, today), Tone(CrmRules.TaskStatus(t, today)))}</div>"));
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

    public static string RenderDeals(HttpContext ctx, CrmData data, string today)
    {
        var selectedId = int.TryParse(ctx.Request.Query["dealId"], out var id) ? id : data.Deals.FirstOrDefault()?.Id ?? 0;
        var selected = data.Deals.FirstOrDefault(d => d.Id == selectedId) ?? data.Deals.FirstOrDefault();
        var add = ctx.Request.Query["add"].ToString() == "deal";
        var clientIdQuery = int.TryParse(ctx.Request.Query["clientId"], out var cid) ? cid : data.Clients.FirstOrDefault()?.Id ?? 0;
        var clientOptions = string.Join("", data.Clients.Select(c => $"<option value='{c.Id}' {(c.Id == clientIdQuery ? "selected" : "")}>{H(c.CompanyName)}</option>"));
        var ownerOptions = string.Join("", CrmRules.OwnerNames.Select(o => $"<option>{H(o)}</option>"));
        var stageOptions = string.Join("", CrmRules.DealStages.Select(s => $"<option>{H(s)}</option>"));
        var addDeal = add ? $"""
<div class="panel"><h3>Create deal</h3><form class="form" method="post" action="/deals/add"><select name="clientId">{clientOptions}</select><input required name="title" placeholder="Deal title *"><select name="stage">{stageOptions}</select><input required type="number" min="0" step="0.01" name="value" placeholder="Value"><input type="date" name="deadline" value="{today}"><select name="owner">{ownerOptions}</select><button class="primary" type="submit">Save deal</button></form></div>
""" : "";

        var cols = string.Join("", CrmRules.DealStages.Select(stage =>
        {
            var deals = data.Deals.Where(d => d.Stage == stage).ToList();
            var cards = string.Join("", deals.Select(d =>
            {
                var client = data.Clients.FirstOrDefault(c => c.Id == d.ClientId);
                return $"<a class='deal-card {(selected?.Id == d.Id ? "active" : "")}' href='/deals?dealId={d.Id}'><strong>{H(d.Title)}</strong><span>{H(client?.CompanyName)}</span><b>{Money(d.Value)}</b><small>{H(d.Owner)} | {H(d.Deadline)}</small></a>";
            }));
            return $"<div class='kanban-col'><div class='kanban-head'><h4>{H(stage)}</h4>{Badge(deals.Count.ToString(), Tone(stage))}</div>{cards}</div>";
        }));

        var detail = selected == null ? "" : RenderDealDetail(ctx, data, selected);
        return $"""
<section class="page">
{Title("Deals", "Deals Pipeline", "Create deals, change stages, assign owner, store history, add follow-up.", "<a class='primary' href='/deals?add=deal'>+ New deal</a>")}
{addDeal}
<div class="pipeline-layout"><div class="kanban">{cols}</div>{detail}</div>
</section>
""";
    }

    public static string RenderDealDetail(HttpContext ctx, CrmData data, Deal deal)
    {
        var user = ActiveUser(ctx, data);
        var role = data.Roles.First(r => r.Id == user.RoleId);
        var client = data.Clients.FirstOrDefault(c => c.Id == deal.ClientId);
        var stageOptions = string.Join("", CrmRules.DealStages.Select(s => $"<option {(s == deal.Stage ? "selected" : "")}>{H(s)}</option>"));
        var ownerOptions = string.Join("", CrmRules.OwnerNames.Select(o => $"<option {(o == deal.Owner ? "selected" : "")}>{H(o)}</option>"));
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

    public static string RenderTasks(HttpContext ctx, CrmData data, string today)
    {
        var selectedDealId = int.TryParse(ctx.Request.Query["dealId"], out var selected) ? selected : data.Deals.FirstOrDefault()?.Id ?? 0;
        var dealOptions = string.Join("", data.Deals.Select(d => $"<option value='{d.Id}' {(d.Id == selectedDealId ? "selected" : "")}>{H(d.Title)}</option>"));
        var ownerOptions = string.Join("", CrmRules.OwnerNames.Select(o => $"<option>{H(o)}</option>"));
        var priorityOptions = string.Join("", CrmRules.Priorities.Select(p => $"<option {(p == "Medium" ? "selected" : "")}>{H(p)}</option>"));
        var add = ctx.Request.Query["add"] == "task" ? $"""
<div class="panel"><h3>Add follow-up</h3><form class="form" method="post" action="/tasks/add">
<select required name="dealId">{dealOptions}</select><input required maxlength="200" name="title" placeholder="Task title">
<input required type="date" name="dueDate" value="{today}"><select name="priority">{priorityOptions}</select>
<select name="owner">{ownerOptions}</select><button class="primary" type="submit">Save follow-up</button>
</form></div>
""" : "";
        var rows = string.Join("", data.Tasks.Select(t =>
        {
            var deal = data.Deals.FirstOrDefault(d => d.Id == t.DealId);
            var client = data.Clients.FirstOrDefault(c => c.Id == t.ClientId);
            var statusOptions = string.Join("", CrmRules.TaskStatuses.Select(s => $"<option {(s == (t.Status == "Overdue" ? "Open" : t.Status) ? "selected" : "")}>{H(s)}</option>"));
            var status = CrmRules.TaskStatus(t, today);
            return $"<tr><td>{H(t.Title)}</td><td>{H(client?.CompanyName)}</td><td>{H(deal?.Title)}</td><td>{H(t.DueDate)}</td><td>{Badge(t.Priority, Tone(t.Priority))}</td><td>{H(t.Owner)}</td><td>{Badge(status, Tone(status))}<form method='post' action='/tasks/status'><input type='hidden' name='taskId' value='{t.Id}'><select name='status' onchange='this.form.submit()'>{statusOptions}</select></form></td></tr>";
        }));
        return $"""
<section class="page">
{Title("Follow-ups", "Tasks and Follow-ups", "Every follow-up has title, due date, priority and responsible person.", "<a class='primary' href='/tasks?add=task'>+ Add follow-up</a>")}
{add}
<div class="panel"><table><thead><tr><th>Task</th><th>Client</th><th>Deal</th><th>Due</th><th>Priority</th><th>Owner</th><th>Status</th></tr></thead><tbody>{rows}</tbody></table></div>
</section>
""";
    }

    public static string RenderReports(HttpContext ctx, CrmData data, string today)
    {
        var filter = ReportService.Filters(ctx, today);
        var (from, to, owner) = filter;
        var deals = ReportService.Deals(data, filter).ToList();
        var tasks = data.Tasks.Where(t => string.CompareOrdinal(t.DueDate, from) >= 0 && string.CompareOrdinal(t.DueDate, to) <= 0 && (owner == "All" || t.Owner == owner)).ToList();
        var current = deals.Where(d => d.Stage == "Won").Sum(d => d.Value);
        var open = deals.Where(d => d.Stage is not "Won" and not "Lost").Sum(d => d.Value);
        var goalPct = CrmRules.SalesGoal == 0 ? 0 : Math.Round(current / CrmRules.SalesGoal * 100);
        var maxStage = Math.Max(1, CrmRules.DealStages.Max(s => deals.Count(d => d.Stage == s)));
        var bars = string.Join("", CrmRules.DealStages.Select(s =>
        {
            var count = deals.Count(d => d.Stage == s);
            var value = deals.Where(d => d.Stage == s).Sum(d => d.Value);
            var pct = Math.Max(4, count * 100 / maxStage);
            return $"<div class='mini-bar-row'><div class='mini-bar-label'><span>{H(s)} | {Money(value)}</span><b>{count}</b></div><div class='mini-bar-track'><div class='mini-bar-fill {Tone(s)}' style='width:{pct}%'></div></div></div>";
        }));
        var ownerRows = string.Join("", CrmRules.OwnerNames.Select(o => $"<tr><td>{H(o)}</td><td>{tasks.Count(t => t.Owner == o)}</td><td>{data.Clients.Count(c => c.Owner == o && string.CompareOrdinal(c.Created, from) >= 0 && string.CompareOrdinal(c.Created, to) <= 0)}</td><td>{deals.Count(d => d.Owner == o && d.Stage == "Won")}</td></tr>"));
        var dealRows = string.Join("", deals.Select(d => $"<tr onclick=\"location.href='/deals?dealId={d.Id}'\"><td>{H(d.Title)}</td><td>{H(d.Owner)}</td><td>{Badge(d.Stage, Tone(d.Stage))}</td><td>{Money(d.Value)}</td></tr>"));
        var ownerOptions = "<option>All</option>" + string.Join("", CrmRules.OwnerNames.Select(o => $"<option {(o == owner ? "selected" : "")}>{H(o)}</option>"));
        var saved = string.Join("", data.SavedViews.Select(v => $"<a class='view-chip' href='/reports?{H(ReportService.Query(v.DateFrom, v.DateTo, v.Owner))}'>{H(v.Name)}</a>"));
        var query = H(ReportService.Query(from, to, owner));

        return $"""
<section class="page">
{Title("Reports", "Reports", "Filters, saved views, pipeline forecast, salesperson comparison, PDF and Excel XML export.", $"<div class='button-row'><a class='secondary' href='/reports/export/pdf?{query}'>Export PDF</a> <a class='secondary' href='/reports/export/xls?{query}'>Export Excel XML</a></div>")}
<form class="toolbar reports-toolbar" method="get" action="/reports"><label>From<input type="date" name="from" value="{H(from)}"></label><label>To<input type="date" name="to" value="{H(to)}"></label><label>Salesperson<select name="owner">{ownerOptions}</select></label><button class="primary" type="submit">Apply filter</button></form>
<form class="toolbar" method="post" action="/reports/save-view"><input type="hidden" name="dateFrom" value="{H(from)}"><input type="hidden" name="dateTo" value="{H(to)}"><input type="hidden" name="owner" value="{H(owner)}"><input name="name" placeholder="Saved view name"><button class="ghost" type="submit">Save view</button></form>
<div class="saved-views">{saved}</div>
<div class="kpi-grid"><div class="kpi-card"><span>Current result</span><strong>{Money(current)}</strong><p>Selected period.</p></div><div class="kpi-card"><span>Goal realization</span><strong>{goalPct.ToString(System.Globalization.CultureInfo.InvariantCulture)}%</strong><p>Target {Money(CrmRules.SalesGoal)}</p></div><div class="kpi-card"><span>Pipeline forecast</span><strong>{Money(open)}</strong><p>Open deals.</p></div><div class="kpi-card"><span>Previous period</span><strong>{Money(ReportService.PreviousRevenue(data, filter))}</strong><p>Same-length preceding period; deals grouped by creation date.</p></div></div>
<div class="report-grid"><div class="panel report"><h3>Pipeline by stage</h3>{bars}</div><div class="panel report"><h3>Salesperson comparison</h3><table><thead><tr><th>User</th><th>Tasks</th><th>Contacts</th><th>Won deals</th></tr></thead><tbody>{ownerRows}</tbody></table></div><div class="panel report"><h3>Filtered deals</h3><table><thead><tr><th>Deal</th><th>Owner</th><th>Stage</th><th>Value</th></tr></thead><tbody>{dealRows}</tbody></table></div><div class="panel report"><h3>Report calculation</h3><p class="muted">Generated {today} UTC. Range: {H(from)} - {H(to)}. Owner: {H(owner)}.</p><div class="check-list"><div>Deals are filtered by creation date and current stage.</div><div>Tasks use due date; contacts use creation date.</div><div>Exports include all filtered deals and the UTC generation date.</div></div></div></div>
</section>
""";
    }

    public static string RenderAdmin(HttpContext ctx, CrmData data)
    {
        var userFilter = ctx.Request.Query["user"].ToString(); if (string.IsNullOrWhiteSpace(userFilter)) userFilter = "All";
        var dateFilter = ctx.Request.Query["date"].ToString();
        var roleOptions = string.Join("", data.Roles.Select(r => $"<option value='{H(r.Id)}'>{H(r.Name)}</option>"));
        var roleCards = string.Join("", data.Roles.Select(role =>
        {
            var boxes = string.Join("", CrmRules.Modules.Select(m => $"<form method='post' action='/admin/roles/toggle' onsubmit=\"return confirm('Change module permissions?')\"><input type='hidden' name='roleId' value='{H(role.Id)}'><input type='hidden' name='moduleId' value='{H(m.Id)}'><label><input type='checkbox' {(role.Modules.Contains(m.Id) ? "checked" : "")} onchange='this.form.submit()' {(role.Id == "admin" && m.Id == "admin" ? "disabled" : "")}> {H(m.Label)}</label></form>"));
            return $"<div class='permission-card'><strong>{H(role.Name)}</strong><div class='permissions'>{boxes}</div></div>";
        }));
        var userRows = string.Join("", data.Users.Select(u =>
        {
            var options = string.Join("", data.Roles.Select(r => $"<option value='{H(r.Id)}' {(r.Id == u.RoleId ? "selected" : "")}>{H(r.Name)}</option>"));
            return $"<tr><td>{H(u.FullName)}</td><td>{H(u.Email)}</td><td><form method='post' action='/admin/users/role' onsubmit=\"return confirm('Change user role?')\"><input type='hidden' name='userId' value='{u.Id}'><select name='roleId' onchange='this.form.submit()'>{options}</select></form></td><td>{Badge(u.Status, Tone(u.Status))}</td><td><form method='post' action='/admin/users/toggle' onsubmit=\"return confirm('Change account status?')\"><input type='hidden' name='userId' value='{u.Id}'><button class='{(u.Status == "Active" ? "danger" : "secondary")}' type='submit'>{(u.Status == "Active" ? "Block" : "Unblock")}</button></form></td></tr>";
        }));
        var userFilterOptions = "<option>All</option>" + string.Join("", data.Users.Select(u => $"<option {(u.FullName == userFilter ? "selected" : "")}>{H(u.FullName)}</option>"));
        var logs = data.Audit.Where(a => (userFilter == "All" || a.Admin == userFilter || a.Target == userFilter) && (string.IsNullOrWhiteSpace(dateFilter) || a.Date.StartsWith(dateFilter))).ToList();
        var logRows = string.Join("", logs.Select(l => $"<tr><td>{H(l.Date)}</td><td>{H(l.Admin)}</td><td>{H(l.Action)}</td><td>{H(l.Target)}</td><td>{H(l.Details)}</td></tr>"));
        return $"""
<section class="page">
{Title("Administration", "Admin / Settings", "Users, roles, module access, block/unblock, confirmations and audit log.")}
<div class="grid two"><div class="panel"><h3>Add user</h3><form class="form compact" method="post" action="/admin/users/add"><input required name="firstName" placeholder="First name"><input required name="lastName" placeholder="Last name"><input required type="email" name="email" placeholder="Email"><select name="roleId">{roleOptions}</select><button class="primary" type="submit">Save active account</button></form></div><div class="panel"><h3>Role module permissions</h3><div class="role-permissions">{roleCards}</div></div></div>
<div class="panel"><h3>User accounts</h3><table><thead><tr><th>User</th><th>Email</th><th>Role</th><th>Status</th><th>Action</th></tr></thead><tbody>{userRows}</tbody></table></div>
<div class="panel"><div class="card-head"><h3>Administrative audit log</h3><form class="audit-filters" method="get" action="/admin"><select name="user">{userFilterOptions}</select><input type="date" name="date" value="{H(dateFilter)}"><button class="ghost" type="submit">Filter</button></form></div><table><thead><tr><th>Date</th><th>Admin</th><th>Action</th><th>Target</th><th>Details</th></tr></thead><tbody>{logRows}</tbody></table></div>
</section>
""";
    }

}
