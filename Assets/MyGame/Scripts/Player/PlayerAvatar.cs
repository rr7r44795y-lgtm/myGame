using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MyGame
{
    /// <summary>
    /// 局内角色：第一人称移动与视角（本人），外观与阵营颜色（所有人）。
    /// 位置由本人计算，经 NetworkTransform（Owner 模式）同步给其他人。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerAvatar : NetworkBehaviour
    {
        public const float EyeHeight = 1.6f;

        public static PlayerAvatar Local { get; private set; }
        public static readonly List<PlayerAvatar> All = new List<PlayerAvatar>();

        public PlayerStats Stats { get; private set; }
        public PlayerInventory Inventory { get; private set; }
        public Transform Head { get; private set; }

        CharacterController _cc;
        Renderer _body;
        Renderer _hat;
        float _pitch;
        float _yVelocity;
        Transform _cameraOldParent;

        public string DisplayName => Session.Instance != null ? Session.Instance.NameOf(OwnerClientId) : "";
        public Team Team => Session.Instance != null ? Session.Instance.TeamOf(OwnerClientId) : Team.None;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            Stats = GetComponent<PlayerStats>();
            Inventory = GetComponent<PlayerInventory>();
        }

        public override void OnNetworkSpawn()
        {
            BuildVisuals();
            All.Add(this);
            Stats.IsGhost.OnValueChanged += (_, __) => RefreshLook();
            RefreshLook();

            if (IsOwner)
            {
                Local = this;
                var cam = Camera.main;
                if (cam != null)
                {
                    _cameraOldParent = cam.transform.parent;
                    cam.transform.SetParent(Head, false);
                    cam.transform.localPosition = Vector3.zero;
                    cam.transform.localRotation = Quaternion.identity;
                    cam.nearClipPlane = 0.05f;
                }
                // 本人看不到自己的身体，但保留影子
                _body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                _hat.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this)
            {
                Local = null;
                var cam = Camera.main;
                if (cam != null && cam.transform.parent == Head)
                {
                    cam.transform.SetParent(_cameraOldParent, false);
                    cam.transform.position = new Vector3(0, 1, -10);
                    cam.transform.rotation = Quaternion.identity;
                }
                UIManager.SetCursorLocked(false);
            }
        }

        void BuildVisuals()
        {
            if (Head != null) return;
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(body.GetComponent<Collider>());
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = Vector3.up * 1f;
            _body = body.GetComponent<Renderer>();

            var hat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(hat.GetComponent<Collider>());
            hat.name = "Visor";
            hat.transform.SetParent(transform, false);
            hat.transform.localPosition = new Vector3(0, EyeHeight, 0.35f);
            hat.transform.localScale = new Vector3(0.6f, 0.2f, 0.2f);
            _hat = hat.GetComponent<Renderer>();

            Head = new GameObject("Head").transform;
            Head.SetParent(transform, false);
            Head.localPosition = Vector3.up * EyeHeight;
        }

        /// <summary>阵营或灵魂状态变化时刷新颜色。</summary>
        public void RefreshLook()
        {
            if (_body == null) return;
            var team = Team;
            var c = TeamUtil.Color(team);
            if (Stats.IsGhost.Value) c = new Color(0.85f, 0.95f, 1f);
            Visuals.Tint(_body, c);
            Visuals.Tint(_hat, Stats.IsGhost.Value ? c : new Color(0.15f, 0.15f, 0.2f));
        }

        Team _lastTeam;

        void Update()
        {
            if (Team != _lastTeam)
            {
                _lastTeam = Team;
                RefreshLook();
            }
            if (!IsOwner || !IsSpawned) return;
            Move();
        }

        void Move()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool blocked = UIManager.GameplayInputBlocked;

            if (!blocked && mouse != null)
            {
                var d = mouse.delta.ReadValue() * GameConfig.MouseSensitivity;
                transform.Rotate(0, d.x, 0);
                _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
                Head.localRotation = Quaternion.Euler(_pitch, 0, 0);
            }

            Vector3 input = Vector3.zero;
            bool sprint = false, jump = false;
            if (!blocked && kb != null)
            {
                if (kb.wKey.isPressed) input.z += 1;
                if (kb.sKey.isPressed) input.z -= 1;
                if (kb.dKey.isPressed) input.x += 1;
                if (kb.aKey.isPressed) input.x -= 1;
                sprint = kb.leftShiftKey.isPressed;
                jump = kb.spaceKey.wasPressedThisFrame;
            }
            input = Vector3.ClampMagnitude(input, 1f);

            bool ghost = Stats.IsGhost.Value;
            float speed = ghost ? GameConfig.GhostSpeed : (sprint ? GameConfig.SprintSpeed : GameConfig.WalkSpeed);
            var move = transform.TransformDirection(input) * speed;

            if (_cc.isGrounded && _yVelocity < 0) _yVelocity = -2f;
            if (jump && _cc.isGrounded) _yVelocity = Mathf.Sqrt(GameConfig.JumpHeight * -2f * GameConfig.Gravity);
            _yVelocity += GameConfig.Gravity * Time.deltaTime;
            move.y = _yVelocity;

            _cc.Move(move * Time.deltaTime);

            // 掉出地图兜底
            if (transform.position.y < -20f)
            {
                _cc.enabled = false;
                transform.position = GameWorld.GetSpawn(Team, 0);
                _cc.enabled = true;
            }
        }
    }
}
