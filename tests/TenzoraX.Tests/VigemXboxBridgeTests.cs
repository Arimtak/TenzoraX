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