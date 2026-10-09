using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MyGame
{
    /// <summary>用代码搭 UI Toolkit 界面的小工具，样式在 Resources/MyGame/UI/MyGameUI.uss。</summary>
    public static class UI
    {
        public static VisualElement Box(VisualElement parent, params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        public static Label Text(VisualElement parent, string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        public static Button Btn(VisualElement parent, string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("btn");
            foreach (var c in classes) b.AddToClassList(c);
            parent?.Add(b);
            return b;
        }

        public static TextField Input(VisualElement parent, string label, string value, int maxLength, params string[] classes)
        {
            var t = new TextField(label) { value = value, maxLength = maxLength };
            t.AddToClassList("input");
            foreach (var c in classes) t.AddToClassList(c);
            parent?.Add(t);
            return t;
        }

        public static void Show(VisualElement e, bool show) => e.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        public static bool IsShown(VisualElement e) => e.resolvedStyle.display != DisplayStyle.None && e.style.display != DisplayStyle.None;

        /// <summary>纯色方块当物品图标。</summary>
        public static VisualElement Icon(VisualElement parent, int itemId, params string[] classes)
        {
            var e = Box(parent, "item-icon");
            foreach (var c in classes) e.AddToClassList(c);
            SetIcon(e, itemId);
            return e;
        }

        public static void SetIcon(VisualElement icon, int itemId)
        {
            var def = ItemDatabase.Get(itemId);
            icon.style.backgroundColor = def != null ? def.Color : new Color(0, 0, 0, 0);
        }

        public static string FormatTime(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }
    }

    /// <summary>界面基类：一个根节点，显示时每帧 Tick。</summary>
    public abstract class UIScreen
    {
        public VisualElement Root { get; }

        protected UIScreen(VisualElement parent, string cls)
        {
            Root = UI.Box(parent, "screen", cls);
            UI.Show(Root, false);
        }

        public bool Visible { get; private set; }

        public void SetVisible(bool v)
        {
            if (v == Visible) return;
            Visible = v;
            UI.Show(Root, v);
            if (v) OnShow(); else OnHide();
        }

        protected virtual void OnShow() { }
        protected virtual void OnHide() { }
        public virtual void Tick() { }
    }
}
