using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MyGame.Foundation
{
    public sealed class FoundationHud : MonoBehaviour
    {
        private FoundationDemo game;
        private RectTransform root, modal, content, panel;
        private Text clock, notice, stats, prepStatus;
        private Button confirm;
        private readonly Button[] hotbar = new Button[FoundationDemo.HotbarSize];
        private Button[] likes, dislikes;
        private Font font;
        private InputField chatInput;
        private Text chatHistory;
        private RectTransform chatHistoryPanel;
        private Button startRoomButton;
        public bool ChatFocused { get { return chatInput != null && chatInput.isFocused; } }
        private string currentPanel;
        public bool HasModal { get { return currentPanel != null; } }
        private Color surface = new Color(.09f, .13f, .2f, .97f);
        public void Initialize(FoundationDemo demo)
        {
            game = demo; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasObject = new GameObject("Foundation HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            canvasObject.GetComponent<CanvasScaler>().matchWidthOrHeight = .5f;
            root = canvasObject.GetComponent<RectTransform>();
            if (EventSystem.current == null) new GameObject("Foundation EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(transform, false);
            var map = Box(root, "Minimap", new Vector2(170, -170), new Vector2(250, 250), new Vector2(0, 1));
            var mapImage = new GameObject("Map image", typeof(RectTransform), typeof(RawImage)); mapImage.transform.SetParent(map, false);
            Stretch(mapImage.GetComponent<RectTransform>()); mapImage.GetComponent<RawImage>().texture = game.Minimap;
            Label(root, "N", new Vector2(170, -30), new Vector2(100, 30), new Vector2(0, 1));
            clock = Label(root, "WAITING", new Vector2(0, -55), new Vector2(350, 50), new Vector2(.5f, 1), 32);
            ButtonAt(root, "Settings", new Vector2(-260, -65), new Vector2(160, 60), new Vector2(1, 1), ShowSettings);
            ButtonAt(root, "Info [C]", new Vector2(-80, -65), new Vector2(160, 60), new Vector2(1, 1), ToggleInfo);
            for (int i = 0; i < hotbar.Length; i++)
            { int slot = i; hotbar[i] = ButtonAt(root, "", new Vector2(80 + i * 130, 90), new Vector2(120, 100), Vector2.zero, () => game.SelectSlot(slot)); }
            ButtonAt(root, "Backpack [B]", new Vector2(150, 200), new Vector2(260, 60), Vector2.zero, ToggleBackpack);
            stats = Label(root, "", new Vector2(-260, 90), new Vector2(450, 100), new Vector2(1, 0));
            notice = Label(root, "", new Vector2(0, 35), new Vector2(1600, 45), new Vector2(.5f, 0), 20);
            Label(root, "+", Vector2.zero, new Vector2(30, 30), new Vector2(.5f, .5f), 26);
            modal = Box(root, "Modal mask", Vector2.zero, Vector2.zero, new Vector2(.5f, .5f), new Color(0, 0, 0, .8f)); Stretch(modal);
            // The mask raycasts, so clicks cannot reach the HUD or world underneath.
            panel = Box(modal, "Panel", Vector2.zero, new Vector2(1040, 860), new Vector2(.5f, .5f));
            content = new GameObject("Panel content", typeof(RectTransform)).GetComponent<RectTransform>(); content.SetParent(panel, false); Stretch(content);
            ShowLobby();
        }
        private void Update()
        {
            if (game == null) return;
            var session = game.Session;
            int seconds = Mathf.CeilToInt((float)session.Remaining);
            clock.text = session.Phase == MatchPhase.Waiting ? "WAITING" : session.Phase + "  " + seconds / 60 + ":" + (seconds % 60).ToString("00");
            notice.text = game.Message;
            stats.text = "Hunger " + game.Player.Hunger.ToString("0") + " / 100\nHydration " + game.Player.Hydration.ToString("0") + " / 100";
            for (int i = 0; i < hotbar.Length; i++)
            {
                var stack = game.Inventory[i];
                hotbar[i].GetComponentInChildren<Text>().text = (i + 1) + "\n" + (stack == null ? "Empty" : stack.Item.name + " x" + stack.Count);
                Mark(hotbar[i], game.SelectedSlot == i);
            }
            if (currentPanel == "prepare") RefreshPreferences();
            if (currentPanel == "room" && startRoomButton != null) startRoomButton.interactable = game.Room.CanStart(game.Player.Id);
            if (chatHistoryPanel != null) chatHistoryPanel.gameObject.SetActive(ChatFocused);
        }
        private void BeginPanel(string name)
        {
            currentPanel = name;
            foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            prepStatus = null; confirm = null; chatInput = null; chatHistory = null; chatHistoryPanel = null; startRoomButton = null;
            modal.gameObject.SetActive(true); modal.SetAsLastSibling(); game.SetPanelOpen(true);
        }
        private void Title(string title) { Label(content, title, new Vector2(0, -55), new Vector2(950, 65), new Vector2(.5f, 1), 30); }
        private void CloseButton() { ButtonAt(content, "Close", new Vector2(0, 55), new Vector2(200, 60), new Vector2(.5f, 0), ClosePanel); }
        public void ClosePanel()
        {
            if (game.Session.Phase == MatchPhase.Preparing && !game.Player.Confirmed) return;
            currentPanel = null; modal.gameObject.SetActive(false); game.SetPanelOpen(false);
        }
        public void ShowLobby()
        {
            BeginPanel("lobby"); Title("Heartbreak Arsonist / local foundation demo");
            Label(content, "Lobby -> room -> 10s preparation -> 20min match\nRoom capacity: 10. Only even player counts can start.\n\nRoom listing and chat here are local simulations.\nSteam discovery, invitation and network transport are pending.", new Vector2(0, 160), new Vector2(950, 240), new Vector2(.5f, .5f), 25);
            ButtonAt(content, "Create room", new Vector2(0, 10), new Vector2(380, 65), new Vector2(.5f, .5f), ShowCreateRoom);
            ButtonAt(content, "Join room", new Vector2(0, -80), new Vector2(380, 65), new Vector2(.5f, .5f), () => ShowRoomList(""));
            ButtonAt(content, "Settings (v1 demo: omitted)", new Vector2(0, -170), new Vector2(380, 65), new Vector2(.5f, .5f), () => { });
            ButtonAt(content, "Quit", new Vector2(0, -260), new Vector2(380, 65), new Vector2(.5f, .5f), () => Ask("Quit the game?", () => {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }, ShowLobby));
        }
        private void ShowCreateRoom()
        {
            BeginPanel("create"); Title("Create room");
            Label(content, "Room name", new Vector2(0, 240), new Vector2(700, 50), new Vector2(.5f, .5f));
            var roomName = InputAt(content, "My room", new Vector2(0, 170));
            Label(content, "Your name", new Vector2(0, 70), new Vector2(700, 50), new Vector2(.5f, .5f));
            var playerName = InputAt(content, "Player", Vector2.zero);
            Label(content, "Capacity: 10 (v1 demo rule)\nRoom code is generated on creation.\nNames: 1-32 visible characters; no content moderation service.", new Vector2(0, -130), new Vector2(850, 170), new Vector2(.5f, .5f), 22);
            var error = Label(content, "", new Vector2(0, -240), new Vector2(900, 50), new Vector2(.5f, .5f), 20);
            ButtonAt(content, "Confirm create", new Vector2(-180, 65), new Vector2(300, 65), new Vector2(.5f, 0), () => { try { game.CreateRoom(roomName.text, playerName.text); } catch (ArgumentException e) { error.text = e.Message; } });
            ButtonAt(content, "Back", new Vector2(180, 65), new Vector2(240, 65), new Vector2(.5f, 0), ShowLobby);
        }
        private void ShowRoomList(string query)
        {
            BeginPanel("list"); Title("Join room / local simulation");
            var search = InputAt(content, query, new Vector2(0, 260));
            ButtonAt(content, "Search / refresh", new Vector2(0, 160), new Vector2(350, 60), new Vector2(.5f, .5f), () => ShowRoomList(search.text));
            float y = 65;
            foreach (var room in game.Directory.Search(query))
            {
                var target = room;
                ButtonAt(content, room.Name + " [" + room.Code + "] " + room.Members.Count + "/10", new Vector2(0, y), new Vector2(900, 65), new Vector2(.5f, .5f), () => game.JoinRoom(target, "Player")); y -= 80;
            }
            ButtonAt(content, "Back", new Vector2(0, 65), new Vector2(240, 60), new Vector2(.5f, 0), ShowLobby);
        }
        public void ShowRoom()
        {
            BeginPanel("room"); Title(game.Room.Name + " / code " + game.Room.Code);
            var room = game.Room;
            Label(content, "Members " + room.Members.Count + "/10 | " + (game.IsHost ? "HOST" : "MEMBER") + " | Start needs even count", new Vector2(0, -125), new Vector2(950, 50), new Vector2(.5f, 1), 23);
            var memberList = MemberList(content);
            foreach (var member in room.Members)
            {
                var target = member;
                var row = Box(memberList, "Member", Vector2.zero, new Vector2(900, 42), new Vector2(.5f, .5f));
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;
                Label(row, member.Name + " | " + (member.Ready ? "Ready" : "Not ready") + " | " + member.Player.Team + " | " + (member.MicrophoneOpen ? member.Speaking ? "Mic: speaking" : "Mic: on" : "Mic: muted"), new Vector2(-135, 0), new Vector2(580, 40), new Vector2(.5f, .5f), 20);
                if (game.IsHost && member.Player.Id.StartsWith("sim-"))
                    ButtonAt(row, "Ready?", new Vector2(260, 0), new Vector2(100, 40), new Vector2(.5f, .5f), () => { room.ToggleReady(target.Player.Id); ShowRoom(); });
                if (game.IsHost && member.Player.Id != room.HostId)
                    ButtonAt(row, "Kick", new Vector2(380, 0), new Vector2(100, 40), new Vector2(.5f, .5f), () => Ask("Kick " + target.Name + "?", () => { room.Kick(game.Player.Id, target.Player.Id); ShowRoom(); }, ShowRoom));
            }
            if (game.IsHost)
            {
                startRoomButton = ButtonAt(content, "Start game", new Vector2(-330, -260), new Vector2(250, 60), new Vector2(.5f, .5f), game.StartHostedDemo);
                ButtonAt(content, "Add demo member", new Vector2(0, -260), new Vector2(300, 60), new Vector2(.5f, .5f), game.AddSimulatedMember);
                ButtonAt(content, "Room settings", new Vector2(330, -260), new Vector2(250, 60), new Vector2(.5f, .5f), ShowRoomSettings);
            }
            else
            {
                ButtonAt(content, "Toggle ready", new Vector2(-230, -260), new Vector2(300, 60), new Vector2(.5f, .5f), () => { room.ToggleReady(game.Player.Id); ShowRoom(); });
                ButtonAt(content, "Simulate host start", new Vector2(230, -260), new Vector2(350, 60), new Vector2(.5f, .5f), game.SimulateHostStart);
            }
            ButtonAt(content, "Leave room", new Vector2(-330, 65), new Vector2(250, 60), new Vector2(.5f, 0), () => Ask(game.IsHost ? "Leaving as host dissolves this room. Leave?" : "Leave this room?", game.LeaveRoom, ShowRoom));
            ButtonAt(content, "Map preview", new Vector2(0, 65), new Vector2(300, 60), new Vector2(.5f, 0), ShowMapPreview);
            ButtonAt(content, "Preferences (at start)", new Vector2(330, 65), new Vector2(300, 60), new Vector2(.5f, 0), () => Ask("Preferences open for 10 seconds when the host starts.", ShowRoom, ShowRoom));
            chatInput = InputAt(content, "", new Vector2(0, -180)); chatInput.characterLimit = 200; chatInput.interactable = false;
            var localInput = chatInput;
            chatInput.onEndEdit.AddListener(text => {
                if (!string.IsNullOrWhiteSpace(text)) room.SendChat(game.Player.Id, text);
                localInput.text = ""; localInput.interactable = false;
                if (chatHistory != null && currentPanel == "room") chatHistory.text = string.Join("\n", room.Chat);
            });
            chatHistoryPanel = Box(content, "Chat history", new Vector2(0, -20), new Vector2(920, 250), new Vector2(.5f, .5f));
            chatHistory = Label(chatHistoryPanel, string.Join("\n", room.Chat), Vector2.zero, new Vector2(900, 240), new Vector2(.5f, .5f), 20); chatHistoryPanel.gameObject.SetActive(false);
        }
        private RectTransform MemberList(RectTransform parent)
        {
            var viewport = Box(parent, "Members viewport", new Vector2(0, 65), new Vector2(950, 380), new Vector2(.5f, .5f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var inner = new GameObject("Members", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>(); inner.SetParent(viewport, false);
            inner.anchorMin = new Vector2(0, 1); inner.anchorMax = new Vector2(1, 1); inner.pivot = new Vector2(.5f, 1); inner.sizeDelta = new Vector2(-30, 0);
            var layout = inner.GetComponent<VerticalLayoutGroup>(); layout.spacing = 3; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandHeight = false; layout.padding = new RectOffset(10, 10, 10, 10);
            inner.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = inner; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var barRect = Box(viewport, "Scrollbar", new Vector2(-10, 0), new Vector2(16, 370), new Vector2(1, .5f), Color.gray);
            var bar = barRect.gameObject.AddComponent<Scrollbar>(); var handle = Box(barRect, "Handle", Vector2.zero, new Vector2(16, 50), new Vector2(.5f, .5f), Color.white);
            bar.handleRect = handle; bar.targetGraphic = handle.GetComponent<Image>(); bar.direction = Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar = bar;
            return inner;
        }
        public void OpenChat()
        { if (currentPanel != "room" || chatInput == null) return; chatInput.interactable = true; chatInput.ActivateInputField(); }
        private void ShowRoomSettings()
        {
            BeginPanel("room-settings"); Title("Room settings");
            Label(content, "Capacity: 10\nStart: at least 2 players; total count must be even.\n\nv2 start-any-time mode (default) vs v1 all-ready mode:", new Vector2(0, 120), new Vector2(950, 240), new Vector2(.5f, .5f));
            ButtonAt(content, game.Room.RequireAllReady ? "All-ready required: ON" : "All-ready required: OFF", new Vector2(0, -50), new Vector2(550, 70), new Vector2(.5f, .5f), () => { game.Room.RequireAllReady = !game.Room.RequireAllReady; ShowRoomSettings(); });
            ButtonAt(content, "Back to room", new Vector2(0, 65), new Vector2(300, 60), new Vector2(.5f, 0), ShowRoom);
        }
        private void ShowMapPreview()
        {
            BeginPanel("preview"); Title("Demo map / one map only");
            var preview = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)); preview.transform.SetParent(content, false);
            var rect = preview.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(550, 550); preview.GetComponent<RawImage>().texture = game.Minimap;
            ButtonAt(content, "Back to room", new Vector2(0, 65), new Vector2(300, 60), new Vector2(.5f, 0), ShowRoom);
        }
        private void Ask(string question, Action yes, Action no)
        {
            BeginPanel("confirmation"); Title(question);
            ButtonAt(content, "Confirm", new Vector2(-200, 0), new Vector2(300, 70), new Vector2(.5f, .5f), yes);
            ButtonAt(content, "Cancel", new Vector2(200, 0), new Vector2(300, 70), new Vector2(.5f, .5f), no);
        }
        private InputField InputAt(RectTransform parent, string value, Vector2 position)
        {
            var rect = Box(parent, "Input", position, new Vector2(850, 65), new Vector2(.5f, .5f), new Color(.15f, .2f, .3f));
            var input = rect.gameObject.AddComponent<InputField>(); input.characterLimit = 32;
            var label = Label(rect, value, Vector2.zero, new Vector2(820, 55), new Vector2(.5f, .5f), 24); label.supportRichText = false; input.textComponent = label; input.targetGraphic = rect.GetComponent<Image>(); input.text = value; return input;
        }
        public void ShowPreparation()
        {
            BeginPanel("prepare"); Title("Choose your preferences");
            Label(content, "Like: exactly 1", new Vector2(-260, -150), new Vector2(430, 45), new Vector2(.5f, 1));
            Label(content, "Dislike: 1 to 3", new Vector2(260, -150), new Vector2(430, 45), new Vector2(.5f, 1));
            likes = new Button[game.Catalog.items.Length]; dislikes = new Button[likes.Length];
            // Scrollable item lists retain the spec's 100 x 100 buttons for larger catalogs.
            var left = GridArea(content, new Vector2(-260, 80), new Vector2(460, 330));
            var right = GridArea(content, new Vector2(260, 80), new Vector2(460, 330));
            for (int i = 0; i < likes.Length; i++)
            {
                var item = game.Catalog.items[i];
                likes[i] = ItemButton(left, item, () => game.Player.SelectLike(item.id));
                dislikes[i] = ItemButton(right, item, () => game.Player.ToggleDislike(item.id));
            }
            ButtonAt(content, "Red", new Vector2(-220, 150), new Vector2(180, 55), new Vector2(.5f, 0), () => { if (!game.Player.Confirmed) game.Player.Team = Team.Red; });
            ButtonAt(content, "Auto", new Vector2(0, 150), new Vector2(180, 55), new Vector2(.5f, 0), () => { if (!game.Player.Confirmed) game.Player.Team = Team.Unassigned; });
            ButtonAt(content, "Blue", new Vector2(220, 150), new Vector2(180, 55), new Vector2(.5f, 0), () => { if (!game.Player.Confirmed) game.Player.Team = Team.Blue; });
            prepStatus = Label(content, "", new Vector2(0, 235), new Vector2(980, 60), new Vector2(.5f, 0), 20);
            confirm = ButtonAt(content, "Confirm", new Vector2(0, 60), new Vector2(240, 70), new Vector2(.5f, 0), () => { if (game.Player.Confirm()) ClosePanel(); });
            RefreshPreferences();
        }
        private void RefreshPreferences()
        {
            var player = game.Player;
            prepStatus.text = "Team: " + player.Team + " | likes " + (player.Like == null ? 0 : 1) + "/1 | dislikes " + player.Dislikes.Count() + "/3\nTimeout fills missing choices; like and dislike are mutually exclusive.";
            confirm.interactable = player.HasValidPreferences && !player.Confirmed;
            for (int i = 0; i < likes.Length; i++)
            { var item = game.Catalog.items[i]; Mark(likes[i], player.Like == item.id); Mark(dislikes[i], player.Dislikes.Contains(item.id)); dislikes[i].interactable = !player.Confirmed && item.id != player.Like; likes[i].interactable = !player.Confirmed; }
        }
        public void ToggleBackpack()
        {
            if (game.Session.Phase != MatchPhase.Playing) return;
            if (currentPanel == "backpack") { ClosePanel(); return; }
            BeginPanel("backpack"); Title("Backpack / click a slot to swap with selected hotbar slot");
            var grid = GridArea(content, new Vector2(0, 10), new Vector2(950, 570));
            for (int i = 0; i < game.Inventory.Capacity; i++)
            {
                int slot = i; var stack = game.Inventory[i];
                var button = GridButton(grid, "", () => { game.Inventory.Swap(slot, game.SelectedSlot); RefreshBackpack(); });
                button.GetComponentInChildren<Text>().text = (i < FoundationDemo.HotbarSize ? "Hotbar " : "Bag ") + (i + 1) + "\n" + (stack == null ? "Empty" : stack.Item.name + " x" + stack.Count);
            }
            CloseButton();
        }
        private void RefreshBackpack() { currentPanel = null; ToggleBackpack(); }
        public void ToggleInfo()
        {
            if (game.Session.Phase != MatchPhase.Playing) return;
            if (currentPanel == "info") { ClosePanel(); return; }
            BeginPanel("info"); Title("Player information");
            Label(content, "Player: " + game.Player.Id + "\nTeam: " + game.Player.Team + "\nLike: " + game.Player.Like + "\nDislikes: " + string.Join(", ", game.Player.Dislikes) + "\n\nLocal demo / no victory or settlement rules configured.", Vector2.zero, new Vector2(950, 400), new Vector2(.5f, .5f), 25); CloseButton();
        }
        public void ShowSettings()
        {
            if (game.Session.Phase != MatchPhase.Playing) return;
            BeginPanel("settings"); Title("Settings / match clock keeps running");
            Setting("Mouse sensitivity", 150, .03f, .4f, game.Motor.sensitivity, value => game.Motor.sensitivity = value);
            Setting("Field of view", 0, 50, 100, game.ViewCamera.fieldOfView, value => game.ViewCamera.fieldOfView = value);
            Setting("Volume", -150, 0, 1, AudioListener.volume, value => AudioListener.volume = value); CloseButton();
        }
        private void Setting(string label, float y, float min, float max, float value, Action<float> changed)
        {
            var text = Label(content, label + ": " + value.ToString("0.00"), new Vector2(0, y + 50), new Vector2(700, 50), new Vector2(.5f, .5f));
            var track = Box(content, label, new Vector2(0, y), new Vector2(650, 30), new Vector2(.5f, .5f), Color.gray);
            var slider = track.gameObject.AddComponent<Slider>(); slider.minValue = min; slider.maxValue = max;
            var handle = Box(track, "Handle", Vector2.zero, new Vector2(30, 45), new Vector2(.5f, .5f), Color.white);
            slider.handleRect = handle; slider.targetGraphic = handle.GetComponent<Image>(); slider.value = value;
            slider.onValueChanged.AddListener(v => { changed(v); text.text = label + ": " + v.ToString("0.00"); });
        }
        public void ShowFinished()
        { BeginPanel("finished"); Title("Match time is over"); Label(content, "The 20 minute match has ended.\nSettlement and victory rules await a separate gameplay design.\nStop Play mode to restart this demo.", Vector2.zero, new Vector2(900, 300), new Vector2(.5f, .5f), 26); }
        private RectTransform GridArea(RectTransform parent, Vector2 position, Vector2 size)
        {
            var viewport = Box(parent, "Item viewport", position, size, new Vector2(.5f, .5f), new Color(.04f, .07f, .11f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var inner = new GameObject("Items", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>(); inner.SetParent(viewport, false);
            inner.anchorMin = new Vector2(0, 1); inner.anchorMax = new Vector2(1, 1); inner.pivot = new Vector2(.5f, 1); inner.sizeDelta = Vector2.zero;
            var grid = inner.GetComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(100, 100); grid.spacing = new Vector2(12, 12); grid.padding = new RectOffset(10, 10, 10, 10); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = Mathf.Max(1, Mathf.FloorToInt((size.x - 20) / 112));
            inner.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = inner; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            return inner;
        }
        private Button ItemButton(RectTransform parent, SupplyDefinition item, Action action)
        {
            var button = GridButton(parent, item.name, action);
            var sprite = string.IsNullOrEmpty(item.iconResource) ? null : Resources.Load<Sprite>(item.iconResource);
            if (sprite != null)
            { var icon = Box(button.GetComponent<RectTransform>(), "Icon", new Vector2(0, 15), new Vector2(50, 50), new Vector2(.5f, .5f), Color.white); icon.GetComponent<Image>().sprite = sprite; icon.GetComponent<Image>().preserveAspect = true; icon.GetComponent<Image>().raycastTarget = false; }
            else button.GetComponent<Image>().color = SupplySpawner.ColorFor(item.type) * .6f;
            return button;
        }
        private Button GridButton(RectTransform parent, string text, Action action)
        { return ButtonAt(parent, text, Vector2.zero, new Vector2(100, 100), new Vector2(.5f, .5f), action); }
        private void Mark(Button button, bool selected)
        { var outline = button.GetComponent<Outline>(); outline.enabled = selected; outline.effectColor = Color.green; outline.effectDistance = new Vector2(4, 4); }
        private RectTransform Box(RectTransform parent, string name, Vector2 position, Vector2 size, Vector2 anchor, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
            go.GetComponent<Image>().color = color ?? surface; return rect;
        }
        private Text Label(RectTransform parent, string text, Vector2 position, Vector2 size, Vector2 anchor, int fontSize = 23)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
            var label = go.GetComponent<Text>(); label.font = font; label.supportRichText = false; label.text = text; label.fontSize = fontSize; label.color = Color.white; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false; return label;
        }
        private Button ButtonAt(RectTransform parent, string text, Vector2 position, Vector2 size, Vector2 anchor, Action action)
        {
            var rect = Box(parent, text, position, size, anchor, new Color(.2f, .3f, .45f));
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>(); button.onClick.AddListener(() => action()); rect.gameObject.AddComponent<Outline>().enabled = false;
            var label = Label(rect, text, Vector2.zero, size - new Vector2(8, 8), new Vector2(.5f, .5f), 20); Stretch(label.rectTransform); return button;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
