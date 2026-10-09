using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MyGame
{
    /// <summary>
    /// 创建 UIDocument，按联机状态和对局阶段切换界面，并提供 toast、二次确认弹窗和鼠标锁定。
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        /// <summary>界面挡住了操作（打开背包、弹窗、聊天输入等），角色不响应移动和交互。</summary>
        public static bool GameplayInputBlocked => Instance == null || Instance._blockReasons.Count > 0 || !_cursorLocked;

        static bool _cursorLocked;
        readonly HashSet<string> _blockReasons = new HashSet<string>();

        UIDocument _doc;
        VisualElement _screens;
        VisualElement _dialogLayer;
        VisualElement _toastLayer;

        MenuScreen _menu;
        JoinScreen _join;
        RoomScreen _room;
        PrepScreen _prep;
        HudScreen _hud;

        public enum OfflinePage { Menu, Join }
        public OfflinePage Page { get; set; } = OfflinePage.Menu;

        void Awake()
        {
            Instance = this;

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("MyGame/UI/MyGameTheme");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.match = 0.5f;
            panel.clearColor = false;

            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = panel;
        }

        void OnEnable()
        {
            // UIDocument 在 OnEnable 里才建好 rootVisualElement
            if (_screens != null) return;
            var root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("MyGame/UI/MyGameUI");
            if (sheet != null) root.styleSheets.Add(sheet);
            else Debug.LogError("[MyGame] 找不到样式表 Resources/MyGame/UI/MyGameUI.uss");
            ApplyChineseFont(root);
            root.AddToClassList("root");
            root.pickingMode = PickingMode.Ignore;

            _screens = UI.Box(root, "layer");
            _dialogLayer = UI.Box(root, "layer");
            _toastLayer = UI.Box(root, "layer", "toast-layer");
            _screens.pickingMode = PickingMode.Ignore;
            _dialogLayer.pickingMode = PickingMode.Ignore;
            _toastLayer.pickingMode = PickingMode.Ignore;

            _menu = new MenuScreen(_screens, this);
            _join = new JoinScreen(_screens, this);
            _room = new RoomScreen(_screens, this);
            _prep = new PrepScreen(_screens, this);
            _hud = new HudScreen(_screens, this);

            Session.Toast += ShowToast;
        }

        void OnDestroy() => Session.Toast -= ShowToast;

        static void ApplyChineseFont(VisualElement root)
        {
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 32);
            if (font != null) root.style.unityFontDefinition = FontDefinition.FromFont(font);
        }

        void Update()
        {
            var boot = NetworkBootstrap.Instance;
            var session = Session.Instance;
            bool online = boot != null && boot.IsOnline;

            // 断线、被踢、被拒绝加入后回到离线页面时提示原因
            if (!online && boot != null && !string.IsNullOrEmpty(boot.LastDisconnectReason))
            {
                ShowToast(boot.LastDisconnectReason);
                boot.LastDisconnectReason = null;
            }

            UIScreen want;
            if (!online || session == null || !session.IsSpawned)
                want = online ? null : (Page == OfflinePage.Join ? _join : (UIScreen)_menu);
            else switch (session.Phase.Value)
            {
                case GamePhase.Room: want = _room; break;
                case GamePhase.Prep: want = _prep; break;
                default: want = _hud; break;
            }

            foreach (var s in new UIScreen[] { _menu, _join, _room, _prep, _hud })
                s.SetVisible(s == want);
            want?.Tick();

            TickToasts();
        }

        // ================= 输入与鼠标 =================

        public static void SetCursorLocked(bool locked)
        {
            _cursorLocked = locked;
            UnityEngine.Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !locked;
        }

        public static bool CursorLocked => _cursorLocked;

        public void Block(string reason, bool on)
        {
            if (on) _blockReasons.Add(reason); else _blockReasons.Remove(reason);
        }

        // ================= Toast =================

        readonly List<(Label label, float until)> _toasts = new List<(Label, float)>();

        public void ShowToast(string text)
        {
            if (_toastLayer == null) return;
            var l = UI.Text(_toastLayer, text, "toast");
            _toasts.Add((l, Time.unscaledTime + 2.5f));
            while (_toasts.Count > 4)
            {
                _toasts[0].label.RemoveFromHierarchy();
                _toasts.RemoveAt(0);
            }
        }

        void TickToasts()
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _toasts[i].until) continue;
                _toasts[i].label.RemoveFromHierarchy();
                _toasts.RemoveAt(i);
            }
        }

        // ================= 弹窗 =================

        public void Confirm(string message, Action onYes, string yes = "确定", string no = "取消")
        {
            var mask = UI.Box(_dialogLayer, "mask");
            var box = UI.Box(mask, "dialog");
            UI.Text(box, message, "dialog-text");
            var row = UI.Box(box, "row", "dialog-buttons");
            Block("dialog", true);
            void Close()
            {
                mask.RemoveFromHierarchy();
                if (_dialogLayer.childCount == 0) Block("dialog", false);
            }
            UI.Btn(row, no, Close);
            UI.Btn(row, yes, () => { Close(); onYes?.Invoke(); }, "btn-primary");
        }

        /// <summary>自定义内容的弹窗，返回内容容器和关闭方法。</summary>
        public (VisualElement body, Action close) Dialog(string title)
        {
            var mask = UI.Box(_dialogLayer, "mask");
            var box = UI.Box(mask, "dialog", "dialog-wide");
            UI.Text(box, title, "dialog-title");
            var body = UI.Box(box, "dialog-body");
            Block("dialog", true);
            void Close()
            {
                mask.RemoveFromHierarchy();
                if (_dialogLayer.childCount == 0) Block("dialog", false);
            }
            return (body, Close);
        }

        public bool DialogOpen => _dialogLayer != null && _dialogLayer.childCount > 0;

        public void NotImplemented(string what) => ShowToast($"{what}：demo 阶段暂不做");
    }
}
