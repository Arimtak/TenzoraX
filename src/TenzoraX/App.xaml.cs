using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TenzoraX;

public partial class App : System.Windows.Application
{
    private static readonly Mutex _instanceMutex = new(false, "TenzoraX-{3F2C5B1A-9E8D-4A7C-B6F3-2D1E0C8A5B4F}");
    private static bool _ownsMutex;

    private static readonly EventWaitHandle _activateSignal = new EventWaitHandle(
        false, EventResetMode.AutoReset, "TenzoraX-Activate-{3F2C5B1A-9E8D-4A7C-B6F3-2D1E0C8A5B4F}");

    internal static EventWaitHandle ActivateSignal => _activateSignal;

    private static string LogsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "TenzoraX", "Logs");

    private static string CrashLogPath => Path.Combine(LogsDir, "crash.log");

    internal static bool HasCrashLog { get; private set; }

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                // Ignore CRT shutdown noise from single-file publish
                if (ex is DllNotFoundException &&
                    ex.StackTrace != null &&
                    (ex.StackTrace.Contains("__std_type_info_destroy_list") ||
                     ex.StackTrace.Contains("__scrt_uninitialize_type_info") ||
                     ex.StackTrace.Contains("_app_exit_callback")))
                    return;

                LogCrash("AppDomain", ex);
                LogApp("FATAL: AppDomain crash – Prozess wird beendet");
            }
        };

        DispatcherUnhandledException += (s, e) =>
        {
            LogCrash("Dispatcher", e.Exception);
            LogApp("ERROR: Dispatcher crash: " + e.Exception.Message);
            try
                {
                    System.Windows.MessageBox.Show(
                        $"TenzoraX hat einen Fehler festgestellt und wird fortgesetzt.\n\n" +
                        $"Fehler: {e.Exception.Message}\n\n" +
                        $"Details wurden in crash.log gespeichert.",
                        "TenzoraX – Fehler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            catch { }
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogCrash("TaskScheduler", e.Exception);
            LogApp("ERROR: Unobserved Task exception: " + e.Exception.Message);
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _ownsMutex = _instanceMutex.WaitOne(TimeSpan.Zero, false);
        if (!_ownsMutex)
        {
            LogApp("Zweite Instanz erkannt – signalisiere vorhandene Instanz");
            try { _activateSignal.Set(); } catch { }
            Shutdown();
            return;
        }

        // Start listener thread for activation signals from second instances
        var listener = new Thread(() =>
        {
            while (_ownsMutex)
            {
                try
                {
                    _activateSignal.WaitOne();
                    Current.Dispatcher.BeginInvoke(() =>
                    {
                        if (Current.MainWindow != null)
                        {
                            Current.MainWindow.Show();
                            Current.MainWindow.WindowState = WindowState.Normal;
                            Current.MainWindow.Activate();
                        }
                    });
                }
                catch { }
            }
        });
        listener.IsBackground = true;
        listener.Start();

        try
        {
            EnsureLogsDir();
            HasCrashLog = File.Exists(CrashLogPath);
            CleanupOldSessionLock();
            LogApp("TenzoraX gestartet, Version=" + AppVersion.Current);
            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            LogCrash("OnStartup", ex);
            System.Windows.MessageBox.Show(
                $"TenzoraX konnte nicht gestartet werden.\n\n{ex.GetType().Name}: {ex.Message}",
                "Startfehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        try { _activateSignal.Set(); } catch { }
        _activateSignal.Dispose();
        if (_ownsMutex)
        {
            _ownsMutex = false;
            try { _instanceMutex.ReleaseMutex(); } catch { }
        }
        _instanceMutex.Dispose();
    }

    internal static void DeleteCrashLog()
    {
        try
        {
            if (File.Exists(CrashLogPath))
                File.Delete(CrashLogPath);
        }
        catch { }
    }

    private static void CleanupOldSessionLock()
    {
        try
        {
            string oldLock = Path.Combine(LogsDir, "session.lock");
            if (File.Exists(oldLock))
                File.Delete(oldLock);
        }
        catch { }
    }

    public static void LogApp(string message)
    {
        try
        {
            EnsureLogsDir();
            string path = Path.Combine(LogsDir, "app.log");
            string entry = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            File.AppendAllText(path, entry);
        }
        catch { }
    }

    public static void LogCrash(string subsystem, Exception ex)
    {
        try
        {
            EnsureLogsDir();
            string path = Path.Combine(LogsDir, "crash.log");
            string entry = $"=== Crash [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===\n" +
                           $"Version:    {AppVersion.Current}\n" +
                           $"Subsystem:  {subsystem}\n" +
                           $"Type:       {ex.GetType().FullName}\n" +
                           $"Message:    {ex.Message}\n" +
                           $"StackTrace:\n{ex.StackTrace}\n";
            if (ex.InnerException != null)
                entry += $"InnerException:\n  {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n  {ex.InnerException.StackTrace}\n";
            entry += "\n";
            File.AppendAllText(path, entry);
        }
        catch { }
    }

    private static void EnsureLogsDir()
    {
        if (!Directory.Exists(LogsDir))
            Directory.CreateDirectory(LogsDir);
    }
}
