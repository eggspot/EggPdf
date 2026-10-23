using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Tests.Unit.Core;

/// <summary>
/// A minimal loopback HTTP/1.1 server for resolver tests: every request gets the response the handler
/// returns for its path, written raw so tests control status, headers and body framing exactly
/// (e.g. a body with no Content-Length, or a server that never answers).
/// </summary>
internal sealed class LocalHttpServer : IDisposable
{
    internal sealed class Response
    {
        public int Status { get; set; } = 200;
        public string? Location { get; set; }
        public byte[] Body { get; set; } = Array.Empty<byte>();
        /// <summary>Omit Content-Length and end the body by closing the connection.</summary>
        public bool OmitContentLength { get; set; }
        /// <summary>Accept the request but never answer it.</summary>
        public bool Hang { get; set; }
    }

    private readonly TcpListener _listener;
    private readonly Func<string, Response> _handler;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Request paths in arrival order.</summary>
    public ConcurrentQueue<string> Requests { get; } = new();

    public int Port { get; }

    public string Url(string path) => "http://127.0.0.1:" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + path;

    public LocalHttpServer(Func<string, Response> handler)
    {
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoop();
    }

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch { return; }
            _ = Serve(client);
        }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII);
                var requestLine = await reader.ReadLineAsync();
                if (requestLine == null) return;
                string? header;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync())) { }

                var path = requestLine.Split(' ')[1];
                Requests.Enqueue(path);
                var response = _handler(path);
                if (response.Hang)
                {
                    try { await Task.Delay(Timeout.Infinite, _stop.Token); } catch { }
                    return;
                }

                var head = new StringBuilder();
                head.Append("HTTP/1.1 ").Append(response.Status.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" X\r\n");
                if (response.Location != null) head.Append("Location: ").Append(response.Location).Append("\r\n");
                if (!response.OmitContentLength)
                    head.Append("Content-Length: ").Append(response.Body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
                head.Append("Connection: close\r\n\r\n");
                var headBytes = Encoding.ASCII.GetBytes(head.ToString());
                await stream.WriteAsync(headBytes, 0, headBytes.Length);
                await stream.WriteAsync(response.Body, 0, response.Body.Length);
                await stream.FlushAsync();
            }
            catch
            {
                // Client gave up (size cap, timeout) -- expected in several tests
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}
