using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace MyGame.EditorTools
{
    /// <summary>
    /// 一键生成联机需要的网络预制体（NetworkObject 的哈希必须在编辑器里生成，不能运行时创建）。
    /// 菜单：MyGame / 生成网络预制体。也可以命令行调用 -executeMethod MyGame.EditorTools.MyGameSetup.GenerateAll。
    /// </summary>
    public static class MyGameSetup
    {
        const string Dir = "Assets/MyGame/Resources/MyGame/NetPrefabs";

        [MenuItem("MyGame/生成网络预制体")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(Dir);

            Save("Session", go =>
            {
                go.AddComponent<Session>();
            });

            Save("Player", go =>
            {
                var cc = go.AddComponent<CharacterController>();
                cc.height = 1.8f;
                cc.radius = 0.35f;
                cc.center = new Vector3(0, 0.9f, 0);
                var nt = go.AddComponent<NetworkTransform>();
                nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;   // 本人算移动，同步给其他人
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                nt.SyncRotAngleX = nt.SyncRotAngleZ = false;
                go.AddComponent<PlayerStats>();
                go.AddComponent<PlayerInventory>();
                go.AddComponent<PlayerAvatar>();
                go.AddComponent<PlayerInteraction>();
            });

            Save("WorldItem", go =>
            {
                var col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = Vector3.one * 0.7f;
                go.AddComponent<WorldItem>();
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MyGame] 网络预制体已生成：" + Dir);
        }

        static void Save(string name, System.Action<GameObject> build)
        {
            var path = $"{Dir}/{name}.prefab";
            var go = new GameObject(name);
            try
            {
                go.AddComponent<NetworkObject>();
                build(go);
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            // 重新导入一次，确保 NetworkObject 的 GlobalObjectIdHash 写入资源
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            EditorUtility.SetDirty(asset);
        }

        /// <summary>打一个 Windows 开发包，用于多开联机测试。输出到工程目录下的 Builds/Win。</summary>
        [MenuItem("MyGame/打包 Windows 测试包")]
        public static void BuildWindows()
        {
            var scenes = System.Array.ConvertAll(EditorBuildSettings.scenes, s => s.path);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/Win/MyGame.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            });
            Debug.Log($"[MyGame] 打包结果：{report.summary.result}，{report.summary.totalErrors} 个错误");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        [InitializeOnLoadMethod]
        static void CheckOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists($"{Dir}/Player.prefab"))
                    Debug.LogWarning("[MyGame] 还没有网络预制体，请点菜单「MyGame/生成网络预制体」。");
            };
        }
    }
}
