using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Instructions panel that explains the prototype features and controls.
/// Attach this to an empty GameObject in your scene.
/// </summary>
public class InstructionsPanel : MonoBehaviour
{
    [Header("Positioning")]
    [Tooltip("Where to place the panel in the scene")]
    public Vector3 panelPosition = new Vector3(0, 1.5f, 2f);

    [Tooltip("Rotation of the panel (Y axis)")]
    public float panelYRotation = 0f;

    [Tooltip("Scale of the panel")]
    public float panelScale = 0.001f;

    [Header("Appearance")]
    public Color backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
    public Color titleColor = new Color(0.3f, 0.8f, 1f);
    public Color headerColor = new Color(1f, 0.8f, 0.3f);
    public Color textColor = Color.white;
    public Color highlightColor = new Color(0.5f, 1f, 0.5f);

    private GameObject panelRoot;
    private Canvas canvas;

    private void Start()
    {
        CreateInstructionsPanel();
    }

    private void CreateInstructionsPanel()
    {
        // Create root object
        panelRoot = new GameObject("InstructionsPanel");
        panelRoot.transform.SetParent(transform);
        panelRoot.transform.position = panelPosition;
        panelRoot.transform.rotation = Quaternion.Euler(0, panelYRotation, 0);
        panelRoot.transform.localScale = Vector3.one * panelScale;

        // Create Canvas
        canvas = panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var scaler = panelRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;

        panelRoot.AddComponent<GraphicRaycaster>();

        // Set canvas size
        RectTransform canvasRect = panelRoot.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(800, 1000);

        // Create background
        GameObject background = CreateUIElement("Background", panelRoot.transform);
        var bgImage = background.AddComponent<Image>();
        bgImage.color = backgroundColor;
        SetFullStretch(background.GetComponent<RectTransform>());

        // Add rounded corners effect (simple border)
        GameObject border = CreateUIElement("Border", background.transform);
        var borderImage = border.AddComponent<Image>();
        borderImage.color = titleColor;
        RectTransform borderRect = border.GetComponent<RectTransform>();
        SetFullStretch(borderRect);
        borderRect.offsetMin = new Vector2(-3, -3);
        borderRect.offsetMax = new Vector2(3, 3);
        border.transform.SetAsFirstSibling();

        // Create content container with padding
        GameObject content = CreateUIElement("Content", background.transform);
        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = new Vector2(30, 30);
        contentRect.offsetMax = new Vector2(-30, -30);

        // Add vertical layout
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 15;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        // === TITLE ===
        CreateText(content.transform, "VR Drawing Prototype", 48, titleColor, FontStyle.Bold, 60);

        // === SUBTITLE ===
        CreateText(content.transform, "Sketch & Manipulate Lines in Virtual Reality", 24, textColor, FontStyle.Italic, 35);

        // Spacer
        CreateSpacer(content.transform, 20);

        // === DRAWING SECTION ===
        CreateText(content.transform, "✏️  DRAWING", 32, headerColor, FontStyle.Bold, 45);

        CreateInstructionRow(content.transform, "Grab Pencil", "Grip Button");
        CreateInstructionRow(content.transform, "Draw on Board", "Hold Trigger while touching sketchboard");
        CreateInstructionRow(content.transform, "Line Color", "Based on pencil tip material");

        // Spacer
        CreateSpacer(content.transform, 15);

        // === LINE SELECTION SECTION ===
        CreateText(content.transform, "👁️  LINE SELECTION + VOICE", 32, headerColor, FontStyle.Bold, 45);

        CreateInstructionRow(content.transform, "Activate Selection Mode", "Press X Button (Left Controller)");
        CreateInstructionRow(content.transform, "Select a Line", "Look at a drawn line (highlights yellow)");
        CreateInstructionRow(content.transform, "Execute Command", "Speak a voice command");
        CreateInstructionRow(content.transform, "Deactivate", "Automatic after speaking or press X again");

        // Spacer
        CreateSpacer(content.transform, 15);

        // === VOICE COMMANDS SECTION ===
        CreateText(content.transform, "🎤  VOICE COMMANDS", 32, headerColor, FontStyle.Bold, 45);

        CreateCommandRow(content.transform, "\"Change color [red/blue/green...]\"", "Changes line color");
        CreateCommandRow(content.transform, "\"Smooth straight\" / \"Straighten\"", "Linear smoothing");
        CreateCommandRow(content.transform, "\"Smooth round\" / \"Curve\"", "Curved smoothing");
        CreateCommandRow(content.transform, "\"Simplify\"", "Reduce points");
        CreateCommandRow(content.transform, "\"Delete\" / \"Remove\"", "Delete line");

        // Spacer
        CreateSpacer(content.transform, 20);

        // === TIPS ===
        CreateText(content.transform, "💡 Tips", 28, highlightColor, FontStyle.Bold, 40);
        CreateText(content.transform, "• Draw on the pink sketchboard only\n• Speak clearly for voice commands\n• Selection mode deactivates when you start drawing", 22, textColor, FontStyle.Normal, 90);

        // Spacer
        CreateSpacer(content.transform, 15);

        // === EXPERIMENT CONTROLS ===
        CreateText(content.transform, "🔬 EXPERIMENT CONTROLS", 32, headerColor, FontStyle.Bold, 45);

        CreateInstructionRow(content.transform, "Advance Phase", "B Button (Right)");
        CreateInstructionRow(content.transform, "Mark Task Complete", "Y Button (Left)");
    }

