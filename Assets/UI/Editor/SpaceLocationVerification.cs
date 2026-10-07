using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SpaceLocationVerification
{
    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            var generator = UnityEngine.Object.FindAnyObjectByType<SpaceLocationGenerator>();
            Require(generator != null, "Scene generator missing");
            generator.Generate("0012345");
            var original = new List<string>(generator.AsteroidNames);
            CheckLocation(generator);
            generator.Generate("0012346");
            CheckLocation(generator);
            for (int i = 0; i < Math.Min(original.Count, generator.AsteroidNames.Count); i++)
                Require(original[i] == generator.AsteroidNames[i], "Seed changed material assignment");
            generator.Generate("0012345");
            CheckLocation(generator);
            Require(original.Count == generator.AsteroidCount, "Same seed changed count");
            var geometry = generator.Layout;
            generator.Generate("0212345");
            CheckLocation(generator);
            Require(generator.MaterialName == "Медь", "Material prefix not applied");
            for (int i = 0; i < geometry.Count; i++)
                Require(geometry[i].Position.Equals(generator.Layout[i].Position), "Material prefix changed geometry");
            generator.Generate("9912345");
            CheckLocation(generator);
            Require(generator.MaterialName == "Камень", "Missing material did not fall back to stone");
            Debug.Log("LOCATION_VERIFICATION_PASS: real imported models, shader, colors, names, markers, regeneration, material independence.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void CheckLocation(SpaceLocationGenerator generator)
    {
        var markers = generator.GetComponentsInChildren<SpaceObjectMarker>();
        Require(markers.Length == generator.AsteroidCount, "Stale or missing markers");
        Require(markers.Length == 5 || markers.Length == 6, "Wrong count");
        for (int i = 0; i < markers.Length; i++)
        {
            Require(markers[i].DisplayName == generator.AsteroidNames[i], "Marker name mismatch");
            Require(markers[i].name.EndsWith("-" + (i + 1)), "Wrong location ordinal");
            Require(markers[i].DisplayName == generator.MaterialName + "-" + (i + 1), "Mixed-material deposit");
            var renderer = markers[i].GetComponentInChildren<MeshRenderer>();
            Require(renderer != null && renderer.GetComponent<MeshFilter>().sharedMesh != null, "Imported mesh missing");
            Require(renderer.sharedMaterial == AsteroidMaterialLibrary.Get(generator.MaterialCode), "Wrong library material");
            var bounds = renderer.bounds;
            Require(bounds.size.magnitude > 50 && bounds.size.magnitude < 280, "Model normalization failed");
            Require(Vector3.Distance(markers[i].transform.position, generator.Centre) >= 2199.9f, "Distance too small");
            Require(Vector3.Distance(markers[i].WorldPosition, markers[i].transform.position) < 0.1f, "Off-centre marker");
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
