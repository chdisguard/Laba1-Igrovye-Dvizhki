using System;
using System.Collections.Generic;
using UnityEngine;

public static class LocationLayoutChecks
{
    public static void Main()
    {
        var counts = new HashSet<int>();
        for (int seed = 0; seed < 1000; seed++)
        {
            var a = SpaceLocationLayout.Generate("00" + seed, 7);
            var b = SpaceLocationLayout.Generate("02" + seed, 7);
            counts.Add(a.Length);
            Check(a.Length == 5 || a.Length == 6, "count range");
            Check(a.Length == b.Length, "repeat count");
            var shapes = new HashSet<int>();
            for (int i = 0; i < a.Length; i++)
            {
                Check(a[i].Position.Equals(b[i].Position) && a[i].EulerAngles.Equals(b[i].EulerAngles)
                    && a[i].Stretch.Equals(b[i].Stretch) && a[i].Shape == b[i].Shape
                    && a[i].Diameter == b[i].Diameter, "determinism");
                Check(shapes.Add(a[i].Shape), "distinct asset shapes");
                Check(a[i].Position.magnitude >= 2199.99f && a[i].Position.magnitude <= 2500.01f, "long distances");
                // At speed 200, allowing up to 200 units for bounds + clearance, arrival takes 10–12 s.
                float seconds = Math.Min(12, (a[i].Position.magnitude - 200) / 200);
                Check(seconds >= 10 && seconds <= 12, "centre flight duration");
                for (int j = 0; j < i; j++)
                    Check((a[i].Position - a[j].Position).magnitude > 280, "separation");
            }
            bool above = false, below = false, left = false, right = false, ahead = false, behind = false;
            foreach (var asteroid in a)
            {
                above |= asteroid.Position.y > 0; below |= asteroid.Position.y < 0;
                left |= asteroid.Position.x < 0; right |= asteroid.Position.x > 0;
                ahead |= asteroid.Position.z > 0; behind |= asteroid.Position.z < 0;
            }
            Check(above && below && left && right && ahead && behind, "surround ship in 3D");
        }
        Check(counts.Count == 2, "both location sizes exist");
        Check(SpaceLocationLayout.Generate("0012345", 7, 12, 12).Length == 12, "custom count");
        ExpectInvalid(() => SpaceLocationLayout.Generate(" ", 7));
        ExpectInvalid(() => SpaceLocationLayout.Generate("001", 0));
        ExpectInvalid(() => SpaceLocationLayout.Generate("001", 7, 6, 5));
        ExpectInvalid(() => SpaceLocationLayout.Generate("00abc", 7));
        Check(SpaceLocationLayout.MaterialCode("99123") == 99, "two-digit prefix");
        Check(SpaceLocationLayout.ResolveMaterial("02123", SpaceLocationLayout.Materials).Name == "Медь", "copper selection");
        Check(SpaceLocationLayout.ResolveMaterial("01123", SpaceLocationLayout.Materials).Name == "Железо", "iron selection");
        Check(SpaceLocationLayout.ResolveMaterial("99123", SpaceLocationLayout.Materials).Name == "Камень", "unknown material fallback");
        Check(SpaceLocationLayout.ResolveMaterial("02123", new[] { SpaceLocationLayout.Materials[0] }).Code == 0, "missing list entry fallback");
        Check(SpaceLocationLayout.Generate("00", 7).Length >= 5, "empty suffix defaults to zero");
        var first = SpaceLocationLayout.Generate("0012345", 7);
        var changed = SpaceLocationLayout.Generate("0012346", 7);
        Check(!first[0].Position.Equals(changed[0].Position), "suffix changes geometry");
        Console.WriteLine("PASS: 1000 numeric seeds, material-prefix independence, 5/6 counts, 3D surround, 2200–2500 distance, 10–12 s flight, distinct shapes, separation, validation, unknown-material fallback.");
    }
    private static void Check(bool valid, string message)
    { if (!valid) throw new Exception(message); }
    private static void ExpectInvalid(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid input accepted"); }
}
