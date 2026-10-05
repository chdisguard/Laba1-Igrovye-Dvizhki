using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitCamera : MonoBehaviour
{
    [SerializeField] private Transform target;

    public Transform Target => target;
    private bool warpView;
    private Quaternion warpCameraStart;
    private float warpCameraBlend;
    public bool WarpView
    {
        get => warpView;
        set
        {
            if (warpView == value) return;
            warpView = value;
            if (value) { warpCameraStart = transform.rotation; warpCameraBlend = 0; }
            else
            {
                yaw = transform.eulerAngles.y;
                pitch = transform.eulerAngles.x;
                if (pitch > 180) pitch -= 360;
                pitch = Mathf.Clamp(pitch, -89, 89);
            }
        }
    }

    [SerializeField] private float distance = 10f;
    [SerializeField] private float minDistance = 3f;
    [SerializeField] private float maxDistance = 30f;

    [SerializeField] private float rotationSpeed = 0.2f;
    [SerializeField] private float zoomSpeed = 0.01f;

    private float yaw;
    private float pitch;

    void Update()
    {
        if (WarpView) { warpCameraBlend += Time.deltaTime; return; }
        if (Mouse.current == null)
            return;

        if (SpaceConsoleUI.BlocksPointer(Mouse.current.position.ReadValue()))
            return;

        // Вращение камеры при зажатой левой кнопке мыши
        if (Mouse.current.leftButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();

            yaw += mouseDelta.x * rotationSpeed;
            pitch -= mouseDelta.y * rotationSpeed;

            // Чтобы камера не переворачивалась через полюса
            pitch = Mathf.Clamp(pitch, -89f, 89f);
        }

        // Зум колёсиком
        float scroll = Mouse.current.scroll.ReadValue().y;

        distance -= scroll * zoomSpeed;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        Quaternion rotation = WarpView
            ? Quaternion.Slerp(warpCameraStart, target.rotation, Mathf.SmoothStep(0, 1, warpCameraBlend / 1.2f))
            : Quaternion.Euler(pitch, yaw, 0f);

        Vector3 offset = rotation * new Vector3(0f, 0f, -distance);

        transform.position = target.position + offset;

        transform.LookAt(target);
    }
}
