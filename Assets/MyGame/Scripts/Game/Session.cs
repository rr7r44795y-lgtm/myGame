using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MyGame
{
    /// <summary>
    /// 一个房间的全部共享状态：房间信息、玩家列表、阶段与倒计时、聊天、准备阶段选择、好感度。
    /// 规则全部在服务器（房主）上执行，客户端只读同步下来的数据、通过 Rpc 发请求。
    /// </summary>
    public class Session : NetworkBehaviour
    {
        public static Session Instance { get; private set; }

        public readonly NetworkVariable<FixedString64Bytes> RoomName = new NetworkVariable<FixedString64Bytes>();
        public readonly NetworkVariable<FixedString32Bytes> RoomCode = new NetworkVariable<FixedString32Bytes>();
        public readonly NetworkVariable<int> MaxPlayers = new NetworkVariable<int>(GameConfig.DefaultMaxPlayers);
        public readonly NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Room);
        public readonly NetworkVariable<double> PhaseEndTime = new NetworkVariable<double>();
        public NetworkList<PlayerEntry> Players;

        /// <summary>本机玩家自己的喜好（服务器单独发给本人）。</summary>
        public ItemPreference LocalPrefs { get; private set; }

        /// <summary>本机收到的聊天记录，最新的在最后。</summary>
        public readonly List<string> ChatLog = new List<string>();
        public event Action ChatChanged;

        /// <summary>给本机的提示信息（UI 订阅后弹 toast）。</summary>
        public static event Action<string> Toast;

        /// <summary>对局结束时的结果文本。</summary>
        public string LastResult { get; private set; }

        // ---------- 仅服务器 ----------
        readonly Dictionary<ulong, ItemPreference> _prefs = new Dictionary<ulong, ItemPreference>();
        readonly FavorSystem _favor = new FavorSystem();
        readonly List<NetworkObject> _spawned = new List<NetworkObject>();
        public FavorSystem Favor => _favor;

        void Awake()
        {
            Players = new NetworkList<PlayerEntry>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Phase.OnValueChanged += OnPhaseChanged;
            OnPhaseChanged(GamePhase.Room, Phase.Value);

            if (IsServer)
            {
                var boot = NetworkBootstrap.Instance;
                RoomName.Value = new FixedString64Bytes(Truncate(boot.PendingRoomName, 18));
                RoomCode.Value = new FixedString32Bytes(GenerateCode());
                MaxPlayers.Value = boot.PendingMaxPlayers > 0 ? boot.PendingMaxPlayers : GameConfig.DefaultMaxPlayers;

                // Session 生成前就已连上的客户端（通常是房主自己）补进列表
                foreach (var id in NetworkManager.ConnectedClientsIds)
                    ServerAddPlayer(id, boot.TakePendingName(id));

                LanDiscovery.Instance?.StartBroadcasting(BuildAdvert);
            }
        }

        public override void OnNetworkDespawn()
        {
            Phase.OnValueChanged -= OnPhaseChanged;
            if (Instance == this) Instance = null;
            GameWorld.Teardown();
        }

        void OnPhaseChanged(GamePhase prev, GamePhase next)
        {
            if (next == GamePhase.Prep || next == GamePhase.Playing) GameWorld.Build();
            if (next == GamePhase.Room)
            {
                GameWorld.Teardown();
                LocalPrefs = null;
            }
        }

        // ================= 查询 =================

        public double Now => NetworkManager.ServerTime.Time;
        public float Remaining => Mathf.Max(0f, (float)(PhaseEndTime.Value - Now));

        public bool TryGetPlayer(ulong clientId, out PlayerEntry entry)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId == clientId)
                {
                    entry = Players[i];
                    return true;
                }
            }
            entry = default;
            return false;
        }

        public string NameOf(ulong clientId) => TryGetPlayer(clientId, out var e) ? e.Name.ToString() : $"玩家{clientId}";
        public Team TeamOf(ulong clientId) => TryGetPlayer(clientId, out var e) ? e.Team : Team.None;

        public bool AllOthersReady()
        {
            for (int i = 0; i < Players.Count; i++)
                if (Players[i].ClientId != NetworkManager.ServerClientId && !Players[i].Ready) return false;
            return true;
        }

        // ================= 玩家进出（服务器） =================

        public void ServerAddPlayer(ulong clientId, string name)
        {
            if (!IsServer || TryGetPlayer(clientId, out _)) return;
            Players.Add(new PlayerEntry
            {
                ClientId = clientId,
                Name = new FixedString64Bytes(Truncate(name, 16)),
                Team = Team.None,
                Alive = true,
            });
            ServerBroadcastSystem($"{name} 加入了房间");
        }

        public void ServerRemovePlayer(ulong clientId)
        {
            if (!IsServer) return;
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId != clientId) continue;
                var name = Players[i].Name.ToString();
                Players.RemoveAt(i);
                ServerBroadcastSystem($"{name} 离开了房间");
                break;
            }
            _prefs.Remove(clientId);
            _favor.RemovePlayer(clientId);
        }

        void ServerUpdatePlayer(ulong clientId, Func<PlayerEntry, PlayerEntry> change)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId != clientId) continue;
                Players[i] = change(Players[i]);
                return;
            }
        }

        // ================= 房间操作 =================

        [Rpc(SendTo.Server)]
        public void SetReadyRpc(bool ready, RpcParams p = default)
        {
            if (Phase.Value != GamePhase.Room) return;
            ServerUpdatePlayer(p.Receive.SenderClientId, e => { e.Ready = ready; return e; });
        }

        [Rpc(SendTo.Server)]
        public void StartGameRpc(RpcParams p = default)
        {
            if (p.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            if (Phase.Value != GamePhase.Room) return;
            if (!AllOthersReady())
            {
                ServerToast(p.Receive.SenderClientId, "还有玩家没有准备。");
                return;
            }
            ServerBeginPrep();
        }

        [Rpc(SendTo.Server)]
        public void SendChatRpc(string text, RpcParams p = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (text.Length > 60) text = text.Substring(0, 60);
            ReceiveChatRpc($"{NameOf(p.Receive.SenderClientId)}：{text}");
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ReceiveChatRpc(string line)
        {
            ChatLog.Add(line);
            if (ChatLog.Count > 50) ChatLog.RemoveAt(0);
            ChatChanged?.Invoke();
        }

        void ServerBroadcastSystem(string text) => ReceiveChatRpc($"【系统】{text}");

        [Rpc(SendTo.SpecifiedInParams)]
        void ToastRpc(string text, RpcParams p)
        {
            Toast?.Invoke(text);
        }

        public void ServerToast(ulong clientId, string text)
        {
            if (!IsServer) return;
            ToastRpc(text, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        // ================= 准备阶段 =================

        void ServerBeginPrep()
        {
            _prefs.Clear();
            _favor.Clear();
            for (int i = 0; i < Players.Count; i++)
            {
                var e = Players[i];
                e.PrepConfirmed = false;
                e.Alive = true;
                Players[i] = e;
            }
            PhaseEndTime.Value = Now + GameConfig.PrepSeconds;
            Phase.Value = GamePhase.Prep;
            LanDiscovery.Instance?.StopBroadcasting();   // 策划案：房间游戏未开始时才能加入
        }

        [Rpc(SendTo.Server)]
        public void SubmitPrefsRpc(int like, int[] dislikes, RpcParams p = default)
        {
            if (Phase.Value != GamePhase.Prep) return;
            var id = p.Receive.SenderClientId;
            _prefs[id] = SanitizePrefs(like, dislikes);
            ServerUpdatePlayer(id, e => { e.PrepConfirmed = true; return e; });
        }

        static ItemPreference SanitizePrefs(int like, int[] dislikes)
        {
            var pref = new ItemPreference();
            pref.Like = ItemDatabase.Get(like) != null ? like : 0;
            pref.Dislikes = (dislikes ?? Array.Empty<int>())
                .Where(d => ItemDatabase.Get(d) != null && d != pref.Like)
                .Distinct()
                .Take(GameConfig.DislikeMax)
                .ToArray();
            return pref;
        }

        /// <summary>没选或没选够的，按策划案随机补齐：喜欢 1 个，厌恶至少 1 个，两类互斥。</summary>
        static void FillRandom(ItemPreference pref)
        {
            var ids = ItemDatabase.All.Select(d => d.Id).ToList();
            if (pref.Like == 0)
            {
                var pool = ids.Where(i => !pref.IsDisliked(i)).ToList();
                pref.Like = pool[UnityEngine.Random.Range(0, pool.Count)];
            }
            if (pref.Dislikes.Length < GameConfig.DislikeMin)
            {
                var pool = ids.Where(i => i != pref.Like && !pref.IsDisliked(i)).ToList();
                var list = pref.Dislikes.ToList();
                while (list.Count < GameConfig.DislikeMin && pool.Count > 0)
                {
                    int k = UnityEngine.Random.Range(0, pool.Count);
                    list.Add(pool[k]);
                    pool.RemoveAt(k);
                }
                pref.Dislikes = list.ToArray();
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void YourPrefsRpc(int like, int[] dislikes, RpcParams p)
        {
            LocalPrefs = new ItemPreference { Like = like, Dislikes = dislikes };
        }

        public ItemPreference ServerGetPrefs(ulong clientId) => _prefs.TryGetValue(clientId, out var v) ? v : null;

        // ================= 对局 =================

        void ServerBeginPlaying()
        {
            GameWorld.Build();

            // 补齐喜好并告诉本人
            foreach (var e in Players)
            {
                if (!_prefs.TryGetValue(e.ClientId, out var pref))
                    _prefs[e.ClientId] = pref = new ItemPreference();
                FillRandom(pref);
                YourPrefsRpc(pref.Like, pref.Dislikes, RpcTarget.Single(e.ClientId, RpcTargetUse.Temp));
            }

            // 策划案：未选阵营的随机加入，使双方人数相等。demo 房间里不能选阵营，所以这里全员分配。
            AssignTeams();

            // 生成角色
            int red = 0, blue = 0;
            for (int i = 0; i < Players.Count; i++)
            {
                var e = Players[i];
                var pos = GameWorld.GetSpawn(e.Team, e.Team == Team.Blue ? blue++ : red++);
                var go = Instantiate(NetworkBootstrap.Instance.PlayerPrefab, pos, Quaternion.Euler(0, e.Team == Team.Blue ? 180 : 0, 0));
                var no = go.GetComponent<NetworkObject>();
                no.SpawnAsPlayerObject(e.ClientId, true);
                _spawned.Add(no);
            }

            // 生成地图物资
            var points = GameWorld.ItemPoints.OrderBy(_ => UnityEngine.Random.value).Take(GameConfig.WorldItemCount);
            foreach (var pt in points)
            {
                var def = ItemDatabase.All[UnityEngine.Random.Range(0, ItemDatabase.All.Count)];
                ServerSpawnWorldItem(def.Id, UnityEngine.Random.Range(1, 3), pt);
            }

            PhaseEndTime.Value = Now + GameConfig.GameSeconds;
            Phase.Value = GamePhase.Playing;
        }

        void AssignTeams()
        {
            var ids = new List<ulong>();
            foreach (var e in Players) ids.Add(e.ClientId);
            int red = 0, blue = 0;
            foreach (var e in Players)
            {
                if (e.Team == Team.Red) red++;
                if (e.Team == Team.Blue) blue++;
            }
            foreach (var id in ids.OrderBy(_ => UnityEngine.Random.value))
            {
                if (TeamOf(id) != Team.None) continue;
                var t = red < blue ? Team.Red : blue < red ? Team.Blue : (UnityEngine.Random.value < 0.5f ? Team.Red : Team.Blue);
                if (t == Team.Red) red++; else blue++;
                ServerUpdatePlayer(id, e => { e.Team = t; return e; });
            }
        }

        public WorldItem ServerSpawnWorldItem(int itemId, int count, Vector3 pos)
        {
            var go = Instantiate(NetworkBootstrap.Instance.WorldItemPrefab, pos, Quaternion.identity);
            var item = go.GetComponent<WorldItem>();
            item.ServerInit(itemId, count);
            var no = go.GetComponent<NetworkObject>();
            no.Spawn(true);
            _spawned.Add(no);
            return item;
        }

        public void ServerMarkDead(ulong clientId)
        {
            ServerUpdatePlayer(clientId, e => { e.Alive = false; return e; });
            ServerBroadcastSystem($"{NameOf(clientId)} 倒下了，变成了灵魂");
        }

        void ServerEndGame()
        {
            int red = 0, blue = 0, redAlive = 0, blueAlive = 0;
            foreach (var e in Players)
            {
                if (e.Team == Team.Red) { red++; if (e.Alive) redAlive++; }
                if (e.Team == Team.Blue) { blue++; if (e.Alive) blueAlive++; }
            }
            // 胜负规则策划案还没定（见求婚玩法待补充），先只展示两边人数
            ShowResultRpc($"对局结束\n红方 {red} 人（存活 {redAlive}）  蓝方 {blue} 人（存活 {blueAlive}）\n胜负规则待定");
            PhaseEndTime.Value = Now + GameConfig.EndResultSeconds;
            Phase.Value = GamePhase.Ended;
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ShowResultRpc(string text) => LastResult = text;

        void ServerBackToRoom()
        {
            foreach (var no in _spawned)
                if (no != null && no.IsSpawned) no.Despawn(true);
            _spawned.Clear();
            _prefs.Clear();
            _favor.Clear();

            for (int i = 0; i < Players.Count; i++)
            {
                var e = Players[i];
                e.Ready = false;
                e.Team = Team.None;
                e.Alive = true;
                e.PrepConfirmed = false;
                Players[i] = e;
            }
            Phase.Value = GamePhase.Room;
            LanDiscovery.Instance?.StartBroadcasting(BuildAdvert);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            switch (Phase.Value)
            {
                case GamePhase.Prep:
                    bool allConfirmed = Players.Count > 0;
                    foreach (var e in Players) allConfirmed &= e.PrepConfirmed;
                    if (allConfirmed || Now >= PhaseEndTime.Value) ServerBeginPlaying();
                    break;
                case GamePhase.Playing:
                    if (Now >= PhaseEndTime.Value) ServerEndGame();
                    break;
                case GamePhase.Ended:
                    if (Now >= PhaseEndTime.Value) ServerBackToRoom();
                    break;
            }
        }

        // ================= 工具 =================

        RoomAdvert BuildAdvert()
        {
            if (!IsSpawned || Phase.Value != GamePhase.Room) return null;
            return new RoomAdvert
            {
                name = RoomName.Value.ToString(),
                code = RoomCode.Value.ToString(),
                players = Players.Count,
                max = MaxPlayers.Value,
                port = GameConfig.GamePort,
            };
        }

        static string GenerateCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var arr = new char[GameConfig.RoomCodeLength];
            for (int i = 0; i < arr.Length; i++) arr[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
            return new string(arr);
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > max ? s.Substring(0, max) : s;
        }
    }
}
