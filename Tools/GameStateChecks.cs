using System;
using System.Collections.Generic;

internal static class GameStateChecks
{
    private static void Require(bool value, string detail) { if (!value) throw new Exception(detail); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } throw new Exception("Invalid action accepted"); }
    public static void Main()
    {
        var state = new SpaceGameState(42);
        Require(state.Capacity == 500 && state.Credits == 1500, "Starting balance");
        for (int i = 0; i < 20; i++) Require(state.AddPiece(0), "Cargo fill");
        Require(state.Used == 500 && !state.AddPiece(0), "Capacity overflow");
        Require(state.SellAll() == 400 && state.Used == 0 && state.Credits == 1900, "Sale arithmetic");
        state.Upgrade("storage"); Require(state.Capacity == 750 && state.Credits == 1300, "Storage upgrade");
        state.Upgrade("quality"); Require(state.OrePrice(0) == 25 && state.Credits == 300, "Quality upgrade");
        state.AddPiece(0); Require(state.CargoValue == 25, "Ore quality price");
        int before = state.Credits; Reject(() => state.Upgrade("laser"));
        Require(state.Credits == before && state.LaserLevel == 0, "Failed purchase mutates state");
        Reject(() => state.Upgrade("bad"));
        int[] deposit = state.Deposit("123 456 789", 6); deposit[2] = 0;
        Require(state.Deposit("123 456 789", 6)[2] == 0 && state.Deposit("999 888 777", 6)[2] == 20, "Deposit depletion persistence");
        Require(SpaceGameState.ParseCoordinates(" 0420 010 +82 ") == SpaceGameState.OutpostCoordinates, "Canonical coordinates");
        Require(SpaceGameState.ParseCoordinates("-1 0 999") == "-1 0 999", "Empty sector coordinates");
        Reject(() => SpaceGameState.ParseCoordinates("1 2")); Reject(() => SpaceGameState.ParseCoordinates("1 a 3"));
        Reject(() => SpaceGameState.ParseCoordinates("2147483648 2 3"));
        state = new SpaceGameState(51);
        state.RollOffers(false);
        var bought = state.BuySpot(1);
        Require(state.Purchased[bought.Coordinates] == bought && state.Credits == 1500 - bought.Price, "Spot purchase");
        int afterBuy = state.Credits; Reject(() => state.BuySpot(1)); Require(state.Credits == afterBuy, "Double spot payment");
        int previous = state.Credits; state.RollOffers(true); Require(state.Credits == previous - state.RerollCost, "Reroll payment");
        Require(state.Purchased.ContainsKey(bought.Coordinates), "Reroll deletes purchased coordinates");
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            state.RollOffers(false);
            Require(state.Offers.Count == 3 && state.Offers[0].Rank == 0, "Three choices and entry-tier availability");
            var ranks = new HashSet<int>(); var coordinates = new HashSet<string>();
            int lastPrice = 0;
            foreach (var spot in state.Offers)
            {
                Require(ranks.Add(spot.Rank) && coordinates.Add(spot.Coordinates), "Duplicate spot");
                Require(spot.Material >= 0 && spot.Material <= 50 && SpaceGameState.MaterialRank(spot.Material) == spot.Rank, "Rarity material mapping");
                Require(spot.Seed.StartsWith(spot.Material.ToString("D2")), "Seed material prefix");
                foreach (var number in spot.Coordinates.Split(' ')) Require(number.Length == 3, "Three digit coordinates");
                Require(spot.Price > lastPrice, "Rarity pricing"); lastPrice = spot.Price;
            }
        }
        for (int material = 0; material <= 50; material++)
            Require(state.MiningSeconds(material) > 0 && state.OrePrice(material) > 0, "Material balance");
        for (int i = 0; i < 20; i++) state.AddPiece(50);
        state.SellAll();
        for (int level = 0; level < 4; level++) state.Upgrade("spotsell");
        for (int i = 0; i < 100; i++)
        {
            state.RollOffers(false);
            Require(state.Offers[0].Rank == 2 && state.Offers[1].Rank == 3 && state.Offers[2].Rank == 4,
                "Spot quality does not improve offers");
        }
        float miningTime = state.MiningSeconds(0);
        state.Upgrade("laser"); Require(state.MiningSeconds(0) < miningTime, "Laser upgrade speed");
        state.ConfigureMining(4, 2, 0.25f, 0.5f, 2);
        Require(Math.Abs(state.MiningSeconds(0) - 4f / 1.5f) < 0.001f, "Mining settings ignored");
        state.Upgrade("laser");
        Reject(() => state.Upgrade("laser"));
        var highLevel = new SpaceGameState(42);
        for (int trip = 0; trip < 5; trip++)
        { for (int piece = 0; piece < 20; piece++) highLevel.AddPiece(50); highLevel.SellAll(); }
        for (int level = 0; level < 20; level++) highLevel.Upgrade("laser");
        Require(highLevel.LaserLevel == 20, "Colour progression levels unreachable");
        Reject(() => highLevel.Upgrade("laser"));
        Console.WriteLine("PASS: storage/quality/sales, payment failures, coordinate validation, deposit depletion, 1000 distinct three-tier spot rolls, material balance.");
    }
}
