using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MyGame
{
    /// <summary>
    /// 局内界面（基建v2 局内 ui）：左上地图与对局列表、上方倒计时、右上设置与个人信息（C）、
    /// 左下快捷栏（1/2/3）与背包按钮（B），以及准星提示、长按进度、头顶名字、结算。
    /// </summary>
    public class HudScreen : UIScreen
    {
        readonly UIManager _ui;

        // 顶部
        readonly Label _timer;
        readonly Image _minimap;
        readonly VisualElement _minimapMarker;
        readonly ScrollView _matchList;
        readonly VisualElement _matchPanel;

        // 中间
        readonly Label _prompt;
        readonly VisualElement _holdBar;
        readonly VisualElement _holdFill;
        readonly Label _ghostBanner;
        readonly Label _hint;
        readonly VisualElement _clickCatcher;
        readonly VisualElement _nameLayer;
        readonly Dictionary<PlayerAvatar, Label> _nameplates = new Dictionary<PlayerAvatar, Label>();

        // 左下
        readonly VisualElement _quickBar;
        readonly List<VisualElement> _quickSlots = new List<VisualElement>();
        readonly VisualElement _hungerFill;
        readonly VisualElement _thirstFill;
        readonly Label _hungerText;
        readonly Label _thirstText;

        // 面板
        readonly VisualElement _bagPanel;
        readonly List<VisualElement> _bagSlots = new List<VisualElement>();
        readonly VisualElement _infoPanel;
        readonly Label _infoText;
        readonly Label _tooltip;
        readonly VisualElement _dragIcon;
        readonly Label _result;

        bool _escFree;
        bool _cursorState;

        // 拖拽
        int _dragSlot = -1;
        Vector2 _dragStart;
        bool _dragging;

        // 小地图相机
        Camera _mapCam;
        RenderTexture _mapRt;

        public HudScreen(VisualElement parent, UIManager ui) : base(parent, "hud")
        {
            _ui = ui;
            Root.pickingMode = PickingMode.Ignore;

            // 点击画面空白处重新锁定鼠标
            _clickCatcher = UI.Box(Root, "click-catcher");
            _clickCatcher.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) _escFree = false; });

            _nameLayer = UI.Box(Root, "layer");
            _nameLayer.pickingMode = PickingMode.Ignore;

            // ---------- 左上：地图 + 对局 ----------
            var topLeft = UI.Box(Root, "row", "hud-top-left");
            var mapFrame = UI.Box(topLeft, "minimap");
            _minimap = new Image { scaleMode = ScaleMode.ScaleToFit };
            _minimap.AddToClassList("minimap-image");
            mapFrame.Add(_minimap);
            _minimapMarker = UI.Box(mapFrame, "minimap-marker");
            var matchCol = UI.Box(topLeft, "match-col");
            UI.Btn(matchCol, "对局", ToggleMatch);
            _matchPanel = UI.Box(matchCol, "panel", "match-panel");
            _matchList = new ScrollView(ScrollViewMode.Vertical);
            _matchList.AddToClassList("match-list");
            _matchPanel.Add(_matchList);
            UI.Show(_matchPanel, false);

            // ---------- 上方中央：倒计时 ----------
            _timer = UI.Text(Root, "", "hud-timer");

            // ---------- 右上：设置、个人信息 ----------
            var topRight = UI.Box(Root, "row", "hud-top-right");
            UI.Btn(topRight, "设置", () => _ui.NotImplemented("游戏设置"));
            UI.Btn(topRight, "个人信息 (C)", ToggleInfo);

            // ---------- 中间 ----------
            UI.Box(Root, "crosshair").pickingMode = PickingMode.Ignore;
            _prompt = UI.Text(Root, "", "prompt");
            _holdBar = UI.Box(Root, "hold-bar");
            _holdFill = UI.Box(_holdBar, "hold-fill");
            _ghostBanner = UI.Text(Root, "你已经变成灵魂，只能四处游荡", "ghost-banner");
            _hint = UI.Text(Root, "按 Esc 释放鼠标，点击画面空白处继续", "hud-hint");

            // ---------- 左下：状态、背包按钮、快捷栏 ----------
            var bottomLeft = UI.Box(Root, "hud-bottom-left");
            var stats = UI.Box(bottomLeft, "stats");
            (_hungerFill, _hungerText) = StatBar(stats, "饱腹", new Color(0.95f, 0.65f, 0.25f));
            (_thirstFill, _thirstText) = StatBar(stats, "水分", new Color(0.35f, 0.7f, 1f));
            UI.Btn(bottomLeft, "背包 (B)", ToggleBag, "bag-btn");
            _quickBar = UI.Box(bottomLeft, "row", "quickbar");
            for (int i = 0; i < GameConfig.QuickSlots; i++) _quickSlots.Add(QuickSlot(_quickBar, i));

            // ---------- 背包面板 ----------
            _bagPanel = UI.Box(Root, "panel", "bag-panel");
            var bagHead = UI.Box(_bagPanel, "row", "bag-head");
            UI.Text(bagHead, "背包", "panel-title");
            UI.Btn(bagHead, "×", () => UI.Show(_bagPanel, false), "close-btn");
            var grid = UI.Box(_bagPanel, "row", "bag-grid");
            for (int i = 0; i < GameConfig.BagSlots; i++) _bagSlots.Add(BagSlot(grid, i));
            UI.Text(_bagPanel, "点击物品：整格放进快捷栏。把快捷栏物品拖到这里：放回背包。拖到其他地方：丢在地上。", "hint");
            UI.Show(_bagPanel, false);

            // ---------- 个人信息面板 ----------
            _infoPanel = UI.Box(Root, "panel", "info-panel");
            var infoHead = UI.Box(_infoPanel, "row", "bag-head");
            UI.Text(infoHead, "个人信息", "panel-title");
            UI.Btn(infoHead, "×", () => UI.Show(_infoPanel, false), "close-btn");
            _infoText = UI.Text(_infoPanel, "", "info-text");
            UI.Show(_infoPanel, false);

            _tooltip = UI.Text(Root, "", "tooltip");
            _tooltip.pickingMode = PickingMode.Ignore;
            UI.Show(_tooltip, false);
            _dragIcon = UI.Box(Root, "item-icon", "drag-icon");
            _dragIcon.pickingMode = PickingMode.Ignore;
            UI.Show(_dragIcon, false);

            _result = UI.Text(Root, "", "result");
            UI.Show(_result, false);
        }

        static (VisualElement fill, Label text) StatBar(VisualElement parent, string name, Color color)
        {
            var row = UI.Box(parent, "row", "stat-row");
            UI.Text(row, name, "stat-name");
            var bar = UI.Box(row, "stat-bar");
            var fill = UI.Box(bar, "stat-fill");
            fill.style.backgroundColor = color;
            var text = UI.Text(row, "", "stat-value");
            return (fill, text);
        }

        VisualElement QuickSlot(VisualElement parent, int index)
        {
            var slot = UI.Box(parent, "slot", "quick-slot");
            UI.Icon(slot, 0, "slot-icon");
            UI.Text(slot, "", "slot-count");
            UI.Text(slot, (index + 1).ToString(), "slot-key");

            slot.RegisterCallback<PointerEnterEvent>(e => ShowTooltip(QuickStack(index), e.position));
            slot.RegisterCallback<PointerLeaveEvent>(_ => UI.Show(_tooltip, false));
            slot.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                _dragSlot = index;
                _dragStart = e.position;
                _dragging = false;
                slot.CapturePointer(e.pointerId);
            });
            slot.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_dragSlot != index) return;
                var stack = QuickStack(index);
                if (!_dragging && !stack.IsEmpty && (((Vector2)e.position) - _dragStart).magnitude > 8f)
                {
                    _dragging = true;
                    UI.SetIcon(_dragIcon, stack.ItemId);
                    UI.Show(_dragIcon, true);
                }
                if (_dragging)
                {
                    var local = Root.WorldToLocal(e.position);
                    _dragIcon.style.left = local.x - 30;
                    _dragIcon.style.top = local.y - 30;
                }
            });
            slot.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_dragSlot != index) return;
                slot.ReleasePointer(e.pointerId);
                var inv = PlayerAvatar.Local?.Inventory;
                if (_dragging && inv != null)
                {
                    if (UI.IsShown(_bagPanel) && _bagPanel.worldBound.Contains(e.position))
                        inv.ReturnQuickToBagRpc(index);        // 拖至背包：放回背包
                    else if (!_quickBar.worldBound.Contains(e.position))
                        inv.DropQuickRpc(index);               // 拖离物品栏：丢弃
                }
                else
                {
                    SelectQuick(index);
                }
                _dragSlot = -1;
                _dragging = false;
                UI.Show(_dragIcon, false);
            });
            return slot;
        }

        VisualElement BagSlot(VisualElement parent, int index)
        {
            var slot = UI.Box(parent, "slot", "bag-slot");
            UI.Icon(slot, 0, "slot-icon");
            UI.Text(slot, "", "slot-count");
            UI.Text(slot, "", "bag-slot-name");
            UI.Text(slot, "", "bag-slot-desc");
            slot.RegisterCallback<ClickEvent>(_ => PlayerAvatar.Local?.Inventory.MoveBagToQuickRpc(index));
            return slot;
        }

        static ItemStack QuickStack(int i)
        {
            var q = PlayerAvatar.Local?.Inventory?.Quick;
            return q != null && i < q.Count ? q[i] : ItemStack.Empty;
        }

        static ItemStack BagStack(int i)
        {
            var b = PlayerAvatar.Local?.Inventory?.Bag;
            return b != null && i < b.Count ? b[i] : ItemStack.Empty;
        }

        void ShowTooltip(ItemStack stack, Vector2 pos)
        {
            var def = stack.IsEmpty ? null : ItemDatabase.Get(stack.ItemId);
            if (def == null) { UI.Show(_tooltip, false); return; }
            _tooltip.text = $"{def.Name}\n{def.Desc}";
            var local = Root.WorldToLocal(pos);
            _tooltip.style.left = local.x + 12;
            _tooltip.style.top = local.y - 90;
            UI.Show(_tooltip, true);
        }

        static void SelectQuick(int index)
        {
            PlayerInteraction.Local?.Select(index);
        }

        void ToggleBag() => UI.Show(_bagPanel, !UI.IsShown(_bagPanel));
        void ToggleInfo() => UI.Show(_infoPanel, !UI.IsShown(_infoPanel));
        void ToggleMatch() => UI.Show(_matchPanel, !UI.IsShown(_matchPanel));

        protected override void OnShow()
        {
            _escFree = false;
            _cursorState = !UIManager.CursorLocked;   // 强制下一帧刷新一次
            UI.Show(_bagPanel, false);
            UI.Show(_infoPanel, false);
            UI.Show(_matchPanel, false);
            EnsureMinimapCamera();
        }

        protected override void OnHide()
        {
            UIManager.SetCursorLocked(false);
            UI.Show(_result, false);
            foreach (var l in _nameplates.Values) l.RemoveFromHierarchy();
            _nameplates.Clear();
            if (_mapCam != null) Object.Destroy(_mapCam.gameObject);
            if (_mapRt != null) _mapRt.Release();
            _mapCam = null;
            _mapRt = null;
        }

        public override void Tick()
        {
            var s = Session.Instance;
            var me = PlayerAvatar.Local;
            if (s == null) return;

            HandleKeys();
            UpdateCursor();

            var myTeam = s.TeamOf(NetworkManager.Singleton.LocalClientId);
            _timer.text = $"{UI.FormatTime(s.Remaining)}    {TeamUtil.Label(myTeam)}";
            _timer.style.color = TeamUtil.Color(myTeam);

            bool ended = s.Phase.Value == GamePhase.Ended;
            UI.Show(_result, ended);
            if (ended) _result.text = (s.LastResult ?? "对局结束") + $"\n{Mathf.CeilToInt(s.Remaining)} 秒后回到房间";

            if (me != null)
            {
                UpdateStats(me);
                UpdateSlots();
                UpdateInfo(s, me);
                UpdateMinimap(me);
            }
            UpdateCenter(me);
            UpdateMatchList(s);
            UpdateNameplates();
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || _ui.DialogOpen) return;
            if (kb.bKey.wasPressedThisFrame) ToggleBag();
            if (kb.cKey.wasPressedThisFrame) ToggleInfo();
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (UI.IsShown(_bagPanel) || UI.IsShown(_infoPanel) || UI.IsShown(_matchPanel))
                {
                    UI.Show(_bagPanel, false);
                    UI.Show(_infoPanel, false);
                    UI.Show(_matchPanel, false);
                }
                else _escFree = !_escFree;
            }
        }

        void UpdateCursor()
        {
            bool panelOpen = UI.IsShown(_bagPanel) || UI.IsShown(_infoPanel) || UI.IsShown(_matchPanel) || _ui.DialogOpen;
            bool wantLocked = !panelOpen && !_escFree && Session.Instance.Phase.Value == GamePhase.Playing;
            if (wantLocked != _cursorState)
            {
                _cursorState = wantLocked;
                UIManager.SetCursorLocked(wantLocked);
            }
            UI.Show(_clickCatcher, !wantLocked && !panelOpen);
            UI.Show(_hint, _escFree);
        }

        void UpdateStats(PlayerAvatar me)
        {
            int h = me.Stats.Hunger.Value, t = me.Stats.Thirst.Value;
            _hungerFill.style.width = Length.Percent(h / GameConfig.StatMax * 100f);
            _thirstFill.style.width = Length.Percent(t / GameConfig.StatMax * 100f);
            _hungerText.text = h.ToString();
            _thirstText.text = t.ToString();
            UI.Show(_ghostBanner, me.Stats.IsGhost.Value);
        }

        void UpdateSlots()
        {
            int selected = PlayerInteraction.Local != null ? PlayerInteraction.Local.SelectedQuick : 0;
            for (int i = 0; i < _quickSlots.Count; i++)
            {
                var st = QuickStack(i);
                var slot = _quickSlots[i];
                UI.SetIcon(slot.Q(className: "slot-icon"), st.IsEmpty ? 0 : st.ItemId);
                slot.Q<Label>(className: "slot-count").text = st.IsEmpty ? "" : st.Count.ToString();
                slot.EnableInClassList("selected", i == selected);
            }
            if (!UI.IsShown(_bagPanel)) return;
            for (int i = 0; i < _bagSlots.Count; i++)
            {
                var st = BagStack(i);
                var def = st.IsEmpty ? null : ItemDatabase.Get(st.ItemId);
                var slot = _bagSlots[i];
                UI.SetIcon(slot.Q(className: "slot-icon"), def != null ? def.Id : 0);
                slot.Q<Label>(className: "slot-count").text = st.IsEmpty ? "" : st.Count.ToString();
                slot.Q<Label>(className: "bag-slot-name").text = def?.Name ?? "";
                slot.Q<Label>(className: "bag-slot-desc").text = def?.Desc ?? "";
            }
        }

        void UpdateInfo(Session s, PlayerAvatar me)
        {
            if (!UI.IsShown(_infoPanel)) return;
            var id = NetworkManager.Singleton.LocalClientId;
            var prefs = s.LocalPrefs;
            string like = prefs != null ? ItemDatabase.Get(prefs.Like)?.Name ?? "—" : "—";
            string dislikes = "—";
            if (prefs != null && prefs.Dislikes.Length > 0)
            {
                var names = new List<string>();
                foreach (var d in prefs.Dislikes) names.Add(ItemDatabase.Get(d)?.Name ?? "?");
                dislikes = string.Join("、", names);
            }
            _infoText.text =
                $"名字：{s.NameOf(id)}\n" +
                $"阵营：{TeamUtil.Label(s.TeamOf(id))}\n" +
                $"状态：{(me.Stats.IsGhost.Value ? "灵魂" : "存活")}\n" +
                $"饱腹：{me.Stats.Hunger.Value}    水分：{me.Stats.Thirst.Value}\n" +
                $"喜欢的物品：{like}\n" +
                $"厌恶的物品：{dislikes}";
        }

        void UpdateCenter(PlayerAvatar me)
        {
            var pi = PlayerInteraction.Local;
            var prompt = pi != null ? pi.Prompt : null;
            _prompt.text = prompt ?? "";
            UI.Show(_prompt, !string.IsNullOrEmpty(prompt) && UIManager.CursorLocked);
            float hold = pi != null ? pi.HoldProgress : 0f;
            UI.Show(_holdBar, hold > 0f);
            _holdFill.style.width = Length.Percent(hold * 100f);
        }

        string _matchSig;

        void UpdateMatchList(Session s)
        {
            if (!UI.IsShown(_matchPanel)) return;
            var sig = "";
            foreach (var p in s.Players) sig += $"{p.ClientId}{p.Team}{p.Alive}|";
            if (sig == _matchSig) return;
            _matchSig = sig;
            _matchList.Clear();
            foreach (var p in s.Players)
            {
                var row = UI.Box(_matchList, "row", "match-row");
                UI.Text(row, p.Name.ToString() + (p.Alive ? "" : "（灵魂）"), "col-name");
                var t = UI.Text(row, TeamUtil.Label(p.Team), "col-team");
                t.style.color = TeamUtil.Color(p.Team);
            }
        }

        void UpdateNameplates()
        {
            var cam = Camera.main;
            var panel = Root.panel;
            if (cam == null || panel == null) return;

            var alive = new HashSet<PlayerAvatar>(PlayerAvatar.All);
            var stale = new List<PlayerAvatar>();
            foreach (var kv in _nameplates) if (!alive.Contains(kv.Key) || kv.Key == null) stale.Add(kv.Key);
            foreach (var k in stale) { _nameplates[k].RemoveFromHierarchy(); _nameplates.Remove(k); }

            foreach (var av in PlayerAvatar.All)
            {
                if (av == null || av.IsOwner) continue;
                if (!_nameplates.TryGetValue(av, out var label))
                {
                    label = UI.Text(_nameLayer, "", "nameplate");
                    label.pickingMode = PickingMode.Ignore;
                    _nameplates[av] = label;
                }
                var world = av.transform.position + Vector3.up * 2.2f;
                var toTarget = world - cam.transform.position;
                bool visible = Vector3.Dot(toTarget, cam.transform.forward) > 0 && toTarget.magnitude < 35f;
                UI.Show(label, visible);
                if (!visible) continue;
                var pos = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, cam);
                label.text = av.DisplayName + (av.Stats.IsGhost.Value ? "（灵魂）" : "");
                label.style.color = TeamUtil.Color(av.Team);
                label.style.left = pos.x - 100;
                label.style.top = pos.y - 20;
            }
        }

        // ---------- 小地图 ----------

        void EnsureMinimapCamera()
        {
            if (_mapCam != null) return;
            // 无显卡环境（命令行测试、专用服务器）不建小地图
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            _mapRt = new RenderTexture(256, 256, 16);
            var go = new GameObject("[MinimapCamera]");
            _mapCam = go.AddComponent<Camera>();
            _mapCam.orthographic = true;
            _mapCam.orthographicSize = 22f;
            _mapCam.targetTexture = _mapRt;
            _mapCam.clearFlags = CameraClearFlags.SolidColor;
            _mapCam.backgroundColor = new Color(0.1f, 0.12f, 0.15f);
            _mapCam.transform.rotation = Quaternion.Euler(90, 0, 0);
            _minimap.image = _mapRt;
        }

        void UpdateMinimap(PlayerAvatar me)
        {
            if (_mapCam == null) EnsureMinimapCamera();
            if (_mapCam == null) return;
            var p = me.transform.position;
            _mapCam.transform.position = new Vector3(p.x, 60f, p.z);
            _minimapMarker.style.rotate = new Rotate(new Angle(me.transform.eulerAngles.y, AngleUnit.Degree));
        }
    }
}
