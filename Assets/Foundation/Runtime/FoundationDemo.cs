using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MyGame.Foundation
{
    // An explicit demo scene bootstrap, never injected into arbitrary scenes.
    public sealed class FoundationDemo : MonoBehaviour
    {
        [SerializeField, Min(0)] private float refreshMinutes = 2;
        public SupplyCatalog Catalog { get; private set; }
        public MatchSession Session { get; private set; }
        public PlayerState Player { get; private set; }
        public Inventory Inventory { get; private set; }
        public int SelectedSlot { get; private set; }
        public const int HotbarSize = 5;
        public FirstPersonMotor Motor { get; private set; }
        public Camera ViewCamera { get; private set; }
        public RenderTexture Minimap { get; private set; }
        public string Message { get; private set; } = "Local foundation demo";
        public bool PanelOpen { get; private set; }
        public FoundationHud Hud { get; private set; }
        private SupplySpawner spawner;
        private Camera mapCamera;
        private bool focused = true;
        private float originalVolume;
        private SupplyTables tables;
        public LocalRoomDirectory Directory { get; private set; } = new LocalRoomDirectory();
        public RoomModel Room { get; private set; }
        public FrontendStage Stage { get; private set; } = FrontendStage.Lobby;
        private int simulatedId;
        public bool IsHost { get { return Room != null && Room.HostId == Player.Id; } }
        public void CreateRoom(string name, string playerName)
        {
            var member = new RoomMember("local-player", playerName);
            Room = Directory.Create(name, member); Player = member.Player; Stage = FrontendStage.Room; Hud.ShowRoom();
        }
        public void JoinRoom(RoomModel room, string playerName)
        {
            var member = new RoomMember("local-player", playerName);
            if (!room.Join(member)) { Message = "Room unavailable, full or already started."; return; }
            Room = room; Player = member.Player; Stage = FrontendStage.Room; Hud.ShowRoom();
        }
        public void AddSimulatedMember()
        {
            if (!IsHost) return;
            var id = ++simulatedId; Room.Join(new RoomMember("sim-" + id, "Demo member " + id)); Hud.ShowRoom();
        }
        public void LeaveRoom()
        {
            if (Room == null || !Room.Leave(Player.Id)) return;
            Room = null; Stage = FrontendStage.Lobby; Player = new PlayerState("local-player"); Hud.ShowLobby();
        }
        // Special effects need a gameplay handler: false leaves the item untouched.
        public Func<SupplyDefinition, PlayerState, bool> SpecialEffectHandler;
        private void Awake()
        {
            originalVolume = AudioListener.volume;
            var json = Resources.Load<TextAsset>("SupplyTables");
            if (json == null) throw new InvalidOperationException("Missing SupplyTables resource.");
            tables = JsonUtility.FromJson<SupplyTables>(json.text); Catalog = tables.ToCatalog();
            Inventory = new Inventory(20); Player = new PlayerState("local-player");
            Directory.Create("Sample room (local simulation)", new RoomMember("sample-host", "Demo host"));
            Session = new MatchSession(Catalog, Environment.TickCount);
            Session.PhaseChanged += OnPhaseChanged;
            BuildWorld(); Hud = gameObject.AddComponent<FoundationHud>(); Hud.Initialize(this);
            UpdateCursor();
        }
        public void StartHostedDemo()
        {
            StartRoomMatch(Player.Id);
        }
        public void SimulateHostStart() { if (Room != null && !IsHost) StartRoomMatch(Room.HostId); }
        private void StartRoomMatch(string hostId)
        {
            if (Room == null || !Room.TryStart(hostId, Session)) { Message = "Only the host may start. Player count must be even (2-10)."; return; }
            Stage = FrontendStage.InGame;
            foreach (var member in Room.Members)
            {
                if (member.Player.Id == Player.Id) continue;
                member.Player.SelectLike(Catalog.items[0].id); member.Player.ToggleDislike(Catalog.items[1].id); member.Player.Confirm();
            }
            Hud.ShowPreparation();
        }
        private void Update()
        {
            Session.Tick(Time.deltaTime);
            var keyboard = Keyboard.current;
            if (!focused) return;
            if (Stage == FrontendStage.Room && keyboard != null && keyboard.spaceKey.wasPressedThisFrame && !Hud.ChatFocused) { Hud.OpenChat(); return; }
            if (Session.Phase != MatchPhase.Playing) return;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) { if (PanelOpen) Hud.ClosePanel(); else Hud.ShowSettings(); return; }
                if (keyboard.tabKey.wasPressedThisFrame && !Hud.HasModal) { SetPanelOpen(!PanelOpen); return; }
                if (keyboard.bKey.wasPressedThisFrame) { Hud.ToggleBackpack(); return; }
                if (keyboard.cKey.wasPressedThisFrame) { Hud.ToggleInfo(); return; }
                if (!PanelOpen)
                {
                    Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
                    for (int i = 0; i < keys.Length; i++) if (keyboard[keys[i]].wasPressedThisFrame) SelectSlot(i);
                    if (keyboard.eKey.wasPressedThisFrame) TryPickup();
                }
            }
            if (!PanelOpen && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) UseSelected();
        }
        private void LateUpdate()
        { if (mapCamera != null) mapCamera.transform.position = new Vector3(Motor.transform.position.x, 30, Motor.transform.position.z); }
        private void OnPhaseChanged(MatchPhase phase)
        {
            if (phase == MatchPhase.Playing)
            { spawner.Begin(Catalog.items, Environment.TickCount); Hud.ClosePanel(); Message = "WASD move | E pick up | 1-5 select | Click use | B backpack | C info | Esc settings"; }
            if (phase == MatchPhase.Finished) { spawner.StopAndClear(); Hud.ShowFinished(); }
            UpdateCursor();
        }
        public void SetPanelOpen(bool value) { PanelOpen = value; UpdateCursor(); }
        private void UpdateCursor()
        {
            bool playable = focused && Session.Phase == MatchPhase.Playing && !PanelOpen;
            if (Motor != null) Motor.InputEnabled = playable;
            Cursor.lockState = playable ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !playable;
        }
        private void OnApplicationFocus(bool value) { focused = value; UpdateCursor(); }
        public void SelectSlot(int index) { if (index >= 0 && index < HotbarSize) SelectedSlot = index; }
        private void TryPickup()
        {
            RaycastHit hit;
            if (!Physics.Raycast(ViewCamera.transform.position, ViewCamera.transform.forward, out hit, 3)) { Message = "Aim at a supply within 3 metres, then press E."; return; }
            var pickup = hit.collider.GetComponent<SupplyPickup>();
            if (pickup == null) return;
            Message = pickup.TryCollect(Inventory) ? "Supply picked up." : "Backpack full; supply stays in the world.";
        }
        public void UseSelected()
        {
            if (Session.Phase != MatchPhase.Playing || PanelOpen) return;
            var stack = Inventory[SelectedSlot];
            if (stack == null) { Message = "Selected slot is empty."; return; }
            var item = stack.Item;
            if (!item.CanUse) { Message = item.name + ": no use rule has been specified."; return; }
            if (!string.IsNullOrEmpty(item.specialDesc) && (SpecialEffectHandler == null || !SpecialEffectHandler(item, Player)))
            { Message = "Special effect handler not configured; item retained."; return; }
            Player.Apply(item); Inventory.RemoveOne(SelectedSlot); Message = "Used " + item.name;
        }
        private void BuildWorld()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Demo floor";
            ground.transform.localScale = new Vector3(4, 1, 4);
            var actor = new GameObject("Local player"); actor.transform.position = new Vector3(0, .1f, -5);
            var controller = actor.AddComponent<CharacterController>(); controller.height = 1.8f; controller.center = Vector3.up * .9f;
            ViewCamera = Camera.main;
            if (ViewCamera == null) { ViewCamera = new GameObject("First person camera").AddComponent<Camera>(); ViewCamera.gameObject.tag = "MainCamera"; ViewCamera.gameObject.AddComponent<AudioListener>(); }
            ViewCamera.transform.SetParent(actor.transform, false); ViewCamera.transform.localPosition = Vector3.up * 1.65f; ViewCamera.transform.localRotation = Quaternion.identity;
            Motor = actor.AddComponent<FirstPersonMotor>(); Motor.view = ViewCamera.transform;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder); marker.name = "Minimap player marker";
            marker.transform.SetParent(actor.transform, false); marker.transform.localPosition = Vector3.up * 2.2f; marker.transform.localScale = new Vector3(.5f, .03f, .5f);
            // Ignore raycast layer; hidden to first-person camera and visible to map camera.
            marker.layer = 2; Destroy(marker.GetComponent<Collider>()); ViewCamera.cullingMask &= ~(1 << 2);
            Minimap = new RenderTexture(256, 256, 16); Minimap.Create();
            mapCamera = new GameObject("Minimap camera").AddComponent<Camera>(); mapCamera.orthographic = true; mapCamera.orthographicSize = 12;
            mapCamera.transform.rotation = Quaternion.Euler(90, 0, 0); mapCamera.targetTexture = Minimap; mapCamera.depth = -10;
            spawner = gameObject.AddComponent<SupplySpawner>(); spawner.refreshMinutes = refreshMinutes;
            spawner.points = new SupplyPoint[tables.dropLocations.Length];
            for (int i = 0; i < spawner.points.Length; i++)
            {
                var row = tables.dropLocations[i];
                spawner.points[i] = new SupplyPoint { address = new Vector3(row.address.x, row.address.y, row.address.z), rule = new SpawnRule { id = row.id, name = row.name, types = row.type, chance = row.chance, minCount = row.number, maxCount = row.number } };
            }
        }
        private void OnDestroy()
        {
            if (Session != null) Session.PhaseChanged -= OnPhaseChanged;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true; AudioListener.volume = originalVolume;
            if (mapCamera != null) mapCamera.targetTexture = null;
            if (Minimap != null) { Minimap.Release(); Destroy(Minimap); }
        }
    }
}
