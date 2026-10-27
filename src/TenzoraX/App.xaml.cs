using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TenzoraX;

public partial class App : System.Windows.Application
{
    private static readonly Mutex _instanceMutex = new(false, "TenzoraX-{3F2C5B1A-9E8D-4A7C-B6F3-2D1E0C8A5B4F}");
    private static bool _ownsMutex;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    private static readonly EventWaitHandle _activateSignal = new EventWaitHandle(
        false, EventResetMode.AutoReset, "TenzoraX-Activate-{3F2C5B1A-9E8D-4A7C-B6F3-2D1E0C8A5B4F}");

    internal static EventWaitHandle ActivateSignal => _activateSignal;

    /// <summary>
    /// Gibt die Einzelinstanz-Mutex explizit frei, bevor der Prozess per
    /// Environment.Exit beendet wird (z.B. beim Neustart als Administrator).
    /// Environment.Exit läuft NICHT durch OnExit – ohne diese Freigabe würde die
    /// neue Instanz auf eine „abandoned“ Mutex treffen.
    /// </summary>
    internal static void ReleaseInstanceMutex()
    {
        if (_ownsMutex)
        {
            _ownsMutex = false;
            try { _instanceMutex.ReleaseMutex(); } catch { }
        }
    }

    private static string LogsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "TenzoraX", "Logs");

    private static string CrashLogPath => Path.Combine(LogsDir, "crash.log");

    internal static bool HasCrashLog { get; private set; }

    public App()
    {
        // Tablet/Stylus-Subsystem von WPF abschalten: Streaming-Apps (virtuelle
        // Tablet-Geräte) lösen sonst DllNotFoundException aus (PenImc.dll fehlt).
        AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.DisableStylusAndTouchSupport", true);

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
            // Benigne WPF-Stylus/Tablet-Fehler (PenImc-DLL fehlt) ohne Dialog ignorieren
            if (IsBenignPenimcError(e.Exception))
            {
                LogApp("WARN: Stylus/Penimc-Fehler ignoriert: " + e.Exception.Message);
                e.Handled = true;
                return;
            }

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

        // Beim Prozessende alle gehaltenen Tasten loslassen – auch bei Environment.Exit
        // (Neustart als Administrator, Update), das nicht durch OnExit läuft. Eine per
        // SendInput gedrückte Taste – besonders die eines Umschalters – bliebe sonst im
        // ganzen System hängen, bis sie jemand physisch drückt.
        AppDomain.CurrentDomain.ProcessExit += (s, e) => ProfileManager.ReleaseAll();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Diagnose-Modus: TenzoraX.exe --diagnose  (Bericht in Konsole + diagnostics.txt)
        if (e.Args != null && HasArg(e.Args, "--diagnose"))
        {
            RunDiagnosticsCli();
            Shutdown();
            return;
        }

        // Mutex übernehmen. WICHTIG: Eine „abandoned“ Mutex (Vorbesitzer starb ohne
        // Freigabe, z.B. Neustart als Administrator per Environment.Exit) wirft beim
        // WaitOne eine AbandonedMutexException – dann gehört sie uns und wir machen
        // normal weiter, statt als „Dispatcher-Crash“ zu enden.
        try { _ownsMutex = _instanceMutex.WaitOne(TimeSpan.Zero, false); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        if (!_ownsMutex)
        {
            // Neustart als Administrator (--admin-restart): Die alte Instanz hat
            // diese Instanz gestartet und beendet sich unmittelbar danach. Kurz
            // warten, bis die Mutex frei wird – sonst würde sich die neue Instanz
            // fälschlich als „zweite Instanz“ beenden (Race: beide Prozesse sterben
            // und am Ende läuft gar keine TenzoraX-Instanz mehr).
            if (e.Args != null && HasArg(e.Args, "--admin-restart"))
            {
                for (int i = 0; i < 20 && !_ownsMutex; i++)
                {
                    Thread.Sleep(250);
                    try { _ownsMutex = _instanceMutex.WaitOne(TimeSpan.Zero, false); }
                    catch (AbandonedMutexException) { _ownsMutex = true; }
                }
            }
            if (!_ownsMutex)
            {
                LogApp("Zweite Instanz erkannt – signalisiere vorhandene Instanz");
                try { _activateSignal.Set(); } catch { }
                Shutdown();
                return;
            }
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

            // Das Hauptfenster wird NICHT automatisch per StartupUri angezeigt,
            // sondern von uns selbst erstellt: Bei Autostart (--minimized) wird es
            // sofort versteckt, BEVOR die erste Bildschirmausgabe stattfindet. Das
            // Verstecken im Loaded-Event der Fensterklasse käme erst NACH dem ersten
            // Zeichnen – genau das verursachte das sichtbare Aufblitzen beim Start.
            bool startMinimized = e.Args != null && HasArg(e.Args, "--minimized")
                                  || IsStartMinimizedSetting();
            base.OnStartup(e);
            ShowMainWindow(startMinimized);
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

    /// <summary>
    /// Liest die Einstellung „StartMinimized“ aus config.json, damit auch ein
    /// Start ohne --minimized-Argument (z.B. Doppelklick aufs Programm) ohne
    /// sichtbares Aufblitzen direkt in den Tray geht.
    /// </summary>
    private static bool IsStartMinimizedSetting()
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TenzoraX", "config.json");
            if (File.Exists(path))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("StartMinimized", out var el) &&
                    el.ValueKind == System.Text.Json.JsonValueKind.True)
                    return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Erzeugt und zeigt das Hauptfenster. Bei startMinimized (Autostart) wird es
    /// VOR der ersten Bildschirmausgabe in den Tray versteckt: Das Fenster wird
    /// minimiert und ohne Taskleisten-Eintrag erzeugt, so dass nie ein Frame
    /// gezeichnet wird. Das Loaded-Event der Fensterklasse (Controller-Init,
    /// RawInput-Registrierung usw.) feuert trotzdem – die App ist voll funktionsfähig.
    /// </summary>
    private void ShowMainWindow(bool startMinimized)
    {
        var main = new MainWindow();
        MainWindow = main;
        if (startMinimized)
        {
            // Minimiert + ohne Taskleisten-Eintrag erzeugen: Das Fenster wird damit
            // nie sichtbar gerendert (kein Aufblitzen), Loaded feuert trotzdem.
            try
            {
                main.ShowInTaskbar = false;
                main.WindowState = WindowState.Minimized;
                main.Show();
                main.Hide();
                // ShowInTaskbar NICHT zurücksetzen: Sonst erscheint das minimierte
                // Fenster als Icon in der Taskleiste („minimiert“) statt nur im Tray.
                // Erst beim Wiederherstellen (RestoreFromTray) wird es wieder gesetzt.
                LogApp("Autostart-Modus: Fenster direkt in den Tray versteckt (ohne Anzeige).");
            }
            catch { }
        }
        else
        {
            main.Show();
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

    private static bool HasArg(string[] args, string name)
    {
        foreach (var a in args)
            if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Diagnose-CLI: führt die Mehr-API-Erkennung aus, schreibt den Bericht in die
    /// Konsole und nach Dokumente\TenzoraX\diagnostics.txt. Beendet sich danach.
    /// </summary>
    private static void RunDiagnosticsCli()
    {
        string report;
        try
        {
            report = ControllerManager.Instance.BuildDiagnosticsReport();
        }
        catch (Exception ex)
        {
            LogCrash("Diagnose", ex);
            TryConsoleLine("Diagnose fehlgeschlagen: " + ex.Message);
            return;
        }

        // Bericht immer speichern – unabhängig davon, ob eine Konsole existiert
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TenzoraX");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "diagnostics.txt");
            File.WriteAllText(path, report);
            LogApp("Diagnosebericht erstellt: " + path);
            TryConsoleLine(report);
            TryConsoleLine("");
            TryConsoleLine("Bericht gespeichert: " + path);
        }
        catch (Exception ex)
        {
            LogCrash("Diagnose-Speichern", ex);
            TryConsoleLine(report);
            TryConsoleLine("Speichern fehlgeschlagen: " + ex.Message);
        }
    }

    /// <summary>Konsolenausgabe, falls eine Konsole angehängt ist (GUI-App: sonst still).</summary>
    private static void TryConsoleLine(string text)
    {
        try
        {
            if (!Console.IsOutputRedirected)
                AttachConsole(ATTACH_PARENT_PROCESS);
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(text);
        }
        catch { }
    }

    private static bool IsBenignPenimcError(Exception ex)
    {
        if (ex is not DllNotFoundException) return false;
        string? st = ex.StackTrace;
        return st != null &&
            (st.Contains("MS.Win32.Penimc") ||
             st.Contains("PenThreadWorker") ||
             st.Contains("StylusWisp") ||
             st.Contains("WispTabletDeviceCollection") ||
             st.Contains("WispLogic"));
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
