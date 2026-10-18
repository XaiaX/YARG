using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class ProfileFileStorageTests
    {
        private static readonly Type StorageType = Type.GetType("YARG.Player.ProfileFileStorage, Assembly-CSharp", true);
        private static readonly Type FileOperationsType = StorageType.GetNestedType("IFileOperations", BindingFlags.NonPublic);
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "yargProfileStorageTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [Test]
        public void ProfileSettingsPersistenceRoundTrip()
        {
            var profile = new YargProfile { Name = "Storage round trip", GameMode = YARG.Core.GameMode.EliteDrums, NoteSpeed = 7.25f };
            Guid layout = Guid.NewGuid();
            Guid color = Guid.NewGuid();
            profile.EliteDrumsLayout = layout;
            profile.ColorProfile = color;
            profile.LeftyFlip = true;
            profile.NoteSpeed = 7.25f;
            var profiles = new List<YargProfile> { profile };

            Assert.That(Save(profiles, new FileOperationsProxy()), Is.True);
            string path = Path.Combine(_directory, "profiles.json");
            var jsonConvert = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
            var deserialize = jsonConvert.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "DeserializeObject" && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(string))
                .MakeGenericMethod(typeof(List<YargProfile>));
            var loaded = (List<YargProfile>)deserialize.Invoke(null, new object[] { File.ReadAllText(path) });
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0].EliteDrumsLayout, Is.EqualTo(layout));
            Assert.That(loaded[0].ColorProfile, Is.EqualTo(color));
            Assert.That(loaded[0].LeftyFlip, Is.True);
            Assert.That(loaded[0].NoteSpeed, Is.EqualTo(7.25f));
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
        }

        [Test]
        public void ProfileStorageWritesOnlySuppliedTempRoot()
        {
            int count = -1;
            bool saved = TrySave(new List<YargProfile>(), new FileOperationsProxy(), out count);
            Assert.That(saved, Is.True);
            Assert.That(count, Is.Zero);
            Assert.That(File.Exists(Path.Combine(_directory, "profiles.json")), Is.True);
        }

        [Test]
        public void ProfileStoragePreservesExistingOnWriteFailure()
        {
            var profiles = new List<YargProfile> { new() { Name = "Failure" } };
            string targetPath = Path.Combine(_directory, "profiles.json");
            File.WriteAllText(targetPath, "existing profiles");
            var writeFailure = new FileOperationsProxy { ThrowOn = "WriteAllText" };
            Assert.That(TrySave(profiles, writeFailure, out _), Is.False);
            Assert.That(writeFailure.Calls, Is.EqualTo(new[] { "WriteAllText" }));
            Assert.That(File.ReadAllText(targetPath), Is.EqualTo("existing profiles"),
                "a failed temp write must not modify the existing profile file");
            File.Delete(targetPath);

            var verifyFailure = new FileOperationsProxy { ThrowOn = "ReadAllText" };
            Assert.That(TrySave(profiles, verifyFailure, out _), Is.False);
            Assert.That(verifyFailure.Calls, Is.EqualTo(new[] { "WriteAllText", "Exists", "ReadAllText", "Delete" }));
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
        }

        [Test]
        public void SerializationFailurePropagatesBeforeAnyFileOperation()
        {
            var files = new FileOperationsProxy();
            var profiles = new ThrowingProfileList();
            Assert.Throws<TargetInvocationException>(() => TrySave(profiles, files, out _));
            Assert.That(files.Calls, Is.Empty);
        }

        private sealed class ThrowingProfileList : IReadOnlyList<YargProfile>
        {
            public int Count => 1;
            public YargProfile this[int index] => throw new InvalidOperationException("Injected serialization failure");
            public IEnumerator<YargProfile> GetEnumerator() => throw new InvalidOperationException("Injected serialization failure");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Test]
        public void ProfileStorageReplacementExceptionsPropagate()
        {
            var profiles = new List<YargProfile> { new() { Name = "Replacement" } };
            string targetPath = Path.Combine(_directory, "profiles.json");
            File.WriteAllText(targetPath, "old");
            var deleteFailure = new FileOperationsProxy { ThrowOn = "Delete" };
            Assert.Throws<TargetInvocationException>(() => TrySave(profiles, deleteFailure, out _));
            Assert.That(File.ReadAllText(targetPath), Is.EqualTo("old"),
                "a failure deleting the existing file leaves it intact");
            foreach (string file in Directory.GetFiles(_directory)) File.Delete(file);
            File.WriteAllText(targetPath, "old");

            var moveFailure = new FileOperationsProxy { ThrowOn = "Move" };
            Assert.Throws<TargetInvocationException>(() => TrySave(profiles, moveFailure, out _));
            Assert.That(File.Exists(Path.Combine(_directory, "profiles.json")), Is.False,
                "target deletion precedes the unguarded move");
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1), "temporary file remains after move failure");
        }

        private bool Save(IReadOnlyList<YargProfile> profiles, FileOperationsProxy files) => TrySave(profiles, files, out _);

        private bool TrySave(IReadOnlyList<YargProfile> profiles, FileOperationsProxy files, out int count)
        {
            var createProxy = typeof(DispatchProxy).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "Create" && method.IsGenericMethodDefinition
                    && method.GetGenericArguments().Length == 2 && method.GetParameters().Length == 0)
                .MakeGenericMethod(FileOperationsType, typeof(FileOperationsProxy));
            object proxy = createProxy.Invoke(null, null);
            var implementation = (FileOperationsProxy)proxy;
            implementation.Directory = _directory;
            implementation.ThrowOn = files.ThrowOn;
            implementation.Calls.Clear();
            try
            {
                object[] args = { _directory, profiles, proxy, 0 };
                bool result = (bool)StorageType.GetMethod("TrySaveProfiles", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, args);
                count = (int)args[3];
                files.Calls.AddRange(implementation.Calls);
                return result;
            }
            catch
            {
                files.Calls.AddRange(implementation.Calls);
                throw;
            }
        }

        public class FileOperationsProxy : DispatchProxy
        {
            public string Directory;
            public string ThrowOn;
            public List<string> Calls = new();

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                string operation = targetMethod.Name;
                Calls.Add(operation);
                if (ThrowOn == operation)
                    throw new IOException("Injected " + operation + " failure");
                string path = args.Length > 0 ? (string)args[0] : null;
                switch (operation)
                {
                    case "WriteAllText": File.WriteAllText(path, (string)args[1]); return null;
                    case "Exists": return File.Exists(path);
                    case "ReadAllText": return File.ReadAllText(path);
                    case "Delete": File.Delete(path); return null;
                    case "Move": File.Move(path, (string)args[1]); return null;
                    default: throw new NotSupportedException(operation);
                }
            }
        }
    }
}
