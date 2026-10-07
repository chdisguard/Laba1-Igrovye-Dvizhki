using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Run actual Play Mode regression checks before producing a non-development Windows build.</summary>
[InitializeOnLoad]
public static class SpaceReleasePipeline
{
    private const string Request = "Tools/Verification/release.request";
    private const string Result = "Tools/Verification/release-result.json";
    private const string PhaseKey = "Space.ReleasePipeline.Phase";
    private const string BatchKey = "Space.ReleasePipeline.Batch";
    private const string BuildPath = "Builds/Release/SpaceMining.exe";
    private static double nextPoll, readyAt;
    private static Scene testScene;
    private static bool busy;
    [Serializable] private sealed class Report
    {
        public string stage, detail, executable;
        public bool passed, playModePassed, development;
        public long bytes;
        public int errors, warnings;
    }
    static SpaceReleasePipeline()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += PlayStateChanged;
    }
    private static void Save(Report report) => File.WriteAllText(Result, JsonUtility.ToJson(report, true));
    private static string Phase => SessionState.GetString(PhaseKey, "idle");
    private static void SetPhase(string phase) => SessionState.SetString(PhaseKey, phase);
    public static void RunBatch()
    {
        Require(Application.isBatchMode, "Use this entry point from a standalone batch editor.");
        SessionState.SetBool(BatchKey, true);
        SetPhase("idle");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        Directory.CreateDirectory("Tools/Verification");
        File.WriteAllText(Request, "verify and build Windows x64");
    }
    private static void PlayStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && Phase == "verified")
        { readyAt = EditorApplication.timeSinceStartup + 2; SetPhase("build"); }
    }
    private static void Poll()
    {
        if (busy || EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + 0.5;
        try
        {
            if (Phase == "idle" && File.Exists(Request) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Require(BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64),
                    "Windows x64 Build Support is not installed in this Unity editor.");
                // The current editor scene is untouched. Play Mode changes are discarded on exit.
                SpaceSkyboxImport.Configure();
                Save(new Report { stage = "play-mode", detail = "Starting isolated runtime checks." });
                SetPhase("entering");
                EditorApplication.EnterPlaymode();
            }
            else if (Phase == "entering" && EditorApplication.isPlaying)
            {
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) root.SetActive(false);
                testScene = EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/SampleScene.unity", new LoadSceneParameters(LoadSceneMode.Additive));
                SetPhase("loading-test");
            }
            else if (Phase == "loading-test" && EditorApplication.isPlaying && testScene.IsValid() && testScene.isLoaded)
            {
                SceneManager.SetActiveScene(testScene);
                readyAt = EditorApplication.timeSinceStartup + 1;
                SetPhase("testing");
            }
            else if (Phase == "testing" && EditorApplication.isPlaying && EditorApplication.timeSinceStartup >= readyAt)
            {
                busy = true;
                VerifyRuntime();
                Save(new Report { stage = "verified", playModePassed = true,
                    detail = "PASS: real Play Mode deferred destruction, 5/6/5/6 transitions, home return without rotation reset, cargo/depletion, mining, sale, upgrades, spot purchase, empty sector." });
                SetPhase("verified");
                EditorApplication.ExitPlaymode();
                busy = false;
            }
            else if ((Phase == "verified" || Phase == "build") && !EditorApplication.isPlayingOrWillChangePlaymode
                && EditorApplication.timeSinceStartup >= readyAt)
            {
                SetPhase("building"); busy = true;
                BuildRelease();
                SetPhase("idle"); busy = false;
                if (File.Exists(Request)) File.Delete(Request);
                if (SessionState.GetBool(BatchKey, false)) EditorApplication.Exit(0);
            }
        }
        catch (Exception exception)
        {
            Save(new Report { stage = "failed", detail = exception.ToString() });
            SetPhase("idle"); busy = false;
            if (File.Exists(Request)) File.Delete(Request);
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            Debug.LogException(exception);
            if (SessionState.GetBool(BatchKey, false)) EditorApplication.Exit(1);
        }
    }
    private static void VerifyRuntime()
    {
        Require(Application.isPlaying && testScene.IsValid(), "Verification must run in Play Mode.");
        SpaceLocationGenerator generator = null;
        SpaceGameplay gameplay = null;
        foreach (var root in testScene.GetRootGameObjects())
        {
            if (root.TryGetComponent<SpaceLocationGenerator>(out var found)) generator = found;
            if (root.TryGetComponent<SpaceGameplay>(out var game)) gameplay = game;
        }
        Require(generator != null && gameplay != null, "Runtime scene missing generator or gameplay.");
        gameplay.enabled = false;
        gameplay.GetComponent<SpaceTerminalCommands>().enabled = false;
        gameplay.Initialize(); gameplay.BindDeposit();
        var manager = generator.GetComponent<SpaceLocationManager>();
        var ship = generator.Ship;
        Vector3 home = ship.position;
        Material homeSky = RenderSettings.skybox;
        Require(generator.Asteroids.Count == 5, "Expected five home asteroids.");
        var first = generator.Asteroids[0];
        Approach(ship, first.transform);
        gameplay.BeginMining(); gameplay.Advance(1000);
        Require(first.Remaining == 0 && !first.gameObject.activeSelf && gameplay.State.Used == 500, "Mining/full storage regression.");
        Approach(ship, manager.TradingOutpost.transform);
        gameplay.TryExecute("sellall", "");
        Require(gameplay.State.Used == 0 && gameplay.State.Credits == 1900, "Sale regression.");
        gameplay.TryExecute("upgrade", "storage");
        Require(gameplay.State.Capacity == 750, "Storage upgrade regression.");
        gameplay.TryExecute("buyspot", ""); gameplay.TryExecute("buyspot", "1");
        Require(gameplay.State.Purchased.Count == 1, "Spot purchase regression.");
        var spot = gameplay.State.Offers[0];
        gameplay.BeginWarp(spot.Coordinates); gameplay.Advance(8);
        Require(!gameplay.IsWarping && gameplay.Coordinates == spot.Coordinates && generator.MaterialCode == spot.Material,
            "Purchased warp regression.");
        gameplay.BeginWarp(SpaceGameState.OutpostCoordinates); gameplay.Advance(8);
        Require(manager.IsSampleLocation && ship.position == home, "Initial home return regression.");

        var six = new SpaceGameState.Spot { Coordinates = "600 600 600", Seed = SeedForCount(6), Material = 2, Rank = 0, Price = 350 };
        var five = new SpaceGameState.Spot { Coordinates = "700 700 700", Seed = SeedForCount(5), Material = 2, Rank = 0, Price = 350 };
        gameplay.State.Purchased.Add(six.Coordinates, six);
        gameplay.State.Purchased.Add(five.Coordinates, five);
        gameplay.BeginWarp(six.Coordinates); gameplay.Advance(8);
        Require(generator.Asteroids.Count == 6, "Six asteroid sector not generated.");
        var retired = generator.Asteroids[5];
        var mined = generator.Asteroids[0];
        Approach(ship, mined.transform);
        gameplay.BeginMining(); gameplay.Advance(gameplay.State.MiningSeconds(2) + 0.01f); gameplay.StopMining();
        Require(mined.Remaining == 19 && gameplay.State.Used == 25, "Field mining regression.");
        gameplay.BeginWarp(SpaceGameState.OutpostCoordinates);
        gameplay.Advance(4.79f);
        Quaternion flight = ship.rotation;
        gameplay.Advance(0.02f);
        Require(retired != null, "Test did not exercise deferred destruction.");
        Require(!retired.transform.IsChildOf(generator.transform), "Retired field still belongs to current generator.");
        Require(generator.Asteroids.Count == 5 && generator.GetComponentsInChildren<SpaceMineableAsteroid>(true).Length == 5,
            "Old six-asteroid field is mixed with home.");
        Require(gameplay.IsWarping && gameplay.Coordinates == SpaceGameState.OutpostCoordinates && ship.position == home,
            "Six-to-five home warp aborted or rolled ship back.");
        Require(Quaternion.Angle(flight, ship.rotation) < 0.01f, "Home transition resets warp rotation.");
        gameplay.Advance(3);
        Require(!gameplay.IsWarping && manager.IsSampleLocation && manager.TradingOutpost.activeInHierarchy
            && RenderSettings.skybox == homeSky && generator.Asteroids[0].Remaining == 0 && gameplay.State.Used == 25,
            "Home arrival/cargo/depletion regression.");
        gameplay.BeginWarp(six.Coordinates); gameplay.Advance(8);
        Require(generator.Asteroids[0].Remaining == 19, "Revisiting a field replenishes ore.");
        gameplay.BeginWarp(five.Coordinates); gameplay.Advance(8);
        Require(!gameplay.IsWarping && generator.Asteroids.Count == 5, "Six-to-five sector warp regression.");
        gameplay.BeginWarp(six.Coordinates); gameplay.Advance(8);
        Require(!gameplay.IsWarping && generator.Asteroids.Count == 6, "Five-to-six sector warp regression.");
        gameplay.BeginWarp("123 0 -456"); gameplay.Advance(8);
        Require(generator.Asteroids.Count == 0 && !manager.TradingOutpost.activeInHierarchy, "Unknown sector is not empty.");
        gameplay.BeginWarp(SpaceGameState.OutpostCoordinates); gameplay.Advance(8);
        Require(manager.IsSampleLocation && ship.position == home && gameplay.State.Capacity == 750
            && gameplay.State.Used == 25, "Empty-to-home persistent state regression.");
        Require(Resources.Load<SpaceMiningLaserSettings>("MiningLaserSettings").Material.shader.isSupported, "Effect shader unsupported.");
    }
    private static string SeedForCount(int count)
    {
        for (int i = 0; i < 1000; i++)
        { string seed = "02" + i; if (SpaceLocationLayout.Generate(seed, 7).Length == count) return seed; }
        throw new InvalidOperationException("No seed found for asteroid count.");
    }
    private static void Approach(Transform ship, Transform destination)
    {
        var bounds = SpaceMineableAsteroid.ModelBounds(destination);
        ship.position = bounds.center + Vector3.back * (bounds.extents.magnitude
            + SpaceMineableAsteroid.ModelBounds(ship).extents.magnitude + 15);
    }
    private static void BuildRelease()
    {
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Minimal);
        Save(new Report { stage = "building", playModePassed = true, detail = "Building Windows x64, BuildOptions.None.", executable = BuildPath });
        Directory.CreateDirectory(Path.GetDirectoryName(BuildPath));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = BuildPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        bool development = (report.summary.options & BuildOptions.Development) != 0;
        var result = new Report
        {
            stage = report.summary.result == BuildResult.Succeeded ? "complete" : "failed",
            passed = report.summary.result == BuildResult.Succeeded && !development,
            playModePassed = true, development = development,
            executable = Path.GetFullPath(BuildPath), bytes = (long)report.summary.totalSize,
            errors = (int)report.summary.totalErrors, warnings = (int)report.summary.totalWarnings,
            detail = report.summary.result + " | Windows x64 | " + report.summary.totalTime
        };
        foreach (var step in report.steps) foreach (var message in step.messages)
            if (message.type == LogType.Error || message.type == LogType.Exception) result.detail += "\n" + message.content;
        Save(result);
        Require(result.passed, result.detail);
        Save(new Report { stage = "building-player-check", playModePassed = true, detail = "Building isolated standalone mining verification." });
        string verificationPath = "Builds/VerificationPlayer/SpaceMiningVerification.exe";
        Directory.CreateDirectory(Path.GetDirectoryName(verificationPath));
        var verification = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" }, locationPathName = verificationPath,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
            extraScriptingDefines = new[] { "SPACE_PLAYER_VERIFICATION" }
        });
        Require(verification.summary.result == BuildResult.Succeeded, "Standalone mining verification build failed.");
        Save(result);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
