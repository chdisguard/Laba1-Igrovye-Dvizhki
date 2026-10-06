using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SpaceConsoleUI))]
public sealed class SpaceTerminalCommands : MonoBehaviour
{
    [SerializeField] private OrbitCamera orbitCamera;
    [SerializeField] private float cruiseSpeed = 200;
    [SerializeField] private float maxTravelSeconds = 12;
    [SerializeField] private float arrivalClearance = 10;
    private SpaceConsoleUI console;
    private Coroutine travel;
    private SpaceShipEngines engines;
    public bool IsTravelling => travel != null;
    public float ArrivalClearance => arrivalClearance;
    private void OnEnable() { console = GetComponent<SpaceConsoleUI>(); console.MessageSubmitted += Execute; }
    private void OnDisable() { console.MessageSubmitted -= Execute; StopTravel(); }
    private void Execute(string input)
    {
        int split = input.IndexOf(' ');
        string command = (split < 0 ? input : input.Substring(0, split)).ToLowerInvariant();
        string argument = split < 0 ? "" : input.Substring(split + 1).Trim().Trim('"');
        if (command == "scan")
        {
            var generator = FindFirstObjectByType<SpaceLocationGenerator>();
            if (generator != null && generator.AsteroidNames != null) console.AddMessage(string.Join("\n", generator.AsteroidNames));
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (command == "location")
        {
            try { var manager = FindFirstObjectByType<SpaceLocationManager>(); if (argument == "sample") manager.ReturnToSampleScene(); else manager.ChangeLocation(argument); }
            catch (Exception exception) { console.AddMessage(exception.Message); }
            return;
        }
#endif
        if (command == "help") { console.AddMessage("moveto <объект> — подлететь\nhelp — команды"); return; }
        if (command != "moveto") { console.AddMessage("Неизвестная команда: " + command); return; }
        SpaceObjectMarker selected = null;
        foreach (var marker in SpaceObjectMarker.Active)
            if (string.Equals(marker.DisplayName, argument, StringComparison.OrdinalIgnoreCase)) { selected = marker; break; }
        if (selected == null) { console.AddMessage("Объект не найден: " + argument); return; }
        if (orbitCamera == null && Camera.main != null) orbitCamera = Camera.main.GetComponent<OrbitCamera>();
        if (orbitCamera == null || orbitCamera.Target == null) return;
        StopTravel(); travel = StartCoroutine(Travel(selected));
    }
    public void StopTravel() { if (travel != null) StopCoroutine(travel); travel = null; if (engines != null) engines.SetThrottle(0); }
    private static Bounds BoundsOf(Transform root)
    {
        var result = new Bounds(root.position, Vector3.zero); bool first = true;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
        { if (first) { result = renderer.bounds; first = false; } else result.Encapsulate(renderer.bounds); }
        return result;
    }
    private IEnumerator Travel(SpaceObjectMarker marker)
    {
        var ship = orbitCamera.Target;
        engines = ship.GetComponent<SpaceShipEngines>();
        if (engines == null) engines = ship.gameObject.AddComponent<SpaceShipEngines>();
        Vector3 start = ship.position, centre = marker.WorldPosition;
        Vector3 approach = (start - centre).normalized;
        if (approach.sqrMagnitude < 0.01f) approach = -ship.forward;
        Vector3 end = centre + approach * (BoundsOf(marker.transform).extents.magnitude + BoundsOf(ship).extents.magnitude + arrivalClearance);
        Quaternion from = ship.rotation;
        Quaternion to = (end - start).sqrMagnitude > 0.01f ? Quaternion.LookRotation((end-start).normalized) : from;
        float duration = Mathf.Clamp(Vector3.Distance(start, end) / cruiseSpeed, 0.1f, maxTravelSeconds);
        for (float elapsed = 0; elapsed < duration; elapsed += Time.deltaTime)
        {
            if (marker == null || !marker.gameObject.activeInHierarchy) { StopTravel(); yield break; }
            float t = elapsed / duration, easing = t*t*t*(10+t*(-15+6*t));
            ship.position = Vector3.Lerp(start, end, easing); ship.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0,1,t*3));
            engines.SetThrottle(16*t*t*(1-t)*(1-t)); yield return null;
        }
        ship.position = end; engines.SetThrottle(0); travel = null;
    }
}
