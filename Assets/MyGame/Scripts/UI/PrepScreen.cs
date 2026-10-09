using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace MyGame
{
    /// <summary>
    /// 准备阶段（基建v2）：10 秒内选 1 个喜欢的物品、1～3 个厌恶的物品，两类互斥。
    /// 弹出时有蒙版，只能操作面板。全员确认后提前进入对局，没选的由服务器随机补齐。
    /// </summary>
    public class PrepScreen : UIScreen
    {
        readonly UIManager _ui;
        readonly Label _timer;
        readonly Label _team;
        readonly Label _status;
        readonly Button _confirm;
        readonly Dictionary<int, Button> _likeButtons = new Dictionary<int, Button>();
        readonly Dictionary<int, Button> _dislikeButtons = new Dictionary<int, Button>();
        readonly VisualElement _panel;

        int _like;
        readonly List<int> _dislikes = new List<int>();
        bool _submitted;

        public PrepScreen(VisualElement parent, UIManager ui) : base(parent, "prep")
        {
            _ui = ui;
            var mask = UI.Box(Root, "mask");
            _panel = UI.Box(mask, "dialog", "prep-panel");

            var head = UI.Box(_panel, "row", "prep-head");
            _team = UI.Text(head, "", "prep-team");
            _timer = UI.Text(head, "", "prep-timer");

            UI.Text(_panel, "你喜欢的物品（选 1 个）", "panel-title");
            var likeList = UI.Box(_panel, "row", "item-grid");
            UI.Text(_panel, $"你厌恶的物品（选 {GameConfig.DislikeMin}～{GameConfig.DislikeMax} 个）", "panel-title");
            var dislikeList = UI.Box(_panel, "row", "item-grid");

            foreach (var def in ItemDatabase.All)
            {
                int id = def.Id;
                _likeButtons[id] = ItemButton(likeList, def, () => ToggleLike(id));
                _dislikeButtons[id] = ItemButton(dislikeList, def, () => ToggleDislike(id));
            }

            _status = UI.Text(_panel, "", "hint");
            _confirm = UI.Btn(_panel, "确认", Submit, "btn-primary", "btn-big");
        }

        static Button ItemButton(VisualElement parent, ItemDef def, System.Action onClick)
        {
            var b = UI.Btn(parent, "", onClick, "item-choice");
            UI.Icon(b, def.Id, "item-choice-icon");
            UI.Text(b, def.Name, "item-choice-name");
            b.tooltip = def.Desc;
            return b;
        }

        protected override void OnShow()
        {
            UIManager.SetCursorLocked(false);
            _like = 0;
            _dislikes.Clear();
            _submitted = false;
            Refresh();
        }

        void ToggleLike(int id)
        {
            if (_submitted || _dislikes.Contains(id)) return;
            _like = _like == id ? 0 : id;
            Refresh();
        }

        void ToggleDislike(int id)
        {
            if (_submitted || _like == id) return;
            if (_dislikes.Contains(id)) _dislikes.Remove(id);
            else if (_dislikes.Count < GameConfig.DislikeMax) _dislikes.Add(id);
            Refresh();
        }

        void Refresh()
        {
            foreach (var kv in _likeButtons)
            {
                kv.Value.EnableInClassList("selected", _like == kv.Key);
                kv.Value.SetEnabled(!_submitted && !_dislikes.Contains(kv.Key));
            }
            foreach (var kv in _dislikeButtons)
            {
                kv.Value.EnableInClassList("selected", _dislikes.Contains(kv.Key));
                kv.Value.SetEnabled(!_submitted && _like != kv.Key);
            }
            _confirm.SetEnabled(!_submitted && _like != 0 && _dislikes.Count >= GameConfig.DislikeMin);
        }

        void Submit()
        {
            if (_submitted) return;
            _submitted = true;
            Session.Instance?.SubmitPrefsRpc(_like, _dislikes.ToArray());
            Refresh();
        }

        public override void Tick()
        {
            var s = Session.Instance;
            if (s == null) return;
            _timer.text = $"剩余 {Mathf.CeilToInt(s.Remaining)} 秒";
            var myTeam = s.TeamOf(NetworkManager.Singleton.LocalClientId);
            _team.text = $"当前阵营：{TeamUtil.Label(myTeam)}" + (myTeam == Team.None ? "（开局时随机分配，两边人数相等）" : "");

            int done = 0;
            foreach (var p in s.Players) if (p.PrepConfirmed) done++;
            _status.text = _submitted
                ? $"已确认，等待其他玩家（{done}/{s.Players.Count}）"
                : "时间到了还没选的，会随机分配。";
        }
    }
}
