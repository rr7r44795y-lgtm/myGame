using System;
using Unity.Collections;
using Unity.Netcode;

namespace MyGame
{
    public enum GamePhase : byte
    {
        Room,       // 房间等人
        Prep,       // 准备阶段：选喜欢 / 厌恶的物品
        Playing,    // 对局中
        Ended,      // 对局结束，展示结果
    }

    public enum Team : byte
    {
        None = 0,
        Red = 1,
        Blue = 2,
    }

    public struct PlayerEntry : INetworkSerializable, IEquatable<PlayerEntry>
    {
        public ulong ClientId;
        public FixedString64Bytes Name;
        public bool Ready;
        public Team Team;
        public bool Alive;
        public bool PrepConfirmed;
        public bool MicOn;           // 开麦系统接入前恒为 false

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ClientId);
            s.SerializeValue(ref Name);
            s.SerializeValue(ref Ready);
            s.SerializeValue(ref Team);
            s.SerializeValue(ref Alive);
            s.SerializeValue(ref PrepConfirmed);
            s.SerializeValue(ref MicOn);
        }

        public bool Equals(PlayerEntry o) =>
            ClientId == o.ClientId && Name.Equals(o.Name) && Ready == o.Ready && Team == o.Team &&
            Alive == o.Alive && PrepConfirmed == o.PrepConfirmed && MicOn == o.MicOn;
    }

    /// <summary>一名玩家在准备阶段选的喜欢 / 厌恶物品。只存在服务器和本人那里，不公开给其他人。</summary>
    public class ItemPreference
    {
        public int Like;
        public int[] Dislikes = Array.Empty<int>();

        public bool IsLiked(int itemId) => Like == itemId;
        public bool IsDisliked(int itemId) => Array.IndexOf(Dislikes, itemId) >= 0;
    }

    public static class TeamUtil
    {
        public static string Label(Team t) => t switch
        {
            Team.Red => "红",
            Team.Blue => "蓝",
            _ => "无",
        };

        public static UnityEngine.Color Color(Team t) => t switch
        {
            Team.Red => new UnityEngine.Color(0.9f, 0.25f, 0.25f),
            Team.Blue => new UnityEngine.Color(0.25f, 0.5f, 0.95f),
            _ => new UnityEngine.Color(0.8f, 0.8f, 0.8f),
        };
    }
}
