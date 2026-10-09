using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MyGame
{
    /// <summary>
    /// 加入房间页面（基建v1）：搜索房间号 + 房间列表。
    /// 目前房间列表来自局域网广播；接入 Steam 后换成 Steam 大厅列表。
    /// 搜索框里输入 IP 地址（如 192.168.1.20）可以直接连接，方便跨网段测试。
    /// </summary>
    public class JoinScreen : UIScreen
    {
        readonly UIManager _ui;
        readonly TextField _search;
        readonly TextField _name;
        readonly ScrollView _list;
        readonly Label _empty;
        float _nextRefresh;

        public JoinScreen(VisualElement parent, UIManager ui) : base(parent, "join")
        {
            _ui = ui;

            var top = UI.Box(Root, "row", "join-top");
            UI.Btn(top, "返回", () => _ui.Page = UIManager.OfflinePage.Menu);
            UI.Text(top, "加入房间", "page-title");

            var body = UI.Box(Root, "row", "join-body");

            var left = UI.Box(body, "join-left");
            var model = UI.Box(left, "character-slot");
            UI.Text(model, "玩家个人形象", "placeholder-text");
            _name = UI.Input(left, "我的名字", MenuScreen.PlayerName, 16);

            var middle = UI.Box(body, "join-middle");
            var searchPanel = UI.Box(middle, "panel");
            UI.Text(searchPanel, "搜索房间号", "panel-title");
            _search = UI.Input(searchPanel, "", "", 32);
            UI.Btn(searchPanel, "搜索", Search, "btn-primary");
            UI.Text(searchPanel, "输入房主告诉你的 6 位房间代码；也可以直接输入房主的 IP 地址。", "hint");

            var right = UI.Box(body, "panel", "room-list-panel");
            UI.Text(right, "房间列表", "panel-title");
            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.AddToClassList("room-list");
            right.Add(_list);
            _empty = UI.Text(right, "正在搜索局域网内的房间……", "hint");
            var refresh = UI.Btn(right, "刷新", Refresh, "btn-small", "refresh-btn");
        }

        protected override void OnShow()
        {
            UIManager.SetCursorLocked(false);
            _name.value = MenuScreen.PlayerName;
            LanDiscovery.Instance?.StartListening();
            Refresh();
        }

        protected override void OnHide()
        {
            MenuScreen.PlayerName = _name.value;
            LanDiscovery.Instance?.StopListening();
        }

        void Refresh()
        {
            _nextRefresh = Time.unscaledTime + 1f;
            _list.Clear();
            var rooms = LanDiscovery.Instance?.Rooms.OrderBy(r => r.name).ToList();
            UI.Show(_empty, rooms == null || rooms.Count == 0);
            if (rooms == null) return;
            foreach (var r in rooms)
            {
                var room = r;
                var b = UI.Btn(_list, $"{room.name}    {room.players}/{room.max}    代码 {room.code}", () => Join(room.Address, room.port), "room-item");
                b.SetEnabled(room.players < room.max);
            }
        }

        public override void Tick()
        {
            if (Time.unscaledTime >= _nextRefresh) Refresh();
        }

        void Search()
        {
            var q = _search.value?.Trim() ?? "";
            if (q.Length == 0) return;
            if (q.Contains("."))
            {
                var parts = q.Split(':');
                int port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : GameConfig.GamePort;
                Join(parts[0], port);
                return;
            }
            var room = LanDiscovery.Instance?.Rooms.FirstOrDefault(r => string.Equals(r.code, q, System.StringComparison.OrdinalIgnoreCase));
            if (room == null)
            {
                _ui.ShowToast("没有找到这个房间。确认房间代码正确，并且和房主在同一个局域网。");
                return;
            }
            Join(room.Address, room.port);
        }

        void Join(string address, int port)
        {
            MenuScreen.PlayerName = _name.value;
            if (NetworkBootstrap.Instance == null || !NetworkBootstrap.Instance.JoinRoom(address, port, MenuScreen.PlayerName))
                _ui.ShowToast("连接失败。");
            else
                _ui.ShowToast("正在加入……");
        }
    }
}
