using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// HTTP server on StreamPort.
//   GET /        → HTML viewer page with live latency meter
//   GET /stream  → raw MJPEG (also used by the HTML <img>)
//   GET /ping    → JSON {"t":<epoch_ms>} for RTT measurement
public class MjpegServer : MonoBehaviour
{
    private const string Tag      = "[MjpegServer]";
    private const string Boundary = "frame";

    [SerializeField] private FrameCapture _frameCapture;

    public int ConnectedClientCount
    {
        get { lock (_clientsLock) { int n = 0; foreach (var c in _clients) if (c.IsStreaming) n++; return n; } }
    }

    private TcpListener                _listener;
    private Thread                     _acceptThread;
    private volatile bool              _running;

    private readonly List<ClientContext>   _clients     = new List<ClientContext>();
    private readonly object                _clientsLock = new object();
    private byte[]                         _sharedFrame;
    private volatile int                   _frameVersion;
    private readonly object                _frameLock   = new object();
    [SerializeField] private ObjectSelectionManager _selectionManager;
    private readonly ConcurrentQueue<SelectionClick> _selectionClicks =
        new ConcurrentQueue<SelectionClick>();

    private struct SelectionClick
    {
        public float x;
        public float y;
        public bool clear;
    }

    private sealed class HttpRequestData
    {
        public string method;
        public string path;
        public string body;
    }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        _running  = true;
        _listener = new TcpListener(IPAddress.Any, NetworkConfig.StreamPort);
        _listener.Start();

        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MjpegAccept" };
        _acceptThread.Start();

