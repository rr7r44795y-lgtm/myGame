using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MyGame
{
    /// <summary>
    /// 本人的局内交互：看向物体的提示、E 拾取 / 喝水、1-3 切换快捷栏、左键使用（食物长按 3 秒）、G 送礼。
    /// 只在本人机器上运行，所有结果都向服务器发请求。
    /// </summary>
    public class PlayerInteraction : NetworkBehaviour
    {
        public static PlayerInteraction Local { get; private set; }

        public int SelectedQuick { get; private set; }
        /// <summary>0～1，长按使用的进度，没在长按时为 0。</summary>
        public float HoldProgress { get; private set; }
        /// <summary>准星处的交互提示，没有则为 null。</summary>
        public string Prompt { get; private set; }

        PlayerAvatar _avatar;
        float _holdTime;
        int _holdSlot = -1;

        void Awake() => _avatar = GetComponent<PlayerAvatar>();

        public void Select(int index) => SelectedQuick = Mathf.Clamp(index, 0, GameConfig.QuickSlots - 1);

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
        }

        void Update()
        {
            if (!IsOwner || !IsSpawned) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // 快捷栏选择：界面打开时也允许切换
            if (kb.digit1Key.wasPressedThisFrame) SelectedQuick = 0;
            if (kb.digit2Key.wasPressedThisFrame) SelectedQuick = 1;
            if (kb.digit3Key.wasPressedThisFrame) SelectedQuick = 2;

            Prompt = null;
            if (UIManager.GameplayInputBlocked || _avatar.Stats.IsGhost.Value)
            {
                CancelHold();
                if (_avatar.Stats.IsGhost.Value) Prompt = "你已经变成灵魂";
                return;
            }

            LookAndInteract(kb);
            HandleUse(mouse);
        }

        void LookAndInteract(Keyboard kb)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, GameConfig.PickupDistance, ~0, QueryTriggerInteraction.Collide)) return;

            var item = hit.collider.GetComponentInParent<WorldItem>();
            if (item != null && item.IsSpawned)
            {
                Prompt = $"[E] 拾取 {item.Label}";
                if (kb.eKey.wasPressedThisFrame) _avatar.Inventory.PickupRpc(item.NetworkObject);
                return;
            }

            if (hit.collider.GetComponentInParent<WaterSource>() != null)
            {
                Prompt = "[E] 喝水";
                if (kb.eKey.wasPressedThisFrame) _avatar.Inventory.DrinkWellRpc();
                return;
            }

            var other = hit.collider.GetComponentInParent<PlayerAvatar>();
            if (other != null && other != _avatar && other.Stats.Alive)
            {
                var stack = Selected;
                var def = stack.IsEmpty ? null : ItemDatabase.Get(stack.ItemId);
                Prompt = def != null && def.Giftable
                    ? $"[G] 把 {def.Name} 送给 {other.DisplayName}"
                    : $"{other.DisplayName}（选中快捷栏里的物品后可以按 G 送礼）";
                if (def != null && def.Giftable && kb.gKey.wasPressedThisFrame)
                    _avatar.Inventory.GiftRpc(other.NetworkObject, SelectedQuick);
            }
        }

        public ItemStack Selected
        {
            get
            {
                var q = _avatar.Inventory.Quick;
                return q != null && SelectedQuick < q.Count ? q[SelectedQuick] : ItemStack.Empty;
            }
        }

        void HandleUse(Mouse mouse)
        {
            var stack = Selected;
            if (stack.IsEmpty) { CancelHold(); return; }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                var def = ItemDatabase.Get(stack.ItemId);
                if (def == null) return;
                if (def.Kind == ItemKind.Gift)
                {
                    UIManager.Instance?.ShowToast($"{def.Name} 不能使用，可以对着别人按 G 送出去。");
                    return;
                }
                if (!ItemDatabase.NeedsHold(stack.ItemId))
                {
                    _avatar.Inventory.UseQuickRpc(SelectedQuick);   // 道具点按
                    return;
                }
                _holdSlot = SelectedQuick;
                _holdTime = 0f;
            }

            if (_holdSlot >= 0)
            {
                // 松手、切换格子都会打断
                if (!mouse.leftButton.isPressed || _holdSlot != SelectedQuick)
                {
                    CancelHold();
                    return;
                }
                _holdTime += Time.deltaTime;
                HoldProgress = Mathf.Clamp01(_holdTime / GameConfig.FoodHoldSeconds);
                if (_holdTime >= GameConfig.FoodHoldSeconds)
                {
                    _avatar.Inventory.UseQuickRpc(_holdSlot);
                    CancelHold();
                }
            }
        }

        void CancelHold()
        {
            _holdSlot = -1;
            _holdTime = 0f;
            HoldProgress = 0f;
        }
    }
}
