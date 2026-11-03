using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Strategy.EditorTools
{
    /// <summary>Creates the playable scene. Runs automatically once, or from the Strategy menu.</summary>
    public static class SceneSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Strategy/Rebuild Main Scene")]
        public static void Build()
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(16, 22, 26, 255);
            cam.transform.rotation = Quaternion.Euler(30, 45, 0);
            cam.gameObject.AddComponent<CameraRig>();

            new GameObject("Game").AddComponent<GameManager>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            PlayerSettings.productName = "Keep";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            Debug.Log("Strategy: Main scene created at " + ScenePath);
        }

        [InitializeOnLoadMethod]
        static void EnsureScene()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                if (System.IO.File.Exists(ScenePath)) return;
                Build();
                EditorSceneManager.OpenScene(ScenePath);
            };
        }
    }
}
