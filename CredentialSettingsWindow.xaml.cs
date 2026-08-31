using System.Windows;

namespace RKSwitch.SynDrvCl;

public partial class CredentialSettingsWindow : Window
{
    public bool Changed { get; private set; }

    public CredentialSettingsWindow()
    {
        InitializeComponent();
        RefreshState();
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void RefreshState()
    {
        var exists = WindowsCredentialStore.Exists;
        CredentialStateText.Text = exists
            ? "Heslo je bezpečně uložené ve Windows. Zadáním nového je nahradíte."
            : "Heslo zatím není uložené. Zadejte je jednou pro automatické přepínání.";
        DeleteButton.IsEnabled = exists;
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e) =>
        SaveButton.IsEnabled = PasswordInput.SecurePassword.Length > 0;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WindowsCredentialStore.Save(PasswordInput.SecurePassword);
            PasswordInput.Clear();
            Changed = true;
            AppLog.Info("Heslo bylo uloženo do Správce přihlašovacích údajů Windows; hodnota nebyla logována.");
            RefreshState();
            MessageBox.Show("Heslo bylo bezpečně uloženo.", "Správa hesla", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("Uložení hesla do Windows selhalo", ex);
            MessageBox.Show($"Chyba: {ex.Message}", "Správa hesla", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Opravdu chcete uložené heslo odstranit? Při příštím živém přepnutí je aplikace znovu vyžádá.",
                "Smazat heslo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            WindowsCredentialStore.Delete();
            Changed = true;
            AppLog.Info("Uložené heslo bylo odstraněno ze Správce přihlašovacích údajů Windows.");
            RefreshState();
        }
        catch (Exception ex)
        {
            AppLog.Error("Odstranění uloženého hesla selhalo", ex);
            MessageBox.Show($"Chyba: {ex.Message}", "Správa hesla", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
