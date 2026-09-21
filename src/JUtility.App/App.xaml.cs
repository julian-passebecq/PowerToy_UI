using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using JUtility.Core.Services;

namespace JUtility.App;

public partial class App : Application
{
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!TryResolveDataDirectory(e.Args, out string dataDirectory, out string? optionError))
        {
            MessageBox.Show(
                optionError,
                "Invalid Power Ops startup option",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        string defaultDataDirectory = GetDefaultDataDirectory();
        bool usesDefaultWorkspace = PathsEqual(dataDirectory, defaultDataDirectory);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: BuildWorkspaceMutexName(dataDirectory),
            createdNew: out bool createdNew);

        if (!createdNew)
        {
            bool activated = usesDefaultWorkspace && TryActivateExistingWindow();
            if (!activated)
            {
                MessageBox.Show(
                    $"Power Ops is already running for this workspace.\n\n{dataDirectory}\n\nUse the existing window or its configured summon shortcut.",
                    "Power Ops workspace already open",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            WorkspaceStore store = new(dataDirectory);
            MainWindow window = new(store);
            if (!usesDefaultWorkspace)
            {
                window.Title = $"J Utility Palette · Power Ops — {Path.GetFileName(dataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}";
            }

            MainWindow = window;
            window.Show();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
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
            // This process never acquired ownership, or ownership was already released.
        }
        finally
        {
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private static bool TryResolveDataDirectory(
        IReadOnlyList<string> args,
        out string dataDirectory,
        out string? error)
    {
        dataDirectory = GetDefaultDataDirectory();
        error = null;

        if (args.Count == 0)
        {
            return true;
        }

        if (args.Count != 2 || !args[0].Equals("--data-dir", StringComparison.OrdinalIgnoreCase))
        {
            error = "Supported syntax: JUtilityPalette.exe [--data-dir <folder>]";
            return false;
        }

        if (string.IsNullOrWhiteSpace(args[1]))
        {
            error = "--data-dir requires a non-empty folder path.";
            return false;
        }

        try
        {
            dataDirectory = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(args[1].Trim()));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"The --data-dir path is invalid.\n\n{ex.Message}";
            return false;
        }
    }

    private static string GetDefaultDataDirectory() =>
        Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JUtilityPalette"));

    private static string BuildWorkspaceMutexName(string dataDirectory)
    {
        string canonical = Path.GetFullPath(dataDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return @"Local\JUtilityPalette.Workspace." + Convert.ToHexString(digest);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

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
