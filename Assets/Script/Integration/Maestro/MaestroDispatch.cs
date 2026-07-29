using System.Threading.Tasks;

namespace YARG.Integration.Maestro
{
    /// <summary>
    /// Bridges a single command from the transport worker thread to the Unity main
    /// thread.  The worker creates one via <see cref="IMaestroHost.EnqueueCommand"/>,
    /// the controller completes <see cref="Result"/> on the main thread, and the
    /// worker awaits it (bounded) to produce the immediate HTTP acknowledgement.
    /// </summary>
    public sealed class MaestroDispatch
    {
        /// <summary>The normalized command to process (already validated by the parser).</summary>
        public MaestroCommand Command { get; }

        /// <summary>Completes with the immediate command acknowledgement.</summary>
        public TaskCompletionSource<MaestroCommandResponse> Result { get; }

        public MaestroDispatch(MaestroCommand command)
        {
            Command = command;
            // RunContinuationsAsynchronously: the controller completes this on the main
            // thread; we never want the main-thread continuation to run inline there.
            Result = new TaskCompletionSource<MaestroCommandResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
