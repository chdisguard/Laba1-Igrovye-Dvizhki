using UnityEngine;

public sealed class SpaceMineableAsteroid : MonoBehaviour
{
    public int Material { get; private set; }
    public int Index { get; private set; }
    public int Remaining { get; private set; } = SpaceGameState.PiecesPerAsteroid;
    public void Configure(int material, int index) { Material = material; Index = index; }
    public void SetRemaining(int value)
    {
        Remaining = Mathf.Clamp(value, 0, SpaceGameState.PiecesPerAsteroid);
        gameObject.SetActive(Remaining > 0);
    }
    public static Bounds ModelBounds(Transform root)
    {
        var bounds = new Bounds(root.position, Vector3.zero);
        bool found = false;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
            if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }
    // Navigation and mining use the same bounding spheres, so moveto always reaches mining range.
    public static float Clearance(Transform a, Transform b)
    {
        Bounds first = ModelBounds(a), second = ModelBounds(b);
        return Vector3.Distance(a.position, second.center) - first.extents.magnitude - second.extents.magnitude;
    }
}
