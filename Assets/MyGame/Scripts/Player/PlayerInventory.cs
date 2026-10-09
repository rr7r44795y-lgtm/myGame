using Unity.Netcode;
using UnityEngine;

namespace MyGame
{
    /// <summary>
    /// 背包（9 格）与快捷栏（3 格），同种物品每格最多叠 5 个。
    ///
    /// 策划案里有一处矛盾：背包里点物品是「全量拿出背包，塞到快捷栏」（快捷栏是独立容器），
    /// 快捷栏说明又写「数量堆叠与背包保持同步」。这里按前者实现：两个独立容器，同一套堆叠规则。
    ///
    /// 所有改动都在服务器执行，客户端通过 Rpc 发请求，结果经 NetworkList 同步回来。
    /// </summary>
    public class PlayerInventory : NetworkBehaviour
    {
        public NetworkList<ItemStack> Bag;
        public NetworkList<ItemStack> Quick;

        PlayerStats _stats;

        void Awake()
        {
            Bag = new NetworkList<ItemStack>(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
            Quick = new NetworkList<ItemStack>(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
            _stats = GetComponent<PlayerStats>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            for (int i = 0; i < GameConfig.BagSlots; i++) Bag.Add(ItemStack.Empty);
            for (int i = 0; i < GameConfig.QuickSlots; i++) Quick.Add(ItemStack.Empty);
        }

        Session S => Session.Instance;
        bool CanAct => _stats != null && _stats.Alive && S != null && S.Phase.Value == GamePhase.Playing;

        // ================= 服务器：容器操作 =================

        /// <summary>尽量放进容器：先叠到同种未满的格子，再放空格。返回实际放进去的数量。</summary>
        static int AddTo(NetworkList<ItemStack> list, int itemId, int count)
        {
            int left = count;
            for (int i = 0; i < list.Count && left > 0; i++)
            {
                var s = list[i];
                if (s.ItemId != itemId || s.Count >= GameConfig.StackMax) continue;
                int put = Mathf.Min(left, GameConfig.StackMax - s.Count);
                list[i] = new ItemStack(itemId, s.Count + put);
                left -= put;
            }
            for (int i = 0; i < list.Count && left > 0; i++)
            {
                if (!list[i].IsEmpty) continue;
                int put = Mathf.Min(left, GameConfig.StackMax);
                list[i] = new ItemStack(itemId, put);
                left -= put;
            }
            return count - left;
        }

        public int ServerAddToBag(int itemId, int count) => AddTo(Bag, itemId, count);

        static void Take(NetworkList<ItemStack> list, int index, int count)
        {
            var s = list[index];
            s.Count -= count;
            list[index] = s.Count <= 0 ? ItemStack.Empty : s;
        }

        static bool Valid(NetworkList<ItemStack> list, int index) => index >= 0 && index < list.Count && !list[index].IsEmpty;

        // ================= 客户端请求 =================

        /// <summary>背包里点物品：全量移到快捷栏（放不下的留在背包）。</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void MoveBagToQuickRpc(int bagIndex)
        {
            if (!Valid(Bag, bagIndex)) return;
            var s = Bag[bagIndex];
            int moved = AddTo(Quick, s.ItemId, s.Count);
            if (moved == 0) { S?.ServerToast(OwnerClientId, "快捷栏已满。"); return; }
            Take(Bag, bagIndex, moved);
        }

        /// <summary>快捷栏物品拖回背包。</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void ReturnQuickToBagRpc(int quickIndex)
        {
            if (!Valid(Quick, quickIndex)) return;
            var s = Quick[quickIndex];
            int moved = AddTo(Bag, s.ItemId, s.Count);
            if (moved == 0) { S?.ServerToast(OwnerClientId, "背包已满。"); return; }
            Take(Quick, quickIndex, moved);
        }

        /// <summary>快捷栏物品拖出去：整格丢在地上，别人可以捡。</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void DropQuickRpc(int quickIndex)
        {
            if (!Valid(Quick, quickIndex) || S == null || S.Phase.Value != GamePhase.Playing) return;
            var s = Quick[quickIndex];
            var pos = transform.position + transform.forward * GameConfig.DropForwardDistance;
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore))
                pos = hit.point;
            pos.y += 0.35f;
            S.ServerSpawnWorldItem(s.ItemId, s.Count, pos);
            Quick[quickIndex] = ItemStack.Empty;
        }

