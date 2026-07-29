using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using YARG.Core.Logging;

namespace YARG.Integration.Maestro
{
    /// <summary>
    /// Minimal compile-safe HTTP/1.1 transport for Maestro built directly on
    /// <see cref="TcpListener"/>.  No HttpListener, no WebSocket, no extra runtime
    /// dependency.  Serves the browser client, the revision-polled state snapshot,
    /// the hello/pairing response, and accepts authenticated JSON commands.
    /// <para>
    /// <b>Thread invariant:</b> the accept loop and every connection handler run on
    /// background threads.  They only (a) read the immutable snapshot via
    /// <see cref="IMaestroHost.GetSnapshot"/>, (b) enqueue commands via
    /// <see cref="IMaestroHost.EnqueueCommand"/>, and (c) call
    /// <see cref="IMaestroHost.ValidateToken"/>.  They never touch Unity APIs, singletons,
    /// players, or settings.  <see cref="Application.platform"/> is read only at Start/Stop
    /// which are called from the main thread.
    /// </para>
    /// </summary>
    public sealed class MaestroHttpTransport : IMaestroTransport
    {
        // --- Bounds (defensive parsing; rejects oversized/malformed requests) ---
        private const int MaxRequestLineBytes = 8 * 1024;
        private const int MaxHeaderBytes = 16 * 1024;
        private const int MaxBodyBytes = 256 * 1024;
        private const int ReadBufferBytes = 4 * 1024;
        private const int ReceiveTimeoutMs = 5_000;
        private const int SendTimeoutMs = 5_000;
        private const int CommandAwaitMs = 2_000;

        private readonly IPAddress _bindAddress;
        private int _port;
        private readonly bool _allowLan;

        // Captured on the main thread at Start() so worker threads never touch UnityEngine.
        private string _indexHtmlPath;
        private string _cachedIndexHtml;
        private bool _indexHtmlLoaded;

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private IMaestroHost _host;

        public bool IsRunning => _running;

        public string BoundAddress
        {
            get
            {
                string schemeAddr = _allowLan
                    ? (_bindAddress == null || _bindAddress.Equals(IPAddress.Any)
                        ? "0.0.0.0"
                        : _bindAddress.ToString())
                    : IPAddress.Loopback.ToString();
                return $"http://{schemeAddr}:{_port}";
            }
        }

        /// <param name="port">TCP port to bind. 0 lets the OS choose a free port.</param>
        /// <param name="allowLan">
        /// When false (default), binds <see cref="IPAddress.Loopback"/> only.  When true,
        /// binds <see cref="IPAddress.Any"/> (all interfaces) — caller must have already
        /// obtained explicit opt-in and is responsible for the pairing-token gate.
        /// </param>
        public MaestroHttpTransport(int port, bool allowLan)
        {
            _port = port <= 0 ? 0 : port;
            _allowLan = allowLan;
            _bindAddress = allowLan ? IPAddress.Any : IPAddress.Loopback;
        }

        // --------------------------------------------------------------------------------
        // Lifecycle (main thread)
        // --------------------------------------------------------------------------------

        public void Start(IMaestroHost host)
        {
            if (_running)
            {
                return;
            }

            _host = host ?? throw new ArgumentNullException(nameof(host));

            // Capture Unity-only data on the main thread (StreamingAssets path + the web
            // client body) so connection handlers never call into UnityEngine off-thread.
            try
            {
                _indexHtmlPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Maestro", "index.html");
                if (System.IO.File.Exists(_indexHtmlPath))
                {
                    _cachedIndexHtml = System.IO.File.ReadAllText(_indexHtmlPath);
                }
                _indexHtmlLoaded = true;
            }
            catch (System.Exception ex)
            {
                YargLogger.LogException(ex, "[Maestro] Could not read Maestro web client at Start.");
                _cachedIndexHtml = null;
                _indexHtmlLoaded = true; // treat missing file as "loaded (empty)" so we 404 cleanly
            }

            try
            {
                _listener = new TcpListener(_bindAddress, _port);
                _listener.Start();
            }
            catch (SocketException ex)
            {
                YargLogger.LogError($"[Maestro] Failed to bind {_bindAddress}:{_port}: {ex.SocketErrorCode} {ex.Message}");
                _listener = null;
                throw;
            }

            // After Start(), the actually-bound port is known (relevant when _port was 0).
            var local = (IPEndPoint) _listener.LocalEndpoint;
            _port = local.Port;

            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "Maestro-Accept" };
            _acceptThread.Start();

