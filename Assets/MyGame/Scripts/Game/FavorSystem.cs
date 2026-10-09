using System.Collections.Generic;
using UnityEngine;

namespace MyGame
{
    /// <summary>
    /// 好感度矩阵，只在服务器上存在。Get(a, b) 表示 a 对 b 的好感度，每个方向独立计算。
    /// 规则来自《myGame好感度系统》：A 送礼给 B，B 对 A 的好感度上升。
    /// 送礼加减的具体数值策划案还没定，先用 GameConfig 里的默认值。
    /// </summary>
    public class FavorSystem
    {
        readonly Dictionary<(ulong from, ulong to), int> _values = new Dictionary<(ulong, ulong), int>();

        public int Get(ulong from, ulong to)
        {
            if (from == to) return GameConfig.FavorMax;
            return _values.TryGetValue((from, to), out var v) ? v : GameConfig.FavorInitial;
        }

        public int Add(ulong from, ulong to, int delta)
        {
            if (from == to) return Get(from, to);
            int v = Mathf.Clamp(Get(from, to) + delta, GameConfig.FavorMin, GameConfig.FavorMax);
            _values[(from, to)] = v;
            return v;
        }

        /// <summary>收礼方 receiver 收到 giver 送的 itemId，返回好感度变化量。</summary>
        public int ApplyGift(ulong giver, ulong receiver, int itemId, ItemPreference receiverPrefs)
        {
            int delta = GameConfig.FavorGiftNormal;
            if (receiverPrefs != null)
            {
                if (receiverPrefs.IsLiked(itemId)) delta = GameConfig.FavorGiftLiked;
                else if (receiverPrefs.IsDisliked(itemId)) delta = GameConfig.FavorGiftDisliked;
            }
            int before = Get(receiver, giver);
            int after = Add(receiver, giver, delta);
            return after - before;
        }

        public void RemovePlayer(ulong clientId)
        {
            var dead = new List<(ulong, ulong)>();
            foreach (var k in _values.Keys)
                if (k.from == clientId || k.to == clientId) dead.Add(k);
            foreach (var k in dead) _values.Remove(k);
        }

        public void Clear() => _values.Clear();
    }
}
