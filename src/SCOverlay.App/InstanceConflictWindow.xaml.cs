using System.Windows;

namespace SCOverlay.App;

public partial class InstanceConflictWindow : Window
{
    public InstanceConflictWindow()
    {
        InitializeComponent();
        Choice = InstanceConflictChoice.FocusExisting;
    }

    public InstanceConflictChoice Choice { get; private set; }

    private void FocusExistingButton_OnClick(object sender, RoutedEventArgs e)
    {
        Choice = InstanceConflictChoice.FocusExisting;
        DialogResult = true;
    }

    private void StartAnotherButton_OnClick(object sender, RoutedEventArgs e)
    {
        Choice = InstanceConflictChoice.StartAnother;
        DialogResult = true;
    }

    private void QuitOldButton_OnClick(object sender, RoutedEventArgs e)
    {
        Choice = InstanceConflictChoice.QuitOldAndStart;
        DialogResult = true;
    }
}

public enum InstanceConflictChoice
{
    FocusExisting,
    StartAnother,
    QuitOldAndStart
}
