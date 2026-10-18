using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Logging;

namespace YARG.Player
{
    /// <summary>Writes profiles after verifying the serialized temporary file.</summary>
    internal static class ProfileFileStorage
    {
        internal interface IFileOperations
        {
            void WriteAllText(string path, string contents);
            bool Exists(string path);
            string ReadAllText(string path);
            void Delete(string path);
            void Move(string source, string destination);
        }

        private sealed class SystemFileOperations : IFileOperations
        {
            public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
            public bool Exists(string path) => File.Exists(path);
            public string ReadAllText(string path) => File.ReadAllText(path);
            public void Delete(string path) => File.Delete(path);
            public void Move(string source, string destination) => File.Move(source, destination);
        }

        private static readonly IFileOperations SYSTEM_FILES = new SystemFileOperations();

        public static bool TrySaveProfiles(string directory, IReadOnlyList<YargProfile> profiles, out int savedCount)
        {
            return TrySaveProfiles(directory, profiles, SYSTEM_FILES, out savedCount);
        }

        internal static bool TrySaveProfiles(string directory, IReadOnlyList<YargProfile> profiles,
            IFileOperations files, out int savedCount)
        {
            savedCount = 0;
            string profilesJson = JsonConvert.SerializeObject(profiles, Formatting.Indented);
            string tempPath = Path.Combine(directory, Path.GetRandomFileName());
            string profilesPath = Path.Combine(directory, "profiles.json");

            try
            {
                files.WriteAllText(tempPath, profilesJson);
            }
            catch (Exception e)
            {
                YargLogger.LogFormatError("Failed to write profiles to file: {0}", e.Message);
                return false;
            }

            int? profileCount = VerifyProfileFile(tempPath, files);
            if (profileCount is null || profileCount != profiles.Count)
            {
                YargLogger.LogFormatError("Failed to verify new profiles file: {0} profiles were expected, but {1} were found", profiles.Count, profileCount ?? -1);
                files.Delete(tempPath);
                return false;
            }

            if (files.Exists(profilesPath))
            {
                files.Delete(profilesPath);
            }

            files.Move(tempPath, profilesPath);
            savedCount = profiles.Count;
            return true;
        }

        private static int? VerifyProfileFile(string path, IFileOperations files)
        {
            if (!files.Exists(path))
            {
                return null;
            }

            List<YargProfile> profiles;
            try
            {
                string profilesJson = files.ReadAllText(path);
                profiles = JsonConvert.DeserializeObject<List<YargProfile>>(profilesJson);
            }
            catch (Exception e)
            {
                YargLogger.LogFormatError("Failed to verify profile file: {0}", e.Message);
                return null;
            }

            return profiles.Count;
        }
    }
}
