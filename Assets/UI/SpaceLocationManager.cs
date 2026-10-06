using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Owns the location environment and authored objects; ship, camera and UI persist.</summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpaceLocationGenerator))]
public sealed class SpaceLocationManager : MonoBehaviour
{
    [Serializable] public sealed class Environment
    {
        public string Name;
        public Material Skybox;
        public Color AmbientColor = new Color(0.2f, 0.2f, 0.25f);
        [Min(0)] public float AmbientIntensity = 1;
        public Color SunColor = Color.white;
        [Min(0)] public float SunIntensity = 2;
    }
    [SerializeField] private SpaceLocationGenerator generator;
    [SerializeField] private Transform ship;
    [SerializeField] private Light sun;
    [Tooltip("Authored outpost and other objects belonging only to SampleScene.")]
    [SerializeField] private GameObject[] sampleLocationObjects;
    [SerializeField] private Environment[] depositSkies;

    public event Action LocationChanged;
    public bool IsSampleLocation { get; private set; } = true;
    public string EnvironmentName { get; private set; } = "SampleScene";
    public GameObject TradingOutpost => sampleLocationObjects != null && sampleLocationObjects.Length > 0
        ? sampleLocationObjects[0] : null;
    private readonly Dictionary<GameObject, bool> sampleObjects = new Dictionary<GameObject, bool>();
    private bool captured;
    private Material sampleSkybox;
    private AmbientMode sampleAmbientMode;
    private Color sampleSky, sampleEquator, sampleGround, sampleSunColor;
    private float sampleAmbientIntensity, sampleSunIntensity;
    private Vector3 sampleShipPosition;
    private Quaternion sampleShipRotation;
    private string sampleSeed;

    private void Awake() => CaptureSample();

    public void CaptureSample()
    {
        if (captured) return;
        if (generator == null) generator = GetComponent<SpaceLocationGenerator>();
        if (ship == null) ship = generator.Ship;
        if (sun == null) sun = RenderSettings.sun;
        sampleSeed = generator.Seed;
        sampleSkybox = RenderSettings.skybox;
        sampleAmbientMode = RenderSettings.ambientMode;
        sampleSky = RenderSettings.ambientSkyColor;
        sampleEquator = RenderSettings.ambientEquatorColor;
        sampleGround = RenderSettings.ambientGroundColor;
        sampleAmbientIntensity = RenderSettings.ambientIntensity;
        if (sun != null) { sampleSunColor = sun.color; sampleSunIntensity = sun.intensity; }
        if (ship != null) { sampleShipPosition = ship.position; sampleShipRotation = ship.rotation; }
        if (sampleLocationObjects != null)
            foreach (var item in sampleLocationObjects) AddSampleObject(item);
        // Include other authored asteroid/model roots, while preserving persistent infrastructure.
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (root == gameObject || root.GetComponentInChildren<Camera>(true) != null
                || root.GetComponentInChildren<SpaceConsoleUI>(true) != null
                || root.GetComponentInChildren<SpaceObjectMarkerOverlay>(true) != null
                || (ship != null && (ship == root.transform || ship.IsChildOf(root.transform)))) continue;
            if (root.GetComponentInChildren<MeshRenderer>(true) != null) AddSampleObject(root);
        }
        captured = true;
    }

    private void AddSampleObject(GameObject item)
    {
        if (item != null && item != gameObject && (ship == null || (item.transform != ship && !ship.IsChildOf(item.transform))))
            sampleObjects[item] = item.activeSelf;
    }

    public void ChangeLocation(string seed) => SetDepositEnvironment(seed, false);

    public void ChangeToEmpty(string coordinateSeed) => SetDepositEnvironment(coordinateSeed, true);

    private void SetDepositEnvironment(string seed, bool empty)
    {
        CaptureSample();
        SpaceLocationLayout.MaterialCode(seed); // Validate before touching the existing location.
        if (depositSkies == null || depositSkies.Length == 0)
            throw new InvalidOperationException("Назначьте скайбоксы новых локаций.");
        var environment = depositSkies[SkyIndex(seed, depositSkies.Length)];
        if (environment == null || environment.Skybox == null)
            throw new InvalidOperationException("Не задан скайбокс локации.");
        if (empty) generator.Clear(); else generator.Generate(seed);
        foreach (var item in sampleObjects) if (item.Key != null) item.Key.SetActive(false);
        RenderSettings.skybox = environment.Skybox;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = environment.AmbientColor;
        RenderSettings.ambientEquatorColor = environment.AmbientColor * 0.6f;
        RenderSettings.ambientGroundColor = environment.AmbientColor * 0.3f;
        RenderSettings.ambientIntensity = environment.AmbientIntensity;
        if (sun != null) { sun.color = environment.SunColor; sun.intensity = environment.SunIntensity; }
        IsSampleLocation = false;
        EnvironmentName = empty ? "Пустой космос / " + environment.Name : environment.Name;
        RefreshEnvironment();
        LocationChanged?.Invoke();
    }

    public void ReturnToSampleScene(bool restoreShipRotation = true)
    {
        CaptureSample();
        if (ship != null)
        {
            ship.position = sampleShipPosition;
            if (restoreShipRotation) ship.rotation = sampleShipRotation;
        }
        generator.Generate(sampleSeed);
        foreach (var item in sampleObjects) if (item.Key != null) item.Key.SetActive(item.Value);
        RenderSettings.skybox = sampleSkybox;
        RenderSettings.ambientMode = sampleAmbientMode;
        RenderSettings.ambientSkyColor = sampleSky;
        RenderSettings.ambientEquatorColor = sampleEquator;
        RenderSettings.ambientGroundColor = sampleGround;
        RenderSettings.ambientIntensity = sampleAmbientIntensity;
        if (sun != null) { sun.color = sampleSunColor; sun.intensity = sampleSunIntensity; }
        IsSampleLocation = true;
        EnvironmentName = "SampleScene";
        RefreshEnvironment();
        LocationChanged?.Invoke();
    }

    public static int SkyIndex(string seed, int count)
    {
        SpaceLocationLayout.MaterialCode(seed);
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        uint hash = 2166136261;
        string suffix = seed.Length > 2 ? seed.Substring(2) : "0";
        unchecked { foreach (char digit in suffix) hash = (hash ^ digit) * 16777619; }
        return (int)(hash % (uint)count);
    }

    private static void RefreshEnvironment()
    {
        // Keep global render state out of isolated edit-mode verification scenes.
        if (Application.isPlaying) DynamicGI.UpdateEnvironment();
    }
}
