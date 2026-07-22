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

        private const int INPUT_KEYBOARD = 1;
        private const int INPUT_MOUSE = 0;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

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
                    SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
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
                    SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
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
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
