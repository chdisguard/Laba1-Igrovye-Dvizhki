using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>Console gameplay, tractor mining and the animated transition between sectors.</summary>
[DisallowMultipleComponent]
public sealed class SpaceGameplay : MonoBehaviour
{
    [Header("Mining")]
    [SerializeField] private SpaceMiningLaserSettings miningLaserSettings;
    [Header("Trading")]
    [SerializeField, Min(0)] private float outpostRangeTolerance = 8;
    public SpaceGameState State { get; private set; } = new SpaceGameState();
    public string Coordinates { get; private set; } = SpaceGameState.OutpostCoordinates;
    public bool IsWarping { get; private set; }
    public bool IsMining => miningTarget != null;
    private SpaceConsoleUI console;
    private SpaceTerminalCommands navigation;
    private SpaceLocationManager manager;
    private SpaceLocationGenerator generator;
    private OrbitCamera orbit;
    private Transform ship;
    private Camera view;
    private SpaceShipEngines engines;
    private SpaceMineableAsteroid miningTarget;
    private float miningClock;
    private SpaceMiningLaser miningLaser;
    private GameObject warpEffects;
    private LineRenderer[] streaks;
    private Vector3[] streakOffsets;
    private float[] streakLengths;
    private float warpClock, originalFov;
    private Vector3 departure;
    private Quaternion initialRotation, flightRotation;
    private string destination;
    private bool transferred, initialDepositBound;
    private const float TurnTime = 1.2f, AccelerationTime = 2.3f, WarpDuration = 7.5f;

