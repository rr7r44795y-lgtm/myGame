using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MyGame
{
    /// <summary>游戏房间（基建v1）：房主页面与成员页面、玩家列表、聊天框。</summary>
    public class RoomScreen : UIScreen
    {
        readonly UIManager _ui;
        readonly Button _start;
        readonly Button _ready;
        readonly Label _roomInfo;
        readonly ScrollView _players;
        readonly Label _count;
        readonly VisualElement _chatLog;
        readonly TextField _chatInput;
        bool _chatting;
        int _chatSeen = -1;
        string _playersSig;

        bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        public RoomScreen(VisualElement parent, UIManager ui) : base(parent, "room")
        {
            _ui = ui;

            // 左上：退出、开始 / 准备
            var topLeft = UI.Box(Root, "row", "room-top-left");
            UI.Btn(topLeft, "退出房间", AskLeave);
            _start = UI.Btn(topLeft, "开始游戏", () => Session.Instance?.StartGameRpc(), "btn-primary");
            _ready = UI.Btn(topLeft, "准备", ToggleReady, "btn-primary");

            _roomInfo = UI.Text(Root, "", "room-info");

            // 右上：设置、玩家头像；下方地图、局内设置
            var topRight = UI.Box(Root, "room-top-right");
            var row = UI.Box(topRight, "row");
            UI.Btn(row, "设置", ShowRoomSettings);
            UI.Btn(row, "头像", () => _ui.NotImplemented("更改装扮"), "avatar-btn");
            UI.Btn(topRight, "地图", ShowMap, "wide-btn");
            UI.Btn(topRight, "局内设置", () => _ui.NotImplemented("局内设置（基建v2 未定义内容）"), "wide-btn");

            // 左侧玩家列表
            var list = UI.Box(Root, "panel", "player-list");
            var header = UI.Box(list, "row", "player-row", "player-header");
            UI.Text(header, "玩家", "col-name");
            UI.Text(header, "状态", "col-ready");
            UI.Text(header, "阵营", "col-team");
            UI.Text(header, "麦克风", "col-mic");
            UI.Text(header, "", "col-kick");
            _players = new ScrollView(ScrollViewMode.Vertical);
            _players.AddToClassList("player-scroll");
            list.Add(_players);
            _count = UI.Text(list, "", "player-count");

            // 左下聊天框
            var chat = UI.Box(Root, "chat");
            _chatLog = UI.Box(chat, "chat-log");
            _chatInput = UI.Input(chat, "", "", 60, "chat-input");
            _chatInput.RegisterCallback<KeyDownEvent>(OnChatKey, TrickleDown.TrickleDown);
            SetChatting(false);
        }

        protected override void OnShow()
        {
            UIManager.SetCursorLocked(false);
            _playersSig = null;
            _chatSeen = -1;
        }

        protected override void OnHide() => SetChatting(false);

        public override void Tick()
        {
            var s = Session.Instance;
            if (s == null) return;

            _roomInfo.text = $"{s.RoomName.Value}    房间代码：{s.RoomCode.Value}";
            UI.Show(_start, IsHost);
            UI.Show(_ready, !IsHost);
            _start.SetEnabled(s.AllOthersReady());   // 策划案：除房主外所有人准备后才能开始
            if (!IsHost && s.TryGetPlayer(NetworkManager.Singleton.LocalClientId, out var me))
                _ready.text = me.Ready ? "已准备" : "未准备";

            RebuildPlayers(s);
            RefreshChat(s);

            var kb = Keyboard.current;
            if (kb != null && !_chatting && !_ui.DialogOpen && kb.spaceKey.wasPressedThisFrame) SetChatting(true);
        }

        void RebuildPlayers(Session s)
        {
            // 列表变化时才重建
            var sig = "";
            foreach (var p in s.Players) sig += $"{p.ClientId}{p.Name}{p.Ready}{p.Team}{p.MicOn}|";
            if (sig == _playersSig) return;
            _playersSig = sig;

            _players.Clear();
            foreach (var p in s.Players)
            {
                var entry = p;
                var row = UI.Box(_players, "row", "player-row");
                bool isHostRow = entry.ClientId == NetworkManager.ServerClientId;
                UI.Text(row, entry.Name.ToString() + (isHostRow ? "（房主）" : ""), "col-name");
                UI.Text(row, isHostRow ? "—" : (entry.Ready ? "已准备" : "未准备"), "col-ready", entry.Ready ? "ok-text" : "dim-text");
                var team = UI.Btn(row, TeamUtil.Label(entry.Team), () => _ui.NotImplemented("切换阵营"), "col-team", "chip");
                team.style.color = TeamUtil.Color(entry.Team);
                UI.Text(row, entry.MicOn ? "开麦" : "未接入", "col-mic", "dim-text");
                var kickCell = UI.Box(row, "col-kick");
                if (IsHost && !isHostRow)
                    UI.Btn(kickCell, "踢出", () => _ui.Confirm($"是否要将 {entry.Name} 踢出？", () => NetworkBootstrap.Instance.Kick(entry.ClientId)), "btn-small", "btn-danger");
            }
            _count.text = $"{s.Players.Count}/{s.MaxPlayers.Value}";
        }

        void RefreshChat(Session s)
        {
            if (_chatSeen == s.ChatLog.Count) return;
            _chatSeen = s.ChatLog.Count;
            _chatLog.Clear();
            // 不允许手动滑动，只显示最新的几条，新消息顶掉最上面那条
            int from = Mathf.Max(0, s.ChatLog.Count - 8);
            for (int i = from; i < s.ChatLog.Count; i++) UI.Text(_chatLog, s.ChatLog[i], "chat-line");
        }

        void SetChatting(bool on)
        {
            _chatting = on;
            _chatInput.SetEnabled(on);
            UI.Show(_chatLog, on);   // 策划案：平时只看到输入框，按空格唤起后才能看到聊天记录
            _chatInput.EnableInClassList("chat-input-active", on);
            _ui.Block("chat", on);
            if (on)
            {
                _chatInput.value = "";
                _chatInput.schedule.Execute(() => _chatInput.Focus());
            }
            else
            {
                _chatInput.Blur();
            }
        }

        void OnChatKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                var text = _chatInput.value;
                if (!string.IsNullOrWhiteSpace(text)) Session.Instance?.SendChatRpc(text);
                SetChatting(false);
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                SetChatting(false);
                e.StopPropagation();
            }
        }

        void ToggleReady()
        {
            var s = Session.Instance;
            if (s == null || !s.TryGetPlayer(NetworkManager.Singleton.LocalClientId, out var me)) return;
            s.SetReadyRpc(!me.Ready);
        }

        void AskLeave()
        {
            if (IsHost)
                _ui.Confirm("作为房主退出房间，房间即可解散，您要退出吗？", () => NetworkBootstrap.Instance.LeaveOnPurpose());
            else
                _ui.Confirm("确定要退出房间吗？", () => NetworkBootstrap.Instance.LeaveOnPurpose());
        }

        void ShowRoomSettings()
        {
            var s = Session.Instance;
            if (s == null) return;
            var (body, close) = _ui.Dialog("房间设置");
            UI.Text(body, $"房间名：{s.RoomName.Value}");
            UI.Text(body, $"人数：{s.MaxPlayers.Value} 人（demo 仅做 10 人）");
            UI.Text(body, $"房间代码：{s.RoomCode.Value}（接入 Steam 后用于邀请好友）");
            if (!IsHost) UI.Text(body, "只有房主可以修改房间设置。", "hint");
            var row = UI.Box(body, "row", "dialog-buttons");
            UI.Btn(row, "关闭", close);
        }

        void ShowMap()
        {
            var (body, close) = _ui.Dialog("地图");
            UI.Text(body, "当前地图：灰盒测试地图");
            UI.Text(body, "demo 阶段不做地图修改，只有这一张地图。", "hint");
            var row = UI.Box(body, "row", "dialog-buttons");
            UI.Btn(row, "关闭", close);
        }
    }
}
