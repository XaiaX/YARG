using System.Collections.Generic;

namespace YARG.Integration.Maestro
{
    /// <summary>
    /// Thread-safe FIFO queue for commands crossing the transport→main-thread boundary.
    /// The transport worker (background thread) calls <see cref="Enqueue"/>;
    /// <see cref="MaestroController"/> calls <see cref="Drain"/> on the Unity main thread.
    /// <para>
    /// Uses a simple <c>lock</c> + <c>Queue&lt;T&gt;</c> to avoid any dependency on
    /// <c>System.Collections.Concurrent</c> availability across all build targets.
    /// </para>
    /// </summary>
    public sealed class MaestroCommandQueue
    {
        private readonly Queue<MaestroDispatch> _queue = new();
        private readonly object _lock = new();

        /// <summary>Enqueue a dispatch from the transport worker thread.</summary>
        public void Enqueue(MaestroDispatch dispatch)
        {
            lock (_lock)
            {
                _queue.Enqueue(dispatch);
            }
        }

        /// <summary>
        /// Drain and return all pending dispatches.  Called on the Unity main thread.
        /// Returns an empty list if nothing is pending.
        /// </summary>
        public List<MaestroDispatch> Drain()
        {
            lock (_lock)
            {
                if (_queue.Count == 0)
                {
                    return new List<MaestroDispatch>();
                }

                var result = new List<MaestroDispatch>(_queue.Count);
                while (_queue.Count > 0)
                {
                    result.Add(_queue.Dequeue());
                }
                return result;
            }
        }

        /// <summary>Current pending count (diagnostic).</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _queue.Count;
                }
            }
        }
    }
}
