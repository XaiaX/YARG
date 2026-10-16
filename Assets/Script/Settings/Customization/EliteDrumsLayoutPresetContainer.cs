using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Helpers;
using YARG.Settings.Metadata;

namespace YARG.Settings.Customization
{
    /// <summary>
    /// Stores custom Elite drums layout presets as individual plain UTF-8 JSON files.
    /// Derived from <see cref="CustomContent"/> directly (not <see cref="CustomContent{T}"/>) so
    /// that files on disk are never renamed or deleted without an explicit user action, and so
    /// every write can be gated on validation first. Presets are exchanged as plain JSON rather
    /// than zip archives, and this container never shows dialogs itself: import/export outcomes
    /// are reported through <see cref="PlanImport"/> and return values for the calling UI.
    /// </summary>
    public class EliteDrumsLayoutPresetContainer : CustomContent
    {
        public const string PRESET_TYPE_STRING_NAME = nameof(EliteDrumsLayoutPreset);

        /// <summary>Upper bound for a single layout JSON file, in bytes.</summary>
        public const int MAX_JSON_BYTES = 64 * 1024;

        /// <summary>Upper bound for JSON nesting depth while reading a layout file.</summary>
        public const int MAX_JSON_DEPTH = 8;

        /// <summary>
        /// When set to an absolute path, all content is rooted under that directory instead of
        /// the persistent customization directory. This exists for tests; because the path is
        /// absolute, the base class's <c>FullContentDirectory</c> combination resolves to it.
        /// Set to null to restore the default location.
        /// </summary>
        public static string? ContentRootOverride { get; set; }

        private readonly List<EliteDrumsLayoutPreset> _content = new();

        protected override string ContentDirectory => ContentRootOverride is null
            ? "eliteDrumsLayoutPresets"
            : Path.Combine(ContentRootOverride, "eliteDrumsLayoutPresets");

        /// <summary>
        /// Fresh copies of the built-in layouts. A new list with new instances is built on every
        /// access so callers can never mutate the built-in definitions.
        /// </summary>
        public IReadOnlyList<EliteDrumsLayoutPreset> DefaultPresets =>
            new[] { EliteDrumsLayoutPreset.Compact, EliteDrumsLayoutPreset.Full };

        public override IReadOnlyList<BasePreset> DefaultBasePresets => DefaultPresets;

        /// <summary>All retained custom presets, including unsupported and incomplete ones.</summary>
        public IReadOnlyList<EliteDrumsLayoutPreset> CustomPresets => _content;

        /// <summary>
        /// All retained custom presets for management UI. Unsupported (merged-assignment) layouts
        /// can be stored, exported, and deleted even though they cannot be selected for gameplay.
        /// Incomplete layouts are recovery-only: retained so they can be inspected and deleted,
        /// but never rewritten or exported.
        /// </summary>
        public override IReadOnlyList<BasePreset> CustomBasePresets => _content;

        /// <summary>
        /// The presets usable for gameplay/activation: fresh built-in copies followed by every
        /// custom preset that is complete and runtime supported.
        /// </summary>
        public IReadOnlyList<EliteDrumsLayoutPreset> UsablePresets
        {
            get
            {
                var usable = new List<EliteDrumsLayoutPreset>(DefaultPresets);
                usable.AddRange(_content.Where(IsUsable));
                return usable;
            }
        }

        /// <summary>True when the layout is structurally valid, complete, and runtime supported.</summary>
        public static bool IsUsable(EliteDrumsLayoutPreset? preset) =>
            EliteDrumsLayoutValidator.Validate(preset).IsRuntimeSupported;

        public override void Initialize()
        {
            base.Initialize();
            LoadFiles();
        }

        public override BasePreset AddPreset(BasePreset preset)
        {
            if (preset is not EliteDrumsLayoutPreset layout)
            {
                throw new InvalidOperationException("Attempted to add invalid preset type.");
            }

            if (IsBuiltinId(layout.Id))
            {
                throw new InvalidOperationException("Built-in Elite drums layouts cannot be added as custom presets.");
            }

            // Skip if the user already has the preset
            if (HasPresetId(layout.Id))
            {
                return GetPresetById(layout.Id)!;
            }

            // Validate before writing anything, and save before mutating memory
            ThrowIfSaveBlocked(layout);
            SavePresetFile(layout);
            _content.Add(layout);

            return layout;
        }

        public override BasePreset CopyPreset(BasePreset original, BasePreset copy)
        {
            return AddPreset(copy);
        }

