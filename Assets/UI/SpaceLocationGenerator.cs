using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SpaceLocationGenerator : MonoBehaviour
{
    [SerializeField] private string seed = "0012345";
    [Tooltip("Location centre follows the ship when generating a new location.")]
    [SerializeField] private Transform ship;
    [SerializeField, Range(1, 64)] private int minAsteroids = 5;
    [SerializeField, Range(1, 64)] private int maxAsteroids = 6;
    [Tooltip("Imported asteroid models from Assets/ассеты, in a stable order.")]
    [SerializeField] private GameObject[] asteroidModels;
    [SerializeField, Range(0.1f, 10)] private float textureScale = 2;
    [SerializeField] private SpaceConsoleUI console;

    private GameObject locationRoot;
    public string Seed => seed;
    public Transform Ship => ship;
    public string MaterialName { get; private set; }
    public int MaterialCode { get; private set; }
    public Vector3 Centre { get; private set; }
    public int AsteroidCount { get; private set; }
    public IReadOnlyList<SpaceLocationLayout.Asteroid> Layout { get; private set; }
    public IReadOnlyList<string> AsteroidNames { get; private set; }
    public IReadOnlyList<SpaceMineableAsteroid> Asteroids { get; private set; } = Array.Empty<SpaceMineableAsteroid>();

    private void Start()
    {
        try { Generate(seed); }
        catch (Exception exception) { Debug.LogError("Не удалось создать локацию: " + exception.Message, this); }
    }

    public void Generate(string newSeed)
    {
        if (asteroidModels == null || asteroidModels.Length == 0)
            throw new InvalidOperationException("Назначьте модели астероидов.");
        foreach (var model in asteroidModels)
            if (model == null || model.GetComponentInChildren<MeshRenderer>(true) == null)
                throw new InvalidOperationException("Модель астероида отсутствует или не содержит MeshRenderer.");
        var selectedResource = AsteroidDepositCatalog.Resolve(newSeed);
        // Load before removing the current location, so missing pack assets leave it usable.
        AsteroidMaterialLibrary.Get(selectedResource.Code);
        if (!float.IsFinite(textureScale) || textureScale <= 0)
            throw new InvalidOperationException("Масштаб текстуры должен быть положительным числом.");
        var layout = SpaceLocationLayout.Generate(newSeed, asteroidModels.Length, minAsteroids, maxAsteroids);
        Clear();
        seed = newSeed;
        locationRoot = new GameObject("Локация [" + seed + "]");
        locationRoot.transform.SetParent(transform, true);
        if (ship == null && Camera.main != null)
        {
            var orbit = Camera.main.GetComponent<OrbitCamera>();
            if (orbit != null) ship = orbit.Target;
        }
        Centre = ship != null ? ship.position : transform.position;
        locationRoot.transform.position = Centre;
        MaterialName = selectedResource.Name;
        MaterialCode = selectedResource.Code;
        var names = new string[layout.Length];
        var currentAsteroids = new SpaceMineableAsteroid[layout.Length];
        for (int index = 0; index < layout.Length; index++)
        {
            var asteroid = layout[index];
            var resource = selectedResource;
            names[index] = resource.Name + "-" + (index + 1);
            var root = new GameObject(names[index]);
            root.transform.SetParent(locationRoot.transform, false);
            var model = Instantiate(asteroidModels[asteroid.Shape], root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            // Normalize source dimensions and offset origins before applying the seeded transform.
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.001f);
            float scale = asteroid.Diameter / size;
            Vector3 centre = root.transform.InverseTransformPoint(bounds.center);
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = -centre * scale;
            AsteroidMaterialLibrary.Apply(root, selectedResource.Code, textureScale);
            root.transform.localPosition = asteroid.Position;
            root.transform.localRotation = Quaternion.Euler(asteroid.EulerAngles);
            root.transform.localScale = asteroid.Stretch;
            root.AddComponent<SpaceObjectMarker>().Configure(names[index],
                Color.Lerp(resource.Color, Color.white, 0.3f));
            currentAsteroids[index] = root.AddComponent<SpaceMineableAsteroid>();
            currentAsteroids[index].Configure(resource.Code, index);
        }
        Layout = layout;
        AsteroidNames = names;
        Asteroids = currentAsteroids;
        AsteroidCount = layout.Length;
        if (console != null) console.AddMessage("Залежь: " + MaterialName + " | Астероидов: " + AsteroidCount
            + "\nScan — список астероидов.");
    }

    public void Clear()
    {
        // Deactivate immediately so marker and navigation registries cannot see the old location.
        if (locationRoot != null)
        {
            locationRoot.SetActive(false);
            // Destroy is deferred in Play Mode: detach retired objects before creating the next sector.
            locationRoot.transform.SetParent(null, true);
            Release(locationRoot);
        }
        locationRoot = null;
        Layout = null;
        AsteroidNames = null;
        AsteroidCount = 0;
        Asteroids = Array.Empty<SpaceMineableAsteroid>();
    }

    private void OnDestroy() => Clear();

    private static void Release(UnityEngine.Object value)
    {
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
