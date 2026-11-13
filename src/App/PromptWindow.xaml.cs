using System.Windows;

namespace GozcuStandalone.App;

public partial class PromptWindow : Window
{
    private readonly bool _required;

    public string Text { get; private set; } = "";

    public PromptWindow(string heading, string detail, string okText = "OK", bool required = true, string initial = "")
    {
        InitializeComponent();
        Heading.Text = heading;
        Detail.Text = detail;
        OkButton.Content = okText;
        Input.Text = initial;
        _required = required;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var text = Input.Text.Trim();
        if (_required && text.Length == 0)
        {
            ErrorText.Text = Loc.T("This can't be empty.");
            return;
        }
        Text = text;
        DialogResult = true;
    }
}
