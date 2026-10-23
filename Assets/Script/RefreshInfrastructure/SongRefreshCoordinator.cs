using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using YARG.Core.Song.Cache;

namespace YARG.Song
{
    /// <summary>A captured scan request. Later settings edits cannot change a queued operation.</summary>
    public sealed class SongRefreshRequest
    {
        public SongScanScope Scope { get; }
        public SongSourceConfiguration Sources { get; }
        public bool Quick { get; }
        public object Context { get; }
        public bool FullDirectoryPlaylists { get; }

        public SongRefreshRequest(SongScanScope scope, SongSourceConfiguration sources, bool quick,
            object context = null, bool fullDirectoryPlaylists = false)
        {
            Scope = scope;
            Sources = sources ?? throw new ArgumentNullException(nameof(sources));
            Quick = quick;
            Context = context;
            FullDirectoryPlaylists = fullDirectoryPlaylists;
        }

        public bool IsEquivalentTo(SongRefreshRequest other) => other != null &&
            Scope == other.Scope && Sources.Equals(other.Sources) && Quick == other.Quick &&
            FullDirectoryPlaylists == other.FullDirectoryPlaylists;
    }

    /// <summary>Equivalent active requests join; distinct requests execute FIFO without broadening scopes.</summary>
    public sealed class SongRefreshCoordinator
    {
        private sealed class Operation
        {
            public readonly SongRefreshRequest Request;
            public readonly List<UniTaskCompletionSource> Waiters = new();
            public Operation(SongRefreshRequest request) => Request = request;
        }

        private readonly Func<SongRefreshRequest, UniTask> _refresh;
        private readonly Func<IDisposable> _acquireInputBlocker;
        private readonly Queue<Operation> _queue = new();
        private Operation _active;
        private bool _running;

        public bool IsRebuilding => _running;

        public SongRefreshCoordinator(Func<SongRefreshRequest, UniTask> refresh,
            Func<IDisposable> acquireInputBlocker = null)
        {
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            _acquireInputBlocker = acquireInputBlocker;
        }

        public UniTask Request(SongRefreshRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var completion = new UniTaskCompletionSource();
            if (_active != null && _active.Request.IsEquivalentTo(request))
            {
                _active.Waiters.Add(completion);
                return completion.Task;
            }

            var operation = new Operation(request);
            operation.Waiters.Add(completion);
            _queue.Enqueue(operation);
            if (!_running)
            {
                _running = true;
                ProcessQueue().Forget();
            }
            return completion.Task;
        }

        private async UniTask ProcessQueue()
        {
            IDisposable blocker = null;
            try
            {
                // Acquire synchronously, before invoking any asynchronous scan work.
                blocker = _acquireInputBlocker?.Invoke();
                while (_queue.Count != 0)
                {
                    var operation = _queue.Dequeue();
                    _active = operation;
                    try
                    {
                        await _refresh(operation.Request);
                        _active = null;
                        foreach (var waiter in operation.Waiters) waiter.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        _active = null;
                        foreach (var waiter in operation.Waiters) waiter.TrySetException(exception);
                    }
                    finally
                    {
                        _active = null;
                    }
                }
            }
            catch (Exception exception)
            {
                while (_queue.Count != 0)
                {
                    foreach (var waiter in _queue.Dequeue().Waiters) waiter.TrySetException(exception);
                }
            }
            finally
            {
                _active = null;
                _running = false;
                blocker?.Dispose();
            }
        }
    }
}
