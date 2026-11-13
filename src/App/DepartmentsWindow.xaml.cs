using System.Windows;

namespace GozcuStandalone.App;

public sealed record DepartmentRow(int Id, string Name, int People);

public partial class DepartmentsWindow : Window
{
    public DepartmentsWindow()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        var counts = AppServices.Store.GetPersonDepartments().GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Count());
        DeptGrid.ItemsSource = AppServices.Store.GetDepartments()
            .Select(d => new DepartmentRow(d.Id, d.Name, counts.GetValueOrDefault(d.Id))).ToList();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var prompt = new PromptWindow("New department", "What should it be called?", "Add department") { Owner = this };
        if (prompt.ShowDialog() != true) return;
        if (AppServices.Store.GetDepartments().Any(d => string.Equals(d.Name, prompt.Text, StringComparison.CurrentCultureIgnoreCase)))
        {
            AppServices.Toast("A department with that name already exists.");
            return;
        }
        AppServices.Store.AddDepartment(prompt.Text);
        AppServices.Log("Info", "People", $"Department “{prompt.Text}” created.");
        Reload();
        AppServices.RaiseDataChanged();
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (DeptGrid.SelectedItem is not DepartmentRow row) { if (sender is System.Windows.Controls.Button) AppServices.Toast("Select a department first."); return; }
        var prompt = new PromptWindow("Rename department", Loc.F("New name for “{0}”:", row.Name), "Rename", initial: row.Name) { Owner = this };
        if (prompt.ShowDialog() != true) return;
        AppServices.Store.RenameDepartment(row.Id, prompt.Text);
        AppServices.Log("Info", "People", $"Department “{row.Name}” renamed to “{prompt.Text}”.");
        Reload();
        AppServices.RaiseDataChanged();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (DeptGrid.SelectedItem is not DepartmentRow row) { AppServices.Toast("Select a department first."); return; }
        var people = row.People > 0 ? Loc.F("\n\n{0} people in it will have no department.", row.People) : "";
        if (Dlg.Show(this, Loc.F("Delete the “{0}” department?{1}", row.Name, people), "Delete department", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        if (AppServices.DepartmentFilter == row.Id) AppServices.DepartmentFilter = null;
        AppServices.Store.DeleteDepartment(row.Id);
        AppServices.Log("Warn", "People", $"Department “{row.Name}” deleted ({row.People} people unassigned).");
        Reload();
        AppServices.RaiseDataChanged();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
