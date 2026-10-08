using System;
using UnityEngine;

namespace MyGame.Foundation
{
    [Serializable] public sealed class SupplyPoint
    {
        public SpawnRule rule = new SpawnRule();
        public Vector3 address;
        [NonSerialized] public SupplyPickup current;
    }
    public sealed class SupplySpawner : MonoBehaviour
    {
        public SupplyPoint[] points;
        [Tooltip("Demo default only: wiki leaves X minutes unspecified. Zero disables refresh.")]
        [Min(0)] public float refreshMinutes = 2;
        private SupplyDefinition[] catalog;
        private System.Random random;
        private float elapsed;
        private bool running;
        public void Begin(SupplyDefinition[] items, int seed)
        {
            StopAndClear(); catalog = items; random = new System.Random(seed); elapsed = 0;
            if (points == null) points = new SupplyPoint[0];
            foreach (var point in points) point.rule.Validate();
            running = true; Refresh();
        }
        private void Update()
        {
            if (!running || refreshMinutes <= 0) return;
            elapsed += Time.deltaTime;
            if (elapsed >= refreshMinutes * 60) { elapsed %= refreshMinutes * 60; Refresh(); }
        }
        public void Refresh()
        {
            if (!running) return;
            foreach (var point in points)
            {
                var stack = point.rule.Roll(catalog, random, point.current != null && point.current.gameObject.activeSelf);
                if (stack == null) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = stack.Item.name + " x" + stack.Count;
                go.transform.SetParent(transform, false); go.transform.position = point.address;
                go.transform.localScale = Vector3.one * .45f;
                var renderer = go.GetComponent<Renderer>();
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Material owned = null;
                if (shader != null)
                {
                    owned = new Material(shader); owned.color = ColorFor(stack.Item.type); renderer.sharedMaterial = owned;
                }
                var pickup = go.AddComponent<SupplyPickup>(); pickup.OwnedMaterial = owned; pickup.Initialize(stack); point.current = pickup;
            }
        }
        public void StopAndClear()
        {
            running = false;
            if (points == null) return;
            foreach (var point in points)
                if (point.current != null) { Destroy(point.current.gameObject); point.current = null; }
        }
        public static Color ColorFor(SupplyType type)
        { return type == SupplyType.Food ? new Color(.9f, .6f, .2f) : type == SupplyType.Water ? Color.cyan : type == SupplyType.HighValue ? new Color(.8f, .4f, 1) : new Color(.9f, .3f, .4f); }
    }
}
