using System;
using System.Collections.Generic;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace TenzoraX
{
    /// <summary>
    /// Bridged einen erkannten (auch fremden Bluetooth-/HID-)Controller auf einen
    /// virtuellen Xbox-360-Controller via ViGEm, damit Spiele, die nur XInput
    /// sprechen, ihn als echten Xbox-Controller sehen.
    ///
    /// Wichtig (ehrlich): Der virtuelle Controller wird erst sichtbar, wenn der
    /// ViGEmBus-Treiber auf dem System installiert ist (ViGEmBus ist seit 2023
    /// eingestellt worden – auf vielen Windows-11-Systemen daher nicht vorhanden).
    /// Diese Klasse erkennt das zuverlässig und meldet einen klaren Status statt
    /// stillschweigend zu scheitern.
    /// </summary>
    public sealed class VigemXboxBridge : IDisposable
    {
        private readonly object _lock = new();
        private ViGEmClient? _client;
        private IXbox360Controller? _pad;
        private bool _connected;
        private bool _disposed;

        /// <summary>Status der letzten Verbindung (für Anzeige in der UI).</summary>
        public string Status { get; private set; } = "Nicht aktiv";

        /// <summary>True, sobald ein virtueller Xbox-Controller verbunden ist.</summary>
        public bool IsActive
        {
            get { lock (_lock) { return _connected && _pad != null; } }
        }

        /// <summary>
        /// XInput-Slot (0–3) des virtuellen Pads, -1 wenn keiner verbunden ist.
        ///
        /// Nur Slot 0 bedeutet: Auch Spiele, die ausschließlich Slot 0 abfragen, finden
        /// den virtuellen Controller.
        /// </summary>
        public int UserIndex
        {
            get
            {
                lock (_lock)
                {
                    if (!_connected || _pad == null) return -1;
                    try { return _pad.UserIndex; }
                    catch { return -1; }
                }
            }
        }

        /// <summary>
        /// Versucht, den virtuellen Controller zu verbinden. Liefert bei fehlendem
        /// ViGEmBus-Treiber false (kein Crash) und setzt Status entsprechend.
        /// </summary>
        public bool TryConnect()
        {
            lock (_lock)
            {
                if (_disposed) return false;
                if (_connected) return true;

                try
                {
                    _client = new ViGEmClient();
                    _pad = _client.CreateXbox360Controller();
                    _pad.Connect();

                    // Rüstsicher: Fehlermeldung/Debug via AutoSubmitReport
                    try { _pad.AutoSubmitReport = true; } catch { }
                    _connected = true;
                    _lastApplyAt = DateTime.UtcNow;
                    Status = "Verbunden (Xbox 360 virtuell)";

                    // Slot 0 ist die einzige Nummer, die auch Spiele ohne Slot-Suche bedienen.
                    try { App.LogApp("ViGEm: virtuelles Pad verbunden – XInput-Slot " + _pad.UserIndex); }
                    catch { }
                    return true;
                }
                catch (VigemBusNotFoundException)
                {
                    Status = "ViGEmBus-Treiber nicht installiert";
                }
                catch (System.IO.FileNotFoundException)
                {
                    Status = "ViGEmBus-Treiber nicht installiert";
                }
                catch (System.DllNotFoundException)
                {
                    Status = "ViGEmBus-Treiber nicht installiert";
                }
                catch (System.TypeInitializationException tiex) when (CausedByMissingDriver(tiex))
                {
                    Status = "ViGEmBus-Treiber nicht installiert";
                }
                catch (VigemNoFreeSlotException)
                {
                    Status = "Kein freier ViGEm-Slot";
                }
                catch (Exception ex)
                {
                    Status = "ViGEm-Fehler: " + ex.Message;
                }
                CleanupInner();
                return false;
            }
        }

        /// <summary>True, wenn eine TypeInitializationException durch eine fehlende native Treiber-DLL verursacht wurde.</summary>
        private static bool CausedByMissingDriver(System.TypeInitializationException ex)
        {
            var e = ex.InnerException;
            while (e != null)
            {
                if (e is System.IO.FileNotFoundException || e is System.DllNotFoundException)
                    return true;
                e = e.InnerException;
            }
            return false;
        }

        /// <summary>
        /// Prüft zuverlässig, ob der ViGEmBus-Treiber auf dem System installiert ist
        /// (ohne die native DLL zu laden), anhand der Existenz des Windows-Diensts.
        /// </summary>
        public static bool IsDriverInstalled() => DriverInstaller.IsDriverInstalled("vigembus");

        /// <summary>Offizieller Download-Pfad für den eingestellten (aber weiterhin nutzbaren) ViGEmBus-Treiber.</summary>
        public const string DriverDownloadUrl = "https://github.com/nefarius/ViGEmBus/releases";

        public void Disconnect()
        {
            lock (_lock)
            {
                CleanupInner();
                Status = "Nicht aktiv";
            }
        }

        private void CleanupInner()
        {
            try { if (_pad != null) { _pad.Disconnect(); } } catch { }
            try { _pad = null; } catch { }
            try { _client?.Dispose(); } catch { }
            _client = null;
            _connected = false;
        }

        private DateTime _lastApplyAt = DateTime.MinValue;
        private long _applyCount;
        private int _lastPressedCount;

        /// <summary>Zeitpunkt der letzten Spiegelung (UTC). DateTime.MinValue = noch nie.</summary>
        public DateTime LastApplyAtUtc
        {
            get { lock (_lock) { return _lastApplyAt; } }
        }

        /// <summary>Anzahl der bisher gespiegelten Zustände (seit Verbindungsaufbau).</summary>
        public long ApplyCount
        {
            get { lock (_lock) { return _applyCount; } }
        }

        /// <summary>Anzahl der im letzten gespiegelten Zustand gedrückten Tasten.</summary>
        public int LastPressedCount
        {
            get { lock (_lock) { return _lastPressedCount; } }
        }

        /// <summary>
        /// Spiegelt den aktuellen Controller-Zustand auf den virtuellen Xbox-Controller.
        /// Aufruf vom Poll-Thread (synchron, ohne Dispatcher-Umweg).
        /// </summary>
        public void ApplyState(ControllerStateEventArgs state)
        {
            lock (_lock)
            {
                if (_disposed || !_connected || _pad == null) return;

                try
                {
                    // Buttons digital setzen (A/B/X/Y, Schulter, Daumen, Menu, D-Pad)
                    var pressed = state.PressedButtons ?? new List<string>();
                    _applyCount++;
                    _lastPressedCount = pressed.Count;
                    SetButton(_pad, Xbox360Button.A, Contains(pressed, "A"));
                    SetButton(_pad, Xbox360Button.B, Contains(pressed, "B"));
                    SetButton(_pad, Xbox360Button.X, Contains(pressed, "X"));
                    SetButton(_pad, Xbox360Button.Y, Contains(pressed, "Y"));
                    SetButton(_pad, Xbox360Button.LeftShoulder, Contains(pressed, "L1"));
                    SetButton(_pad, Xbox360Button.RightShoulder, Contains(pressed, "R1"));
                    SetButton(_pad, Xbox360Button.LeftThumb, Contains(pressed, "L3"));
                    SetButton(_pad, Xbox360Button.RightThumb, Contains(pressed, "R3"));
                    SetButton(_pad, Xbox360Button.Back, Contains(pressed, "SELECT"));
                    SetButton(_pad, Xbox360Button.Start, Contains(pressed, "START"));
                    SetButton(_pad, Xbox360Button.Up, Contains(pressed, "DPAD_UP"));
                    SetButton(_pad, Xbox360Button.Down, Contains(pressed, "DPAD_DOWN"));
                    SetButton(_pad, Xbox360Button.Left, Contains(pressed, "DPAD_LEFT"));
                    SetButton(_pad, Xbox360Button.Right, Contains(pressed, "DPAD_RIGHT"));

                    // Trigger: liegen im State nur als Buttons (Schwellwert) vor –
                    // digital → Slider 0 oder maximal.
                    SetSlider(_pad, Xbox360Slider.LeftTrigger, Contains(pressed, "L2"));
                    SetSlider(_pad, Xbox360Slider.RightTrigger, Contains(pressed, "R2"));

                    // Sticks (analog, -1..1 → -32768..32767)
                    SetAxis(_pad, Xbox360Axis.LeftThumbX, state.LeftStickX);
                    SetAxis(_pad, Xbox360Axis.LeftThumbY, state.LeftStickY);
                    SetAxis(_pad, Xbox360Axis.RightThumbX, state.RightStickX);
                    SetAxis(_pad, Xbox360Axis.RightThumbY, state.RightStickY);

                    if (_lastApplyAt == DateTime.MinValue || (DateTime.UtcNow - _lastApplyAt).TotalMilliseconds > 100)
                        App.LogApp("ViGEm: State gespiegelt (btns=" + pressed.Count + ")");
                    _lastApplyAt = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    // Verbindung kann mitten im Spiel reißen (Treiber-/Bus-Wechsel)
                    CleanupInner();
                    Status = "Verbindung verloren: " + ex.Message;
                    App.LogApp("ViGEm-ApplyState-Fehler: " + ex.Message);
                }
            }
        }

        private static bool Contains(List<string> pressed, string name)
        {
            if (pressed == null) return false;
            for (int i = 0; i < pressed.Count; i++)
                if (string.Equals(pressed[i], name, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void SetButton(IXbox360Controller pad, Xbox360Button button, bool down)
        {
            try { pad.SetButtonState(button, down); } catch { }
        }

        private static void SetSlider(IXbox360Controller pad, Xbox360Slider slider, bool on)
        {
            try { pad.SetSliderValue(slider, on ? byte.MaxValue : (byte)0); } catch { }
        }

        private static void SetAxis(IXbox360Controller pad, Xbox360Axis axis, double normalized)
        {
            try
            {
                short v = (short)Math.Clamp((int)Math.Round(normalized * short.MaxValue), short.MinValue, short.MaxValue);
                pad.SetAxisValue(axis, v);
            }
            catch { }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                CleanupInner();
            }
        }
    }
}