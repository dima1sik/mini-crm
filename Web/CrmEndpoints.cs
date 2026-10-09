using Microsoft.AspNetCore.Antiforgery;
using MiniCRM.Data;
using MiniCRM.Models;
using MiniCRM.Reporting;
using MiniCRM.Services;
using static MiniCRM.Web.CrmPages;

namespace MiniCRM.Web;

public static class CrmEndpoints
{
    public static void MapCrmEndpoints(this WebApplication app)
    {
        app.MapGet("/", () => Results.Redirect("/dashboard"));
        app.MapGet("/{screen:regex(^(dashboard|clients|deals|tasks|reports|admin)$)}", (string screen, HttpContext ctx, CrmStore store) =>
        {
            var data = store.Read();
            var user = ActiveUser(ctx, data);
            if (!CrmAccess.CanUse(data, user, screen))
                return Page(ctx, data, screen, user.Status == "Blocked" ? RenderBlocked(user)
                    : Title("Access denied", "Module unavailable", "This demo role does not have access to this module."), statusCode: 403);
            var body = screen switch
            {
                "dashboard" => RenderDashboard(data, store.Today),
                "clients" => RenderClients(ctx, data, store.Today),
                "deals" => RenderDeals(ctx, data, store.Today),
                "tasks" => RenderTasks(ctx, data, store.Today),
                "reports" => RenderReports(ctx, data, store.Today),
                "admin" => RenderAdmin(ctx, data),
                _ => ""
            };
            return Page(ctx, data, screen, body);
        });

        app.MapPost("/set-user", async (HttpContext ctx, CrmStore store) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var id = Id(form, "activeUserId");
            if (!store.Read().Users.Any(u => u.Id == id)) throw new CrmException(404, "Demo user not found.");
            ctx.Session.SetInt32("activeUserId", id);
            return Results.Redirect("/dashboard");
        });
        Post(app, "/reset", "admin", (data, user, form, service, store) =>
        {
            var seed = DemoData.Create(store.Today);
            data.Users = seed.Users; data.Roles = seed.Roles; data.Clients = seed.Clients;
            data.Deals = seed.Deals; data.Tasks = seed.Tasks; data.Audit = seed.Audit; data.SavedViews = seed.SavedViews;
            return "/dashboard";
        });
        Post(app, "/clients/add", "clients", (data, user, form, service, _) =>
            $"/clients?clientId={service.AddClient(data, new(form["companyName"].ToString(), form["contactName"].ToString(), form["phone"].ToString(), form["email"].ToString(), form["owner"].ToString()))}");
        Post(app, "/clients/note", "clients", (data, user, form, service, _) =>
            $"/clients?clientId={service.AddNote(data, user, Id(form, "clientId"), form["note"].ToString())}");
        Post(app, "/deals/add", "deals", (data, user, form, service, _) =>
            $"/deals?dealId={service.AddDeal(data, user, new(Id(form, "clientId"), form["title"].ToString(), form["stage"].ToString(), form["value"].ToString(), form["deadline"].ToString(), form["owner"].ToString()))}");
        Post(app, "/deals/update", "deals", (data, user, form, service, _) =>
            $"/deals?dealId={service.UpdateDeal(data, user, Id(form, "dealId"), form["stage"].ToString(), form["owner"].ToString())}");
        Post(app, "/tasks/add", "tasks", (data, user, form, service, _) =>
        {
            service.AddTask(data, new(Id(form, "dealId"), form["title"].ToString(), form["dueDate"].ToString(), form["priority"].ToString(), form["owner"].ToString()));
            return "/tasks";
        });
        Post(app, "/tasks/status", "tasks", (data, user, form, service, _) =>
        {
            service.UpdateTask(data, Id(form, "taskId"), form["status"].ToString()); return "/tasks";
        });
        Post(app, "/admin/users/add", "admin", (data, user, form, service, _) =>
        {
            service.AddUser(data, user, new(form["firstName"].ToString(), form["lastName"].ToString(), form["email"].ToString(), form["roleId"].ToString())); return "/admin";
        });
        Post(app, "/admin/users/role", "admin", (data, user, form, service, _) =>
        {
            service.ChangeUserRole(data, user, Id(form, "userId"), form["roleId"].ToString()); return "/admin";
        });
        Post(app, "/admin/users/toggle", "admin", (data, user, form, service, _) =>
        {
            service.ToggleUser(data, user, Id(form, "userId")); return "/admin";
        });
        Post(app, "/admin/roles/toggle", "admin", (data, user, form, service, _) =>
        {
            service.ToggleModule(data, user, form["roleId"].ToString(), form["moduleId"].ToString()); return "/admin";
        });
        Post(app, "/reports/save-view", "reports", (data, user, form, service, _) =>
        {
            service.SaveView(data, form["name"].ToString(), form["dateFrom"].ToString(), form["dateTo"].ToString(), form["owner"].ToString());
            return "/reports?" + ReportService.Query(form["dateFrom"].ToString(), form["dateTo"].ToString(), form["owner"].ToString());
        });
        app.MapGet("/reports/export/pdf", (HttpContext ctx, CrmStore store) =>
        {
            var data = store.Read(); CrmAccess.Require(data, ActiveUser(ctx, data), "reports");
            return Results.File(ReportExport.Pdf(ReportService.Lines(data, ReportService.Filters(ctx, store.Today), store.Timestamp)), "application/pdf", "mini-crm-report.pdf");
        });
        app.MapGet("/reports/export/xls", (HttpContext ctx, CrmStore store) =>
        {
            var data = store.Read(); CrmAccess.Require(data, ActiveUser(ctx, data), "reports");
            return Results.File(ReportExport.Spreadsheet(ReportService.Lines(data, ReportService.Filters(ctx, store.Today), store.Timestamp)), "application/xml", "mini-crm-report.xml");
        });
    }

    public static AppUser ActiveUser(HttpContext context, CrmData data) => CrmAccess.ActiveUser(data, context.Session.GetInt32("activeUserId"));

    private static int Id(IFormCollection form, string name) => int.TryParse(form[name], out var id) && id > 0 ? id : throw new CrmException(400, $"{name} must be a positive identifier.");

    private static void Post(WebApplication app, string route, string module,
        Func<CrmData, AppUser, IFormCollection, CrmService, CrmStore, string> action) =>
        app.MapPost(route, async (HttpContext ctx, CrmStore store, CrmService service) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var redirect = store.Change(data =>
            {
                var user = ActiveUser(ctx, data);
                CrmAccess.Require(data, user, module);
                return action(data, user, form, service, store);
            });
            return Results.Redirect(redirect);
        });

    private static IResult Page(HttpContext ctx, CrmData data, string screen, string body, int statusCode = 200)
    {
        var token = ctx.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(ctx);
        return Results.Content(Layout(ctx, data, screen, body, token), "text/html; charset=utf-8", statusCode: statusCode);
    }
}
