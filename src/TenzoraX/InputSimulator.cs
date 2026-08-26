using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace TenzoraX
{
    public class InputSimulator
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, uint tokenInformationLength, out uint returnLength);
        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthorityIndex);

        private const int INPUT_KEYBOARD = 1;
        private const int INPUT_MOUSE = 0;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        // Key → (vk, scan, extended)
        public static readonly Dictionary<string, (ushort vk, ushort scan, bool ext)> KeyData = new()
        {
            { "A", (0x41, 0x1E, false) }, { "B", (0x42, 0x30, false) }, { "C", (0x43, 0x2E, false) },
            { "D", (0x44, 0x20, false) }, { "E", (0x45, 0x12, false) }, { "F", (0x46, 0x21, false) },
            { "G", (0x47, 0x22, false) }, { "H", (0x48, 0x23, false) }, { "I", (0x49, 0x17, false) },
            { "J", (0x4A, 0x24, false) }, { "K", (0x4B, 0x25, false) }, { "L", (0x4C, 0x26, false) },
            { "M", (0x4D, 0x32, false) }, { "N", (0x4E, 0x31, false) }, { "O", (0x4F, 0x18, false) },
            { "P", (0x50, 0x19, false) }, { "Q", (0x51, 0x10, false) }, { "R", (0x52, 0x13, false) },
            { "S", (0x53, 0x1F, false) }, { "T", (0x54, 0x14, false) }, { "U", (0x55, 0x16, false) },
            { "V", (0x56, 0x2F, false) }, { "W", (0x57, 0x11, false) }, { "X", (0x58, 0x2D, false) },
            { "Y", (0x59, 0x15, false) }, { "Z", (0x5A, 0x2C, false) },
            { "0", (0x30, 0x0B, false) }, { "1", (0x31, 0x02, false) }, { "2", (0x32, 0x03, false) },
            { "3", (0x33, 0x04, false) }, { "4", (0x34, 0x05, false) }, { "5", (0x35, 0x06, false) },
            { "6", (0x36, 0x07, false) }, { "7", (0x37, 0x08, false) }, { "8", (0x38, 0x09, false) },
            { "9", (0x39, 0x0A, false) },
            { "ESC", (0x1B, 0x01, false) }, { "TAB", (0x09, 0x0F, false) },
            { "ENTER", (0x0D, 0x1C, false) }, { "SPACE", (0x20, 0x39, false) },
            { "BACKSPACE", (0x08, 0x0E, false) },
            { "SHIFT", (0x10, 0x2A, false) }, { "CTRL", (0x11, 0x1D, false) },
            { "ALT", (0x12, 0x38, false) }, { "WIN", (0x5B, 0x5B, true) },
            { "CAPSLOCK", (0x14, 0x3A, false) },
            { "ARROW_UP", (0x26, 0x48, true) }, { "ARROW_DOWN", (0x28, 0x50, true) },
            { "ARROW_LEFT", (0x25, 0x4B, true) }, { "ARROW_RIGHT", (0x27, 0x4D, true) },
            { "PAGE_UP", (0x21, 0x49, true) }, { "PAGE_DOWN", (0x22, 0x51, true) },
            { "HOME", (0x24, 0x47, true) }, { "END", (0x23, 0x4F, true) },
            { "INSERT", (0x2D, 0x52, true) }, { "DELETE", (0x2E, 0x53, true) },
            { "F1", (0x70, 0x3B, false) }, { "F2", (0x71, 0x3C, false) }, { "F3", (0x72, 0x3D, false) },
            { "F4", (0x73, 0x3E, false) }, { "F5", (0x74, 0x3F, false) }, { "F6", (0x75, 0x40, false) },
            { "F7", (0x76, 0x41, false) }, { "F8", (0x77, 0x42, false) }, { "F9", (0x78, 0x43, false) },
            { "F10", (0x79, 0x44, false) }, { "F11", (0x7A, 0x57, false) }, { "F12", (0x7B, 0x58, false) },
            { "F13", (0x7C, 0x64, false) }, { "F14", (0x7D, 0x65, false) }, { "F15", (0x7E, 0x66, false) },
            { "F16", (0x7F, 0x67, false) }, { "F17", (0x80, 0x68, false) }, { "F18", (0x81, 0x69, false) },
            { "F19", (0x82, 0x6A, false) }, { "F20", (0x83, 0x6B, false) },
            { "F21", (0x84, 0x6C, false) }, { "F22", (0x85, 0x6D, false) }, { "F23", (0x86, 0x6E, false) },
            { "F24", (0x87, 0x6F, false) },
            { "NUMLOCK", (0x90, 0x45, true) },

            // Numpad (echte Numpad-VKs + Set-1-Scancodes)
            { "NUMPAD_0", (0x60, 0x52, false) }, { "NUMPAD_1", (0x61, 0x4F, false) },
            { "NUMPAD_2", (0x62, 0x50, false) }, { "NUMPAD_3", (0x63, 0x51, false) },
            { "NUMPAD_4", (0x64, 0x4B, false) }, { "NUMPAD_5", (0x65, 0x4C, false) },
            { "NUMPAD_6", (0x66, 0x4D, false) }, { "NUMPAD_7", (0x67, 0x47, false) },
            { "NUMPAD_8", (0x68, 0x48, false) }, { "NUMPAD_9", (0x69, 0x49, false) },
            { "NUMPAD_DOT", (0x6E, 0x53, false) },
            { "NUMPAD_DIV", (0x6F, 0x35, false) },
            { "NUMPAD_MUL", (0x6A, 0x37, false) },
            { "NUMPAD_MINUS", (0x6D, 0x4A, false) },
            { "NUMPAD_PLUS", (0x6B, 0x4E, false) },

            // Mouse buttons (custom VKs > 0xE000)
            { "MOUSE_LEFT", (0xE001, 0, false) },
            { "MOUSE_RIGHT", (0xE002, 0, false) },
            { "MOUSE_MIDDLE", (0xE003, 0, false) },
        };

        // Mouse button VKs (custom, above 0xE000)
        private const ushort VK_MOUSE_LEFT = 0xE001;
        private const ushort VK_MOUSE_RIGHT = 0xE002;
        private const ushort VK_MOUSE_MIDDLE = 0xE003;

        private const int DELAY_MS = 30;

        /// <summary>"VK" or "ScanCode"</summary>
        public static string OutputMode { get; set; } = "VK";

        public static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // ============================================================
        //  UIPI-Erkennung: SendInput wird STILL verworfen, wenn das
        //  Vordergrund-Fenster mit höheren Rechten läuft als dieser
        //  Prozess – weder Rückgabewert noch GetLastError melden das
        //  (laut Doku). Verlässlich ist nur der Integritäts-Level-
        //  Vergleich (Equal-or-Lesser-Regel von UIPI).
        // ============================================================

        private const int TokenIntegrityLevel = 25;   // TOKEN_INFORMATION_CLASS::TokenIntegrityLevel
        private const uint TokenQuery = 0x0008;       // TOKEN_QUERY
        private const uint ProcessQueryLimited = 0x1000;

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_MANDATORY_LABEL
        {
            public IntPtr Label;   // PSID
        }

        /// <summary>Interner Test-Seam: liefert den Integritäts-Level des (simulierten)
        /// Vordergrund-Fensters statt per Win32-API nachzuschlagen.</summary>
        internal static Func<int>? ForegroundIntegrityOverride { get; set; }

        /// <summary>Wird ausgelöst, wenn die Tastatur-Ausgabe blockiert ist (UIPI oder
        /// SendInput-Fehler). Payload: verständliche, lokalisierte Meldung.</summary>
        internal static event Action<string>? InjectionBlocked;

        internal static string? LastInjectionBlockMessage { get; private set; }
        internal static DateTime? LastInjectionBlockAt { get; private set; }

        private const long InjectionReportCooldownMs = 10_000;
        private static long _lastInjectionReportTicks = -InjectionReportCooldownMs;

        private static int? _ownIntegrityLevel;
        private static int OwnIntegrityLevel => _ownIntegrityLevel ??= GetProcessIntegrityLevel(GetCurrentProcess());

        private static IntPtr _lastCheckedForeground;
        private static long _lastCheckTicks;
        private static bool _cachedBlocked;

        internal static bool IsBlockedByUipi()
        {
            if (ForegroundIntegrityOverride != null)
            {
                int own = OwnIntegrityLevel;
                return own >= 0 && ForegroundIntegrityOverride() > own;
            }

            // Schneller Pfad: nur bei Vordergrund-Wechsel oder nach 1 s neu prüfen
            // (GetForegroundWindow ist billig, OpenProcess+Token nicht).
            long now = Environment.TickCount64;
            IntPtr fg = GetForegroundWindow();
            if (fg == _lastCheckedForeground && now - _lastCheckTicks < 1000)
                return _cachedBlocked;

            _lastCheckedForeground = fg;
            _lastCheckTicks = now;
            _cachedBlocked = false;

            if (fg == IntPtr.Zero) return false;
            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == 0) return false;

            IntPtr proc = OpenProcess(ProcessQueryLimited, false, pid);
            if (proc == IntPtr.Zero) return false;
            try
            {
                int fgLevel = GetProcessIntegrityLevel(proc);
                int own = OwnIntegrityLevel;
                _cachedBlocked = own >= 0 && fgLevel > own;
            }
            finally
            {
                CloseHandle(proc);
            }
            return _cachedBlocked;
        }

        private static int GetProcessIntegrityLevel(IntPtr processHandle)
        {
            if (!OpenProcessToken(processHandle, TokenQuery, out IntPtr token)) return -1;
            try { return ReadIntegrityLevel(token); }
            finally { CloseHandle(token); }
        }

        private static int ReadIntegrityLevel(IntPtr tokenHandle)
        {
            // Erster Aufruf liefert absichtlich ERROR_INSUFFICIENT_BUFFER (122)
            // und die benötigte Puffergröße – das ist KEIN Fehlerfall.
            if (!GetTokenInformation(tokenHandle, TokenIntegrityLevel, IntPtr.Zero, 0, out uint size))
            {
                if (Marshal.GetLastWin32Error() != 122) return -1;
            }
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!GetTokenInformation(tokenHandle, TokenIntegrityLevel, buffer, size, out _))
                    return -1;
                var label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer);
                if (label.Label == IntPtr.Zero) return -1;
                IntPtr countPtr = GetSidSubAuthorityCount(label.Label);
                if (countPtr == IntPtr.Zero) return -1;
                byte count = Marshal.ReadByte(countPtr);
                if (count == 0) return -1;
                IntPtr subPtr = GetSidSubAuthority(label.Label, (uint)(count - 1));
                if (subPtr == IntPtr.Zero) return -1;
                return Marshal.ReadInt32(subPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Zentrale SendInput-Stelle: prüft vorab auf UIPI-Blockade (dann wird gar
        /// nicht erst gesendet – die Eingabe würde sonst still verworfen) und meldet
        /// Fehlfälle anstatt still zu scheitern. Gethrottet auf eine Meldung pro
        /// Cooldown-Zeitraum, damit der Poll-Thread (bis ~250×/s) nicht spammt.
        /// </summary>
        private static uint SendInputChecked(INPUT[] inputs)
        {
            if (IsBlockedByUipi())
            {
                ReportInjectionBlocked(LanguageManager.Instance["Injection_UipiBlocked"]);
                return 0;
            }

            uint ret = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (ret != inputs.Length)
                ReportInjectionBlocked(LanguageManager.Instance.Format("Injection_SendInputFailed", Marshal.GetLastWin32Error()));
            return ret;
        }

        private static void ReportInjectionBlocked(string message)
        {
            long now = Environment.TickCount64;
            if (now - _lastInjectionReportTicks < InjectionReportCooldownMs) return;
            _lastInjectionReportTicks = now;
            LastInjectionBlockMessage = message;
            LastInjectionBlockAt = DateTime.Now;
            try
            {
                InjectionBlocked?.Invoke(message);
                App.LogApp("Tastatur-Ausgabe blockiert: " + message);
            }
            catch { }
        }

        /// <summary>Statuszeile für den Diagnosebericht.</summary>
        public static string FormatInjectionStatus()
        {
            if (LastInjectionBlockAt == null)
                return "OK – keine Blockade erkannt";
            return "blockiert: " + LastInjectionBlockMessage + " (um " + LastInjectionBlockAt.Value.ToString("HH:mm:ss") + ")";
        }

        /// <summary>Interner Test-Seam: setzt Blockade-Erkennung und Report-Throttle zurück.</summary>
        internal static void ResetInjectionReportingForTesting()
        {
            ForegroundIntegrityOverride = null;
            _lastInjectionReportTicks = -InjectionReportCooldownMs;
            LastInjectionBlockMessage = null;
            LastInjectionBlockAt = null;
        }

        public static void SimulateKeyDown(List<string> actionKeys)
        {
            if (actionKeys == null || actionKeys.Count == 0) return;
            SendKeys(actionKeys, false);
        }

        public static void SimulateKeyUp(List<string> actionKeys)
        {
            if (actionKeys == null || actionKeys.Count == 0) return;
            SendKeys(actionKeys, true);
        }

        /// <summary>
        /// Tippt einen Text (inkl. Emojis) als Tastatureingabe. Emojis (Surrogatpaare)
        /// werden als Unicode-Zeichenereignisse gesendet (Windows setzt die Paare
        /// korrekt zusammen), Enter/Tab als echte Tasten.
        /// </summary>
        public static void SimulateText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            foreach (char c in text)
            {
                switch (c)
                {
                    case '\r':
                        continue;   // Teil von CRLF – nicht doppelt senden
                    case '\n':
                        SendVkKey(0x0D);   // Enter
                        break;
                    case '\t':
                        SendVkKey(0x09);   // Tab
                        break;
                    default:
                        SendUnicodeChar(c);
                        break;
                }
            }
        }

        private static void SendUnicodeChar(char c)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = (ushort)c,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInputChecked(new[] { input });
        }

        private static void SendVkKey(ushort vk)
        {
            var down = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            var up = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            SendInputChecked(new[] { down, up });
        }

        public static void SimulateCombination(List<string> actionKeys)
        {
            if (actionKeys == null || actionKeys.Count == 0) return;

            var modKeys = new List<string>();
            var otherKeys = new List<string>();

            foreach (var key in actionKeys)
            {
                if (key == "SHIFT" || key == "CTRL" || key == "ALT" || key == "WIN")
                    modKeys.Add(key);
                else
                    otherKeys.Add(key);
            }

            // KeyDown: modifiers first, then others
            var downAll = new List<string>();
            downAll.AddRange(modKeys);
            downAll.AddRange(otherKeys);
            SendKeys(downAll, false);

            Thread.Sleep(DELAY_MS);

            // KeyUp: others last (reverse), then modifiers last (reverse)
            var upAll = new List<string>();
            for (int i = otherKeys.Count - 1; i >= 0; i--)
                upAll.Add(otherKeys[i]);
            for (int i = modKeys.Count - 1; i >= 0; i--)
                upAll.Add(modKeys[i]);
            SendKeys(upAll, true);
        }

        private static void SendKeys(List<string> keys, bool keyUp)
        {
            foreach (var key in keys)
            {
                if (!KeyData.TryGetValue(key, out var data)) continue;

                if (data.vk >= 0xE000)
                {
                    SendMouseClick(data.vk, !keyUp);
                    continue;
                }

                if (OutputMode == "ScanCode")
                {
                    uint flags = KEYEVENTF_SCANCODE;
                    if (keyUp) flags |= KEYEVENTF_KEYUP;
                    if (data.ext) flags |= KEYEVENTF_EXTENDEDKEY;

                    var input = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = 0,
                                wScan = data.scan,
                                dwFlags = flags,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    };
                    SendInputChecked(new[] { input });
                }
                else // VK mode (default)
                {
                    uint flags = 0;
                    if (keyUp) flags |= KEYEVENTF_KEYUP;
                    if (data.ext) flags |= KEYEVENTF_EXTENDEDKEY;

                    var input = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = data.vk,
                                wScan = data.scan,
                                dwFlags = flags,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    };
                    SendInputChecked(new[] { input });
                }
            }
        }

        private static void SendMouseClick(ushort vk, bool isDown)
        {
            uint flags = vk switch
            {
                VK_MOUSE_LEFT => isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
                VK_MOUSE_RIGHT => isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
                VK_MOUSE_MIDDLE => isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
                _ => 0
            };

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInputChecked(new[] { input });
        }
    }
}
