using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class SongLibraryPublicationTests
    {
        [Test]
        public void SameCountPublicationInvalidatesFilterValues()
        {
            var songs = Type.GetType("YARG.Song.SongContainer, Assembly-CSharp", true);
            var filters = Type.GetType("YARG.Menu.Filters.FiltersMenu, Assembly-CSharp", true);
            var revision = songs.GetField("<LibraryRevision>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            var getValues = filters.GetMethod("GetAllCached", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(revision, Is.Not.Null);
            Assert.That(getValues, Is.Not.Null);
            int originalRevision = (int) revision.GetValue(null);
            try
            {
                int builds = 0;
                Func<IReadOnlyList<string>> build = () => new[] { (++builds).ToString() };
                object[] arguments = { null, -1, build };
                revision.SetValue(null, originalRevision + 1);
                var first = getValues.Invoke(null, arguments);
                Assert.That(getValues.Invoke(null, arguments), Is.SameAs(first));
                Assert.That(builds, Is.EqualTo(1));
                // Song count is unchanged. A successful publication changes the revision.
                revision.SetValue(null, originalRevision + 2);
                Assert.That(getValues.Invoke(null, arguments), Is.Not.SameAs(first));
                Assert.That(builds, Is.EqualTo(2));
            }
            finally
            {
                revision.SetValue(null, originalRevision);
            }
        }
    }
}
