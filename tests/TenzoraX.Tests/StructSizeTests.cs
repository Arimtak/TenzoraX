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
        public void JoyCapsW_HasNativeSize()
        {
            // joystickapi.h: 2 WORD + 32 WCHAR + 15 DWORD + 4 WORD + 32 WCHAR + 260 WCHAR.
            // Windows vergleicht cbjc mit sizeof(JOYCAPS); jede Abweichung ergibt
            // JOYERR_PARMS (165) und damit "kein Gamepad" für alle WinMM-Slots.
            Assert.Equal(720, Marshal.SizeOf<WinMMControllerProvider.JOYCAPSW>());
        }

        [Fact]
        public void JoyCapsW_InPipeline_HasSameNativeSize()
        {
            // Beide WinMM-Pfade (Detection + Live-Polling in ControllerManager) müssen
            // dieselbe Strukturgröße an joyGetDevCapsW melden.
            var managerField = typeof(TenzoraX.ControllerManager)
                .GetNestedType("JOYCAPSW", System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(managerField);
            Assert.Equal(720, Marshal.SizeOf(managerField!));
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
