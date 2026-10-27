using System.Collections.Generic;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Prüft die Übersetzung der XInput-Tastenmaske in Programm-Namen. Kern ist das
    /// Guide-Bit (0x0400): Es ist die einzige Quelle des Xbox-/HOME-Knopfes und kommt
    /// ausschließlich über den erweiterten Aufruf (Ordnungszahl 100). Ohne den Namen
    /// „HOME“ bleibt der Knopf in Oberfläche, Aufzeichnung und Kombinationen unsichtbar –
    /// genau das war der gemeldete Fehler (Log 27.10.2026: A ⇒ ex=0x1000, Xbox-Knopf ⇒
    /// ex=0x0400, dokumentierter Aufruf in beiden Fällen 0x0000).
    /// </summary>
    public class XInputButtonMapTests
    {
        private static List<string> Map(ushort mask, byte leftTrigger = 0, byte rightTrigger = 0)
        {
            var pressed = new List<string>();
            ControllerManager.AppendXInputButtons(mask, leftTrigger, rightTrigger, pressed);
            return pressed;
        }

        [Fact]
        public void GuideBit_WirdZuHome()
        {
            Assert.Contains("HOME", Map(0x0400));
        }

        [Fact]
        public void OhneGuideBit_KeinHome()
        {
            // Gegenprobe: jede andere Belegung darf HOME nicht erzeugen.
            Assert.DoesNotContain("HOME", Map(0x0000));
            Assert.DoesNotContain("HOME", Map(0xFBFF));   // alles außer 0x0400
        }

        [Fact]
        public void GuideBit_VerdraengtKeineAnderenTasten()
        {
            var pressed = Map(0x0400 | 0x1000 | 0x0010);   // HOME + A + START
            Assert.Contains("HOME", pressed);
            Assert.Contains("A", pressed);
            Assert.Contains("START", pressed);
            Assert.Equal(3, pressed.Count);
        }

        [Fact]
        public void StandardTasten_UndSteuerkreuz()
        {
            var pressed = Map(0x1000 | 0x2000 | 0x4000 | 0x8000 | 0x0100 | 0x0200
                              | 0x0020 | 0x0010 | 0x0001 | 0x0002 | 0x0004 | 0x0008);
            foreach (var name in new[] { "A", "B", "X", "Y", "L1", "R1", "SELECT", "START",
                                         "DPAD_UP", "DPAD_DOWN", "DPAD_LEFT", "DPAD_RIGHT" })
            {
                Assert.Contains(name, pressed);
            }
        }

        [Fact]
        public void Trigger_SchwelleBei30()
        {
            Assert.Contains("L2", Map(0, leftTrigger: 31));
            Assert.Contains("R2", Map(0, rightTrigger: 31));
            Assert.DoesNotContain("L2", Map(0, leftTrigger: 30));
            Assert.DoesNotContain("R2", Map(0, rightTrigger: 30));
        }
    }
}
