using System;
using System.IO;
using System.Windows;

namespace TenzoraX;

public partial class App : System.Windows.Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                LogCrash("AppDomain", ex);
        };

        DispatcherUnhandledException += (s, e) =>
        {
            LogCrash("Dispatcher", e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            LogCrash("OnStartup", ex);
            System.Windows.MessageBox.Show(
                $"TenzoraX konnte nicht gestartet werden.\n\n{ex.GetType().Name}: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                "Startfehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static void LogCrash(string context, Exception ex)
    {
        try
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string logPath = Path.Combine(docs, "TenzoraX", "crash.log");
            string? dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex.Message}\n{ex.StackTrace}\n";
            File.AppendAllText(logPath, entry);
        }
        catch { }
    }
}
