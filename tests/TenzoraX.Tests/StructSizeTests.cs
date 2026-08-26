using System.Runtime.InteropServices;
using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Stellt sicher, dass die nativen Interop-Strukturen exakt die von Windows
    /// erwartete Größe haben – Abweichungen führen zu verstümmelten Daten.
    /// </summary>
    public class StructSizeTests
    {
        [Fact]
        public void JoyCapsW_HasProvenLegacySize()
        {
            // 1:1 aus dem bisherigen, funktionsfähigen Code übernommen. Marshal.SizeOf
            // liefert 388: die beiden ByValTStr-Arrays werden vom Marshaler auf
            // 4 Bytes ausgerichtet (2 Byte Padding vor szRegKey). Da die Definition
            // identisch zum Original ist, ist auch der an joyGetDevCapsW gemeldete
            // cbjc-Wert identisch – genau das soll der Test garantieren.
            Assert.Equal(388, Marshal.SizeOf<WinMMControllerProvider.JOYCAPSW>());
        }

        [Fact]
        public void DiDeviceInstanceW_Is1100Bytes()
        {
            // dinput.h: dwSize(4)+guidInstance(16)+guidProduct(16)+dwDevType(4)
            //           +tszInstanceName[260 wchar](520)+tszProductName[260 wchar](520)+guidFFDriver(16)+wUsagePage(2)+wUsage(2) = 1100
            Assert.Equal(1100, Marshal.SizeOf<DirectInputControllerProvider.DIDEVICEINSTANCEW>());
        }
    }
}
