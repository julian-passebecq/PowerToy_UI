using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace JUtility.App;

public partial class App : Application
{
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\JUtilityPalette.SingleInstance",
            createdNew: out bool createdNew);

        if (!createdNew)
        {
            if (!TryActivateExistingWindow())
            {
                MessageBox.Show(
                    "J Utility Palette is already running. Use the existing window or summon it with your configured mouse shortcut.",
                    "J Utility Palette",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            MainWindow window = new();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            string dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JUtilityPalette");

            MessageBox.Show(
                $"Power Ops could not open the local workspace.\n\n{ex.Message}\n\nNo reset was performed. Your workspace files were left in:\n{dataDirectory}",
                "Power Ops workspace could not be opened",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(2);
        }
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

    private static bool TryActivateExistingWindow()
    {
        IntPtr window = FindWindow(null, "J Utility Palette · Power Ops");
        if (window == IntPtr.Zero)
        {
            return false;
        }

        ShowWindow(window, SwShow);
        ShowWindow(window, SwRestore);
        SetForegroundWindow(window);
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
