using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Helpers;

namespace YARG.Settings.Customization
{
    public class ColorProfileContainer : CustomContent<ColorProfile>
    {
        private const string ELITE = "EliteDrums";
        private readonly Dictionary<Guid, JObject> _saved = new();
        private readonly HashSet<Guid> _unreadableElite = new();

        protected override string ContentDirectory => "colors";
        public override string PresetTypeStringName => "ColorProfile";
        public override IReadOnlyList<ColorProfile> DefaultPresets => ColorProfile.Defaults;

        private string ElitePath(Guid id) =>
            Path.Combine(Path.GetDirectoryName(FullContentDirectory), "elite-colors", $"{id:D}.json");

        private static JObject Snapshot(ColorProfile profile) =>
            JObject.FromObject(profile, JsonSerializer.Create(JsonSettings));

        private static JObject ReadObject(string path) => JObject.Parse(File.ReadAllText(path));

        private static JProperty Property(JObject json, string name) =>
            json.Property(name, StringComparison.OrdinalIgnoreCase);

        private static void Put(JObject json, string name, JToken value)
        {
            var property = Property(json, name);
            if (property == null) json.Add(name, value);
            else property.Value = value;
        }

        private Guid CheckIdentity(JObject json)
        {
            if (!Guid.TryParse((string) Property(json, "Id")?.Value, out var id) || id == Guid.Empty ||
                Property(json, "Name")?.Value.Type != JTokenType.String ||
                (Property(json, "Type")?.Value is JToken type && type.Type != JTokenType.Null && (string) type != PresetTypeStringName))
                throw new InvalidDataException("Unrecognized color profile identity or type.");
            return id;
        }

        private static void LoadElite(ColorProfile profile, JToken section)
        {
            if (section == null || section.Type == JTokenType.Null) return;
            if (section is not JObject) throw new InvalidDataException("Unrecognized Elite palette format.");
            using var reader = new JObject { [ELITE] = section }.CreateReader();
            JsonSerializer.Create(JsonSettings).Populate(reader, profile);
        }

        public override void Initialize()
        {
            Directory.CreateDirectory(FullContentDirectory);
            Content.Clear();
            _saved.Clear();
            _unreadableElite.Clear();
            PathHelper.SafeEnumerateFiles(FullContentDirectory, "*.json", true, path =>
            {
                ReloadPresetAtPath(path);
                return true;
            });
        }

        public override void ReloadPresetAtPath(string path)
        {
            try
            {
                var old = Content.Find(p => p.Path == path);
                if (!File.Exists(path))
                {
                    if (old != null)
                    {
                        Content.Remove(old);
                        _saved.Remove(old.Id);
                    }
                    return;
                }

                var json = ReadObject(path);
                var id = CheckIdentity(json);
                if (GetPresetById(id) is { } existing && existing != old)
                    throw new InvalidDataException($"Duplicate color profile ID {id}.");
                var inline = Property(json, ELITE);
                inline?.Remove();
                var profile = json.ToObject<ColorProfile>(JsonSerializer.Create(JsonSettings));
                profile.Path = path;
                profile.Type = PresetTypeStringName;
                var side = ElitePath(id);
                try
                {
                    bool hasSide = File.Exists(side);
                    var elite = hasSide ? ReadObject(side) : inline?.Value;
                    LoadElite(profile, elite);
                    // Protect inline palettes before an older build can rewrite the shared file.
                    if (!hasSide && elite is JObject colors) WriteNew(side, colors);
                    _unreadableElite.Remove(id);
                }
                catch (Exception e)
                {
                    _unreadableElite.Add(id);
                    YargLogger.LogException(e, $"Keeping the classic colors available after an Elite palette read or migration error: {path}");
                }
                var snapshot = Snapshot(profile);
                if (old != null)
                {
                    _saved.Remove(old.Id);
                    Content[Content.IndexOf(old)] = profile;
                }
                else Content.Add(profile);
                _saved[id] = snapshot;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, $"Leaving unreadable color profile untouched: {path}");
            }
        }

        public override BasePreset AddPreset(BasePreset preset) =>
            AddImportedPreset((ColorProfile) preset, null);