        Debug.Log($"{Tag} Listening on :{NetworkConfig.StreamPort}");
    }

    private void Update()
    {
        if (_frameCapture != null && _frameCapture.HasNewFrame)
        {
            byte[] frame = _frameCapture.LatestFrame;
            _frameCapture.ConsumeFrame();
            if (frame != null && frame.Length > 0)
            {
                lock (_frameLock) _sharedFrame = frame;
                _frameVersion++;
            }
        }

        SelectionClick click;
        while (_selectionClicks.TryDequeue(out click))
        {
            if (_selectionManager == null)
                continue;

            if (click.clear)
                _selectionManager.ClearSelection();
            else
                _selectionManager.ToggleAtNormalizedPoint(click.x, click.y);
        }
    }

    private void OnDestroy()
    {
        _running = false;
        _listener?.Stop();
        _acceptThread?.Join(1000);
        lock (_clientsLock)
        {
            foreach (var c in _clients) try { c.Tcp.Close(); } catch { }
            _clients.Clear();
        }
    }

    // ── Accept loop ───────────────────────────────────────────────────────────

    private void AcceptLoop()
    {
        while (_running)
        {
            try
            {
                TcpClient tcp = _listener.AcceptTcpClient();
                var ctx = new ClientContext(tcp);
                lock (_clientsLock) _clients.Add(ctx);
                new Thread(() => ServeClient(ctx)) { IsBackground = true, Name = "MjpegClient" }.Start();
            }
            catch (SocketException) { break; }
            catch (Exception ex) { if (_running) Debug.LogWarning($"{Tag} Accept: {ex.Message}"); }
        }
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    private void ServeClient(ClientContext ctx)
    {
        try
        {
            NetworkStream ns = ctx.Tcp.GetStream();
            ctx.Tcp.ReceiveTimeout = 2000;
            HttpRequestData request = ReadHttpRequest(ns);
            ctx.Tcp.ReceiveTimeout = 0;

            if (request == null)
            {
                WriteResponse(ns, "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Bad request"), 400, "Bad Request");
                return;
            }

            if (request.method == "GET" && request.path == "/ping")
            {
                ServePing(ns);
                return;
            }
            if (request.method == "GET"
                && (request.path == "/" || request.path == "/index.html"))
            {
                ServeHtml(ns);
                return;
            }
            if (request.method == "POST" && request.path == "/selection")
            {
                float x, y;
                if (!TryParseClick(request.body, out x, out y))
                {
                    WriteResponse(ns, "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("Expected normalized x and y coordinates"),
                        400, "Bad Request");
                    return;
                }

                _selectionClicks.Enqueue(new SelectionClick { x = x, y = y });
                WriteResponse(ns, "application/json",
                    Encoding.UTF8.GetBytes("{\"queued\":true}"), 202, "Accepted");
                return;
            }
            if (request.method == "POST" && request.path == "/selection/clear")
            {
                _selectionClicks.Enqueue(new SelectionClick { clear = true });
                WriteResponse(ns, "application/json",
                    Encoding.UTF8.GetBytes("{\"queued\":true}"), 202, "Accepted");
                return;
            }
            if (request.method != "GET")
            {
                WriteResponse(ns, "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Not found"), 404, "Not Found");
                return;
            }

            ctx.IsStreaming = true;
            ServeMjpeg(ns, ctx);
        }
        catch (Exception ex) { if (_running) Debug.Log($"{Tag} Client disconnected: {ex.Message}"); }
        finally
        {
            ctx.Tcp.Close();
            lock (_clientsLock) _clients.Remove(ctx);
        }
    }

    private static HttpRequestData ReadHttpRequest(NetworkStream stream)
    {
        const int maxHeaderBytes = 8192;
        const int maxBodyBytes = 1024;
        var headerStream = new MemoryStream();
        bool complete = false;

        while (headerStream.Length < maxHeaderBytes)
        {
            int value = stream.ReadByte();
            if (value < 0)
                return null;
            headerStream.WriteByte((byte)value);

            long length = headerStream.Length;
            if (length >= 4)
            {
                byte[] buffer = headerStream.GetBuffer();
                int end = (int)length;
                complete = buffer[end - 4] == '\r' && buffer[end - 3] == '\n'
                    && buffer[end - 2] == '\r' && buffer[end - 1] == '\n';
                if (complete)
                    break;
            }
        }
        if (!complete)
            return null;

        byte[] headerBytes = headerStream.ToArray();
        string headerText = Encoding.ASCII.GetString(headerBytes, 0, headerBytes.Length - 4);
        string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        if (lines.Length == 0)
            return null;

        string[] requestLine = lines[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2)
            return null;

        int contentLength = 0;
        bool chunked = false;
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0)
                continue;

            string name = lines[i].Substring(0, colon).Trim();
            string value = lines[i].Substring(colon + 1).Trim();
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
                        out contentLength))
                    return null;
            }
            else if (string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                     && value.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                chunked = true;
            }
        }

        if (chunked || contentLength < 0 || contentLength > maxBodyBytes)
            return null;

        var bodyBytes = new byte[contentLength];
        int bodyRead = 0;
        while (bodyRead < bodyBytes.Length)
        {
            int read = stream.Read(bodyBytes, bodyRead, bodyBytes.Length - bodyRead);
            if (read <= 0)
                return null;
            bodyRead += read;
        }

        string path = requestLine[1];
        int query = path.IndexOf('?');
        if (query >= 0)
            path = path.Substring(0, query);

        return new HttpRequestData
        {
            method = requestLine[0].ToUpperInvariant(),
            path = path,
            body = Encoding.UTF8.GetString(bodyBytes)
        };
    }

    private static bool TryParseClick(string body, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        bool hasX = false;
        bool hasY = false;

        string[] values = body.Split('&');
        foreach (string value in values)
        {
            int equals = value.IndexOf('=');
            if (equals <= 0)
                continue;

            string key = Uri.UnescapeDataString(value.Substring(0, equals));
            string text = Uri.UnescapeDataString(value.Substring(equals + 1));
            float parsed;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                continue;

            if (key == "x") { x = parsed; hasX = true; }
            else if (key == "y") { y = parsed; hasY = true; }
        }

        return hasX && hasY
            && !float.IsNaN(x) && !float.IsInfinity(x) && x >= 0f && x <= 1f
            && !float.IsNaN(y) && !float.IsInfinity(y) && y >= 0f && y <= 1f;
    }

    // ── Route handlers ────────────────────────────────────────────────────────

    private static void ServePing(NetworkStream ns)
    {
        long ms   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        byte[] body = Encoding.UTF8.GetBytes($"{{\"t\":{ms}}}");
        WriteResponse(ns, "application/json", body);
    }

    private static void ServeHtml(NetworkStream ns) =>
        WriteResponse(ns, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(ViewerHtml));

    private void ServeMjpeg(NetworkStream ns, ClientContext ctx)
    {
        string respHeader =
            "HTTP/1.1 200 OK\r\n" +
            $"Content-Type: multipart/x-mixed-replace; boundary={Boundary}\r\n" +
            "Cache-Control: no-cache\r\n" +
            "Connection: close\r\n\r\n";
        ns.Write(Encoding.ASCII.GetBytes(respHeader));

        int lastVersion = -1;
        while (_running && ctx.Tcp.Connected)
        {
            int v = _frameVersion;
            if (v == lastVersion)
            {
                Thread.Sleep(4); // ~240 fps poll ceiling, no busy-spin
                continue;
            }
            lastVersion = v;

            byte[] jpeg;
            lock (_frameLock) jpeg = _sharedFrame;
            if (jpeg == null) continue;

            byte[] part = Encoding.ASCII.GetBytes(
                $"--{Boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n");
            ns.Write(part, 0, part.Length);
            ns.Write(jpeg, 0, jpeg.Length);
            ns.Write(new byte[] { (byte)'\r', (byte)'\n' }, 0, 2);
        }
    }

    private static void WriteResponse(NetworkStream ns, string contentType, byte[] body)
    {
        WriteResponse(ns, contentType, body, 200, "OK");
    }

    private static void WriteResponse(
        NetworkStream ns, string contentType, byte[] body, int statusCode, string statusText)
    {
        byte[] header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {statusText}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Connection: close\r\n\r\n");
        ns.Write(header, 0, header.Length);
        ns.Write(body,   0, body.Length);
    }

    // ── Embedded viewer HTML ──────────────────────────────────────────────────

    private const string ViewerHtml = @"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>SafeCapture Live</title>
