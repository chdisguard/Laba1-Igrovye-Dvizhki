using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

/// <summary>Projects world anchors into a screen overlay after the orbit camera has moved.</summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class SpaceObjectMarkerOverlay : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField] private Font markerFont;
    [Header("Screen Pixels")]
    [SerializeField, Range(4, 40)] private float circleRadius = 12;
    [SerializeField] private Vector2 diagonalLength = new Vector2(38, 38);
    [SerializeField, Range(40, 300)] private float horizontalLength = 150;
    [SerializeField, Range(8, 40)] private int fontSize = 15;
    [SerializeField, Range(1, 4)] private float lineWidth = 1.5f;

    private UIDocument document;
    private PanelSettings settings;
    private FontAsset textFont;
    private FontAsset fallbackFont;
    private readonly Dictionary<SpaceObjectMarker, Callout> callouts = new Dictionary<SpaceObjectMarker, Callout>();
    private readonly List<SpaceObjectMarker> removed = new List<SpaceObjectMarker>();

    private void OnEnable()
    {
        settings = ScriptableObject.CreateInstance<PanelSettings>();
        // Positions follow world projection, while dimensions stay constant in actual screen pixels.
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1;
        settings.sortingOrder = 10;
        settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("SpaceConsoleTheme");
        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = settings;
        var root = document.rootVisualElement;
        // Standard shaping works without editor-imported ICU panel data.
        root.style.unityTextGenerator = TextGeneratorType.Standard;
        root.pickingMode = PickingMode.Ignore;
        if (markerFont != null)
        {
            textFont = FontAsset.CreateFontAsset(markerFont);
            fallbackFont = FontAsset.CreateFontAsset("Arial", "Regular");
            if (fallbackFont != null) textFont.fallbackFontAssetTable = new List<FontAsset> { fallbackFont };
            root.style.unityFontDefinition = FontDefinition.FromSDFFont(textFont);
        }
    }

    private void LateUpdate()
    {
        if (document == null) return;
        Camera camera = viewCamera != null ? viewCamera : Camera.main;
        var root = document.rootVisualElement;
        if (root.panel == null) return;

        removed.Clear();
        foreach (var pair in callouts)
            if (pair.Key == null || !SpaceObjectMarker.Active.Contains(pair.Key)) removed.Add(pair.Key);
        foreach (var marker in removed)
        {
            callouts[marker].RemoveFromHierarchy();
            callouts.Remove(marker);
        }

        foreach (var marker in SpaceObjectMarker.Active)
        {
            if (!callouts.TryGetValue(marker, out var callout))
            {
                callout = new Callout();
                callouts.Add(marker, callout);
                root.Add(callout);
            }
            if (camera == null || !camera.isActiveAndEnabled)
            {
                callout.style.display = DisplayStyle.None;
                continue;
            }

            Vector3 worldPosition = marker.WorldPosition;
            Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
            if (viewport.z < camera.nearClipPlane || viewport.z > camera.farClipPlane
                || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1)
            {
                callout.style.display = DisplayStyle.None;
                continue;
            }

            Vector3 screen = camera.WorldToScreenPoint(worldPosition);
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel,
                new Vector2(screen.x, Screen.height - screen.y));
            Vector2 position = root.WorldToLocal(panelPoint);
            float extent = circleRadius + diagonalLength.x + horizontalLength + lineWidth * 2;
            bool left = marker.Side == SpaceObjectMarker.LabelSide.Left;
            if (marker.Side == SpaceObjectMarker.LabelSide.Automatic)
                left = position.x + extent > root.contentRect.width - 8 && position.x > root.contentRect.width * 0.5f;

            callout.Configure(marker.DisplayName, left, circleRadius, diagonalLength,
                horizontalLength, fontSize, lineWidth, marker.MarkerColor);
            callout.style.display = DisplayStyle.Flex;
            callout.style.left = position.x - callout.CircleCenter.x;
            callout.style.top = position.y - callout.CircleCenter.y;
        }
    }

    private void OnValidate()
    {
        diagonalLength.x = Mathf.Max(8, diagonalLength.x);
        diagonalLength.y = Mathf.Max(8, diagonalLength.y);
    }

    private void OnDisable()
    {
        callouts.Clear();
        removed.Clear();
        if (document != null) { document.enabled = false; Destroy(document); }
        if (settings != null) Destroy(settings);
        if (textFont != null) Destroy(textFont);
        if (fallbackFont != null) Destroy(fallbackFont);
    }

    private sealed class Callout : VisualElement
    {
        private readonly Label label;
        private Vector2 lineStart, elbow, lineEnd;
        private float radius, strokeWidth;
        private Color color;
        public Vector2 CircleCenter { get; private set; }

        public Callout()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.style.position = Position.Absolute;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.marginLeft = label.style.marginRight = 0;
            label.style.marginTop = label.style.marginBottom = 0;
            label.style.paddingLeft = label.style.paddingRight = 0;
            label.style.paddingTop = label.style.paddingBottom = 0;
            Add(label);
            generateVisualContent += Draw;
        }

        public void Configure(string text, bool left, float circleRadius, Vector2 diagonal,
            float horizontal, int textSize, float thickness, Color tint)
        {
            radius = circleRadius;
            strokeWidth = thickness;
            color = tint;
            float ringCorner = radius / Mathf.Sqrt(2);
            float padding = thickness * 2;
            float height = textSize + 8 + diagonal.y + ringCorner + radius + padding;
            float width = radius + ringCorner + diagonal.x + horizontal + padding * 2;
            CircleCenter = new Vector2(left ? width - radius - padding : radius + padding, height - radius - padding);
            float direction = left ? -1 : 1;
            lineStart = CircleCenter + new Vector2(direction * ringCorner, -ringCorner);
            elbow = lineStart + new Vector2(direction * diagonal.x, -diagonal.y);
            lineEnd = elbow + new Vector2(direction * horizontal, 0);
            style.width = width;
            style.height = height;
            label.text = text;
            label.style.fontSize = textSize;
            label.style.color = tint;
            label.style.left = left ? lineEnd.x : elbow.x;
            label.style.top = elbow.y - textSize - 8;
            label.style.width = horizontal;
            label.style.height = textSize + 4;
            label.style.unityTextAlign = left ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.strokeColor = color;
            painter.lineWidth = strokeWidth;
            painter.BeginPath();
            painter.Arc(CircleCenter, radius, new Angle(0, AngleUnit.Degree), new Angle(360, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();
            painter.BeginPath();
            painter.MoveTo(lineStart);
            painter.LineTo(elbow);
            painter.LineTo(lineEnd);
            painter.Stroke();
        }
    }
}
