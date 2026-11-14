using GozcuStandalone.Reporting;

namespace GozcuStandalone;

// Interface language. English text is the key: anything without an Azerbaijani entry is shown as written.
public static class Loc
{
    private static string _global = "en";
    private static readonly AsyncLocal<string?> Scoped = new();

    // The desktop app sets one language for the whole process; the web app sets it per request.
    public static string Language => Scoped.Value ?? _global;
    public static bool IsAz => Language == "az";

    public static void Init(string? language) => _global = language == "az" ? "az" : "en";

    public static void UseForCurrentFlow(string? language) => Scoped.Value = language == "az" ? "az" : "en";

    public static IReadOnlyDictionary<string, string> AzTable => Az;

    public static string T(string text)
    {
        if (!IsAz || string.IsNullOrEmpty(text)) return text;
        return Az.TryGetValue(text, out var az) ? az : text;
    }

    // Translates a template with {0}-style placeholders, then fills it in.
    public static string F(string template, params object?[] args) => string.Format(T(template), args);

    // Day and month names in dates written in English ("Tue 3 Nov", "November 2026").
    public static string Dates(string text)
    {
        if (!IsAz || string.IsNullOrEmpty(text)) return text;
        return NameRegex.Replace(text, m => NameMap[m.Value]);
    }

    private static readonly string[] Day2Az = { "Ba", "Be", "Ça", "Çə", "Ca", "Cü", "Şə" };

