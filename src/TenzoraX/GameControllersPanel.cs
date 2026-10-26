using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TenzoraX.Detection;

namespace TenzoraX
{
    /// <summary>
    /// Öffnet das Windows-Panel „Gamecontroller“ (joy.cpl) kontrolliert.
    ///
    /// Warum nicht einfach Process.Start("joy.cpl"):
    /// - Das Panel baut seine Liste genau einmal beim Öffnen auf. Wird es während eines
    ///   Reconnects geöffnet, bleibt die Liste leer, und der Aufruf kann minutenlang in
    ///   einem unsichtbaren Fenster hängen. Windows protokolliert das als „Application
    ///   Hang“ für rundll32.exe – der Prozess führt dabei ein verstecktes Fenster der
    ///   Klasse „RunDLL“ (gemessen 26.10.2026, PID 1152).
    /// - Ein bereits offenes Panel behält seine alte Liste; nach einem Ab- und Anstecken
    ///   fehlt der Controller dort, bis das Fenster neu aufgebaut wird.
    ///
    /// Deshalb: warten, bis die Geräteliste ruhig ist, vorhandene Panels schließen,
    /// neu öffnen, Sichtbarkeit prüfen und einmal wiederholen. Ein hängendes Panel wird
    /// so erkannt und gemeldet, statt den Nutzer mit einem unsichtbaren RunDLL-Fenster
    /// allein zu lassen.
    /// </summary>
    public static class GameControllersPanel
    {
        private const int WmClose = 0x0010;
        private const int WmNull = 0x0000;
        private const uint SmtoAbortIfHung = 0x0002;

        /// <summary>So lange muss die Controller-Liste unverändert bleiben, bevor geöffnet wird.</summary>
        private const int QuietMs = 1500;

        /// <summary>Längste Wartezeit auf eine ruhige Liste.</summary>
        private const int MaxQuietWaitMs = 6000;

        /// <summary>Zeit, die das Panel nach dem Start sichtbar werden darf.</summary>
        private const int VisibleTimeoutMs = 6000;

        private const int RetryDelayMs = 500;
        private const int PollMs = 200;

        /// <summary>So lange darf auf das Ende einer laufenden DirectInput-Aufzählung gewartet werden.</summary>
        private const int PanelGateTimeoutMs = 4000;

        /// <summary>Pause zwischen zwei Versuchen, die DirectInput-Schnittstelle zu bekommen.</summary>
        private const int PanelGateRetryMs = 1500;

        // Wächter: prüft jede Sekunde, ob ein geöffnetes Panel noch antwortet.
        private static System.Threading.Timer? _watchdog;
        private static readonly object WatchLock = new();
        private static readonly List<DateTime> RecoveryTimes = new();
        private static int _watchedPid;
        private static DateTime _watchedSinceUtc;
        private static int _unresponsiveTicks;

        // Cache für die DirectInput-Pause (der Rescan fragt alle 300 ms).
        private static readonly object VisibleLock = new();
        private static DateTime _visibleCheckedAt = DateTime.MinValue;
        private static bool _visibleCache;

        /// <summary>Ein gefundenes Panel-Fenster (auch ein verstecktes, also hängendes).</summary>
        public sealed class PanelWindow
        {
            public IntPtr Handle { get; init; }
            public int ProcessId { get; init; }
            public bool Visible { get; init; }
            public string Title { get; init; } = "";
        }

        /// <summary>Ergebnis des Öffnens – der Text ist für eine Meldung im Hauptfenster gedacht.</summary>
        public sealed class PanelOpenResult
        {
            public bool Success { get; }
            public int Attempts { get; }
            public string ErrorMessage { get; }

            public PanelOpenResult(bool success, int attempts, string errorMessage)
            {
                Success = success;
                Attempts = attempts;
                ErrorMessage = errorMessage;
            }
        }

