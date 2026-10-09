using UnityEngine;
using UnityEngine.UIElements;

namespace MyGame
{
    /// <summary>开始页面与创建房间弹窗（基建v1：开始页面、创建房间、房间设置）。</summary>
    public class MenuScreen : UIScreen
    {
        const string NameKey = "mygame.playerName";
        readonly UIManager _ui;

        public static string PlayerName
        {
            get
            {
                var n = UnityEngine.PlayerPrefs.GetString(NameKey, "");
                if (string.IsNullOrWhiteSpace(n))
                {
                    n = "玩家" + Random.Range(1000, 9999);
                    UnityEngine.PlayerPrefs.SetString(NameKey, n);
                }
                return n;
            }
            set => UnityEngine.PlayerPrefs.SetString(NameKey, value?.Trim() ?? "");
        }

        public MenuScreen(VisualElement parent, UIManager ui) : base(parent, "menu")
        {
            _ui = ui;

            UI.Text(Root, "芳心纵火犯", "game-title");

            var stage = UI.Box(Root, "menu-stage");
            var left = UI.Box(stage, "character-slot");
            UI.Text(left, "角色（其一）\n待机动作", "placeholder-text");
            var buttons = UI.Box(stage, "menu-buttons");
            var right = UI.Box(stage, "character-slot");
            UI.Text(right, "玩家形象（3D）\n待机动作", "placeholder-text");

            UI.Btn(buttons, "创建房间", OpenCreateDialog, "btn-big", "btn-primary");
            UI.Btn(buttons, "加入房间", () => _ui.Page = UIManager.OfflinePage.Join, "btn-big");
            UI.Btn(buttons, "设置", () => _ui.NotImplemented("设置"), "btn-big");
            UI.Btn(buttons, "退出游戏", () => _ui.Confirm("是否退出游戏？", Quit), "btn-big");
        }

        protected override void OnShow()
        {
            UIManager.SetCursorLocked(false);
        }

        void OpenCreateDialog()
        {
            var (body, close) = _ui.Dialog("创建房间");
            var nameField = UI.Input(body, "房间：", $"{PlayerName}的房间", 18);

            var settings = UI.Box(body, "panel");
            UI.Text(settings, "房间设置", "panel-title");

            var countRow = UI.Box(settings, "row", "setting-row");
            UI.Text(countRow, "人数设置", "setting-label");
            var b8 = UI.Btn(countRow, "8人", () => _ui.NotImplemented("8 人房"), "chip");
            var b10 = UI.Btn(countRow, "10人", null, "chip", "chip-on");
            b8.SetEnabled(false);

            var playerName = UI.Input(settings, "个人名字", PlayerName, 16);
            UI.Text(settings, "（名字是否需要接审核：策划案待定）", "hint");

            var codeRow = UI.Box(settings, "row", "setting-row");
            UI.Text(codeRow, "房间代码", "setting-label");
            UI.Text(codeRow, "创建后由系统随机分配", "hint");

            var row = UI.Box(body, "row", "dialog-buttons");
            UI.Btn(row, "取消", close);
            UI.Btn(row, "确认创建", () =>
            {
                PlayerName = playerName.value;
                var boot = NetworkBootstrap.Instance;
                if (boot == null || !boot.CreateRoom(nameField.value, GameConfig.DefaultMaxPlayers, PlayerName))
                {
                    // 失败原因写在 LastDisconnectReason 里，由 UIManager 统一提示
                    if (boot == null) _ui.ShowToast("网络模块没有初始化，请先生成网络预制体。");
                    return;
                }
                close();
            }, "btn-primary");
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
