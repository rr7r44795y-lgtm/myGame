using System;
using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace MyGame
{
    /// <summary>
    /// 联机自动测试，只在命令行带 -autotest host / -autotest client 时启用，正常游玩不会运行。
    /// 结果以 [AUTOTEST] 开头写进日志，房主等待客户端完成移动和拾取后判定通过。
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        string _role;
        float _deadline;

        public static void TryAttach(GameObject root)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autotest");
            if (i < 0 || i + 1 >= args.Length) return;
            root.AddComponent<AutoTest>()._role = args[i + 1];
        }

        IEnumerator Start()
        {
            _deadline = Time.realtimeSinceStartup + 90f;
            Log($"角色 {_role} 启动");
            yield return new WaitUntil(() => NetworkBootstrap.Instance != null && NetworkBootstrap.Instance.Manager != null);
            yield return _role == "host" ? Host() : Client();
        }

        void Update()
        {
            if (Time.realtimeSinceStartup > _deadline) Finish(false, "超时");
        }

        IEnumerator Host()
        {
            var boot = NetworkBootstrap.Instance;
            if (!boot.CreateRoom("自动测试房", GameConfig.DefaultMaxPlayers, "房主")) { Finish(false, "建房失败"); yield break; }
            yield return new WaitUntil(() => Session.Instance != null && Session.Instance.Players.Count == 2);
            var s = Session.Instance;
            Log($"客户端已加入：{s.NameOf(s.Players[1].ClientId)}");

            yield return new WaitUntil(() => s.AllOthersReady());
            Log("客户端已准备，开始游戏");
            s.StartGameRpc();

            yield return new WaitUntil(() => s.Phase.Value == GamePhase.Prep);
            s.SubmitPrefsRpc(2, new[] { 7 });
            yield return new WaitUntil(() => s.Phase.Value == GamePhase.Playing);
            yield return new WaitUntil(() => PlayerAvatar.All.Count == 2);
            var teams = "";
            foreach (var p in s.Players) teams += $"{p.Name}={TeamUtil.Label(p.Team)} ";
            Log($"两名角色已生成，阵营：{teams}");

            var client = PlayerAvatar.All.First(a => !a.IsOwner);
            var spawnPos = client.transform.position;

            // 等客户端移动（Owner 权威的位置同步）和拾取（服务器权威的背包）
            yield return new WaitUntil(() => Vector3.Distance(client.transform.position, spawnPos) > 3f);
            Log($"房主看到客户端移动了 {Vector3.Distance(client.transform.position, spawnPos):F1} 米");
            yield return new WaitUntil(() => client.Inventory.Bag.Count > 0 && !client.Inventory.Bag[0].IsEmpty);
            Log($"客户端背包：{ItemDatabase.Get(client.Inventory.Bag[0].ItemId)?.Name} x{client.Inventory.Bag[0].Count}");

            // 等客户端送礼，检查好感度方向：房主收到礼物 → 房主对客户端的好感度变化
            var hostAvatar = PlayerAvatar.Local;
            yield return new WaitUntil(() => !hostAvatar.Inventory.Bag[0].IsEmpty);
            Log($"房主收到礼物：{ItemDatabase.Get(hostAvatar.Inventory.Bag[0].ItemId)?.Name}");
            Log($"房主对客户端的好感度：{s.Favor.Get(NetworkManager.ServerClientId, client.OwnerClientId)}，客户端对房主：{s.Favor.Get(client.OwnerClientId, NetworkManager.ServerClientId)}");

            yield return new WaitForSeconds(2f);
            Finish(true, "房主侧全部通过");
        }

        IEnumerator Client()
        {
            var boot = NetworkBootstrap.Instance;
            LanDiscovery.Instance.StartListening();
            RoomAdvert room = null;
            yield return new WaitUntil(() => (room = LanDiscovery.Instance.Rooms.FirstOrDefault()) != null);
            Log($"局域网发现房间：{room.name} 代码 {room.code} 地址 {room.Address}");
            LanDiscovery.Instance.StopListening();

            if (!boot.JoinRoom(room.Address, room.port, "客户端")) { Finish(false, "加入失败"); yield break; }
            yield return new WaitUntil(() => Session.Instance != null && Session.Instance.IsSpawned && Session.Instance.Players.Count == 2);
            var s = Session.Instance;
            Log($"已进入房间：{s.RoomName.Value}，玩家 {s.Players.Count} 人");

            s.SetReadyRpc(true);
            yield return new WaitUntil(() => s.Phase.Value == GamePhase.Prep);
            Log("收到准备阶段");
            s.SubmitPrefsRpc(1, new[] { 6, 8 });
            yield return new WaitUntil(() => s.Phase.Value == GamePhase.Playing && PlayerAvatar.Local != null && PlayerAvatar.All.Count == 2);
            var me = PlayerAvatar.Local;
            Log($"对局开始，我的阵营 {TeamUtil.Label(me.Team)}，喜欢 {ItemDatabase.Get(s.LocalPrefs?.Like ?? 0)?.Name}");

            yield return new WaitUntil(() => FindObjectsByType<WorldItem>(FindObjectsSortMode.None).Length > 0);
            var item = FindObjectsByType<WorldItem>(FindObjectsSortMode.None).OrderBy(w => Vector3.Distance(w.transform.position, me.transform.position)).First();
            Log($"客户端看到 {FindObjectsByType<WorldItem>(FindObjectsSortMode.None).Length} 个物资，去拾取 {item.Label}");

            // 走过去（用 CharacterController 真实移动，验证位置同步）
            var cc = me.GetComponent<CharacterController>();
            var target = item.transform.position;
            float walkEnd = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < walkEnd)
            {
                var to = target - me.transform.position;
                to.y = 0;
                if (to.magnitude < 1.2f) break;
                var step = to.normalized * GameConfig.SprintSpeed * Time.deltaTime;
                if (!cc.isGrounded) step.y = -5f * Time.deltaTime;
                cc.Move(step);
                yield return null;
            }
            if (Vector3.Distance(me.transform.position, target) > 3f)
            {
                // 被障碍挡住就直接传过去
                cc.enabled = false;
                me.transform.position = target + Vector3.back;
                cc.enabled = true;
                yield return null;
            }
            me.Inventory.PickupRpc(item.NetworkObject);
            yield return new WaitUntil(() => !me.Inventory.Bag[0].IsEmpty);
            Log($"拾取成功，背包：{ItemDatabase.Get(me.Inventory.Bag[0].ItemId)?.Name} x{me.Inventory.Bag[0].Count}");

            // 送礼给房主：先放进快捷栏，再走到房主身边
            me.Inventory.MoveBagToQuickRpc(0);
            yield return new WaitUntil(() => !me.Inventory.Quick[0].IsEmpty);
            var host = PlayerAvatar.All.First(a => !a.IsOwner);
            cc.enabled = false;
            me.transform.position = host.transform.position + host.transform.forward * 1.5f;
            cc.enabled = true;
            yield return new WaitForSeconds(0.5f);
            me.Inventory.GiftRpc(host.NetworkObject, 0);
            yield return new WaitForSeconds(3f);
            Finish(true, "客户端侧全部通过");
        }

        bool _done;

        void Finish(bool ok, string msg)
        {
            if (_done) return;
            _done = true;
            Log((ok ? "PASS " : "FAIL ") + msg);
            NetworkBootstrap.Instance?.LeaveOnPurpose();
            Application.Quit(ok ? 0 : 1);
        }

        static void Log(string msg) => Debug.Log("[AUTOTEST] " + msg);
    }
}
