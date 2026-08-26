using System.Collections.Generic;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Testet den Editor-Ladevorgang einer gespeicherten Kombination: Anklicken in
    /// der Liste lädt Combo/Aktion/Text in den Capture-Zustand, damit die Kombination
    /// bearbeitet werden kann, ohne sie neu aufzunehmen.
    /// </summary>
    public class InputCaptureEngineTests
    {
        [Fact]
        public void LoadMapping_FillsComboActionAndText()
        {
            var engine = new InputCaptureEngine();
            var mapping = new Mapping
            {
                Combo = new List<string> { "LB", "SELECT" },
                Action = new List<string> { "SHIFT", "TAB" },
                Text = "Steam"
            };

            engine.LoadMapping(mapping);

            Assert.False(engine.IsCapturing);
            Assert.True(engine.HasCombo);
            Assert.True(engine.HasAction);
            Assert.Equal("LB + SELECT", engine.ComboDisplay);
            Assert.Equal("Steam", engine.ActionDisplay);
            Assert.Equal("Steam", engine.CurrentActionText);
        }

        [Fact]
        public void LoadMapping_KeyOnlyMapping_ShowsKeys()
        {
            var engine = new InputCaptureEngine();
            var mapping = new Mapping
            {
                Combo = new List<string> { "A" },
                Action = new List<string> { "F13" },
                Text = null
            };

            engine.LoadMapping(mapping);

            Assert.Equal("F13", engine.ActionDisplay);
            Assert.Null(engine.CurrentActionText);
            Assert.Contains("F13", engine.CurrentAction);
        }

        [Fact]
        public void LoadMapping_EndsActiveCapture()
        {
            var engine = new InputCaptureEngine();
            engine.StartCapture();
            engine.AddComboButton("LB");
            engine.AddActionKey("F13");

            engine.LoadMapping(new Mapping
            {
                Combo = new List<string> { "X" },
                Action = new List<string> { "F14" }
            });

            Assert.False(engine.IsCapturing);
            Assert.Equal("X", engine.ComboDisplay);
            Assert.Equal("F14", engine.ActionDisplay);
        }
    }
}
