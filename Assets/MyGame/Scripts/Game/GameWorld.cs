using System.Collections.Generic;
using UnityEngine;

namespace MyGame
{
    /// <summary>
    /// 局内灰盒地图。每台机器按固定种子在本地生成，不走网络，所以所有人看到的地形完全一样。
    /// 正式地图做好后，把这里换成加载场景，再从场景里读出生点和物资点即可。
    /// </summary>
    public static class GameWorld
    {
        public const float Size = 90f;
        const int Seed = 20260927;

        static GameObject _root;
        static readonly List<Vector3> _redSpawns = new List<Vector3>();
        static readonly List<Vector3> _blueSpawns = new List<Vector3>();
        static readonly List<Vector3> _itemPoints = new List<Vector3>();

        public static bool IsBuilt => _root != null;
        public static IReadOnlyList<Vector3> ItemPoints => _itemPoints;
        public static Vector3 WellPosition { get; private set; }

        public static Vector3 GetSpawn(Team team, int index)
        {
            var list = team == Team.Blue ? _blueSpawns : _redSpawns;
            if (list.Count == 0) return Vector3.up;
            return list[index % list.Count];
        }

        public static void Build()
        {
            if (_root != null) return;
            _root = new GameObject("[GameWorld]");
            _redSpawns.Clear();
            _blueSpawns.Clear();
            _itemPoints.Clear();

            var rng = new System.Random(Seed);

            // 地面
            var ground = Box("地面", new Vector3(0, -0.5f, 0), new Vector3(Size, 1, Size), new Color(0.45f, 0.6f, 0.4f));
            ground.isStatic = true;

            // 四周围墙
            float h = Size / 2f;
            Box("围墙", new Vector3(0, 2, h), new Vector3(Size, 4, 1), new Color(0.55f, 0.55f, 0.6f));
            Box("围墙", new Vector3(0, 2, -h), new Vector3(Size, 4, 1), new Color(0.55f, 0.55f, 0.6f));
            Box("围墙", new Vector3(h, 2, 0), new Vector3(1, 4, Size), new Color(0.55f, 0.55f, 0.6f));
            Box("围墙", new Vector3(-h, 2, 0), new Vector3(1, 4, Size), new Color(0.55f, 0.55f, 0.6f));

            // 中间一条河（视觉），两座桥
            Box("河", new Vector3(0, -0.45f, 0), new Vector3(Size - 2, 1, 6), new Color(0.3f, 0.55f, 0.85f)).GetComponent<Collider>().enabled = false;
            Box("桥", new Vector3(-20, 0.05f, 0), new Vector3(5, 0.3f, 8), new Color(0.55f, 0.4f, 0.25f));
            Box("桥", new Vector3(20, 0.05f, 0), new Vector3(5, 0.3f, 8), new Color(0.55f, 0.4f, 0.25f));

            // 两个阵营的出生区
            Box("红方出生区", new Vector3(0, 0.02f, -h + 8), new Vector3(16, 0.05f, 10), TeamUtil.Color(Team.Red) * 0.8f).GetComponent<Collider>().enabled = false;
            Box("蓝方出生区", new Vector3(0, 0.02f, h - 8), new Vector3(16, 0.05f, 10), TeamUtil.Color(Team.Blue) * 0.8f).GetComponent<Collider>().enabled = false;
            for (int i = 0; i < GameConfig.DefaultMaxPlayers; i++)
            {
                float x = -6f + (i % 5) * 3f;
                float z = (i / 5) * 3f;
                _redSpawns.Add(new Vector3(x, 1.1f, -h + 6 + z));
                _blueSpawns.Add(new Vector3(x, 1.1f, h - 6 - z));
            }

            // 水井（可反复喝水）
            WellPosition = new Vector3(0, 0, 12);
            var well = Cylinder("水井", WellPosition + Vector3.up * 0.5f, new Vector3(2.2f, 0.5f, 2.2f), new Color(0.5f, 0.5f, 0.55f));
            well.AddComponent<WaterSource>();
            var water = Cylinder("井水", WellPosition + Vector3.up * 0.98f, new Vector3(1.8f, 0.02f, 1.8f), new Color(0.3f, 0.55f, 0.85f));
            Object.Destroy(water.GetComponent<Collider>());

            // 散落的房子和箱子当掩体
            var houseColors = new[] { new Color(0.85f, 0.75f, 0.6f), new Color(0.7f, 0.8f, 0.85f), new Color(0.9f, 0.85f, 0.7f) };
            for (int i = 0; i < 14; i++)
            {
                var p = RandomPoint(rng, 8f);
                if (Mathf.Abs(p.z) < 6f || Mathf.Abs(p.z) > h - 14f) continue;
                float w = 4 + (float)rng.NextDouble() * 5;
                float d = 4 + (float)rng.NextDouble() * 5;
                float hh = 3 + (float)rng.NextDouble() * 3;
                Box("房子", new Vector3(p.x, hh / 2, p.z), new Vector3(w, hh, d), houseColors[i % houseColors.Length]);
            }
            for (int i = 0; i < 30; i++)
            {
                var p = RandomPoint(rng, 4f);
                if (Mathf.Abs(p.z) < 4f) continue;
                Box("箱子", new Vector3(p.x, 0.5f, p.z), Vector3.one, new Color(0.6f, 0.45f, 0.3f));
            }

            // 物资点：随机撒，避开河和障碍
            int guard = 0;
            while (_itemPoints.Count < GameConfig.WorldItemCount * 2 && guard++ < 2000)
            {
                var p = RandomPoint(rng, 3f);
                if (Mathf.Abs(p.z) < 4f) continue;
                var probe = new Vector3(p.x, 0.6f, p.z);
                if (Physics.CheckSphere(probe, 0.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                _itemPoints.Add(new Vector3(p.x, 0.35f, p.z));
            }

            // 小地图和光照用的朝向
            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null && sun.type == LightType.Directional) sun.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        public static void Teardown()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }

        static Vector3 RandomPoint(System.Random rng, float margin)
        {
            float r = Size / 2f - margin;
            return new Vector3((float)(rng.NextDouble() * 2 - 1) * r, 0, (float)(rng.NextDouble() * 2 - 1) * r);
        }

        static GameObject Box(string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Setup(go, name, pos, scale, color);
            // 立刻同步物理，后面的 CheckSphere 才能检测到
            Physics.SyncTransforms();
            return go;
        }

        static GameObject Cylinder(string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Setup(go, name, pos, scale, color);
            return go;
        }

        static void Setup(GameObject go, string name, Vector3 pos, Vector3 scale, Color color)
        {
            go.name = name;
            go.transform.SetParent(_root.transform);
            go.transform.position = pos;
            go.transform.localScale = scale;
            Visuals.Tint(go.GetComponent<Renderer>(), color);
        }
    }

    /// <summary>挂在水井上，供射线识别。</summary>
    public class WaterSource : MonoBehaviour { }
}
