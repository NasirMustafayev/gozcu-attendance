using System.Globalization;
using System.Security.Claims;
using GozcuStandalone.Reporting;
using GozcuStandalone.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GozcuStandalone.Web;

public static class WebApp
{
    public const string CanEdit = "CanEdit";
    public const string IsAdmin = "IsAdmin";
    public const string LanguageClaim = "lang";
    public const string DisplayNameClaim = "display";

    // Translation for text that only the web app shows; everything else comes from the shared table.
    public static string T(string text) =>
        Loc.IsAz && WebAz.TryGetValue(text, out var az) ? az : Loc.T(text);

    public static string F(string template, params object?[] args) => string.Format(T(template), args);

    public static string Hm(TimeSpan t) => t == TimeSpan.Zero ? "—" : Loc.F("{0}h {1:00}m", (int)t.TotalHours, t.Minutes);

    public static string Min(int m) => m > 0 ? Loc.F("{0} min", m) : "—";

    public static string Day(DateTime d) => Loc.Dates(d.ToString("ddd d MMM", CultureInfo.InvariantCulture));

    public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static ClaimsPrincipal Principal(WebUser u) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.Name, u.Username),
        new Claim(DisplayNameClaim, u.DisplayName),
        new Claim(ClaimTypes.Role, u.Role),
        new Claim(LanguageClaim, u.Language),
    }, "cookie"));

    private static readonly Dictionary<string, string> WebAz = new()
    {
        ["Sign in"] = "Daxil ol", ["Username"] = "İstifadəçi adı", ["Password"] = "Şifrə", ["Sign out"] = "Çıxış",
        ["Wrong username or password."] = "İstifadəçi adı və ya şifrə yanlışdır.",
        ["First-time setup"] = "İlk quraşdırma", ["Create the administrator account. You can add more people in Settings afterwards."] = "Administrator hesabını yaradın. Sonra Ayarlar bölməsində başqa istifadəçilər əlavə edə bilərsiniz.",
        ["Your name"] = "Adınız", ["Repeat password"] = "Şifrəni təkrarlayın", ["Create account"] = "Hesab yarat",
        ["Use at least 8 characters."] = "Ən azı 8 simvol istifadə edin.", ["The passwords do not match."] = "Şifrələr uyğun gəlmir.", ["Fill in every field."] = "Bütün sahələri doldurun.",
        ["You don't have permission for that."] = "Bunun üçün icazəniz yoxdur.", ["Back to the dashboard"] = "Panelə qayıt",
        ["Device status comes from the desktop app."] = "Cihazın vəziyyəti masaüstü tətbiqdən gəlir.",
        ["Desktop app not seen"] = "Masaüstü tətbiq görünmür", ["The desktop app has not reported for a while. Check that it is running in the office."] = "Masaüstü tətbiq bir müddətdir xəbər vermir. Ofisdə işlədiyini yoxlayın.",
        ["Last check"] = "Son yoxlama", ["Updated {0}"] = "Yeniləndi: {0}", ["Refreshes every minute"] = "Hər dəqiqə yenilənir",
        ["Account"] = "Hesab", ["Change password"] = "Şifrəni dəyiş", ["Current password"] = "Cari şifrə", ["New password"] = "Yeni şifrə",
        ["Password changed."] = "Şifrə dəyişdirildi.", ["The current password is wrong."] = "Cari şifrə yanlışdır.",
        ["Web users"] = "Veb istifadəçilər", ["Role"] = "Rol", ["Add user"] = "İstifadəçi əlavə et", ["Save"] = "Saxla", ["Remove"] = "Sil",
        ["admin"] = "administrator", ["editor"] = "redaktor", ["viewer"] = "baxış",
        ["Administrators manage users; editors change attendance, people and schedules; viewers only look."] = "Administratorlar istifadəçiləri idarə edir; redaktorlar davamiyyəti, işçiləri və cədvəlləri dəyişir; baxış rolu yalnız baxa bilər.",
        ["Leave the password empty to keep it."] = "Şifrəni saxlamaq üçün boş buraxın.", ["User saved."] = "İstifadəçi saxlanıldı.", ["User removed."] = "İstifadəçi silindi.",
        ["You can't remove your own account."] = "Öz hesabınızı silə bilməzsiniz.", ["Device and sync settings are changed in the desktop app."] = "Cihaz və sinxronizasiya ayarları masaüstü tətbiqdə dəyişdirilir.",
        ["Language saved."] = "Dil saxlanıldı.", ["Saved."] = "Saxlanıldı.", ["Open"] = "Aç", ["Show"] = "Göstər", ["Print"] = "Çap",
        ["Excel"] = "Excel", ["CSV"] = "CSV", ["Actions"] = "Əməliyyatlar", ["Void"] = "Ləğv et", ["Back"] = "Geri",
        ["Select people"] = "İşçiləri seçin", ["Select all"] = "Hamısını seç", ["No people selected."] = "İşçi seçilməyib.",
        ["Person not found."] = "İşçi tapılmadı.", ["Scan not found."] = "Skan tapılmadı.", ["New shift"] = "Yeni növbə", ["Edit shift"] = "Növbəni redaktə et",
        ["Entry added."] = "Qeyd əlavə edildi.", ["Department saved."] = "Şöbə saxlanıldı.", ["Name saved."] = "Ad saxlanıldı.", ["Holiday saved."] = "Bayram saxlanıldı.",
        ["Holiday removed."] = "Bayram silindi.", ["Leave removed."] = "Məzuniyyət silindi.", ["Shift deleted."] = "Növbə silindi.", ["Department deleted."] = "Şöbə silindi.",
        ["Choose a month"] = "Ay seçin", ["Timesheet"] = "Tabel", ["Holiday name"] = "Bayramın adı", ["Days"] = "Günlər",
        ["Only the last 3,000 entries are shown."] = "Yalnız son 3000 qeyd göstərilir.", ["Something went wrong on the server."] = "Serverdə xəta baş verdi.",
        ["Enroll a person on the device under their ID number, then add them here."] = "İşçini cihazda öz ID nömrəsi ilə qeydiyyatdan keçirin, sonra burada əlavə edin.",
        ["Menu"] = "Menyu", ["Synced. {0} new scan(s)."] = "Sinxronlaşdırıldı. {0} yeni skan.", ["Sync failed: {0}"] = "Sinxronizasiya alınmadı: {0}",
        ["The desktop app did not answer in time. Check that it is running."] = "Masaüstü tətbiq vaxtında cavab vermədi. İşlədiyini yoxlayın.",
        ["Another sync is already running."] = "Başqa sinxronizasiya artıq gedir.",
        ["The desktop app is not running, so the web app synced with the device directly."] = "Masaüstü tətbiq işləmir, ona görə veb tətbiq cihazla birbaşa sinxronlaşdı.", ["Edit"] = "Redaktə", ["ON DEVICE"] = "CİHAZDA", ["from the desktop app"] = "masaüstü tətbiqdən",
    };
}

