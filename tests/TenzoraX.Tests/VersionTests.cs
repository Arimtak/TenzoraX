using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Stellt sicher, dass die Versionsnummer an allen Stellen konsistent ist:
    /// AppVersion.cs und die Version/FileVersion/AssemblyVersion in der csproj
    /// müssen zusammengehören, sonst wird beim Release eine falsche Version gepackt.
    /// </summary>
    public class VersionTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TenzoraX.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string CsprojPath()
            => Path.Combine(RepoRoot(), "src", "TenzoraX", "TenzoraX.csproj");

        private static string AppVersionPath()
            => Path.Combine(RepoRoot(), "src", "TenzoraX", "AppVersion.cs");

        [Fact]
        public void AppVersion_IsSemanticVersion()
        {
            Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), AppVersion.Current);
        }

        [Fact]
        public void AppVersion_MatchesCsprojVersion()
        {
            string csproj = File.ReadAllText(CsprojPath());
            var xml = XDocument.Parse(csproj);
            var propGroup = xml.Root!.Element("PropertyGroup");
            Assert.NotNull(propGroup);

            string? version = propGroup!.Element("Version")?.Value;
            Assert.Equal(AppVersion.Current, version);
        }

        [Fact]
        public void CsprojFileAndAssemblyVersion_AreVersionPlusZero()
        {
            string csproj = File.ReadAllText(CsprojPath());
            var xml = XDocument.Parse(csproj);
            var propGroup = xml.Root!.Element("PropertyGroup")!;

            string expected = AppVersion.Current + ".0";
            Assert.Equal(expected, propGroup.Element("FileVersion")?.Value);
            Assert.Equal(expected, propGroup.Element("AssemblyVersion")?.Value);
        }

        [Fact]
        public void AppVersionCs_DeclaresCurrentVersion()
        {
            string source = File.ReadAllText(AppVersionPath());
            Assert.Contains($"\"{AppVersion.Current}\"", source);
        }
    }
}
