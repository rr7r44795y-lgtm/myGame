using System;
using Unity.Netcode;

namespace MyGame
{
    /// <summary>背包或快捷栏的一格。ItemId 为 0 表示空格。</summary>
    public struct ItemStack : INetworkSerializable, IEquatable<ItemStack>
    {
        public int ItemId;
        public int Count;

        public bool IsEmpty => ItemId == 0 || Count <= 0;
        public static ItemStack Empty => default;

        public ItemStack(int itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ItemId);
            s.SerializeValue(ref Count);
        }

        public bool Equals(ItemStack o) => ItemId == o.ItemId && Count == o.Count;
    }
}
