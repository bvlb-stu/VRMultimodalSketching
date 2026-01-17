using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class VRWhiteboardSystem : MonoBehaviour
{
    [Header("Referências VR")]
    public Transform rightHandController;

    [Tooltip("Trigger (Activate Value) da mão direita.")]
    public InputActionProperty drawAction;

    [Tooltip("Botão para trocar de cor (ex: Right Hand / PrimaryButton).")]
    public InputActionProperty colorChangeAction;

    [Header("Parede onde o quadro vai ficar")]
    public Transform wallTransform;

    [Range(0.1f, 1f)] public float boardWidthFraction = 0.6f;
    [Range(0.1f, 1f)] public float boardHeightFraction = 0.5f;
    public float boardMargin = 0.2f;
    public float boardOffsetFromWall = 0.01f;

    [Header("Textura do quadro")]
    public int textureWidth = 1024;
    public int textureHeight = 1024;
    public float brushSize = 15f;

    [Tooltip("Distância máxima de desenho ao quadro.")]
    public float maxDrawDistance = 0.2f;

    [Tooltip("Offset para trás da ponta para evitar ficar \"dentro\" do quadro.")]
    public float insideFixOffset = 0.01f;

    [Header("LineRenderer")]
    public float lineWidth = 0.01f;
    public float minDistanceBetweenPoints = 0.002f;

    [Header("Cores")]
    public Color[] palette = new Color[]
    {
        Color.black,
        Color.red,
        Color.green,
        Color.blue,
        Color.magenta
    };

    public int startColorIndex = 0;

    private Transform penTip;
    private Texture2D boardTexture;
    private Collider boardCollider;

    private int currentColorIndex = 0;
    private Color currentColor = Color.black;
    private Material penMat;

    private static readonly Color32 White = new Color32(255, 255, 255, 255);

    // Line drawing state
    private LineRenderer currentLineRenderer;
    private List<Vector3> currentLinePoints = new List<Vector3>();
    private bool isDrawing = false;

    void OnEnable()
    {
        drawAction.action?.Enable();
        colorChangeAction.action?.Enable();
    }

    void OnDisable()
    {
        drawAction.action?.Disable();
        colorChangeAction.action?.Disable();
    }

    void Start()
    {
        if (rightHandController == null)
        {
            Debug.LogError("VRWhiteboardSystem: RightHandController não definido.");
            return;
        }

        if (wallTransform == null)
        {
            Debug.LogError("VRWhiteboardSystem: WallTransform não definido.");
            return;
        }

        currentColorIndex = Mathf.Clamp(startColorIndex, 0, palette.Length - 1);
        currentColor = palette[currentColorIndex];

        CreateWhiteboardOnWallAuto();
        CreatePenTip();
    }

    void Update()
    {
        if (penTip == null || boardCollider == null || boardTexture == null || drawAction.action == null)
            return;

        // trocar cor
        if (colorChangeAction.action != null && colorChangeAction.action.triggered)
        {
            CycleColor();
        }

        float triggerValue = drawAction.action.ReadValue<float>();
        bool triggerPressed = triggerValue > 0.4f;

        if (triggerPressed)
        {
            Vector3 origin = penTip.position - penTip.forward * insideFixOffset;

            if (Physics.Raycast(origin, penTip.forward, out RaycastHit hit, maxDrawDistance + insideFixOffset))
            {
                if (hit.collider == boardCollider)
                {
                    Vector3 hitPoint = hit.point;

                    if (!isDrawing)
                    {
                        StartLine(hitPoint);
                    }
                    else
                    {
                        AddPointToLine(hitPoint);
                    }

                    Vector2 uv = hit.textureCoord;
                    int x = Mathf.RoundToInt(uv.x * (textureWidth - 1));
                    int y = Mathf.RoundToInt(uv.y * (textureHeight - 1));
                    DrawCircleOnTexture(x, y, brushSize, currentColor);
                    boardTexture.Apply();
                }
            }
        }
        else if (isDrawing)
        {
            EndLine();
        }
    }

    private void StartLine(Vector3 startPoint)
    {
        isDrawing = true;
        currentLinePoints.Clear();

        GameObject lineObj = new GameObject("DrawnLine");
        lineObj.transform.SetParent(wallTransform, true);

        currentLineRenderer = lineObj.AddComponent<LineRenderer>();
        currentLineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        currentLineRenderer.startColor = currentColor;
        currentLineRenderer.endColor = currentColor;
        currentLineRenderer.startWidth = lineWidth;
        currentLineRenderer.endWidth = lineWidth;
        currentLineRenderer.positionCount = 1;
        currentLineRenderer.useWorldSpace = true;
        currentLineRenderer.SetPosition(0, startPoint);

        currentLinePoints.Add(startPoint);
    }

    private void AddPointToLine(Vector3 point)
    {
        if (currentLinePoints.Count == 0 || Vector3.Distance(currentLinePoints[currentLinePoints.Count - 1], point) > minDistanceBetweenPoints)
        {
            currentLinePoints.Add(point);
            currentLineRenderer.positionCount = currentLinePoints.Count;
            currentLineRenderer.SetPosition(currentLinePoints.Count - 1, point);
        }
    }

    private void EndLine()
    {
        isDrawing = false;
        currentLineRenderer = null;
        currentLinePoints.Clear();
    }

    private void CreateWhiteboardOnWallAuto()
    {
        BoxCollider box = wallTransform.GetComponent<BoxCollider>();
        if (box == null)
        {
            Debug.LogError("VRWhiteboardSystem: WallTransform precisa de um BoxCollider.");
            return;
        }

        GameObject board = GameObject.CreatePrimitive(PrimitiveType.Quad);
        board.name = "Whiteboard_OnWall";
        board.transform.SetParent(wallTransform, false);

        float width = box.size.x * boardWidthFraction;
        float height = box.size.y * boardHeightFraction;
        board.transform.localScale = new Vector3(width, height, 1f);

        float yLocal = boardMargin;
        float zLocal = box.size.z * 0.5f + boardOffsetFromWall;
        board.transform.localPosition = new Vector3(0f, yLocal, zLocal);

        board.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        boardCollider = board.GetComponent<Collider>();
        if (boardCollider == null)
            boardCollider = board.AddComponent<MeshCollider>();

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Texture");

        boardTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[textureWidth * textureHeight];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = White;
        boardTexture.SetPixels32(pixels);
        boardTexture.Apply();

        Material mat = new Material(shader);

        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", boardTexture);
        else if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", boardTexture);
        else
            mat.mainTexture = boardTexture;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.white);
        if (mat.HasProperty("_TintColor"))
            mat.SetColor("_TintColor", Color.white);

        Renderer rend = board.GetComponent<Renderer>();
        rend.material = mat;
    }

    private void CreatePenTip()
    {
        GameObject tip = new GameObject("PenTip_Auto");
        tip.transform.SetParent(rightHandController, false);
        tip.transform.localPosition = new Vector3(0f, 0f, 0.2f);
        tip.transform.localRotation = Quaternion.identity;
        penTip = tip.transform;

        GameObject tipVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tipVisual.name = "PenTipVisual";
        tipVisual.transform.SetParent(penTip, false);
        tipVisual.transform.localPosition = Vector3.zero;
        tipVisual.transform.localScale = Vector3.one * 0.05f;
        Destroy(tipVisual.GetComponent<Collider>());

        Shader penShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (penShader == null)
            penShader = Shader.Find("Unlit/Color");

        penMat = new Material(penShader);
        penMat.color = currentColor;

        Renderer r = tipVisual.GetComponent<Renderer>();
        r.material = penMat;
    }

    private void CycleColor()
    {
        if (palette == null || palette.Length == 0) return;

        currentColorIndex = (currentColorIndex + 1) % palette.Length;
        currentColor = palette[currentColorIndex];

        if (penMat != null)
            penMat.color = currentColor;

        if (currentLineRenderer != null)
        {
            currentLineRenderer.startColor = currentColor;
            currentLineRenderer.endColor = currentColor;
        }
    }

    private void DrawCircleOnTexture(int centerX, int centerY, float radius, Color color)
    {
        int r = Mathf.CeilToInt(radius);
        int rSquared = r * r;

        for (int y = -r; y <= r; y++)
        {
            int yy = y * y;
            int py = centerY + y;
            if (py < 0 || py >= textureHeight) continue;

            for (int x = -r; x <= r; x++)
            {
                int xx = x * x;
                if (xx + yy > rSquared) continue;

                int px = centerX + x;
                if (px < 0 || px >= textureWidth) continue;

                boardTexture.SetPixel(px, py, color);
            }
        }
    }
}