using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SpaceGameplayVerification
{
    private const string Request = "Tools/Verification/gameplay.request";
    private const string Result = "Tools/Verification/gameplay-result.json";
    private static double nextPoll;
    static SpaceGameplayVerification() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
            || !File.Exists(Request)) return;
        File.Delete(Request); EditorApplication.delayCall += Verify;
    }
    [Serializable] private sealed class Report { public bool passed; public string detail; }
    public static void Verify()
    {
        Scene preview = default;
        var report = new Report();
        Material originalSky = RenderSettings.skybox;
        var mode = RenderSettings.ambientMode;
        Color sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor;
        float intensity = RenderSettings.ambientIntensity;
        try
        {
            SpaceSkyboxImport.Configure();
            preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
            SpaceLocationGenerator generator = null;
            SpaceConsoleUI console = null;
            foreach (var root in preview.GetRootGameObjects())
            {
                if (root.TryGetComponent<SpaceLocationGenerator>(out var found)) generator = found;
                if (root.TryGetComponent<SpaceConsoleUI>(out var ui)) console = ui;
            }
            Require(generator != null && console != null, "Missing scene setup");
            var manager = generator.GetComponent<SpaceLocationManager>();
            manager.CaptureSample(); generator.Generate(generator.Seed);
            var gameplay = console.GetComponent<SpaceGameplay>();
            if (gameplay == null) gameplay = console.gameObject.AddComponent<SpaceGameplay>();
            gameplay.Initialize(); gameplay.BindDeposit();
            var ship = generator.Ship;
            Require(!gameplay.IsNearOutpost, "Trading range initially too large");
            int credits = gameplay.State.Credits;
            gameplay.TryExecute("upgrade", "storage");
            Require(gameplay.State.Credits == credits && gameplay.State.StorageLevel == 0, "Remote trading allowed");
            var asteroid = generator.GetComponentsInChildren<SpaceMineableAsteroid>()[0];
            Approach(ship, asteroid.transform);
            gameplay.BeginMining();
            gameplay.Advance(gameplay.State.MiningSeconds(asteroid.Material) + 0.01f);
            Require(asteroid.Remaining == 19 && gameplay.State.Used == 25, "First piece extraction");
            gameplay.Advance(1000);
            Require(asteroid.Remaining == 0 && !asteroid.gameObject.activeSelf && !gameplay.IsMining
                && gameplay.State.Used == 500, "Full asteroid extraction / marker removal");
            Approach(ship, manager.TradingOutpost.transform);
            Require(gameplay.IsNearOutpost, "Moveto clearance does not allow trading");
            gameplay.TryExecute("sellall", "");
            Require(gameplay.State.Used == 0 && gameplay.State.Credits == credits + 400, "Sale in scene");
            gameplay.TryExecute("upgrade", "storage");
            Require(gameplay.State.Capacity == 750, "Scene upgrade");
            gameplay.TryExecute("buyspot", "");
            gameplay.TryExecute("buyspot", "1");
            Require(gameplay.State.Purchased.Count == 1, "Spot purchase in scene");
            var spot = gameplay.State.Offers[0];
            int balance = gameplay.State.Credits;
            gameplay.BeginWarp(spot.Coordinates);
            var departure = ship.position;
            gameplay.Advance(0.6f);
            Require(ship.position == departure && gameplay.IsWarping, "Ship accelerates before finishing turn");
            gameplay.Advance(1.2f);
            Require(Vector3.Distance(ship.position, departure) > 0, "No smooth acceleration");
            gameplay.Advance(1.8f);
            Require(ship.Find("Варп-туннель").gameObject.activeSelf
                && ship.Find("Варп-туннель").GetComponentsInChildren<LineRenderer>().Length == 64, "Warp tunnel missing");
            RenderWarp(ship);
            gameplay.Advance(6);
            Require(!gameplay.IsWarping && !manager.IsSampleLocation && !manager.TradingOutpost.activeSelf
                && generator.MaterialCode == spot.Material && generator.AsteroidCount >= 5
                && gameplay.Coordinates == spot.Coordinates && gameplay.State.Credits == balance && gameplay.State.Capacity == 750,
                "Purchased sector warp / persistent economy");
            asteroid = generator.GetComponentsInChildren<SpaceMineableAsteroid>()[0];
            Approach(ship, asteroid.transform); gameplay.BeginMining();
            gameplay.Advance(gameplay.State.MiningSeconds(asteroid.Material) + 0.01f);
            int remaining = asteroid.Remaining;
            gameplay.BeginWarp("123 0 -456"); gameplay.Advance(8);
            Require(generator.AsteroidCount == 0 && generator.GetComponentsInChildren<SpaceMineableAsteroid>().Length == 0
                && !manager.TradingOutpost.activeSelf, "Unknown coordinates are not empty");
            gameplay.BeginWarp(spot.Coordinates); gameplay.Advance(8);
            Require(generator.GetComponentsInChildren<SpaceMineableAsteroid>()[0].Remaining == remaining, "Warp replenishes mined ore");
            gameplay.BeginWarp(SpaceGameState.OutpostCoordinates); gameplay.Advance(8);
            Require(manager.IsSampleLocation && manager.TradingOutpost.activeSelf && gameplay.State.Used == 25
                && !generator.GetComponentsInChildren<SpaceMineableAsteroid>(true)[0].gameObject.activeSelf,
                "Return to outpost loses cargo or replenishes asteroid");
            report.passed = true;
            report.detail = "PASS: mining range, 20 pieces/capacity, depleted markers, outpost distance gates, selling/upgrades, 3 offers/purchase, turn/acceleration/warp, known and empty sectors, cargo/upgrades/depletion preserved, outpost return.";
        }
        catch (Exception exception) { report.detail = exception.ToString(); Debug.LogException(exception); }
        finally
        {
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            RenderSettings.skybox = originalSky; RenderSettings.ambientMode = mode;
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground; RenderSettings.ambientIntensity = intensity;
            File.WriteAllText(Result, JsonUtility.ToJson(report, true));
        }
    }
    private static void Approach(Transform ship, Transform destination)
    {
        var bounds = SpaceMineableAsteroid.ModelBounds(destination);
        float radius = bounds.extents.magnitude + SpaceMineableAsteroid.ModelBounds(ship).extents.magnitude;
        ship.position = bounds.center + Vector3.back * (radius + 10);
    }
    private static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
    private static void RenderWarp(Transform ship)
    {
        var preview = new PreviewRenderUtility();
        Texture2D copy = null;
        try
        {
            var model = UnityEngine.Object.Instantiate(ship.gameObject);
            preview.AddSingleGO(model);
            model.transform.position = Vector3.zero;
            model.transform.rotation = Quaternion.identity;
            preview.camera.transform.position = new Vector3(0, 1.5f, -12);
            preview.camera.transform.LookAt(new Vector3(0, 0, 50));
            preview.camera.fieldOfView = 85;
            preview.camera.nearClipPlane = 0.1f; preview.camera.farClipPlane = 1200;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.004f, 0.008f, 0.025f);
            preview.lights[0].intensity = 2;
            preview.lights[0].transform.rotation = Quaternion.Euler(40, 25, 0);
            preview.lights[1].intensity = 1;
            preview.BeginPreview(new Rect(0, 0, 960, 540), GUIStyle.none);
            preview.Render(true);
            var texture = preview.EndPreview();
            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(960, 540, 0);
            try
            {
                Graphics.Blit(texture, rt); RenderTexture.active = rt;
                copy = new Texture2D(960, 540, TextureFormat.RGB24, false);
                copy.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); copy.Apply();
                int bright = 0, magenta = 0;
                foreach (var pixel in copy.GetPixels())
                {
                    if (pixel.b > 0.2f || pixel.r > 0.2f || pixel.g > 0.2f) bright++;
                    if (pixel.r > 0.8f && pixel.g < 0.1f && pixel.b > 0.8f) magenta++;
                }
                Require(bright > 1000 && magenta < 1000, "Warp render is blank or has shader errors");
                File.WriteAllBytes("Tools/Verification/warp-preview.png", copy.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
        }
        finally { if (copy != null) UnityEngine.Object.DestroyImmediate(copy); preview.Cleanup(); }
    }
}
