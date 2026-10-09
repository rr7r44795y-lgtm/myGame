namespace MyGame
{
    /// <summary>
    /// 全部可调参数集中在这里。带「默认值」注释的项是策划案没写、先按常见做法定的，
    /// 策划定了以后改这里即可，完整清单见 Assets/MyGame/README.md。
    /// </summary>
    public static class GameConfig
    {
        // ---------- 联机 ----------
        public const ushort GamePort = 7777;
        public const int DiscoveryPort = 47777;          // 局域网房间广播端口
        public const float DiscoveryInterval = 1f;       // 房主广播间隔（秒）
        public const float DiscoveryTimeout = 3.5f;      // 超过这个时间没收到广播就从列表移除
        public const int DefaultMaxPlayers = 10;         // 策划案：demo 仅做 10 人
        public const int RoomCodeLength = 6;

        // ---------- 流程 ----------
        public const float PrepSeconds = 10f;            // 策划案：准备阶段 10s
        public const float GameSeconds = 20f * 60f;      // 策划案：对局 20min
        public const float EndResultSeconds = 6f;        // 默认值：结束后展示结果多久再回房间
        public const int LikeCount = 1;                  // 策划案：喜欢上限 1 下限 1
        public const int DislikeMin = 1;                 // 策划案：厌恶下限 1
        public const int DislikeMax = 3;                 // 策划案：厌恶上限 3

        // ---------- 背包 ----------
        public const int BagSlots = 9;                   // 策划案
        public const int QuickSlots = 3;                 // 策划案：快捷键 1、2、3
        public const int StackMax = 5;                   // 策划案
        public const float FoodHoldSeconds = 3f;         // 策划案：食物长按 3s
        public const float PickupDistance = 3f;          // 默认值
        public const float DropForwardDistance = 1.5f;   // 默认值：丢在身前多远

        // ---------- 角色状态（全部为默认值） ----------
        public const float StatMax = 100f;
        public const float HungerDecayPerSecond = 100f / 360f;   // 6 分钟从满饿到 0
        public const float ThirstDecayPerSecond = 100f / 270f;   // 4.5 分钟从满渴到 0
        public const float WellThirstRestore = 30f;
        public const float WellCooldown = 5f;

        // ---------- 好感度（全部为默认值，见好感度系统待补充） ----------
        public const int FavorInitial = 0;
        public const int FavorMin = 0;
        public const int FavorMax = 100;
        public const int FavorGiftNormal = 10;           // 策划案示例：送礼 +10
        public const int FavorGiftLiked = 30;
        public const int FavorGiftDisliked = -20;

        // ---------- 角色控制 ----------
        public const float WalkSpeed = 4.5f;
        public const float SprintSpeed = 7f;
        public const float JumpHeight = 1.1f;
        public const float Gravity = -20f;
        public const float MouseSensitivity = 0.12f;
        public const float GhostSpeed = 6f;

        // ---------- 地图 ----------
        public const int WorldItemCount = 40;            // 默认值：开局在地图上刷多少个物资
    }
}
