using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit file-request checks in the existing editor, in a disposable preview scene.</summary>
[InitializeOnLoad]
public static class SpaceMaterialVerification
{
    private const string Request = "Temp/material-verification.request";
    private const string Result = "Temp/material-verification-result.json";
    private static bool running;

    static SpaceMaterialVerification() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        running = true;
        EditorApplication.delayCall += Verify;
    }

    [Serializable] private sealed class Report
    { public bool passed; public int materials; public string detail; public string preview; }

    public static void Verify()
    {
        var report = new Report();
        Scene scene = default;
        try
        {
            scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
            SpaceLocationGenerator generator = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<SpaceLocationGenerator>(out var found)) generator = found;
            Require(generator != null, "Generator missing in scene");
            var original = SpaceLocationLayout.Generate("0012345", 7);
            for (int code = 0; code < AsteroidMaterialLibrary.Count; code++)
            {
                string seed = code.ToString("D2") + "12345";
                generator.Generate(seed);
                var material = AsteroidMaterialLibrary.Get(code);
                Require(generator.MaterialCode == code, "Material code mismatch: " + seed);
                Require(material.shader != null && !ShaderUtil.ShaderHasError(material.shader), "Shader compilation error: " + code);
                foreach (string map in new[] { "_BaseMap", "_SurfaceMap", "_EmissionMap" })
                    Require(material.GetTexture(map) != null, "Missing texture " + map + " for " + code);
                var markers = generator.GetComponentsInChildren<SpaceObjectMarker>();
                Require(markers.Length == generator.AsteroidCount, "Stale or missing markers");
                Require(markers.Length == original.Length, "Material changed count");
                for (int i = 0; i < markers.Length; i++)
                {
                    Require(markers[i].DisplayName == generator.MaterialName + "-" + (i + 1), "Mixed material or ordinal");
                    Require(generator.Layout[i].Position.Equals(original[i].Position), "Prefix changed geometry");
                    foreach (var renderer in markers[i].GetComponentsInChildren<MeshRenderer>())
                    {
                        foreach (var slot in renderer.sharedMaterials) Require(slot == material, "Material not applied to all slots");
                        var block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block);
                        Require(block.GetFloat("_TextureScale") > 0, "Missing triplanar scale");
                        Require(block.GetVector("_MappingSize").x > 0, "Missing mesh mapping bounds");
                    }
                    Require(Vector3.Distance(markers[i].WorldPosition, markers[i].transform.position) < 0.1f, "Marker off centre");
                }
                report.materials++;
                report.detail = "Checking library materials: " + report.materials + "/" + AsteroidMaterialLibrary.Count;
                File.WriteAllText(Result, JsonUtility.ToJson(report, true));
            }
            generator.Generate("9912345");
            Require(generator.MaterialCode == 0 && generator.MaterialName == "Камень", "Unknown code fallback broken");
            report.preview = RenderPreview();
            report.passed = true;
            report.detail = "51 materials loaded and applied; textures, shaders, triplanar blocks, names, markers, suffix geometry and fallback passed.";
        }
        catch (Exception exception) { report.detail = exception.ToString(); Debug.LogException(exception); }
        finally
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllText(Result, JsonUtility.ToJson(report, true));
            running = false;
        }
    }

    private static string RenderPreview()
    {
        var preview = new PreviewRenderUtility();
        GameObject model = null;
        Texture2D copy = null;
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ассеты/Rock_1.obj");
            Require(source != null, "Rock model failed to import");
            model = UnityEngine.Object.Instantiate(source);
            preview.AddSingleGO(model);
            model.transform.position = Vector3.zero;
            model.transform.rotation = Quaternion.Euler(15, 25, 0);
            AsteroidMaterialLibrary.Apply(model, 2);
            Bounds bounds = model.GetComponentInChildren<Renderer>().bounds;
            float radius = bounds.extents.magnitude;
            preview.camera.transform.position = bounds.center + new Vector3(0, 0, -radius * 3);
            preview.camera.transform.LookAt(bounds.center);
            preview.camera.nearClipPlane = Mathf.Max(0.01f, radius * 0.01f);
            preview.camera.farClipPlane = radius * 10;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);
            preview.lights[0].intensity = 3;
            preview.lights[0].transform.rotation = Quaternion.Euler(40, 35, 0);
            preview.lights[1].intensity = 1.5f;
            preview.lights[1].transform.rotation = Quaternion.Euler(-25, -120, 0);
            preview.BeginPreview(new Rect(0, 0, 512, 512), GUIStyle.none);
            preview.Render(true);
            var texture = preview.EndPreview();
            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(512, 512, 0);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(512, 512, TextureFormat.RGB24, false);
                copy.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                copy.Apply();
                Require(!ShaderUtil.ShaderHasError(AsteroidMaterialLibrary.Get(2).shader), "Shader failed during render");
                int magenta = 0, visible = 0;
                foreach (var pixel in copy.GetPixels())
                {
                    if (pixel.r > 0.8f && pixel.g < 0.15f && pixel.b > 0.8f) magenta++;
                    if (pixel.r > 0.12f || pixel.g > 0.12f || pixel.b > 0.12f) visible++;
                }
                Require(magenta < 1000, "Render contains shader-error pink pixels");
                Require(visible > 1000, "Render is blank or asteroid is unlit");
                string path = "Temp/material-copper-preview.png";
                File.WriteAllBytes(path, copy.EncodeToPNG());
                return path;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
        }
        finally
        {
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            preview.Cleanup();
        }
    }

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
