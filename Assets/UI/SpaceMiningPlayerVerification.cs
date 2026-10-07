#if SPACE_PLAYER_VERIFICATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Compiled only into the isolated verification player, never into the shipping build.</summary>
public sealed class SpaceMiningPlayerVerification : MonoBehaviour
{
    [Serializable] private sealed class Report { public bool passed; public int shapes, points; public string detail; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() => new GameObject("Standalone mining checks").AddComponent<SpaceMiningPlayerVerification>();
    private IEnumerator Start()
    {
        yield return null;
        yield return null;
        var report = new Report();
        var seen = new HashSet<int>();
        var generator = FindFirstObjectByType<SpaceLocationGenerator>();
        var gameplay = FindFirstObjectByType<SpaceGameplay>();
        if (generator == null || gameplay == null) { Finish(report, "Missing gameplay scene."); yield break; }
        gameplay.enabled = false;
        gameplay.GetComponent<SpaceTerminalCommands>().enabled = false;
        var axes = new[] { Vector3.left, Vector3.right, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        Exception failure = null;
        for (int seed = 0; seed < 100 && seen.Count < 7; seed++)
        {
            try
            {
                generator.Generate("00" + seed);
                for (int index = 0; index < generator.Asteroids.Count; index++)
                {
                    if (!seen.Add(generator.Layout[index].Shape)) continue;
                    var asteroid = generator.Asteroids[index].transform;
                    foreach (var mesh in asteroid.GetComponentsInChildren<MeshFilter>())
                        Require(mesh.sharedMesh.isReadable, "Unreadable player mesh: " + mesh.sharedMesh.name);
                    var bounds = SpaceMineableAsteroid.ModelBounds(asteroid);
                    foreach (var axis in axes)
                    {
                        var points = SpaceMiningLaser.SurfacePoints(asteroid, bounds.center + axis * (bounds.extents.magnitude + 50), 0.22f, 0.25f);
                        Require(points.Length == 3, "Missing laser surface anchors.");
                        foreach (var point in points)
                        {
                            Require(float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z), "Invalid surface anchor.");
                            var expanded = bounds; expanded.Expand(2);
                            Require(expanded.Contains(asteroid.TransformPoint(point)), "Surface anchor outside model.");
                            report.points++;
                        }
                    }
                }
            }
            catch (Exception exception) { failure = exception; break; }
            yield return null;
        }
        if (failure == null)
        {
            try
            {
                Require(seen.Count == 7 && report.points == 126, "Not all seven imported shapes tested.");
                generator.GetComponent<SpaceLocationManager>().ReturnToSampleScene();
                gameplay.Initialize(); gameplay.BindDeposit();
                var asteroid = generator.Asteroids[0];
                var ship = generator.Ship;
                var bounds = SpaceMineableAsteroid.ModelBounds(asteroid.transform);
                ship.position = bounds.center + Vector3.back * (bounds.extents.magnitude + SpaceMineableAsteroid.ModelBounds(ship).extents.magnitude + 15);
                gameplay.BeginMining();
                Require(gameplay.IsMining && ship.GetComponent<SpaceMiningLaser>().Active, "Player could not start mining.");
                gameplay.Advance(gameplay.State.MiningSeconds(asteroid.Material) + 0.01f);
                Require(gameplay.State.Used == 25 && asteroid.Remaining == 19, "Player extraction failed.");
                gameplay.StopMining();
                report.passed = true;
            }
            catch (Exception exception) { failure = exception; }
        }
        report.shapes = seen.Count;
        Finish(report, failure == null ? "PASS: seven readable shapes, 126 surface anchors, laser start and ore extraction in the standalone player." : failure.ToString());
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    private static void Finish(Report report, string detail)
    {
        report.detail = detail;
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "mining-result.json"), JsonUtility.ToJson(report, true));
        Application.Quit(report.passed ? 0 : 1);
    }
}
#endif
