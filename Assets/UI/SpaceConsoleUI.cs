using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

/// <summary>Ship message overlay. Gameplay can subscribe to MessageSubmitted and call AddMessage.</summary>
public sealed class SpaceConsoleUI : MonoBehaviour
{
    [SerializeField] private Font consoleFont;
    [SerializeField] private Font headingFont;
    [SerializeField] private StyleSheet styleSheet;
    [SerializeField] private ThemeStyleSheet themeStyleSheet;
    [SerializeField, Range(20, 500)] private int messageLimit = 100;

    [Header("Font Sizes")]
    [SerializeField, Range(8, 48)] private int titleFontSize = 15;
    [SerializeField, Range(8, 48)] private int messageFontSize = 15;
    [SerializeField, Range(8, 48)] private int inputFontSize = 16;
    [SerializeField, Range(8, 36)] private int metadataFontSize = 12;

    public event Action<string> MessageSubmitted;
    public string LogFilePath => Path.Combine(Application.persistentDataPath, "Logs", "messages.jsonl");
    private static SpaceConsoleUI active;
    private UIDocument document;
    private PanelSettings settings;
    private FontAsset textFontAsset;
    private FontAsset fallbackFontAsset;
    private VisualElement terminal;
    private ScrollView history;
    private TextField input;
    private bool scrollToBottomPending;
    private IVisualElementScheduledItem scrollToBottomTask;
    private readonly List<string> commands = new List<string>();
    private int commandIndex;
    private string draft = "";
    private Rect windowRect;
    private bool hasPlacement;
    private int gesturePointer = -1;
    private Vector2 gestureStart;
    private Rect gestureRect;
    private int resizeEdges;
    private const int LeftEdge = 1, RightEdge = 2, TopEdge = 4, BottomEdge = 8;

    private void OnEnable()
    {
        active = this;
        settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1920, 1080);
        settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        settings.match = 0.5f;
        settings.sortingOrder = 100;
        settings.themeStyleSheet = themeStyleSheet != null
            ? themeStyleSheet : Resources.Load<ThemeStyleSheet>("SpaceConsoleTheme");
        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = settings;

        VisualElement root = document.rootVisualElement;
        // Runtime-created panels have no imported ICU data for advanced shaping.
        root.style.unityTextGenerator = TextGeneratorType.Standard;
        root.pickingMode = PickingMode.Ignore;
        if (styleSheet != null) root.styleSheets.Add(styleSheet);
        if (consoleFont != null)
        {
            textFontAsset = FontAsset.CreateFontAsset(consoleFont);
            // LegacyRuntime has no embedded font bytes, so use an OS font for missing Cyrillic glyphs.
            fallbackFontAsset = FontAsset.CreateFontAsset("Arial", "Regular");
            if (fallbackFontAsset != null)
                textFontAsset.fallbackFontAssetTable = new List<FontAsset> { fallbackFontAsset };
            root.style.unityFontDefinition = FontDefinition.FromSDFFont(textFontAsset);
        }

        terminal = new VisualElement { name = "space-console" };
        terminal.AddToClassList("terminal");
        root.Add(terminal);
        root.RegisterCallback<GeometryChangedEvent>(_ => FitWindow());
        terminal.RegisterCallback<PointerDownEvent>(BeginWindowGesture, TrickleDown.TrickleDown);
        terminal.RegisterCallback<PointerMoveEvent>(MoveWindowGesture);
        terminal.RegisterCallback<PointerUpEvent>(EndWindowGesture);
        terminal.RegisterCallback<PointerCancelEvent>(EndWindowGesture);
        terminal.RegisterCallback<PointerCaptureOutEvent>(_ =>
        {
            gesturePointer = -1;
            terminal.RemoveFromClassList("window-manipulating");
        });

        var header = Element("terminal-header");
        var title = Label("Terminal", "terminal-title");
        if (headingFont != null) title.style.unityFontDefinition = FontDefinition.FromFont(headingFont);
        header.Add(title);
        terminal.Add(header);

