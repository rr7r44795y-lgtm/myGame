using System.Collections.Generic;
using UnityEngine;

namespace MyGame
{
    public enum ItemKind
    {
        Food,       // 长按 3s 食用
        Drink,      // 长按 3s 饮用（默认按食物处理）
        Prop,       // 点按使用，效果等道具案
        Gift,       // 不能使用，只能送人
    }

    public class ItemDef
    {
        public int Id;
        public string Name;
        public string Desc;
        public ItemKind Kind;
        public Color Color;          // 没有美术前用纯色当图标和模型
        public float Hunger;         // 使用后恢复的饱腹
        public float Thirst;         // 使用后恢复的水分
        public bool Giftable;
    }

    /// <summary>
    /// 占位物品表。物资系统的物资清单定了以后替换这里。Id 0 表示空格子，不要占用。
    /// </summary>
    public static class ItemDatabase
    {
        public static readonly List<ItemDef> All = new List<ItemDef>
        {
            new ItemDef { Id = 1, Name = "面包",   Desc = "朴实的面包，能填饱肚子。",     Kind = ItemKind.Food,  Color = new Color(0.85f, 0.65f, 0.35f), Hunger = 35, Giftable = true },
            new ItemDef { Id = 2, Name = "苹果",   Desc = "又脆又甜，顺便解点渴。",       Kind = ItemKind.Food,  Color = new Color(0.9f, 0.2f, 0.2f),   Hunger = 15, Thirst = 10, Giftable = true },
            new ItemDef { Id = 3, Name = "矿泉水", Desc = "一瓶干净的水。",               Kind = ItemKind.Drink, Color = new Color(0.4f, 0.75f, 1f),    Thirst = 40, Giftable = true },
            new ItemDef { Id = 4, Name = "果汁",   Desc = "甜甜的果汁，解渴也顶饿。",     Kind = ItemKind.Drink, Color = new Color(1f, 0.6f, 0.1f),     Hunger = 10, Thirst = 25, Giftable = true },
            new ItemDef { Id = 5, Name = "巧克力", Desc = "热量很高，也很适合送人。",     Kind = ItemKind.Food,  Color = new Color(0.4f, 0.25f, 0.15f), Hunger = 20, Giftable = true },
            new ItemDef { Id = 6, Name = "鲜花",   Desc = "一束鲜花，不能吃，但可以送人。", Kind = ItemKind.Gift,  Color = new Color(1f, 0.5f, 0.8f),     Giftable = true },
            new ItemDef { Id = 7, Name = "小熊玩偶", Desc = "毛茸茸的玩偶。",             Kind = ItemKind.Gift,  Color = new Color(0.75f, 0.55f, 0.4f), Giftable = true },
            new ItemDef { Id = 8, Name = "香水",   Desc = "闻起来像春天。",               Kind = ItemKind.Gift,  Color = new Color(0.7f, 0.5f, 1f),     Giftable = true },
        };

        static Dictionary<int, ItemDef> _byId;

        public static ItemDef Get(int id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<int, ItemDef>();
                foreach (var d in All) _byId[d.Id] = d;
            }
            return _byId.TryGetValue(id, out var def) ? def : null;
        }

        public static bool IsUsable(int id)
        {
            var d = Get(id);
            return d != null && d.Kind != ItemKind.Gift;
        }

        public static bool NeedsHold(int id)
        {
            var d = Get(id);
            return d != null && (d.Kind == ItemKind.Food || d.Kind == ItemKind.Drink);
        }
    }
}
