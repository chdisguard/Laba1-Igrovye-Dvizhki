using System.Collections.Generic;
using UnityEngine;

/// <summary>Add to any object that needs a screen-space name marker.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Space UI/Space Object Marker")]
public sealed class SpaceObjectMarker : MonoBehaviour
{
    public enum LabelSide { Automatic, Right, Left }

    [SerializeField] private string objectName;
    [SerializeField] private Color markerColor = new Color(0.33f, 0.88f, 0.96f, 0.9f);
    [SerializeField] private Transform anchor;
    [SerializeField] private Vector3 worldOffset;
    [SerializeField] private LabelSide labelSide = LabelSide.Automatic;

    internal static readonly HashSet<SpaceObjectMarker> Active = new HashSet<SpaceObjectMarker>();
    private Renderer[] renderers;

    public string DisplayName => string.IsNullOrWhiteSpace(objectName) ? gameObject.name : objectName;
    public LabelSide Side => labelSide;
    public Color MarkerColor => markerColor;

    public void Configure(string displayName, Color color)
    {
        objectName = displayName;
        markerColor = color;
    }

    public Vector3 WorldPosition
    {
        get
        {
            if (anchor != null) return anchor.position + worldOffset;
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            // Model origins are often offset; attach the circle to the visible model's centre.
            bool found = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || renderer is ParticleSystemRenderer) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return (found ? bounds.center : transform.position) + worldOffset;
        }
    }

    private void OnEnable()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        Active.Add(this);
    }

    private void OnDisable() => Active.Remove(this);

    private void OnTransformChildrenChanged() => renderers = GetComponentsInChildren<Renderer>(true);
}
