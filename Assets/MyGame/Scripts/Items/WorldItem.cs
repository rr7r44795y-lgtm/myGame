using Unity.Netcode;
using UnityEngine;

namespace MyGame
{
    /// <summary>地面上可拾取的物资。位置在生成时同步，之后不会移动。</summary>
    public class WorldItem : NetworkBehaviour
    {
        public readonly NetworkVariable<int> ItemId = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Count = new NetworkVariable<int>(1);

        Transform _visual;
        float _phase;
        int _initId, _initCount;

        public ItemDef Def => ItemDatabase.Get(ItemId.Value);

        /// <summary>服务器生成前调用；网络变量要等生成时再写入。</summary>
        public void ServerInit(int itemId, int count)
        {
            _initId = itemId;
            _initCount = count;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && _initId != 0)
            {
                ItemId.Value = _initId;
                Count.Value = _initCount;
            }
            if (_visual == null)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(cube.GetComponent<Collider>());
                cube.name = "Visual";
                cube.transform.SetParent(transform, false);
                cube.transform.localScale = Vector3.one * 0.45f;
                _visual = cube.transform;
            }
            _phase = Random.value * 10f;
            ItemId.OnValueChanged += OnItemChanged;
            OnItemChanged(0, ItemId.Value);
        }

        public override void OnNetworkDespawn()
        {
            ItemId.OnValueChanged -= OnItemChanged;
        }

        void OnItemChanged(int _, int id)
        {
            var def = ItemDatabase.Get(id);
            if (def != null && _visual != null) Visuals.Tint(_visual.GetComponent<Renderer>(), def.Color);
        }

        void Update()
        {
            if (_visual == null) return;
            _visual.localRotation = Quaternion.Euler(20, (Time.time * 60f + _phase * 36f) % 360f, 20);
            _visual.localPosition = Vector3.up * (Mathf.Sin(Time.time * 2f + _phase) * 0.08f);
        }

        public string Label
        {
            get
            {
                var def = Def;
                if (def == null) return "";
                return Count.Value > 1 ? $"{def.Name} x{Count.Value}" : def.Name;
            }
        }
    }
}
