using System;
using UnityEngine;

/// <summary>Standalone library. Does not change the location generator or seed parsing.</summary>
public static class AsteroidMaterialLibrary
{
    public const int Count = 51;
    private static readonly Material[] Cache = new Material[Count];
    public static Material Get(int number)
    {
        if (number < 0 || number >= Count) throw new ArgumentOutOfRangeException(nameof(number), "Номер материала: 00–50.");
        if (Cache[number] == null) Cache[number] = Resources.Load<Material>("AsteroidMaterials/" + number.ToString("D2"));
        if (Cache[number] == null) throw new InvalidOperationException("Не найден материал " + number.ToString("D2") + ". Сохраните папку Resources из набора.");
        return Cache[number];
    }

    /// <summary>Replaces all material slots on mesh renderers; supports models without UVs.</summary>
    public static void Apply(GameObject asteroid, int number, float tiling = 2f)
    {
        if (asteroid == null) throw new ArgumentNullException(nameof(asteroid));
        if (!float.IsFinite(tiling) || tiling <= 0) throw new ArgumentOutOfRangeException(nameof(tiling));
        var material = Get(number);
        foreach (var renderer in asteroid.GetComponentsInChildren<Renderer>(true))
        {
            Bounds bounds;
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null) bounds = skinned.localBounds;
            else if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null) bounds = filter.sharedMesh.bounds;
            else continue;
            int count = Math.Max(1, renderer.sharedMaterials.Length);
            var slots = new Material[count];
            for (int i = 0; i < count; i++) slots[i] = material;
            renderer.sharedMaterials = slots;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, .0001f);
            properties.SetVector("_MappingCentre", bounds.center);
            properties.SetVector("_MappingSize", new Vector4(size, size, size, 0));
            properties.SetFloat("_TextureScale", tiling);
            renderer.SetPropertyBlock(properties);
            // Existing per-slot blocks override renderer blocks; update them only when present.
            for (int i = 0; i < count; i++)
            {
                var perSlot = new MaterialPropertyBlock(); renderer.GetPropertyBlock(perSlot, i);
                if (perSlot.isEmpty) continue;
                perSlot.SetVector("_MappingCentre", bounds.center);
                perSlot.SetVector("_MappingSize", new Vector4(size, size, size, 0));
                perSlot.SetFloat("_TextureScale", tiling); renderer.SetPropertyBlock(perSlot, i);
            }
        }
    }
}
