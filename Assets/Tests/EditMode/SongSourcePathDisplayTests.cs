using System;
using System.Linq;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class SongSourcePathDisplayTests
    {
        private static class SongSourcePathDisplay
        {
            public static string FitPath(string path, float width, Func<string, float> measure)
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("YARG.Menu.Settings.SongSourcePathDisplay"))
                    .First(candidate => candidate != null);
                return (string)type.GetMethod("FitPath").Invoke(null, new object[] { path, width, measure });
            }
        }

        [TestCase("/Volumes/media/Songs/Clone Hero/The Useful Folder", "/Volumes/")]
        [TestCase("/Users/xaiax/Local/Updates/songs_updates.dta", "/Users/")]
        [TestCase("C:\\Users\\Player\\Updates\\songs_updates.dta", "C:\\")]
        [TestCase("\\\\server\\share\\Updates\\songs_updates.dta", "\\\\server\\")]
        public void LongPathsRetainPrefixAndTail(string path, string prefix)
        {
            string result = SongSourcePathDisplay.FitPath(path, prefix.Length + 26, s => s.Length);
            Assert.That(result, Does.StartWith(prefix + "…"));
            Assert.That(result.Length, Is.LessThanOrEqualTo(prefix.Length + 26));
            Assert.That(path, Does.EndWith(result.Substring(result.IndexOf('…') + 1)));
        }

        [Test]
        public void ShortPathsAreUnchanged()
        {
            Assert.That(SongSourcePathDisplay.FitPath("/Users/file.dta", 100, s => s.Length),
                Is.EqualTo("/Users/file.dta"));
        }

        [Test]
        public void NarrowRowsPreferWholeFilenameToPrefix()
        {
            Assert.That(SongSourcePathDisplay.FitPath("/Users/a/long/file.dta", 9, s => s.Length),
                Is.EqualTo("…file.dta"));
        }

        [Test]
        public void TinyWidthNeverReturnsOversizedEllipsis()
        {
            Assert.That(SongSourcePathDisplay.FitPath("/Volumes/example", 0.5f, s => s.Length), Is.Empty);
        }

        [Test]
        public void OversizedFirstComponentDoesNotDiscardUsefulTail()
        {
            string result = SongSourcePathDisplay.FitPath("/averylongfirstcomponent/file.dta", 12, s => s.Length);
            Assert.That(result.Length, Is.LessThanOrEqualTo(12));
            Assert.That(result, Does.EndWith("file.dta"));
        }

        [Test]
        public void ProportionalMeasurementDeterminesFit()
        {
            float Measure(string text)
            {
                float width = 0;
                foreach (char c in text) width += c == 'W' ? 3 : 1;
                return width;
            }
            string path = "/Users/WWWWWWWWWWWW/Updates/tiny.dta";
            string result = SongSourcePathDisplay.FitPath(path, 27, Measure);
            Assert.That(Measure(result), Is.LessThanOrEqualTo(27));
            Assert.That(result, Does.StartWith("/Users/…"));
            Assert.That(result, Does.EndWith("tiny.dta"));
        }
    }
}
