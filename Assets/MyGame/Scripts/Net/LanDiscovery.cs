using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace MyGame
{
    [Serializable]
    public class RoomAdvert
    {
        public string name;
        public string code;
        public int players;
        public int max;
        public int port;

        [NonSerialized] public string Address;
        [NonSerialized] public float LastSeen;
    }

    /// <summary>
    /// 局域网房间发现：房主定时广播房间信息，加入页面监听广播生成房间列表。
    /// 以后接 Steam 大厅时，把房间列表的数据来源换成 Steam Lobby 即可，界面不用动。
    /// </summary>
    public class LanDiscovery : MonoBehaviour
    {
        public static LanDiscovery Instance { get; private set; }

        readonly Dictionary<string, RoomAdvert> _rooms = new Dictionary<string, RoomAdvert>();
        readonly ConcurrentQueue<(string json, string address)> _inbox = new ConcurrentQueue<(string, string)>();

        UdpClient _listener;
        UdpClient _sender;
        float _nextBroadcast;
        Func<RoomAdvert> _advertSource;

        public IEnumerable<RoomAdvert> Rooms => _rooms.Values;
        public bool IsListening => _listener != null;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            StopListening();
            StopBroadcasting();
        }

        // ---------------- 房主：广播 ----------------

        public void StartBroadcasting(Func<RoomAdvert> source)
        {
            _advertSource = source;
            if (_sender == null)
            {
                _sender = new UdpClient();
                _sender.EnableBroadcast = true;
            }
        }

        public void StopBroadcasting()
        {
            _advertSource = null;
            _sender?.Close();
            _sender = null;
        }

        // ---------------- 加入方：监听 ----------------

        public void StartListening()
        {
            if (_listener != null) return;
            try
            {
                _listener = new UdpClient();
                _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Client.Bind(new IPEndPoint(IPAddress.Any, GameConfig.DiscoveryPort));
                _listener.BeginReceive(OnReceive, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LanDiscovery] 监听失败：{e.Message}");
                _listener = null;
            }
        }

        public void StopListening()
        {
            _listener?.Close();
            _listener = null;
            _rooms.Clear();
        }

        public void ClearRooms() => _rooms.Clear();

        void OnReceive(IAsyncResult ar)
        {
            var listener = _listener;
            if (listener == null) return;
            try
            {
                IPEndPoint remote = null;
                var data = listener.EndReceive(ar, ref remote);
                _inbox.Enqueue((Encoding.UTF8.GetString(data), remote.Address.ToString()));
                listener.BeginReceive(OnReceive, null);
            }
            catch (ObjectDisposedException) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[LanDiscovery] 接收失败：{e.Message}");
            }
        }

        void Update()
        {
            while (_inbox.TryDequeue(out var msg))
            {
                RoomAdvert ad;
                try { ad = JsonUtility.FromJson<RoomAdvert>(msg.json); }
                catch { continue; }
                if (ad == null || string.IsNullOrEmpty(ad.code)) continue;
                ad.Address = msg.address;
                ad.LastSeen = Time.unscaledTime;
                // 同一台电脑会同时收到局域网广播和本机回环，按房间代码去重，优先用本机地址
                if (_rooms.TryGetValue(ad.code, out var old) && old.Address == "127.0.0.1" && Time.unscaledTime - old.LastSeen < 1.5f)
                    ad.Address = old.Address;
                _rooms[ad.code] = ad;
            }

            if (_rooms.Count > 0)
            {
                List<string> stale = null;
                foreach (var kv in _rooms)
                    if (Time.unscaledTime - kv.Value.LastSeen > GameConfig.DiscoveryTimeout)
                        (stale ??= new List<string>()).Add(kv.Key);
                if (stale != null) foreach (var k in stale) _rooms.Remove(k);
            }

            if (_advertSource != null && _sender != null && Time.unscaledTime >= _nextBroadcast)
            {
                _nextBroadcast = Time.unscaledTime + GameConfig.DiscoveryInterval;
                var ad = _advertSource();
                if (ad != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(ad));
                    TrySend(bytes, new IPEndPoint(IPAddress.Broadcast, GameConfig.DiscoveryPort));
                    TrySend(bytes, new IPEndPoint(IPAddress.Loopback, GameConfig.DiscoveryPort));
                }
            }
        }

        void TrySend(byte[] bytes, IPEndPoint ep)
        {
            try { _sender.Send(bytes, bytes.Length, ep); }
            catch (Exception e) { Debug.LogWarning($"[LanDiscovery] 广播失败 {ep}：{e.Message}"); }
        }
    }
}
