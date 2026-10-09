using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MyGame
{
    [Serializable]
    public class ConnectionPayload
    {
        public string name;
    }

    /// <summary>
    /// 在代码里创建 NetworkManager，负责建房、加入、离开和入房审批。
    /// 网络预制体由编辑器菜单「MyGame/生成网络预制体」生成到 Resources 里。
    /// </summary>
    public class NetworkBootstrap : MonoBehaviour
    {
        public static NetworkBootstrap Instance { get; private set; }

        public const string PrefabRoot = "MyGame/NetPrefabs/";

        public NetworkManager Manager { get; private set; }
        public GameObject SessionPrefab { get; private set; }
        public GameObject PlayerPrefab { get; private set; }
        public GameObject WorldItemPrefab { get; private set; }

        /// <summary>本机断开后的原因（被踢、房间已满、房主解散等），菜单页读出来提示。</summary>
        public string LastDisconnectReason { get; set; }

        /// <summary>建房时的房间设置，Session 生成后读取。</summary>
        public string PendingRoomName { get; private set; }
        public int PendingMaxPlayers { get; private set; }

        // 服务器端：审批时记下的名字，连接成功后交给 Session
        readonly Dictionary<ulong, string> _pendingNames = new Dictionary<ulong, string>();

        public bool IsOnline => Manager != null && (Manager.IsServer || Manager.IsClient);

        void Awake()
        {
            Instance = this;

            SessionPrefab = Resources.Load<GameObject>(PrefabRoot + "Session");
            PlayerPrefab = Resources.Load<GameObject>(PrefabRoot + "Player");
            WorldItemPrefab = Resources.Load<GameObject>(PrefabRoot + "WorldItem");
            if (SessionPrefab == null || PlayerPrefab == null || WorldItemPrefab == null)
            {
                Debug.LogError("[MyGame] 找不到网络预制体。请先在菜单栏点「MyGame/生成网络预制体」。");
                return;
            }

            // NetworkManager 必须是根物体，不能挂在别的物体下面
            var go = new GameObject("NetworkManager");
            DontDestroyOnLoad(go);
            var transport = go.AddComponent<UnityTransport>();
            Manager = go.AddComponent<NetworkManager>();
            Manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                ConnectionApproval = true,
                EnableSceneManagement = false,   // 单场景，局内地图在本地生成
                TickRate = 30,
            };
            Manager.AddNetworkPrefab(SessionPrefab);
            Manager.AddNetworkPrefab(PlayerPrefab);
            Manager.AddNetworkPrefab(WorldItemPrefab);

            Manager.ConnectionApprovalCallback = ApproveConnection;
            Manager.OnServerStarted += OnServerStarted;
            Manager.OnConnectionEvent += OnConnectionEvent;
            Manager.OnClientStopped += OnClientStopped;
        }

        // ---------------- 对外接口 ----------------

        public bool CreateRoom(string roomName, int maxPlayers, string playerName)
        {
            if (Manager == null || IsOnline) return false;
            PendingRoomName = string.IsNullOrWhiteSpace(roomName) ? $"{playerName}的房间" : roomName.Trim();
            PendingMaxPlayers = maxPlayers;
            LastDisconnectReason = null;

            var transport = (UnityTransport)Manager.NetworkConfig.NetworkTransport;
            transport.SetConnectionData("127.0.0.1", GameConfig.GamePort, "0.0.0.0");
            SetPayload(playerName);
            if (!Manager.StartHost())
            {
                LastDisconnectReason = $"建房失败，端口 {GameConfig.GamePort} 可能被占用（同一台电脑只能有一个房主）。";
                return false;
            }
            return true;
        }

        public bool JoinRoom(string address, int port, string playerName)
        {
            if (Manager == null || IsOnline) return false;
            LastDisconnectReason = null;
            var transport = (UnityTransport)Manager.NetworkConfig.NetworkTransport;
            transport.SetConnectionData(address, (ushort)port);
            SetPayload(playerName);
            return Manager.StartClient();
        }

        public void Leave()
        {
            if (Manager == null) return;
            LanDiscovery.Instance?.StopBroadcasting();
            if (Manager.IsListening) Manager.Shutdown();
        }

        public void Kick(ulong clientId)
        {
            if (Manager == null || !Manager.IsServer || clientId == NetworkManager.ServerClientId) return;
            Manager.DisconnectClient(clientId, "你已被房主移出房间。");
        }

        public string TakePendingName(ulong clientId)
        {
            if (_pendingNames.TryGetValue(clientId, out var n))
            {
                _pendingNames.Remove(clientId);
                return n;
            }
            return $"玩家{clientId}";
        }

        // ---------------- 内部 ----------------

        void SetPayload(string playerName)
        {
            var json = JsonUtility.ToJson(new ConnectionPayload { name = playerName });
            Manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(json);
        }

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;   // 局内角色在开局时才生成

            string name = null;
            try
            {
                var payload = JsonUtility.FromJson<ConnectionPayload>(Encoding.UTF8.GetString(request.Payload));
                name = payload?.name;
            }
            catch { }
            name = SanitizeName(name, request.ClientNetworkId);

            // 房主自己一定通过
            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                _pendingNames[request.ClientNetworkId] = name;
                response.Approved = true;
                return;
            }

            var session = Session.Instance;
            if (session != null && session.Phase.Value != GamePhase.Room)
            {
                response.Approved = false;
                response.Reason = "对局已经开始，无法加入。";
                return;
            }
            int max = session != null ? session.MaxPlayers.Value : PendingMaxPlayers;
            if (Manager.ConnectedClientsIds.Count >= max)
            {
                response.Approved = false;
                response.Reason = "房间已满。";
                return;
            }

            _pendingNames[request.ClientNetworkId] = name;
            response.Approved = true;
        }

        static string SanitizeName(string name, ulong clientId)
        {
            if (string.IsNullOrWhiteSpace(name)) return $"玩家{clientId}";
            name = name.Trim();
            // FixedString64Bytes 按 UTF-8 存，中文每字 3 字节，限制在 16 个字符以内
            if (name.Length > 16) name = name.Substring(0, 16);
            return name;
        }

        void OnServerStarted()
        {
            var go = Instantiate(SessionPrefab);
            go.GetComponent<NetworkObject>().Spawn();
        }

        void OnConnectionEvent(NetworkManager nm, ConnectionEventData data)
        {
            if (!nm.IsServer) return;
            if (data.EventType == ConnectionEvent.ClientConnected)
                Session.Instance?.ServerAddPlayer(data.ClientId, TakePendingName(data.ClientId));
            else if (data.EventType == ConnectionEvent.ClientDisconnected)
                Session.Instance?.ServerRemovePlayer(data.ClientId);
        }

        void OnClientStopped(bool wasHost)
        {
            LanDiscovery.Instance?.StopBroadcasting();
            if (string.IsNullOrEmpty(LastDisconnectReason))
            {
                var reason = Manager.DisconnectReason;
                if (!wasHost && !string.IsNullOrEmpty(reason))
                    LastDisconnectReason = reason.Contains("shutting down") ? "房主已解散房间。" : reason;
                else if (!wasHost && !_leavingOnPurpose)
                    LastDisconnectReason = "与房间断开连接。";
            }
            _leavingOnPurpose = false;
            GameWorld.Teardown();
        }

        bool _leavingOnPurpose;

        /// <summary>玩家自己点退出时调用，避免弹「断开连接」的提示。</summary>
        public void LeaveOnPurpose()
        {
            _leavingOnPurpose = true;
            LastDisconnectReason = null;
            Leave();
        }
    }
}