        /// <summary>使用快捷栏物品（食物的 3 秒长按在客户端计时，完成后才发这个请求）。</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void UseQuickRpc(int quickIndex)
        {
            if (!CanAct || !Valid(Quick, quickIndex)) return;
            var def = ItemDatabase.Get(Quick[quickIndex].ItemId);
            if (def == null || def.Kind == ItemKind.Gift) return;
            if (def.Kind == ItemKind.Prop)
            {
                S.ServerToast(OwnerClientId, $"{def.Name} 的使用效果等道具案。");
                return;
            }
            if (!_stats.ServerRestore(def.Hunger, def.Thirst))
            {
                S.ServerToast(OwnerClientId, "现在不需要吃这个。");
                return;
            }
            Take(Quick, quickIndex, 1);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void PickupRpc(NetworkObjectReference itemRef)
        {
            if (!CanAct || !itemRef.TryGet(out var no)) return;
            var item = no.GetComponent<WorldItem>();
            if (item == null || !no.IsSpawned) return;
            if (Vector3.Distance(no.transform.position, transform.position) > GameConfig.PickupDistance + 1.5f) return;

            int added = ServerAddToBag(item.ItemId.Value, item.Count.Value);
            if (added == 0) { S.ServerToast(OwnerClientId, "背包已满。"); return; }
            var def = item.Def;
            if (added >= item.Count.Value) no.Despawn(true);
            else item.Count.Value -= added;
            S.ServerToast(OwnerClientId, $"拾取了 {def?.Name} x{added}");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void DrinkWellRpc()
        {
            if (!CanAct) return;
            var well = GameWorld.WellPosition;
            var flat = new Vector3(transform.position.x - well.x, 0, transform.position.z - well.z);
            if (flat.magnitude > GameConfig.PickupDistance + 1.5f) return;
            if (!_stats.ServerTryUseWell()) { S.ServerToast(OwnerClientId, "刚喝过，歇一会儿再喝。"); return; }
            _stats.ServerRestore(0, GameConfig.WellThirstRestore);
            S.ServerToast(OwnerClientId, "你喝了几口井水。");
        }

        /// <summary>
        /// 把快捷栏选中物品送 1 个给目标玩家。送礼玩法策划案还没写，这里是占位实现：
        /// 距离、对象限制、是否可拒收都用默认值，只为让好感度系统能跑起来。
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void GiftRpc(NetworkObjectReference targetRef, int quickIndex)
        {
            if (!CanAct || !Valid(Quick, quickIndex) || !targetRef.TryGet(out var targetNo)) return;
            var target = targetNo.GetComponent<PlayerInventory>();
            var targetStats = targetNo.GetComponent<PlayerStats>();
            if (target == null || target == this || targetStats == null || !targetStats.Alive) return;
            if (Vector3.Distance(targetNo.transform.position, transform.position) > GameConfig.PickupDistance + 1.5f) return;

            var itemId = Quick[quickIndex].ItemId;
            var def = ItemDatabase.Get(itemId);
            if (def == null || !def.Giftable) return;
            if (target.ServerAddToBag(itemId, 1) == 0)
            {
                S.ServerToast(OwnerClientId, "对方的背包已满。");
                return;
            }
            Take(Quick, quickIndex, 1);

            ulong giver = OwnerClientId, receiver = target.OwnerClientId;
            S.Favor.ApplyGift(giver, receiver, itemId, S.ServerGetPrefs(receiver));
            S.ServerToast(giver, $"你把 {def.Name} 送给了 {S.NameOf(receiver)}");
            S.ServerToast(receiver, $"{S.NameOf(giver)} 送给你 {def.Name}");
        }
    }
}
