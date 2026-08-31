using System.Security;
using System.Windows;
using System.Windows.Input;

namespace RKSwitch.SynDrvCl;

public partial class PasswordPromptWindow : Window
{
    public bool ShouldSavePassword => SavePasswordCheckBox.IsChecked == true;

    public PasswordPromptWindow(string target, string transferMessage)
    {
        InitializeComponent();
        TargetText.Text = target;
        TransferText.Text = transferMessage;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public SecureString TakePassword()
    {
        var copy = PasswordInput.SecurePassword.Copy();
        copy.MakeReadOnly();
        PasswordInput.Clear();
        return copy;
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e) =>
        ContinueButton.IsEnabled = PasswordInput.SecurePassword.Length > 0;

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ContinueButton.IsEnabled) Continue_Click(sender, e);
    }

    private void Continue_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
