using System.Windows;

namespace GozcuStandalone.App;

// MessageBox with the interface language applied to its text and title.
public static class Dlg
{
    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon) =>
        MessageBox.Show(Loc.T(text), Loc.T(caption), buttons, icon);

    public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxImage icon) =>
        owner is null
            ? MessageBox.Show(Loc.T(text), Loc.T(caption), buttons, icon)
            : MessageBox.Show(owner, Loc.T(text), Loc.T(caption), buttons, icon);
}
