using System;
using UnityEngine;

/// <summary>Location metadata copied from the completed material pack's catalog.</summary>
public static class AsteroidDepositCatalog
{
    [Serializable] private sealed class Catalog { public Entry[] entries; }
    [Serializable] private sealed class Entry
    {
        public int id;
        public string name;
        public string accent;
        public float metallic;
    }
    private static SpaceLocationLayout.Resource[] resources;

    public static SpaceLocationLayout.Resource Resolve(string seed)
    {
        if (resources == null)
        {
            var asset = Resources.Load<TextAsset>("AsteroidDepositCatalog");
            if (asset == null) throw new InvalidOperationException("Не найден каталог материалов залежей.");
            var catalog = JsonUtility.FromJson<Catalog>(asset.text);
            if (catalog == null || catalog.entries == null || catalog.entries.Length != AsteroidMaterialLibrary.Count)
                throw new InvalidOperationException("Каталог залежей не соответствует библиотеке материалов.");
            resources = new SpaceLocationLayout.Resource[catalog.entries.Length];
            var seen = new bool[AsteroidMaterialLibrary.Count];
            for (int i = 0; i < resources.Length; i++)
            {
                var entry = catalog.entries[i];
                if (entry.id < 0 || entry.id >= seen.Length || seen[entry.id] || string.IsNullOrWhiteSpace(entry.name))
                    throw new InvalidOperationException("Некорректный код или название в каталоге материалов.");
                seen[entry.id] = true;
                Color tint;
                if (!ColorUtility.TryParseHtmlString("#" + entry.accent, out tint)) tint = Color.white;
                resources[i] = new SpaceLocationLayout.Resource(entry.id, entry.name, tint, entry.metallic);
            }
        }
        return SpaceLocationLayout.ResolveMaterial(seed, resources);
    }
}
