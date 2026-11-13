using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace GozcuStandalone.App;

// Translates the text written into the XAML pages when a window opens.
public static class LocUi
{
    public static void Register()
    {
        if (!Loc.IsAz) return;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => Visit((DependencyObject)s)));
    }

    private static void Visit(DependencyObject d)
    {
        switch (d)
        {
            case Window w: w.Title = Loc.T(w.Title); break;
            case TextBlock tb when !BindingOperations.IsDataBound(tb, TextBlock.TextProperty): tb.Text = Loc.T(tb.Text); break;
            case ComboBoxItem: break;
            case MenuItem mi: if (mi.Header is string mh) mi.Header = Loc.T(mh); break;
            case TabItem ti: if (ti.Header is string th) ti.Header = Loc.T(th); break;
            case ContentControl cc when cc.Content is string text: cc.Content = Loc.T(text); break;
        }

        if (d is FrameworkElement fe)
        {
            if (fe.ToolTip is string tip) fe.ToolTip = Loc.T(tip);
            if (fe.ContextMenu is { } menu) foreach (var item in menu.Items.OfType<DependencyObject>()) Visit(item);
        }
        if (d is DataGrid grid)
            foreach (var col in grid.Columns)
                if (col.Header is string h) col.Header = Loc.T(h);

        foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Visit(child);
    }
}
