using System.Collections.Generic;
using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Windows liefert dieselbe Geräteinstanz je nach Quelle unterschiedlich geschrieben
    /// (Raw Input klein, SetupAPI groß; '#' statt '\' aus Interface-Pfaden). Die Liste der
    /// Controller darf daraus keine zwei Einträge machen.
    /// </summary>
    public class InstanceKeyTests
    {
        private static DetectedController Ctrl(
            string instanceId,
            string name = "Pad",
            bool accessError = false)
        {
            return new DetectedController
            {
                Name = name,
                InstanceId = instanceId,
                StableId = DeviceHelpers.NormalizeInstanceKey(instanceId),
                AccessError = accessError,
                Connected = true
            };
        }

        [Theory]
        [InlineData(@"HID\VID_2DC8&PID_3024\9&8581B18&0&0000", @"hid\vid_2dc8&pid_3024\9&8581b18&0&0000")]
        [InlineData(@"  HID\VID_045E&PID_02FF&IG_00\9&8581B18  ", @"HID\VID_045E&PID_02FF&IG_00\9&8581B18")]
        public void NormalizeInstanceKey_IgnoresCaseAndWhitespace(string a, string b)
        {
            Assert.Equal(DeviceHelpers.NormalizeInstanceKey(a), DeviceHelpers.NormalizeInstanceKey(b));
        }

        [Fact]
        public void NormalizeInstanceKey_UnifiesHashAndBackslash()
        {
            Assert.Equal(
                DeviceHelpers.NormalizeInstanceKey(@"\\?\HID#VID_045E&PID_02FF#9&8581b18&0&0000"),
                DeviceHelpers.NormalizeInstanceKey(@"HID\VID_045E&PID_02FF\9&8581B18&0&0000"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NormalizeInstanceKey_EmptyStaysEmpty(string? instanceId)
        {
            Assert.Equal("", DeviceHelpers.NormalizeInstanceKey(instanceId));
        }

        [Fact]
        public void Collapse_SameInstanceDifferentCase_IsOneEntry()
        {
            var list = new List<DetectedController>
            {
                Ctrl(@"HID\VID_2DC8&PID_3024\9&8581B18&0&0000", "Ultimate 2"),
                Ctrl(@"HID\VID_2DC8&PID_3024\9&8581b18&0&0000", "Ultimate 2")
            };

            var collapsed = ControllerManager.CollapseDuplicateInstances(list);

            Assert.Single(collapsed);
            Assert.Equal("Ultimate 2", collapsed[0].Name);
        }

        [Fact]
        public void Collapse_HashAndBackslashSpelling_IsOneEntry()
        {
            var list = new List<DetectedController>
            {
                Ctrl(@"HID\VID_045E&PID_0B13\9&8581B18&0&0000"),
                Ctrl(@"\\?\HID#VID_045E&PID_0B13#9&8581b18&0&0000")
            };

            Assert.Single(ControllerManager.CollapseDuplicateInstances(list));
        }

        [Fact]
        public void Collapse_TwoUnitsOfSameModel_StayTwoEntries()
        {
            var list = new List<DetectedController>
            {
                Ctrl(@"HID\VID_2DC8&PID_3024\9&11111111&0&0000", "Pad A"),
                Ctrl(@"HID\VID_2DC8&PID_3024\9&22222222&0&0000", "Pad B")
            };

            Assert.Equal(2, ControllerManager.CollapseDuplicateInstances(list).Count);
        }

        [Fact]
        public void Collapse_EntriesWithoutInstanceId_AreKept()
        {
            // XInput-Slots und WinMM-Geräte ohne HID-Treffer haben keine Instanz-ID;
            // über VID/PID allein wären zwei Exemplare desselben Modells nicht trennbar.
            var list = new List<DetectedController>
            {
                new() { Name = "XInput-Pad", StableId = "XINPUT_SLOT_0", Connected = true },
                new() { Name = "WinMM-Pad", StableId = "VID_1234&PID_5678_WINMM", Connected = true }
            };

            Assert.Equal(2, ControllerManager.CollapseDuplicateInstances(list).Count);
        }

        [Fact]
        public void Collapse_ReportsKeptAndDroppedEntry()
        {
            // Grundlage der Log-Zeile: gemeldet wird das Paar, das zusammengefasst wurde.
            var first = Ctrl(@"HID\VID_2DC8&PID_3024\9&8581B18&0&0000", "ohne Zugriff", accessError: true);
            var second = Ctrl(@"hid\vid_2dc8&pid_3024\9&8581b18&0&0000", "mit Zugriff");
            var reports = new List<(string Kept, string Dropped)>();

            var collapsed = ControllerManager.CollapseDuplicateInstances(
                new List<DetectedController> { first, second },
                (kept, dropped) => reports.Add((kept.Name, dropped.Name)));

            Assert.Single(collapsed);
            Assert.Single(reports);
            Assert.Equal("mit Zugriff", reports[0].Kept);
            Assert.Equal("ohne Zugriff", reports[0].Dropped);
        }

        [Fact]
        public void DiagnosticsSection_ListsKeptAndDroppedSpellings()
        {
            var records = new List<CollapsedInstanceRecord>
            {
                new()
                {
                    At = new System.DateTime(2026, 10, 26, 18, 14, 2),
                    Key = @"HID\VID_2DC8&PID_3024\9&8581B18&0&0000",
                    KeptName = "Ultimate 2",
                    KeptInstanceId = @"HID\VID_2DC8&PID_3024\9&8581B18&0&0000",
                    KeptHasAccess = true,
                    DroppedName = "Ultimate 2",
                    DroppedInstanceId = @"HID\VID_2DC8&PID_3024\9&8581b18&0&0000",
                    DroppedHasAccess = false
                }
            };

            string text = ControllerManager.FormatCollapsedInstances(records);

            Assert.Contains("behalten 'Ultimate 2' (mit Zugriff)", text);
            Assert.Contains("verworfen 'Ultimate 2' (ohne Zugriff)", text);
            Assert.Contains(@"9&8581B18", text);
            Assert.Contains(@"9&8581b18", text);
        }

        [Fact]
        public void DiagnosticsSection_WithoutCollapses_SaysSo()
        {
            Assert.Contains("keine", ControllerManager.FormatCollapsedInstances(new List<CollapsedInstanceRecord>()));
            Assert.Contains("keine", ControllerManager.FormatCollapsedInstances(null));
        }

        [Fact]
        public void Collapse_KeepsEntryWithInputAccess()
        {
            var list = new List<DetectedController>
            {
                Ctrl(@"HID\VID_2DC8&PID_3024\9&8581B18&0&0000", "ohne Zugriff", accessError: true),
                Ctrl(@"hid\vid_2dc8&pid_3024\9&8581b18&0&0000", "mit Zugriff")
            };

            var collapsed = ControllerManager.CollapseDuplicateInstances(list);

            Assert.Single(collapsed);
            Assert.Equal("mit Zugriff", collapsed[0].Name);
            Assert.False(collapsed[0].AccessError);
        }
    }
}
