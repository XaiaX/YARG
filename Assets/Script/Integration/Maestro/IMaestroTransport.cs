using System.Threading.Tasks;

namespace YARG.Integration.Maestro
{
    /// <summary>
    /// Host-side capabilities the transport queries and pushes commands through.
    /// Implemented by <see cref="MaestroController"/>.  Keeping this as an interface
    /// allows the transport (TcpListener HTTP server) to depend on the contract
    /// rather than the concrete MonoBehaviour.
    /// </summary>
    public interface IMaestroHost
    {
        /// <summary>Whether Maestro hosting is enabled (transport running).</summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Returns the latest immutable snapshot built on the main thread, or null
        /// if none has been published yet.  Safe to call from a transport worker
        /// thread — the returned object is never mutated after publication.
        /// </summary>
        MaestroSnapshot GetSnapshot();

        /// <summary>
        /// Enqueue a command from a transport worker thread for main-thread
        /// processing and return a completable result.  The worker awaits
        /// <see cref="MaestroDispatch.Result"/> for the immediate acknowledgement.
        /// </summary>
        MaestroDispatch EnqueueCommand(MaestroCommand command);

        /// <summary>Validate a bearer/pairing token.  Returns false if not enabled.</summary>
        bool ValidateToken(string token);

        /// <summary>The per-session pairing token (6-digit PIN), or null if disabled.</summary>
        string PairingToken { get; }
    }

    /// <summary>
    /// Transport-side lifecycle contract.  The TcpListener-based HTTP transport
    /// implements this.  The controller owns the instance.
    /// </summary>
    public interface IMaestroTransport
    {
        /// <summary>Start listening for connections.</summary>
        void Start(IMaestroHost host);

        /// <summary>Stop listening and release the socket/threads.</summary>
        void Stop();

        /// <summary>Whether the transport is currently accepting connections.</summary>
        bool IsRunning { get; }

        /// <summary>Human-readable bind address (e.g. "http://127.0.0.1:5151").</summary>
        string BoundAddress { get; }
    }
}
