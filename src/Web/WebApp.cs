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
    public const string PagesClaim = "pages";

    // Every page a role can be restricted to, with the folder its route lives under (matches _Layout's nav).
    // Settings and Denied are never restricted: everyone needs their own account page, and Denied must always show.
    public static readonly (string Key, string Folder, string Label)[] Pages =
    {
        ("dashboard", "/Index", "Dashboard"), ("attendance", "/Attendance", "Attendance"), ("schedule", "/Schedule", "Schedule"),
        ("reports", "/Reports", "Reports"), ("people", "/People", "People"), ("log", "/Log", "Activity log"),
    };

    // Null means "every page the role allows" (no restriction set). Settings/Entry/Denied/Setup/Login are always reachable.
    public static HashSet<string>? AllowedFolders(WebUser u)
    {
        if (string.IsNullOrWhiteSpace(u.Pages)) return null;
        var keys = u.Pages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        return Pages.Where(p => keys.Contains(p.Key)).Select(p => p.Folder).ToHashSet();
    }

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
        new Claim(PagesClaim, u.Pages),
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
        ["Mark full"] = "Tam gün say", ["This week"] = "Bu həftə", ["This month"] = "Bu ay",
        ["That person has no shift on that day."] = "Bu şəxsin o gün növbəsi yoxdur.", ["That day already has entries."] = "O gündə artıq qeydlər var.",
        ["Marked {0:yyyy-MM-dd} as a full day for {1}."] = "{1} üçün {0:yyyy-MM-dd} tam iş günü kimi qeyd edildi.",
        ["Pages"] = "Səhifələr", ["Every page for the role"] = "Rol üçün bütün səhifələr",
        ["Leave every page unchecked to allow all pages for the role; check only some pages to restrict the user to those."] = "Rol üçün bütün səhifələrə icazə vermək üçün heç birini işarələməyin; istifadəçini yalnız bəzi səhifələrlə məhdudlaşdırmaq üçün onları işarələyin.",
        ["Who"] = "Kim", ["Chosen people"] = "Seçilmiş işçilər", ["Whole department"] = "Bütün şöbə", ["Everyone"] = "Hamı",
        ["People (for \"Chosen people\")"] = "İşçilər (\"Seçilmiş işçilər\" üçün)",
        ["Choose at least one person, a department, or everyone."] = "Ən azı bir işçi, bir şöbə və ya hamını seçin.",
        ["{0} recorded for {1} day(s), {2} people."] = "{0} {1} gün üçün {2} nəfərə qeyd edildi.",
        ["Overwrite in place (admin): change the time directly, with no void/replace entry in the audit list."] = "Birbaşa yerində dəyiş (admin): vaxtı birbaşa dəyişdirir, jurnalda ləğv/əvəz qeydi olmadan.",
        ["Edit this day's scans"] = "Bu günün skanlarını redaktə et",
        ["Back to People"] = "İşçilərə qayıt", ["No department"] = "Şöbə yoxdur", ["Full timesheet"] = "Tam tabel",
        ["ABSENT"] = "YOXLUQ", ["day(s) this month"] = "gün bu ay", ["day(s) with a scan"] = "gün skanla",
        ["time(s)"] = "dəfə", ["time(s) · avg {0} min"] = "dəfə · orta {0} dəq", ["{0} time(s) · avg {1} min"] = "{0} dəfə · orta {1} dəq", ["OVERTIME"] = "ƏLAVƏ VAXT",
        ["ID {0}"] = "ID {0}", ["Profile"] = "Profil",
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