        history = new ScrollView(ScrollViewMode.Vertical) { name = "message-history" };
        history.AddToClassList("history");
        history.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        history.verticalScrollerVisibility = ScrollerVisibility.Auto;
        history.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => ScheduleScrollToBottom());
        // Cancel a queued follow operation if the user starts navigating the history.
        history.RegisterCallback<WheelEvent>(_ => CancelScrollToBottom(), TrickleDown.TrickleDown);
        history.verticalScroller.RegisterCallback<PointerDownEvent>(_ => CancelScrollToBottom(), TrickleDown.TrickleDown);
        terminal.Add(history);

        var entry = Element("entry");
        entry.Add(Label(">_", "prompt"));
        input = new TextField { name = "command-input", multiline = false, maxLength = 256 };
        input.AddToClassList("command-input");
        input.textEdition.placeholder = "Введите сообщение...";
        input.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        input.RegisterCallback<FocusInEvent>(_ => entry.AddToClassList("entry-focused"));
        input.RegisterCallback<FocusOutEvent>(_ => entry.RemoveFromClassList("entry-focused"));
        entry.Add(input);
        terminal.Add(entry);
        var grip = Label("◢", "resize-grip");
        terminal.Add(grip);
        AddMessage("Введите help для просмотра списка команд", "System");
        ApplyFontSizes();
        commandIndex = commands.Count;
    }

    private void OnValidate()
    {
        if (terminal != null) terminal.schedule.Execute(ApplyFontSizes);
    }

    private void ApplyFontSizes()
    {
        if (terminal == null) return;
        terminal.style.fontSize = messageFontSize;
        terminal.Q<Label>(className: "terminal-title").style.fontSize = titleFontSize;
        terminal.Q(className: "terminal-header").style.minHeight = Mathf.Max(45, titleFontSize + 20);
        terminal.Q<Label>(className: "prompt").style.fontSize = inputFontSize;
        terminal.Q<Label>(className: "resize-grip").style.fontSize = inputFontSize;
        terminal.Q(className: "entry").style.minHeight = Mathf.Max(46, inputFontSize + 24);
        input.style.fontSize = inputFontSize;
        var inputElement = input.Q(className: "unity-text-field__input");
        if (inputElement != null) inputElement.style.fontSize = inputFontSize;
        history.Query<Label>(className: "timestamp").ForEach(label => ApplyMetadataSize(label, 5.7f));
        history.Query<Label>(className: "source-system").ForEach(label => ApplyMetadataSize(label, 6f));
        history.Query<Label>(className: "source-user").ForEach(label => ApplyMetadataSize(label, 6f));
    }

    private void ApplyMetadataSize(Label label, float widthInCharacters)
    {
        label.style.fontSize = metadataFontSize;
        label.style.width = metadataFontSize * widthInCharacters;
    }

    private Vector2 ViewportSize => document.rootVisualElement.contentRect.size;

    private void FitWindow()
    {
        Vector2 viewport = ViewportSize;
        if (viewport.x <= 0 || viewport.y <= 0) return;
        if (!hasPlacement)
        {
            windowRect = new Rect(28, viewport.y - 334, 540, 306);
            hasPlacement = true;
        }
        windowRect.width = Mathf.Clamp(windowRect.width, Mathf.Min(360, viewport.x), viewport.x);
        windowRect.height = Mathf.Clamp(windowRect.height, Mathf.Min(210, viewport.y), viewport.y);
        windowRect.x = Mathf.Clamp(windowRect.x, 0, viewport.x - windowRect.width);
        windowRect.y = Mathf.Clamp(windowRect.y, 0, viewport.y - windowRect.height);
        ApplyWindowRect();
    }

    private void ApplyWindowRect()
    {
        terminal.style.bottom = StyleKeyword.Auto;
        terminal.style.left = windowRect.x;
        terminal.style.top = windowRect.y;
        terminal.style.width = windowRect.width;
        terminal.style.height = windowRect.height;
    }

    private void BeginWindowGesture(PointerDownEvent evt)
    {
        if (evt.button != 0 || gesturePointer != -1) return;
        Vector2 local = terminal.WorldToLocal(evt.position);
        resizeEdges = 0;
        if (local.x <= 8) resizeEdges |= LeftEdge;
        else if (local.x >= terminal.layout.width - 8) resizeEdges |= RightEdge;
        if (local.y <= 8) resizeEdges |= TopEdge;
        else if (local.y >= terminal.layout.height - 8) resizeEdges |= BottomEdge;
        if (local.x >= terminal.layout.width - 20 && local.y >= terminal.layout.height - 20)
            resizeEdges = RightEdge | BottomEdge;

        bool onHeader = false;
        for (var target = evt.target as VisualElement; target != null && target != terminal; target = target.parent)
            if (target.ClassListContains("terminal-header")) onHeader = true;
        if (resizeEdges == 0 && !onHeader) return;

        FitWindow();
        gesturePointer = evt.pointerId;
        gestureStart = document.rootVisualElement.WorldToLocal(evt.position);
        gestureRect = windowRect;
        terminal.CapturePointer(gesturePointer);
        terminal.AddToClassList("window-manipulating");
        evt.StopImmediatePropagation();
    }

    private void MoveWindowGesture(PointerMoveEvent evt)
    {
        if (evt.pointerId != gesturePointer) return;
        Vector2 position = document.rootVisualElement.WorldToLocal(evt.position);
        Vector2 delta = position - gestureStart;
        Vector2 viewport = ViewportSize;
        if (resizeEdges == 0)
        {
            windowRect = gestureRect;
            windowRect.position += delta;
            FitWindow();
        }
        else
        {
            float minWidth = Mathf.Min(360, viewport.x);
            float minHeight = Mathf.Min(210, viewport.y);
            float left = gestureRect.xMin, right = gestureRect.xMax;
            float top = gestureRect.yMin, bottom = gestureRect.yMax;
            if ((resizeEdges & LeftEdge) != 0) left = Mathf.Clamp(left + delta.x, 0, right - minWidth);
            if ((resizeEdges & RightEdge) != 0) right = Mathf.Clamp(right + delta.x, left + minWidth, viewport.x);
            if ((resizeEdges & TopEdge) != 0) top = Mathf.Clamp(top + delta.y, 0, bottom - minHeight);
            if ((resizeEdges & BottomEdge) != 0) bottom = Mathf.Clamp(bottom + delta.y, top + minHeight, viewport.y);
            windowRect = Rect.MinMaxRect(left, top, right, bottom);
            ApplyWindowRect();
        }
        evt.StopImmediatePropagation();
    }

    private void EndWindowGesture(IPointerEvent evt)
    {
        if (evt.pointerId != gesturePointer) return;
        int pointer = gesturePointer;
        gesturePointer = -1;
        terminal.RemoveFromClassList("window-manipulating");
        terminal.ReleasePointer(pointer);
        (evt as EventBase)?.StopImmediatePropagation();
    }

    private static VisualElement Element(string className)
    {
        var element = new VisualElement();
        element.AddToClassList(className);
        return element;
    }

    private static Label Label(string text, string className)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(className);
        return label;
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            evt.StopImmediatePropagation();
            Submit();
        }
        else if (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow)
        {
            evt.StopImmediatePropagation();
            if (commands.Count == 0) return;
            if (commandIndex == commands.Count) draft = input.value;
            commandIndex = Mathf.Clamp(commandIndex + (evt.keyCode == KeyCode.UpArrow ? -1 : 1), 0, commands.Count);
            input.SetValueWithoutNotify(commandIndex == commands.Count ? draft : commands[commandIndex]);
            input.SelectRange(input.value.Length, input.value.Length);
        }
        else if (evt.keyCode == KeyCode.Escape)
        {
            evt.StopImmediatePropagation();
            input.Blur();
        }
    }

    private void Submit()
    {
        string message = input.value.Trim();
        if (message.Length == 0) return;
        if (commands.Count == 0 || commands[commands.Count - 1] != message) commands.Add(message);
        if (commands.Count > messageLimit) commands.RemoveAt(0);
        commandIndex = commands.Count;
        draft = "";
        input.SetValueWithoutNotify("");
        AddMessage(message, "You");
        MessageSubmitted?.Invoke(message);
        input.schedule.Execute(() => input.Focus());
    }

    public void AddMessage(string message, string source = "System")
    {
        if (history == null || string.IsNullOrEmpty(message)) return;
        var scroller = history.verticalScroller;
        bool wasAtBottom = scroller.highValue <= scroller.lowValue
            || scroller.value >= scroller.highValue - 1f;
        scrollToBottomPending = scrollToBottomPending || wasAtBottom;
        WriteLog(message, source);
        var row = Element("message");
        row.RegisterCallback<GeometryChangedEvent>(_ => ScheduleScrollToBottom());
        var timestamp = Label(DateTime.Now.ToString("HH:mm:ss"), "timestamp");
        ApplyMetadataSize(timestamp, 5.7f);
        row.Add(timestamp);
        var author = Label("[" + source + "]:", source == "You" ? "source-user" : "source-system");
        ApplyMetadataSize(author, 6f);
        row.Add(author);
        var body = Label(message, "message-body");
        body.enableRichText = false;
        row.Add(body);
        history.Add(row);
        while (history.contentContainer.childCount > messageLimit) history.RemoveAt(0);
        // GeometryChanged schedules scrolling after the new content has been laid out.
    }

    private void ScheduleScrollToBottom()
    {
        if (!scrollToBottomPending || history == null || scrollToBottomTask != null) return;
        scrollToBottomTask = history.schedule.Execute(() =>
        {
            scrollToBottomTask = null;
            if (!scrollToBottomPending || history == null) return;
            history.verticalScroller.value = history.verticalScroller.highValue;
            scrollToBottomPending = false;
        });
    }

    private void CancelScrollToBottom()
    {
        scrollToBottomPending = false;
        scrollToBottomTask?.Pause();
        scrollToBottomTask = null;
    }

    [Serializable]
    private sealed class LogEntry
    {
        public string timestamp;
        public string source;
        public string message;
    }

    private void WriteLog(string message, string source)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath));
            var entry = new LogEntry { timestamp = DateTimeOffset.Now.ToString("O"), source = source, message = message };
            File.AppendAllText(LogFilePath, JsonUtility.ToJson(entry) + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogWarning("Could not save message log: " + exception.Message, this);
        }
    }

    // Mouse position arrives bottom-up from Input System; UI Toolkit uses top-down coordinates.
    public static bool BlocksPointer(Vector2 screenPosition)
    {
        if (active == null || active.terminal == null || active.terminal.panel == null) return false;
        if (active.gesturePointer != -1) return true;
        Vector2 point = RuntimePanelUtils.ScreenToPanel(active.terminal.panel,
            new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        return active.terminal.worldBound.Contains(point);
    }

    private void OnDisable()
    {
        if (active == this) active = null;
        CancelScrollToBottom();
        gesturePointer = -1;
        if (document != null) { document.enabled = false; Destroy(document); }
        if (settings != null) Destroy(settings);
        if (textFontAsset != null) Destroy(textFontAsset);
        if (fallbackFontAsset != null) Destroy(fallbackFontAsset);
        history = null;
        terminal = null;
    }
}
