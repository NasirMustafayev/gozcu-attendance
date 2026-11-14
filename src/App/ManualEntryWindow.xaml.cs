using System.Globalization;
using System.Windows;

namespace GozcuStandalone.App;

public sealed record PersonChoice(uint Enroll, string Display);

public partial class ManualEntryWindow : Window
{
    public uint Enroll { get; private set; }
    public DateTime When { get; private set; }
    public string Note { get; private set; } = "";
    public DateTime? LogIn { get; private set; }
    public DateTime? LogOff { get; private set; }
    public bool Overwrite { get; private set; }

    private readonly List<PersonChoice> _people;
    private readonly bool _editing;

    // editing: the person is fixed and the date/time start from the scan being corrected.
    // canOverwrite: offers a checkbox to change the time directly instead of the audited void-and-replace.
    public ManualEntryWindow(IEnumerable<PersonChoice> people, uint? enroll, DateTime? date, bool editing = false, string? note = null, bool canOverwrite = false)
    {
        InitializeComponent();
        _editing = editing;
        _people = people.ToList();
        PersonBox.ItemsSource = _people;
        if (enroll is { } id) PersonBox.SelectedItem = _people.FirstOrDefault(p => p.Enroll == id);

        DateBox.SelectedDate = date?.Date ?? DateTime.Today;
        TimeBox.Text = editing && date is { } d ? d.ToString("HH:mm", CultureInfo.InvariantCulture)
            : (date ?? DateTime.Today) == DateTime.Today ? DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) : "09:00";
        if (editing)
        {
            Title = "Edit scan";
            HeadText.Text = "Edit scan";
            IntroText.Text = "Change the time of this scan. A device scan is never altered: it is voided and replaced by a manual entry, so the original stays in the audit list.";
            PersonBox.IsEnabled = false;
            SaveButton.Content = "Save changes";
            NoteBox.Text = note ?? "";
            TimeLabel.Text = "Time (HH:mm)";
            OutPanel.Visibility = Visibility.Collapsed;
            OutGap.Width = new GridLength(0);
            OutCol.Width = new GridLength(0);
            if (canOverwrite) OverwriteBox.Visibility = Visibility.Visible;
        }
        Loaded += (_, _) => (PersonBox.SelectedItem is null ? PersonBox : (UIElement)TimeBox).Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = Loc.T("");

        uint enroll;
        if (PersonBox.SelectedItem is PersonChoice p) enroll = p.Enroll;
        else if (!uint.TryParse(new string((PersonBox.Text ?? "").Where(char.IsDigit).ToArray()), out enroll))
        {
            ErrorText.Text = Loc.T("Choose a person from the list, or type their ID number.");
            return;
        }

        if (DateBox.SelectedDate is not { } date)
        {
            ErrorText.Text = Loc.T("Pick a date.");
            return;
        }

        static bool Parse(string text, out TimeSpan t) =>
            TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out t);

        TimeSpan inTime = default, outTime = default;
        bool hasIn = TimeBox.Text.Trim().Length > 0, hasOut = _editing ? false : OutBox.Text.Trim().Length > 0;
        if (!hasIn && !hasOut)
        {
            ErrorText.Text = Loc.T("Enter the log in time, the log off time, or both.");
            return;
        }
        if ((hasIn && !Parse(TimeBox.Text, out inTime)) || (hasOut && !Parse(OutBox.Text, out outTime)))
        {
            ErrorText.Text = Loc.T("Times should look like 09:30.");
            return;
        }
        if (hasIn && hasOut && outTime <= inTime)
        {
            ErrorText.Text = Loc.T("The log off time must be after the log in time.");
            return;
        }

        DateTime? login = hasIn ? date.Date + inTime : null, logoff = hasOut ? date.Date + outTime : null;
        if ((login ?? logoff)!.Value > DateTime.Now.AddMinutes(5) || (logoff ?? login)!.Value > DateTime.Now.AddMinutes(5))
        {
            ErrorText.Text = Loc.T("That moment is in the future.");
            return;
        }
        var when = login ?? logoff!.Value;

        var note = NoteBox.Text.Trim();
        if (note.Length == 0)
        {
            ErrorText.Text = Loc.T("Please write a short reason so the entry can be explained later.");
            return;
        }

        Enroll = enroll;
        When = when;
        LogIn = login;
        LogOff = logoff;
        Note = note;
        Overwrite = OverwriteBox.Visibility == Visibility.Visible && OverwriteBox.IsChecked == true;
        DialogResult = true;
    }
}
