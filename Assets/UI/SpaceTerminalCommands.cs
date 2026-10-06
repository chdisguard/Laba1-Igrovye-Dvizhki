using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Terminal navigation commands. Moves the orbit camera's tracked object.</summary>
[RequireComponent(typeof(SpaceConsoleUI))]
[DefaultExecutionOrder(-100)]
public sealed class SpaceTerminalCommands : MonoBehaviour
{
    [SerializeField] private OrbitCamera orbitCamera;
    [SerializeField] private SpaceLocationGenerator locationGenerator;
    [SerializeField] private SpaceLocationManager locationManager;
    [SerializeField, Min(1)] private float cruiseSpeed = 200;
    [SerializeField, Range(0.1f, 12)] private float maxTravelSeconds = 12;
    [SerializeField, Min(0)] private float arrivalClearance = 10;
    [SerializeField, Range(0.1f, 3)] private float turnSeconds = 1.2f;

    private SpaceConsoleUI console;
    private Transform traveller;
    private Transform destinationObject;
    private Vector3 startPosition, endPosition;
    private float elapsed, duration;
    private bool travelling;
    private Quaternion startRotation, endRotation;
    private float rotationDuration;
    private SpaceShipEngines engines;
    private SpaceGameplay gameplay;
    public bool IsTravelling => travelling;
    public float ArrivalClearance => arrivalClearance;

    private void OnEnable()
    {
        console = GetComponent<SpaceConsoleUI>();
        gameplay = GetComponent<SpaceGameplay>();
        if (gameplay == null) gameplay = gameObject.AddComponent<SpaceGameplay>();
        console.MessageSubmitted += Execute;
        if (locationManager == null) locationManager = FindAnyObjectByType<SpaceLocationManager>();
        if (locationManager != null) locationManager.LocationChanged += StopTravel;
    }

    private void OnDisable()
    {
        if (console != null) console.MessageSubmitted -= Execute;
        if (locationManager != null) locationManager.LocationChanged -= StopTravel;
        travelling = false;
        if (engines != null) engines.SetThrottle(0);
    }

