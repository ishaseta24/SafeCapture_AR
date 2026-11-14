using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class GPSReceiver : MonoBehaviour
{
    private const string Tag = "[GPSReceiver]";

    public GPSPacket LastPacket { get; private set; }
    public float LastPacketTime { get; private set; } = -999f;
    public int PacketCount { get; private set; }

    private UdpClient _udpClient;
    private Thread _receiveThread;
    private volatile bool _running;

    // Thread-safe packet queue (one slot — latest wins)
    private GPSPacket _pendingPacket;
    private readonly object _lock = new object();

    private void Start()
    {
        try
        {
            _udpClient = new UdpClient(NetworkConfig.GpsPort);
            _running = true;
            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            _receiveThread.Start();
            Debug.Log($"{Tag} Listening on UDP :{NetworkConfig.GpsPort} — waiting for companion phone.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"{Tag} Failed to start UDP listener: {ex.Message}");
        }
    }

    private void ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                var endpoint = new IPEndPoint(IPAddress.Any, NetworkConfig.GpsPort);
                byte[] data = _udpClient.Receive(ref endpoint);
                string json = Encoding.UTF8.GetString(data);
                var packet = JsonUtility.FromJson<GPSPacket>(json);
                lock (_lock) { _pendingPacket = packet; }
            }
            catch (SocketException)
            {
                // socket closed on shutdown — exit cleanly
                break;
            }
            catch (Exception ex)
            {
                if (_running)
                    Debug.LogWarning($"{Tag} Receive error: {ex.Message}");
            }
        }
    }

    private void Update()
    {
        GPSPacket packet;
        lock (_lock)
        {
            packet = _pendingPacket;
            _pendingPacket = null;
        }

        if (packet == null) return;

        PacketCount++;
        LastPacket = packet;
        LastPacketTime = Time.realtimeSinceStartup;
        Debug.Log($"{Tag} Packet #{PacketCount}: lat={packet.Latitude:F6}, lon={packet.Longitude:F6}, ts={packet.TimestampMs}, note=\"{packet.Note}\"");
    }

    private void OnDestroy()
    {
        _running = false;
        _udpClient?.Close();
        _receiveThread?.Join(500);
    }
}
