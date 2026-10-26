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
    /// 4) 32 Byte – 8BitDo Ultimate 3 Bluetooth (VID_2DC8&PID_202F), ReportID 0x01.
    ///    Diese Sammlung deklariert im HID-Descriptor KEINE Achsen/Buttons, deshalb
    ///    wird sie nur über diesen Parser ausgewertet. Layout (aus einer
    ///    Tastenaufzeichnung abgeleitet, Mitte jeweils 0x7F/0x80):
    ///    [1]=Hat (0=N, 2=O, 4=S, 6=W, 8=neutral), [2]=LX, [3]=LY, [4]=RX, [5]=RY,
    ///    [6]=RT analog, [7]=LT analog (gemessen), [8]/[9]=Tastenmaske,
    ///    [8] belegt wie bei der gesamten Ultimate-Familie: 0x01=A, 0x02=B,
    ///    0x08=X, 0x10=Y, 0x40=L1, 0x80=R1.
    ///    [10..13]=0, [14]=Akkustand (%), [15..29]=IMU, [30..31]=Zähler.
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

            // --- 32 Byte: 8BitDo Ultimate 3 Bluetooth ---
            // ReportID 0x01 als Schutz davor, 32-Byte-Reports fremder Geräte
            // fälschlich als dieses Layout zu lesen (dann neutral).
            if (report.Length == 32 && report[0] == 0x01)
            {
                byte b8 = report[8];
                byte b9 = report[9];

                MapHat(report[1], pressed);   // 8 = neutral, wird von MapHat ignoriert

                // Sticks: Mitte 0x7F/0x80, oben/links kleiner. Im Programm ist
                // oben/hoch positiv – daher Y-Achsen invertieren.
                lx = Normalize8(report[2]);
                ly = -Normalize8(report[3]);
                rx = Normalize8(report[4]);
                ry = -Normalize8(report[5]);

                // Trigger: LT analog in [7], RT analog in [6] (Einzelmessung),
                // zusätzlich als Digitalbit in [9]: 0x01 = LT, 0x02 = RT.
                if (report[7] > 0x20 || (b9 & 0x01) != 0) pressed.Add("L2");
                if (report[6] > 0x20 || (b9 & 0x02) != 0) pressed.Add("R2");

                // Face-Tasten wie bei 2C/2-Wireless/generisch (0x01=A, 0x02=B,
                // 0x08=X, 0x10=Y). Eine frühere Aufzeichnung hatte diese vier
                // Bits um eine Position verschoben, weil die Drücke nicht
                // einzeln getrennt waren – B/L1/R1 waren dort schon korrekt.
                MapUltimateFace(b8, pressed);

                if ((b9 & 0x04) != 0) pressed.Add("SELECT");
                if ((b9 & 0x08) != 0) pressed.Add("START");
                if ((b9 & 0x10) != 0) pressed.Add("HOME");
                if ((b9 & 0x20) != 0) pressed.Add("L3");
                if ((b9 & 0x40) != 0) pressed.Add("R3");

                return (pressed, lx, ly, rx, ry);
            }

            // --- 16 Byte: generisches 8BitDo-Layout (Ultimate Mobile) ---
            // ACHTUNG: 16 Byte ist auch das Format der Windows-Fassade
            // "XINPUT-kompatibles HID-Gerät" (VID_045E&PID_02FF, xinputhid). Dort
            // steht der feste Rahmen [2]=0x80, [3]=0xFF, [4]=0x7F; [8]=0x80 ist
            // dort ein Achsenbyte und KEIN R2. Ohne diese Sperre entstehen
            // dauerhaft "R2 gedrückt" und zappelnde Sticks (gemessen 26.10.2026
            // am 8BitDo Ultimate 3, PID_202F am Kabel).
            if ((report.Length == 16 || report.Length == 64) && IsXInputHidFrame(report))
                return (pressed, 0f, 0f, 0f, 0f);

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
        /// Erkennt den Eingaberrahmen des Windows-Treibers <c>xinputhid</c>, der zu
        /// jedem XInput-Pad als HID-Sammlung erscheint: 16 Byte, fester Rahmen
        /// [2]=0x80, [3]=0xFF, [4]=0x7F und keine Face-Tasten in [1]. Diese Reports
        /// sind kein 8BitDo-Layout und dürfen nie über den Fallback gedeutet werden.
        /// </summary>
        internal static bool IsXInputHidFrame(byte[]? report)
        {
            if (report == null || report.Length != 16) return false;
            return report[0] == 0x00 && report[1] == 0x00
                && report[2] == 0x80 && report[3] == 0xFF && report[4] == 0x7F;
        }

        /// <summary>8-Bit-Achse mit Mitte bei 0x7F (0x00 = -1, 0xFF = +1).</summary>
        internal static float Normalize8(byte value)
        {
            return Math.Clamp((value - 0x7F) / 127f, -1f, 1f);
        }
    }
}
