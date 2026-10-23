using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class SongFolderStatusTests
    {
        [Test]
        public void FolderContainmentRespectsBoundariesAndNormalization()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("YARG.Menu.Settings.SettingsDirectory"))
                .First(candidate => candidate != null);
            bool Contains(string location, string folder) => (bool)type.GetMethod("IsWithinFolder")
                .Invoke(null, new object[] { location, folder });
            string root = Path.Combine(Path.GetTempPath(), "song-root");
            Assert.That(Contains(Path.Combine(root, "nested", "package.con"), root), Is.True);
            Assert.That(Contains(root, root + Path.DirectorySeparatorChar), Is.True);
            Assert.That(Contains(root + "-different/song", root), Is.False);
            Assert.That(Contains(Path.Combine(root, "nested", "..", "package.con"), root), Is.True);
            Assert.That(Contains("", root), Is.False);
            Assert.That(Contains(Path.GetPathRoot(root), Path.GetPathRoot(root)), Is.True);
        }
    }
}