// Shared plumbing for every page: the database, the chosen department, permissions and messages.
public abstract class WebPage : Page
{
    private ReportContext? _ctx;

    public AttendanceStore Store => HttpContext.RequestServices.GetRequiredService<AttendanceStore>();
    public AppSettings Settings => HttpContext.RequestServices.GetRequiredService<AppSettings>();

    public int? Dept => int.TryParse(Request.Cookies["dept"], out var d) ? d : null;
    public ReportContext Ctx => _ctx ??= ReportContext.Load(Store, Settings, Dept);

    public bool CanEditData => User.IsInRole(AttendanceStore.RoleAdmin) || User.IsInRole(AttendanceStore.RoleEditor);
    public bool IsAdminUser => User.IsInRole(AttendanceStore.RoleAdmin);
    public string Who => User.Identity?.Name ?? "?";

    public string? Flash
    {
        get => TempData["flash"] as string;
        set => TempData["flash"] = value;
    }

    public string? FlashError
    {
        get => TempData["flashError"] as string;
        set => TempData["flashError"] = value;
    }

    public string T(string text) => WebApp.T(text);
    public string F(string template, params object?[] args) => WebApp.F(template, args);

    // Every change made on the web is written to the shared activity log with the person who made it.
    public void Log(string level, string area, string message) => Store.Log(level, area, $"{message} [web: {Who}]");

    public IActionResult Denied() => RedirectToPage("/Denied");

    public IActionResult Export(ReportTable table, string? format, string baseName)
    {
        var xlsx = !string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"att_{Guid.NewGuid():N}{(xlsx ? ".xlsx" : ".csv")}");
        try
        {
            table.Save(tmp);
            var bytes = System.IO.File.ReadAllBytes(tmp);
            Log("Info", "Export", $"Exported “{table.Title}” ({table.Rows.Count} rows) as {(xlsx ? "Excel" : "CSV")}.");
            return File(bytes, xlsx ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv",
                $"{baseName}_{DateTime.Today:yyyy-MM-dd}{(xlsx ? ".xlsx" : ".csv")}");
        }
        finally
        {
            try { System.IO.File.Delete(tmp); } catch (IOException) { }
        }
    }

    public static DateTime? ParseDate(string? s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static TimeSpan? ParseTime(string? s) =>
        TimeSpan.TryParseExact((s ?? "").Trim(), new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var t) ? t : null;
}