    public static string Day2(DateTime d) => IsAz ? Day2Az[(int)d.DayOfWeek] : d.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture)[..2];

    public static ReportTable Localize(ReportTable t)
    {
        if (!IsAz) return t;
        int day = t.Headers.IndexOf("Day");
        if (day >= 0) foreach (var r in t.Rows) if (day < r.Length) r[day] = Dates(r[day]);
        return new ReportTable
        {
            Title = T(t.Title),
            Subtitle = Dates(t.Subtitle),
            Headers = t.Headers.Select(T).ToList(),
            Rows = t.Rows,
            RowKinds = t.RowKinds,
            MarkFullKeys = t.MarkFullKeys,
            EditKeys = t.EditKeys,
            Footer = Dates(t.Footer),
        };
    }

    private static readonly (string En, string Az)[] Names =
    {
        ("Monday", "Bazar ertəsi"), ("Tuesday", "Çərşənbə axşamı"), ("Wednesday", "Çərşənbə"), ("Thursday", "Cümə axşamı"),
        ("Friday", "Cümə"), ("Saturday", "Şənbə"), ("Sunday", "Bazar"),
        ("January", "Yanvar"), ("February", "Fevral"), ("March", "Mart"), ("April", "Aprel"), ("May", "May"), ("June", "İyun"),
        ("July", "İyul"), ("August", "Avqust"), ("September", "Sentyabr"), ("October", "Oktyabr"), ("November", "Noyabr"), ("December", "Dekabr"),
        ("Mon", "B.e."), ("Tue", "Ç.a."), ("Wed", "Çər"), ("Thu", "C.a."), ("Fri", "Cümə"), ("Sat", "Şən"), ("Sun", "Baz"),
        ("Jan", "Yan"), ("Feb", "Fev"), ("Apr", "Apr"), ("Jun", "İyn"), ("Jul", "İyl"), ("Aug", "Avq"),
        ("Sep", "Sen"), ("Oct", "Okt"), ("Nov", "Noy"), ("Dec", "Dek"),
    };

    private static readonly Dictionary<string, string> NameMap = Names.ToDictionary(n => n.En, n => n.Az);
    private static readonly System.Text.RegularExpressions.Regex NameRegex = new(@"\b(" + string.Join("|", Names.Select(n => n.En)) + @")\b");

    private static readonly Dictionary<string, string> Az = new()
    {
        // navigation and general
        ["Dashboard"] = "Panel", ["Attendance"] = "Davamiyyət", ["Schedule"] = "Cədvəl", ["Reports"] = "Hesabatlar", ["People"] = "İşçilər",
        ["Activity log"] = "Fəaliyyət jurnalı", ["Settings"] = "Ayarlar", ["Standalone"] = "Müstəqil", ["SHOWING"] = "GÖSTƏRİLİR",
        ["All departments"] = "Bütün şöbələr", ["Connecting…"] = "Qoşulur…", ["Connected"] = "Qoşulub", ["Offline"] = "Oflayn", ["Syncing…"] = "Sinxronlaşır…",
        ["Not synced yet"] = "Hələ sinxronlaşmayıb", ["Last sync {0}"] = "Son sinxronizasiya {0}", ["Last sync {0:HH:mm:ss}"] = "Son sinxronizasiya {0:HH:mm:ss}",
        ["Show one department's people on every page"] = "Seçilmiş şöbənin işçilərini bütün səhifələrdə göstər",
        ["OK"] = "OK", ["Cancel"] = "Ləğv et", ["Close"] = "Bağla", ["Delete"] = "Sil", ["Edit"] = "Redaktə", ["Rename"] = "Adını dəyiş",
        ["Restore"] = "Bərpa et", ["Apply"] = "Tətbiq et", ["All"] = "Hamısı", ["Search"] = "Axtar", ["Filter"] = "Süzgəc", ["Date"] = "Tarix",
        ["From"] = "Başlanğıc", ["To"] = "Son", ["To (inclusive)"] = "Son (daxil olmaqla)", ["Type"] = "Növ", ["Person"] = "İşçi", ["Name"] = "Ad",
        ["Reason"] = "Səbəb", ["Report"] = "Hesabat", ["View"] = "Görünüş", ["Month"] = "Ay", ["Level"] = "Səviyyə", ["days"] = "gün", ["minutes"] = "dəqiqə",
        ["Details"] = "Ətraflı", ["Export"] = "İxrac", ["Export CSV"] = "İxrac", ["Print / PDF"] = "Çap / PDF", ["Find person"] = "İşçi tap",
        ["Select a department first."] = "Əvvəlcə şöbə seçin.",

        // dashboard
        ["PRESENT"] = "İŞDƏ", ["NOT IN YET"] = "HƏLƏ GƏLMƏYİB", ["LATE"] = "GECİKƏN", ["LEFT EARLY"] = "ERKƏN ÇIXAN", ["SCANS"] = "SKANLAR", ["ON DEVICE"] = "CİHAZDA",
        ["scanned in"] = "giriş edib", ["expected, no scan"] = "gözlənilir, skan yoxdur", ["past grace time"] = "güzəşt vaxtından sonra",
        ["past the grace time"] = "güzəşt vaxtından sonra", ["after {0}"] = "{0} -dən sonra", ["scanned out early"] = "erkən çıxıb", ["today"] = "bu gün", ["to collect"] = "yığılmalı",
        ["Device"] = "Cihaz", ["Serial number"] = "Seriya nömrəsi", ["Last sync"] = "Son sinxronizasiya", ["Sync now"] = "İndi sinxronlaşdır",
        ["Auto-sync on"] = "Avto-sinxronizasiya açıqdır", ["Auto-sync off"] = "Avto-sinxronizasiya söndürülüb",
        ["Reading never removes scans from the device, so the original software keeps working alongside this app."] = "Oxuma skanları cihazdan silmir, ona görə orijinal proqram bu tətbiqlə yanaşı işləməyə davam edir.",
        ["Expected today, not in yet"] = "Bu gün gözlənilən, hələ gəlməyənlər", ["Latest scans"] = "Son skanlar", ["Unnamed"] = "Adsız",
        ["No schedule yet. Import from the original software (Settings) or assign shifts in Schedule."] = "Hələ cədvəl yoxdur. Orijinal proqramdan idxal edin (Ayarlar) və ya Cədvəl bölməsində növbə təyin edin.",
        ["Everyone expected today has scanned in."] = "Bu gün gözlənilən hər kəs giriş edib.",
        ["Click to see who has scanned in"] = "Giriş edənləri görmək üçün klikləyin", ["Click to see who is expected but not in"] = "Gözlənilən, lakin gəlməyənləri görmək üçün klikləyin",
        ["Click to see who is late"] = "Gecikənləri görmək üçün klikləyin", ["Click to see who left early"] = "Erkən çıxanları görmək üçün klikləyin",
        ["Click to see today's scans"] = "Bu günün skanlarını görmək üçün klikləyin", ["Click to sync with the device now"] = "Cihazla indi sinxronlaşmaq üçün klikləyin",
        ["Click to see the full list"] = "Tam siyahını görmək üçün klikləyin",
        ["{0} · {1} · {2:HH:mm}"] = "{0} · {1} · {2:HH:mm}", ["{0} new scans, latest {1} at {2:HH:mm}"] = "{0} yeni skan, sonuncu: {1}, saat {2:HH:mm}",
        ["Up to date. Nothing new on the device."] = "Hər şey aktualdır. Cihazda yeni skan yoxdur.", ["Sync failed. Check the device and network."] = "Sinxronizasiya alınmadı. Cihazı və şəbəkəni yoxlayın.",
        ["Could not reach the device: {0}"] = "Cihaza qoşulmaq mümkün olmadı: {0}",

        // attendance
        ["Search name or ID"] = "Ad və ya ID axtar", ["Daily summary"] = "Günlük xülasə", ["All scans"] = "Bütün skanlar", ["Show voided"] = "Ləğv edilmişləri göstər",
        ["Add manual entry"] = "Əl ilə qeyd əlavə et", ["Ctrl+N"] = "Ctrl+N", ["Double-click a day to see and edit its scans"] = "Günün skanlarını görmək və dəyişmək üçün cüt klikləyin",
        ["Edit this scan…"] = "Bu skanı redaktə et…", ["Void this scan…"] = "Bu skanı ləğv et…", ["Restore this scan"] = "Bu skanı bərpa et", ["Delete this manual entry"] = "Bu əl ilə qeydi sil",
        ["No scans in this period."] = "Bu dövrdə skan yoxdur.", ["DATE"] = "TARİX", ["ID"] = "ID", ["NAME"] = "AD", ["DEPARTMENT"] = "ŞÖBƏ", ["SHIFT"] = "NÖVBƏ", ["IN"] = "GİRİŞ", ["OUT"] = "ÇIXIŞ",
        ["WORKED"] = "İŞLƏNİB", ["EARLY"] = "ERKƏN", ["EARLY LEAVE"] = "ERKƏN ÇIXIŞ", ["WHEN"] = "VAXT", ["METHOD"] = "ÜSUL", ["NOTE"] = "QEYD", ["STATUS"] = "STATUS", ["TIME"] = "VAXT",
        ["Voided"] = "Ləğv edilib", ["Manual"] = "Əl ilə", ["Fingerprint"] = "Barmaq izi", ["Face"] = "Üz", ["Active"] = "Aktiv", ["Former"] = "Keçmiş",
        ["{0} daily row(s) from {1} scan(s)"] = "{1} skandan {0} günlük sətir", ["{0} scan(s)"] = "{0} skan", [" · {0} manual"] = " · {0} əl ilə", [" · {0} voided"] = " · {0} ləğv edilib",
        [" · right-click a scan to void or restore it"] = " · skanı ləğv etmək və ya bərpa etmək üçün sağ klikləyin",
        ["Added for {0}: {1}."] = "{0} üçün əlavə edildi: {1}.", ["Those entries already exist."] = "Bu qeydlər artıq mövcuddur.",
        ["{0}, {1}: double-click a scan to edit it, or use Add manual entry."] = "{0}, {1}: skanı dəyişmək üçün cüt klikləyin və ya \"Əl ilə qeyd əlavə et\" düyməsindən istifadə edin.",
        ["Select a scan first."] = "Əvvəlcə skan seçin.", ["Restore the scan before editing it."] = "Redaktə etməzdən əvvəl skanı bərpa edin.", ["Nothing changed."] = "Heç nə dəyişmədi.",
        ["Scan changed to {0:HH:mm}."] = "Skan {0:HH:mm} olaraq dəyişdirildi.", ["An entry at that exact time already exists."] = "Həmin vaxtda artıq qeyd mövcuddur.",
        ["That scan is already voided."] = "Bu skan artıq ləğv edilib.", ["Void this scan?"] = "Bu skan ləğv edilsin?",
        ["{0} · {1}\nIt stays in the database but no report will count it. Write the reason."] = "{0} · {1}\nBazada qalır, lakin heç bir hesabatda nəzərə alınmayacaq. Səbəbi yazın.",
        ["Void scan"] = "Skanı ləğv et", ["Scan voided."] = "Skan ləğv edildi.", ["That scan is not voided."] = "Bu skan ləğv edilməyib.", ["Scan restored."] = "Skan bərpa edildi.",
        ["Select an entry first."] = "Əvvəlcə qeyd seçin.", ["Only manual entries can be deleted. Void a device scan instead."] = "Yalnız əl ilə qeydləri silmək olar. Cihaz skanını ləğv edin.",
        ["Delete the manual entry for {0} at {1}?"] = "{0} üçün {1} tarixli əl ilə qeyd silinsin?", ["Delete manual entry"] = "Əl ilə qeydi sil",
        ["Daily attendance"] = "Günlük davamiyyət", ["Time"] = "Vaxt", ["Method"] = "Üsul", ["Note"] = "Qeyd",

        // manual entry / edit / prompt dialogs
        ["Add entry"] = "Qeyd əlavə et", ["Save changes"] = "Dəyişiklikləri saxla", ["Edit scan"] = "Skanı redaktə et",
        ["Use this when a scan is missing, for example the device was offline or someone forgot to scan. Fill in the log in time, the log off time, or both. Entries are marked as manual in every report and kept in the audit list."]
            = "Skan çatışmayanda istifadə edin, məsələn cihaz oflayn olub və ya kimsə skan etməyi unudub. Giriş vaxtını, çıxış vaxtını və ya hər ikisini daxil edin. Qeydlər bütün hesabatlarda \"əl ilə\" kimi işarələnir və audit siyahısında saxlanılır.",
        ["Change the time of this scan. A device scan is never altered: it is voided and replaced by a manual entry, so the original stays in the audit list."]
            = "Bu skanın vaxtını dəyişin. Cihaz skanı heç vaxt dəyişdirilmir: ləğv edilir və əl ilə qeydlə əvəz olunur, orijinal audit siyahısında qalır.",
        ["Log in (HH:mm)"] = "Giriş (SS:dd)", ["Log off (HH:mm)"] = "Çıxış (SS:dd)", ["Time (HH:mm)"] = "Vaxt (SS:dd)",
        ["Choose a person from the list, or type their ID number."] = "Siyahıdan işçi seçin və ya onun ID nömrəsini yazın.", ["Pick a date."] = "Tarix seçin.",
        ["Enter the log in time, the log off time, or both."] = "Giriş vaxtını, çıxış vaxtını və ya hər ikisini daxil edin.", ["Times should look like 09:30."] = "Vaxt 09:30 kimi olmalıdır.",
        ["The log off time must be after the log in time."] = "Çıxış vaxtı giriş vaxtından sonra olmalıdır.", ["That moment is in the future."] = "Bu an gələcəkdədir.",
        ["Please write a short reason so the entry can be explained later."] = "Qeydin sonradan izah oluna bilməsi üçün qısa səbəb yazın.", ["This can't be empty."] = "Bu sahə boş ola bilməz.",

        // people
        ["Enroll a person's fingerprint or face on the device under their ID number, then add them here. Double-click a name or department to change it."]
            = "İşçinin barmaq izini və ya üzünü cihazda onun ID nömrəsi ilə qeydiyyatdan keçirin, sonra burada əlavə edin. Adı və ya şöbəni dəyişmək üçün cüt klikləyin.",
        ["Add person"] = "İşçi əlavə et", ["Mark as former"] = "Keçmiş kimi işarələ", ["Departments…"] = "Şöbələr…", ["Import names from CSV"] = "Adları CSV-dən idxal et", ["Show former"] = "Keçmişləri göstər",
        ["IDs that scanned on the device but have no name yet. Pick one to fill in the ID."] = "Cihazda skan edən, lakin hələ adı olmayan ID-lər. ID-ni doldurmaq üçün birini seçin.",
        ["{0} unnamed ID(s) · next free ID: {1}"] = "{0} adsız ID · növbəti boş ID: {1}", ["No unnamed IDs · next free ID: {0}"] = "Adsız ID yoxdur · növbəti boş ID: {0}",
        ["Enter a numeric ID and a name."] = "Rəqəmlə ID və ad daxil edin.", ["Select a person first."] = "Əvvəlcə işçi seçin.",
        ["{0} marked as former. Their old scans keep the name."] = "{0} keçmiş kimi işarələndi. Köhnə skanlarda ad saxlanılır.", ["{0} is active again."] = "{0} yenidən aktivdir.",
        ["Imported {0} name(s)."] = "{0} ad idxal edildi.",
        ["Departments"] = "Şöbələr", ["New department"] = "Yeni şöbə", ["PEOPLE"] = "İŞÇİLƏR", ["What should it be called?"] = "Adı nə olsun?", ["Add department"] = "Şöbə əlavə et",
        ["A department with that name already exists."] = "Bu adda şöbə artıq mövcuddur.", ["Rename department"] = "Şöbənin adını dəyiş", ["New name for “{0}”:"] = "“{0}” üçün yeni ad:",
        ["Delete the “{0}” department?{1}"] = "“{0}” şöbəsi silinsin?{1}", ["\n\n{0} people in it will have no department."] = "\n\nOnun içindəki {0} işçinin şöbəsi olmayacaq.", ["Delete department"] = "Şöbəni sil",
        ["Use departments to look at one group of people at a time. Assign people to a department on the People page."] = "Şöbələr vasitəsilə eyni anda bir qrup işçiyə baxa bilərsiniz. İşçiləri \"İşçilər\" səhifəsində şöbəyə təyin edin.",

        // settings
        ["Device and sync"] = "Cihaz və sinxronizasiya", ["Device IP address"] = "Cihazın IP ünvanı", ["Port"] = "Port", ["Sync every (seconds)"] = "Sinxronizasiya intervalı (saniyə)",
        ["Default work start"] = "Standart iş başlanğıcı", ["Default work end"] = "Standart iş sonu",
        ["Default hours apply only to days where a person has no shift in the Schedule."] = "Standart saatlar yalnız işçinin Cədvəldə növbəsi olmayan günlərə aiddir.",
        ["Set the device clock to this PC once a day"] = "Cihazın saatını gündə bir dəfə bu kompüterə uyğunlaşdır", ["Start with Windows (minimized)"] = "Windows ilə birlikdə başlat (kiçildilmiş)",
        ["Keep running in the tray when the window is closed"] = "Pəncərə bağlananda sistem tepsisində işləməyə davam et",
        ["Take over from the original software: remove scans from the device once they are saved here"] = "Orijinal proqramı əvəz et: skanlar burada saxlanıldıqdan sonra cihazdan sil",
        ["Leave this off while the original software is still in use, otherwise it will stop receiving new scans."] = "Orijinal proqram hələ istifadə olunursa bunu söndürülmüş saxlayın, əks halda o yeni skanları almayacaq.",
        ["Also copy new scans from the original software every"] = "Orijinal proqramdan yeni skanları həmçinin hər", ["Catches scans the original software collected before this app did. Read-only, and it never changes the original."]
            = "Orijinal proqramın bu tətbiqdən əvvəl topladığı skanları tutur. Yalnız oxuyur və orijinalı heç vaxt dəyişmir.",
        ["Keep the activity log for"] = "Fəaliyyət jurnalını saxla:", ["Save settings"] = "Ayarları saxla", ["Set device clock now"] = "Cihazın saatını indi qur",
        ["Import from the original software"] = "Orijinal proqramdan idxal", ["Import history and names"] = "Tarixçəni və adları idxal et",
        ["Copies old scans, employee names, departments, shifts and leave into this app. It only reads from the original database and never changes it. Safe to run more than once; anything changed here is kept."]
            = "Köhnə skanları, işçi adlarını, şöbələri, növbələri və məzuniyyətləri bu tətbiqə köçürür. Orijinal bazadan yalnız oxuyur və onu heç vaxt dəyişmir. Bir neçə dəfə işlətmək təhlükəsizdir; burada edilən dəyişikliklər saxlanılır.",
        ["Backup and restore"] = "Ehtiyat nüsxə və bərpa", ["Back up now"] = "İndi ehtiyat nüsxə çıxar", ["Restore from backup…"] = "Ehtiyat nüsxədən bərpa et…", ["Open backups folder"] = "Ehtiyat nüsxələr qovluğunu aç",
        ["A backup is made automatically once a day (the last 14 are kept). Restoring replaces everything in this app with the chosen backup after a restart; the current data is saved first."]
            = "Ehtiyat nüsxə gündə bir dəfə avtomatik çıxarılır (son 14 saxlanılır). Bərpa yenidən başladıqdan sonra bu tətbiqdəki hər şeyi seçilmiş nüsxə ilə əvəz edir; cari məlumat əvvəlcə saxlanılır.",
        ["About"] = "Haqqında", ["Open data folder"] = "Məlumat qovluğunu aç", ["Language"] = "Dil", ["Restart the app to change the language."] = "Dili dəyişmək üçün tətbiqi yenidən başladın.",
        ["The language changes after the app restarts. Restart now?"] = "Dil tətbiq yenidən başladıqdan sonra dəyişəcək. İndi yenidən başladılsın?", ["Restart"] = "Yenidən başlat",
        ["Data folder: {0}"] = "Məlumat qovluğu: {0}", ["No backup yet."] = "Hələ ehtiyat nüsxə yoxdur.", ["Latest backup: {0:yyyy-MM-dd HH:mm} ({1}, {2:0.0} MB)"] = "Son ehtiyat nüsxə: {0:yyyy-MM-dd HH:mm} ({1}, {2:0.0} MB)",
        ["Attendance {0}. Reads attendance from the face and fingerprint terminal on your network. {1:N0} scans stored, database {2:0.0} MB."]
            = "Davamiyyət {0}. Şəbəkədəki üz və barmaq izi terminalından davamiyyəti oxuyur. {1:N0} skan saxlanılıb, baza {2:0.0} MB.",
        ["Check the values: valid IP, port, interval of 5+ seconds, log days 7+, times like 09:00."] = "Dəyərləri yoxlayın: düzgün IP, port, 5+ saniyə interval, jurnal 7+ gün, vaxt 09:00 kimi.",
        ["With this on, the device forgets each scan as soon as this app has saved it, so the original software will no longer receive new scans.\n\nTurn it on?"]
            = "Bu açıq olduqda cihaz hər skanı bu tətbiq saxlayan kimi unudur, ona görə orijinal proqram artıq yeni skanları almayacaq.\n\nAçılsın?",
        ["Take over from the original software"] = "Orijinal proqramı əvəz et", ["Saved, but Windows blocked the startup entry."] = "Saxlanıldı, lakin Windows başlanğıc qeydini bloklayıb.",
        ["Settings saved."] = "Ayarlar saxlanıldı.", ["Device clock set to {0:HH:mm:ss}."] = "Cihazın saatı {0:HH:mm:ss} olaraq quruldu.", ["The device did not confirm the new time."] = "Cihaz yeni vaxtı təsdiq etmədi.",
        ["Could not set the clock: {0}"] = "Saatı qurmaq mümkün olmadı: {0}", ["Importing…"] = "İdxal edilir…", ["Import failed: {0}"] = "İdxal alınmadı: {0}",
        ["Read {0:N0} scans ({1:N0} new), {2} new name(s), {3} departments, {4} shifts, {5:N0} shift days, {6} leave entries."] = "{0:N0} skan oxundu ({1:N0} yeni), {2} yeni ad, {3} şöbə, {4} növbə, {5:N0} növbə günü, {6} məzuniyyət qeydi.",
        ["Backup saved."] = "Ehtiyat nüsxə saxlanıldı.", ["Backup failed: {0}"] = "Ehtiyat nüsxə alınmadı: {0}", ["Could not start the restore: {0}"] = "Bərpanı başlatmaq mümkün olmadı: {0}",
        ["Replace everything in this app with the backup “{0}”?\n\nYour current data is saved first, and the app restarts to finish the restore."]
            = "Bu tətbiqdəki hər şey “{0}” ehtiyat nüsxəsi ilə əvəz edilsin?\n\nCari məlumat əvvəlcə saxlanılır və bərpanı tamamlamaq üçün tətbiq yenidən başlayır.",
        ["Restore from backup"] = "Ehtiyat nüsxədən bərpa", ["Something went wrong"] = "Xəta baş verdi", ["Could not save"] = "Saxlamaq mümkün olmadı",
        ["\n\nIf the file is open in Excel, close it and try again."] = "\n\nFayl Excel-də açıqdırsa, bağlayıb yenidən cəhd edin.", ["Saved {0}"] = "Saxlanıldı: {0}",
        ["Attendance is still running"] = "Davamiyyət hələ də işləyir", ["It keeps syncing from the tray. Right-click the icon to exit."] = "Sistem tepsisində sinxronizasiya davam edir. Çıxmaq üçün işarəyə sağ klikləyin.",

        // schedule
        ["Who works which shift on which day. Reports use this to decide what counts as late, overtime or absent."] = "Kimin hansı gün hansı növbədə işlədiyi. Hesabatlar gecikməni, əlavə işi və yoxluğu müəyyən etmək üçün bundan istifadə edir.",
        ["Calendar"] = "Təqvim", ["Shifts"] = "Növbələr", ["Leave"] = "Məzuniyyət", ["Holidays"] = "Bayramlar", ["Assign shifts"] = "Növbə təyin et", ["Remove shifts"] = "Növbələri sil",
        ["Select people in the table below (Ctrl or Shift to select several), pick a shift and the dates, then press Apply."] = "Aşağıdakı cədvəldə işçiləri seçin (bir neçəsi üçün Ctrl və ya Shift), növbəni və tarixləri seçin, sonra Tətbiq et düyməsini basın.",
        ["Shift"] = "Növbə", ["Skip days the shift doesn't run"] = "Növbənin işləmədiyi günləri keç", ["PERSON"] = "İŞÇİ",
        ["{0}   H = holiday   L = personal leave   lowercase = assigned, but the shift doesn't run that weekday   blank = no shift"] = "{0}   H = bayram   L = şəxsi məzuniyyət   kiçik hərf = təyin edilib, lakin növbə həmin gün işləmir   boş = növbə yoxdur",
        ["Select one or more people in the table first."] = "Əvvəlcə cədvəldə bir və ya bir neçə işçi seçin.", ["Choose a shift first."] = "Əvvəlcə növbə seçin.", ["{0} day(s) updated."] = "{0} gün yeniləndi.",
        ["A shift sets the working hours and how forgiving the reports are about arriving late or leaving early."] = "Növbə iş saatlarını və hesabatların gecikmə və ya erkən çıxışa nə qədər güzəşt etdiyini müəyyən edir.",
        ["New shift"] = "Yeni növbə", ["Save shift"] = "Növbəni saxla", ["Delete selected"] = "Seçilmişi sil", ["Remove selected"] = "Seçilmişi sil", ["HOURS"] = "SAATLAR", ["DAYS"] = "GÜNLƏR", ["DAYS ASSIGNED"] = "TƏYİN EDİLƏN GÜNLƏR",
        ["LATE ARRIVAL GRACE"] = "GECİKMƏ GÜZƏŞTİ", ["EARLY LEAVE GRACE"] = "ERKƏN ÇIXIŞ GÜZƏŞTİ", ["OVERTIME AFTER"] = "ƏLAVƏ İŞ BUNDAN SONRA", ["UNPAID BREAK"] = "HAQQI ÖDƏNMƏYƏN FASİLƏ", ["ORIGIN"] = "MƏNŞƏ",
        ["Select a shift first."] = "Əvvəlcə növbə seçin.", ["Shift saved. Reports now use the new rules."] = "Növbə saxlanıldı. Hesabatlar indi yeni qaydalardan istifadə edir.", ["Delete shift"] = "Növbəni sil",
        ["Every day"] = "Hər gün", ["Mon"] = "B.e.", ["Tue"] = "Ç.a.", ["Wed"] = "Çər", ["Thu"] = "C.a.", ["Fri"] = "Cümə", ["Sat"] = "Şən", ["Sun"] = "Baz",
        ["Name"] = "Ad", ["Starts (HH:mm)"] = "Başlayır (SS:dd)", ["Ends (HH:mm)"] = "Bitir (SS:dd)", ["Grace times (minutes)"] = "Güzəşt vaxtları (dəqiqə)", ["Early arrival allowed"] = "Erkən gəliş icazəlidir",
        ["Late arrival grace"] = "Gecikmə güzəşti", ["Early leave grace"] = "Erkən çıxış güzəşti", ["Late leave grace (before overtime)"] = "Gec çıxış güzəşti (əlavə işdən əvvəl)", ["Unpaid break"] = "Haqqı ödənməyən fasilə",
        ["Minutes taken off worked time on a full working day (5 hours or more). Leave at 0 if the break counts as work."] = "Tam iş günündə (5 saat və daha çox) işlənmiş vaxtdan çıxılan dəqiqələr. Fasilə iş sayılırsa 0 saxlayın.",
        ["Working days"] = "İş günləri", ["Late arrival counts only after the grace time has passed. Overtime counts only after the late-leave grace has passed."] = "Gecikmə yalnız güzəşt vaxtı keçdikdən sonra sayılır. Əlavə iş yalnız gec çıxış güzəşti keçdikdən sonra sayılır.",
        ["Give the shift a name."] = "Növbəyə ad verin.", ["Start and end should look like 09:00."] = "Başlanğıc və son 09:00 kimi olmalıdır.",
        ["The shift must end after it starts (overnight shifts are not supported)."] = "Növbə başladıqdan sonra bitməlidir (gecə növbələri dəstəklənmir).", ["Grace times must be whole numbers of minutes, 0 or more."] = "Güzəşt vaxtları 0 və ya daha çox tam dəqiqə olmalıdır.",
        ["The unpaid break must be between 0 and 240 minutes."] = "Haqqı ödənməyən fasilə 0 ilə 240 dəqiqə arasında olmalıdır.", ["Choose at least one working day."] = "Ən azı bir iş günü seçin.",
        ["Record annual leave, sick days and business trips. People on leave are not reported absent, and the calendar marks those days with L."] = "İllik məzuniyyəti, xəstəlik günlərini və ezamiyyətləri qeyd edin. Məzuniyyətdəki işçilər yoxluq sayılmır, təqvimdə həmin günlər L ilə işarələnir.",
        ["Add leave"] = "Məzuniyyət əlavə et", ["Note (optional)"] = "Qeyd (könüllü)", ["No leave recorded for this period."] = "Bu dövr üçün məzuniyyət qeyd edilməyib.",
        ["Annual leave"] = "İllik məzuniyyət", ["Sick leave"] = "Xəstəlik məzuniyyəti", ["Unpaid leave"] = "Ödənişsiz məzuniyyət", ["Business trip"] = "Ezamiyyət", ["Other"] = "Digər",
        ["Choose a person first."] = "Əvvəlcə işçi seçin.", ["Pick the first and last day."] = "İlk və son günü seçin.", ["The last day can't be before the first day."] = "Son gün ilk gündən əvvəl ola bilməz.",
        ["{0} recorded for {1} day(s)."] = "{0} {1} gün üçün qeyd edildi.", ["Select a leave entry first."] = "Əvvəlcə məzuniyyət qeydi seçin.", ["Leave copied from the original software can't be deleted here."] = "Orijinal proqramdan köçürülmüş məzuniyyət burada silinə bilməz.",
        ["Delete {0}'s leave from {1} to {2}?"] = "{0} adlı işçinin {1} - {2} məzuniyyəti silinsin?", ["Delete leave"] = "Məzuniyyəti sil", ["Add holiday"] = "Bayram əlavə et",
        ["Holidays apply to everyone: nobody is reported absent on these days. Personal leave copied from the original software is applied automatically."] = "Bayramlar hamıya aiddir: bu günlərdə heç kim yoxluq sayılmır. Orijinal proqramdan köçürülmüş şəxsi məzuniyyət avtomatik tətbiq olunur.",
        ["No holidays added yet."] = "Hələ bayram əlavə edilməyib.", ["Pick a date and give the holiday a name."] = "Tarix seçin və bayrama ad verin.", ["Select a holiday first."] = "Əvvəlcə bayram seçin.",
        ["Delete the “{0}” shift?{1}{2}"] = "“{0}” növbəsi silinsin?{1}{2}", ["Schedule"] = "Cədvəl",
        ["{0}: {1}, from {2:d MMM yyyy} to {3:d MMM yyyy}?"] = "{0}: {1}, {2:d MMM yyyy} - {3:d MMM yyyy}?",

        // reports and log
        ["Month"] = "Ay", ["Monthly summary"] = "Aylıq xülasə", ["Person timesheet"] = "İşçinin tabeli", ["Timesheet"] = "Tabel", ["Absences"] = "Yoxluqlar", ["Late arrivals"] = "Gecikmələr", ["Early leave"] = "Erkən çıxışlar",
        ["Overtime"] = "Əlavə iş", ["Manual entries and voided scans"] = "Əl ilə qeydlər və ləğv edilmiş skanlar", ["Present today"] = "Bu gün işdə olanlar", ["Late today"] = "Bu gün gecikənlər", ["Left early today"] = "Bu gün erkən çıxanlar",
        ["Scans today"] = "Bu günün skanları", ["Nothing to report for these settings."] = "Bu ayarlar üçün hesabat yoxdur.", ["Choose a person."] = "İşçi seçin.", ["Could not build the report: {0}"] = "Hesabatı hazırlamaq mümkün olmadı: {0}",
        ["{0} · {1}"] = "{0} · {1}", [" · {0} of {1} rows match"] = " · {1} sətirdən {0} uyğun gəlir", ["Nobody to show."] = "Göstəriləcək işçi yoxdur.",
        ["Department"] = "Şöbə", ["Days"] = "Günlər", ["Absent"] = "Yoxluq", ["Worked"] = "İşlənib", ["Avg per day"] = "Gündə orta", ["Late"] = "Gecikmə", ["No out scan"] = "Çıxış skanı yoxdur", ["Day"] = "Gün",
        ["First in"] = "İlk giriş", ["Last out"] = "Son çıxış", ["Scans"] = "Skanlar", ["Late by"] = "Gecikmə", ["Early by"] = "Erkən çıxma", ["Status"] = "Status", ["When"] = "Vaxt", ["Kind"] = "Növ", ["Changed by"] = "Dəyişdirən", ["Recorded at"] = "Qeyd vaxtı",
        ["Not in yet"] = "Hələ gəlməyib", ["Holiday: {0}"] = "Bayram: {0}", ["Leave: {0}"] = "Məzuniyyət: {0}", ["On leave: {0}"] = "Məzuniyyətdə: {0}", ["Manual entry"] = "Əl ilə qeyd", ["Counted"] = "Sayılır", ["department"] = "şöbə",
        ["{0} people. “Absent” counts working days with no scan, leave or holiday. Days with a single scan are left out of the hours."] = "{0} işçi. “Yoxluq” skanı, məzuniyyəti və ya bayramı olmayan iş günlərini sayır. Tək skanı olan günlər saatlara daxil edilmir.",
        ["Present {0} day(s) · absent {1} · worked {2} · late {3} min · early leave {4} min · overtime {5}"] = "İştirak {0} gün · yoxluq {1} · işlənib {2} · gecikmə {3} dəq · erkən çıxış {4} dəq · əlavə iş {5}",
        ["{0} to {1}"] = "{0} - {1}", ["{0} absence(s) across {1} people. Leave and holidays are not counted."] = "{1} işçi üzrə {0} yoxluq. Məzuniyyət və bayramlar sayılmır.",
        ["{0} late arrival(s), {1} in total. Arrivals inside the shift's grace time are not counted."] = "{0} gecikmə, cəmi {1}. Növbənin güzəşt vaxtı daxilində gəlişlər sayılmır.",
        ["{0} early departure(s), {1} in total."] = "{0} erkən çıxış, cəmi {1}.", ["{0} day(s) with overtime, {1} in total. Leaving inside the shift's grace time is not overtime."] = "{0} gün əlavə iş, cəmi {1}. Növbənin güzəşt vaxtı daxilində çıxış əlavə iş sayılmır.",
        ["{0} people have scanned in today. Double-click a person for their monthly timesheet."] = "Bu gün {0} nəfər giriş edib. Aylıq tabel üçün işçiyə cüt klikləyin.",
        ["{0} late arrival(s). Arrivals inside the shift's grace time are not counted. Double-click a person for their timesheet."] = "{0} gecikmə. Növbənin güzəşt vaxtı daxilində gəlişlər sayılmır. Tabel üçün işçiyə cüt klikləyin.",
        ["{0} early departure(s). Only people who have scanned out are counted. Double-click a person for their timesheet."] = "{0} erkən çıxış. Yalnız çıxış skanı edənlər sayılır. Tabel üçün işçiyə cüt klikləyin.",
        ["{0} not in yet, {1} on leave or holiday. Double-click a person for their timesheet."] = "{0} nəfər hələ gəlməyib, {1} nəfər məzuniyyət və ya bayramdadır. Tabel üçün işçiyə cüt klikləyin.",
        ["{0} scan(s) today."] = "Bu gün {0} skan.", ["{0} entry."] = "{0} qeyd.", ["{0} entries."] = "{0} qeyd.",
        ["Search"] = "Axtar", ["Clear log"] = "Jurnalı təmizlə", ["LEVEL"] = "SƏVİYYƏ", ["AREA"] = "SAHƏ", ["MESSAGE"] = "MESAJ", ["Nothing logged for these filters."] = "Bu süzgəclər üçün jurnal qeydi yoxdur.",
        ["Delete the whole activity log? Scans and reports are not affected."] = "Bütün fəaliyyət jurnalı silinsin? Skanlara və hesabatlara təsir etmir.",
        ["{0} entr{1}"] = "{0} qeyd", ["Showing the latest 3,000 entries. Narrow the dates to see older ones."] = "Son 3000 qeyd göstərilir. Köhnələri görmək üçün tarixləri daraldın.",
        ["Time"] = "Vaxt", ["Level"] = "Səviyyə", ["Area"] = "Sahə", ["Message"] = "Mesaj",
        ["Title"] = "Başlıq", ["Report"] = "Hesabat", ["Activity log"] = "Fəaliyyət jurnalı", ["Activity log (export)"] = "Fəaliyyət jurnalı",
        ["Something went wrong."] = "Xəta baş verdi.",
        ["{0}d · {1} min"] = "{0}g · {1} dəq",
        ["only one scan"] = "yalnız bir skan", ["manual entry"] = "əl ilə qeyd", ["Day off"] = "istirahət günü", ["{0} min"] = "{0} dəq", ["{0}h {1:00}m"] = "{0}s {1:00}d",
        ["ID {0} · {1} scan(s) · last {2}"] = "ID {0} · {1} skan · sonuncu {2}", ["1 person"] = "1 nəfər", ["{0} people"] = "{0} nəfər",
        ["Remove all shifts from"] = "Bütün növbələr silinsin", ["Put on the “{0}” shift"] = "“{0}” növbəsinə qoyulsun",
        ["\n\n{0:N0} assigned day(s) will lose their shift."] = "\n\n{0:N0} təyin edilmiş gün növbəsini itirəcək.",
        ["\n\nThis shift was copied from the original software and will come back if you import from it again."] = "\n\nBu növbə orijinal proqramdan köçürülüb və yenidən idxal etsəniz geri qayıdacaq.",
        ["Open Attendance"] = "Davamiyyəti aç", ["Exit"] = "Çıxış", ["Info"] = "Məlumat", ["Warn"] = "Xəbərdarlıq", ["Error"] = "Xəta",
        ["Mark full day"] = "Tam gün say", ["This week"] = "Bu həftə", ["This month"] = "Bu ay",
        ["That person has no shift on that day."] = "Bu şəxsin o gün növbəsi yoxdur.", ["That day already has entries."] = "O gündə artıq qeydlər var.",
        ["Marked {0:yyyy-MM-dd} as a full day for {1}."] = "{1} üçün {0:yyyy-MM-dd} tam iş günü kimi qeyd edildi.",
        ["Edit this day's scans"] = "Bu günün skanlarını redaktə et",
        ["Overwrite in place: change the time directly, with no void/replace entry in the audit list."] = "Birbaşa yerində dəyiş: vaxtı birbaşa dəyişdirir, jurnalda ləğv/əvəz qeydi olmadan.",
        ["Profile"] = "Profil", ["{0} · Profile"] = "{0} · Profil",
        ["ABSENT"] = "YOXLUQ", ["OVERTIME"] = "ƏLAVƏ VAXT",
        ["day(s) this month"] = "gün bu ay", ["day(s) with a scan"] = "gün skanla",
        ["time(s)"] = "dəfə", ["time(s) · avg {0} min"] = "dəfə · orta {0} dəq", ["{0} time(s) · avg {1} min"] = "{0} dəfə · orta {1} dəq",
        ["ID {0}"] = "ID {0}", ["Full timesheet"] = "Tam tabel",
        ["Double-click a row to see that person's profile"] = "İşçinin profilini görmək üçün sətrə cüt klikləyin",
    };
}