<style>
*{box-sizing:border-box;margin:0;padding:0}
body{background:#111;color:#eee;font-family:monospace;display:flex;flex-direction:column;align-items:center;padding:16px;gap:10px;min-height:100vh}
h1{color:#4cf;font-size:22px;margin-top:6px}
img{width:100%;max-width:960px;border:2px solid #333;border-radius:4px;background:#000;min-height:200px;cursor:crosshair}
#stats{font-size:15px;color:#0f9;background:#1a1a1a;padding:10px 20px;border-radius:6px;width:100%;max-width:960px;text-align:center}
button{background:#333;color:#eee;border:1px solid #666;border-radius:4px;padding:8px 14px;cursor:pointer}
#selection{color:#fc6;min-height:1.2em}
</style>
</head>
<body>
<h1>SafeCapture Live</h1>
<img id=""s"" src=""/stream"" alt=""stream"">
<button id=""clear"">Clear selected objects</button>
<div id=""selection"">Click a detected box to toggle its privacy mask.</div>
<div id=""stats"">Measuring latency...</div>
<script>
(function(){
  var el=document.getElementById('stats');
  var selection=document.getElementById('selection');
  var image=document.getElementById('s');
  var buf=[];
  image.addEventListener('click',function(event){
    var rect=image.getBoundingClientRect();
    var x=(event.clientX-rect.left)/rect.width;
    var y=(event.clientY-rect.top)/rect.height;
    fetch('/selection',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},
      body:'x='+encodeURIComponent(x)+'&y='+encodeURIComponent(y)})
      .then(function(r){if(!r.ok)throw new Error('Selection request failed');selection.textContent='Selection request queued; a cyan box means selected.';})
      .catch(function(){selection.textContent='Could not update selection. Try clicking a detected box.';});
  });
  document.getElementById('clear').addEventListener('click',function(){
    fetch('/selection/clear',{method:'POST'})
      .then(function(r){if(!r.ok)throw new Error('Clear request failed');selection.textContent='Clear request queued.';})
      .catch(function(){selection.textContent='Could not clear selection.';});
  });
  function ping(){
    var t0=Date.now();
    fetch('/ping').then(function(r){return r.json();}).then(function(){
      var rtt=Date.now()-t0;
      buf.push(rtt);if(buf.length>6)buf.shift();
      var avg=Math.round(buf.reduce(function(a,b){return a+b;},0)/buf.length);
      el.textContent='Viewer RTT: '+avg+' ms  │  Est. one-way latency: ~'+Math.round(avg/2)+' ms';
    }).catch(function(){el.textContent='Reconnecting...';});
  }
  setInterval(ping,1000);ping();
})();
</script>
</body>
</html>";

    private class ClientContext
    {
        public readonly TcpClient Tcp;
        public bool IsStreaming;
        public ClientContext(TcpClient tcp) { Tcp = tcp; }
    }
}
