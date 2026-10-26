using System.Collections.Generic;
using Xunit;

namespace TenzoraX.Tests
{
    public class ControllerHistoryTests
    {
        private static ControllerHistoryEntry Entry(
            string name, string instanceId, string vidPid, string connection, int times,
            string first = "2026-10-26T12:00:00.0000000Z", string last = "2026-10-26T12:00:00.0000000Z")
        {
            return new ControllerHistoryEntry
            {
                Name = name,
                InstanceId = instanceId,
                VidPid = vidPid,
                StableId = ControllerHistoryManager.StableKey(instanceId, vidPid, name),
                ConnectionType = connection,
                TimesConnected = times,
                FirstSeen = first,
                LastSeen = last
            };
        }

        [Fact]
        public void SameInstanceInDifferentCaseIsOneEntry()
        {
            // Genau der Fall aus controller-history.json vom 26.10.2026:
            // gleiche Instanz, einmal klein (Raw Input), einmal groß (HID) geschrieben.
            var list = new List<ControllerHistoryEntry>
            {
                Entry("VID_045E&PID_02FF", @"HID\VID_045E&PID_02FF&IG_00\9&8581b18&0&0000",
                      "VID_045E&PID_02FF", "Unknown", 1,
                      first: "2026-10-26T13:23:00.3350336Z", last: "2026-10-26T13:23:00.3350336Z"),
                Entry("Xbox-Controller", @"HID\VID_045E&PID_02FF&IG_00\9&8581B18&0&0000",
                      "VID_045E&PID_02FF", "USB", 17,
                      first: "2026-10-26T12:22:10.1089990Z", last: "2026-10-26T14:50:23.1914287Z")
            };

            bool changed = ControllerHistoryManager.FoldDuplicates(list);

            Assert.True(changed);
            var only = Assert.Single(list);
            Assert.Equal("Xbox-Controller", only.Name);
            Assert.Equal("USB", only.ConnectionType);
            Assert.Equal(18, only.TimesConnected);
            Assert.Equal("2026-10-26T12:22:10.1089990Z", only.FirstSeen);
            Assert.Equal("2026-10-26T14:50:23.1914287Z", only.LastSeen);
        }

        [Fact]
        public void RealNameWinsOverPlaceholder()
        {
            var list = new List<ControllerHistoryEntry>
            {
                Entry("VID_2DC8&PID_3024", @"HID\VID_2DC8&PID_3024&IG_00\a&1163df2a&0&0000",
                      "VID_2DC8&PID_3024", "Unknown", 1),
                Entry("8BitDo Gamepad", @"HID\VID_2DC8&PID_3024&IG_00\A&1163DF2A&0&0000",
                      "VID_2DC8&PID_3024", "USB", 3)
            };

            ControllerHistoryManager.FoldDuplicates(list);

            var only = Assert.Single(list);
            Assert.Equal("8BitDo Gamepad", only.Name);
            Assert.Equal("USB", only.ConnectionType);
        }

        [Fact]
        public void DifferentInstancesStaySeparate()
        {
            var list = new List<ControllerHistoryEntry>
            {
                Entry("Xbox-Controller", @"HID\VID_045E&PID_02FF&IG_00\9&8581B18&0&0000", "VID_045E&PID_02FF", "USB", 2),
                Entry("8BitDo Gamepad", @"HID\VID_2DC8&PID_3024&IG_00\A&1163DF2A&0&0000", "VID_2DC8&PID_3024", "USB", 1)
            };

            Assert.False(ControllerHistoryManager.FoldDuplicates(list));
            Assert.Equal(2, list.Count);
        }

        [Fact]
        public void FoldIsIdempotent()
        {
            var list = new List<ControllerHistoryEntry>
            {
                Entry("VID_045E&PID_02FF", @"HID\X\1&a", "VID_045E&PID_02FF", "Unknown", 1),
                Entry("Xbox-Controller", @"HID\X\1&A", "VID_045E&PID_02FF", "USB", 1)
            };

            Assert.True(ControllerHistoryManager.FoldDuplicates(list));
            Assert.False(ControllerHistoryManager.FoldDuplicates(list));
            Assert.Single(list);
        }

        [Fact]
        public void StableKeyFallsBackFromInstanceToVidPidToName()
        {
            Assert.Equal(@"HID\X\1", ControllerHistoryManager.StableKey(@"HID\X\1", "VID_1&PID_2", "Name"));
            Assert.Equal("VID_1&PID_2", ControllerHistoryManager.StableKey("", "VID_1&PID_2", "Name"));
            Assert.Equal("Name", ControllerHistoryManager.StableKey("  ", "", "Name"));
            Assert.Equal("", ControllerHistoryManager.StableKey(null!, null!, null!));
        }

        [Fact]
        public void PlaceholderNameIsRecognised()
        {
            Assert.True(ControllerHistoryManager.IsPlaceholderName("", "VID_1&PID_2"));
            Assert.True(ControllerHistoryManager.IsPlaceholderName("   ", "VID_1&PID_2"));
            Assert.True(ControllerHistoryManager.IsPlaceholderName("VID_1&PID_2", "VID_1&PID_2"));
            Assert.True(ControllerHistoryManager.IsPlaceholderName("vid_1&pid_2", "VID_1&PID_2"));
            Assert.True(ControllerHistoryManager.IsPlaceholderName("Gamepad", "VID_1&PID_2"));
            Assert.False(ControllerHistoryManager.IsPlaceholderName("Xbox-Controller", "VID_045E&PID_02FF"));
            Assert.False(ControllerHistoryManager.IsPlaceholderName("8BitDo Gamepad", "VID_2DC8&PID_3024"));
        }

        [Fact]
        public void TimesConnectedAddUpAcrossThreeSpellings()
        {
            var list = new List<ControllerHistoryEntry>
            {
                Entry("VID_045E&PID_02FF", @"HID\X\1&a", "VID_045E&PID_02FF", "Unknown", 1),
                Entry("Xbox-Controller", @"HID\X\1&A", "VID_045E&PID_02FF", "USB", 4),
                Entry("Xbox-Controller", @"HID\X\1&A", "VID_045E&PID_02FF", "USB", 2)
            };

            ControllerHistoryManager.FoldDuplicates(list);

            var only = Assert.Single(list);
            Assert.Equal(7, only.TimesConnected);
            Assert.Equal("Xbox-Controller", only.Name);
            Assert.Equal("USB", only.ConnectionType);
        }
    }
}
