using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace TenzoraX;

public partial class App : System.Windows.Application
{
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
