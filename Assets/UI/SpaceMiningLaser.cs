using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Surface aiming and tractor visuals, independent of cargo and extraction timing.</summary>
[DisallowMultipleComponent]
public sealed class SpaceMiningLaser : MonoBehaviour
{
    [SerializeField] private SpaceMiningLaserSettings settings;
    private Transform target;
    private readonly Vector3[] localPoints = new Vector3[3];
    private LineRenderer beam;
    private ParticleSystem flow;
    private ParticleSystem.Particle[] particles;
    private readonly System.Random random = new System.Random();
    private float elapsed, remainingSwitchTime;
    private int currentPoint, level;
    public IReadOnlyList<Vector3> LocalPoints => localPoints;
    public int CurrentPointIndex => currentPoint;
    public Vector3 CurrentPoint => target != null ? target.TransformPoint(localPoints[currentPoint]) : transform.position;
    public bool Active => target != null;
    public SpaceMiningLaserSettings Settings => settings;

    public void Begin(Transform asteroid, SpaceMiningLaserSettings configuration, int laserLevel)
    {
        Stop();
        if (configuration == null) throw new InvalidOperationException("Не назначены настройки майнинг-лазера.");
        settings = configuration;
        var points = SurfacePoints(asteroid, transform.TransformPoint(settings.EmitterLocalPosition), settings.CentreSpread, settings.SurfaceOffset);
        Array.Copy(points, localPoints, 3);
        CreateEffects();
        target = asteroid;
        level = laserLevel;
        currentPoint = 0;
        elapsed = 0;
        remainingSwitchTime = NextSwitchDelay();
        beam.enabled = true;
        flow.gameObject.SetActive(true);
        flow.Play();
        Advance(0, laserLevel, Color.white);
    }
    public void Stop()
    {
        target = null;
        if (beam != null) beam.enabled = false;
        if (flow != null) { flow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); flow.gameObject.SetActive(false); }
    }
    private float NextSwitchDelay()
    {
        var interval = settings.SwitchInterval(level);
        return Mathf.Lerp(interval.x, interval.y, (float)random.NextDouble());
    }
    public void Advance(float delta, int laserLevel, Color oreColour)
    {
        if (target == null) { Stop(); return; }
        if (laserLevel != level)
        {
            float oldMaximum = settings.SwitchInterval(level).y;
            level = laserLevel;
            remainingSwitchTime *= settings.SwitchInterval(level).y / oldMaximum;
        }
        elapsed += delta;
        remainingSwitchTime -= delta;
        while (remainingSwitchTime <= 0)
        {
            currentPoint = (currentPoint + 1) % 3;
            remainingSwitchTime += NextSwitchDelay();
        }
        Vector3 emitter = transform.TransformPoint(settings.EmitterLocalPosition);
        Vector3 surface = CurrentPoint;
        beam.SetPosition(0, emitter); beam.SetPosition(1, surface);
        Color colour = settings.BeamColour(level);
        beam.startColor = new Color(colour.r, colour.g, colour.b, settings.StartAlpha);
        beam.endColor = new Color(colour.r, colour.g, colour.b, settings.EndAlpha);
        for (int i = 0; i < particles.Length; i++)
        {
            float progress = Mathf.Repeat(elapsed * settings.FlowCyclesPerSecond + i / (float)particles.Length, 1);
            particles[i].position = Vector3.Lerp(surface, emitter, progress);
            particles[i].startColor = oreColour;
            particles[i].startSize = settings.ParticleSize;
            particles[i].startLifetime = 10; particles[i].remainingLifetime = 10;
        }
        flow.SetParticles(particles, particles.Length);
    }
    private void CreateEffects()
    {
        Material material = settings.Material != null ? settings.Material : Resources.Load<Material>("EngineFlame");
        if (material == null || material.shader == null || !material.shader.isSupported)
            throw new InvalidOperationException("Материал эффектов отсутствует или шейдер не поддерживается.");
        if (beam == null)
        {
            var item = new GameObject("Притягивающий лазер");
            item.transform.SetParent(transform, false);
            beam = item.AddComponent<LineRenderer>();
            beam.useWorldSpace = true; beam.positionCount = 2;
            beam.shadowCastingMode = ShadowCastingMode.Off; beam.receiveShadows = false;
            beam.numCapVertices = 4;
        }
        beam.sharedMaterial = material; beam.startWidth = settings.StartWidth; beam.endWidth = settings.EndWidth;
        if (flow == null)
        {
            var item = new GameObject("Поток добытой руды");
            item.transform.SetParent(transform, false);
            flow = item.AddComponent<ParticleSystem>();
            flow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = flow.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false;
            var emission = flow.emission; emission.enabled = false;
            var renderer = flow.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        int count = Mathf.Clamp(settings.ParticleCount, 1, 64);
        var module = flow.main; module.maxParticles = count;
        particles = new ParticleSystem.Particle[count];
        flow.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
    }

    public static Vector3[] SurfacePoints(Transform asteroid, Vector3 emitter, float spread, float surfaceOffset)
    {
        Bounds bounds = SpaceMineableAsteroid.ModelBounds(asteroid);
        Vector3 direction = (bounds.center - emitter).normalized;
        Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
        if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(direction, Vector3.right).normalized;
        Vector3 up = Vector3.Cross(right, direction).normalized;
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.y, bounds.extents.z) * spread;
        var queries = new List<MeshCollider>();
        try
        {
            // Runtime cooking needs readable geometry in a player (the editor retains CPU mesh data).
            // They are removed immediately after selecting the three local anchors.
            foreach (var filter in asteroid.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                if (!Application.isEditor && !filter.sharedMesh.isReadable)
                    throw new InvalidOperationException("Модель астероида недоступна для лазера: включите Read/Write при импорте " + filter.sharedMesh.name + ".");
                var query = new GameObject("Mining surface query") { layer = 2 };
                query.transform.SetParent(filter.transform, false);
                var collider = query.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                queries.Add(collider);
            }
            Physics.SyncTransforms();
            var result = new Vector3[3];
            for (int i = 0; i < result.Length; i++)
            {
                float angle = i * Mathf.PI * 2 / 3;
                Vector3 offset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
                bool found = false;
                RaycastHit hit = default;
                for (int retry = 0; retry < 5 && !found; retry++)
                {
                    Vector3 aim = bounds.center + offset * Mathf.Pow(0.5f, retry);
                    var ray = new Ray(emitter, (aim - emitter).normalized);
                    float closest = float.MaxValue;
                    foreach (var query in queries)
                        if (query.Raycast(ray, out var candidate, Vector3.Distance(emitter, bounds.center) + bounds.size.magnitude * 2)
                            && candidate.distance < closest)
                        { found = true; hit = candidate; closest = candidate.distance; }
                }
                if (!found) throw new InvalidOperationException("Не удалось выбрать точку на поверхности астероида.");
                result[i] = asteroid.InverseTransformPoint(hit.point + hit.normal * surfaceOffset);
            }
            return result;
        }
        finally
        {
            foreach (var query in queries)
            {
                query.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(query.gameObject); else DestroyImmediate(query.gameObject);
            }
        }
    }
    private void OnDisable() => Stop();
    private void OnDestroy()
    {
        if (beam != null) Release(beam.gameObject);
        if (flow != null) Release(flow.gameObject);
    }
    private static void Release(UnityEngine.Object item)
    { if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
}
