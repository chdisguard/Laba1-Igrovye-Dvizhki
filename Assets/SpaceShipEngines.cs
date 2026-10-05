using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[AddComponentMenu("Space/Ship Engines")]
public sealed class SpaceShipEngines : MonoBehaviour
{
    [SerializeField] private Material flameMaterial;
    [SerializeField] private Vector3[] nozzlePositions =
    {
        new Vector3(-1.25f, -0.5f, -2.65f),
        new Vector3(1.25f, -0.5f, -2.65f)
    };
    [SerializeField] private Vector3 exhaustDirection = Vector3.back;
    [SerializeField] private Color flameColor = new Color(0.25f, 0.65f, 1f, 0.9f);
    [SerializeField, Min(0.01f)] private float nozzleRadius = 0.24f;
    [SerializeField, Min(0.1f)] private float flameLength = 4f;
    [SerializeField, Min(1)] private float emissionRate = 100f;
    private ParticleSystem[] engines;
    private float throttle;

    private void Awake()
    {
        if (flameMaterial == null) flameMaterial = Resources.Load<Material>("EngineFlame");
        engines = new ParticleSystem[nozzlePositions.Length];
        for (int i = 0; i < engines.Length; i++)
        {
            var nozzle = new GameObject("Engine Flame " + (i + 1));
            nozzle.layer = gameObject.layer;
            nozzle.transform.SetParent(transform, false);
            nozzle.transform.localPosition = nozzlePositions[i];
            Vector3 direction = exhaustDirection.sqrMagnitude > 0.001f ? exhaustDirection.normalized : Vector3.back;
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            nozzle.transform.localRotation = Quaternion.LookRotation(direction, up);
            var particles = nozzle.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = 0.3f;
            main.startSpeed = flameLength / 0.3f;
            main.startSize = new ParticleSystem.MinMaxCurve(nozzleRadius * 1.1f, nozzleRadius * 2.2f);
            main.startColor = Color.white;
            main.maxParticles = 100;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 4;
            shape.radius = nozzleRadius;
            var emission = particles.emission;
            emission.rateOverTime = 0;
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[]
            {
                new GradientColorKey(new Color(0.9f, 0.96f, 1), 0),
                new GradientColorKey(flameColor, 0.2f),
                new GradientColorKey(flameColor * 0.6f, 1)
            }, new[] { new GradientAlphaKey(0.95f, 0), new GradientAlphaKey(0.6f, 0.45f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.1f));
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = flameMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.5f;
            renderer.velocityScale = 0.03f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
            engines[i] = particles;
        }
    }

    public void SetThrottle(float value)
    {
        throttle = Mathf.Clamp01(value);
        if (engines == null) return;
        foreach (var engine in engines)
        {
            var emission = engine.emission;
            emission.rateOverTime = emissionRate * throttle;
            var main = engine.main;
            main.startSpeed = flameLength / 0.3f * Mathf.Lerp(0.2f, 1, throttle);
        }
    }

    private void OnEnable()
    {
        if (engines == null) return;
        foreach (var engine in engines) engine.Play();
        SetThrottle(0);
    }

    private void OnDisable()
    {
        throttle = 0;
        if (engines == null) return;
        foreach (var engine in engines) engine.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDestroy()
    {
        if (engines == null) return;
        foreach (var engine in engines)
            if (engine != null)
            {
                if (Application.isPlaying) Destroy(engine.gameObject);
                else DestroyImmediate(engine.gameObject);
            }
    }
}
