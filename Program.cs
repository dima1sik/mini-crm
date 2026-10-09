using MiniCRM.Data;
using MiniCRM.Web;

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
app.MapCrmEndpoints();
app.Run();

public partial class Program;