    private void Execute(string input)
    {
        int separator = 0;
        while (separator < input.Length && !char.IsWhiteSpace(input[separator])) separator++;
        string command = input.Substring(0, separator);
        string argument = input.Substring(separator).Trim();
        if (command.Equals("Help", StringComparison.OrdinalIgnoreCase))
        {
            string help = "moveto [объект] — подлететь\nscan — астероиды\nwarp X Y Z — перейти в локацию\nmine / mine stop — добыча\nstorage — груз и кредиты\nupgrade /? — улучшения\nsellall — продать груз\nbuyspot / buyspot 1..3 — купить координаты\nreroll — новые три спота\nspots — купленные координаты\nТорговый аванпост: warp 420 10 82\nhelp";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            help += "\nLocation <сид> / Location sample (разработка)";
#endif
            console.AddMessage(help);
            return;
        }
        if (gameplay != null && gameplay.TryExecute(command, argument)) return;
        if (gameplay != null && gameplay.IsWarping)
        { console.AddMessage("Дождитесь завершения варпа."); return; }
        if (command.Equals("Scan", StringComparison.OrdinalIgnoreCase))
        {
            if (locationGenerator == null) locationGenerator = FindAnyObjectByType<SpaceLocationGenerator>();
            if (locationGenerator == null) { console.AddMessage("Генератор локаций не найден."); return; }
            var names = locationGenerator.AsteroidNames;
            if (names == null) { console.AddMessage("Локация ещё не создана."); return; }
            var listing = new System.Text.StringBuilder("Астероиды:");
            foreach (var asteroid in locationGenerator.Asteroids)
                if (asteroid != null && asteroid.gameObject.activeInHierarchy)
                    listing.Append("\n").Append(asteroid.name).Append(" — ").Append(asteroid.Remaining).Append(" кусков");
            if (locationManager != null && locationManager.IsSampleLocation)
                listing.Append("\nТорговый аванпост");
            console.AddMessage(listing.ToString());
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (command.Equals("Location", StringComparison.OrdinalIgnoreCase))
        {
            if (locationGenerator == null) locationGenerator = FindAnyObjectByType<SpaceLocationGenerator>();
            if (locationGenerator == null) { console.AddMessage("Генератор локаций не найден."); return; }
            if (argument.Length >= 2 && argument[0] == '"' && argument[argument.Length - 1] == '"')
                argument = argument.Substring(1, argument.Length - 2);
            if (string.IsNullOrWhiteSpace(argument)) { console.AddMessage("Укажите числовой сид, например: Location 0112345"); return; }
            try
            {
                if (locationManager == null) locationManager = FindAnyObjectByType<SpaceLocationManager>();
                if (locationManager == null) throw new InvalidOperationException("Менеджер локаций не найден.");
                if (argument.Equals("sample", StringComparison.OrdinalIgnoreCase)) locationManager.ReturnToSampleScene();
                else locationManager.ChangeLocation(argument);
                StopTravel();
                console.AddMessage("Локация: " + locationManager.EnvironmentName);
            }
            catch (Exception exception) { console.AddMessage("Ошибка генерации: " + exception.Message); }
            return;
        }
#endif
        if (!command.Equals("Moveto", StringComparison.OrdinalIgnoreCase))
        {
            console.AddMessage("Неизвестная команда: " + command);
            return;
        }
        if (argument.Length >= 2 && argument[0] == '"' && argument[argument.Length - 1] == '"')
            argument = argument.Substring(1, argument.Length - 2).Trim();
        if (argument.Length == 0)
        {
            console.AddMessage("Укажите название объекта: Moveto [object]");
            return;
        }

        if (orbitCamera == null)
        {
            Camera camera = Camera.main;
            if (camera != null) orbitCamera = camera.GetComponent<OrbitCamera>();
        }
        if (orbitCamera == null || orbitCamera.Target == null)
        {
            console.AddMessage("Не задан объект, за которым следит камера.");
            return;
        }

        var matches = new HashSet<Transform>();
        SpaceObjectMarker selectedMarker = null;
        foreach (var marker in SpaceObjectMarker.Active)
        {
            if (!NameMatches(marker.DisplayName, argument) && !NameMatches(marker.gameObject.name, argument)) continue;
            matches.Add(marker.transform);
            selectedMarker = marker;
        }
        // Marker names take precedence; unmarked models can also be addressed by their GameObject name.
        if (matches.Count == 0)
        {
            foreach (var candidate in FindObjectsByType<Transform>())
                if (candidate.gameObject.activeInHierarchy && NameMatches(candidate.name, argument)
                    && candidate.GetComponentInChildren<Renderer>() != null)
                    matches.Add(candidate);
        }
        if (matches.Count == 0)
        {
            console.AddMessage("Объект не найден: " + argument);
            return;
        }
        if (matches.Count > 1)
        {
            console.AddMessage("Найдено несколько объектов с названием: " + argument);
            return;
        }
        Transform destination = null;
        foreach (var match in matches) destination = match;
        Transform movingObject = orbitCamera.Target;
        if (destination == movingObject || destination.IsChildOf(movingObject) || movingObject.IsChildOf(destination))
        {
            console.AddMessage("Нельзя переместиться к самому себе.");
            return;
        }

        Bounds targetBounds = GetBounds(destination);
        if (gameplay != null) gameplay.StopMining();
        Vector3 centre = selectedMarker != null ? selectedMarker.WorldPosition : targetBounds.center;
        Vector3 approach = movingObject.position - centre;
        if (approach.sqrMagnitude < 0.0001f) approach = -movingObject.forward;
        float safetyRadius = targetBounds.extents.magnitude + GetBounds(movingObject).extents.magnitude;
        Vector3 arrival = centre + approach.normalized * (safetyRadius + arrivalClearance);
        if (engines != null) engines.SetThrottle(0);
        traveller = movingObject;
        engines = traveller.GetComponent<SpaceShipEngines>();
        if (engines == null) engines = traveller.gameObject.AddComponent<SpaceShipEngines>();
        destinationObject = destination;
        startPosition = traveller.position;
        endPosition = arrival;
        elapsed = 0;
        duration = Mathf.Clamp(Vector3.Distance(startPosition, endPosition) / Mathf.Max(1, cruiseSpeed),
            0.1f, Mathf.Clamp(maxTravelSeconds, 0.1f, 12));
        startRotation = traveller.rotation;
        Vector3 flightDirection = endPosition - startPosition;
        if (flightDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 forward = flightDirection.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            endRotation = Quaternion.LookRotation(forward, up);
        }
        else endRotation = startRotation;
        float angle = Quaternion.Angle(startRotation, endRotation);
        float desiredTurnDuration = turnSeconds * angle / 180f;
        duration = Mathf.Min(Mathf.Clamp(maxTravelSeconds, 0.1f, 12), Mathf.Max(duration, desiredTurnDuration));
        rotationDuration = Mathf.Min(desiredTurnDuration, duration);
        travelling = true;
    }

    private static bool NameMatches(string name, string query) =>
        string.Equals(name.Trim(), query, StringComparison.OrdinalIgnoreCase);

    public void StopTravel()
    {
        travelling = false;
        destinationObject = null;
        if (engines != null) engines.SetThrottle(0);
    }

    private static Bounds GetBounds(Transform root)
    {
        Bounds bounds = new Bounds(root.position, Vector3.zero);
        bool found = false;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    private void LateUpdate()
    {
        if (!travelling) return;
        if (traveller == null || destinationObject == null || !destinationObject.gameObject.activeInHierarchy)
        {
            travelling = false;
            if (engines != null) engines.SetThrottle(0);
            console.AddMessage("Перемещение прервано: объект недоступен.");
            return;
        }
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        // Quintic easing: zero velocity and acceleration at both ends, peak speed halfway.
        float progress = t * t * t * (10 + t * (-15 + 6 * t));
        float turnProgress = rotationDuration > 0.001f ? Mathf.Clamp01(elapsed / rotationDuration) : 1;
        turnProgress = turnProgress * turnProgress * (3 - 2 * turnProgress);
        traveller.rotation = Quaternion.Slerp(startRotation, endRotation, turnProgress);
        traveller.position = Vector3.LerpUnclamped(startPosition, endPosition, progress);
        if (engines != null)
        {
            float speedEnvelope = 16 * t * t * (1 - t) * (1 - t);
            engines.SetThrottle((endPosition - startPosition).sqrMagnitude > 0.0001f ? speedEnvelope : 0);
        }
        if (t >= 1)
        {
            traveller.position = endPosition;
            travelling = false;
            if (engines != null) engines.SetThrottle(0);
        }
    }
}
