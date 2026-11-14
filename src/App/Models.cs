using System.Windows;
using System.Windows.Media;

namespace GozcuStandalone.App;

public static class VisualTreeExtensions
{
    // Walks up from a clicked element to find the DataGridCell (or other ancestor type) it was inside.
    public static T? FindAncestor<T>(this DependencyObject d) where T : DependencyObject
    {
        var current = d;
        while (current is not null && current is not T) current = VisualTreeHelper.GetParent(current);
        return current as T;
    }
}

public sealed record FeedRow(string Time, string Name, string Id, string Method, Brush MethodBack, Brush MethodFore, string Initial);

public sealed record DayRow(string Date, string Id, string Name, string Dept, string Shift, string In, string Out, string Worked, string Late, string Early, string Scans, DateTime DateValue, uint Enroll);

public sealed record ScanView(long Key, string When, string Id, string Name, string Method, string Note, string Status, bool IsManual, bool IsVoided, DateTime Time);

public sealed class PersonRow
{
    public uint Enroll { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "Active";
    public string Department { get; set; } = "";
}

public sealed record UnnamedItem(uint Enroll, string Label);

public sealed record NotInRow(string Name, string Shift);

public sealed record DeptItem(int? Id, string Name);
