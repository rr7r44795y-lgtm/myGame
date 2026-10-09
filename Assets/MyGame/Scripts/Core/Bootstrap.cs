using UnityEngine;

namespace MyGame
{
    /// <summary>
    /// 进入任意场景后自动搭好运行环境：网络、UI。所以工程里不需要手动摆放任何物体。
    /// </summary>
    public static class Bootstrap
    {
        public static GameObject Root { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Root != null) return;

            // 同一台电脑开多个客户端测试时，失去焦点的窗口也要继续跑网络
            Application.runInBackground = true;
            Application.targetFrameRate = 120;

            Root = new GameObject("[MyGame]");
            Object.DontDestroyOnLoad(Root);
            Root.AddComponent<NetworkBootstrap>();
            Root.AddComponent<LanDiscovery>();
            Root.AddComponent<UIManager>();
            AutoTest.TryAttach(Root);
        }
    }
}
