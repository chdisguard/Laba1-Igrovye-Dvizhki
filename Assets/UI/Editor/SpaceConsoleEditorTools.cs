using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

public static class SpaceConsoleEditorTools
{
    [MenuItem("Tools/Space Console/Retry Script Compilation")]
    private static void RetryScriptCompilation()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play Mode before retrying script compilation.");
            return;
        }
        AssetDatabase.Refresh();
        CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
        Debug.Log("Requested a clean script compilation without restarting the Unity Editor.");
    }
}