    private GameObject CreateUIElement(string name, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private void SetFullStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }

    private Text CreateText(Transform parent, string content, int fontSize, Color color, FontStyle style, float height)
    {
        GameObject textObj = CreateUIElement("Text", parent);

        var layoutElement = textObj.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = height;
        layoutElement.minHeight = height;

        var text = textObj.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        return text;
    }

    private void CreateSpacer(Transform parent, float height)
    {
        GameObject spacer = CreateUIElement("Spacer", parent);
        var layoutElement = spacer.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = height;
        layoutElement.minHeight = height;
    }

    private void CreateInstructionRow(Transform parent, string action, string input)
    {
        GameObject row = CreateUIElement("Row", parent);

        var layoutElement = row.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 35;
        layoutElement.minHeight = 35;

        var horizontalLayout = row.AddComponent<HorizontalLayoutGroup>();
        horizontalLayout.spacing = 20;
        horizontalLayout.childAlignment = TextAnchor.MiddleCenter;
        horizontalLayout.childControlHeight = true;
        horizontalLayout.childControlWidth = true;
        horizontalLayout.childForceExpandHeight = true;
        horizontalLayout.childForceExpandWidth = false;

        // Action text (left)
        GameObject actionObj = CreateUIElement("Action", row.transform);
        var actionLayout = actionObj.AddComponent<LayoutElement>();
        actionLayout.preferredWidth = 250;
        actionLayout.flexibleWidth = 0;

        var actionText = actionObj.AddComponent<Text>();
        actionText.text = action;
        actionText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        actionText.fontSize = 22;
        actionText.color = highlightColor;
        actionText.fontStyle = FontStyle.Bold;
        actionText.alignment = TextAnchor.MiddleRight;

        // Separator
        GameObject sepObj = CreateUIElement("Sep", row.transform);
        var sepLayout = sepObj.AddComponent<LayoutElement>();
        sepLayout.preferredWidth = 30;
        sepLayout.flexibleWidth = 0;

        var sepText = sepObj.AddComponent<Text>();
        sepText.text = "→";
        sepText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sepText.fontSize = 22;
        sepText.color = textColor;
        sepText.alignment = TextAnchor.MiddleCenter;

        // Input text (right)
        GameObject inputObj = CreateUIElement("Input", row.transform);
        var inputLayout = inputObj.AddComponent<LayoutElement>();
        inputLayout.preferredWidth = 400;
        inputLayout.flexibleWidth = 1;

        var inputText = inputObj.AddComponent<Text>();
        inputText.text = input;
        inputText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        inputText.fontSize = 20;
        inputText.color = textColor;
        inputText.alignment = TextAnchor.MiddleLeft;
    }

    private void CreateCommandRow(Transform parent, string command, string description)
    {
        GameObject row = CreateUIElement("CommandRow", parent);

        var layoutElement = row.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 32;
        layoutElement.minHeight = 32;

        var horizontalLayout = row.AddComponent<HorizontalLayoutGroup>();
        horizontalLayout.spacing = 15;
        horizontalLayout.childAlignment = TextAnchor.MiddleCenter;
        horizontalLayout.childControlHeight = true;
        horizontalLayout.childControlWidth = true;
        horizontalLayout.childForceExpandHeight = true;
        horizontalLayout.childForceExpandWidth = false;

        // Command text (left) - in quotes, colored
        GameObject commandObj = CreateUIElement("Command", row.transform);
        var commandLayout = commandObj.AddComponent<LayoutElement>();
        commandLayout.preferredWidth = 380;
        commandLayout.flexibleWidth = 0;

        var commandText = commandObj.AddComponent<Text>();
        commandText.text = command;
        commandText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        commandText.fontSize = 19;
        commandText.color = new Color(0.6f, 0.9f, 1f);
        commandText.fontStyle = FontStyle.Italic;
        commandText.alignment = TextAnchor.MiddleRight;

        // Dash separator
        GameObject dashObj = CreateUIElement("Dash", row.transform);
        var dashLayout = dashObj.AddComponent<LayoutElement>();
        dashLayout.preferredWidth = 20;
        dashLayout.flexibleWidth = 0;

        var dashText = dashObj.AddComponent<Text>();
        dashText.text = "-";
        dashText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        dashText.fontSize = 19;
        dashText.color = textColor;
        dashText.alignment = TextAnchor.MiddleCenter;

        // Description text (right)
        GameObject descObj = CreateUIElement("Description", row.transform);
        var descLayout = descObj.AddComponent<LayoutElement>();
        descLayout.preferredWidth = 280;
        descLayout.flexibleWidth = 1;

        var descText = descObj.AddComponent<Text>();
        descText.text = description;
        descText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        descText.fontSize = 19;
        descText.color = textColor;
        descText.alignment = TextAnchor.MiddleLeft;
    }

    /// <summary>
    /// Call this to show/hide the instructions panel
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }
    }

    /// <summary>
    /// Toggle panel visibility
    /// </summary>
    public void ToggleVisibility()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(!panelRoot.activeSelf);
        }
    }

    /// <summary>
    /// Reposition the panel to a new location
    /// </summary>
    public void SetPosition(Vector3 newPosition, float yRotation = 0f)
    {
        if (panelRoot != null)
        {
            panelRoot.transform.position = newPosition;
            panelRoot.transform.rotation = Quaternion.Euler(0, yRotation, 0);
        }
    }
}