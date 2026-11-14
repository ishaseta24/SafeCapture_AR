using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class QuestDiscoveryBroadcaster : MonoBehaviour
{
    private const string Tag = "[QuestDiscoveryBroadcaster]";

    [Tooltip("Seconds before the pairing code auto-refreshes. 0 = never.")]
    [SerializeField] private float _codeLifespanSeconds = 0f;

    public string PairingCode { get; private set; }
    public string IP { get; private set; }
    public float TimeUntilRefresh => _codeLifespanSeconds > 0f
        ? Mathf.Max(0f, _codeExpireTime - Time.realtimeSinceStartup)
        : -1f;

    private UdpClient _udpClient;
    private byte[] _packetBytes;
    private float _nextBroadcastTime;
    private float _codeExpireTime;

    private void Awake()
    {
        IP = GetLocalIP();
        GenerateCode();
        Debug.Log($"{Tag} Pairing code: {PairingCode}");
    }

    private void Start()
    {
        try
        {
            _udpClient = new UdpClient();
            _udpClient.EnableBroadcast = true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"{Tag} UDP init failed: {ex.Message}");
        }
    }

    private void Update()
    {
        if (_codeLifespanSeconds > 0f && Time.realtimeSinceStartup >= _codeExpireTime)
            RefreshCode();

        if (Time.realtimeSinceStartup >= _nextBroadcastTime)
        {
            Broadcast();
            _nextBroadcastTime = Time.realtimeSinceStartup + 1f;
        }
    }

    public void RefreshCode()
    {
        GenerateCode();
        Debug.Log($"{Tag} Code refreshed: {PairingCode}");
    }

    public void RefreshIP()
    {
        IP = GetLocalIP();
        RebuildPacket();
        Debug.Log($"{Tag} IP refreshed: {IP}");
    }

    private void GenerateCode()
    {
        PairingCode = UnityEngine.Random.Range(1000, 9999).ToString();
        _codeExpireTime = Time.realtimeSinceStartup + _codeLifespanSeconds;
        RebuildPacket();
    }

    private void RebuildPacket()
    {
        var dp = new DiscoveryPacket { IP = IP, Code = PairingCode, GpsPort = NetworkConfig.GpsPort };
        _packetBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(dp));
    }

    private void Broadcast()
    {
        if (_udpClient == null || _packetBytes == null) return;
        try
        {
            var endpoint = new IPEndPoint(IPAddress.Broadcast, NetworkConfig.DiscoveryPort);
            _udpClient.Send(_packetBytes, _packetBytes.Length, endpoint);
            Debug.Log($"{Tag} Broadcasting — IP={IP}, Code={PairingCode}, Port={NetworkConfig.DiscoveryPort}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{Tag} Broadcast failed: {ex.Message}");
        }
    }

    private static string GetLocalIP()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                        return addr.Address.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{Tag} Could not determine IP: {ex.Message}");
        }
        return "127.0.0.1";
    }

    private void OnDestroy()
    {
        _udpClient?.Close();
    }
}
