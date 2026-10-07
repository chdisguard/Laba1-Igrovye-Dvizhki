using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SpaceMiningLaserVerification
{
    private const string Request = "Tools/Verification/mining-laser.request";
    private const string Result = "Tools/Verification/mining-laser-result.json";
    private static double nextPoll;
    private static bool running;
    static SpaceMiningLaserVerification() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
            || !File.Exists(Request)) return;
        running = true;
        // Keep the request until completion so a domain reload cannot discard a queued check.
        EditorApplication.delayCall += Verify;
    }
    [Serializable] private sealed class Report { public bool passed; public int surfaceChecks; public string detail; }
    public static void Verify()
    {
        Scene scene = default;
        var report = new Report();
        try
        {
            var settings = Resources.Load<SpaceMiningLaserSettings>("MiningLaserSettings");
            Require(settings != null && settings.Material != null, "Settings or effect material missing");
            Require(settings.SwitchInterval(0) == new Vector2(1, 3), "Base timing");
            Require(settings.SwitchInterval(8) == new Vector2(0.25f, 0.3f), "Fastest timing");
            Require(settings.SwitchInterval(20) == settings.SwitchInterval(8), "Timing falls below minimum");
            Require(settings.BeamColour(0) == settings.BeamColour(4) && settings.BeamColour(4) != settings.BeamColour(5)
                && settings.BeamColour(9) != settings.BeamColour(10) && settings.BeamColour(14) != settings.BeamColour(15), "Colour milestones");
            Require(settings.Material.shader.isSupported && !ShaderUtil.ShaderHasError(settings.Material.shader), ShaderDiagnostic(settings.Material));
            Require(settings.Material.FindPass("EffectUnlit") >= 0, "Unlit effect pass missing");
            scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
            SpaceLocationGenerator generator = null;
            SpaceGameplay gameplay = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.TryGetComponent<SpaceLocationGenerator>(out var item)) generator = item;
                if (root.TryGetComponent<SpaceGameplay>(out var game)) gameplay = game;
            }
            Require(generator != null && gameplay != null, "Scene components missing");
            gameplay.Initialize();
            Vector3[] approaches = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
            var covered = new bool[7];
            for (int suffix = 0; suffix < 20; suffix++)
            {
                generator.Generate("00" + suffix);
                var asteroids = generator.GetComponentsInChildren<SpaceMineableAsteroid>();
                for (int i = 0; i < asteroids.Length; i++)
                {
                    int shape = generator.Layout[i].Shape;
                    if (covered[shape]) continue;
                    var asteroid = asteroids[i];
                    var bounds = SpaceMineableAsteroid.ModelBounds(asteroid.transform);
                    foreach (var direction in approaches)
                    {
                        Vector3 emitter = bounds.center + direction * (bounds.extents.magnitude + 50);
                        var points = SpaceMiningLaser.SurfacePoints(asteroid.transform, emitter, settings.CentreSpread, 0);
                        Require(points.Length == 3, "Wrong target count");
                        Require(Vector3.Distance(points[0], points[1]) > 0.001f && Vector3.Distance(points[1], points[2]) > 0.001f, "Duplicate anchors");
                        foreach (var local in points)
                        {
                            var point = asteroid.transform.TransformPoint(local);
                            Require(bounds.Contains(point), "Anchor outside mesh bounds");
                            // An endpoint on the bounding box would lie on an outer box face.
                            Require((point - bounds.center).magnitude < bounds.extents.magnitude, "Box corner used as target");
                            report.surfaceChecks++;
                        }
                    }
                    covered[shape] = true;
                }
                bool complete = true; foreach (bool present in covered) complete &= present;
                if (complete) break;
            }
            foreach (bool present in covered) Require(present, "Not all imported rock shapes covered");
            generator.Generate("0012345");
            var target = generator.GetComponentsInChildren<SpaceMineableAsteroid>()[0];
            Bounds targetBounds = SpaceMineableAsteroid.ModelBounds(target.transform);
            var ship = generator.Ship;
            float radius = targetBounds.extents.magnitude + SpaceMineableAsteroid.ModelBounds(ship).extents.magnitude;
            ship.position = targetBounds.center + Vector3.back * (radius + 15);
            gameplay.BindDeposit(); gameplay.BeginMining();
            var laser = ship.GetComponent<SpaceMiningLaser>();
            Require(laser != null && laser.Active, "Mining extension beyond moveto clearance failed");
            int index = laser.CurrentPointIndex;
            bool switched = false;
            for (int frame = 0; frame < 181; frame++)
            { laser.Advance(1f / 60, 0, Color.white); switched |= laser.CurrentPointIndex != index; }
            Require(switched, "Target did not switch after maximum delay");
            laser.Advance(0, 10, Color.white);
            var beam = ship.Find("Притягивающий лазер").GetComponent<LineRenderer>();
            Color expected = settings.BeamColour(10);
            Require(Mathf.Abs(beam.startColor.r - expected.r) < 0.001f && Mathf.Abs(beam.startColor.g - expected.g) < 0.001f,
                "Upgrade colour not applied");
            gameplay.StopMining();
            var outpost = generator.GetComponent<SpaceLocationManager>().TradingOutpost;
            Bounds tradeBounds = SpaceMineableAsteroid.ModelBounds(outpost.transform);
            ship.position = tradeBounds.center + Vector3.back * (tradeBounds.extents.magnitude
                + SpaceMineableAsteroid.ModelBounds(ship).extents.magnitude + 15);
            Require(gameplay.IsNearOutpost, "Outpost extension beyond moveto clearance failed");
            RenderEffects(settings);
            report.passed = true;
            report.detail = "PASS: 126 actual mesh surface anchors on all 7 rocks / 6 approaches; switching 1-3 to 0.25-0.30; colour steps 5/10/15; extended mining/trading range; cyan particle and beam render without magenta.";
        }
        catch (Exception exception) { report.detail = exception.ToString(); Debug.LogException(exception); }
        finally
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllText(Result, JsonUtility.ToJson(report, true));
            if (File.Exists(Request)) File.Delete(Request);
            running = false;
        }
    }
    private static void RenderEffects(SpaceMiningLaserSettings settings)
    {
        var preview = new PreviewRenderUtility();
        Texture2D copy = null;
        try
        {
            var group = new GameObject("Laser and flame verification");
            preview.AddSingleGO(group);
            var beam = new GameObject("Beam").AddComponent<LineRenderer>();
            beam.transform.SetParent(group.transform, false);
            beam.sharedMaterial = settings.Material; beam.positionCount = 2;
            beam.startColor = settings.BeamColour(0); beam.endColor = settings.BeamColour(0);
            beam.startWidth = 0.8f; beam.endWidth = 0.8f;
            beam.SetPosition(0, new Vector3(-3, 1, 0)); beam.SetPosition(1, new Vector3(3, 1, 0));
            var particles = new GameObject("Flame particles").AddComponent<ParticleSystem>();
            particles.transform.SetParent(group.transform, false);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission; emission.enabled = false;
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = settings.Material;
            var values = new ParticleSystem.Particle[8];
            for (int i = 0; i < values.Length; i++)
            {
                values[i].position = new Vector3(-2.8f + i * 0.8f, -1, 0);
                values[i].startColor = new Color(0.25f, 0.65f, 1, 0.9f);
                values[i].startSize = 0.8f; values[i].startLifetime = 10; values[i].remainingLifetime = 10;
            }
            particles.Play();
            particles.SetParticles(values, values.Length);
            // Preview scenes do not advance the native particle simulation each frame.
            particles.Simulate(0.01f, true, false, false);
            preview.camera.transform.position = new Vector3(0, 0, -10); preview.camera.transform.LookAt(Vector3.zero);
            preview.camera.fieldOfView = 50; preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = Color.black;
            preview.BeginPreview(new Rect(0, 0, 800, 450), GUIStyle.none);
            preview.Render(true);
            var texture = preview.EndPreview();
            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(800, 450, 0);
            try
            {
                Graphics.Blit(texture, rt); RenderTexture.active = rt;
                copy = new Texture2D(800, 450, TextureFormat.RGB24, false);
                copy.ReadPixels(new Rect(0, 0, 800, 450), 0, 0); copy.Apply();
                int magenta = 0, cyanAbove = 0, blueBelow = 0;
                var pixels = copy.GetPixels();
                for (int y = 0; y < 450; y++) for (int x = 0; x < 800; x++)
                {
                    var pixel = pixels[y * 800 + x];
                    if (pixel.r > 0.6f && pixel.b > 0.6f && pixel.g < 0.2f) magenta++;
                    if (pixel.b > 0.25f && pixel.g > 0.15f && pixel.r < pixel.b * 0.7f)
                    { if (y > 225) cyanAbove++; else blueBelow++; }
                }
                File.WriteAllBytes("Tools/Verification/mining-effects-preview.png", copy.EncodeToPNG());
                Require(magenta == 0 && cyanAbove > 100 && blueBelow > 100, "Beam/flame render is magenta or blank: "
                    + magenta + "/" + cyanAbove + "/" + blueBelow + " particles=" + particles.particleCount
                    + " bounds=" + particles.GetComponent<ParticleSystemRenderer>().bounds);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
        }
        finally { if (copy != null) UnityEngine.Object.DestroyImmediate(copy); preview.Cleanup(); }
    }
    private static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
    private static string ShaderDiagnostic(Material material)
    {
        var shader = material.shader;
        var text = new System.Text.StringBuilder("Effect shader: " + shader.name + " | supported=" + shader.isSupported
            + " | path=" + AssetDatabase.GetAssetPath(shader) + " | graphics=" + SystemInfo.graphicsDeviceType
            + " | pipeline=" + UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
            text.Append("\n").Append(message.severity).Append(" line ").Append(message.line).Append(": ").Append(message.message);
        return text.ToString();
    }
}
