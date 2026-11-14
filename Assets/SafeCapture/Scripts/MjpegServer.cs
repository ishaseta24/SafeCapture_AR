using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Runs an HTTP server on StreamPort. Any browser connecting to
// http://<quest-ip>:<port>/ receives a live MJPEG stream.
public class MjpegServer : MonoBehaviour
{
    private const string Tag = "[MjpegServer]";
    private const string Boundary = "frame";

    [SerializeField] private FrameCapture _frameCapture;

    public int ConnectedClientCount { get { lock (_clientsLock) return _clients.Count; } }

    private TcpListener _listener;
    private Thread _acceptThread;
    private volatile bool _running;

    private readonly List<ClientContext> _clients = new List<ClientContext>();
    private readonly object _clientsLock = new object();

    // Latest JPEG bytes shared to all client threads
    private byte[] _sharedFrame;
    private readonly object _frameLock = new object();
    private readonly ManualResetEventSlim _frameReady = new ManualResetEventSlim(false);

    private void Start()
    {
        _running = true;
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

        lock (_frameLock)
            _sharedFrame = frame;

        _frameReady.Set();
        _frameReady.Reset();
    }

    // ── Accept loop ──────────────────────────────────────────────────────────

    private void AcceptLoop()
    {
        while (_running)
        {
            try
            {
                TcpClient tcp = _listener.AcceptTcpClient();
                var ctx = new ClientContext(tcp);

                lock (_clientsLock) _clients.Add(ctx);

                var t = new Thread(() => ServeClient(ctx)) { IsBackground = true, Name = "MjpegClient" };
                t.Start();
            }
            catch (SocketException) { break; }
            catch (Exception ex) { if (_running) Debug.LogWarning($"{Tag} Accept error: {ex.Message}"); }
        }
    }

    // ── Per-client serve loop ─────────────────────────────────────────────────

    private void ServeClient(ClientContext ctx)
    {
        try
        {
            NetworkStream ns = ctx.Tcp.GetStream();

            // Drain the HTTP request (we don't need it)
            byte[] buf = new byte[4096];
            if (ctx.Tcp.ReceiveTimeout == 0) ctx.Tcp.ReceiveTimeout = 2000;
            try { ns.Read(buf, 0, buf.Length); } catch { /* timeout ok */ }
            ctx.Tcp.ReceiveTimeout = 0;

            // HTTP response header
            string header =
                "HTTP/1.1 200 OK\r\n" +
                $"Content-Type: multipart/x-mixed-replace; boundary={Boundary}\r\n" +
                "Cache-Control: no-cache\r\n" +
                "Connection: close\r\n" +
                "\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            ns.Write(headerBytes, 0, headerBytes.Length);

            while (_running && ctx.Tcp.Connected)
            {
                // Wait for the next frame (500 ms timeout to check _running)
                _frameReady.Wait(500);

                byte[] jpeg;
                lock (_frameLock)
                    jpeg = _sharedFrame;

                if (jpeg == null) continue;

                string partHeader =
                    $"--{Boundary}\r\n" +
                    "Content-Type: image/jpeg\r\n" +
                    $"Content-Length: {jpeg.Length}\r\n" +
                    "\r\n";
                byte[] partHeaderBytes = Encoding.ASCII.GetBytes(partHeader);

                ns.Write(partHeaderBytes, 0, partHeaderBytes.Length);
                ns.Write(jpeg, 0, jpeg.Length);
                ns.Write(Encoding.ASCII.GetBytes("\r\n"), 0, 2);
            }
        }
        catch (Exception ex)
        {
            if (_running) Debug.Log($"{Tag} Client disconnected: {ex.Message}");
        }
        finally
        {
            ctx.Tcp.Close();
            lock (_clientsLock) _clients.Remove(ctx);
        }
    }

    // ── Teardown ──────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        _running = false;
        _frameReady.Set(); // unblock waiting client threads

        _listener?.Stop();
        _acceptThread?.Join(1000);

        lock (_clientsLock)
        {
            foreach (var c in _clients) try { c.Tcp.Close(); } catch { }
            _clients.Clear();
        }
    }

    private class ClientContext
    {
        public readonly TcpClient Tcp;
        public ClientContext(TcpClient tcp) { Tcp = tcp; }
    }
}
