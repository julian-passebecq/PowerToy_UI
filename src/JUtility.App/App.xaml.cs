using System.Threading;
using System.Windows;

namespace JUtility.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\JUtilityPalette.SingleInstance",
            createdNew: out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "J Utility Palette is already running. Use the existing window or summon it with your configured mouse shortcut.",
                "J Utility Palette",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Ownership was already released during abnormal shutdown.
        }
        finally
        {
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }
}
