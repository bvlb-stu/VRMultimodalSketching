using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// VR Surface Pencil - ONLY draws on surfaces tagged as "DrawableSurface"
/// 
/// DRAWING MODES:
/// - Automatic: Draws when tip touches surface (no trigger needed) - great for hand tracking
/// - Trigger: Draws only when trigger is held - traditional controller mode
/// - Both: Automatic drawing OR trigger drawing (either works)
/// 
/// Attach this script to the pencil GameObject.
/// </summary>
public class VRSurfacePencil : MonoBehaviour
{
    public enum DrawingMode
    {
        Automatic,      // Draw when tip touches surface (no trigger needed)
        TriggerOnly,    // Only draw when trigger is held
        Both            // Either automatic touch OR trigger works
    }

    [Header("Drawing Mode")]
    [Tooltip("How drawing is activated")]
    public DrawingMode drawingMode = DrawingMode.Automatic;

    [Header("Drawing Settings")]
    [Tooltip("The tip_point transform where drawing originates")]
    public Transform tipPoint;

    [Tooltip("The material on the tip that determines drawing color")]
    public Renderer tipRenderer;

    [Tooltip("Width of the drawn line")]
    public float lineWidth = 0.003f;

    [Tooltip("Minimum distance to add a new point")]
    public float minDistance = 0.0005f;

    [Tooltip("Small offset to prevent z-fighting with the surface")]
    public float surfaceOffset = 0.001f;

    [Header("Detection Settings")]
    [Tooltip("Max distance to detect surface from tip")]
    public float maxDrawDistance = 0.015f;

    [Tooltip("Distance threshold to start automatic drawing (tip must be this close)")]
    public float autoDrawDistance = 0.005f;

    [Header("Line Settings")]
    [Tooltip("Shader for the line material")]
    public Shader lineShader;

    [Header("Debug")]
    public bool showDebug = true;

    // Public property to check if currently drawing
    public bool IsCurrentlyDrawing => isDrawing && isGrabbed;

