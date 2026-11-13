using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace GozcuStandalone.App;

public partial class ShiftEditorWindow : Window
{
    private readonly int _id;
    private readonly List<CheckBox> _days = new();

    public ShiftDef Result { get; private set; } = null!;

    public ShiftEditorWindow(ShiftDef? existing)
    {
        InitializeComponent();
        _id = existing?.Id ?? 0;
        Heading.Text = existing is null ? "New shift" : "Edit shift";

        for (int i = 0; i < 7; i++)
        {
            var box = new CheckBox
            {
                Content = ShiftDef.DayNames[i],
                Margin = new Thickness(0, 0, 14, 4),
                IsChecked = existing is null ? i < 5 : (existing.DaysOfWeekMask & (1 << i)) != 0,
            };
            _days.Add(box);
            DaysPanel.Children.Add(box);
        }

        NameBox.Text = existing?.Name ?? "";
        StartBox.Text = (existing?.Start ?? new TimeSpan(9, 0, 0)).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        EndBox.Text = (existing?.End ?? new TimeSpan(18, 0, 0)).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        LateEnterBox.Text = (existing?.LateEnterMinutes ?? 5).ToString();
        EarlyEnterBox.Text = (existing?.EarlyEnterMinutes ?? 120).ToString();
        EarlyLeaveBox.Text = (existing?.EarlyLeaveMinutes ?? 0).ToString();
        LateLeaveBox.Text = (existing?.LateLeaveMinutes ?? 15).ToString();
        BreakBox.Text = (existing?.BreakMinutes ?? 0).ToString();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = Loc.T("");
        var name = NameBox.Text.Trim();
        if (name.Length == 0) { ErrorText.Text = Loc.T("Give the shift a name."); return; }

        var formats = new[] { @"h\:mm", @"hh\:mm" };
        if (!TimeSpan.TryParseExact(StartBox.Text.Trim(), formats, CultureInfo.InvariantCulture, out var start)
            || !TimeSpan.TryParseExact(EndBox.Text.Trim(), formats, CultureInfo.InvariantCulture, out var end))
        {
            ErrorText.Text = Loc.T("Start and end should look like 09:00.");
            return;
        }
        if (end <= start) { ErrorText.Text = Loc.T("The shift must end after it starts (overnight shifts are not supported)."); return; }

        if (!TryMinutes(LateEnterBox, out var lateEnter) || !TryMinutes(EarlyEnterBox, out var earlyEnter)
            || !TryMinutes(EarlyLeaveBox, out var earlyLeave) || !TryMinutes(LateLeaveBox, out var lateLeave))
        {
            ErrorText.Text = Loc.T("Grace times must be whole numbers of minutes, 0 or more.");
            return;
        }

        if (!TryMinutes(BreakBox, out var breakMinutes) || breakMinutes > 240)
        {
            ErrorText.Text = Loc.T("The unpaid break must be between 0 and 240 minutes.");
            return;
        }

        int mask = 0;
        for (int i = 0; i < 7; i++) if (_days[i].IsChecked == true) mask |= 1 << i;
        if (mask == 0) { ErrorText.Text = Loc.T("Choose at least one working day."); return; }

        Result = new ShiftDef(_id, name, start, end, earlyEnter, lateEnter, earlyLeave, lateLeave, mask, breakMinutes);
        DialogResult = true;
    }

    private static bool TryMinutes(TextBox box, out int minutes) =>
        int.TryParse(box.Text.Trim(), out minutes) && minutes >= 0 && minutes <= 1440;
}
