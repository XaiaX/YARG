using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using YARG.Core.Song.Cache;
using YARG.Song;

namespace YARG.Tests.EditMode
{
    public sealed class SongRefreshCoordinatorTests
    {
        private static SongRefreshRequest Request(SongScanScope scope, bool quick = false,
            string updates = "") => new(scope,
                new SongSourceConfiguration(Array.Empty<string>(), updates, ""), quick);

        // Retain the historical inventory entry while testing the approved scoped contract:
        // only active equivalents join; distinct requests retain their exact scope FIFO.
        [UnityTest]
        public IEnumerator ConcurrentRequests_SerializeAndCoalesceLatestMode()
        {
            return RefreshGateSerializesAllScopes();
        }

        [UnityTest]
        public IEnumerator RefreshGateSerializesAllScopes()
        {
            var gates = new List<UniTaskCompletionSource>();
            var requests = new List<SongRefreshRequest>();
            var coordinator = new SongRefreshCoordinator(request =>
            {
                requests.Add(request);
                var gate = new UniTaskCompletionSource();
                gates.Add(gate);
                return gate.Task;
            });
            var update = Request(SongScanScope.Updates);
            var upgrade = Request(SongScanScope.Upgrades);
            UniTask first = coordinator.Request(update);
            UniTask duplicate = coordinator.Request(Request(SongScanScope.Updates));
            UniTask second = coordinator.Request(upgrade);
            UniTask third = coordinator.Request(Request(SongScanScope.Updates, true));
            Assert.That(requests, Is.EqualTo(new[] { update }));
            Assert.That(coordinator.IsRebuilding, Is.True);

            gates[0].TrySetResult();
            yield return null;
            Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(duplicate.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(requests.Count, Is.EqualTo(2));
            Assert.That(requests[1], Is.SameAs(upgrade));
            gates[1].TrySetResult();
            yield return null;
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(requests.Count, Is.EqualTo(3));
            Assert.That(requests[2].Scope, Is.EqualTo(SongScanScope.Updates));
            Assert.That(requests[2].Quick, Is.True);
            gates[2].TrySetResult();
            yield return null;
            Assert.That(third.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(coordinator.IsRebuilding, Is.False);
        }

        [UnityTest]
        public IEnumerator DistinctConfigurationsExecuteWithCapturedRequests()
        {
            var gates = new List<UniTaskCompletionSource>();
            var requests = new List<SongRefreshRequest>();
            var coordinator = new SongRefreshCoordinator(request =>
            {
                requests.Add(request);
                var gate = new UniTaskCompletionSource();
                gates.Add(gate);
                return gate.Task;
            });
            var firstContext = new object();
            var secondContext = new object();
            var firstRequest = new SongRefreshRequest(SongScanScope.Updates,
                new SongSourceConfiguration(new[] { "first-root" }, "first.dta", ""), false, firstContext);
            var secondRequest = new SongRefreshRequest(SongScanScope.Updates,
                new SongSourceConfiguration(new[] { "second-root" }, "second.dta", ""), false, secondContext);
            var first = coordinator.Request(firstRequest);
            var second = coordinator.Request(secondRequest);
            gates[0].TrySetResult();
            yield return null;
            Assert.That(requests, Is.EqualTo(new[] { firstRequest, secondRequest }));
            Assert.That(requests[0].Context, Is.SameAs(firstContext));
            Assert.That(requests[1].Context, Is.SameAs(secondContext));
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Pending));
            gates[1].TrySetResult();
            yield return null;
            Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Succeeded));
        }

        [UnityTest]
        public IEnumerator CompletionCallbackCanEnqueueWithoutLosingRequest()
        {
            var gates = new List<UniTaskCompletionSource>();
            var scopes = new List<SongScanScope>();
            var coordinator = new SongRefreshCoordinator(request =>
            {
                scopes.Add(request.Scope);
                var gate = new UniTaskCompletionSource();
                gates.Add(gate);
                return gate.Task;
            });
            var first = coordinator.Request(Request(SongScanScope.Updates));
            UniTask followup = default;
            async UniTask EnqueueAfterCompletion()
            {
                await first;
                followup = coordinator.Request(Request(SongScanScope.Updates));
            }
            var continuation = EnqueueAfterCompletion();
            gates[0].TrySetResult();
            yield return null;
            Assert.That(scopes.Count, Is.EqualTo(2));
            gates[1].TrySetResult();
            yield return null;
            Assert.That(followup.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            continuation.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator FailedScan_PropagatesAndDoesNotBlockQueuedRefresh()
        {
            var gates = new List<UniTaskCompletionSource>();
            int blockerCount = 0;
            var coordinator = new SongRefreshCoordinator(_ =>
            {
                Assert.That(blockerCount, Is.EqualTo(1));
                var gate = new UniTaskCompletionSource();
                gates.Add(gate);
                return gate.Task;
            }, () =>
            {
                blockerCount++;
                return new CallbackDisposable(() => blockerCount--);
            });
            UniTask failed = coordinator.Request(Request(SongScanScope.Updates));
            UniTask queued = coordinator.Request(Request(SongScanScope.Upgrades));
            gates[0].TrySetException(new InvalidOperationException("scan failed"));
            yield return null;
            Assert.That(failed.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.Throws<InvalidOperationException>(() => failed.GetAwaiter().GetResult());
            Assert.That(gates.Count, Is.EqualTo(2));
            Assert.That(blockerCount, Is.EqualTo(1));
            gates[1].TrySetResult();
            yield return null;
            Assert.That(queued.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(blockerCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator IdenticalPendingRequestsRemainSeparateTransactions()
        {
            var gates = new List<UniTaskCompletionSource>();
            var coordinator = new SongRefreshCoordinator(_ =>
            {
                var gate = new UniTaskCompletionSource();
                gates.Add(gate);
                return gate.Task;
            });
            var active = coordinator.Request(Request(SongScanScope.All));
            var pending = coordinator.Request(Request(SongScanScope.Updates));
            var anotherPending = coordinator.Request(Request(SongScanScope.Updates));
            gates[0].TrySetResult();
            yield return null;
            Assert.That(gates.Count, Is.EqualTo(2));
            gates[1].TrySetResult();
            yield return null;
            Assert.That(gates.Count, Is.EqualTo(3));
            Assert.That(anotherPending.Status, Is.EqualTo(UniTaskStatus.Pending));
            gates[2].TrySetResult();
            yield return null;
            Assert.That(active.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(pending.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(anotherPending.Status, Is.EqualTo(UniTaskStatus.Succeeded));
        }

        [Test]
        public void BlockerAcquisitionFailureFaultsRequestAndReleasesGate()
        {
            int calls = 0;
            var coordinator = new SongRefreshCoordinator(_ =>
            {
                calls++;
                return UniTask.CompletedTask;
            }, () => throw new InvalidOperationException("blocker unavailable"));
            var request = coordinator.Request(Request(SongScanScope.Updates));
            Assert.That(request.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.Throws<InvalidOperationException>(() => request.GetAwaiter().GetResult());
            Assert.That(calls, Is.Zero);
            Assert.That(coordinator.IsRebuilding, Is.False);
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private readonly Action _dispose;
            public CallbackDisposable(Action dispose) => _dispose = dispose;
            public void Dispose() => _dispose();
        }
    }
}
