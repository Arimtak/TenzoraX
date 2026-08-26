using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Stellt sicher, dass die virtuelle Tastatur und die KeyData-Tabelle konsistent
    /// sind: Jede Taste, die die Oberfläche anbietet, muss auch tatsächlich
    /// simulierbar sein – inklusive korrekter Numpad-Scancodes.
    /// </summary>
    public class KeyDataTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TenzoraX.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string MainWindowXaml()
        {
            string path = Path.Combine(RepoRoot(), "src", "TenzoraX", "MainWindow.xaml");
            Assert.True(File.Exists(path), $"MainWindow.xaml nicht gefunden: {path}");
            return File.ReadAllText(path);
        }

        /// <summary>
        /// Alle Tasten der virtuellen Tastatur (Click="KeyboardKey_Click") müssen in
        /// KeyData vorhanden sein. Der gültige Schlüssel ist das Tag-Attribut, sonst
        /// der Content (Maus-Tasten nutzen Tags, Buchstaben/Ziffern den Content).
        /// </summary>
        [Fact]
        public void AllVirtualKeyboardKeys_ExistInKeyData()
        {
            string xaml = MainWindowXaml();

            var buttonRegex = new Regex(@"<Button\b[^>]*Click=""KeyboardKey_Click""[^>]*>");
            var attrRegex = new Regex(@"(Content|Tag)=""([^""]*)""");

            var keys = new List<string>();
            foreach (Match m in buttonRegex.Matches(xaml))
            {
                string? tag = null;
                string? content = null;
                foreach (Match a in attrRegex.Matches(m.Value))
                {
                    if (a.Groups[1].Value == "Tag") tag = a.Groups[2].Value;
                    else content = a.Groups[2].Value;
                }

                string? key = tag;
                if (key == null && content != null && !content.StartsWith("{Binding"))
                    key = content;

                Assert.False(string.IsNullOrEmpty(key),
                    "Keyboard-Taste ohne Tag und ohne festen Content gefunden: " + m.Value);
                keys.Add(key!);
            }

            Assert.NotEmpty(keys);
            foreach (var key in keys)
            {
                Assert.True(InputSimulator.KeyData.ContainsKey(key),
                    $"Taste '{key}' aus MainWindow.xaml fehlt in InputSimulator.KeyData");
            }
        }

        [Fact]
        public void KeyDataNames_AreValidIdentifiers()
        {
            var nameRegex = new Regex(@"^[A-Z0-9_]+$");
            foreach (var name in InputSimulator.KeyData.Keys)
            {
                Assert.Matches(nameRegex, name);
            }
        }

        [Theory]
        [InlineData("NUMPAD_0", 0x60, 0x52)]
        [InlineData("NUMPAD_1", 0x61, 0x4F)]
        [InlineData("NUMPAD_2", 0x62, 0x50)]
        [InlineData("NUMPAD_3", 0x63, 0x51)]
        [InlineData("NUMPAD_4", 0x64, 0x4B)]
        [InlineData("NUMPAD_5", 0x65, 0x4C)]
        [InlineData("NUMPAD_6", 0x66, 0x4D)]
        [InlineData("NUMPAD_7", 0x67, 0x47)]
        [InlineData("NUMPAD_8", 0x68, 0x48)]
        [InlineData("NUMPAD_9", 0x69, 0x49)]
        [InlineData("NUMPAD_DOT", 0x6E, 0x53)]
        [InlineData("NUMPAD_DIV", 0x6F, 0x35)]
        [InlineData("NUMPAD_MUL", 0x6A, 0x37)]
        [InlineData("NUMPAD_MINUS", 0x6D, 0x4A)]
        [InlineData("NUMPAD_PLUS", 0x6B, 0x4E)]
        public void NumpadKeys_HaveCorrectVkAndScanCode(string name, ushort expectedVk, ushort expectedScan)
        {
            Assert.True(InputSimulator.KeyData.TryGetValue(name, out var data),
                $"KeyData-Eintrag '{name}' fehlt");
            Assert.Equal(expectedVk, data.vk);
            Assert.Equal(expectedScan, data.scan);
        }

        [Fact]
        public void NumpadKeys_AreNotExtended()
        {
            foreach (var name in InputSimulator.KeyData.Keys.Where(k => k.StartsWith("NUMPAD_")))
            {
                Assert.False(InputSimulator.KeyData[name].ext, $"{name} darf nicht extended sein");
            }
        }

        [Fact]
        public void NoDuplicateVirtualKeys_AmongKeyboardEntries()
        {
            var keyboard = InputSimulator.KeyData.Where(kv => kv.Value.vk < 0xE000).ToList();
            var duplicate = keyboard
                .GroupBy(kv => kv.Value.vk)
                .FirstOrDefault(g => g.Count() > 1);
            Assert.Null(duplicate);
        }

        /// <summary>
        /// Scancodes dürfen sich nur unterscheiden, wenn mindestens einer extended
        /// ist (z. B. Numpad 4 = 0x4B vs. Pfeil-links = E0 0x4B). Identische
        /// (Scan, Extended)-Paare wären im SendInput-Scancode-Modus nicht unterscheidbar.
        /// </summary>
        [Fact]
        public void NoDuplicateScanCodeWithSameExtendedFlag()
        {
            var keyboard = InputSimulator.KeyData.Where(kv => kv.Value.vk < 0xE000).ToList();
            var duplicate = keyboard
                .GroupBy(kv => (kv.Value.scan, kv.Value.ext))
                .FirstOrDefault(g => g.Count() > 1);
            Assert.Null(duplicate);
        }

        [Fact]
        public void FunctionKeys_F1ToF24_AreSequential()
        {
            for (int i = 1; i <= 24; i++)
            {
                Assert.True(InputSimulator.KeyData.TryGetValue($"F{i}", out var data),
                    $"F{i} fehlt in KeyData");
                Assert.Equal((ushort)(0x70 + i - 1), data.vk);
            }
        }

        [Theory]
        [InlineData("A", 0x41)]
        [InlineData("Z", 0x5A)]
        [InlineData("0", 0x30)]
        [InlineData("9", 0x39)]
        [InlineData("ESC", 0x1B)]
        [InlineData("ENTER", 0x0D)]
        [InlineData("SPACE", 0x20)]
        public void StandardKeys_HaveExpectedVirtualKeys(string name, ushort expectedVk)
        {
            Assert.True(InputSimulator.KeyData.TryGetValue(name, out var data));
            Assert.Equal(expectedVk, data.vk);
        }

        [Fact]
        public void MouseButtons_UseCustomVkRange()
        {
            Assert.Equal(0xE001, InputSimulator.KeyData["MOUSE_LEFT"].vk);
            Assert.Equal(0xE002, InputSimulator.KeyData["MOUSE_RIGHT"].vk);
            Assert.Equal(0xE003, InputSimulator.KeyData["MOUSE_MIDDLE"].vk);
        }
    }
}
