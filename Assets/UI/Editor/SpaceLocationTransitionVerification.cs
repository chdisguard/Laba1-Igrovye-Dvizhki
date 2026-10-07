using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SpaceLocationTransitionVerification
{
    private const string Request = "Tools/Verification/location-transition.request";
    private const string Result = "Tools/Verification/location-transition-result.json";
    private static double nextPoll;
    static SpaceLocationTransitionVerification() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
            || !File.Exists(Request)) return;
        File.Delete(Request);
        EditorApplication.delayCall += Verify;
    }
    [Serializable] private sealed class Report { public bool passed; public string detail; }

    public static void Verify()
    {
        Scene preview = default;
        SpaceLocationManager manager = null;
        var report = new Report();
        var originalSky = RenderSettings.skybox;
        var mode = RenderSettings.ambientMode;
        var sky = RenderSettings.ambientSkyColor;
        var equator = RenderSettings.ambientEquatorColor;
        var ground = RenderSettings.ambientGroundColor;
        float intensity = RenderSettings.ambientIntensity;
        try
        {
            SpaceSkyboxImport.Configure();
            preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
            SpaceLocationGenerator generator = null;
            GameObject outpost = null;
            foreach (var root in preview.GetRootGameObjects())
            {
                if (root.TryGetComponent<SpaceLocationManager>(out var found)) manager = found;
                if (root.TryGetComponent<SpaceLocationGenerator>(out var gen)) generator = gen;
                if (root.GetComponentInChildren<SpaceObjectMarker>(true) != null) outpost = root;
            }
            Require(manager != null && generator != null && outpost != null, "Scene references missing");
            // Additional authored asteroid checks automatic cleanup, including objects without markers.
            var authored = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ассеты/Rock_1.obj"));
            SceneManager.MoveGameObjectToScene(authored, preview);
            authored.name = "Authored asteroid cleanup check";
            manager.CaptureSample();
            generator.Generate(generator.Seed);
            Require(outpost.activeSelf, "Sample outpost missing initially");
            var ship = generator.Ship;
            var shipPosition = ship.position;
            var originalLocation = generator.GetComponentsInChildren<SpaceObjectMarker>()[0].gameObject;
            var skies = new System.Collections.Generic.HashSet<Material>();
            for (int index = 0; index < 3; index++)
            {
                string seed = null;
                for (int suffix = 0; suffix < 100; suffix++)
                    if (SpaceLocationManager.SkyIndex("02" + suffix, 3) == index) { seed = "02" + suffix; break; }
                manager.ChangeLocation(seed);
                Require(!manager.IsSampleLocation, "Transition did not leave sample");
                Require(!outpost.activeSelf && !authored.activeSelf, "Sample objects still visible");
                Require(ship.gameObject.activeInHierarchy && ship.position == shipPosition, "Ship removed or moved during transition");
                Require(RenderSettings.skybox != originalSky && RenderSettings.skybox != null, "Sample skybox reused");
                Require(!ShaderUtil.ShaderHasError(RenderSettings.skybox.shader), "Skybox shader error");
                Require(RenderSettings.skybox.shader.name == "Skybox/6 Sided", "Wrong cube sky shader");
                foreach (var face in new[] { "_FrontTex", "_BackTex", "_LeftTex", "_RightTex", "_UpTex", "_DownTex" })
                    Require(RenderSettings.skybox.GetTexture(face) != null, "Missing sky face " + face);
                Require(skies.Add(RenderSettings.skybox), "Sky presets are not distinct");
                var markers = generator.GetComponentsInChildren<SpaceObjectMarker>();
                Require(markers.Length == generator.AsteroidCount, "Previous asteroids remain");
                foreach (var marker in markers) Require(marker.DisplayName.StartsWith("Медь-"), "Wrong deposit material");
                Require(originalLocation == null, "Old generated location not destroyed in edit mode");
                originalLocation = markers[0].gameObject;
            }
            var currentSky = RenderSettings.skybox;
            try { manager.ChangeLocation("invalid"); throw new Exception("Invalid seed accepted"); }
            catch (ArgumentException) { }
            Require(RenderSettings.skybox == currentSky && originalLocation != null, "Invalid seed damaged current location");
            manager.ReturnToSampleScene();
            Require(manager.IsSampleLocation && outpost.activeSelf && authored.activeSelf, "Sample objects did not return");
            Require(RenderSettings.skybox == originalSky, "Original skybox not restored");
            Require(ship.position == shipPosition, "Sample ship position not restored");
            Require(originalLocation == null, "Deposit objects remain after return");
            report.passed = true;
            report.detail = "PASS: sample sky preserved; 3 distinct skies; outpost and authored/generated asteroids removed; ship preserved; invalid seed safe; sample return restores sky, objects and centre.";
        }
        catch (Exception exception) { report.detail = exception.ToString(); Debug.LogException(exception); }
        finally
        {
            RenderSettings.skybox = originalSky;
            RenderSettings.ambientMode = mode;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = intensity;
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            File.WriteAllText(Result, JsonUtility.ToJson(report, true));
        }
    }
    private static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
}