        /// <summary>
        /// Erkennt das Panel an Fensterklasse und Titel. Nur echte Dialogfenster (#32770)
        /// zählen – ein beliebiges Fenster mit „Gamecontroller“ im Titel wäre zu unscharf.
        /// </summary>
        public static bool IsPanelWindow(string? className, string? title)
        {
            if (!string.Equals(className, "#32770", StringComparison.OrdinalIgnoreCase)) return false;
            string t = (title ?? "").Trim();
            return string.Equals(t, "Gamecontroller", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "Game Controllers", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True, solange auf die ruhige Geräteliste gewartet werden soll: Es gab kürzlich
        /// eine Änderung und die Wartezeit ist noch nicht abgelaufen.
        /// </summary>
        public static bool ShouldWaitForQuiet(int sinceChangeMs, int waitedMs,
            int quietMs = QuietMs, int maxWaitMs = MaxQuietWaitMs)
        {
            return waitedMs < maxWaitMs && sinceChangeMs < quietMs;
        }

        /// <summary>
        /// True, wenn ein Panel-Fenster nach dem Schließen noch steht und entweder nie
        /// sichtbar wurde oder auf Nachrichten nicht mehr antwortet: Das ist der hängende
        /// rundll32 („RunDLL“). Beendet wird er nur, wenn der Prozess keine anderen
        /// sichtbaren Fenster führt – sonst könnte ein anderes Systemsteuerungs-Element
        /// mitgerissen werden.
        /// </summary>
        public static bool ShouldTerminateHungPanel(bool windowVisible, bool responsive, bool hasOtherVisibleWindows)
        {
            return !hasOtherVisibleWindows && (!windowVisible || !responsive);
        }

        /// <summary>
        /// Entscheidung des Wächters: Nur ein VERSTECKTES Panel wird ersetzt – höchstens
        /// dreimal in zehn Minuten. Ein verstecktes Fenster ist der bekannte RunDLL-Zombie
        /// (nie angezeigt, hängt in der Geräteabfrage); es wird nach 10 s beendet, bevor
        /// Windows den Absturz meldet.
        ///
        /// Ein SICHTBARES Panel wird nie automatisch beendet: Es ist das Fenster des
        /// Nutzers, und joy.cpl antwortet beim Aufbau der Liste minutenlang nicht auf
        /// WM_NULL (gemessen 26.10.2026: 10 s im isolierten Lauf, über 30 s während einer
        /// Geräteänderung). Ein Abschuss erzeugte genau das Flackern/„Verschwinden“, das
        /// wie ein Absturz aussieht – Windows entscheidet über ein sichtbares Fenster selbst.
        /// </summary>
        public static bool ShouldRecover(bool hasPanel, bool visible, bool responsive, int unresponsiveTicks,
            int ageMs, int recoveriesInWindow)
        {
            if (!hasPanel || visible) return false;
            return !responsive && unresponsiveTicks >= 5 && ageMs >= 10000 && recoveriesInWindow < 3;
        }

        /// <summary>
        /// True, wenn gerade ein sichtbares Gamecontroller-Panel offen ist. Der Rescan nutzt
        /// das, um die DirectInput-Aufzählung zu pausieren; das Ergebnis wird ~1 s gecacht.
        /// </summary>
        public static bool IsPanelVisible()
        {
            lock (VisibleLock)
            {
                if (DateTime.UtcNow - _visibleCheckedAt < TimeSpan.FromMilliseconds(900))
                    return _visibleCache;

                _visibleCheckedAt = DateTime.UtcNow;
                _visibleCache = false;
                foreach (var window in FindWindows())
                {
                    if (!window.Visible) continue;
                    _visibleCache = true;
                    break;
                }
                return _visibleCache;
            }
        }

        /// <summary>
        /// Startet den Wächter einmal pro Prozess. Er beendet ein hängendes RunDLL-Fenster,
        /// bevor Windows es nach mehreren Sekunden als „Application Hang“ abschießt, und
        /// öffnet das Panel frisch. Damit ist auch ein direkt aus Windows gestartetes Panel
        /// abgedeckt, solange TenzoraX läuft.
        /// </summary>
        public static void StartWatchdog()
        {
            lock (WatchLock)
            {
                if (_watchdog != null) return;
                _watchdog = new System.Threading.Timer(_ => WatchdogTick(), null, 1000, 1000);
                App.LogApp("Panel-Wächter: aktiv (prüft jede Sekunde auf hängende RunDLL-Fenster).");
            }
        }

        private static void WatchdogTick()
        {
            try
            {
                var panels = FindWindows();
                if (panels.Count == 0)
                {
                    _watchedPid = 0;
                    _unresponsiveTicks = 0;
                    return;
                }

                var panel = panels[0];
                if (panel.ProcessId != _watchedPid)
                {
                    // Neues Fenster: erst nach der Schonzeit beurteilen, sonst würde
                    // der Wächter ein gerade startendes Panel abschießen.
                    _watchedPid = panel.ProcessId;
                    _watchedSinceUtc = DateTime.UtcNow;
                    _unresponsiveTicks = 0;
                    return;
                }

                bool responsive = panel.Visible && IsResponsive(panel.Handle);
                if (responsive)
                {
                    _unresponsiveTicks = 0;
                    return;
                }

                _unresponsiveTicks++;
                int ageMs = (int)Math.Max(0, (DateTime.UtcNow - _watchedSinceUtc).TotalMilliseconds);
                if (!ShouldRecover(true, panel.Visible, false, _unresponsiveTicks, ageMs, CountRecentRecoveries()))
                    return;

                RecoveryTimes.Add(DateTime.UtcNow);
                App.LogApp("Panel-Wächter: Gamecontroller-Fenster reagiert nicht (PID " + panel.ProcessId +
                           ") – beende es und öffne ein frisches.");
                TerminateHungPanels(panels);
                _watchedPid = 0;
                _unresponsiveTicks = 0;

                Task.Run(() =>
                {
                    try { Open(ControllerManager.Instance.LastControllerChangeUtc); }
                    catch (Exception ex) { App.LogApp("Panel-Wächter: erneutes Öffnen fehlgeschlagen: " + ex.Message); }
                });
            }
            catch (Exception ex)
            {
                App.LogApp("Panel-Wächter: Prüfung fehlgeschlagen: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static int CountRecentRecoveries()
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-10);
            RecoveryTimes.RemoveAll(time => time < cutoff);
            return RecoveryTimes.Count;
        }

        /// <summary>
        /// Öffnet das Panel mit frischer Liste. <paramref name="lastControllerChangeUtc"/>
        /// kommt aus <see cref="ControllerManager.LastControllerChangeUtc"/>; liegt sie
        /// länger zurück, wird nicht zusätzlich gewartet.
        /// </summary>
        public static PanelOpenResult Open(DateTime lastControllerChangeUtc)
        {
            // Zuerst die gemeinsame DirectInput-Schnittstelle beanspruchen. Läuft noch
            // eine Aufzählung oder endete die letzte im Stau, würde joy.cpl daran hängen
            // und als RunDLL abstürzen. Kurze Staus (z.B. die erste Aufzählung nach dem
            // Start, gemessen 10,4 s) gehen vorbei – deshalb einige Sekunden erneut
            // versuchen, statt den Klick sofort abzuweisen.
            bool gate = false;
            for (int attempt = 0; attempt < 4 && !gate; attempt++)
            {
                gate = DirectInputControllerProvider.TryBeginPanelExclusive(PanelGateTimeoutMs);
                if (!gate) Thread.Sleep(PanelGateRetryMs);
            }

            if (!gate)
            {
                App.LogApp("Panel: DirectInput ist noch beschäftigt oder zuletzt im Stau – Öffnen abgelehnt, " +
                           "bevor joy.cpl als RunDLL abstürzt.");
                return new PanelOpenResult(false, 0,
                    "DirectInput antwortet gerade nicht (Geräte-Timeout). Das Windows-Panel würde daran abstürzen; " +
                    "die blockierenden Geräte werden im Hintergrund neu gestartet – bitte in etwa einer Minute erneut versuchen.");
            }

            try
            {
                // Alte Panels merken: Sie dürfen später nicht als „neu geöffnet“ gelten.
                var oldPids = new HashSet<int>();
                foreach (var window in FindWindows())
                    oldPids.Add(window.ProcessId);

                int closed = ClosePanels("vor dem Öffnen");
                if (closed > 0)
                    App.LogApp("Panel: " + closed + " bestehende(s) Gamecontroller-Fenster geschlossen – Liste wird neu aufgebaut.");

                bool quiet = WaitForQuiet(lastControllerChangeUtc);
                App.LogApp(quiet
                    ? "Panel: Eingabezustand ruhig – öffne Windows-Panel „Gamecontroller“."
                    : "Panel: Eingabezustand blieb unruhig (max. " + MaxQuietWaitMs / 1000 + " s gewartet) – öffne trotzdem.");

                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    Launch();

                    if (WaitForNewVisible(oldPids, VisibleTimeoutMs))
                    {
                        App.LogApp("Panel: sichtbar nach Versuch " + attempt + ".");
                        return new PanelOpenResult(true, attempt, "");
                    }

                    App.LogApp("Panel: nach " + VisibleTimeoutMs + " ms nicht sichtbar (Versuch " + attempt +
                               ") – schließe hängendes Fenster und versuche erneut.");
                    ClosePanels("hängendes Panel");
                    Thread.Sleep(RetryDelayMs);
                }

                App.LogApp("Panel: Windows-Panel „Gamecontroller“ reagiert nicht – Öffnen abgebrochen.");
                return new PanelOpenResult(false, 2,
                    "Windows hat das Fenster „Gamecontroller“ nicht angezeigt. Das Panel hängt vermutlich " +
                    "in einer Geräteänderung; bitte gleich noch einmal versuchen. Einzelheiten stehen im Log.");
            }
            catch (Exception ex)
            {
                App.LogApp("Panel: Öffnen fehlgeschlagen: " + ex.GetType().Name + ": " + ex.Message);
                return new PanelOpenResult(false, 0, "Öffnen fehlgeschlagen: " + ex.Message);
            }
            finally
            {
                DirectInputControllerProvider.EndPanelExclusive();
                // Sichtbarkeits-Cache verwerfen: Der Rescan soll den Zustand des gerade
                // geöffneten Fensters sofort messen, nicht den Stand von vor dem Start.
                lock (VisibleLock) { _visibleCheckedAt = DateTime.MinValue; }
            }
        }

        /// <summary>Alle Gamecontroller-Fenster, auch versteckte (hängende) – rein lesend.</summary>
        public static List<PanelWindow> FindWindows()
        {
            var found = new List<PanelWindow>();
            EnumWindowsProc callback = (handle, _) =>
            {
                string title = GetWindowTitle(handle);
                string cls = GetWindowClass(handle);
                if (!IsPanelWindow(cls, title)) return true;

                GetWindowThreadProcessId(handle, out uint pid);
                found.Add(new PanelWindow
                {
                    Handle = handle,
                    ProcessId = (int)pid,
                    Visible = IsWindowVisible(handle),
                    Title = title
                });
                return true;
            };

            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return found;
        }

        /// <summary>Schließt alle gefundenen Panels höflich über WM_CLOSE.</summary>
        private static int ClosePanels(string reason)
        {
            var windows = FindWindows();
            foreach (var window in windows)
            {
                App.LogApp("Panel: schließe " + (window.Visible ? "offenes" : "verstecktes") +
                           " Gamecontroller-Fenster (PID " + window.ProcessId + ", " + reason + ").");
                PostMessage(window.Handle, WmClose, IntPtr.Zero, IntPtr.Zero);
            }

            if (windows.Count > 0)
            {
                // Nicht nur kurz warten: Solange das alte Panel noch steht, kann der neue
                // joy.cpl-Aufruf an die sterbende Instanz gehen und erzeugt gar kein Fenster
                // (gemessen 26.10.2026, 19:23: erster Versuch blieb 6 s unsichtbar).
                WaitUntilClosed(1500);
                TerminateHungPanels(windows);
            }
            return windows.Count;
        }

        /// <summary>Wartet, bis kein Gamecontroller-Fenster mehr existiert.</summary>
        private static void WaitUntilClosed(int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (FindWindows().Count == 0) return;
                Thread.Sleep(PollMs);
            }
        }

        /// <summary>
        /// Räumt Panels auf, die auf WM_CLOSE nicht reagieren und nie sichtbar wurden.
        /// Genau solche Fenster bleiben sonst als unsichtbarer rundll32 liegen und lassen
        /// Windows den „RunDLL“-Absturz melden.
        /// </summary>
        private static void TerminateHungPanels(List<PanelWindow> windows)
        {
            bool terminated = false;
            foreach (var window in windows)
            {
                if (!IsWindow(window.Handle)) continue;

                bool visible = IsWindowVisible(window.Handle);
                bool responsive = IsResponsive(window.Handle);
                if (!ShouldTerminateHungPanel(visible, responsive, HasOtherVisibleWindows(window.ProcessId)))
                    continue;

                try
                {
                    using var process = Process.GetProcessById(window.ProcessId);
                    process.Kill();
                    process.WaitForExit(1500);
                    terminated = true;
                    App.LogApp("Panel: hängendes RunDLL-Fenster beendet (PID " + window.ProcessId +
                               ", sichtbar=" + visible + ", antwortet=" + responsive + ").");
                }
                catch (Exception ex)
                {
                    App.LogApp("Panel: hängendes RunDLL-Fenster nicht beendbar (PID " + window.ProcessId + "): " + ex.Message);
                }
            }

            if (terminated) WaitUntilClosed(1000);
        }

        /// <summary>
        /// Fragt per WM_NULL, ob der UI-Thread des Fensters noch Nachrichten verarbeitet.
        /// Ein sichtbares, aber hängendes Panel liefert hier 0 (Timeout).
        /// </summary>
        private static bool IsResponsive(IntPtr handle)
        {
            return SendMessageTimeout(handle, WmNull, IntPtr.Zero, IntPtr.Zero,
                SmtoAbortIfHung, 800, out _) != IntPtr.Zero;
        }

        /// <summary>True, wenn der Prozess außer den Panel-Fenstern noch sichtbare Fenster führt.</summary>
        private static bool HasOtherVisibleWindows(int processId)
        {
            bool other = false;
            EnumWindowsProc callback = (handle, _) =>
            {
                GetWindowThreadProcessId(handle, out uint pid);
                if ((int)pid != processId || !IsWindowVisible(handle)) return true;
                if (!IsPanelWindow(GetWindowClass(handle), GetWindowTitle(handle)))
                {
                    other = true;
                    return false;
                }
                return true;
            };

            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return other;
        }

        private static bool WaitForQuiet(DateTime lastControllerChangeUtc)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                int sinceChange = lastControllerChangeUtc == DateTime.MinValue
                    ? int.MaxValue
                    : (int)Math.Max(0, (DateTime.UtcNow - lastControllerChangeUtc).TotalMilliseconds);
                int waited = (int)watch.ElapsedMilliseconds;

                if (!ShouldWaitForQuiet(sinceChange, waited))
                    return sinceChange >= QuietMs;

                Thread.Sleep(PollMs);
            }
        }

        /// <summary>
        /// Erfolg ist nur ein SICHTBARES Panel mit neuer Prozess-ID. Ein altes, hängendes
        /// Panel (gleiche PID, weiterhin unsichtbar) zählt bewusst nicht.
        /// </summary>
        private static bool WaitForNewVisible(HashSet<int> oldPids, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                foreach (var window in FindWindows())
                {
                    if (window.Visible && !oldPids.Contains(window.ProcessId))
                        return true;
                }
                Thread.Sleep(PollMs);
            }
            return false;
        }

        private static void Launch()
        {
            try
            {
                Process.Start(new ProcessStartInfo("joy.cpl") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.LogApp("Panel: joy.cpl konnte nicht gestartet werden: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string GetWindowTitle(IntPtr handle)
        {
            var buffer = new StringBuilder(256);
            GetWindowText(handle, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        private static string GetWindowClass(IntPtr handle)
        {
            var buffer = new StringBuilder(256);
            GetClassName(handle, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        private delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr handle, StringBuilder className, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr handle, int message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeoutMs, out IntPtr result);
    }
}