    private void Awake() => Initialize();
    public void Initialize()
    {
        if (miningLaserSettings == null) miningLaserSettings = Resources.Load<SpaceMiningLaserSettings>("MiningLaserSettings");
        if (miningLaserSettings != null) State.ConfigureMining(miningLaserSettings.BaseSecondsPerPiece,
            miningLaserSettings.SecondsPerRarity, miningLaserSettings.SecondsPerMaterialStep,
            miningLaserSettings.SpeedBonusPerLevel, miningLaserSettings.MaximumLaserLevel);
        console = GetComponent<SpaceConsoleUI>();
        navigation = GetComponent<SpaceTerminalCommands>();
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (manager == null) manager = root.GetComponentInChildren<SpaceLocationManager>(true);
            if (orbit == null) orbit = root.GetComponentInChildren<OrbitCamera>(true);
        }
        if (manager != null) generator = manager.GetComponent<SpaceLocationGenerator>();
        if (orbit != null) { ship = orbit.Target; view = orbit.GetComponent<Camera>(); }
        if (ship != null) engines = ship.GetComponent<SpaceShipEngines>();
    }
    private void OnEnable()
    {
        Initialize();
        if (manager != null) manager.LocationChanged += OnLocationChanged;
    }
    private void OnDisable()
    {
        if (manager != null) manager.LocationChanged -= OnLocationChanged;
        StopMining();
        FinishWarp();
    }
    private void OnDestroy()
    {
        Release(warpEffects);
        if (miningLaser != null) Release(miningLaser);
    }
    private static void Release(UnityEngine.Object value)
    { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    private void Say(string message) { if (console != null) console.AddMessage(message); }

    public bool TryExecute(string command, string argument)
    {
        command = command.ToLowerInvariant();
        if (command != "warp" && command != "mine" && command != "storage" && command != "upgrade"
            && command != "sellall" && command != "buyspot" && command != "reroll" && command != "spots") return false;
        Initialize();
        try
        {
            if (command == "storage") { PrintStorage(); return true; }
            if (command == "spots") { PrintPurchased(); return true; }
            if (command == "upgrade" && (argument == "/?" || argument == "?" || argument.Length == 0))
            { PrintUpgrades(); return true; }
            if (IsWarping) throw new InvalidOperationException("Дождитесь завершения варпа.");
            switch (command)
            {
                case "warp": BeginWarp(SpaceGameState.ParseCoordinates(argument)); break;
                case "mine":
                    if (argument.Equals("stop", StringComparison.OrdinalIgnoreCase)) { StopMining(); Say("Добыча остановлена."); }
                    else if (argument.Length > 0) throw new ArgumentException("mine / mine stop");
                    else BeginMining();
                    break;
                case "upgrade":
                    RequireOutpost();
                    string type = argument.Trim(' ', '/', '\\').ToLowerInvariant();
                    State.Upgrade(type);
                    Say("Улучшение " + type + " куплено. Кредиты: " + State.Credits + ".");
                    PrintUpgrades(); break;
                case "sellall":
                    RequireOutpost(); StopMining();
                    int revenue = State.SellAll();
                    Say("Груз продан за " + revenue + ". Кредиты: " + State.Credits + "."); break;
                case "buyspot":
                    RequireOutpost();
                    if (argument.Length == 0) { if (State.Offers.Count == 0) State.RollOffers(false); PrintOffers(); }
                    else
                    {
                        if (!int.TryParse(argument, out int index)) throw new ArgumentException("buyspot 1, 2 или 3");
                        var bought = State.BuySpot(index);
                        Say("Куплен спот " + SpaceGameState.Ranks[bought.Rank] + ": " + MaterialName(bought.Material)
                            + "\nКоординаты: " + bought.Coordinates + "\nwarp " + bought.Coordinates + "\nКредиты: " + State.Credits);
                    }
                    break;
                case "reroll": RequireOutpost(); State.RollOffers(true); PrintOffers(); break;
            }
        }
        catch (Exception exception) { Say(exception.Message); }
        return true;
    }
    private static string MaterialName(int material)
    {
        return AsteroidDepositCatalog.Resolve(material.ToString("D2", CultureInfo.InvariantCulture)).Name;
    }
    private void PrintStorage()
    {
        var text = new StringBuilder("Хранилище: " + State.Used + " / " + State.Capacity + "\nКусок: " + SpaceGameState.ChunkVolume
            + " объёма. Кредиты: " + State.Credits);
        foreach (var item in State.Cargo)
            text.Append("\n").Append(MaterialName(item.Material)).Append(" × ").Append(item.Count)
                .Append(", цена: ").Append(item.UnitPrice).Append("/кусок, всего: ").Append(item.UnitPrice * item.Count);
        if (State.Cargo.Count == 0) text.Append("\nГруз отсутствует.");
        text.Append("\nСтоимость груза: ").Append(State.CargoValue);
        Say(text.ToString());
    }
    private void PrintUpgrades()
    {
        Say("Улучшения у аванпоста (upgrade <тип>):\nstorage: +250 объёма — " + State.UpgradeCost("storage")
            + "\nlaser: +" + (State.LaserSpeedBonus * 100).ToString("0.#") + "% к скорости за уровень — " + State.UpgradeCost("laser")
            + "\nquality: +25% к цене новой руды за уровень — " + State.UpgradeCost("quality")
            + "\nspotsell: повышает шанс редких спотов — " + State.UpgradeCost("spotsell")
            + "\nУровни: storage " + State.StorageLevel + ", laser " + State.LaserLevel + ", quality " + State.QualityLevel
            + ", spotsell " + State.SpotLevel + "\nКредиты: " + State.Credits);
    }
    private void PrintOffers()
    {
        var text = new StringBuilder("Споты (buyspot <номер>):");
        for (int i = 0; i < State.Offers.Count; i++)
        {
            var item = State.Offers[i];
            text.Append("\n").Append(i + 1).Append(". [").Append(SpaceGameState.Ranks[item.Rank]).Append("] ")
                .Append(MaterialName(item.Material)).Append(" — ").Append(item.Price)
                .Append(State.Purchased.ContainsKey(item.Coordinates) ? " (куплен)" : " кредитов");
        }
        text.Append("\nreroll: ").Append(State.RerollCost).Append(" кредитов. Баланс: ").Append(State.Credits);
        Say(text.ToString());
    }
    private void PrintPurchased()
    {
        var text = new StringBuilder("Аванпост: " + SpaceGameState.OutpostCoordinates + "\nТекущие координаты: " + Coordinates);
        foreach (var item in State.Purchased.Values)
            text.Append("\n[").Append(SpaceGameState.Ranks[item.Rank]).Append("] ").Append(MaterialName(item.Material))
                .Append(" — warp ").Append(item.Coordinates);
        Say(text.ToString());
    }
    public bool IsNearOutpost => manager != null && manager.IsSampleLocation && manager.TradingOutpost != null
        && manager.TradingOutpost.activeInHierarchy && ship != null
        && SpaceMineableAsteroid.Clearance(ship, manager.TradingOutpost.transform)
            <= (navigation != null ? navigation.ArrivalClearance : 10) + outpostRangeTolerance;
    private float MiningRange => (navigation != null ? navigation.ArrivalClearance : 10)
        + (miningLaserSettings != null ? miningLaserSettings.RangeTolerance : 8);
    private void RequireOutpost()
    {
        if (navigation != null && navigation.IsTravelling) throw new InvalidOperationException("Сначала завершите перемещение.");
        if (!IsNearOutpost) throw new InvalidOperationException("Подлетите к торговому аванпосту: warp 420 10 82, затем moveto Торговый аванпост.");
    }
    private void OnLocationChanged()
    {
        StopMining();
        initialDepositBound = false;
    }
    public void BindDeposit()
    {
        if (generator == null || generator.AsteroidCount == 0) return;
        int[] pieces = State.Deposit(Coordinates + "|" + generator.Seed, generator.AsteroidCount);
        foreach (var asteroid in generator.Asteroids)
            asteroid.SetRemaining(pieces[asteroid.Index]);
        initialDepositBound = true;
    }
    public void BeginMining()
    {
        if (IsWarping) throw new InvalidOperationException("Дождитесь завершения варпа.");
        if (ship == null || generator == null) throw new InvalidOperationException("Корабль или локация не найдены.");
        if (navigation != null && navigation.IsTravelling) throw new InvalidOperationException("Дождитесь завершения moveto.");
        if (State.Used + SpaceGameState.ChunkVolume > State.Capacity) throw new InvalidOperationException("Хранилище заполнено.");
        if (!initialDepositBound) BindDeposit();
        float nearest = float.MaxValue;
        SpaceMineableAsteroid selected = null;
        foreach (var asteroid in generator.Asteroids)
        {
            if (asteroid == null || !asteroid.gameObject.activeInHierarchy) continue;
            float distance = SpaceMineableAsteroid.Clearance(ship, asteroid.transform);
            if (asteroid.Remaining > 0 && distance <= MiningRange && distance < nearest) { selected = asteroid; nearest = distance; }
        }
        if (selected == null) throw new InvalidOperationException("В радиусе лазера нет астероидов. Используйте scan и moveto <название>.");
        StopMining();
        if (miningLaser == null) miningLaser = ship.GetComponent<SpaceMiningLaser>();
        if (miningLaser == null) miningLaser = ship.gameObject.AddComponent<SpaceMiningLaser>();
        miningLaser.Begin(selected.transform, miningLaserSettings, State.LaserLevel);
        miningTarget = selected;
        miningClock = 0;
        Say("Добыча: " + selected.name + ". " + State.MiningSeconds(selected.Material).ToString("0.0", CultureInfo.InvariantCulture)
            + " с/кусок, " + State.OrePrice(selected.Material) + " кредитов/кусок. Осталось: " + selected.Remaining + ".");
    }
    public void StopMining()
    {
        miningTarget = null;
        miningClock = 0;
        if (miningLaser != null) miningLaser.Stop();
    }
    private void AdvanceMining(float delta)
    {
        if (miningTarget == null) return;
        if (!miningTarget.gameObject.activeInHierarchy || SpaceMineableAsteroid.Clearance(ship, miningTarget.transform) > MiningRange)
        { StopMining(); Say("Добыча прервана: астероид вне радиуса лазера."); return; }
        Color oreColor = miningTarget.GetComponent<SpaceObjectMarker>().MarkerColor;
        miningLaser.Advance(delta, State.LaserLevel, oreColor);
        miningClock += delta;
        float interval = State.MiningSeconds(miningTarget.Material);
        while (miningClock >= interval && miningTarget != null)
        {
            miningClock -= interval;
            if (!State.AddPiece(miningTarget.Material)) { StopMining(); Say("Хранилище заполнено. storage — содержимое."); break; }
            int left = miningTarget.Remaining - 1;
            State.Deposit(Coordinates + "|" + generator.Seed, generator.AsteroidCount)[miningTarget.Index] = left;
            string name = miningTarget.name;
            miningTarget.SetRemaining(left);
            Say("+1 " + MaterialName(miningTarget.Material) + ". Груз: " + State.Used + "/" + State.Capacity + ". Осталось: " + left + ".");
            if (left == 0) { StopMining(); Say(name + " исчерпан."); }
            else if (State.Used + SpaceGameState.ChunkVolume > State.Capacity) { StopMining(); Say("Хранилище заполнено."); }
        }
    }
    public void BeginWarp(string targetCoordinates)
    {
        targetCoordinates = SpaceGameState.ParseCoordinates(targetCoordinates);
        if (IsWarping) throw new InvalidOperationException("Варп уже выполняется.");
        if (ship == null || manager == null) throw new InvalidOperationException("Не настроен корабль или менеджер локаций.");
        if (targetCoordinates == Coordinates && (targetCoordinates != SpaceGameState.OutpostCoordinates || manager.IsSampleLocation))
            throw new InvalidOperationException("Вы уже в этой локации.");
        StopMining();
        if (navigation != null) navigation.StopTravel();
        destination = targetCoordinates;
        departure = ship.position;
        initialRotation = ship.rotation;
        var from = Coordinates.Split(' '); var to = destination.Split(' ');
        Vector3 direction = new Vector3((float)int.Parse(to[0]) - int.Parse(from[0]),
            (float)int.Parse(to[1]) - int.Parse(from[1]), (float)int.Parse(to[2]) - int.Parse(from[2])).normalized;
        if (direction.sqrMagnitude < 0.001f) direction = ship.forward;
        Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
        flightRotation = Quaternion.LookRotation(direction, up);
        warpClock = 0; transferred = false; IsWarping = true;
        if (view != null) originalFov = view.fieldOfView;
        if (orbit != null) orbit.WarpView = true;
        CreateWarpEffects();
        Say("Курс: " + destination + ". Поворот и разгон...");
    }
    private void CreateWarpEffects()
    {
        if (warpEffects != null) return;
        warpEffects = new GameObject("Варп-туннель");
        warpEffects.transform.SetParent(ship, false);
        streaks = new LineRenderer[64]; streakOffsets = new Vector3[64]; streakLengths = new float[64];
        var random = new System.Random(4201082);
        Material glow = Resources.Load<Material>("EngineFlame");
        for (int i = 0; i < streaks.Length; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2, radius = 12 + (float)random.NextDouble() * 75;
            streakOffsets[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, (float)random.NextDouble() * 700);
            streakLengths[i] = 20 + (float)random.NextDouble() * 90;
            var line = new GameObject("Звёздный след").AddComponent<LineRenderer>();
            line.transform.SetParent(warpEffects.transform, false);
            line.sharedMaterial = glow; line.useWorldSpace = false; line.positionCount = 2;
            line.startWidth = 0.08f; line.endWidth = 0.35f;
            line.startColor = new Color(0.3f, 0.7f, 1, 0); line.endColor = new Color(0.5f, 0.8f, 1, 0.9f);
            streaks[i] = line;
        }
        warpEffects.SetActive(false);
    }
    private void AdvanceWarp(float delta)
    {
        if (!IsWarping) return;
        warpClock += delta;
        float turn = Mathf.SmoothStep(0, 1, warpClock / TurnTime);
        ship.rotation = Quaternion.Slerp(initialRotation, flightRotation, turn);
        float acceleration = Mathf.SmoothStep(0, 1, (warpClock - TurnTime) / AccelerationTime);
        float deceleration = 1 - Mathf.SmoothStep(0, 1, (warpClock - (WarpDuration - 1.5f)) / 1.5f);
        float strength = acceleration * deceleration;
        if (engines != null) engines.SetThrottle(strength);
        if (view != null) view.fieldOfView = Mathf.Lerp(originalFov, Mathf.Min(105, originalFov + 35), strength);
        warpEffects.SetActive(warpClock > TurnTime && strength > 0.01f);
        for (int i = 0; i < streaks.Length; i++)
        {
            var point = streakOffsets[i];
            point.z = Mathf.Repeat(point.z - (warpClock - TurnTime) * 650 * acceleration, 700) - 45;
            streaks[i].SetPosition(0, point); streaks[i].SetPosition(1, point + Vector3.forward * streakLengths[i] * strength);
        }
        if (!transferred) ship.position = departure + flightRotation * Vector3.forward * (600 * acceleration * acceleration);
        if (!transferred && warpClock >= 4.8f)
        {
            Vector3 previous = ship.position;
            try
            {
                if (destination == SpaceGameState.OutpostCoordinates) manager.ReturnToSampleScene(false);
                else
                {
                    ship.position = Vector3.zero;
                    if (State.Purchased.TryGetValue(destination, out var spot)) manager.ChangeLocation(spot.Seed);
                    else manager.ChangeToEmpty("00" + destination.Replace(" ", "").Replace("-", ""));
                }
                Coordinates = destination;
                transferred = true;
                BindDeposit();
            }
            catch (Exception exception) { ship.position = previous; FinishWarp(); Say("Варп прерван: " + exception.Message); return; }
        }
        if (warpClock >= WarpDuration) { FinishWarp(); Say("Прибытие: " + Coordinates + "\n" + manager.EnvironmentName); }
    }
    private void FinishWarp()
    {
        if (!IsWarping) return;
        IsWarping = false;
        if (warpEffects != null) warpEffects.SetActive(false);
        if (view != null) view.fieldOfView = originalFov;
        if (orbit != null) orbit.WarpView = false;
        if (engines != null) engines.SetThrottle(0);
    }
    public void Advance(float delta)
    {
        if (!initialDepositBound) BindDeposit();
        AdvanceWarp(delta);
        AdvanceMining(delta);
    }
    private void Update() => Advance(Time.deltaTime);
}
