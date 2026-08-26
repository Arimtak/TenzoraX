using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Live-Integrationstests für die Tastatur-Ausgabe (Text- UND Tasten-Pfad).
    /// Opt-in: Nur mit TENZORAX_LIVE_TESTS=1 (und --filter "Category=Live") wird
    /// wirklich getestet. Ohne die Variable sind die Tests dokumentierte No-Ops,
    /// damit normale Testläufe weder Eingaben erzeugen noch Fenster öffnen.
    ///
    /// Es wird bewusst KEIN Vordergrund-Fenster verwendet (ein Hintergrund-Prozess
    /// bekommt keinen OS-Fokus – Windows Foreground-Stealing-Schutz). Stattdessen
    /// wird die Injektion über einen WH_KEYBOARD_LL-Hook nachgewiesen: Nur injizierte
    /// Events (LLKHF_INJECTED) werden aufgezeichnet UND geschluckt (return 1) –
    /// der Test beweist die OS-Injektion, aber es kommt garantiert nichts im
    /// Vordergrund-Fenster des Benutzers an.
    /// </summary>
    [Trait("Category", "Live")]
    public class LiveTextActionTests
    {
        private const int WH_KEYBOARD_LL = 13;
        private const uint LLKHF_EXTENDED = 0x0001;
        private const uint LLKHF_INJECTED = 0x0010;
        private const uint LLKHF_UP = 0x0080;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [Fact]
        public void Live_SimulateText_InjectsUnicodeEvents()
        {
            if (!IsLiveModeEnabled()) return;

            // 🎉 = U+1F389 → Surrogatpaar (D83C DF89) → zwei Unicode-Events.
            // Windows liefert Unicode-Events im LL-Hook als VK_PACKET (0xE7)
            // mit dem Unicode-Wert im scanCode.
            var events = CaptureInjectedEvents(() => InputSimulator.SimulateText("🎉"));
            RequireInjected(events);

            Assert.Equal(2, events.Count);
            Assert.Equal(0xE7u, events[0].vk);
            Assert.Equal(0xD83Cu, events[0].scan);
            Assert.Equal(0xE7u, events[1].vk);
            Assert.Equal(0xDF89u, events[1].scan);

            // 'A' als Unicode-Zeichen, '\n' als Enter (VK_RETURN down+up), 'B' als Unicode
            events = CaptureInjectedEvents(() => InputSimulator.SimulateText("A\nB"));
            RequireInjected(events);

            Assert.Equal(4, events.Count);
            Assert.Equal(0xE7u, events[0].vk);   // 'A' als VK_PACKET
            Assert.Equal((uint)'A', events[0].scan);
            Assert.Equal(0x0Du, events[1].vk);  // Enter down
            Assert.Equal(0x0Du, events[2].vk);  // Enter up
            Assert.Equal(0xE7u, events[3].vk);  // 'B' als VK_PACKET
            Assert.Equal((uint)'B', events[3].scan);
        }

        [Fact]
        public void Live_SimulateKeys_InjectsVkEventsInOrder()
        {
            if (!IsLiveModeEnabled()) return;

            // Trigger-Pfad: SimulateKeyDown (List-Reihenfolge) + SimulateKeyUp.
            var events = CaptureInjectedEvents(() =>
            {
                InputSimulator.SimulateKeyDown(new List<string> { "SHIFT", "TAB" });
                InputSimulator.SimulateKeyUp(new List<string> { "SHIFT", "TAB" });
                InputSimulator.SimulateKeyDown(new List<string> { "ARROW_RIGHT" });
                InputSimulator.SimulateKeyUp(new List<string> { "ARROW_RIGHT" });
            });
            RequireInjected(events);

            Assert.Equal(6, events.Count);

            // SHIFT↓, TAB↓, SHIFT↑, TAB↑ – Reihenfolge = List-Reihenfolge.
            // Windows mappt den generischen SHIFT-VK (0x10) im LL-Hook über den
            // Scancode 0x2A (links) auf VK_LSHIFT (0xA0).
            Assert.Equal(0xA0u, events[0].vk);
            Assert.Equal(0x2Au, events[0].scan);
            Assert.Equal(0u, events[0].flags & LLKHF_UP);
            Assert.Equal(0x09u, events[1].vk);
            Assert.Equal(0x0Fu, events[1].scan);
            Assert.Equal(0u, events[1].flags & LLKHF_UP);
            Assert.Equal(0xA0u, events[2].vk);
            Assert.NotEqual(0u, events[2].flags & LLKHF_UP);
            Assert.Equal(0x09u, events[3].vk);
            Assert.NotEqual(0u, events[3].flags & LLKHF_UP);

            // Erweiterte Taste: ARROW_RIGHT (0x27) mit LLKHF_EXTENDED, down→up
            Assert.Equal(0x27u, events[4].vk);
            Assert.Equal(0u, events[4].flags & LLKHF_UP);
            Assert.NotEqual(0u, events[4].flags & LLKHF_EXTENDED);
            Assert.Equal(0x27u, events[5].vk);
            Assert.NotEqual(0u, events[5].flags & LLKHF_UP);
            Assert.NotEqual(0u, events[5].flags & LLKHF_EXTENDED);
        }

        [Fact]
        public void Live_Notification_ShowsOnlyText_ForTextAction()
        {
            if (!IsLiveModeEnabled()) return;
            RunOnStaThread(() =>
            {
                EnsureAppResources();

                // Text-Aktion: Benachrichtigung zeigt nur den ausgelösten Text
                var textNotif = new NotificationWindow("", "🎉", 1.0, 360);
                Assert.Equal("🎉", textNotif.TxtAction.Text);
                Assert.Equal(Visibility.Collapsed, textNotif.TxtCombo.Visibility);
                Assert.Equal(Visibility.Collapsed, textNotif.TxtArrow.Visibility);
                textNotif.Close();

                // Tasten-Aktion: Combo + Pfeil bleiben sichtbar (wie bisher)
                var keyNotif = new NotificationWindow("LB + ↑", "SHIFT + TAB", 1.0, 360);
                Assert.Equal(Visibility.Visible, keyNotif.TxtCombo.Visibility);
                Assert.Equal(Visibility.Visible, keyNotif.TxtArrow.Visibility);
                Assert.Equal("SHIFT + TAB", keyNotif.TxtAction.Text);
                keyNotif.Close();
            });
        }

        /// <summary>
        /// Installiert den LL-Hook, führt die Aktion aus und gibt die injizierten
        /// Events zurück (vk, scan, flags). Injizierte Events werden geschluckt.
        /// Läuft auf einem eigenen STA-Thread (Hook braucht Message-Loop).
        /// </summary>
        private static List<(uint vk, uint scan, uint flags)> CaptureInjectedEvents(Action action)
        {
            List<(uint vk, uint scan, uint flags)>? result = null;
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var events = new List<(uint vk, uint scan, uint flags)>();
                    IntPtr hook = IntPtr.Zero;
                    LowLevelKeyboardProc proc = (nCode, wParam, lParam) =>
                    {
                        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_KEYUP
                                          || wParam == (IntPtr)WM_SYSKEYDOWN || wParam == (IntPtr)WM_SYSKEYUP))
                        {
                            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                            if ((kbd.flags & LLKHF_INJECTED) != 0)
                            {
                                events.Add((kbd.vkCode, kbd.scanCode, kbd.flags));
                                // Schlucken (return 1): nichts darf im Vordergrund-Fenster
                                // des Benutzers ankommen – nur die Injektion wird bewiesen.
                                return (IntPtr)1;
                            }
                        }
                        return CallNextHookEx(hook, nCode, wParam, lParam);
                    };

                    hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, IntPtr.Zero, 0);
                    if (hook == IntPtr.Zero)
                        throw new InvalidOperationException("WH_KEYBOARD_LL konnte nicht installiert werden: " + Marshal.GetLastWin32Error());

                    try
                    {
                        action();
                        Pump(TimeSpan.FromMilliseconds(300));
                    }
                    finally
                    {
                        UnhookWindowsHookEx(hook);
                    }
                    result = events;
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            return result!;
        }

        private static void RequireInjected(List<(uint vk, uint scan, uint flags)> events)
        {
            if (events.Count == 0)
                throw new InvalidOperationException(
                    "Keine injizierten Events im Hook angekommen – SendInput wurde vermutlich " +
                    "durch UIPI blockiert (Vordergrund-Fenster läuft als Administrator?). " +
                    "Test mit nicht-erhöhtem Vordergrund-Fenster wiederholen.");
        }

        private static bool IsLiveModeEnabled() =>
            Environment.GetEnvironmentVariable("TENZORAX_LIVE_TESTS") == "1";

        private static void EnsureAppResources()
        {
            // NotificationWindow.xaml nutzt StaticResource-Brushed aus App.xaml –
            // in Tests existiert kein Application, daher hier nachlegen.
            if (Application.Current == null)
            {
                var app = new Application();
                app.Resources["PanelBgBrush"] = new SolidColorBrush(Color.FromRgb(22, 22, 30));
                app.Resources["BorderBrush"] = new SolidColorBrush(Color.FromRgb(45, 45, 61));
                app.Resources["TextBrush"] = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            }
        }

        private static void RunOnStaThread(Action action)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        private static void Pump(TimeSpan duration)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < duration)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }
        }
    }
}
