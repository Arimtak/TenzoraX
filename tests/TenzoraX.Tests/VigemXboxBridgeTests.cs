using System.Collections.Generic;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Testet die fehlertolerante ViGEm-Xbox-Spiegelung. Der virtuelle Controller
    /// funktioniert nur mit installiertem ViGEmBus-Treiber; ohne ihn (oder ohne die
    /// native ViGEmClient.dll) darf TryConnect NICHT crashen, sondern muss einen
    /// klaren Status setzen und false liefern. Mit installiertem Treiber muss
    /// TryConnect einen verbundenen virtuellen Controller liefern, dessen Spiegelung
    /// und die Trennung sauber ablaufen. Der Test läuft in beiden Umgebungen grün,
    /// weil er nur den Vertrag prüft: kein Crash, sinnvoller Status, sauberes Dispose.
    /// </summary>
    public class VigemXboxBridgeTests
    {
        [Fact]
        public void TryConnect_NeverCrashes_AndSetsAStatus()
        {
            using var bridge = new VigemXboxBridge();
            bool result = bridge.TryConnect();   // darf keine Exception werfen

            Assert.False(string.IsNullOrEmpty(bridge.Status));
            // entwedler verbunden, oder klare Fehlermeldung (Treiber/DLL fehlt)
            if (result)
                Assert.True(bridge.IsActive);
            else
                Assert.Contains("ViGEm", bridge.Status);
        }

        [Fact]
        public void ApplyState_WithoutConnection_IsNoOp()
        {
            using var bridge = new VigemXboxBridge();
            // Wenn kein virtueller Controller verbunden ist (typisch ohne Treiber),
            // darf ApplyState still nichts tun.
            bridge.ApplyState(new ControllerStateEventArgs
            {
                ControllerName = "Test",
                PressedButtons = new List<string> { "A", "DPAD_UP" },
                LeftStickX = -0.5
            });
            Assert.False(bridge.IsActive);
        }

        [Fact]
        public void UserIndex_MeldetDenSlotDesVirtuellenPads()
        {
            using var bridge = new VigemXboxBridge();

            // Ohne Verbindung gibt es keinen Slot.
            Assert.Equal(-1, bridge.UserIndex);

            if (!bridge.TryConnect()) return;   // nur sinnvoll, wenn Treiber da ist

            // Der Treiber meldet den Slot nicht zwingend im selben Moment – kurz warten.
            int slot = -1;
            for (int i = 0; i < 20 && slot < 0; i++)
            {
                slot = bridge.UserIndex;
                if (slot < 0) System.Threading.Thread.Sleep(100);
            }

            // Nur Slot 0-3 sind gültig; welche Nummer es wird, entscheidet die Maschine
            // (siehe XInputSlotFallbackTests für die Bewertung).
            Assert.InRange(slot, 0, 3);
        }

        [Fact]
        public void Disconnect_AfterSuccessfulConnect_IsClean()
        {
            using var bridge = new VigemXboxBridge();
            if (!bridge.TryConnect()) return;   // nur sinnvoll, wenn Treiber da ist

            Assert.True(bridge.IsActive);
            bridge.Disconnect();
            Assert.False(bridge.IsActive);
            Assert.Equal("Nicht aktiv", bridge.Status);
        }
    }
}