using System.Windows;

namespace GozcuStandalone.App;

public partial class BulkShiftWindow : Window
{
    public int ShiftId { get; private set; }
    public DateTime From { get; private set; }
    public DateTime To { get; private set; }
    public bool ClearShift { get; private set; }
    public bool OnlyShiftDays { get; private set; }

    public BulkShiftWindow(int peopleCount)
    {
        InitializeComponent();
        IntroText.Text = Loc.F("Choose a shift and a date range to apply to all {0} people you selected.", peopleCount);

        var items = AppServices.Store.GetShifts().Select(s => new ShiftItem(s, $"{s.Name}  ({s.Start:hh\\:mm}–{s.End:hh\\:mm})")).ToList();
        ShiftBox.ItemsSource = items;
        if (items.Count > 0) ShiftBox.SelectedIndex = 0;

        FromBox.SelectedDate = DateTime.Today;
        ToBox.SelectedDate = DateTime.Today;
    }

    private void ClearBox_Changed(object sender, RoutedEventArgs e) => ShiftBox.IsEnabled = ClearBox.IsChecked != true;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";

        var clear = ClearBox.IsChecked == true;
        var shift = (ShiftBox.SelectedItem as ShiftItem)?.Def;
        if (!clear && shift is null)
        {
            ErrorText.Text = Loc.T("Choose a shift first.");
            return;
        }

        if (FromBox.SelectedDate is not { } from || ToBox.SelectedDate is not { } to)
        {
            ErrorText.Text = Loc.T("Pick a date range.");
            return;
        }
        if (to < from) (from, to) = (to, from);

        ShiftId = shift?.Id ?? 0;
        From = from.Date;
        To = to.Date;
        ClearShift = clear;
        OnlyShiftDays = OnlyShiftDaysBox.IsChecked == true;
        DialogResult = true;
    }
}
