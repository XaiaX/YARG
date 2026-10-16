using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    /// <summary>
    /// JSON lifecycle tests for the Elite drums layout preset container. The container lives in
    /// Assembly-CSharp, which assembly definitions cannot reference, so every container
    /// interaction goes through reflection (the same pattern as EliteLayoutPlayerSelectionTests).
    /// YARG.Core types are referenced directly.
    /// </summary>
    public sealed class EliteDrumsLayoutContainerJsonLifecycleTests
    {
        private static readonly Type ContainerType =
            Type.GetType("YARG.Settings.Customization.EliteDrumsLayoutPresetContainer, Assembly-CSharp", true);

        private object _container;
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "eliteDrumsLayoutTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            // Redirect all container file IO into the temporary root
            ContainerType.GetProperty("ContentRootOverride").SetValue(null, _root);
            _container = Activator.CreateInstance(ContainerType);

            // Some tests write fixture files directly; make sure the content directory exists
            Directory.CreateDirectory((string) ContainerType.GetProperty("FullContentDirectory").GetValue(_container));
        }

        [TearDown]
        public void TearDown()
        {
            ContainerType.GetProperty("ContentRootOverride").SetValue(null, null);
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [Test]
        public void SupportedLayoutRoundTripsThroughPlainJson()
        {
            var preset = SupportedPreset("Roundtrip");
            object added = Invoke("AddPreset", preset);
            Assert.That(added, Is.SameAs(preset));

            string[] files = Directory.GetFiles(ContentDir, "*.json");
            Assert.That(files.Length, Is.EqualTo(1));
            Assert.That(Path.GetFileName(files[0]).Contains(preset.Id.ToString()[..8]), Is.True);

            string text = File.ReadAllText(files[0]);
            Assert.That(text.TrimStart().StartsWith("{"), Is.True, "export must be plain JSON, not an archive");
            Assert.That(text.Contains("\"Type\": \"EliteDrumsLayoutPreset\""), Is.True);

            // Adding the same preset again is skipped, like the other containers
            object addedAgain = Invoke("AddPreset", preset);
            Assert.That(addedAgain, Is.SameAs(preset));
            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(1));

            // Reload from disk in a fresh container and compare everything
            _container = Activator.CreateInstance(ContainerType);
            Invoke("Initialize");

            var loaded = GetPresets("CustomPresets");
            Assert.That(loaded.Count, Is.EqualTo(1));
            Assert.That(loaded[0].Id, Is.EqualTo(preset.Id));
            Assert.That(loaded[0].Name, Is.EqualTo("Roundtrip"));
            AssertSlotsEqual(preset, loaded[0]);

            // Usable list contains both built-ins plus the supported custom layout
            var usable = GetPresets("UsablePresets");
            Assert.That(usable.Count, Is.EqualTo(3));
            Assert.That(InvokeStatic("IsUsable", loaded[0]), Is.True);
        }

        [Test]
        public void UnsupportedMergeLayoutIsRetainedButNotUsable()
        {
            var preset = MergedPreset("Merged Layout");
            Invoke("AddPreset", preset);

            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(1));
            Assert.That(GetPresets("UsablePresets").Count, Is.EqualTo(2), "built-ins only");
            Assert.That(InvokeStatic("IsUsable", preset), Is.False);
            Assert.That(Invoke("GetUsablePresetById", preset.Id), Is.Null);
            Assert.That(Invoke("GetPresetById", preset.Id), Is.Not.Null);

            // Unsupported layouts stay exportable as plain JSON and deletable
            string exportPath = Path.Combine(_root, "merged-export.json");
            Invoke("ExportPreset", preset, exportPath);
            Assert.That(File.Exists(exportPath), Is.True);
            Assert.That(File.ReadAllText(exportPath).Contains("\"Type\": \"EliteDrumsLayoutPreset\""), Is.True);

            _container = Activator.CreateInstance(ContainerType);
            Invoke("Initialize");
            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(1));
            Assert.That(GetPresets("UsablePresets").Count, Is.EqualTo(2));

            // Delete through the reloaded instance, like the management UI would
            Invoke("DeletePreset", GetPresets("CustomPresets")[0]);
            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(0));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(0));
        }

        [Test]
        public void DuplicateJsonPropertyNamesAreRejected()
        {
            string duplicateName = LayoutJson(Guid.NewGuid(), "DupName", SupportedSlots())
                .Replace("\"Name\": \"DupName\",", "\"Name\": \"One\", \"Name\": \"DupName\",");
            string duplicateKind = LayoutJson(Guid.NewGuid(), "DupKind", SupportedSlots())
                .Replace("\"Kind\": \"Ordinary\",", "\"Kind\": \"Ordinary\", \"Kind\": \"Ordinary\",");
            Assert.That(duplicateName, Is.Not.EqualTo(duplicateKind));

            File.WriteAllText(Path.Combine(ContentDir, "duplicate-name.json"), duplicateName);
            File.WriteAllText(Path.Combine(ContentDir, "duplicate-kind.json"), duplicateKind);

            Invoke("Initialize");

            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(0));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(2),
                "bad files must be kept on disk");
        }

        [Test]
        public void MalformedJsonIsDiagnosedWithoutDeletingFile()
        {
            string malformed = "{ \"Name\": \"Broken\", \"Id\": \"" + Guid.NewGuid() + "\"";
            string path = Path.Combine(ContentDir, "malformed.json");
            File.WriteAllText(path, malformed);

            Invoke("Initialize");

            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(0));
            Assert.That(File.Exists(path), Is.True);

            // Importing the same malformed file also writes nothing anywhere
            object imported = Invoke("ImportPreset", path);
            Assert.That(imported, Is.Null);
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(1));
        }

        [Test]
        public void OversizedAndDeeplyNestedFilesAreRejected()
        {
            int maxBytes = (int) ContainerType.GetField("MAX_JSON_BYTES").GetValue(null);
            string oversized = "{ \"Name\": \"" + new string('x', maxBytes) + "\" }";
            string deep = "{\"Junk\":" + new string('[', 20) + new string(']', 20) + "}";

            string oversizedPath = Path.Combine(ContentDir, "oversized.json");
            string deepPath = Path.Combine(ContentDir, "deep.json");
            File.WriteAllText(oversizedPath, oversized);
            File.WriteAllText(deepPath, deep);

            Invoke("Initialize");

            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(0));
            Assert.That(File.Exists(oversizedPath), Is.True);
            Assert.That(File.Exists(deepPath), Is.True);
        }

        [Test]
        public void OversizedSerializationIsRejectedBeforeWrite()
        {
            // A name that passes the read-time bound must not be re-serialized into a file
            // this container could never load back
            var preset = SupportedPreset(new string('x', 70000));
            AssertInvokeThrows<InvalidOperationException>(() => Invoke("AddPreset", preset));
            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(0));
            Assert.That(Directory.GetFiles(ContentDir).Length, Is.EqualTo(0),
                "no file, not even a temporary one, may be left behind");

            var draft = SupportedPreset("Huge Name");
            draft.Name = new string('y', 70000);
            AssertInvokeThrows<InvalidOperationException>(() => Invoke("CommitPreset", draft));
            Assert.That(Directory.GetFiles(ContentDir).Length, Is.EqualTo(0));
        }

        [Test]
        public void UnknownSourceIdsAreRejectedBeforeWrite()
        {
            var preset = SupportedPreset("Unknown Source");
            preset.Slots[0].Drum = "gong";

            AssertInvokeThrows<InvalidOperationException>(() => Invoke("AddPreset", preset));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(0),
                "invalid layouts must never reach disk");

            // The same applies when the unknown ID arrives from a file
            string[] slots = SupportedSlots();
            slots[0] = SlotJson("Ordinary", "gong", null);
            string path = Path.Combine(ContentDir, "unknown-source.json");
            File.WriteAllText(path, LayoutJson(Guid.NewGuid(), "Unknown Source", slots));

            Invoke("Initialize");

            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(0));
            Assert.That(File.Exists(path), Is.True);
        }

        [Test]
        public void IncompleteLayoutIsRejectedBeforeWriteButRetainedFromFile()
        {
            var preset = SupportedPreset("Incomplete");
            preset.Slots[4].Cymbal = null;

            AssertInvokeThrows<InvalidOperationException>(() => Invoke("AddPreset", preset));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(0));

            // An incomplete hand-edited file stays loaded for management, but is not usable
            string[] slots = SupportedSlots();
            slots[4] = SlotJson("Ordinary", "tom3", null);
            Guid id = Guid.NewGuid();
            string path = Path.Combine(ContentDir, "incomplete.json");
            File.WriteAllText(path, LayoutJson(id, "Incomplete", slots));

            Invoke("Initialize");

            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(1));
            Assert.That(GetPresets("UsablePresets").Count, Is.EqualTo(2));
            Assert.That(Invoke("GetUsablePresetById", id), Is.Null);

            // SaveAll must not rewrite the incomplete file
            byte[] before = File.ReadAllBytes(path);
            Invoke("SaveAll");
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));

            // Incomplete layouts cannot be exported either, and the failure is thrown for the UI
            string exportPath = Path.Combine(_root, "incomplete-export.json");
            AssertInvokeThrows<InvalidOperationException>(
                () => Invoke("ExportPreset", GetPresets("CustomPresets")[0], exportPath));
            Assert.That(File.Exists(exportPath), Is.False);
        }

        [Test]
        public void BuiltinsAreProtectedAndImmutableCopies()
        {
            AssertInvokeThrows<InvalidOperationException>(
                () => Invoke("AddPreset", EliteDrumsLayoutPreset.Compact));
            AssertInvokeThrows<InvalidOperationException>(
                () => Invoke("DeletePreset", EliteDrumsLayoutPreset.Full));
            AssertInvokeThrows<InvalidOperationException>(
                () => Invoke("RenamePreset", EliteDrumsLayoutPreset.Compact, "Renamed"));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(0),
                "built-ins must never be written to disk");

            var first = (EliteDrumsLayoutPreset) Invoke("GetPresetById", EliteDrumsLayoutPreset.CompactId);
            var second = (EliteDrumsLayoutPreset) Invoke("GetPresetById", EliteDrumsLayoutPreset.CompactId);
            Assert.That(first, Is.Not.SameAs(second), "built-ins must be fresh copies");
            Assert.That(EliteDrumsLayoutValidator.Validate(first).IsRuntimeSupported, Is.True);

            first.Slots[0].Drum = null;
            Assert.That(second.Slots[0].Drum, Is.EqualTo("snare"),
                "mutating one built-in copy must not affect the next one");

            Assert.That(Invoke("GetUsablePresetById", EliteDrumsLayoutPreset.FullId), Is.Not.Null);

            // A mutated built-in copy is rejected on export; an intact one exports fine
            var corruptedBuiltin = EliteDrumsLayoutPreset.Full;
            corruptedBuiltin.Slots[3].Cymbal = "gong";
            AssertInvokeThrows<InvalidOperationException>(
                () => Invoke("ExportPreset", corruptedBuiltin, Path.Combine(_root, "corrupted.json")));
            Assert.That(File.Exists(Path.Combine(_root, "corrupted.json")), Is.False);

            string builtinExportPath = Path.Combine(_root, "compact-export.json");
            Invoke("ExportPreset", EliteDrumsLayoutPreset.Compact, builtinExportPath);
            Assert.That(File.Exists(builtinExportPath), Is.True);
        }

        [Test]
        public void ImportWithConflictingGuidBecomesNewIdentity()
        {
            var alpha = SupportedPreset("Alpha");
            Invoke("AddPreset", alpha);

            string exportPath = Path.Combine(_root, "alpha-export.json");
            Invoke("ExportPreset", alpha, exportPath);
            Assert.That(File.Exists(exportPath), Is.True);

            object imported = Invoke("ImportPreset", exportPath);
            Assert.That(imported, Is.Not.Null);
            var reidentified = (EliteDrumsLayoutPreset) imported;
            Assert.That(reidentified.Id, Is.Not.EqualTo(alpha.Id), "conflicting IDs become new identities");
            Assert.That(reidentified.Name, Is.EqualTo("Alpha"));
            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(2));

            // A file claiming a built-in ID is also imported under a new identity
            string builtinClonePath = Path.Combine(_root, "builtin-clone.json");
            File.WriteAllText(builtinClonePath,
                LayoutJson(EliteDrumsLayoutPreset.CompactId, "Compact Clone", SupportedSlots()));

            object cloneImport = Invoke("ImportPreset", builtinClonePath);
            Assert.That(cloneImport, Is.Not.Null);
            var clone = (EliteDrumsLayoutPreset) cloneImport;
            Assert.That(clone.Id, Is.Not.EqualTo(EliteDrumsLayoutPreset.CompactId));
            Assert.That(clone.Name, Is.EqualTo("Compact Clone"));
            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(3));
        }

        [Test]
        public void ImportPlanConflictAndCancelLeaveContainerUnchanged()
        {
            var alpha = SupportedPreset("Alpha");
            Invoke("AddPreset", alpha);

            string exportPath = Path.Combine(_root, "alpha-export.json");
            Invoke("ExportPreset", alpha, exportPath);

            object plan = Invoke("PlanImport", exportPath);
            Assert.That(GetPlanProperty(plan, "CanImport"), Is.True);
            Assert.That(GetPlanProperty(plan, "IdConflict"), Is.True,
                "the plan reports the conflict so the UI can inform or cancel");

            // Cancelling means simply not importing: the container stays untouched
            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(1));

            string malformedPath = Path.Combine(_root, "malformed.json");
            File.WriteAllText(malformedPath, "{ not json");

            object badPlan = Invoke("PlanImport", malformedPath);
            Assert.That(GetPlanProperty(badPlan, "CanImport"), Is.False);
            Assert.That(((IReadOnlyList<string>) GetPlanProperty(badPlan, "Errors")).Count, Is.GreaterThan(0));
            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateGuidFilesFirstWinsWithoutDeletion()
        {
            Guid id = Guid.NewGuid();
            string json = LayoutJson(id, "Original", SupportedSlots());
            string first = Path.Combine(ContentDir, "dupe-a.json");
            string second = Path.Combine(ContentDir, "dupe-b.json");
            File.WriteAllText(first, json);
            File.WriteAllText(second, json);

            Invoke("Initialize");

            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(1));
            Assert.That(File.Exists(first), Is.True);
            Assert.That(File.Exists(second), Is.True);
            Assert.That(GetPresets("CustomPresets")[0].Id, Is.EqualTo(id));
        }

        [Test]
        public void SaveValidatesFirstAndWritesAtomically()
        {
            var preset = SupportedPreset("Atomic");
            Invoke("AddPreset", preset);

            string[] files = Directory.GetFiles(ContentDir, "*.json");
            Assert.That(files.Length, Is.EqualTo(1));
            string before = File.ReadAllText(files[0]);

            // An in-memory corruption must not be written
            preset.Slots[2].Drum = "gong";
            Invoke("SaveAll");
            Assert.That(File.ReadAllText(files[0]), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(ContentDir, "*.tmp").Length, Is.EqualTo(0),
                "no temporary files may be left behind");

            // A valid overwrite round-trips through the same file
            preset.Slots[2].Drum = "tom1";
            Invoke("SaveAll");
            Assert.That(File.Exists(files[0]), Is.True);

            _container = Activator.CreateInstance(ContainerType);
            Invoke("Initialize");
            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(1));
        }

        [Test]
        public void CommitPresetValidatesAndReplacesDetachedDraftByIdentity()
        {
            var original = SupportedPreset("V1");
            Invoke("AddPreset", original);

            // Editor-style detached draft: same identity, new name and assignments
            var draft = original.Copy();
            draft.Name = "V2";
            draft.Slots[4] = EliteDrumsLayoutSlot.Ordinary("tom3", null);
            draft.Slots[5] = EliteDrumsLayoutSlot.Ordinary(null, "right-crash");

            object committed = Invoke("CommitPreset", draft);
            Assert.That(committed, Is.SameAs(draft));
            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(1));
            Assert.That(GetPresets("CustomPresets")[0], Is.SameAs(draft));
            Assert.That(GetPresets("CustomPresets")[0].Id, Is.EqualTo(original.Id),
                "identity is preserved across the commit");

            string[] files = Directory.GetFiles(ContentDir, "*.json");
            Assert.That(files.Length, Is.EqualTo(1), "the stale V1 file was cleaned up");
            Assert.That(File.ReadAllText(files[0]).Contains("V2"), Is.True);

            _container = Activator.CreateInstance(ContainerType);
            Invoke("Initialize");
            Assert.That(GetPresets("CustomPresets")[0].Name, Is.EqualTo("V2"));

            // Invalid, incomplete, and built-in drafts are rejected before any write
            var invalid = draft.Copy();
            invalid.Slots[2].Drum = "gong";
            AssertInvokeThrows<InvalidOperationException>(() => Invoke("CommitPreset", invalid));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(1));

            var incomplete = draft.Copy();
            incomplete.Slots[5].Cymbal = null;
            AssertInvokeThrows<InvalidOperationException>(() => Invoke("CommitPreset", incomplete));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(1));

            AssertInvokeThrows<InvalidOperationException>(
                () => Invoke("CommitPreset", EliteDrumsLayoutPreset.Compact));

            // A draft with an unknown ID is added as a brand-new preset
            var fresh = SupportedPreset("Fresh");
            object added = Invoke("CommitPreset", fresh);
            Assert.That(added, Is.SameAs(fresh));
            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(2));
        }

        [Test]
        public void ReloadPresetAtPathRemovesEntryWhenFileDisappears()
        {
            var preset = SupportedPreset("Volatile");
            Invoke("AddPreset", preset);

            string path = Directory.GetFiles(ContentDir, "*.json")[0];
            File.Delete(path);
            Invoke("ReloadPresetAtPath", path);

            Assert.That(GetPresets("CustomPresets").Count, Is.EqualTo(0));
        }

        [Test]
        public void LayoutSchema_DocumentedExampleImport()
        {
            // Locate the real documented example under the branch-level docs directory
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string branchRoot = Path.GetDirectoryName(projectRoot);
            Assert.That(branchRoot, Is.Not.Null, "could not locate the branch root");
            string examplePath = Path.Combine(branchRoot, "docs", "examples", "elite-drums-layout.json");
            Assert.That(File.Exists(examplePath), Is.True,
                "docs/examples/elite-drums-layout.json must exist next to the YARG project");

            string examplesDirectory = Path.GetDirectoryName(examplePath);
            string[] filesBefore = Directory.GetFiles(examplesDirectory);
            byte[] exampleBefore = File.ReadAllBytes(examplePath);

            // Import the real file through the production import path
            object imported = Invoke("ImportPreset", examplePath);
            Assert.That(imported, Is.Not.Null);
            var preset = (EliteDrumsLayoutPreset) imported;

            Assert.That(preset.Name, Is.EqualTo("Example Full with Separate Kicks"));
            Assert.That(preset.SchemaVersion, Is.EqualTo(EliteDrumsLayoutPreset.CURRENT_SCHEMA_VERSION));
            Assert.That(preset.Slots.Length, Is.EqualTo(EliteDrumsLayoutPreset.SLOT_COUNT));

            var validation = EliteDrumsLayoutValidator.Validate(preset);
            Assert.That(validation.IsStructurallyValid, Is.True, string.Join(" ", validation.Errors));
            Assert.That(validation.IsComplete, Is.True);
            Assert.That(validation.IsRuntimeSupported, Is.True);

            // Snapshot the resolved lane geometry of the documented layout
            Assert.That(EliteDrumsLayoutResolver.TryResolve(preset, out var resolved), Is.True);
            Assert.That(resolved.Lanes.Count, Is.EqualTo(10), "eight pads plus a split kick pair");
            Assert.That(resolved.KickMode, Is.EqualTo(EliteDrumsLayoutKickMode.Split));
            Assert.That(resolved.Lanes[8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Kick1));
            Assert.That(resolved.Lanes[9].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Kick2));
            Assert.That(resolved.SourceToLane["snare"], Is.EqualTo(0));
            Assert.That(resolved.SourceToLane["hi-hat"], Is.EqualTo(1));
            foreach (string source in EliteDrumsLayoutSources.All)
            {
                Assert.That(resolved.SourceToLane.ContainsKey(source), Is.True, "source {0} mapped", source);
            }

            // The documented example itself is never modified, and the docs folder gains nothing
            Assert.That(File.ReadAllBytes(examplePath), Is.EqualTo(exampleBefore));
            Assert.That(Directory.GetFiles(examplesDirectory), Is.EqualTo(filesBefore));
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(1),
                "the import lands only in the redirected content directory");

            // A malformed import writes no files at all
            string malformedPath = Path.Combine(_root, "example-malformed.json");
            File.WriteAllText(malformedPath, "{ \"Name\": ");
            Assert.That(Invoke("ImportPreset", malformedPath), Is.Null);
            Assert.That(Directory.GetFiles(ContentDir, "*.json").Length, Is.EqualTo(1));
        }

        private static EliteDrumsLayoutPreset SupportedPreset(string name)
        {
            var preset = new EliteDrumsLayoutPreset(name);
            preset.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", null);
            preset.Slots[1] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat);
            preset.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom1", "left-crash");
            preset.Slots[3] = EliteDrumsLayoutSlot.Ordinary("tom2", "ride");
            preset.Slots[4] = EliteDrumsLayoutSlot.Ordinary("tom3", "right-crash");
            return preset;
        }

        private static EliteDrumsLayoutPreset MergedPreset(string name)
        {
            // A canonical pair (tom1+tom2) covering both sources; neither tom1 nor tom2
            // is assigned individually anywhere, per the validator's merge coverage rule
            var preset = new EliteDrumsLayoutPreset(name);
            preset.Merges = new[] { new EliteDrumsLayoutMerge
                { EffectiveId = "tom1+tom2", Sources = new[] { "tom1", "tom2" } } };
            preset.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", null);
            preset.Slots[1] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat);
            preset.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom1+tom2", "left-crash");
            preset.Slots[3] = EliteDrumsLayoutSlot.Ordinary("tom3", "ride");
            preset.Slots[4] = EliteDrumsLayoutSlot.Ordinary(null, "right-crash");
            return preset;
        }

        private static string QuoteOrNull(string value)
        {
            return value is null ? "null" : "\"" + value + "\"";
        }

        private static string SlotJson(string kind, string drum, string cymbal)
        {
            return "{ \"Kind\": \"" + kind + "\", \"Drum\": " + QuoteOrNull(drum) +
                   ", \"Cymbal\": " + QuoteOrNull(cymbal) + " }";
        }

        private static string LayoutJson(Guid id, string name, params string[] slots)
        {
            var padded = new List<string>(slots);
            while (padded.Count < EliteDrumsLayoutPreset.SLOT_COUNT)
            {
                padded.Add(SlotJson("Empty", null, null));
            }

            return "{\n" +
                   "  \"Name\": \"" + name + "\",\n" +
                   "  \"Id\": \"" + id.ToString() + "\",\n" +
                   "  \"Type\": \"EliteDrumsLayoutPreset\",\n" +
                   "  \"SchemaVersion\": 1,\n" +
                   "  \"Slots\": [\n    " + string.Join(",\n    ", padded) + "\n  ]\n" +
                   "}";
        }

        private static string[] SupportedSlots()
        {
            return new[]
            {
                SlotJson("Ordinary", "snare", null),
                SlotJson("HiHat", null, "hi-hat"),
                SlotJson("Ordinary", "tom1", "left-crash"),
                SlotJson("Ordinary", "tom2", "ride"),
                SlotJson("Ordinary", "tom3", "right-crash")
            };
        }

        private static void AssertSlotsEqual(EliteDrumsLayoutPreset expected, EliteDrumsLayoutPreset actual)
        {
            Assert.That(actual.Slots.Length, Is.EqualTo(expected.Slots.Length));
            for (int i = 0; i < expected.Slots.Length; i++)
            {
                Assert.That(actual.Slots[i].Kind, Is.EqualTo(expected.Slots[i].Kind), "slot {0} kind", i);
                Assert.That(actual.Slots[i].Drum, Is.EqualTo(expected.Slots[i].Drum), "slot {0} drum", i);
                Assert.That(actual.Slots[i].Cymbal, Is.EqualTo(expected.Slots[i].Cymbal), "slot {0} cymbal", i);
            }
        }

        private static void AssertInvokeThrows<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TargetInvocationException invocation)
            {
                Assert.That(invocation.InnerException, Is.InstanceOf<TException>());
                return;
            }

            Assert.Fail("Expected " + typeof(TException).Name + " but nothing was thrown.");
        }

        private object Invoke(string methodName, params object[] args)
        {
            return ContainerType.GetMethod(methodName).Invoke(_container, args);
        }

        private static object InvokeStatic(string methodName, params object[] args)
        {
            return ContainerType.GetMethod(methodName).Invoke(null, args);
        }

        private object GetProperty(string propertyName)
        {
            return ContainerType.GetProperty(propertyName).GetValue(_container);
        }

        private static object GetPlanProperty(object plan, string propertyName)
        {
            return plan.GetType().GetProperty(propertyName).GetValue(plan);
        }

        private IReadOnlyList<EliteDrumsLayoutPreset> GetPresets(string propertyName)
        {
            return (IReadOnlyList<EliteDrumsLayoutPreset>) GetProperty(propertyName);
        }

        private string ContentDir
        {
            get { return (string) ContainerType.GetProperty("FullContentDirectory").GetValue(_container); }
        }
    }
}