using System.Windows;
namespace CommunityFrontier.Launcher;
public partial class MainWindow : Window
{
    private readonly System.Windows.Threading.DispatcherTimer accountTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    public MainWindow()
    {
        InitializeComponent();
        accountTimer.Tick += async (_, _) => { if (DataContext is MainViewModel vm) await vm.RefreshAccountOnReturnAsync(); };
        Activated += async (_, _) => { if (DataContext is MainViewModel vm) await vm.RefreshAccountOnReturnAsync(); };
        Loaded += (_, _) => accountTimer.Start();
        Closed += (_, _) => { accountTimer.Stop(); if (DataContext is MainViewModel vm) vm.Close(); };
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel { IsBusy: true })
        { e.Cancel = true; MessageBox.Show("Please wait for the current operation, or cancel the search before closing."); }
        base.OnClosing(e);
    }
}