        protected override BasePreset AddImportedPreset(ColorProfile profile, string json)
        {
            if (HasPresetId(profile.Id)) return profile;
            profile.Type = PresetTypeStringName;
            var current = Snapshot(profile);
            var document = json == null ? (JObject) current.DeepClone() : JObject.Parse(json);
            if (CheckIdentity(document) != profile.Id) throw new InvalidDataException("Color profile ID mismatch.");
            var path = Path.Combine(FullContentDirectory, GetFileNameForPreset(profile));
            var side = ElitePath(profile.Id);
            if (File.Exists(path)) throw new IOException("Color profile file already exists.");
            var inline = Property(document, ELITE);
            var elite = inline?.Value ?? current[ELITE];
            inline?.Remove();
            bool createdSide = false;
            try
            {
                // A legacy build can delete the main file while leaving its protected palette here.
                if (File.Exists(side))
                {
                    LoadElite(profile, ReadObject(side));
                    current = Snapshot(profile);
                }
                else if (elite != null && elite.Type != JTokenType.Null)
                {
                    if (elite is not JObject colors) throw new InvalidDataException("Unrecognized Elite palette format.");
                    WriteNew(side, colors);
                    createdSide = true;
                }
                WriteNew(path, document);
            }
            catch
            {
                if (createdSide) File.Delete(side);
                throw;
            }
            profile.Path = path;
            _unreadableElite.Remove(profile.Id);
            _saved.Add(profile.Id, current);
            Content.Add(profile);
            return profile;
        }

        public ColorProfile OverwriteImportedProfile(ColorProfile existing, string json)
        {
            var document = JObject.Parse(json);
            if (!Content.Contains(existing) || CheckIdentity(document) != existing.Id ||
                (string) Property(document, "Type")?.Value != PresetTypeStringName)
                throw new InvalidDataException("The selected color profile has changed. Import the file again.");

            var oldBase = ReadBase(existing);
            var profile = document.ToObject<ColorProfile>(JsonSerializer.Create(JsonSettings));
            var inline = Property(document, ELITE);
            var palette = inline?.Value as JObject;
            if (inline != null && inline.Value.Type != JTokenType.Null && palette == null)
                throw new InvalidDataException("Unrecognized Elite palette format.");
            inline?.Remove();

            var side = ElitePath(existing.Id);
            bool hasSide = File.Exists(side);
            if (palette == null)
            {
                // An export from a classic build can restore the shared colors without discarding Elite colors.
                var retained = hasSide ? ReadObject(side) : Property(oldBase, ELITE)?.Value;
                LoadElite(profile, retained);
                if (!hasSide) palette = retained as JObject;
            }

            profile.Path = existing.Path;
            var snapshot = Snapshot(profile);
            var backup = existing.Path + "." + Guid.NewGuid().ToString("N") + ".bak";
            Write(existing.Path, document, true, backup);
            try
            {
                if (palette != null) Write(side, palette, hasSide);
            }
            catch
            {
                File.Replace(backup, existing.Path, null);
                throw;
            }
            Content[Content.IndexOf(existing)] = profile;
            _saved[profile.Id] = snapshot;
            _unreadableElite.Remove(profile.Id);
            try { File.Delete(backup); }
            catch (IOException e) { YargLogger.LogException(e, $"Could not remove import backup {backup}"); }
            return profile;
        }

        public override BasePreset CopyPreset(BasePreset original, BasePreset copy)
        {
            var document = PortableDocument((ColorProfile) original);
            Put(document, "Name", copy.Name);
            Put(document, "Id", copy.Id.ToString("D"));
            Put(document, "Type", PresetTypeStringName);
            return AddImportedPreset((ColorProfile) copy, document.ToString());
        }

        private JObject ReadBase(ColorProfile profile)
        {
            var json = ReadObject(profile.Path);
            if (CheckIdentity(json) != profile.Id) throw new IOException("Color profile file changed identity.");
            return json;
        }

        // Apply only edited leaves so fields introduced by another build survive a save.
        private static void ApplyChanges(JObject disk, JObject before, JObject after)
        {
            foreach (var property in after.Properties())
            {
                var prior = before[property.Name];
                if (JToken.DeepEquals(prior, property.Value)) continue;
                if (prior is JObject a && property.Value is JObject b && Property(disk, property.Name)?.Value is JObject target)
                    ApplyChanges(target, a, b);
                else
                    Put(disk, property.Name, property.Value.DeepClone());
            }
            foreach (var property in before.Properties())
                if (after[property.Name] == null) Property(disk, property.Name)?.Remove();
        }

