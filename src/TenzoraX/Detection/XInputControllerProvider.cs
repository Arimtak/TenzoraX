using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Erkennung über XInput (xinput1_4.dll) – Xbox-/XInput-kompatible Controller, Slots 0-3.
    /// </summary>
    public sealed class XInputControllerProvider : IControllerProvider
    {
        private const uint ERROR_SUCCESS = 0;

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        public string ApiName => "XInput";

        List<DetectedDeviceInfo> IControllerProvider.Enumerate() => Enumerate();

        /// <summary>Prüft alle vier XInput-Slots auf verbundene Controller.</summary>
        public static List<DetectedDeviceInfo> Enumerate()
        {
            var result = new List<DetectedDeviceInfo>();
            try
            {
                for (uint i = 0; i < 4; i++)
                {
                    try
                    {
                        if (XInputGetState(i, out _) == ERROR_SUCCESS)
                        {
                            result.Add(new DetectedDeviceInfo
                            {
                                Api = ControllerApi.XInput,
                                Name = $"Xbox-Controller (Slot {i})",
                                SlotIndex = (int)i,
                                Transport = "Unknown"   // USB/Bluetooth wird beim Zusammenführen aus dem HID-Befund übernommen
                            });
                        }
                    }
                    catch { }
                }
            }
            catch (DllNotFoundException)
            {
                // xinput1_4.dll fehlt – XInput einfach überspringen
            }
            catch (Exception ex)
            {
                App.LogApp("XInput-Enumeration fehlgeschlagen: " + ex.Message);
            }
            return result;
        }
    }
}
