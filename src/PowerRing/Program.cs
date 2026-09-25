using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using PowerRing.Core;

namespace PowerRing;

// PowerRing.exe [--config <ring.json>] [--show] [--profile <1-5>] [--exit]
// One instance per ring.json. A second start forwards --show / --profile / --exit to the running one and quits, so a
// launcher, a script or Logi Options+ "Run program" can drive the ring without a hotkey.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string configPath = RingConfigStore.DefaultPath();
        bool show = false, exit = false;
        int? profile = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--config" when i + 1 < args.Length: configPath = Path.GetFullPath(args[++i]); break;
                case "--show": show = true; break;
                case "--exit": exit = true; break;
                case "--profile" when i + 1 < args.Length && int.TryParse(args[i + 1], out int p): profile = p - 1; i++; break;
            }
        }

        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configPath.ToLowerInvariant())))[..16];
        using var mutex = new Mutex(true, $@"Local\PowerRing-{key}", out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\PowerRing-{key}-show");
        using var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\PowerRing-{key}-exit");
        using var profileSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\PowerRing-{key}-profile");
        string profileFile = Path.Combine(Path.GetTempPath(), $"PowerRing-{key}.profile");

        if (!first)
        {
            // The running instance may take the foreground only because this (user-started) process allows it.
            foreach (var running in System.Diagnostics.Process.GetProcessesByName("PowerRing")) { Native.AllowSetForegroundWindow(running.Id); running.Dispose(); }
            if (profile is int index) { File.WriteAllText(profileFile, index.ToString()); profileSignal.Set(); }
            if (exit) exitSignal.Set();
            else if (show || profile is null) showSignal.Set();
            return 0;
        }
        if (exit) return 0;

        // Software rendering: the ring is a few circles, and skipping Direct3D keeps the GPU drivers (100+ MB) out of this
        // always-running process.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        RingHost? host = null;
        app.Startup += (_, _) =>
        {
            host = new RingHost(new RingConfigStore(configPath), app.Dispatcher);
            if (profile is int index) host.SetProfile(index);
            if (show) host.ShowRing();
            // Three blocked waits (no polling): --show, --exit and --profile from later starts.
            var listener = new Thread(() =>
            {
                WaitHandle[] handles = [showSignal, exitSignal, profileSignal];
                while (true)
                {
                    int which = WaitHandle.WaitAny(handles);
                    if (which == 1) { app.Dispatcher.BeginInvoke(() => app.Shutdown()); return; }
                    if (which == 2 && int.TryParse(ReadQuietly(profileFile), out int p)) app.Dispatcher.BeginInvoke(() => host!.SetProfile(p));
                    if (which == 0) app.Dispatcher.BeginInvoke(() => host!.ShowRing());
                }
            }) { IsBackground = true, Name = "PowerRing signals" };
            listener.Start();
        };
        app.Exit += (_, _) => host?.Dispose();
        return app.Run();
    }

    private static string? ReadQuietly(string path)
    {
        try { return File.ReadAllText(path); } catch (IOException) { return null; }
    }
}
