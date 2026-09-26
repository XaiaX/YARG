using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using YARG.Song;

namespace YARG.Tests.EditMode
{
    public sealed class SongRefreshCoordinatorTests
    {
        [UnityTest]
        public IEnumerator ConcurrentRequests_SerializeAndCoalesceLatestMode()
        {
            var gates = new List<UniTaskCompletionSource>();
            var modes = new List<bool>();
            var coordinator = new SongRefreshCoordinator(quick =>
            {
                modes.Add(quick);
                var gate = new UniTaskCompletionSource();
                gates.Add(gate);
                return gate.Task;
            });

            UniTask first = coordinator.Request(true);
            UniTask second = coordinator.Request(false);
            UniTask third = coordinator.Request(true);
            UniTask fourth = coordinator.Request(true);
            Assert.That(modes, Is.EqualTo(new[] { true }));
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(third.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(coordinator.IsRebuilding, Is.True);

            gates[0].TrySetResult();
            yield return null;
            Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(modes, Is.EqualTo(new[] { true, false }));
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(gates.Count, Is.EqualTo(2));

            gates[1].TrySetResult();
            yield return null;
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(third.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(fourth.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(modes.Count, Is.EqualTo(2));
            Assert.That(coordinator.IsRebuilding, Is.False);
        }

        [UnityTest]
        public IEnumerator FailedScan_PropagatesAndDoesNotBlockQueuedRefresh()
        {
            var gates = new List<UniTaskCompletionSource>();
            var coordinator = new SongRefreshCoordinator(_ =>
            {
                var gate = new UniTaskCompletionSource();
                gates.Add(gate);
                return gate.Task;
            });
            UniTask failed = coordinator.Request(false);
            UniTask queued = coordinator.Request(true);
            gates[0].TrySetException(new InvalidOperationException("scan failed"));
            yield return null;
            Assert.That(failed.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.Throws<InvalidOperationException>(() => failed.GetAwaiter().GetResult());
            Assert.That(gates.Count, Is.EqualTo(2));
            gates[1].TrySetResult();
            yield return null;
            Assert.That(queued.Status, Is.EqualTo(UniTaskStatus.Succeeded));
        }
    }
}
