using Unity.Netcode;
using UnityEngine;

namespace MyGame
{
    /// <summary>
    /// 角色状态：饱腹、水分，降到 0 变成灵魂。下降速度等数值是默认值，见角色状态系统待补充。
    /// 服务器按帧计算精确值，只在整数变化时同步，节省流量。
    /// </summary>
    public class PlayerStats : NetworkBehaviour
    {
        public readonly NetworkVariable<int> Hunger = new NetworkVariable<int>((int)GameConfig.StatMax);
        public readonly NetworkVariable<int> Thirst = new NetworkVariable<int>((int)GameConfig.StatMax);
        public readonly NetworkVariable<bool> IsGhost = new NetworkVariable<bool>();

        float _hunger = GameConfig.StatMax;
        float _thirst = GameConfig.StatMax;
        float _wellReadyTime;

        public bool Alive => !IsGhost.Value;

        void Update()
        {
            if (!IsServer || !IsSpawned || IsGhost.Value) return;
            var session = Session.Instance;
            if (session == null || session.Phase.Value != GamePhase.Playing) return;

            _hunger -= GameConfig.HungerDecayPerSecond * Time.deltaTime;
            _thirst -= GameConfig.ThirstDecayPerSecond * Time.deltaTime;
            Push();

            if (_hunger <= 0f || _thirst <= 0f)
            {
                IsGhost.Value = true;
                session.ServerMarkDead(OwnerClientId);
                session.ServerToast(OwnerClientId, _hunger <= 0f ? "你饿倒了，变成了灵魂。" : "你渴倒了，变成了灵魂。");
            }
        }

        /// <summary>服务器：恢复状态，返回是否有实际效果。</summary>
        public bool ServerRestore(float hunger, float thirst)
        {
            if (IsGhost.Value) return false;
            bool changed = (hunger > 0 && _hunger < GameConfig.StatMax) || (thirst > 0 && _thirst < GameConfig.StatMax);
            _hunger = Mathf.Min(GameConfig.StatMax, _hunger + hunger);
            _thirst = Mathf.Min(GameConfig.StatMax, _thirst + thirst);
            Push();
            return changed;
        }

        /// <summary>服务器：水井冷却检查。</summary>
        public bool ServerTryUseWell()
        {
            if (Time.time < _wellReadyTime) return false;
            _wellReadyTime = Time.time + GameConfig.WellCooldown;
            return true;
        }

        void Push()
        {
            int h = Mathf.CeilToInt(Mathf.Max(0, _hunger));
            int t = Mathf.CeilToInt(Mathf.Max(0, _thirst));
            if (Hunger.Value != h) Hunger.Value = h;
            if (Thirst.Value != t) Thirst.Value = t;
        }
    }
}
