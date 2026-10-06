using UnityEngine;

[CreateAssetMenu(menuName = "Space/Mining Laser Settings", fileName = "MiningLaserSettings")]
public sealed class SpaceMiningLaserSettings : ScriptableObject
{
    [Header("Extraction")]
    [Min(0.01f)] public float BaseSecondsPerPiece = 2;
    [Min(0)] public float SecondsPerRarity = 1.4f;
    [Min(0)] public float SecondsPerMaterialStep = 0.12f;
    [Min(0)] public float SpeedBonusPerLevel = 0.35f;
    [Min(1)] public int MaximumLaserLevel = 20;
    [Header("Beam")]
    public Material Material;
    public Vector3 EmitterLocalPosition = new Vector3(0, 0, 2);
    [Min(0.01f)] public float StartWidth = 0.22f;
    [Min(0.01f)] public float EndWidth = 0.8f;
    [Range(0, 1)] public float StartAlpha = 0.9f;
    [Range(0, 1)] public float EndAlpha = 0.65f;
    [Tooltip("Extra surface clearance beyond the moveto stopping distance.")]
    [Min(0)] public float RangeTolerance = 8;

    [Header("Surface targeting (internal visual timing)")]
    [Range(0.01f, 0.4f)] public float CentreSpread = 0.15f;
    [Min(0)] public float SurfaceOffset = 0.02f;
    public Vector2 BaseSwitchSeconds = new Vector2(1, 3);
    public Vector2 FastestSwitchSeconds = new Vector2(0.25f, 0.30f);
    [Min(1)] public int FastestSwitchLevel = 8;

    [Header("Colour progression")]
    [Min(1)] public int LevelsPerColour = 5;
    public Color[] Colours =
    {
        new Color(0.2f, 0.8f, 1), new Color(0.15f, 0.4f, 1),
        new Color(1, 0.75f, 0.2f), new Color(0.2f, 1, 0.5f), new Color(1, 0.25f, 0.1f)
    };

    [Header("Tractor flow")]
    [Range(1, 64)] public int ParticleCount = 9;
    [Min(0.01f)] public float ParticleSize = 0.8f;
    [Min(0.01f)] public float FlowCyclesPerSecond = 0.45f;

    public Vector2 SwitchInterval(int laserLevel)
    {
        float t = Mathf.Clamp01(laserLevel / (float)Mathf.Max(1, FastestSwitchLevel));
        var value = Vector2.Lerp(BaseSwitchSeconds, FastestSwitchSeconds, t);
        return new Vector2(Mathf.Max(0.01f, value.x), Mathf.Max(Mathf.Max(0.01f, value.x), value.y));
    }
    public Color BeamColour(int laserLevel)
    {
        if (Colours == null || Colours.Length == 0) return new Color(0.2f, 0.8f, 1);
        int index = Mathf.Max(0, laserLevel) / Mathf.Max(1, LevelsPerColour);
        return Colours[index % Colours.Length];
    }
    private void OnValidate()
    {
        BaseSwitchSeconds.x = Mathf.Max(0.01f, BaseSwitchSeconds.x);
        BaseSwitchSeconds.y = Mathf.Max(BaseSwitchSeconds.x, BaseSwitchSeconds.y);
        FastestSwitchSeconds.x = Mathf.Clamp(FastestSwitchSeconds.x, 0.01f, BaseSwitchSeconds.x);
        FastestSwitchSeconds.y = Mathf.Clamp(FastestSwitchSeconds.y, FastestSwitchSeconds.x, BaseSwitchSeconds.y);
        FastestSwitchLevel = Mathf.Max(1, FastestSwitchLevel);
        LevelsPerColour = Mathf.Max(1, LevelsPerColour);
        ParticleCount = Mathf.Clamp(ParticleCount, 1, 64);
    }
}