    // Private variables
    private XRGrabInteractable grabInteractable;
    private LineRenderer currentLine;
    private List<Vector3> currentLinePoints = new List<Vector3>();
    private bool isDrawing = false;
    private InputAction triggerAction;
    private bool isGrabbed = false;
    private bool triggerHeld = false;
    private Transform currentSurface;
    private bool isUsingHandTracking = false;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        if (grabInteractable == null)
        {
            Debug.LogError("VRSurfacePencil: No XRGrabInteractable found on this GameObject!");
            return;
        }

        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);

        if (lineShader == null)
        {
            lineShader = Shader.Find("Unlit/Color");
            if (lineShader == null)
            {
                lineShader = Shader.Find("Universal Render Pipeline/Unlit");
            }
        }
    }

    private void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            grabInteractable.selectExited.RemoveListener(OnReleased);
        }

        CleanupTriggerAction();
    }

    private void CleanupTriggerAction()
    {
        if (triggerAction != null)
        {
            triggerAction.performed -= OnTriggerPressed;
            triggerAction.canceled -= OnTriggerReleased;
            triggerAction.Disable();
            triggerAction.Dispose();
            triggerAction = null;
        }
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        isGrabbed = true;

        // Detect if this is hand tracking or controller
        isUsingHandTracking = IsHandTrackingInteractor(args.interactorObject);

        if (!isUsingHandTracking)
        {
            SetupTriggerAction(args.interactorObject);
        }

        if (showDebug)
        {
            string inputType = isUsingHandTracking ? "Hand Tracking" : "Controller";
            Debug.Log($"VRSurfacePencil: Pencil grabbed ({inputType})");
        }
    }

    private bool IsHandTrackingInteractor(IXRSelectInteractor interactor)
    {
        // Check if the interactor is from hand tracking
        var interactorObject = interactor as MonoBehaviour;
        if (interactorObject == null) return false;

        string name = interactorObject.gameObject.name.ToLower();
        string parentNames = "";
        Transform parent = interactorObject.transform;
        while (parent != null)
        {
            parentNames += parent.name.ToLower() + " ";
            parent = parent.parent;
        }

        // Common hand tracking interactor names
        bool isHand = name.Contains("hand") || name.Contains("poke") ||
                      parentNames.Contains("hand") || parentNames.Contains("poke") ||
                      name.Contains("direct") || parentNames.Contains("direct");

        // Check if it's NOT a controller (controllers usually have "controller" or "ray" in name)
        bool isController = name.Contains("controller") || name.Contains("ray") ||
                           parentNames.Contains("controller");

        // If it looks like a hand and doesn't look like a controller, it's probably hand tracking
        return isHand && !isController;
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        isGrabbed = false;
        triggerHeld = false;
        isUsingHandTracking = false;
        StopDrawing();

        CleanupTriggerAction();

        if (showDebug) Debug.Log("VRSurfacePencil: Pencil released");
    }

    private void SetupTriggerAction(IXRSelectInteractor interactor)
    {
        // Clean up any existing action first
        CleanupTriggerAction();

        var controllerInteractor = interactor as MonoBehaviour;
        if (controllerInteractor == null) return;

        string triggerBinding = "<XRController>{RightHand}/trigger";

        Transform parent = controllerInteractor.transform;
        while (parent != null)
        {
            string parentName = parent.name.ToLower();
            if (parentName.Contains("left"))
            {
                triggerBinding = "<XRController>{LeftHand}/trigger";
                break;
            }
            else if (parentName.Contains("right"))
            {
                triggerBinding = "<XRController>{RightHand}/trigger";
                break;
            }
            parent = parent.parent;
        }

        triggerAction = new InputAction("DrawTrigger", InputActionType.Button, triggerBinding);
        triggerAction.performed += OnTriggerPressed;
        triggerAction.canceled += OnTriggerReleased;
        triggerAction.Enable();

        if (showDebug) Debug.Log($"VRSurfacePencil: Trigger action set up with binding: {triggerBinding}");
    }

    private void OnTriggerPressed(InputAction.CallbackContext context)
    {
        triggerHeld = true;
        if (showDebug) Debug.Log("VRSurfacePencil: Trigger pressed");
    }

    private void OnTriggerReleased(InputAction.CallbackContext context)
    {
        triggerHeld = false;

        // Only stop drawing if in TriggerOnly mode
        // In Automatic/Both modes, drawing continues if tip is still touching
        if (drawingMode == DrawingMode.TriggerOnly)
        {
            StopDrawing();
        }

        if (showDebug) Debug.Log("VRSurfacePencil: Trigger released");
    }

    private void Update()
    {
        if (!isGrabbed || tipPoint == null) return;

        // Determine if we should be drawing based on mode
        bool shouldDraw = ShouldBeDrawing();

        if (shouldDraw)
        {
            // Try to find a drawable surface at the tip position
            if (TryGetDrawableSurface(out Transform surface, out Vector3 drawPosition, out Vector3 surfaceNormal))
            {
                if (!isDrawing)
                {
                    StartDrawingOnSurface(surface, drawPosition, surfaceNormal);
                }
                else if (surface == currentSurface)
                {
                    UpdateDrawing(drawPosition);
                }
                else
                {
                    // Switched to different surface
                    StopDrawing();
                    StartDrawingOnSurface(surface, drawPosition, surfaceNormal);
                }
            }
            else
            {
                if (isDrawing)
                {
                    StopDrawing();
                }
            }
        }
        else
        {
            if (isDrawing)
            {
                StopDrawing();
            }
        }
    }

    private bool ShouldBeDrawing()
    {
        switch (drawingMode)
        {
            case DrawingMode.Automatic:
                // Always try to draw when tip is close enough to surface
                return IsTipTouchingSurface();

            case DrawingMode.TriggerOnly:
                // Only draw when trigger is held
                return triggerHeld;

            case DrawingMode.Both:
                // Either trigger OR automatic touch
                return triggerHeld || IsTipTouchingSurface();

            default:
                return false;
        }
    }

    private bool IsTipTouchingSurface()
    {
        if (tipPoint == null) return false;

        Vector3 tipPos = tipPoint.position;
        Collider[] colliders = Physics.OverlapSphere(tipPos, autoDrawDistance);

        foreach (var col in colliders)
        {
            if (col.CompareTag("DrawableSurface"))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetDrawableSurface(out Transform surface, out Vector3 drawPosition, out Vector3 surfaceNormal)
    {
        surface = null;
        drawPosition = Vector3.zero;
        surfaceNormal = Vector3.forward;

        Vector3 tipPos = tipPoint.position;

        // Use OverlapSphere to find nearby colliders
        Collider[] colliders = Physics.OverlapSphere(tipPos, maxDrawDistance);

        float closestDist = float.MaxValue;
        Collider closestCollider = null;

        foreach (var col in colliders)
        {
            // STRICT CHECK: Only accept objects with DrawableSurface tag
            if (!col.CompareTag("DrawableSurface"))
            {
                continue;
            }

            Vector3 closest = col.ClosestPoint(tipPos);
            float dist = Vector3.Distance(tipPos, closest);

            if (dist < closestDist)
            {
                closestDist = dist;
                closestCollider = col;
            }
        }

        if (closestCollider != null)
        {
            surface = closestCollider.transform;

            // Get the surface normal
            // For a Quad, the visible face points in -forward direction (negative Z)
            surfaceNormal = -surface.forward;

            // Project the tip onto the surface plane
            Vector3 surfacePoint = surface.position;
            Vector3 toTip = tipPos - surfacePoint;
            float distToPlane = Vector3.Dot(toTip, surfaceNormal);

            // The draw position is the tip projected onto the surface, offset slightly
            drawPosition = tipPos - surfaceNormal * distToPlane + surfaceNormal * surfaceOffset;

            if (showDebug)
            {
                Debug.DrawLine(tipPos, drawPosition, Color.green);
            }

            return true;
        }

        return false;
    }

    private void StartDrawingOnSurface(Transform surface, Vector3 worldDrawPos, Vector3 normal)
    {
        isDrawing = true;
        currentSurface = surface;

        if (showDebug) Debug.Log($"VRSurfacePencil: Started drawing on {currentSurface.name}");

        // Create line as child of the surface (so it moves with surface)
        GameObject lineObj = new GameObject("DrawnLine");
        lineObj.transform.SetParent(currentSurface);
        lineObj.transform.localPosition = Vector3.zero;
        lineObj.transform.localRotation = Quaternion.identity;
        lineObj.transform.localScale = Vector3.one;

        currentLine = lineObj.AddComponent<LineRenderer>();

        // Configure line renderer - USE LOCAL SPACE so it follows parent
        currentLine.useWorldSpace = false;  // LOCAL SPACE - lines will follow parent transform
        currentLine.startWidth = lineWidth;
        currentLine.endWidth = lineWidth;
        currentLine.positionCount = 0;
        currentLine.numCapVertices = 4;
        currentLine.numCornerVertices = 4;

        // Create material with tip color
        Material lineMaterial = new Material(lineShader);
        Color drawColor = GetTipColor();
        lineMaterial.color = drawColor;

        if (lineMaterial.HasProperty("_BaseColor"))
        {
            lineMaterial.SetColor("_BaseColor", drawColor);
        }

        // Prevent z-fighting by adjusting render queue and depth settings
        lineMaterial.renderQueue = 3000; // Render after geometry (default is 2000)

        // If shader supports it, disable depth write to prevent z-fighting
        if (lineMaterial.HasProperty("_ZWrite"))
        {
            lineMaterial.SetFloat("_ZWrite", 0);
        }

        currentLine.material = lineMaterial;
        currentLinePoints.Clear();

        // Add first point (convert to local space)
        AddPoint(worldDrawPos);
    }

    private void UpdateDrawing(Vector3 worldDrawPos)
    {
        if (currentLine == null || currentSurface == null) return;

        // Convert to local for distance check
        Vector3 localPos = currentSurface.InverseTransformPoint(worldDrawPos);

        if (currentLinePoints.Count == 0 ||
            Vector3.Distance(localPos, currentLinePoints[currentLinePoints.Count - 1]) >= minDistance)
        {
            AddPoint(worldDrawPos);
        }
    }

    private void AddPoint(Vector3 worldDrawPos)
    {
        // Convert world position to local position relative to the surface
        Vector3 localPos = currentSurface.InverseTransformPoint(worldDrawPos);

        currentLinePoints.Add(localPos);
        currentLine.positionCount = currentLinePoints.Count;
        currentLine.SetPositions(currentLinePoints.ToArray());
    }

    private void StopDrawing()
    {
        if (isDrawing && showDebug)
        {
            Debug.Log($"VRSurfacePencil: Stopped drawing. Points: {currentLinePoints.Count}");
        }

        isDrawing = false;
        currentLine = null;
        currentSurface = null;
        currentLinePoints.Clear();
    }

    private Color GetTipColor()
    {
        if (tipRenderer != null && tipRenderer.material != null)
        {
            Material mat = tipRenderer.material;

            if (mat.HasProperty("_Color"))
            {
                return mat.color;
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                return mat.GetColor("_BaseColor");
            }
        }

        return Color.black;
    }

    public void SetTipColor(Color newColor)
    {
        if (tipRenderer != null && tipRenderer.material != null)
        {
            Material mat = tipRenderer.material;

            if (mat.HasProperty("_Color"))
            {
                mat.color = newColor;
            }
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", newColor);
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (tipPoint != null && showDebug)
        {
            // Max draw distance (yellow)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(tipPoint.position, maxDrawDistance);

            // Auto draw distance (green)
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(tipPoint.position, autoDrawDistance);

            // Tip point (red)
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(tipPoint.position, 0.002f);
        }
    }
}