        /// <summary>
        /// Commits a (possibly detached) draft of a custom layout, such as an edited copy from
        /// the layout editor. The draft is validated first, its file is written before any
        /// in-memory state changes, and it then replaces the stored preset with the same ID
        /// (preserving that identity) or is added when the ID is new. Built-in IDs are rejected.
        /// </summary>
        public EliteDrumsLayoutPreset CommitPreset(EliteDrumsLayoutPreset draft)
        {
            if (draft is null)
            {
                throw new ArgumentNullException(nameof(draft));
            }

            if (IsBuiltinId(draft.Id))
            {
                throw new InvalidOperationException("Built-in Elite drums layouts cannot be committed.");
            }

            // Validate before writing anything
            ThrowIfSaveBlocked(draft);

            var existing = _content.FirstOrDefault(preset => preset.Id == draft.Id);
            SavePresetFile(draft);

            if (existing is null)
            {
                _content.Add(draft);
            }
            else
            {
                _content[_content.IndexOf(existing)] = draft;
                DeleteStalePresetFiles(draft);
            }

            return draft;
        }

        public override void DeletePreset(BasePreset preset)
        {
            if (preset is not EliteDrumsLayoutPreset layout)
            {
                throw new InvalidOperationException("Attempted to delete invalid preset type.");
            }

            if (IsBuiltinId(layout.Id))
            {
                throw new InvalidOperationException("Built-in Elite drums layouts cannot be deleted.");
            }

            _content.Remove(layout);
            DeletePresetFiles(layout);
        }

        public override void RenamePreset(BasePreset preset, string name)
        {
            if (preset is not EliteDrumsLayoutPreset layout)
            {
                throw new InvalidOperationException("Attempted to rename invalid preset type.");
            }

            if (IsBuiltinId(layout.Id))
            {
                throw new InvalidOperationException("Built-in Elite drums layouts cannot be renamed.");
            }

            string trimmedName = (name ?? "").Trim();
            if (trimmedName.Length == 0 || PathHelper.SanitizeFileName(trimmedName).Length == 0)
            {
                throw new InvalidOperationException("Preset name must contain at least one letter or number.");
            }

            // Renaming rewrites the preset file, so the saving validation gate applies
            ThrowIfSaveBlocked(layout);

            string previousName = layout.Name;
            layout.Name = trimmedName;
            try
            {
                // Write the new file first, then remove the stale one, so a failure cannot lose the layout
                SavePresetFile(layout);
                DeleteStalePresetFiles(layout);
            }
            catch
            {
                layout.Name = previousName;
                throw;
            }
        }

        public override void ReloadPresetAtPath(string path)
        {
            if (!File.Exists(path))
            {
                _content.RemoveAll(preset => preset.Path == path);
                return;
            }

            if (!TryLoadPresetFile(path, out var preset, out var error))
            {
                // Bad files are diagnosed only; they are never deleted or renamed
                YargLogger.LogFormatWarning<string, string?>("Skipping Elite drums layout preset `{0}`: {1}", path, error);
                _content.RemoveAll(preset => preset.Path == path);
                return;
            }

            var sameId = _content.FirstOrDefault(existing => existing.Id == preset!.Id);
            var samePath = _content.FirstOrDefault(existing => existing.Path == path);

            if (!EliteDrumsLayoutValidator.Validate(preset).IsComplete)
            {
                YargLogger.LogFormatWarning<string>(
                    "Elite drums layout preset `{0}` is incomplete; kept for recovery only: it cannot be used, saved, or exported.",
                    path);
            }

            if (sameId is null)
            {
                _content.RemoveAll(existing => existing.Path == path);
                _content.Add(preset!);
                return;
            }

            if (samePath is not null && !ReferenceEquals(samePath, sameId))
            {
                // This file used to hold a different preset whose ID no longer exists
                _content.RemoveAll(existing => existing.Path == path);
            }

            if (ReferenceEquals(samePath, sameId))
            {
                // Reload the same preset in place
                int index = _content.IndexOf(sameId);
                _content[index] = preset!;
            }
            else
            {
                // The ID already belongs to another file; keep the first and skip this duplicate
                YargLogger.LogFormatWarning<string, string?>("Elite drums layout preset `{0}` duplicates the ID of `{1}`; keeping the first.",
                    path, sameId.Path);
            }
        }

