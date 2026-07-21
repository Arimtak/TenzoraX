using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TenzoraX
{
    public class InputSimulator
    {
        // Import SendInput from User32.dll
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private const int INPUT_KEYBOARD = 1;
        private const int INPUT_MOUSE = 0;

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_XDOWN = 0x0080;
        private const uint MOUSEEVENTF_XUP = 0x0100;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        // Virtual Key Codes for important buttons
        public static readonly System.Collections.Generic.Dictionary<string, ushort> KeyCodes = new()
        {
            { "A", 0x41 }, { "B", 0x42 }, { "C", 0x43 }, { "D", 0x44 }, { "E", 0x45 },
            { "F", 0x46 }, { "G", 0x47 }, { "H", 0x48 }, { "I", 0x49 }, { "J", 0x4A },
            { "K", 0x4B }, { "L", 0x4C }, { "M", 0x4D }, { "N", 0x4E }, { "O", 0x4F },
            { "P", 0x50 }, { "Q", 0x51 }, { "R", 0x52 }, { "S", 0x53 }, { "T", 0x54 },
            { "U", 0x55 }, { "V", 0x56 }, { "W", 0x57 }, { "X", 0x58 }, { "Y", 0x59 },
            { "Z", 0x5A },
            { "0", 0x30 }, { "1", 0x31 }, { "2", 0x32 }, { "3", 0x33 }, { "4", 0x34 },
            { "5", 0x35 }, { "6", 0x36 }, { "7", 0x37 }, { "8", 0x38 }, { "9", 0x39 },
            { "ESC", 0x1B }, { "TAB", 0x09 }, { "ENTER", 0x0D }, { "SPACE", 0x20 },
            { "SHIFT", 0x10 }, { "CTRL", 0x11 }, { "ALT", 0x12 }, { "WIN", 0x5B },
            { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 }, { "F5", 0x74 },
            { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 }, { "F9", 0x78 }, { "F10", 0x79 },
            { "F11", 0x7A }, { "F12", 0x7B },
            { "F13", 0x7C }, { "F14", 0x7D }, { "F15", 0x7E }, { "F16", 0x7F },
            { "F17", 0x80 }, { "F18", 0x81 }, { "F19", 0x82 }, { "F20", 0x83 },
            { "F21", 0x84 }, { "F22", 0x85 }, { "F23", 0x86 }, { "F24", 0x87 },
            // Mouse mappings
            { "MOUSE_LEFT", 0xE001 },
            { "MOUSE_RIGHT", 0xE002 },
            { "MOUSE_MIDDLE", 0xE003 },
            // NumPad mappings
            { "NumLock", 0x90 },
            { "Num 0", 0x60 }, { "Num 1", 0x61 }, { "Num 2", 0x62 }, { "Num 3", 0x63 },
            { "Num 4", 0x64 }, { "Num 5", 0x65 }, { "Num 6", 0x66 }, { "Num 7", 0x67 },
            { "Num 8", 0x68 }, { "Num 9", 0x69 },
            { "Num /", 0x6F }, { "Num *", 0x6A }, { "Num -", 0x6D },
            { "Num +", 0x6B }, { "Num .", 0x6E }
        };

        /// <summary>
        /// Simulates pressing and releasing multiple keys sequentially (or simultaneously as combinations)
        /// </summary>
        public static void SimulateCombination(System.Collections.Generic.List<string> actionKeys)
        {
            if (actionKeys == null || actionKeys.Count == 0) return;

            int nKeys = actionKeys.Count;
            // We need 2 * nKeys inputs (down then up)
            INPUT[] inputs = new INPUT[nKeys * 2];

            for (int i = 0; i < nKeys; i++)
            {
                string key = actionKeys[i];
                if (!KeyCodes.TryGetValue(key, out ushort vk)) continue;

                if (vk >= 0xE000) // Special Mouse Actions
                {
                    inputs[i] = CreateMouseInput(vk, true);
                    inputs[inputs.Length - 1 - i] = CreateMouseInput(vk, false);
                }
                else // Keyboard Input
                {
                    inputs[i] = CreateKeyboardInput(vk, false);
                    inputs[inputs.Length - 1 - i] = CreateKeyboardInput(vk, true);
                }
            }

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>
        /// Simulates pressing down multiple keys (without releasing them)
        /// </summary>
        public static void SimulateKeyDown(System.Collections.Generic.List<string> actionKeys)
        {
            if (actionKeys == null || actionKeys.Count == 0) return;

            int nKeys = actionKeys.Count;
            INPUT[] inputs = new INPUT[nKeys];

            for (int i = 0; i < nKeys; i++)
            {
                string key = actionKeys[i];
                if (!KeyCodes.TryGetValue(key, out ushort vk)) continue;

                if (vk >= 0xE000) // Special Mouse Actions
                {
                    inputs[i] = CreateMouseInput(vk, true);
                }
                else // Keyboard Input
                {
                    inputs[i] = CreateKeyboardInput(vk, false);
                }
            }

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>
        /// Simulates releasing multiple keys
        /// </summary>
        public static void SimulateKeyUp(System.Collections.Generic.List<string> actionKeys)
        {
            if (actionKeys == null || actionKeys.Count == 0) return;

            int nKeys = actionKeys.Count;
            INPUT[] inputs = new INPUT[nKeys];

            // Release in reverse order to properly exit nested combinations
            for (int i = 0; i < nKeys; i++)
            {
                string key = actionKeys[nKeys - 1 - i];
                if (!KeyCodes.TryGetValue(key, out ushort vk)) continue;

                if (vk >= 0xE000) // Special Mouse Actions
                {
                    inputs[i] = CreateMouseInput(vk, false);
                }
                else // Keyboard Input
                {
                    inputs[i] = CreateKeyboardInput(vk, true);
                }
            }

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static INPUT CreateKeyboardInput(ushort vk, bool keyUp)
        {
            ushort scanCode = (ushort)MapVirtualKey(vk, 0);
            uint flags = KEYEVENTF_SCANCODE;
            if (keyUp) flags |= KEYEVENTF_KEYUP;

            // Mark extended keys (like WIN or arrow keys)
            if (vk == 0x5B || vk == 0x5C || vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28)
            {
                flags |= KEYEVENTF_EXTENDEDKEY;
            }

            return new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0, // Using scancode instead
                        wScan = scanCode,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        private static INPUT CreateMouseInput(ushort vk, bool isDown)
        {
            uint flags = 0;
            if (vk == 0xE001) flags = isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
            else if (vk == 0xE002) flags = isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
            else if (vk == 0xE003) flags = isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;

            return new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = 0,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        /// <summary>
        /// Moves mouse cursor by offset
        /// </summary>
        public static void MoveMouse(int dx, int dy)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0] = new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = 0,
                        dwFlags = MOUSEEVENTF_MOVE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
