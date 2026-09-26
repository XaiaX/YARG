using System;
using Cysharp.Threading.Tasks;

namespace YARG.Song
{
    /// <summary>Serializes full refresh transactions; concurrent requests coalesce to the latest pending mode.</summary>
    public sealed class SongRefreshCoordinator
    {
        private readonly Func<bool, UniTask> _refresh;
        private UniTaskCompletionSource _pending;
        private bool _pendingQuick;
        private bool _running;

        public bool IsRebuilding => _running;

        public SongRefreshCoordinator(Func<bool, UniTask> refresh)
        {
            _refresh = refresh;
        }

        public UniTask Request(bool quick)
        {
            _pendingQuick = _pending == null ? quick : _pendingQuick && quick;
            _pending ??= new UniTaskCompletionSource();
            UniTask result = _pending.Task;
            if (!_running)
            {
                _running = true;
                ProcessQueue().Forget();
            }
            return result;
        }

        private async UniTask ProcessQueue()
        {
            while (_pending != null)
            {
                UniTaskCompletionSource completion = _pending;
                bool quick = _pendingQuick;
                _pending = null;
                try
                {
                    await _refresh(quick);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }
            _running = false;
        }
    }
}