        public override void SaveAll()
        {
            foreach (var preset in _content)
            {
                try
                {
                    var errors = GetSaveBlockingErrors(preset);
                    if (errors.Count > 0)
                    {
                        // For example an incomplete hand-edited layout: keep its file untouched
                        YargLogger.LogFormatWarning<string, string>("Not saving Elite drums layout preset `{0}`: {1}",
                            preset.Name, string.Join(" ", errors));
                        continue;
                    }

                    SavePresetFile(preset);
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, $"Failed to save Elite drums layout preset '{preset.Name}'.");
                }
            }
        }

        /// <summary>
        /// A built-in layout, or a custom layout by ID. Built-ins are returned as fresh copies.
        /// </summary>
        public EliteDrumsLayoutPreset? GetPresetById(Guid guid)
        {
            if (guid == EliteDrumsLayoutPreset.CompactId) return EliteDrumsLayoutPreset.Compact;
            if (guid == EliteDrumsLayoutPreset.FullId) return EliteDrumsLayoutPreset.Full;
            return _content.FirstOrDefault(preset => preset.Id == guid);
        }

        public bool TryGetPresetById(Guid guid, out EliteDrumsLayoutPreset? preset)
        {
            preset = GetPresetById(guid);
            return preset is not null;
        }

        /// <summary>
        /// Like <see cref="GetPresetById"/>, but only returns presets that can be activated for
        /// gameplay. Unsupported merged layouts and incomplete layouts return null.
        /// </summary>
        public EliteDrumsLayoutPreset? GetUsablePresetById(Guid guid)
        {
            var preset = GetPresetById(guid);
            return preset is not null && IsUsable(preset) ? preset : null;
        }

        public override BasePreset GetBasePresetById(Guid guid)
        {
            return GetPresetById(guid);
        }

        public override bool HasPresetId(Guid guid)
        {
            return IsBuiltinId(guid) || _content.Any(preset => preset.Id == guid);
        }

        /// <summary>
        /// Writes the preset to <paramref name="path"/> as plain UTF-8 JSON (no zip archive).
        /// Built-ins are exported as ordinary copies, but their layout must still be structurally
        /// valid and complete, so a mutated built-in copy is rejected. Invalid or incomplete
        /// layouts are rejected before any file is written. Unlike the other preset containers,
        /// failures are thrown (InvalidOperationException for validation, IOException for file
        /// errors) so the calling UI can report them; nothing is silently swallowed.
        /// </summary>
        public override void ExportPreset(BasePreset preset, string path)
        {
            if (preset is not EliteDrumsLayoutPreset layout)
            {
                throw new InvalidOperationException("Attempted to export invalid preset type.");
            }

            if (!HasPresetId(layout.Id))
            {
                throw new InvalidOperationException("Cannot export an unknown Elite drums layout preset.");
            }

            var errors = GetExportBlockingErrors(layout);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Cannot export Elite drums layout preset: " + string.Join(" ", errors));
            }

