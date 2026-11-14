using System;
using System.Collections.Generic;
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
    private readonly object                _frameLock   = new object();
    private readonly ManualResetEventSlim  _frameReady  = new ManualResetEventSlim(false);

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
        if (_frameCapture == null || !_frameCapture.HasNewFrame) return;

        byte[] frame = _frameCapture.LatestFrame;
        _frameCapture.ConsumeFrame();
        if (frame == null || frame.Length == 0) return;

        lock (_frameLock) _sharedFrame = frame;

        _frameReady.Set();
        _frameReady.Reset();
    }

    private void OnDestroy()
    {
        _running = false;
        _frameReady.Set();
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

            byte[] buf = new byte[4096];
            ctx.Tcp.ReceiveTimeout = 2000;
            int read = 0;
            try { read = ns.Read(buf, 0, buf.Length); } catch { }
            ctx.Tcp.ReceiveTimeout = 0;

            string path = read > 0 ? ParsePath(Encoding.ASCII.GetString(buf, 0, read)) : "/stream";

            if (path == "/ping")        { ServePing(ns);   return; }
            if (path == "/" || path == "/index.html") { ServeHtml(ns); return; }

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

    private static string ParsePath(string request)
    {
        int s = request.IndexOf(' ');
        if (s < 0) return "/stream";
        int e = request.IndexOf(' ', s + 1);
        if (e < 0) return "/stream";
        string p = request.Substring(s + 1, e - s - 1);
        int q = p.IndexOf('?');
        return q >= 0 ? p.Substring(0, q) : p;
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
        byte[] respBytes = Encoding.ASCII.GetBytes(respHeader);
        ns.Write(respBytes, 0, respBytes.Length);

        while (_running && ctx.Tcp.Connected)
        {
            _frameReady.Wait(500);

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
        byte[] header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
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
img{width:100%;max-width:960px;border:2px solid #333;border-radius:4px;background:#000;min-height:200px}
#stats{font-size:15px;color:#0f9;background:#1a1a1a;padding:10px 20px;border-radius:6px;width:100%;max-width:960px;text-align:center}
</style>
</head>
<body>
<h1>SafeCapture Live</h1>
<img id=""s"" src=""/stream"" alt=""stream"">
<div id=""stats"">Measuring latency...</div>
<script>
(function(){
  var el=document.getElementById('stats');
  var buf=[];
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
