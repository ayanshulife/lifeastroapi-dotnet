using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LifeAstroApi.Tests
{
    /// <summary>A recorded request.</summary>
    internal sealed class Recorded
    {
        internal Recorded(HttpRequestMessage request)
        {
            Method = request.Method.Method;
            Uri = request.RequestUri!;
            Headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        }

        internal string Method { get; }

        internal Uri Uri { get; }

        internal Dictionary<string, string> Headers { get; }

        /// <summary>The raw (escaped) path.</summary>
        internal string Path => Uri.AbsolutePath;

        /// <summary>The raw path and query, e.g. <c>/v1/panchang/basic?lat=19.076&amp;...</c>.</summary>
        internal string PathAndQuery => Uri.PathAndQuery;

        /// <summary>Decoded query parameters.</summary>
        internal Dictionary<string, string> Query
        {
            get
            {
                var q = new Dictionary<string, string>(StringComparer.Ordinal);
                string raw = Uri.Query.TrimStart('?');
                if (raw.Length == 0)
                {
                    return q;
                }

                foreach (string pair in raw.Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    string k = eq < 0 ? pair : pair.Substring(0, eq);
                    string v = eq < 0 ? string.Empty : pair.Substring(eq + 1);
                    q[WebUtility.UrlDecode(k)] = WebUtility.UrlDecode(v);
                }

                return q;
            }
        }

        internal string? Header(string name) => Headers.TryGetValue(name, out string? v) ? v : null;
    }

    /// <summary>A canned response.</summary>
    internal sealed class Reply
    {
        internal Reply(int status, byte[] body, IDictionary<string, string>? headers = null, int delayMs = 0)
        {
            Status = status;
            Body = body;
            Headers = new Dictionary<string, string>(headers ?? new Dictionary<string, string> { ["Content-Type"] = "application/json" }, StringComparer.OrdinalIgnoreCase);
            DelayMs = delayMs;
        }

        internal int Status { get; }

        internal byte[] Body { get; }

        internal Dictionary<string, string> Headers { get; }

        internal int DelayMs { get; }

        internal static Reply Json(int status, string body) => new Reply(status, Encoding.UTF8.GetBytes(body));

        internal static Reply Ok(string body) => Json(200, body);

        internal Reply With(string name, string value)
        {
            var h = new Dictionary<string, string>(Headers, StringComparer.OrdinalIgnoreCase) { [name] = value };
            return new Reply(Status, Body, h, DelayMs);
        }

        internal Reply With(IDictionary<string, string> extra)
        {
            Reply r = this;
            foreach (KeyValuePair<string, string> e in extra)
            {
                r = r.With(e.Key, e.Value);
            }

            return r;
        }

        internal Reply Delayed(int ms) => new Reply(Status, Body, Headers, ms);

        internal HttpResponseMessage ToResponse()
        {
            var content = new ByteArrayContent(Body);
            var response = new HttpResponseMessage((HttpStatusCode)Status) { Content = content };
            foreach (KeyValuePair<string, string> h in Headers)
            {
                if (!response.Headers.TryAddWithoutValidation(h.Key, h.Value))
                {
                    content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                }
            }

            return response;
        }
    }

    /// <summary>An in-memory transport: records every request and answers with what the responder returns.</summary>
    internal sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<Recorded> _calls = new List<Recorded>();

        internal Func<Recorded, Reply> Responder { get; set; } = _ => Reply.Ok("{}");

        internal IReadOnlyList<Recorded> Calls
        {
            get
            {
                lock (_calls)
                {
                    return _calls.ToList();
                }
            }
        }

        internal Recorded Last
        {
            get
            {
                IReadOnlyList<Recorded> all = Calls;
                if (all.Count == 0)
                {
                    throw new InvalidOperationException("no request reached the stub handler");
                }

                return all[all.Count - 1];
            }
        }

        internal StubHandler Respond(Func<Recorded, Reply> responder)
        {
            Responder = responder;
            return this;
        }

        internal StubHandler Respond(Reply reply) => Respond(_ => reply);

        internal void Reset()
        {
            lock (_calls)
            {
                _calls.Clear();
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var rec = new Recorded(request);
            lock (_calls)
            {
                _calls.Add(rec);
            }

            Reply reply = Responder(rec);
            if (reply.DelayMs > 0)
            {
                await Task.Delay(reply.DelayMs, cancellationToken).ConfigureAwait(false);
            }

            return reply.ToResponse();
        }
    }

    /// <summary>Shared inputs and client factories. The neutral sample person is Arjun Mehta, born 1990-01-15 10:30 in Mumbai.</summary>
    internal static class Fixtures
    {
        internal static readonly BirthInput Arjun = new BirthInput("1990-01-15", "10:30", "Asia/Kolkata", 19.076, 72.8777);

        /// <summary>The second chart of the Node SDK's tests (Delhi).</summary>
        internal static readonly BirthInput Girl = new BirthInput("1992-03-20", "08:15", "Asia/Kolkata", 28.6139, 77.209);

        internal static readonly Place Mumbai = new Place(19.076, 72.8777, "Asia/Kolkata");

        internal const string BaseUrl = "https://sdk.test";

        /// <summary>A client on a stub transport: key <c>dv_live_test</c>, no retries unless set, no waits between retries.</summary>
        internal static LifeAstroClient Client(StubHandler stub, Action<LifeAstroClientOptions>? configure = null, Func<string, string?>? env = null)
        {
            var options = new LifeAstroClientOptions { ApiKey = "dv_live_test", BaseUrl = BaseUrl, MaxRetries = 0 };
            configure?.Invoke(options);
            var client = new LifeAstroClient(new HttpClient(stub), options, env ?? (_ => null));
            client.Core.Backoff = (_, _) => TimeSpan.Zero;
            return client;
        }
    }

    /// <summary>
    /// A raw TCP server on 127.0.0.1 for what an in-memory handler cannot show: connections dropped without
    /// an answer, a body that arrives too slowly. Each connection gets the next behaviour.
    /// </summary>
    internal sealed class RawServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<int, NetworkStream, Task> _handle;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private int _connections;

        internal RawServer(Func<int, NetworkStream, Task> handle)
        {
            _handle = handle;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _ = Task.Run(AcceptLoop);
        }

        internal int Connections => Volatile.Read(ref _connections);

        internal string BaseUrl => "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;

        internal static async Task ReadRequestAsync(NetworkStream stream)
        {
            var buf = new byte[8192];
            var seen = new StringBuilder();
            while (!seen.ToString().Contains("\r\n\r\n"))
            {
                int n = await stream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                if (n <= 0)
                {
                    return;
                }

                seen.Append(Encoding.ASCII.GetString(buf, 0, n));
            }
        }

        internal static Task WriteAsync(NetworkStream stream, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return stream.WriteAsync(bytes, 0, bytes.Length);
        }

        /// <summary>A 200 JSON answer that closes the connection.</summary>
        internal static async Task AnswerAsync(NetworkStream stream, string json)
        {
            await ReadRequestAsync(stream).ConfigureAwait(false);
            byte[] body = Encoding.UTF8.GetBytes(json);
            await WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>A port nothing listens on.</summary>
        internal static int ClosedPort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                int n = Interlocked.Increment(ref _connections);
                _ = Task.Run(async () =>
                {
                    using (tcp)
                    {
                        try
                        {
                            await _handle(n, tcp.GetStream()).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            // The client went away (timeout tests).
                        }
                    }
                });
            }
        }
    }
}
