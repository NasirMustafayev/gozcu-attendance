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

// One-time upgrade for databases created before multi-device support.
using (var scope = app.Services.CreateScope())
{
    var store = scope.ServiceProvider.GetRequiredService<AttendanceStore>();
    var settings = scope.ServiceProvider.GetRequiredService<AppSettings>();
    store.MigrateLegacyDevice(settings.DeviceIp, settings.DevicePort);
}

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

// A user can be restricted to a chosen set of pages; everyone keeps their own account page and admins keep every page.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true && !ctx.User.IsInRole(AttendanceStore.RoleAdmin))
    {
        var pages = ctx.User.FindFirstValue(WebApp.PagesClaim);
        if (!string.IsNullOrWhiteSpace(pages))
        {
            var allowed = WebApp.Pages.Where(p => pages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(p.Key))
                .Select(p => p.Folder).ToHashSet();
            // "/" is the dashboard's route, just as the sidebar treats it as "/Index".
            var path = ctx.Request.Path.Value is "/" or "" ? "/Index" : ctx.Request.Path.Value!;
            // A plain StartsWith would match "/Logout" against the "/Log" folder; require the folder
            // name to end there (end of path, or the next character is "/") so only that page counts.
            static bool InFolder(string path, string folder) =>
                path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
                (path.Length == folder.Length || path[folder.Length] == '/');
            var restricted = WebApp.Pages.Select(p => p.Folder).Any(f => InFolder(path, f));
            if (restricted && !allowed.Any(f => InFolder(path, f)))
            {
                var target = allowed.FirstOrDefault() is { Length: > 0 } first ? first : "/Settings";
                ctx.Response.Redirect(target == "/Index" ? "/" : target);
                return;
            }
        }
    }
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
