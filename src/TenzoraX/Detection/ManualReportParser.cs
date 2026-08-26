using System;
using System.Collections.Generic;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Manueller Fallback-Parser für Geräte, deren HID-Report keine Standard-HID-Usages
    /// deklariert. Es werden NUR exakt bekannte Report-Layouts interpretiert – unbekannte
    /// Längen liefern bewusst einen neutralen Zustand statt Fantasie-Buttons (sonst
    /// erscheinen bei neutraler Stellung beliebige Tasten als dauerhaft gedrückt).
    ///
    /// Bekannte Layouts (Byte-Layout empirisch durch Einzel-Button-Tests ermittelt):
    /// 1) 11 Byte – 8BitDo Ultimate 2C Bluetooth:
    ///    [0]=ReportID(01), [1]=Hat low nibble (0x0F=neutral), [2]=LX, [3]=LY,
    ///    [4]=RX, [5]=RY (0x7F Mitte), [6]=R2 analog, [7]=L2 analog,
    ///    [8]=0x01=A, 0x02=B, 0x08=X, 0x10=Y, 0x40=L1, 0x80=R1,
    ///    [9]=0x01=L2, 0x02=R2, 0x04=SELECT, 0x08=START, 0x10=HOME, 0x20=L3, 0x40=R3,
    ///    [10]=Checksumme.
    /// 2) 16 Byte – generisches 8BitDo-Layout (Ultimate Mobile-Tests):
    ///    [1]=Facebuttons, [2]=Menubuttons, [3]=Hat, [4..7]=Sticks, [8]=R2, [9]=L2.
    /// 3) 34 Byte – 8BitDo Ultimate 2 Wireless Bluetooth (VID_2DC8&PID_6012):
    ///    Daten-Bytes [0..9] identisch zum 2C-Layout ([1]=Hat, [2..5]=Sticks,
    ///    [6]=R2, [7]=L2, [8]=Face, [9]=Menu), dahinter Zusatz-Bytes
    ///    [10..13]=0 (Batterie/LED) und [14]/[15]=Checksumme/CRC; Rest Padding.
    /// </summary>
    internal static class ManualReportParser
    {
        public static (List<string> Pressed, float Lx, float Ly, float Rx, float Ry) Parse(byte[] report)
        {
            var pressed = new List<string>(16);
            float lx, ly, rx, ry;

            if (report == null || report.Length == 0)
                return (pressed, 0f, 0f, 0f, 0f);

            // --- 11 Byte: 8BitDo Ultimate 2C Bluetooth ---
            if (report.Length == 11)
            {
                MapUltimateFace(report[8], pressed);
                MapUltimateMenu(report[9], pressed);

                lx = Normalize8(report[2]);
                ly = -Normalize8(report[3]);
                rx = Normalize8(report[4]);
                ry = -Normalize8(report[5]);

                if (report[7] > 0x20) pressed.Add("L2");
                if (report[6] > 0x20) pressed.Add("R2");

                MapHat(report[1] & 0x0F, pressed);
                return (pressed, lx, ly, rx, ry);
            }

            // --- 34 Byte: 8BitDo Ultimate 2 Wireless Bluetooth ---
            // Daten-Bytes [0..9] wie beim 2C; [10..33] Zusatzdaten/Checksumme/Padding.
            if (report.Length == 34)
            {
                MapUltimateFace(report[8], pressed);
                MapUltimateMenu(report[9], pressed);

                lx = Normalize8(report[2]);
                ly = -Normalize8(report[3]);
                rx = Normalize8(report[4]);
                ry = -Normalize8(report[5]);

                if (report[7] > 0x20) pressed.Add("L2");
                if (report[6] > 0x20) pressed.Add("R2");

                MapHat(report[1] & 0x0F, pressed);
                return (pressed, lx, ly, rx, ry);
            }

            // --- 16 Byte: generisches 8BitDo-Layout (Ultimate Mobile) ---
            if (report.Length == 16 || report.Length == 64)
            {
                MapGenericFace(report[1], report[2], pressed);

                byte hat = report[3];
                if (hat != 0x0F && hat != 0xFF && hat < 8)
                    MapHat(hat, pressed);

                lx = Normalize8(report[4]);
                ly = -Normalize8(report[5]);
                rx = Normalize8(report[6]);
                ry = -Normalize8(report[7]);

                if (report[9] > 0x20) pressed.Add("L2");
                if (report[8] > 0x20) pressed.Add("R2");

                return (pressed, lx, ly, rx, ry);
            }

            // --- Unbekanntes Layout: neutral, NIE Fantasie-Buttons erzeugen ---
            return (pressed, 0f, 0f, 0f, 0f);
        }

        /// <summary>Ultimate-Familie, Face-Byte: A/B/X/Y/L1/R1.</summary>
        private static void MapUltimateFace(byte b, List<string> pressed)
        {
            if ((b & 0x01) != 0) pressed.Add("A");
            if ((b & 0x02) != 0) pressed.Add("B");
            if ((b & 0x08) != 0) pressed.Add("X");
            if ((b & 0x10) != 0) pressed.Add("Y");
            if ((b & 0x40) != 0) pressed.Add("L1");
            if ((b & 0x80) != 0) pressed.Add("R1");
        }

        /// <summary>Ultimate-Familie, Menu-Byte: L2/R2/SELECT/START/HOME/L3/R3.</summary>
        private static void MapUltimateMenu(byte b, List<string> pressed)
        {
            if ((b & 0x01) != 0) pressed.Add("L2");
            if ((b & 0x02) != 0) pressed.Add("R2");
            if ((b & 0x04) != 0) pressed.Add("SELECT");
            if ((b & 0x08) != 0) pressed.Add("START");
            if ((b & 0x10) != 0) pressed.Add("HOME");
            if ((b & 0x20) != 0) pressed.Add("L3");
            if ((b & 0x40) != 0) pressed.Add("R3");
        }

        /// <summary>Generisches Layout: Face/Menu verteilt auf zwei Bytes.</summary>
        private static void MapGenericFace(byte b1, byte b2, List<string> pressed)
        {
            if ((b1 & 0x01) != 0) pressed.Add("A");
            if ((b1 & 0x02) != 0) pressed.Add("B");
            if ((b1 & 0x08) != 0) pressed.Add("X");
            if ((b1 & 0x10) != 0) pressed.Add("Y");
            if ((b1 & 0x40) != 0) pressed.Add("L1");
            if ((b1 & 0x80) != 0) pressed.Add("R1");
            if ((b2 & 0x01) != 0) pressed.Add("L2");
            if ((b2 & 0x02) != 0) pressed.Add("R2");
            if ((b2 & 0x04) != 0) pressed.Add("SELECT");
            if ((b2 & 0x08) != 0) pressed.Add("START");
            if ((b2 & 0x10) != 0) pressed.Add("HOME");
            if ((b2 & 0x20) != 0) pressed.Add("L3");
            if ((b2 & 0x40) != 0) pressed.Add("R3");
        }

        /// <summary>Hat-Switch (0-7 Richtungen, 8+ oder 0x0F/0xFF = neutral).</summary>
        private static void MapHat(int hat, List<string> pressed)
        {
            if (hat > 7) return;
            if (hat == 0 || hat == 1 || hat == 7) pressed.Add("DPAD_UP");
            if (hat >= 1 && hat <= 3) pressed.Add("DPAD_RIGHT");
            if (hat >= 3 && hat <= 5) pressed.Add("DPAD_DOWN");
            if (hat >= 5 && hat <= 7) pressed.Add("DPAD_LEFT");
        }

        /// <summary>
        /// Akkustand aus bekannten Report-Layouts (0–100 direkt als Byte).
        /// 11-Byte-Layout (8BitDo Ultimate 2C / 2 Bluetooth): Byte [10] sinkt über
        /// die Nutzungsdauer monoton (log-verifiziert: 0x46=70 % → 0x3B=59 % über
        /// den Tag) – im Gegensatz zu den Bytes [10..13] des 34-Byte-Reports, die
        /// konstant sind und KEINEN Akku enthalten.
        /// </summary>
        public static byte? ReadBattery(byte[] report)
        {
            if (report == null || report.Length != 11) return null;
            byte v = report[10];
            return v >= 5 && v <= 100 ? v : null;
        }

        /// <summary>8-Bit-Achse mit Mitte bei 0x7F (0x00 = -1, 0xFF = +1).</summary>
        internal static float Normalize8(byte value)
        {
            return Math.Clamp((value - 0x7F) / 127f, -1f, 1f);
        }
    }
}