            string json = JsonConvert.SerializeObject(layout, JsonSettings);
            WriteFileAtomically(path, json);
        }

        private static IReadOnlyList<string> GetExportBlockingErrors(EliteDrumsLayoutPreset preset)
        {
            if (!IsBuiltinId(preset.Id))
            {
                return GetSaveBlockingErrors(preset);
            }

            // Built-ins keep their fixed identity, but a mutated copy must still be a valid,
            // complete layout before it may be exported
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(preset.Name))
            {
                errors.Add("The preset name is empty.");
            }

            var validation = EliteDrumsLayoutValidator.Validate(preset);
            if (!validation.IsStructurallyValid)
            {
                errors.AddRange(validation.Errors);
            }
            else if (!validation.IsComplete)
            {
                errors.Add("The layout is incomplete; every Elite drums source must be assigned.");
            }

            return errors;
        }

        /// <summary>
        /// Reads and validates a layout file without modifying anything. The calling UI uses this
        /// to decide whether to import (and to inform the user about ID conflicts, or cancel).
        /// </summary>
        public EliteDrumsLayoutImportPlan PlanImport(string path)
        {
            if (!TryReadPresetFile(path, out var preset, out var error))
            {
                return new EliteDrumsLayoutImportPlan(null, false, new[] { error ?? "The file could not be read." });
            }

            var errors = new List<string>();
            var validation = EliteDrumsLayoutValidator.Validate(preset);
            if (!validation.IsStructurallyValid)
            {
                errors.AddRange(validation.Errors);
            }
            else if (!validation.IsComplete)
            {
                errors.Add("The layout is incomplete; every Elite drums source must be assigned.");
            }

            bool idConflict = IsBuiltinId(preset.Id) || HasPresetId(preset.Id);
            return new EliteDrumsLayoutImportPlan(preset, idConflict, errors);
        }

        /// <summary>
        /// Imports a plain JSON layout file. A file whose ID collides with a built-in or an
        /// existing custom preset is imported under a new identity. Returns null (with a log)
        /// for unreadable, malformed, or invalid files; dialogs are the caller's responsibility.
        /// </summary>
        public override BasePreset ImportPreset(string path)
        {
            var plan = PlanImport(path);
            if (!plan.CanImport)
            {
                YargLogger.LogFormatWarning<string, string>("Cannot import Elite drums layout preset `{0}`: {1}",
                    path, string.Join(" ", plan.Errors));
                return null;
            }

            var preset = plan.Preset!;
            if (plan.IdConflict)
            {
                preset.Id = Guid.NewGuid();
            }

            try
            {
                return AddPreset(preset);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Failed to import Elite drums layout preset.");
                return null;
            }
        }

        /// <summary>
        /// The reasons this preset cannot be saved, or an empty list when it can. Checks the ID,
        /// name, type, and that the layout is structurally valid and complete. Runtime-unsupported
        /// (merged-assignment) layouts pass: they are valid and complete, just not activatable.
        /// </summary>
        public static IReadOnlyList<string> GetSaveBlockingErrors(EliteDrumsLayoutPreset? preset)
        {
            var errors = new List<string>();

            if (preset is null)
            {
                errors.Add("The preset is null.");
                return errors;
            }

            if (preset.Id == Guid.Empty)
            {
                errors.Add("The preset ID is empty.");
            }

            if (IsBuiltinId(preset.Id))
            {
                errors.Add("Built-in Elite drums layouts cannot be saved as custom presets.");
            }

            if (preset.Type != PRESET_TYPE_STRING_NAME)
            {
                errors.Add($"The preset type must be '{PRESET_TYPE_STRING_NAME}'.");
            }

            if (string.IsNullOrWhiteSpace(preset.Name))
            {
                errors.Add("The preset name is empty.");
            }
            else if (PathHelper.SanitizeFileName(preset.Name.Trim()).Length == 0)
            {
                errors.Add("The preset name must contain at least one letter or number.");
            }

            var validation = EliteDrumsLayoutValidator.Validate(preset);
            if (!validation.IsStructurallyValid)
            {
                errors.AddRange(validation.Errors);
            }
            else if (!validation.IsComplete)
            {
                errors.Add("The layout is incomplete; every Elite drums source must be assigned.");
            }

            return errors;
        }

        /// <summary>
        /// Reads a layout JSON file with hard bounds: at most <see cref="MAX_JSON_BYTES"/> bytes
        /// and <see cref="MAX_JSON_DEPTH"/> levels of nesting, rejecting duplicate JSON property
        /// names, trailing content, wrong types, and missing required properties.
        /// </summary>
        public static bool TryReadPresetFile(string path, out EliteDrumsLayoutPreset? preset, out string? error)
        {
            preset = null;
            error = null;

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    error = "The file does not exist.";
                    return false;
                }

                if (info.Length > MAX_JSON_BYTES)
                {
                    error = $"The file is {info.Length} bytes, over the {MAX_JSON_BYTES} byte limit.";
                    return false;
                }

                using var stream = info.OpenRead();
                if (stream.Length > MAX_JSON_BYTES)
                {
                    error = $"The file is {stream.Length} bytes, over the {MAX_JSON_BYTES} byte limit.";
                    return false;
                }

                using var streamReader = new StreamReader(stream, Encoding.UTF8);
                using var reader = new StrictJsonReader(streamReader);
                var root = JsonSerializer.Create(JsonSettings).Deserialize<JObject>(reader);

                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.Comment)
                    {
                        error = "Unexpected trailing content after the JSON object.";
                        return false;
                    }
                }

                if (root is null)
                {
                    error = "The file does not contain a JSON object.";
                    return false;
                }

                if (!root.TryGetValue("Id", out var idToken) ||
                    !Guid.TryParse(idToken.ToString(), out var id) || id == Guid.Empty)
                {
                    error = "Missing or invalid 'Id'.";
                    return false;
                }

                if (!root.TryGetValue("Name", out var nameToken) || string.IsNullOrWhiteSpace(nameToken.ToString()))
                {
                    error = "Missing or empty 'Name'.";
                    return false;
                }

                if (!root.TryGetValue("Type", out var typeToken) || typeToken.ToString() != PRESET_TYPE_STRING_NAME)
                {
                    error = $"Wrong preset type (expected '{PRESET_TYPE_STRING_NAME}').";
                    return false;
                }

                if (!root.TryGetValue("SchemaVersion", out _))
                {
                    error = "Missing 'SchemaVersion'.";
                    return false;
                }

                if (!root.TryGetValue("Slots", out _))
                {
                    error = "Missing 'Slots'.";
                    return false;
                }

                preset = root.ToObject<EliteDrumsLayoutPreset>(JsonSerializer.Create(JsonSettings));
                if (preset is null)
                {
                    error = "The preset could not be deserialized.";
                    return false;
                }

                preset.Path = path;
                return true;
            }
            catch (Exception e)
            {
                preset = null;
                error = e.Message;
                return false;
            }
        }

        private static void ThrowIfSaveBlocked(EliteDrumsLayoutPreset preset)
        {
            var errors = GetSaveBlockingErrors(preset);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Cannot save Elite drums layout preset: " + string.Join(" ", errors));
            }
        }

        private bool TryLoadPresetFile(string path, out EliteDrumsLayoutPreset? preset, out string? error)
        {
            if (!TryReadPresetFile(path, out preset, out error))
            {
                return false;
            }

            if (IsBuiltinId(preset!.Id))
            {
                error = "The file claims a built-in preset ID.";
                return false;
            }

            var validation = EliteDrumsLayoutValidator.Validate(preset);
            if (!validation.IsStructurallyValid)
            {
                error = "Invalid layout (" + string.Join(" ", validation.Errors) + ")";
                return false;
            }

            // Incomplete-but-valid layouts stay loaded for management; they are never usable
            return true;
        }

        private void LoadFiles()
        {
            _content.Clear();

            if (!Directory.Exists(FullContentDirectory))
            {
                return;
            }

            List<string> paths;
            try
            {
                // Deterministic order so duplicate IDs always resolve to the same first file
                paths = Directory.EnumerateFiles(FullContentDirectory, "*.json", PathHelper.SafeSearchOptions_NoRecurse)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Failed to enumerate Elite drums layout presets.");
                return;
            }

            foreach (string path in paths)
            {
                if (!TryLoadPresetFile(path, out var preset, out var error))
                {
                    YargLogger.LogFormatWarning<string, string?>("Skipping Elite drums layout preset `{0}`: {1}", path, error);
                    continue;
                }

                if (!EliteDrumsLayoutValidator.Validate(preset).IsComplete)
                {
                    YargLogger.LogFormatWarning<string>(
                        "Elite drums layout preset `{0}` is incomplete; kept for recovery only: it cannot be used, saved, or exported.",
                        path);
                }

                if (_content.Any(existing => existing.Id == preset!.Id))
                {
                    YargLogger.LogFormatWarning<string>("Skipping duplicate Elite drums layout preset `{0}`.", path);
                    continue;
                }

                _content.Add(preset!);
            }
        }

        private void SavePresetFile(EliteDrumsLayoutPreset preset)
        {
            preset.Type = PRESET_TYPE_STRING_NAME;
            string json = JsonConvert.SerializeObject(preset, JsonSettings);

            string path = Path.Combine(FullContentDirectory, GetFileNameForPreset(preset));
            WriteFileAtomically(path, json);

            preset.Path = path;
        }

        private static void WriteFileAtomically(string path, string json)
        {
            // Bound the serialized form as well: a preset that barely fit inside the read limit
            // must never be re-serialized into a file this container could no longer load
            int jsonBytes = Encoding.UTF8.GetByteCount(json);
            if (jsonBytes > MAX_JSON_BYTES)
            {
                throw new InvalidOperationException(
                    $"The serialized preset is {jsonBytes} bytes, over the {MAX_JSON_BYTES} byte limit.");
            }

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a sibling temporary file, then swap it in, so a crash mid-write can
            // never leave a truncated layout file behind
            string tempPath = path + ".tmp";
            try
            {
                File.WriteAllText(tempPath, json);

                if (File.Exists(path))
                {
                    ReplaceFile(tempPath, path);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch
            {
                TryDeleteQuietly(tempPath);
                throw;
            }
        }

        private static void ReplaceFile(string sourcePath, string destinationPath)
        {
            try
            {
                File.Replace(sourcePath, destinationPath, null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Some platforms and streams do not support Replace; copy + delete instead
                File.Copy(sourcePath, destinationPath, true);
                File.Delete(sourcePath);
            }
        }

        private static void TryDeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, $"Failed to clean up temporary file `{path}`.");
            }
        }

        private void DeletePresetFiles(EliteDrumsLayoutPreset preset)
        {
            // Never delete by file name alone: only remove files that actually contain this
            // preset's ID, and leave anything unreadable completely untouched
            PathHelper.SafeEnumerateFiles(FullContentDirectory, "*.json", false, path =>
            {
                if (TryReadPresetFile(path, out var filePreset, out _) && filePreset is not null &&
                    filePreset.Id == preset.Id)
                {
                    File.Delete(path);
                    PresetsTab.IgnorePathUpdate(path);
                }

                return true;
            });

            preset.Path = null;
        }

        private void DeleteStalePresetFiles(EliteDrumsLayoutPreset preset)
        {
            // Used after a rename rewrote the preset under a new file name; keep the fresh file
            string? keepPath = preset.Path;
            PathHelper.SafeEnumerateFiles(FullContentDirectory, "*.json", false, path =>
            {
                if (path == keepPath)
                {
                    return true;
                }

                if (TryReadPresetFile(path, out var filePreset, out _) && filePreset is not null &&
                    filePreset.Id == preset.Id)
                {
                    File.Delete(path);
                    PresetsTab.IgnorePathUpdate(path);
                }

                return true;
            });
        }

        private static bool IsBuiltinId(Guid guid)
        {
            return guid == EliteDrumsLayoutPreset.CompactId || guid == EliteDrumsLayoutPreset.FullId;
        }

        /// <summary>
        /// A JSON reader with a hard depth limit that rejects duplicate property names within an
        /// object instead of silently taking the last value.
        /// </summary>
        private sealed class StrictJsonReader : JsonTextReader
        {
            private readonly Stack<HashSet<string>> _objects = new();

            public StrictJsonReader(TextReader reader) : base(reader)
            {
                MaxDepth = MAX_JSON_DEPTH;
            }

            public override bool Read()
            {
                if (!base.Read())
                {
                    return false;
                }

                switch (TokenType)
                {
                    case JsonToken.StartObject:
                        _objects.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonToken.EndObject:
                        if (_objects.Count > 0)
                        {
                            _objects.Pop();
                        }
                        break;
                    case JsonToken.PropertyName:
                        string? propertyName = Value as string;
                        if (propertyName is not null && _objects.Count > 0 &&
                            !_objects.Peek().Add(propertyName))
                        {
                            throw new JsonReaderException(
                                $"Duplicate JSON property '{propertyName}' at path '{Path}'.");
                        }
                        break;
                }

                return true;
            }
        }
    }

    /// <summary>The read-only outcome of <see cref="EliteDrumsLayoutPresetContainer.PlanImport"/>.</summary>
    public sealed class EliteDrumsLayoutImportPlan
    {
        /// <summary>True when the file can be imported.</summary>
        public bool CanImport { get; }

        /// <summary>
        /// True when the file's ID collides with a built-in or an already present custom preset.
        /// Importing anyway re-identifies the preset; canceling leaves everything unchanged.
        /// </summary>
        public bool IdConflict { get; }

        /// <summary>True when the imported layout is complete and runtime supported.</summary>
        public bool IsRuntimeSupported { get; }

        /// <summary>The ID stored in the file, or <see cref="Guid.Empty"/> when unreadable.</summary>
        public Guid FileId { get; }

        /// <summary>The name stored in the file, or an empty string when unreadable.</summary>
        public string PresetName { get; }

        /// <summary>Why the file cannot be imported; empty when it can.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>The parsed preset; null when the file could not be read.</summary>
        public EliteDrumsLayoutPreset? Preset { get; }

        internal EliteDrumsLayoutImportPlan(EliteDrumsLayoutPreset? preset, bool idConflict,
            IReadOnlyList<string> errors) : this(preset, idConflict, preset is not null &&
                EliteDrumsLayoutValidator.Validate(preset).IsRuntimeSupported, errors)
        {
        }

        internal EliteDrumsLayoutImportPlan(EliteDrumsLayoutPreset? preset, bool idConflict,
            bool isRuntimeSupported, IReadOnlyList<string> errors)
        {
            Preset = preset;
            IdConflict = idConflict;
            IsRuntimeSupported = isRuntimeSupported;
            Errors = errors;
            CanImport = errors.Count == 0;
            FileId = preset?.Id ?? Guid.Empty;
            PresetName = preset?.Name ?? "";
        }
    }
}