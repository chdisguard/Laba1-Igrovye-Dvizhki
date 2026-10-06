using System;
using UnityEngine;

/// <summary>Two-digit material prefix; only the numeric suffix drives geometry.</summary>
public static class SpaceLocationLayout
{
    [Serializable]
    public sealed class Resource
    {
        [Range(0, 99)] public int Code;
        public string Name;
        public Color Color;
        [Range(0, 1)] public float Metallic;
        public Resource(int code, string name, Color color, float metallic)
        { Code = code; Name = name; Color = color; Metallic = metallic; }
    }

    public static readonly Resource[] Materials =
    {
        new Resource(0, "Камень", new Color(0.48f, 0.43f, 0.38f), 0.05f),
        new Resource(1, "Железо", new Color(0.48f, 0.54f, 0.62f), 0.65f),
        new Resource(2, "Медь", new Color(0.85f, 0.35f, 0.17f), 0.6f)
    };

    public const float MinDistance = 2200;
    public const float MaxDistance = 2500;

    public static int MaterialCode(string seed)
    {
        ValidateSeed(seed);
        return (seed[0] - '0') * 10 + seed[1] - '0';
    }

    public static Resource ResolveMaterial(string seed, Resource[] resources)
    {
        int code = MaterialCode(seed);
        Resource stone = null, selected = null;
        if (resources != null)
            foreach (var resource in resources)
            {
                if (resource == null) continue;
                if (resource.Code == 0) stone = resource;
                if (resource.Code == code) selected = resource;
            }
        return selected ?? stone ?? Materials[0];
    }

    private static void ValidateSeed(string seed)
    {
        if (string.IsNullOrEmpty(seed) || seed.Length < 2)
            throw new ArgumentException("Сид должен начинаться с двух цифр материала, например 0012345.", nameof(seed));
        foreach (char character in seed)
            if (character < '0' || character > '9')
                throw new ArgumentException("Сид должен содержать только цифры: первые две — материал, остальные — геометрия.", nameof(seed));
    }

    public readonly struct Asteroid
    {
        public readonly int Shape;
        public readonly Vector3 Position, EulerAngles, Stretch;
        public readonly float Diameter;
        public Asteroid(int shape, Vector3 position,
            Vector3 angles, Vector3 stretch, float diameter)
        {
            Shape = shape;
            Position = position; EulerAngles = angles; Stretch = stretch; Diameter = diameter;
        }
    }

    public static Asteroid[] Generate(string seed, int shapeCount, int minCount = 5, int maxCount = 6)
    {
        ValidateSeed(seed);
        if (shapeCount < 1) throw new ArgumentOutOfRangeException(nameof(shapeCount));
        if (minCount < 1 || maxCount < minCount || maxCount > 64)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "Количество должно быть в пределах 1–64.");
        var random = new SeedRandom(seed.Length > 2 ? seed.Substring(2) : "0");
        int count = minCount + random.Next(maxCount - minCount + 1);
        var shapes = Shuffled(shapeCount, ref random);
        var slots = Shuffled(count, ref random);
        var result = new Asteroid[count];
        float phase = random.Range(0, Mathf.PI * 2);
        for (int i = 0; i < count; i++)
        {
            int slot = slots[i];
            // Separate azimuth sectors surround the ship, with alternating elevations.
            float angle = phase + (slot + random.Range(-0.15f, 0.15f)) * Mathf.PI * 2 / count;
            float height = slot % 2 == 0 ? random.Range(0.12f, 0.55f) : random.Range(-0.55f, -0.12f);
            float horizontal = Mathf.Sqrt(1 - height * height);
            Vector3 direction = new Vector3(Mathf.Cos(angle) * horizontal, height, Mathf.Sin(angle) * horizontal);
            Vector3 position = direction * random.Range(MinDistance, MaxDistance);
            result[i] = new Asteroid(shapes[i % shapeCount], position,
                new Vector3(random.Range(0, 360), random.Range(0, 360), random.Range(0, 360)),
                new Vector3(random.Range(0.8f, 1.15f), random.Range(0.8f, 1.15f), random.Range(0.8f, 1.15f)),
                random.Range(70, 140));
        }
        return result;
    }

    private static int[] Shuffled(int count, ref SeedRandom random)
    {
        var values = new int[count];
        for (int i = 0; i < count; i++) values[i] = i;
        for (int i = count - 1; i > 0; i--)
        { int j = random.Next(i + 1); int value = values[i]; values[i] = values[j]; values[j] = value; }
        return values;
    }

    private struct SeedRandom
    {
        private uint state;
        public SeedRandom(string seed)
        {
            state = 2166136261;
            unchecked { foreach (char character in seed) state = (state ^ character) * 16777619; }
            if (state == 0) state = 0x9e3779b9;
        }
        private uint NextUInt()
        { unchecked { state ^= state << 13; state ^= state >> 17; state ^= state << 5; } return state; }
        public int Next(int exclusiveMax) => (int)(NextUInt() % (uint)exclusiveMax);
        public float Range(float min, float max) => min + (max - min) * ((NextUInt() >> 8) / 16777216f);
    }
}
