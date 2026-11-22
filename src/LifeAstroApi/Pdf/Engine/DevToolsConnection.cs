using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LifeAstroApi.Pdf.Engine
{
    /// <summary>
    /// One DevTools WebSocket, shared by every tab: commands may be sent from several threads and each
    /// answer finds its caller by id. Of the events, only a tab that crashed or went away matters: its
    /// commands are never answered, so they fail at once instead of waiting for a timeout.
    /// </summary>
    internal sealed class DevToolsConnection
    {
        private static readonly JsonWriterOptions Writer = new JsonWriterOptions
        {
            // The HTML goes over as UTF-8 rather than six bytes for every Indic character.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly ClientWebSocket _socket;
        private readonly CancellationTokenSource _closed = new CancellationTokenSource();
        private readonly SemaphoreSlim _sending = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<long, PendingCommand> _pending = new ConcurrentDictionary<long, PendingCommand>();
        private readonly ConcurrentDictionary<string, bool> _crashed = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        private long _lastId;
        private volatile Exception? _failure;

        private DevToolsConnection(ClientWebSocket socket)
        {
            _socket = socket;
        }

        internal bool IsOpen => _failure is null;

        internal static async Task<DevToolsConnection> ConnectAsync(Uri url, bool local, CancellationToken cancellationToken)
        {
            ClientWebSocket? socket = null;
            try
            {
                socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
                if (local)
                {
                    // A browser this process started: a system proxy has no business in between.
                    socket.Options.Proxy = null;
                }

                await socket.ConnectAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (PlatformNotSupportedException e)
            {
                socket?.Dispose();
                throw new LifeAstroPdfException(
                    "This platform has no WebSocket client (.NET Framework needs Windows 8 / Server 2012 or later). "
                        + "Use a GotenbergPdfEngine instead.",
                    "browser_error",
                    e);
            }
            catch (Exception e)
            {
                socket?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("Cancelled while connecting to the browser.", e, cancellationToken);
                }

                throw new LifeAstroPdfException(
                    "Cannot connect to the browser at " + url.GetLeftPart(UriPartial.Authority) + ": " + e.Message,
                    "connection_error",
                    e);
            }

            var connection = new DevToolsConnection(socket);
            _ = Task.Run(connection.ReceiveAsync);
            return connection;
        }

        internal async Task<JsonElement> SendAsync(
            string method,
            Action<Utf8JsonWriter>? writeParameters,
            string? sessionId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long id = Interlocked.Increment(ref _lastId);
            byte[] message;
            using (var buffer = new MemoryStream())
            {
                using (var json = new Utf8JsonWriter(buffer, Writer))
                {
                    json.WriteStartObject();
                    json.WriteNumber("id", id);
                    json.WriteString("method", method);
                    if (sessionId != null)
                    {
                        json.WriteString("sessionId", sessionId);
                    }

                    json.WriteStartObject("params");
                    writeParameters?.Invoke(json);
                    json.WriteEndObject();
                    json.WriteEndObject();
                }

                message = buffer.ToArray();
            }

            var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Fail() may complete it after the caller has left (cancelled, or thrown before it awaited).
            EngineTasks.Observe(reply.Task);
            _pending[id] = new PendingCommand(reply, sessionId);
            try
            {
                Exception? failure = _failure; // read after adding: Fail() cannot miss this call
                if (failure != null)
                {
                    throw Lost(failure);
                }

                if (sessionId != null && _crashed.ContainsKey(sessionId))
                {
                    throw TabGone();
                }

                // The caller's token never reaches the socket: cancelling a WebSocket send aborts the connection.
                _ = WriteAsync(message, reply, cancellationToken);
                using (cancellationToken.Register(() => reply.TrySetCanceled()))
                {
                    JsonElement answer = await reply.Task.ConfigureAwait(false);
                    if (answer.TryGetProperty("error", out JsonElement error))
                    {
                        string detail = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement text)
                            ? text.ToString() ?? string.Empty
                            : error.ToString() ?? string.Empty;
                        throw new LifeAstroPdfException("The browser refused " + method + ": " + detail, "render_failed");
                    }

                    return answer.TryGetProperty("result", out JsonElement result) ? result : answer;
                }
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        private async Task WriteAsync(byte[] message, TaskCompletionSource<JsonElement> reply, CancellationToken cancellationToken)
        {
            try
            {
                await _sending.WaitAsync(_closed.Token).ConfigureAwait(false);
                try
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await _socket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, _closed.Token)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    _sending.Release();
                }
            }
            catch (Exception e)
            {
                Fail(e);
                reply.TrySetException(Lost(e));
            }
        }

        private async Task ReceiveAsync()
        {
            var chunk = new byte[64 * 1024];
            try
            {
                while (true)
                {
                    using (var message = new MemoryStream())
                    {
                        WebSocketReceiveResult part;
                        do
                        {
                            part = await _socket.ReceiveAsync(new ArraySegment<byte>(chunk), _closed.Token).ConfigureAwait(false);
                            if (part.MessageType == WebSocketMessageType.Close)
                            {
                                Fail(new IOException("the browser closed the connection"));
                                return;
                            }

                            message.Write(chunk, 0, part.Count);
                        }
                        while (!part.EndOfMessage);

                        message.Position = 0;
                        using (JsonDocument document = JsonDocument.Parse(message))
                        {
                            JsonElement root = document.RootElement;
                            if (root.TryGetProperty("id", out JsonElement id))
                            {
                                if (id.TryGetInt64(out long number) && _pending.TryGetValue(number, out PendingCommand? command))
                                {
                                    command.Reply.TrySetResult(root.Clone());
                                }
                            }
                            else
                            {
                                OnEvent(root);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void Fail(Exception error)
        {
            _failure ??= error;
            foreach (PendingCommand command in _pending.Values)
            {
                command.Reply.TrySetException(Lost(error));
            }
        }

        private void OnEvent(JsonElement message)
        {
            if (!message.TryGetProperty("method", out JsonElement method))
            {
                return;
            }

            string? session;
            if (method.ValueEquals("Inspector.targetCrashed"))
            {
                // Sent on the tab's own session.
                session = StringField(message, "sessionId");
                if (session != null)
                {
                    _crashed[session] = true;
                }
            }
            else if (method.ValueEquals("Target.detachedFromTarget") && message.TryGetProperty("params", out JsonElement details))
            {
                session = StringField(details, "sessionId");
                if (session != null)
                {
                    _crashed.TryRemove(session, out _);
                }
            }
            else
            {
                return;
            }

            if (session is null)
            {
                return;
            }

            foreach (PendingCommand command in _pending.Values)
            {
                if (string.Equals(command.Session, session, StringComparison.Ordinal))
                {
                    command.Reply.TrySetException(TabGone());
                }
            }
        }

        private static string? StringField(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static LifeAstroPdfException TabGone() =>
            new LifeAstroPdfException("The browser's tab crashed or was closed during the print.", "browser_error");

        private sealed class PendingCommand
        {
            internal PendingCommand(TaskCompletionSource<JsonElement> reply, string? session)
            {
                Reply = reply;
                Session = session;
            }

            internal TaskCompletionSource<JsonElement> Reply { get; }

            internal string? Session { get; }
        }

        private static LifeAstroPdfException Lost(Exception error) =>
            new LifeAstroPdfException("The connection to the browser was lost: " + error.Message, "browser_error", error);

        internal void Close()
        {
            Fail(new IOException("the connection was closed"));
            EngineTasks.CancelQuietly(_closed);
            try
            {
                _socket.Abort();
                _socket.Dispose();
            }
            catch (Exception)
            {
                // Closing must not fail: the socket is of no use any more either way.
            }
        }
    }
}