            YargLogger.LogInfo($"[Maestro] Transport listening on {BoundAddress} (LAN opt-in={_allowLan}).");
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;

            try
            {
                _listener?.Stop();
            }
            catch
            {
                // Ignore — best-effort shutdown.
            }

            var thread = _acceptThread;
            if (thread != null && thread.IsAlive)
            {
                try
                {
                    thread.Join(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // Ignore — background thread will die with the process.
                }
            }

            _listener = null;
            _acceptThread = null;
            _host = null;

            YargLogger.LogInfo("[Maestro] Transport stopped.");
        }

        // --------------------------------------------------------------------------------
        // Accept loop (background thread)
        // --------------------------------------------------------------------------------

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    // Expected when Stop() closes the listener.
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    YargLogger.LogException(ex, "[Maestro] Accept failed");
                    break;
                }

                // Handle each connection on its own short-lived background thread so a
                // slow client cannot stall the accept loop.  Connection: close means each
                // connection serves exactly one request.
                var handlerThread = new Thread(() => HandleConnection(client))
                {
                    IsBackground = true,
                    Name = "Maestro-Conn"
                };
                handlerThread.Start();
            }
        }

        private void HandleConnection(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = ReceiveTimeoutMs;
                client.SendTimeout = SendTimeoutMs;
                using (client)
                using (var stream = client.GetStream())
                {
                    try
                    {
                        HandleRequest(stream);
                    }
                    catch (Exception ex)
                    {
                        TrySendError(stream, MaestroErrorCode.InternalError, ex.Message);
                    }
                }
            }
            catch
            {
                // Connection-level failure; nothing more to do.
            }
        }

        // --------------------------------------------------------------------------------
        // Request parsing + dispatch (background thread)
        // --------------------------------------------------------------------------------

        private void HandleRequest(NetworkStream stream)
        {
            var reader = new MaestroRequestReader(stream, MaxRequestLineBytes, MaxHeaderBytes, MaxBodyBytes);
            if (!reader.TryReadRequest(out var request, out string parseError))
            {
                // Malformed/oversized request-line, headers, or body.
                string code = parseError == MaestroRequestReader.OversizedBody
                    ? MaestroErrorCode.PayloadTooLarge
                    : MaestroErrorCode.BadRequest;
                TrySendError(stream, code, parseError);
                return;
            }

            string method = request.Method;
            string pathQuery = request.Path;

            // Split path and query string.
            string path;
            var queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int q = pathQuery.IndexOf('?');
            if (q >= 0)
            {
                path = pathQuery.Substring(0, q);
                ParseQueryString(pathQuery.Substring(q + 1), queryParams);
            }
            else
            {
                path = pathQuery;
            }

            // Strict route + method allowlist.
            if (path == MaestroProtocol.RootRoute && method == "GET")
            {
                ServeIndex(stream);
                return;
            }

            if (path == MaestroProtocol.HelloRoute && method == "GET")
            {
                ServeHello(stream);
                return;
            }

            if (path == MaestroProtocol.StateRoute && method == "GET")
            {
                ServeState(stream, queryParams);
                return;
            }

            if (path == MaestroProtocol.CommandsRoute && method == "POST")
            {
                HandleCommand(stream, request);
                return;
            }

            // Known path, wrong method → 405; otherwise 404.
            if (path == MaestroProtocol.RootRoute || path == MaestroProtocol.HelloRoute ||
                path == MaestroProtocol.StateRoute || path == MaestroProtocol.CommandsRoute)
            {
                TrySendError(stream, MaestroErrorCode.MethodNotAllowed, $"Method {method} not allowed for {path}");
            }
            else
            {
                TrySendError(stream, MaestroErrorCode.NotFound, $"Unknown path: {path}");
            }
        }

        // --------------------------------------------------------------------------------
        // Route handlers (background thread; only host-contract + immutable snapshot)
        // --------------------------------------------------------------------------------

        private void ServeIndex(NetworkStream stream)
        {
            string html = LoadIndexHtml();
            if (html == null)
            {
                TrySendError(stream, MaestroErrorCode.NotFound, "Maestro web client not found in StreamingAssets.");
                return;
            }

            byte[] body = Encoding.UTF8.GetBytes(html);
            WriteResponse(stream, "200 OK", MaestroProtocol.HtmlContentType, body);
        }

        private void ServeHello(NetworkStream stream)
        {
            var host = _host;
            var snapshot = host?.GetSnapshot();

            // Use the server identity/version already captured on the main thread into the
            // snapshot, so we never touch UnityEngine.Application off the main thread here.
            string version = snapshot?.Server?.Version ?? string.Empty;

            var hello = new
            {
                protocolVersion = MaestroProtocol.ProtocolVersion,
                server = new
                {
                    identity = MaestroProtocol.ServerIdentity,
                    version = version,
                    capabilities = new[]
                    {
                        MaestroCapability.LiveVolume,
                        MaestroCapability.DeferredProfile,
                        MaestroCapability.RevisionPolling,
                    },
                },
                connection = new
                {
                    mode = _allowLan ? MaestroConnectionMode.Lan : MaestroConnectionMode.Loopback,
                    bindAddress = BoundAddress,
                    writeEnabled = host != null && host.IsEnabled,
                    // Hello is unauthenticated: advertise that a token is required, never the token itself.
                    pairingRequired = true,
                },
            };

            byte[] body = MaestroJson.ToBytes(hello);
            WriteResponse(stream, "200 OK", MaestroProtocol.JsonContentType, body);
        }

        private void ServeState(NetworkStream stream, IReadOnlyDictionary<string, string> queryParams)
        {
            var host = _host;
            var snapshot = host?.GetSnapshot();
            if (snapshot == null)
            {
                TrySendError(stream, MaestroErrorCode.NotFound, "Maestro host is not enabled.");
                return;
            }

            // Revision-aware polling: if the client passed ?since=<rev> and the current
            // revision is unchanged, return 304 with an empty body.
            if (queryParams.TryGetValue("since", out string sinceText) &&
                long.TryParse(sinceText, NumberStyles.None, CultureInfo.InvariantCulture, out long sinceRev) &&
                snapshot.Revision == sinceRev)
            {
                WriteResponse(stream, "304 Not Modified", MaestroProtocol.JsonContentType, Array.Empty<byte>());
                return;
            }

            byte[] body = MaestroJson.ToBytes(snapshot);
            WriteResponse(stream, "200 OK", MaestroProtocol.JsonContentType, body);
        }

        private void HandleCommand(NetworkStream stream, MaestroHttpRequest request)
        {
            var host = _host;
            if (host == null || !host.IsEnabled)
            {
                TrySendError(stream, MaestroErrorCode.Forbidden, "Maestro host is not enabled.");
                return;
            }

            // Authorization: require a valid bearer token for all state-changing commands.
            if (!TryAuthorize(request, out string authError))
            {
                TrySendError(stream, MaestroErrorCode.Unauthorized, authError);
                return;
            }

            // Content-Type must be JSON.
            if (!IsJsonContentType(request.Headers))
            {
                TrySendError(stream, MaestroErrorCode.BadRequest, "Content-Type must be application/json.");
                return;
            }

            // Parse the command envelope.
            MaestroCommandEnvelope envelope;
            try
            {
                envelope = MaestroJson.FromString<MaestroCommandEnvelope>(request.BodyText);
            }
            catch (Exception ex)
            {
                TrySendError(stream, MaestroErrorCode.BadRequest, "Malformed JSON command: " + ex.Message);
                return;
            }

            if (envelope == null || !MaestroCommandType.IsKnown(envelope.Type))
            {
                TrySendError(stream, MaestroErrorCode.BadRequest,
                    "Missing or unknown command 'type'.");
                return;
            }

            // Normalize into a validated MaestroCommand, or reject with a structured error.
            if (!MaestroCommandParser.TryParse(envelope, out MaestroCommand command, out string error))
            {
                var errBody = new MaestroCommandResponse
                {
                    Id = envelope?.Id,
                    Ok = false,
                    Status = null,
                    Error = new MaestroError { Code = MaestroErrorCode.BadRequest, Message = error },
                };
                WriteResponse(stream, "200 OK", MaestroProtocol.JsonContentType, MaestroJson.ToBytes(errBody));
                return;
            }

            // Enqueue and await the bounded main-thread acknowledgement.
            var dispatch = host.EnqueueCommand(command);
            MaestroCommandResponse response;
            try
            {
                // Bounded wait: if the main thread is stalled, return a queued ack rather
                // than holding the socket open indefinitely.
                if (dispatch.Result.Task.Wait(CommandAwaitMs))
                {
                    response = dispatch.Result.Task.Result;
                }
                else
                {
                    response = new MaestroCommandResponse
                    {
                        Id = command.Id,
                        Ok = true,
                        Status = MaestroCommandStatus.Queued,
                        Message = "Command queued; main thread did not acknowledge in time.",
                    };
                }
            }
            catch (Exception ex)
            {
                response = new MaestroCommandResponse
                {
                    Id = command.Id,
                    Ok = false,
                    Error = new MaestroError
                    {
                        Code = MaestroErrorCode.InternalError,
                        Message = ex.Message,
                    },
                };
            }

            WriteResponse(stream, "200 OK", MaestroProtocol.JsonContentType, MaestroJson.ToBytes(response));
        }

        // --------------------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------------------

        private bool TryAuthorize(MaestroHttpRequest request, out string error)
        {
            string header = FindHeader(request.Headers, "Authorization");
            if (string.IsNullOrEmpty(header) || !header.StartsWith(MaestroProtocol.BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = "Missing Bearer authorization.";
                return false;
            }

            string token = header.Substring(MaestroProtocol.BearerPrefix.Length).Trim();
            if (!_host.ValidateToken(token))
            {
                error = "Invalid or expired pairing token.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsJsonContentType(IReadOnlyList<MaestroHeader> headers)
        {
            string ct = FindHeader(headers, "Content-Type");
            if (string.IsNullOrEmpty(ct))
            {
                return false;
            }

            // Accept "application/json" and "application/json; charset=...".
            int semi = ct.IndexOf(';');
            string media = (semi >= 0 ? ct.Substring(0, semi) : ct).Trim();
            return string.Equals(media, "application/json", StringComparison.OrdinalIgnoreCase);
        }

        private static string FindHeader(IReadOnlyList<MaestroHeader> headers, string name)
        {
            if (headers == null)
            {
                return null;
            }

            for (int i = 0; i < headers.Count; i++)
            {
                if (string.Equals(headers[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return headers[i].Value;
                }
            }

            return null;
        }

        private static void ParseQueryString(string qs, Dictionary<string, string> into)
        {
            if (string.IsNullOrEmpty(qs))
            {
                return;
            }

            foreach (var pair in qs.Split('&'))
            {
                if (string.IsNullOrEmpty(pair))
                {
                    continue;
                }

                int eq = pair.IndexOf('=');
                string key, value;
                if (eq < 0)
                {
                    key = UrlDecode(pair);
                    value = string.Empty;
                }
                else
                {
                    key = UrlDecode(pair.Substring(0, eq));
                    value = UrlDecode(pair.Substring(eq + 1));
                }

                into[key] = value;
            }
        }

        private static string UrlDecode(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }

            return Uri.UnescapeDataString(s.Replace('+', ' '));
        }

        private string LoadIndexHtml()
        {
            // _cachedIndexHtml was read at Start() on the main thread. If empty/missing,
            // return null so ServeIndex returns a clean 404.
            return string.IsNullOrEmpty(_cachedIndexHtml) ? null : _cachedIndexHtml;
        }

        private void WriteResponse(NetworkStream stream, string statusLine, string contentType, byte[] body)
        {
            // Always Connection: close — one request per connection.
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(statusLine).Append("\r\n");
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            sb.Append("Connection: close\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("\r\n");

            byte[] head = Encoding.UTF8.GetBytes(sb.ToString());
            stream.Write(head, 0, head.Length);
            if (body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }
        }

        private void TrySendError(NetworkStream stream, string code, string message)
        {
            try
            {
                var err = new MaestroErrorResponse
                {
                    Error = new MaestroError { Code = code, Message = message },
                };

                string status = HttpStatusForCode(code);
                byte[] body = MaestroJson.ToBytes(err);
                WriteResponse(stream, status, MaestroProtocol.JsonContentType, body);
            }
            catch
            {
                // If we can't even write the error (client gone), give up silently.
            }
        }

        private static string HttpStatusForCode(string code)
        {
            switch (code)
            {
                case MaestroErrorCode.BadRequest: return "400 Bad Request";
                case MaestroErrorCode.Unauthorized: return "401 Unauthorized";
                case MaestroErrorCode.Forbidden: return "403 Forbidden";
                case MaestroErrorCode.NotFound: return "404 Not Found";
                case MaestroErrorCode.MethodNotAllowed: return "405 Method Not Allowed";
                case MaestroErrorCode.PayloadTooLarge: return "413 Payload Too Large";
                default: return "500 Internal Server Error";
            }
        }
    }
}
