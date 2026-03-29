#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Unity.Editor.Example
{
    public class EmbedToonShader
    {
        [MenuItem("Window/Embed Toon Shader")]
        private static void Execute()
        {
            var request = Client.Embed("com.unity.toonshader");
            EditorApplication.update += () =>
            {
                if (!request.IsCompleted)
                {
                    return;
                }

                if (request.Status == StatusCode.Success)
                {
                    Debug.Log("埋め込み完了: " + request.Result.packageId);
                }
                else
                {
                    Debug.LogError("埋め込み失敗: " + request.Error.message);
                }

                EditorApplication.update -= null; // 実際には名前付きデリゲートが必要
            };
        }
    }
}
#endif
