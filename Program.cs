using Microsoft.AspNetCore.Antiforgery;
using MiniCRM.Data;
using MiniCRM.Services;
using MiniCRM.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.IsEssential = true;
});
builder.Services.AddAntiforgery();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CrmStore>();
builder.Services.AddSingleton<CrmService>();

var app = builder.Build();
// Validate persisted data before accepting requests, and fail without replacing it.
_ = app.Services.GetRequiredService<CrmStore>();
app.UseStaticFiles();
app.UseSession();
app.Use(async (context, next) =>
{
    try
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
        if (HttpMethods.IsPost(context.Request.Method))
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        await next(context);
    }
    catch (AntiforgeryValidationException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("The form expired or is missing its verification token. Reload the page and retry.");
    }
    catch (CrmException error)
    {
        context.Response.StatusCode = error.StatusCode;
        await context.Response.WriteAsync(error.Message);
    }
});
app.MapCrmEndpoints();
app.Run();

public partial class Program;
