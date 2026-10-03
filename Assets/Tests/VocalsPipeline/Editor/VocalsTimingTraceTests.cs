using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using YARG.Audio.PitchDetection;

namespace YARG.Tests.VocalsPipeline
{
    public sealed class VocalsTimingTraceTests
    {
        [Test]
        public void DisabledDoesNotCreateWriter()
        {
            Assert.That(VocalsTimingTrace.Create(null, "/unused"), Is.Null);
            Assert.That(VocalsTimingTrace.Create("", "/unused"), Is.Null);
            Assert.DoesNotThrow(() => VocalsTimingTrace.Emit(VocalsTraceEvent.Read, 0, 1));
        }

        [Test]
        public void RejectsRelativeProjectRootDescendantsAndExistingFile()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "vocals-trace-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Assert.Throws<ArgumentException>(() => VocalsTimingTrace.ValidatePath("relative.csv", root));
                Assert.Throws<ArgumentException>(() => VocalsTimingTrace.ValidatePath(root, root));
                Assert.Throws<ArgumentException>(() => VocalsTimingTrace.ValidatePath(Path.Combine(root, "trace.csv"), root));
                Assert.Throws<ArgumentException>(() => VocalsTimingTrace.ValidatePath(root, Path.Combine(root, "other")));
                Assert.DoesNotThrow(() => VocalsTimingTrace.ValidatePath(Path.Combine(root, "trace.csv"), Path.Combine(root, "other")));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void RejectsSymlinkEscapeAndLinkedFile()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                Assert.Ignore("Unix symlink fixture; runtime rejects Windows reparse points too.");
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "vocals-links-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string project = Path.Combine(root, "project");
            Directory.CreateDirectory(project);
            string alias = Path.Combine(root, "alias");
            string fileAlias = Path.Combine(root, "linked.csv");
            try
            {
                foreach (string link in new[] { alias, fileAlias })
                {
                    using var process = System.Diagnostics.Process.Start("/bin/ln", "-s \"" + project + "\" \"" + link + "\"");
                    Assert.That(process, Is.Not.Null);
                    process.WaitForExit();
                    Assert.That(process.ExitCode, Is.Zero);
                }
                Assert.Throws<ArgumentException>(() => VocalsTimingTrace.ValidatePath(Path.Combine(alias, "trace.csv"), project));
                Assert.Throws<ArgumentException>(() => VocalsTimingTrace.ValidatePath(fileAlias, project));
            }
            finally
            {
                if (Directory.Exists(alias)) Directory.Delete(alias);
                if (Directory.Exists(fileAlias)) Directory.Delete(fileAlias);
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void BoundedQueueDropsAndFlushesOnWriterThread()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int callerThread = Thread.CurrentThread.ManagedThreadId;
            int writerThread = 0;
            var text = new StringWriter();
            var trace = new VocalsTimingTrace(() =>
            {
                writerThread = Thread.CurrentThread.ManagedThreadId;
                entered.Set();
                if (!release.Wait(5000)) throw new TimeoutException();
                return text;
            }, 2);
            try
            {
                Assert.That(entered.Wait(5000), Is.True);
                for (int i = 0; i < 10; i++) trace.Record(VocalsTraceEvent.Read, 1, i);
                Assert.That(trace.Dropped, Is.EqualTo(8));
            }
            finally { release.Set(); trace.Dispose(); }
            Assert.That(trace.Completed, Is.True);
            Assert.That(trace.Written, Is.EqualTo(2));
            Assert.That(writerThread, Is.Not.EqualTo(callerThread));
            Assert.That(text.ToString(), Does.Contain("# summary,written=2,dropped=8"));
            Assert.That(trace.Errors, Is.Zero);
        }

        [Test]
        public void StorageFailureIsContainedAndCounted()
        {
            using var trace = new VocalsTimingTrace(() => throw new IOException("synthetic storage failure"));
            Assert.That(SpinWait.SpinUntil(() => trace.Errors > 0, 5000), Is.True);
            Assert.DoesNotThrow(() => trace.Record(VocalsTraceEvent.Output, 1, 440));
            Assert.That(trace.Dropped, Is.EqualTo(1));
            Assert.That(trace.Errors, Is.EqualTo(1));
        }

        [Test]
        public void CreatesNewExternalFileAndRejectsOverwrite()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "vocals-file-" + Guid.NewGuid().ToString("N") + ".csv");
            string project = Path.Combine(Path.GetDirectoryName(path), "project");
            try
            {
                using (var trace = VocalsTimingTrace.Create(path, project))
                {
                    trace.Record(VocalsTraceEvent.Decision, 1, 0.04, -160, 0, 0, 0, 0);
                    trace.Dispose();
                    Assert.That(trace.Completed, Is.True);
                    Assert.That(trace.Errors, Is.Zero);
                    Assert.That(trace.Written, Is.EqualTo(1));
                }
                Assert.That(File.ReadAllText(path), Does.Contain("# summary,written=1"));
                Assert.Throws<ArgumentException>(() => VocalsTimingTrace.Create(path, project));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void FailedWriteReportsUnwrittenRecord()
        {
            using var trace = new VocalsTimingTrace(() => new FailingWriter());
            trace.Record(VocalsTraceEvent.Read, 1, 1);
            Assert.That(SpinWait.SpinUntil(() => trace.Errors > 0, 5000), Is.True);
            trace.Dispose();
            Assert.That(trace.Errors, Is.EqualTo(1));
            Assert.That(trace.Unwritten, Is.EqualTo(1));
            Assert.That(trace.Written, Is.Zero);
        }

        private sealed class FailingWriter : StringWriter
        {
            private int _lines;
            public override void WriteLine(string value)
            {
                if (++_lines > 1) throw new IOException("synthetic write failure");
                base.WriteLine(value);
            }
        }

        [Test]
        public void RejectsOversizedNumericRecord()
        {
            using var trace = new VocalsTimingTrace(() => new StringWriter());
            trace.Record(VocalsTraceEvent.Read, 1, new double[21]);
            Assert.That(trace.Dropped, Is.EqualTo(1));
        }
    }
}
