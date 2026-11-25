using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GozcuStandalone.Reporting;

namespace GozcuStandalone.App;

public sealed record ShiftItem(ShiftDef? Def, string Label);

public sealed record ShiftRow(ShiftDef Def, string Name, string Hours, string Days, string LateGrace, string EarlyLeave, string LateLeave, string Break, string Assigned, string Origin);

public sealed record LeaveTypeItem(int Type, string Label);

public sealed record LeaveItem(long Id, string Name, string From, string To, string Days, string Description, string Origin, bool IsLocal);

public sealed record HolidayRow(DateTime Date, string DateText, string Day, string Name);

public partial class ScheduleView : UserControl
{
    private readonly bool _ready;
    private HashSet<uint> _selected = new();
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public ScheduleView()
    {
        InitializeComponent();

        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        MonthBox.ItemsSource = Enumerable.Range(-3, 16).Select(i => first.AddMonths(i).ToString("yyyy-MM", CultureInfo.InvariantCulture)).ToList();
        MonthBox.SelectedIndex = 3;
        SetApplyDates();
        _ready = true;

        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) Reload(); };
        AppServices.DataChanged += () => { if (IsVisible) Reload(); };
    }

    private Window? Owner => Window.GetWindow(this);

    public void Reload()
    {
        if (!_ready || AppServices.Store is null) return;
        LoadShiftChoices();
        LoadDeptChoices();
        BuildCalendar();
        LoadShifts();
        LoadHolidays();
        LoadLeave();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        CalendarPanel.Visibility = TabCalendar.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ShiftsPanel.Visibility = TabShifts.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HolidaysPanel.Visibility = TabHolidays.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LeavePanel.Visibility = TabLeave.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- calendar ----------

    private void Month_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (DateTime.TryParseExact((MonthBox.SelectedItem as string ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)) _month = m;
        SetApplyDates();
        BuildCalendar();
    }

    private void SetApplyDates()
    {
        if (MonthBox.SelectedItem is string s && DateTime.TryParseExact(s + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)) _month = m;
        ApplyFrom.SelectedDate = _month;
        ApplyTo.SelectedDate = _month.AddMonths(1).AddDays(-1);
    }

    private void CalSearch_Changed(object sender, TextChangedEventArgs e) { if (_ready) BuildCalendar(); }

    private void LoadShiftChoices()
    {
        var keep = (ShiftBox.SelectedItem as ShiftItem)?.Def?.Id;
        var items = AppServices.Store.GetShifts().Select(s => new ShiftItem(s, $"{s.Name}  ({s.Start:hh\\:mm}–{s.End:hh\\:mm})")).ToList();
        ShiftBox.ItemsSource = items;
        ShiftBox.SelectedItem = items.FirstOrDefault(i => i.Def?.Id == keep) ?? items.FirstOrDefault();
    }

    private void LoadDeptChoices()
    {
        var items = new List<DeptItem> { new(null, Loc.T("Select department")) };
        items.AddRange(AppServices.Store.GetDepartments().OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).Select(d => new DeptItem(d.Id, d.Name)));
        SelectDeptBox.ItemsSource = items;
        SelectDeptBox.SelectedIndex = 0;
    }

    private void SelectAllCal_Click(object sender, RoutedEventArgs e)
    {
        CalGrid.SelectAll();
        _selected = CalGrid.SelectedItems.OfType<DataRowView>().Select(r => (uint)r["Id"]).ToHashSet();
    }

    private void SelectNoneCal_Click(object sender, RoutedEventArgs e)
    {
        CalGrid.SelectedItems.Clear();
        _selected.Clear();
    }

    private void SelectDeptBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SelectDeptBox.SelectedItem is not DeptItem { Id: { } id } d) return;
        var deptOf = AppServices.Store.GetPersonDepartments();
        CalGrid.SelectedItems.Clear();
        foreach (var rv in CalGrid.Items.OfType<DataRowView>())
            if (deptOf.TryGetValue((uint)rv["Id"], out var pd) && pd == d.Id) CalGrid.SelectedItems.Add(rv);
        _selected = CalGrid.SelectedItems.OfType<DataRowView>().Select(r => (uint)r["Id"]).ToHashSet();
        SelectDeptBox.SelectedIndex = 0;
    }

    private void BulkEntryCal_Click(object sender, RoutedEventArgs e)
    {
        var ids = SelectedPeople();
        if (ids.Count == 0) { AppServices.Toast("Select one or more people in the table first."); return; }

        var win = new ManualEntryWindow(Array.Empty<PersonChoice>(), null, null, bulkCount: ids.Count) { Owner = Owner };
        if (win.ShowDialog() != true) return;

        int entries = 0;
        foreach (var id in ids)
        {
            foreach (var (label, at) in new[] { ("log in", win.LogIn), ("log off", win.LogOff) })
            {
                if (at is not { } moment) continue;
                if (AppServices.Store.AddManual(id, moment, $"{label}: {win.Note}")) entries++;
            }
        }
        AppServices.Log("Info", "Manual", $"Bulk manual entry added for {ids.Count} people ({entries} scan(s) written). Reason: {win.Note}");
        AppServices.Toast(Loc.F("Added {0} scan(s) for {1} people.", entries, ids.Count));
        AppServices.RaiseDataChanged();
    }

    private void BuildCalendar()
    {
        // Remember who is selected so the table can be rebuilt without losing the selection.
        foreach (var rv in CalGrid.SelectedItems.OfType<DataRowView>()) _selected.Add((uint)rv["Id"]);
        if (CalGrid.SelectedItems.Count == 0 && CalGrid.Items.Count > 0) { /* nothing selected: keep earlier choice */ }

        var ctx = AppServices.Context();
        int days = DateTime.DaysInMonth(_month.Year, _month.Month);
        var q = CalSearch.Text.Trim();

        var dt = new DataTable();
        dt.Columns.Add("Id", typeof(uint));
        dt.Columns.Add("Person", typeof(string));
        for (int d = 1; d <= days; d++) dt.Columns.Add("d" + d, typeof(string));

        foreach (var p in ctx.People.Where(p => p.Active)
                     .Where(p => q.Length == 0 || p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || p.Enroll.ToString().Contains(q))
                     .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var row = dt.NewRow();
            row[0] = p.Enroll;
            row[1] = p.Name;
            for (int d = 1; d <= days; d++)
            {
                var date = new DateTime(_month.Year, _month.Month, d);
                var shift = ctx.Shifts.For(date, p.Enroll);
                row[1 + d] = ctx.Holidays.ContainsKey(date) ? "H"
                    : ctx.Leaves.OnLeave(date, p.Enroll) is not null ? "L"
                    : shift is null ? ""
                    : shift.RunsOn(date) ? shift.Code : shift.Code.ToLowerInvariant();
            }
            dt.Rows.Add(row);
        }

        CalGrid.ItemsSource = dt.DefaultView;
        CalGrid.SelectedItems.Clear();
        foreach (var rv in CalGrid.Items.OfType<DataRowView>())
            if (_selected.Contains((uint)rv["Id"])) CalGrid.SelectedItems.Add(rv);

        var legend = string.Join("   ", ctx.Shifts.Shifts.OrderBy(s => s.Id).Select(s => $"{s.Code} = {s.Name}"));
        LegendText.Text = Loc.F("{0}   H = holiday   L = personal leave   lowercase = assigned, but the shift doesn't run that weekday   blank = no shift", legend);
    }

    private void CalGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName == "Id") { e.Cancel = true; return; }
        if (e.PropertyName == "Person")
        {
            e.Column.Header = Loc.T("PERSON");
            e.Column.Width = 210;
            return;
        }
        if (e.PropertyName.StartsWith('d') && int.TryParse(e.PropertyName.AsSpan(1), out var n) && e.Column is DataGridTextColumn col)
        {
            var date = new DateTime(_month.Year, _month.Month, n);
            col.Header = $"{n}{Environment.NewLine}{Loc.Day2(date)}";
            col.Width = 40;

            var cell = new Style(typeof(TextBlock));
            cell.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
            cell.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            col.ElementStyle = cell;

            var head = new Style(typeof(DataGridColumnHeader), (Style)FindResource(typeof(DataGridColumnHeader)));
            head.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            head.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 6, 0, 6)));
            col.HeaderStyle = head;
        }
    }

    private List<uint> SelectedPeople() => CalGrid.SelectedItems.OfType<DataRowView>().Select(r => (uint)r["Id"]).ToList();

    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyShift(clear: false);

    private void RemoveShifts_Click(object sender, RoutedEventArgs e) => ApplyShift(clear: true);

    private void ApplyShift(bool clear)
    {
        var ids = SelectedPeople();
        if (ids.Count == 0) { AppServices.Toast("Select one or more people in the table first."); return; }

        var shift = (ShiftBox.SelectedItem as ShiftItem)?.Def;
        if (!clear && shift is null) { AppServices.Toast("Choose a shift first."); return; }

        var from = (ApplyFrom.SelectedDate ?? _month).Date;
        var to = (ApplyTo.SelectedDate ?? _month.AddMonths(1).AddDays(-1)).Date;
        if (to < from) (from, to) = (to, from);

        var what = clear ? Loc.T("Remove all shifts from") : Loc.F("Put on the “{0}” shift", shift!.Name);
        var who = ids.Count == 1 ? Loc.T("1 person") : Loc.F("{0} people", ids.Count);
        if (Dlg.Show(Owner, Loc.Dates(Loc.F("{0}: {1}, from {2:d MMM yyyy} to {3:d MMM yyyy}?", what, who, from, to)), "Schedule",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        _selected = ids.ToHashSet();
        var written = AppServices.Store.AssignShift(ids, from, to, clear ? null : shift!.Id, OnlyShiftDays.IsChecked == true);
        AppServices.Log("Info", "Schedule", clear
            ? $"Removed shifts for {who} from {from:yyyy-MM-dd} to {to:yyyy-MM-dd} ({written} day(s))."
            : $"Assigned “{shift!.Name}” to {who} from {from:yyyy-MM-dd} to {to:yyyy-MM-dd} ({written} day(s)).");
        AppServices.RaiseDataChanged();
        AppServices.Toast(Loc.F("{0} day(s) updated.", written));
    }

    // ---------- shifts ----------

    private void LoadShifts()
    {
        var store = AppServices.Store;
        ShiftsGrid.ItemsSource = store.GetShifts().Select(s => new ShiftRow(
            s, s.Name, $"{s.Start:hh\\:mm} – {s.End:hh\\:mm}", s.DaysText,
            $"{s.LateEnterMinutes} min", $"{s.EarlyLeaveMinutes} min", $"{s.LateLeaveMinutes} min", s.BreakMinutes == 0 ? "—" : $"{s.BreakMinutes} min",
            store.CountAssignments(s.Id).ToString("N0", CultureInfo.InvariantCulture),
            s.Id < 1000 ? "Copied from original software" : "Created here")).ToList();
    }

    private void NewShift_Click(object sender, RoutedEventArgs e)
    {
        var win = new ShiftEditorWindow(null) { Owner = Owner };
        if (win.ShowDialog() != true) return;
        AppServices.Store.UpsertShift(win.Result);
        AppServices.Log("Info", "Schedule", $"Created shift “{win.Result.Name}” ({win.Result.Start:hh\\:mm}–{win.Result.End:hh\\:mm}).");
        AppServices.RaiseDataChanged();
    }

    private void EditShift_Click(object sender, RoutedEventArgs e)
    {
        if (ShiftsGrid.SelectedItem is not ShiftRow row) { if (sender is Button) AppServices.Toast("Select a shift first."); return; }
        var win = new ShiftEditorWindow(row.Def) { Owner = Owner };
        if (win.ShowDialog() != true) return;
        AppServices.Store.UpsertShift(win.Result);
        AppServices.Log("Info", "Schedule", $"Edited shift “{win.Result.Name}”.");
        AppServices.RaiseDataChanged();
        AppServices.Toast("Shift saved. Reports now use the new rules.");
    }

    private void DeleteShift_Click(object sender, RoutedEventArgs e)
    {
        if (ShiftsGrid.SelectedItem is not ShiftRow row) { AppServices.Toast("Select a shift first."); return; }
        var days = AppServices.Store.CountAssignments(row.Def.Id);
        var warning = days > 0 ? Loc.F("\n\n{0:N0} assigned day(s) will lose their shift.", days) : "";
        var origin = row.Def.Id < 1000 ? Loc.T("\n\nThis shift was copied from the original software and will come back if you import from it again.") : "";
        if (Dlg.Show(Owner, Loc.F("Delete the “{0}” shift?{1}{2}", row.Name, warning, origin), "Delete shift", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        AppServices.Store.DeleteShift(row.Def.Id);
        AppServices.Log("Warn", "Schedule", $"Deleted shift “{row.Name}” ({days} assigned day(s) cleared).");
        AppServices.RaiseDataChanged();
    }

    // ---------- leave ----------

    private void LoadLeave()
    {
        var ctx = AppServices.Context();
        var keep = (LeavePerson.SelectedItem as PersonChoice)?.Enroll;
        var people = ctx.ActivePeople.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new PersonChoice(p.Enroll, $"{p.Name} · {p.Enroll}")).ToList();
        LeavePerson.ItemsSource = people;
        LeavePerson.SelectedItem = people.FirstOrDefault(p => p.Enroll == keep);

        if (LeaveType.ItemsSource is null)
        {
            LeaveType.ItemsSource = LeaveTypes.Local.Select(t => new LeaveTypeItem(t.Type, Loc.T(t.Label))).ToList();
            LeaveType.SelectedIndex = 0;
        }
        LeaveFrom.SelectedDate ??= DateTime.Today;
        LeaveTo.SelectedDate ??= DateTime.Today;

        var rows = AppServices.Store.GetLeaves(DateTime.Today.AddDays(-120), DateTime.Today.AddDays(400))
            .Where(l => ctx.Allows(l.Enroll))
            .Select(l => new LeaveItem(
                l.Id, ctx.NameOf(l.Enroll) is { Length: > 0 } n ? n : $"ID {l.Enroll}",
                l.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), l.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ((l.End - l.Start).Days + 1).ToString(), l.Description,
                l.Source == "local" ? "Entered here" : "Copied from original software", l.Source == "local")).ToList();
        LeaveGrid.ItemsSource = rows;
        LeaveEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddLeave_Click(object sender, RoutedEventArgs e)
    {
        if (LeavePerson.SelectedItem is not PersonChoice person) { AppServices.Toast("Choose a person first."); return; }
        if (LeaveFrom.SelectedDate is not { } from || LeaveTo.SelectedDate is not { } to) { AppServices.Toast("Pick the first and last day."); return; }
        if (to < from) { AppServices.Toast("The last day can't be before the first day."); return; }
        if (LeaveType.SelectedItem is not LeaveTypeItem type) return;

        var note = LeaveNote.Text.Trim();
        var description = note.Length > 0 ? $"{type.Label} – {note}" : type.Label;
        AppServices.Store.AddLeave(person.Enroll, from.Date, to.Date, description, type.Type);
        AppServices.Log("Info", "Leave", $"Leave added for {person.Display}: {from:yyyy-MM-dd} to {to:yyyy-MM-dd} ({description}).");
        LeaveNote.Clear();
        AppServices.RaiseDataChanged();
        AppServices.Toast(Loc.F("{0} recorded for {1} day(s).", type.Label, (to - from).Days + 1));
    }

    private void DeleteLeave_Click(object sender, RoutedEventArgs e)
    {
        if (LeaveGrid.SelectedItem is not LeaveItem row) { AppServices.Toast("Select a leave entry first."); return; }
        if (!row.IsLocal) { AppServices.Toast("Leave copied from the original software can't be deleted here."); return; }
        if (Dlg.Show(Owner, Loc.F("Delete {0}'s leave from {1} to {2}?", row.Name, row.From, row.To), "Delete leave", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        AppServices.Store.DeleteLeave(row.Id);
        AppServices.Log("Warn", "Leave", $"Leave deleted: {row.Name} {row.From} to {row.To} ({row.Description}).");
        AppServices.RaiseDataChanged();
    }

    // ---------- holidays ----------

    private void LoadHolidays()
    {
        var rows = AppServices.Store.GetHolidays()
            .OrderByDescending(h => h.Key)
            .Select(h => new HolidayRow(h.Key, h.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Loc.Dates(h.Key.ToString("dddd", CultureInfo.InvariantCulture)), h.Value))
            .ToList();
        HolidayGrid.ItemsSource = rows;
        HolidayEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddHoliday_Click(object sender, RoutedEventArgs e)
    {
        if (HolidayDate.SelectedDate is not { } date || HolidayName.Text.Trim().Length == 0)
        {
            AppServices.Toast("Pick a date and give the holiday a name.");
            return;
        }
        AppServices.Store.SetHoliday(date, HolidayName.Text.Trim());
        AppServices.Log("Info", "Schedule", $"Holiday added: {date:yyyy-MM-dd} {HolidayName.Text.Trim()}.");
        HolidayName.Clear();
        AppServices.RaiseDataChanged();
    }

    private void RemoveHoliday_Click(object sender, RoutedEventArgs e)
    {
        if (HolidayGrid.SelectedItem is not HolidayRow row) { AppServices.Toast("Select a holiday first."); return; }
        AppServices.Store.DeleteHoliday(row.Date);
        AppServices.Log("Info", "Schedule", $"Holiday removed: {row.DateText} {row.Name}.");
        AppServices.RaiseDataChanged();
    }
}