        public override string SavePresetFile(ColorProfile profile)
        {
            if (!_saved.TryGetValue(profile.Id, out var before) || GetPresetById(profile.Id) != profile)
                throw new InvalidOperationException("Color profile is not an active custom preset.");
            var after = Snapshot(profile);
            if (JToken.DeepEquals(before, after)) return profile.Path;

            var disk = ReadBase(profile);
            var oldBase = (JObject) before.DeepClone();
            var newBase = (JObject) after.DeepClone();
            oldBase.Remove(ELITE);
            newBase.Remove(ELITE);
            bool eliteChanged = !JToken.DeepEquals(before[ELITE], after[ELITE]);
            bool blocked = eliteChanged && _unreadableElite.Contains(profile.Id);
            if (eliteChanged && !blocked && after[ELITE] is JObject colors)
            {
                var path = ElitePath(profile.Id);
                bool exists = File.Exists(path);
                var merged = exists ? ReadObject(path) : (JObject) colors.DeepClone();
                if (exists) ApplyChanges(merged, before[ELITE] as JObject ?? new JObject(), colors);
                Write(path, merged, exists);
            }
            if (!JToken.DeepEquals(oldBase, newBase))
            {
                ApplyChanges(disk, oldBase, newBase);
                if (!_unreadableElite.Contains(profile.Id) && File.Exists(ElitePath(profile.Id)))
                    Property(disk, ELITE)?.Remove();
                Write(profile.Path, disk, true);
            }
            if (blocked)
            {
                after[ELITE] = before[ELITE]?.DeepClone();
                _saved[profile.Id] = after;
                throw new IOException("Reload the protected Elite palette before saving Elite color edits.");
            }
            _saved[profile.Id] = after;
            return profile.Path;
        }

        private JObject PortableDocument(ColorProfile profile)
        {
            if (!_saved.ContainsKey(profile.Id))
            {
                var builtIn = Snapshot(profile);
                builtIn["Type"] = PresetTypeStringName;
                return builtIn;
            }
            SavePresetFile(profile);
            var json = ReadBase(profile);
            var path = ElitePath(profile.Id);
            if (File.Exists(path)) Put(json, ELITE, ReadObject(path));
            Put(json, "Type", PresetTypeStringName);
            return json;
        }

        public override void ExportPreset(BasePreset preset, string path)
        {
            if (GetPresetById(preset.Id) is not ColorProfile profile) return;
            try
            {
                var document = PortableDocument(profile);
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                using var writer = new StreamWriter(archive.CreateEntry("preset.json").Open());
                writer.Write(document.ToString());
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Failed to export color profile.");
            }
        }

        public override void RenamePreset(BasePreset preset, string name)
        {
            var profile = (ColorProfile) preset;
            SavePresetFile(profile);
            var oldName = profile.Name;
            var oldPath = profile.Path;
            profile.Name = name;
            var path = Path.Combine(FullContentDirectory, GetFileNameForPreset(profile));
            try
            {
                MoveForRename(oldPath, path);
                profile.Path = path;
                SavePresetFile(profile);
            }
            catch
            {
                if (profile.Path != oldPath) MoveForRename(path, oldPath);
                profile.Path = oldPath;
                profile.Name = oldName;
                throw;
            }
        }

        private static void MoveForRename(string source, string destination)
        {
            if (source == destination) return;
            if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(source, destination);
                return;
            }

            // Windows and macOS can resolve both spellings to the same file.
            var intermediate = Path.Combine(Path.GetDirectoryName(source), Guid.NewGuid().ToString("N") + ".json");
            File.Move(source, intermediate);
            try { File.Move(intermediate, destination); }
            catch
            {
                File.Move(intermediate, source);
                throw;
            }
        }

        public override void DeletePreset(BasePreset preset)
        {
            var profile = (ColorProfile) preset;
            if (!Content.Contains(profile)) return;
            if (File.Exists(profile.Path))
            {
                ReadBase(profile);
                File.Delete(profile.Path);
            }
            var side = ElitePath(profile.Id);
            if (File.Exists(side)) File.Delete(side);
            _unreadableElite.Remove(profile.Id);
            Content.Remove(profile);
            _saved.Remove(profile.Id);
            profile.Path = null;
        }

        private static void WriteNew(string path, JObject json) => Write(path, json, false);

        private static void Write(string path, JObject json, bool replace, string backup = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json.ToString());
                if (replace) File.Replace(temporary, path, backup);
                else File.Move(temporary, path);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }
}
