using System.Security.Claims;
using GozcuStandalone;
using GozcuStandalone.Storage;
using GozcuStandalone.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

// The web version of the attendance app. It shares the desktop app's database and never talks to the
// device itself: the desktop app keeps collecting scans, this shows and edits the same data in a browser.
// As a Windows service the app starts in system32 and runs under another account,
// so files are found next to the exe and the database path is set in appsettings.json.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});
builder.Host.UseWindowsService(o => o.ServiceName = "AttendanceWeb");

var dbPath = builder.Configuration["DatabasePath"] is { Length: > 0 } p ? Environment.ExpandEnvironmentVariables(p) : AppPaths.Database;
builder.Services.AddSingleton(new AttendanceStore(dbPath));
builder.Services.AddSingleton(_ => AppSettings.Load(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "settings.json")));

// Sign-in cookies stay valid across restarts: keys are kept next to the database.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "web-keys")))
    .SetApplicationName("AttendanceWeb");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Login";
        o.AccessDeniedPath = "/Denied";
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
        o.Cookie.Name = "attendance.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(WebApp.CanEdit, x => x.RequireRole(AttendanceStore.RoleAdmin, AttendanceStore.RoleEditor));
    o.AddPolicy(WebApp.IsAdmin, x => x.RequireRole(AttendanceStore.RoleAdmin));
});
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AllowAnonymousToPage("/Login");
    o.Conventions.AllowAnonymousToPage("/Setup");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();

// Until the first account exists, every page leads to the setup page.
app.Use(async (ctx, next) =>
{
    var store = ctx.RequestServices.GetRequiredService<AttendanceStore>();
    var path = ctx.Request.Path.Value ?? "";
    if (!path.StartsWith("/Setup", StringComparison.OrdinalIgnoreCase) && !Path.HasExtension(path) && store.CountWebUsers() == 0)
    {
        ctx.Response.Redirect("/Setup");
        return;
    }

    // Each person sees the app in their own language.
    var lang = ctx.User.FindFirstValue(WebApp.LanguageClaim) ?? ctx.Request.Cookies["lang"] ?? "en";
    Loc.UseForCurrentFlow(lang);
    await next();
});

app.UseAuthorization();
app.MapRazorPages();

// The department filter in the sidebar applies to every page, like in the desktop app.
app.MapGet("/Dept", (HttpContext ctx, string? d, string? back) =>
{
    if (int.TryParse(d, out var id)) ctx.Response.Cookies.Append("dept", id.ToString(), new CookieOptions { MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax });
    else ctx.Response.Cookies.Delete("dept");
    return Results.LocalRedirect(back is { Length: > 0 } b && b.StartsWith('/') && !b.StartsWith("//") ? b : "/");
}).RequireAuthorization();

app.MapPost("/Logout", async ctx =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    ctx.Response.Redirect("/Login");
}).RequireAuthorization();

app.Run();
