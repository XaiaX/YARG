using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace YARG.Integration.Maestro
{
    /// <summary>A parsed HTTP request line + headers + body text.</summary>
    internal sealed class MaestroHttpRequest
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public string BodyText { get; set; }
        public List<MaestroHeader> Headers { get; } = new();
    }

    internal sealed class MaestroHeader
    {
        public string Name { get; }
        public string Value { get; }
        public MaestroHeader(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// Bounded HTTP/1.1 request reader for Maestro.  Reads exactly one request from a
    /// <see cref="NetworkStream"/> using <c>Connection: close</c> semantics: read the
    /// request line, headers, then (for POST) Content-Length bytes of body.  Every stage
    /// is size-capped to reject malformed/oversized input.
    /// <para>
    /// This is deliberately minimal: no chunked transfer, no keep-alive, no trailers.
    /// It only supports what the Maestro routes need (GET + fixed-size JSON POST).
    /// </para>
    /// </summary>
    internal sealed class MaestroRequestReader
    {
        public const string OversizedBody = "oversized";

        private readonly NetworkStream _stream;
        private readonly int _maxRequestLine;
        private readonly int _maxHeaders;
        private readonly int _maxBody;

        public MaestroRequestReader(NetworkStream stream, int maxRequestLine, int maxHeaders, int maxBody)
        {
            _stream = stream;
            _maxRequestLine = maxRequestLine;
            _maxHeaders = maxHeaders;
            _maxBody = maxBody;
        }

        public bool TryReadRequest(out MaestroHttpRequest request, out string error)
        {
            request = null;
            error = null;

            // --- Request line: "METHOD SP PATH SP HTTP/1.1" ---
            if (!TryReadLine(_stream, _maxRequestLine, out string requestLine, out error))
            {
                error ??= "Empty or oversized request line.";
                return false;
            }

            if (string.IsNullOrEmpty(requestLine))
            {
                error = "Empty request line.";
                return false;
            }

            var parts = requestLine.Split(' ');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                error = "Malformed request line.";
                return false;
            }

            if (!parts[2].StartsWith("HTTP/1", System.StringComparison.Ordinal))
            {
                error = "Unsupported HTTP version.";
                return false;
            }

            var req = new MaestroHttpRequest
            {
                Method = parts[0],
                Path = parts[1],
            };

            // --- Headers ---
            int headerBytes = 0;
            while (true)
            {
                if (!TryReadLine(_stream, 8 * 1024, out string headerLine, out error))
                {
                    error ??= "Truncated headers.";
                    return false;
                }

                headerBytes += Encoding.UTF8.GetByteCount(headerLine) + 2;
                if (headerBytes > _maxHeaders)
                {
                    error = "Oversized request headers.";
                    return false;
                }

                // Blank line ends the header block.
                if (headerLine.Length == 0)
                {
                    break;
                }

                int colon = headerLine.IndexOf(':');
                if (colon <= 0)
                {
                    error = "Malformed header line.";
                    return false;
                }

                string name = headerLine.Substring(0, colon).Trim();
                string value = headerLine.Substring(colon + 1).Trim();
                req.Headers.Add(new MaestroHeader(name, value));
            }

            // --- Body (POST only, fixed Content-Length) ---
            if (req.Method == "POST")
            {
                if (!TryGetContentLength(req, out int contentLength, out error))
                {
                    return false;
                }

                if (contentLength < 0)
                {
                    error = "Invalid Content-Length.";
                    return false;
                }

                if (contentLength > _maxBody)
                {
                    error = OversizedBody;
                    return false;
                }

                if (contentLength > 0)
                {
                    var bodyBuf = new byte[contentLength];
                    int read = 0;
                    while (read < contentLength)
                    {
                        int n = _stream.Read(bodyBuf, read, contentLength - read);
                        if (n <= 0)
                        {
                            error = "Truncated request body.";
                            return false;
                        }

                        read += n;
                    }

                    req.BodyText = Encoding.UTF8.GetString(bodyBuf, 0, read);
                }
                else
                {
                    req.BodyText = string.Empty;
                }
            }

            request = req;
            return true;
        }

        private static bool TryGetContentLength(MaestroHttpRequest req, out int length, out string error)
        {
            length = 0;
            error = null;

            foreach (var h in req.Headers)
            {
                if (System.StringComparer.OrdinalIgnoreCase.Equals(h.Name, "Content-Length"))
                {
                    if (!int.TryParse(h.Value, out length))
                    {
                        error = "Malformed Content-Length.";
                        return false;
                    }

                    return true;
                }
            }

            // No Content-Length on a POST: allow zero-length (treated as empty body).
            length = 0;
            return true;
        }

        /// <summary>
        /// Reads a single CRLF-terminated line, stripping the trailing CR/LF.  Bounded
        /// by <paramref name="maxBytes"/>; returns false with an error if the line
        /// exceeds the bound or the stream ends without a terminator.
        /// </summary>
        private static bool TryReadLine(NetworkStream stream, int maxBytes, out string line, out string error)
        {
            line = null;
            error = null;

            var sb = new StringBuilder(maxBytes + 16);
            int total = 0;
            bool sawCr = false;

            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0)
                {
                    // End of stream. If we read nothing, that's a clean EOF (caller treats
                    // empty line as malformed). Otherwise it's a truncation mid-line.
                    if (total == 0)
                    {
                        error = null; // distinguishable: caller sets its own message
                        line = string.Empty;
                        return true;
                    }

                    error = "Truncated request line/header.";
                    return false;
                }

                total++;

                if (b == '\r')
                {
                    sawCr = true;
                    continue;
                }

                if (b == '\n')
                {
                    // End of line. If the previous byte was CR it's already consumed.
                    line = sb.ToString();
                    return true;
                }

                // We skipped a CR that wasn't followed by LF — that's technically a bare CR;
                // tolerate it by appending nothing special and continuing.
                if (sawCr)
                {
                    sb.Append('\r');
                    sawCr = false;
                }

                if (total > maxBytes)
                {
                    error = "Oversized request line/header.";
                    return false;
                }

                sb.Append((char) b);
            }
        }
    }
}
