using System.IO;
using System.Windows;
using GozcuStandalone.Reporting;
using Microsoft.Win32;

namespace GozcuStandalone.App;

public static class ExportHelper
{
    // Asks where to save, then writes Excel (.xlsx) or CSV depending on the chosen name.
    public static void Save(Window? owner, ReportTable table, string baseName)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx|CSV file (*.csv)|*.csv",
            FileName = $"{baseName}_{DateTime.Today:yyyy-MM-dd}.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
        };
        if (dlg.ShowDialog(owner) != true) return;

        try
        {
            table.Save(dlg.FileName);
            AppServices.Toast(Loc.F("Saved {0}", Path.GetFileName(dlg.FileName)));
            AppServices.Log("Info", "Export", $"Exported “{(table.Title.Length > 0 ? table.Title : baseName)}” ({table.Rows.Count} rows) to {Path.GetFileName(dlg.FileName)}.");
        }
        catch (IOException ex)
        {
            Dlg.Show(owner, ex.Message + Loc.T("\n\nIf the file is open in Excel, close it and try again."), "Could not save", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static string FileName(string title) => new string(title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
}
