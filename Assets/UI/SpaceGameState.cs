using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>Session economy. Coordinates identify locations, not local Unity positions.</summary>
public sealed class SpaceGameState
{
    public const int ChunkVolume = 25;
    public const int PiecesPerAsteroid = 20;
    public const string OutpostCoordinates = "420 10 82";
    public int Credits { get; private set; } = 1500;
    public int StorageLevel { get; private set; }
    public int LaserLevel { get; private set; }
    public int QualityLevel { get; private set; }
    public int SpotLevel { get; private set; }
    public float LaserSpeedBonus { get; private set; } = 0.35f;
    private float miningBaseSeconds = 2, miningRaritySeconds = 1.4f, miningMaterialSeconds = 0.12f;
    private int maximumLaserLevel = 20;
    public void ConfigureMining(float baseSeconds, float raritySeconds, float materialSeconds, float speedBonus, int maxLevel)
    {
        miningBaseSeconds = Math.Max(0.01f, baseSeconds);
        miningRaritySeconds = Math.Max(0, raritySeconds);
        miningMaterialSeconds = Math.Max(0, materialSeconds);
        LaserSpeedBonus = Math.Max(0, speedBonus);
        maximumLaserLevel = Math.Max(1, maxLevel);
    }
    public int Capacity => 500 + StorageLevel * 250;
    public int Used { get { int result = 0; foreach (var item in Cargo) result += item.Count * ChunkVolume; return result; } }
    public int CargoValue { get { int result = 0; foreach (var item in Cargo) result += item.Count * item.UnitPrice; return result; } }
    public readonly List<CargoItem> Cargo = new List<CargoItem>();
    public readonly List<Spot> Offers = new List<Spot>();
    public readonly Dictionary<string, Spot> Purchased = new Dictionary<string, Spot>();
    private readonly Dictionary<string, int[]> deposits = new Dictionary<string, int[]>();
    private readonly Random random;
    public SpaceGameState(int seed = 0) { random = seed == 0 ? new Random() : new Random(seed); }

    [Serializable] public sealed class CargoItem { public int Material, Count, UnitPrice; }
    [Serializable] public sealed class Spot { public string Coordinates, Seed; public int Rank, Material, Price; }
    public static readonly string[] Ranks = { "D", "C", "B", "A", "S" };
    public static int MaterialRank(int material) => Math.Min(4, Math.Max(0, material / 10));
    public static int BasePrice(int material)
    {
        int[] bases = { 20, 55, 150, 420, 1200 };
        int rank = MaterialRank(material);
        return bases[rank] + (material - rank * 10) * bases[rank] / 12;
    }
    public int OrePrice(int material) => BasePrice(material) * (100 + QualityLevel * 25) / 100;
    public float MiningSeconds(int material) => (miningBaseSeconds + MaterialRank(material) * miningRaritySeconds
        + (material - MaterialRank(material) * 10) * miningMaterialSeconds) / (1 + LaserLevel * LaserSpeedBonus);
    public bool AddPiece(int material)
    {
        if (Used + ChunkVolume > Capacity) return false;
        int price = OrePrice(material);
        var item = Cargo.Find(value => value.Material == material && value.UnitPrice == price);
        if (item == null) { item = new CargoItem { Material = material, UnitPrice = price }; Cargo.Add(item); }
        item.Count++;
        return true;
    }
    public int SellAll() { int amount = CargoValue; Credits += amount; Cargo.Clear(); return amount; }
    public int UpgradeCost(string type)
    {
        switch (type)
        {
            case "storage": return 600 * (StorageLevel + 1);
            case "laser": return 800 * (LaserLevel + 1);
            case "quality": return 1000 * (QualityLevel + 1);
            case "spotsell": return 1200 * (SpotLevel + 1);
            default: throw new ArgumentException("Улучшения: storage, laser, quality, spotsell.");
        }
    }
    public void Upgrade(string type)
    {
        if ((type == "spotsell" && SpotLevel >= 4) || (type != "storage" && type != "spotsell" &&
            ((type == "laser" && LaserLevel >= maximumLaserLevel) || (type == "quality" && QualityLevel >= 8))))
            throw new InvalidOperationException("Достигнут максимальный уровень.");
        int price = UpgradeCost(type);
        Pay(price);
        switch (type) { case "storage": StorageLevel++; break; case "laser": LaserLevel++; break;
            case "quality": QualityLevel++; break; case "spotsell": SpotLevel++; break; }
    }
    public int RerollCost => 50 + SpotLevel * 25;
    public void RollOffers(bool paid)
    {
        if (paid) Pay(RerollCost);
        Offers.Clear();
        int minimumRank = Math.Min(2, SpotLevel / 2);
        var available = new List<int>();
        for (int rank = minimumRank; rank <= 4; rank++) available.Add(rank);
        for (int slot = 0; slot < 3; slot++)
        {
            int total = 0;
            foreach (int rank in available) total += RankWeight(rank);
            int draw = random.Next(total), selected = available[0];
            foreach (int rank in available) { draw -= RankWeight(rank); if (draw < 0) { selected = rank; break; } }
            // Keep a progressively better entry offer, then roll two distinct rarities.
            if (slot == 0) selected = minimumRank;
            available.Remove(selected);
            int material = selected == 4 ? random.Next(40, 51) : random.Next(selected * 10, selected * 10 + 10);
            string coordinates;
            do { coordinates = random.Next(100, 1000) + " " + random.Next(100, 1000) + " " + random.Next(100, 1000); }
            while (Purchased.ContainsKey(coordinates) || Offers.Exists(item => item.Coordinates == coordinates));
            int[] prices = { 350, 900, 2400, 6500, 18000 };
            Offers.Add(new Spot { Coordinates = coordinates, Material = material, Rank = selected,
                Seed = material.ToString("D2", CultureInfo.InvariantCulture) + random.Next(100000, 1000000000),
                Price = prices[selected] + prices[selected] * (material - selected * 10) / 25 });
        }
        Offers.Sort((a, b) => a.Rank.CompareTo(b.Rank));
    }
    private int RankWeight(int rank)
    {
        int[] weights = { 50, 28, 14, 6, 2 };
        return Math.Max(1, weights[rank] + SpotLevel * (rank * 5 - 8));
    }
    public Spot BuySpot(int oneBasedIndex)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > Offers.Count) throw new ArgumentException("Выберите слот: buyspot 1, 2 или 3.");
        var spot = Offers[oneBasedIndex - 1];
        if (Purchased.ContainsKey(spot.Coordinates)) throw new InvalidOperationException("Координаты уже куплены.");
        Pay(spot.Price);
        Purchased.Add(spot.Coordinates, spot);
        return spot;
    }
    private void Pay(int amount)
    {
        if (Credits < amount) throw new InvalidOperationException("Недостаточно кредитов. Нужно " + amount + ", есть " + Credits + ".");
        Credits -= amount;
    }
    public int[] Deposit(string coordinates, int count)
    {
        if (!deposits.TryGetValue(coordinates, out var result) || result.Length != count)
        {
            result = new int[count];
            for (int i = 0; i < count; i++) result[i] = PiecesPerAsteroid;
            deposits[coordinates] = result;
        }
        return result;
    }
    public static string ParseCoordinates(string input)
    {
        var parts = input.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) throw new ArgumentException("Формат: warp X Y Z (три целых числа).");
        var values = new string[3];
        for (int i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new ArgumentException("Координаты должны быть целыми числами.");
            values[i] = value.ToString(CultureInfo.InvariantCulture);
        }
        return string.Join(" ", values);
    }
}
