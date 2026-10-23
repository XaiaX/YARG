using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class SongContainerStagingTests
    {
        private const BindingFlags STATIC_PRIVATE = BindingFlags.Static | BindingFlags.NonPublic;
        private Type _container;
        private Dictionary<FieldInfo, object> _originalFields;
        private DictionaryEntry[] _originalStars;

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        [OneTimeSetUp]
        public void InitializeAudio()
        {
            // SettingContainer callbacks require the same audio initialization as startup.
            Production("YARG.Helpers.PathHelper").GetMethod("Init").Invoke(null, null);
            var audio = Production("YARG.Core.Audio.GlobalAudioHandler");
            if (audio.GetField("_instance", STATIC_PRIVATE).GetValue(null) == null)
                audio.GetMethod("Initialize").MakeGenericMethod(Production("YARG.Audio.BASS.BassAudioManager"))
                    .Invoke(null, null);
            var settingsManager = Production("YARG.Settings.SettingsManager");
            var settings = settingsManager.GetProperty("Settings");
            if (settings.GetValue(null) == null)
            {
                var settingsType = settingsManager.GetNestedType("SettingContainer");
                settings.SetValue(null, Activator.CreateInstance(settingsType));
            }
        }

        [SetUp]
        public void SavePublishedState()
        {
            _container = Production("YARG.Song.SongContainer");
            _originalFields = _container.GetFields(STATIC_PRIVATE)
                .Where(field => !field.IsInitOnly && !field.IsLiteral)
                .ToDictionary(field => field, field => field.GetValue(null));
            var stars = (IDictionary) Field("_runtimeStars").GetValue(null);
            _originalStars = stars.Keys.Cast<object>()
                .Select(key => new DictionaryEntry(key, stars[key])).ToArray();
            Field("_containerBuildCompletedForTesting").SetValue(null, null);
        }

        [TearDown]
        public void RestorePublishedState()
        {
            foreach (var pair in _originalFields)
                pair.Key.SetValue(null, pair.Value);
            var stars = (IDictionary) Field("_runtimeStars").GetValue(null);
            stars.Clear();
            foreach (var entry in _originalStars)
                stars.Add(entry.Key, entry.Value);
        }

        [Test]
        public void CandidateBuildFailurePreservesPublishedContainersAndRevision()
        {
            PublishEmptyCandidate();
            Field("_starsCacheValid").SetValue(null, true);
            var published = _originalFields.Keys.ToDictionary(field => field, field => field.GetValue(null));
            bool buildCompleted = false;
            var expected = new InvalidOperationException("Injected container build failure");
            Field("_containerBuildCompletedForTesting").SetValue(null, (Action) (() =>
            {
                buildCompleted = true;
                throw expected;
            }));

            var exception = Assert.Throws<TargetInvocationException>(() => PublishEmptyCandidate());

            Assert.That(buildCompleted, Is.True, "The actual candidate build must reach the failure boundary.");
            Assert.That(exception.InnerException, Is.SameAs(expected));
            foreach (var pair in published)
            {
                if (pair.Key.Name == "_containerBuildCompletedForTesting")
                    continue;
                if (pair.Key.FieldType.IsValueType)
                    Assert.That(pair.Key.GetValue(null), Is.EqualTo(pair.Value), pair.Key.Name);
                else
                    Assert.That(pair.Key.GetValue(null), Is.SameAs(pair.Value), pair.Key.Name);
            }
        }

        [Test]
        public void SameCountCandidatePublishesNewContainersAndInvalidatesStarsOnce()
        {
            PublishEmptyCandidate();
            int revision = (int) _container.GetProperty("LibraryRevision").GetValue(null);
            var cache = Field("_songCache").GetValue(null);
            var sorted = Field("_sortedSongs").GetValue(null);
            var hash = Field("_songsByHash").GetValue(null);
            var instruments = Field("_sortInstruments").GetValue(null);
            var titles = Field("_sortTitles").GetValue(null);
            int count = (int) _container.GetProperty("Count").GetValue(null);
            Field("_starsCacheValid").SetValue(null, true);
            bool buildCompleted = false;
            Field("_containerBuildCompletedForTesting").SetValue(null, (Action) (() =>
            {
                buildCompleted = true;
                Assert.That(Field("_songCache").GetValue(null), Is.SameAs(cache));
                Assert.That(Field("_songsByHash").GetValue(null), Is.SameAs(hash));
                Assert.That(Field("_starsCacheValid").GetValue(null), Is.True);
                Assert.That(_container.GetProperty("LibraryRevision").GetValue(null), Is.EqualTo(revision));
            }));

            var build = _container.GetMethod("BuildContainers", STATIC_PRIVATE);
            var candidate = build.GetParameters()
                .Select(parameter => Activator.CreateInstance(parameter.ParameterType)).ToArray();
            var publish = (Action) build.Invoke(null, candidate);
            Assert.That(Field("_songCache").GetValue(null), Is.SameAs(cache));
            Assert.That(Field("_sortedSongs").GetValue(null), Is.SameAs(sorted));
            Assert.That(Field("_sortTitles").GetValue(null), Is.SameAs(titles));
            Assert.That(Field("_sortInstruments").GetValue(null), Is.SameAs(instruments));
            Assert.That(Field("_starsCacheValid").GetValue(null), Is.True);
            Assert.That(_container.GetProperty("LibraryRevision").GetValue(null), Is.EqualTo(revision));

            publish();

            Assert.That(buildCompleted, Is.True);
            Assert.That(_container.GetProperty("Count").GetValue(null), Is.EqualTo(count));
            Assert.That(_container.GetProperty("LibraryRevision").GetValue(null), Is.EqualTo(revision + 1));
            Assert.That(Field("_songCache").GetValue(null), Is.Not.SameAs(cache));
            Assert.That(Field("_sortedSongs").GetValue(null), Is.Not.SameAs(sorted));
            Assert.That(Field("_songsByHash").GetValue(null), Is.Not.SameAs(hash));
            Assert.That(Field("_sortInstruments").GetValue(null), Is.Not.SameAs(instruments));
            Assert.That(Field("_sortTitles").GetValue(null), Is.Not.SameAs(titles));
            Assert.That(Field("_starsCacheValid").GetValue(null), Is.False);
        }

        private FieldInfo Field(string name) => _container.GetField(name, STATIC_PRIVATE);

        private void PublishEmptyCandidate()
        {
            var fill = _container.GetMethod("FillContainers", STATIC_PRIVATE);
            var parameters = fill.GetParameters();
            fill.Invoke(null, parameters.Select(parameter => Activator.CreateInstance(parameter.ParameterType)).ToArray());
        }
    }
}
