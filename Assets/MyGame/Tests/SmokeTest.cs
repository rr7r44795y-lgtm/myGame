using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace MyGame.Tests
{
    /// <summary>
    /// 冒烟测试：单人建房 → 开局 → 准备阶段提交喜好 → 进入对局 → 捡物资 → 放进快捷栏 → 丢弃 → 离开房间。
    /// 在 Test Runner 的 PlayMode 页签里运行。
    /// </summary>
    public class SmokeTest
    {
        static IEnumerator WaitUntil(System.Func<bool> cond, float timeout, string what)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!cond())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("超时：" + what);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator HostFlow()
        {
            yield return WaitUntil(() => NetworkBootstrap.Instance != null && NetworkBootstrap.Instance.Manager != null, 5, "网络模块初始化");
            var boot = NetworkBootstrap.Instance;

            Assert.IsTrue(boot.CreateRoom("测试房间", GameConfig.DefaultMaxPlayers, "测试员"), "建房");
            yield return WaitUntil(() => Session.Instance != null && Session.Instance.IsSpawned && Session.Instance.Players.Count == 1, 5, "房间生成");
            var s = Session.Instance;
            Assert.AreEqual("测试房间", s.RoomName.Value.ToString());
            Assert.AreEqual(GameConfig.RoomCodeLength, s.RoomCode.Value.Length);

            // 只有房主一个人时可以直接开始
            s.StartGameRpc();
            yield return WaitUntil(() => s.Phase.Value == GamePhase.Prep, 5, "进入准备阶段");
            Assert.IsTrue(GameWorld.IsBuilt, "准备阶段应生成地图");

            s.SubmitPrefsRpc(1, new[] { 6 });
            yield return WaitUntil(() => s.Phase.Value == GamePhase.Playing, 5, "全员确认后进入对局");
            yield return WaitUntil(() => PlayerAvatar.Local != null, 5, "生成本人角色");

            var me = PlayerAvatar.Local;
            Assert.AreNotEqual(Team.None, s.TeamOf(NetworkManager.Singleton.LocalClientId), "应分配阵营");
            Assert.IsNotNull(s.LocalPrefs, "应收到自己的喜好");
            Assert.AreEqual(1, s.LocalPrefs.Like);

            var items = Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None);
            Assert.Greater(items.Length, 0, "地图上应有物资");

            // 走到物资旁边捡起来
            var item = items[0];
            int itemId = item.ItemId.Value, count = item.Count.Value;
            var cc = me.GetComponent<CharacterController>();
            cc.enabled = false;
            me.transform.position = item.transform.position + Vector3.back;
            cc.enabled = true;
            yield return null;
            me.Inventory.PickupRpc(item.NetworkObject);
            yield return WaitUntil(() => me.Inventory.Bag[0].ItemId == itemId, 3, "拾取进背包");
            Assert.AreEqual(count, me.Inventory.Bag[0].Count);

            // 背包 → 快捷栏（全量）
            me.Inventory.MoveBagToQuickRpc(0);
            yield return WaitUntil(() => me.Inventory.Quick[0].ItemId == itemId, 3, "移到快捷栏");
            Assert.IsTrue(me.Inventory.Bag[0].IsEmpty, "背包里应全部移走");

            // 丢弃：快捷栏清空，地上多一个物资
            int before = Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None).Length;
            me.Inventory.DropQuickRpc(0);
            yield return WaitUntil(() => me.Inventory.Quick[0].IsEmpty, 3, "丢弃");
            Assert.AreEqual(before + 1, Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None).Length);

            // 好感度：自己对自己不计算，别人初始 0
            Assert.AreEqual(GameConfig.FavorInitial, s.Favor.Get(0, 99));

            boot.LeaveOnPurpose();
            yield return WaitUntil(() => !boot.IsOnline, 5, "离开房间");
            yield return null;
            Assert.IsFalse(GameWorld.IsBuilt, "离开后应清理地图");
        }
    }
}
