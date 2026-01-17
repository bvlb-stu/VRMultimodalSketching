using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using System.Collections.Generic;

/// <summary>
/// Pen-only workflow for line selection and manipulation.
/// Uses controller ray to select lines and shows a hand-attached menu for manipulation.
/// Toggle with A button, select line with Trigger, then pick menu option.
/// </summary>
public class PenOnlyLineSelector : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the Near-Far Interactor on the right controller")]
    public NearFarInteractor nearFarInteractor;

    [Tooltip("Alternative: Direct reference to the controller transform for ray origin")]
    public Transform controllerTransform;

    [Tooltip("Reference to the VRSurfacePencil to check if drawing")]
    public VRSurfacePencil surfacePencil;

    [Tooltip("Reference to ExperimentManager for error tracking (optional)")]
    public ExperimentManager experimentManager;

    [Header("Hand Menu UI")]
    [Tooltip("The Hand Menu UI GameObject to show/hide")]
    public GameObject handMenuUI;

    [Tooltip("Offset from controller position for menu placement")]
    public Vector3 menuOffset = new Vector3(0.1f, 0.05f, 0.1f);

    [Tooltip("How smoothly the menu follows the controller (higher = faster)")]
    public float menuFollowSpeed = 10f;

    [Tooltip("If true, menu always faces the camera/user")]
    public bool menuFaceUser = true;

    [Tooltip("Toggle for Smooth Linear function")]
    public Toggle smoothLinearToggle;

    [Tooltip("Toggle for Smooth Round function")]
    public Toggle smoothRoundToggle;

    [Tooltip("Toggle for Simplify function")]
    public Toggle simplifyToggle;

    [Tooltip("Toggle for Change Color function")]
    public Toggle changeColorToggle;

    [Tooltip("Toggle for Delete function")]
    public Toggle deleteToggle;

    [Tooltip("Toggle for Undo function")]
    public Toggle undoToggle;

    [Tooltip("Toggle for Cancel/Close function")]
    public Toggle cancelToggle;

    [Header("Selection Settings")]
    [Tooltip("Maximum distance for ray selection")]
    public float maxRayDistance = 10f;

    [Tooltip("Radius for line detection - increase for easier selection")]
    public float selectionRadius = 0.03f;

    [Header("Selection Stability")]
    [Tooltip("New line must be this much closer (in meters) to switch selection")]
    public float switchThreshold = 0.015f;

    [Tooltip("Minimum time (seconds) before switching to another line")]
    public float switchLockTime = 0.15f;

    [Header("Visual Feedback")]
    [Tooltip("Color for highlighted line (hovering)")]
    public Color highlightColor = Color.yellow;

    [Tooltip("Color for selected line")]
    public Color selectedColor = Color.cyan;

    [Header("Reticle/Crosshair")]
    [Tooltip("Optional: Prefab for the reticle that shows where the ray hits the board")]
    public GameObject reticlePrefab;

    [Tooltip("Color of the reticle when not hovering over a line")]
    public Color reticleDefaultColor = Color.white;

    [Tooltip("Color of the reticle when hovering over a line")]
    public Color reticleHoverColor = Color.green;

    [Tooltip("Size of the reticle")]
    public float reticleSize = 0.02f;

    [Tooltip("Distance to offset reticle from surface (increase if using sprites to prevent flickering)")]
    public float reticleOffset = 0.005f;

    [Tooltip("Layer mask for raycasting to find the whiteboard surface")]
    public LayerMask whiteboardLayerMask = -1;

    [Header("Debug")]
    public bool showDebug = true;
    public bool showRayDebug = false;

    // State
    private bool isSelectionModeActive = false;
    private LineRenderer currentlyHoveredLine = null;
    private LineRenderer selectedLine = null;
    private Color originalHoveredColor;
    private Color originalSelectedColor;

    // Selection stability
    private float currentHoverDistance = float.MaxValue;
    private float lastHoverChangeTime = 0f;

    // Reticle
    private GameObject reticleInstance;
    private Renderer reticleRenderer;

    // Input
    private InputAction toggleModeAction;  // A button
    private InputAction selectAction;       // Trigger

    // Undo system
    private Stack<UndoState> undoHistory = new Stack<UndoState>();
    public int maxUndoSteps = 20;

    private void Awake()
    {
        // Setup A button input (right controller) - toggle selection mode
        toggleModeAction = new InputAction("TogglePenMode", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
        toggleModeAction.performed += OnToggleMode;
        toggleModeAction.Enable();

        // Setup Trigger input (right controller) - select line
        selectAction = new InputAction("SelectLine", InputActionType.Button, "<XRController>{RightHand}/trigger");
        selectAction.performed += OnSelectPressed;
        selectAction.Enable();

        // Find Near-Far Interactor if not assigned
        if (nearFarInteractor == null)
        {
            var interactors = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
            foreach (var interactor in interactors)
            {
                if (interactor.handedness == InteractorHandedness.Right)
                {
                    nearFarInteractor = interactor;
                    break;
                }
            }
        }

        // Get controller transform from the interactor if not assigned
        if (controllerTransform == null && nearFarInteractor != null)
        {
            controllerTransform = nearFarInteractor.transform;
        }

        // Setup toggle listeners
        SetupToggleListeners();

        // Hide menu initially
        if (handMenuUI != null)
        {
            handMenuUI.SetActive(false);
        }

        // Create reticle
        CreateReticle();
    }

    private void SetupToggleListeners()
    {
        // For toggles, we listen to onValueChanged and trigger when turned ON
        if (smoothLinearToggle != null)
            smoothLinearToggle.onValueChanged.AddListener(OnSmoothLinearToggle);

        if (smoothRoundToggle != null)
            smoothRoundToggle.onValueChanged.AddListener(OnSmoothRoundToggle);

        if (simplifyToggle != null)
            simplifyToggle.onValueChanged.AddListener(OnSimplifyToggle);

        if (changeColorToggle != null)
            changeColorToggle.onValueChanged.AddListener(OnChangeColorToggle);

        if (deleteToggle != null)
            deleteToggle.onValueChanged.AddListener(OnDeleteToggle);

        if (undoToggle != null)
            undoToggle.onValueChanged.AddListener(OnUndoToggle);

        if (cancelToggle != null)
            cancelToggle.onValueChanged.AddListener(OnCancelToggle);
    }

    private void CreateReticle()
    {
        if (reticlePrefab != null)
        {
            // Use provided prefab
            reticleInstance = Instantiate(reticlePrefab);
            reticleInstance.name = "PenRayReticle";
        }
        else
        {
            // Create a simple crosshair programmatically
            reticleInstance = new GameObject("PenRayReticle");

            // Create outer ring
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.transform.SetParent(reticleInstance.transform);
            ring.transform.localScale = new Vector3(reticleSize, 0.001f, reticleSize);
            ring.transform.localPosition = Vector3.zero;
            ring.transform.localRotation = Quaternion.identity;

            // Remove collider
            Collider col = ring.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Create center dot
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.transform.SetParent(reticleInstance.transform);
            dot.transform.localScale = new Vector3(reticleSize * 0.3f, reticleSize * 0.3f, reticleSize * 0.3f);
            dot.transform.localPosition = Vector3.zero;

            // Remove collider
            col = dot.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Create crosshair lines
            CreateCrosshairLine(reticleInstance.transform, Vector3.right, reticleSize * 0.8f);
            CreateCrosshairLine(reticleInstance.transform, Vector3.forward, reticleSize * 0.8f);
        }

        // Get renderer for color changes
        reticleRenderer = reticleInstance.GetComponentInChildren<Renderer>();

        // Set initial color
        SetReticleColor(reticleDefaultColor);

        // Hide initially
        reticleInstance.SetActive(false);

        if (showDebug) Debug.Log("PenOnlyLineSelector: Reticle created");
    }

    private void CreateCrosshairLine(Transform parent, Vector3 direction, float length)
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.transform.SetParent(parent);

        // Make it thin and long
        float thickness = reticleSize * 0.1f;
        if (direction == Vector3.right)
            line.transform.localScale = new Vector3(length, thickness, thickness);
        else
            line.transform.localScale = new Vector3(thickness, thickness, length);

        line.transform.localPosition = Vector3.zero;

        // Remove collider
        Collider col = line.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    private void SetReticleColor(Color color)
    {
        if (reticleInstance == null) return;

        // Handle SpriteRenderers
        SpriteRenderer[] spriteRenderers = reticleInstance.GetComponentsInChildren<SpriteRenderer>();
        foreach (var spriteRend in spriteRenderers)
        {
            spriteRend.color = color;
        }

        // Handle regular Renderers (MeshRenderer, etc.)
        Renderer[] renderers = reticleInstance.GetComponentsInChildren<Renderer>();
        foreach (var rend in renderers)
        {
            // Skip SpriteRenderers as we already handled them
            if (rend is SpriteRenderer) continue;

            // Create material instance to avoid affecting other objects
            if (rend.material != null)
            {
                rend.material.color = color;

                // Make it unlit/emissive so it's visible
                if (rend.material.HasProperty("_EmissionColor"))
                {
                    rend.material.EnableKeyword("_EMISSION");
                    rend.material.SetColor("_EmissionColor", color * 0.5f);
                }
            }
        }
    }

    private void UpdateReticle(Ray ray)
    {
        if (reticleInstance == null) return;

        // Raycast to find where the ray hits the whiteboard/surface
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, maxRayDistance, whiteboardLayerMask))
        {
            // Position reticle at hit point, offset along normal to avoid z-fighting
            // For sprites, we need more offset (0.005-0.01) than for 3D meshes (0.001)
            reticleInstance.transform.position = hit.point + hit.normal * reticleOffset;

            // Orient reticle to face along the surface normal
            // For sprites, we might need different rotation handling
            SpriteRenderer spriteRenderer = reticleInstance.GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                // Sprite - make it face the camera/user while staying on the surface plane
                // Option 1: Face opposite to surface normal (flat on surface)
                reticleInstance.transform.rotation = Quaternion.LookRotation(-hit.normal);

                // Option 2: Billboard toward camera (uncomment if you prefer this)
                // Vector3 toCamera = Camera.main.transform.position - reticleInstance.transform.position;
                // reticleInstance.transform.rotation = Quaternion.LookRotation(toCamera);
            }
            else
            {
                // 3D mesh - original rotation
                reticleInstance.transform.rotation = Quaternion.LookRotation(hit.normal) * Quaternion.Euler(90, 0, 0);
            }

            // Change color based on whether hovering a line
            if (currentlyHoveredLine != null)
            {
                SetReticleColor(reticleHoverColor);
            }
            else
            {
                SetReticleColor(reticleDefaultColor);
            }

            reticleInstance.SetActive(true);
        }
        else
        {
            // No hit - hide reticle
            reticleInstance.SetActive(false);
        }
    }

    private void ShowReticle()
    {
        if (reticleInstance != null)
        {
            reticleInstance.SetActive(true);
        }
    }

    private void HideReticle()
    {
        if (reticleInstance != null)
        {
            reticleInstance.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (toggleModeAction != null)
        {
            toggleModeAction.performed -= OnToggleMode;
            toggleModeAction.Disable();
            toggleModeAction.Dispose();
        }

        if (selectAction != null)
        {
            selectAction.performed -= OnSelectPressed;
            selectAction.Disable();
            selectAction.Dispose();
        }

        // Remove toggle listeners
        if (smoothLinearToggle != null) smoothLinearToggle.onValueChanged.RemoveListener(OnSmoothLinearToggle);
        if (smoothRoundToggle != null) smoothRoundToggle.onValueChanged.RemoveListener(OnSmoothRoundToggle);
        if (simplifyToggle != null) simplifyToggle.onValueChanged.RemoveListener(OnSimplifyToggle);
        if (changeColorToggle != null) changeColorToggle.onValueChanged.RemoveListener(OnChangeColorToggle);
        if (deleteToggle != null) deleteToggle.onValueChanged.RemoveListener(OnDeleteToggle);
        if (undoToggle != null) undoToggle.onValueChanged.RemoveListener(OnUndoToggle);
        if (cancelToggle != null) cancelToggle.onValueChanged.RemoveListener(OnCancelToggle);

        // Destroy reticle
        if (reticleInstance != null)
        {
            Destroy(reticleInstance);
        }
    }

    private void Update()
    {
        // Check if drawing started - deactivate selection mode
        if (isSelectionModeActive && surfacePencil != null && surfacePencil.IsCurrentlyDrawing)
        {
            DeactivateSelectionMode();
            return;
        }

        // Only do ray hover when in selection mode and no line selected yet
        if (isSelectionModeActive && selectedLine == null)
        {
            UpdateRayHover();

            // Update reticle position
            Ray ray = GetControllerRay();
            UpdateReticle(ray);
        }
        else if (!isSelectionModeActive)
        {
            // Hide reticle when not in selection mode
            HideReticle();
        }

        // Update menu position to follow controller when visible
        if (handMenuUI != null && handMenuUI.activeSelf && controllerTransform != null)
        {
            UpdateMenuPosition();
        }
    }

    /// <summary>
    /// Updates the menu position to follow the controller
    /// </summary>
    private void UpdateMenuPosition()
    {
        if (handMenuUI == null || controllerTransform == null) return;

        // Calculate target position - offset from controller
        Vector3 targetPosition = controllerTransform.position +
                                 controllerTransform.right * menuOffset.x +
                                 controllerTransform.up * menuOffset.y +
                                 controllerTransform.forward * menuOffset.z;

        // Smoothly move menu to target position
        handMenuUI.transform.position = Vector3.Lerp(
            handMenuUI.transform.position,
            targetPosition,
            Time.deltaTime * menuFollowSpeed
        );

        // Make menu face the user/camera
        if (menuFaceUser && Camera.main != null)
        {
            Vector3 directionToCamera = Camera.main.transform.position - handMenuUI.transform.position;
            directionToCamera.y = 0; // Keep menu upright (optional - remove this line if you want full facing)

            if (directionToCamera.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(-directionToCamera); // Negative to face toward camera
                handMenuUI.transform.rotation = Quaternion.Slerp(
                    handMenuUI.transform.rotation,
                    targetRotation,
                    Time.deltaTime * menuFollowSpeed
                );
            }
        }
    }

    private void OnToggleMode(InputAction.CallbackContext context)
    {
        if (isSelectionModeActive)
        {
            DeactivateSelectionMode();
        }
        else
        {
            // Only activate if not currently drawing
            if (surfacePencil == null || !surfacePencil.IsCurrentlyDrawing)
            {
                ActivateSelectionMode();
            }
        }
    }

    private void OnSelectPressed(InputAction.CallbackContext context)
    {
        if (!isSelectionModeActive) return;

        // If no line selected yet and we're hovering over one, select it
        if (selectedLine == null && currentlyHoveredLine != null)
        {
            // Validate line selection (records LINE SELECTION ERROR if wrong line)
            if (experimentManager != null)
            {
                experimentManager.ValidateLineSelection(currentlyHoveredLine);
            }
            SelectLine(currentlyHoveredLine);
        }
        else if (selectedLine == null && currentlyHoveredLine == null)
        {
            // User triggered in empty space - MODE ERROR
            if (experimentManager != null)
            {
                experimentManager.RecordModeError();
            }
            if (showDebug) Debug.Log("PenOnlyLineSelector: Mode error - triggered in empty space");
        }
        else if (selectedLine == null && currentlyHoveredLine == null)
        {
            // Mode error: clicking trigger without pointing at a line
            if (experimentManager != null)
            {
                experimentManager.RecordModeError();
            }
            if (showDebug) Debug.Log("PenOnlyLineSelector: Mode error - trigger pressed without targeting a line");
        }
    }

    private void ActivateSelectionMode()
    {
        isSelectionModeActive = true;

        if (showDebug) Debug.Log("PenOnlyLineSelector: Selection mode ACTIVATED - Point at a line and press Trigger");
    }

    private void DeactivateSelectionMode()
    {
        isSelectionModeActive = false;

        // Clear hover highlight
        ClearHoverHighlight();

        // Clear selection
        ClearSelection();

        // Hide menu
        HideMenu();

        // Hide reticle
        HideReticle();

        // Reset all toggles
        ResetAllToggles();

        if (showDebug) Debug.Log("PenOnlyLineSelector: Selection mode DEACTIVATED");
    }

    private void ResetAllToggles()
    {
        // Reset toggles to off state without triggering actions
        if (smoothLinearToggle != null) smoothLinearToggle.SetIsOnWithoutNotify(false);
        if (smoothRoundToggle != null) smoothRoundToggle.SetIsOnWithoutNotify(false);
        if (simplifyToggle != null) simplifyToggle.SetIsOnWithoutNotify(false);
        if (changeColorToggle != null) changeColorToggle.SetIsOnWithoutNotify(false);
        if (deleteToggle != null) deleteToggle.SetIsOnWithoutNotify(false);
        if (undoToggle != null) undoToggle.SetIsOnWithoutNotify(false);
        if (cancelToggle != null) cancelToggle.SetIsOnWithoutNotify(false);
    }

    private void UpdateRayHover()
    {
        Ray ray = GetControllerRay();

        if (showRayDebug)
        {
            Debug.DrawRay(ray.origin, ray.direction * maxRayDistance, Color.green);
        }

        // Find the closest line to the ray
        LineRenderer closestLine = FindClosestLineToRay(ray);

        // Update hover state
        if (closestLine != currentlyHoveredLine)
        {
            // Clear previous hover
            ClearHoverHighlight();

            // Set new hover
            if (closestLine != null)
            {
                currentlyHoveredLine = closestLine;
                originalHoveredColor = GetLineColor(closestLine);
                SetLineColor(closestLine, highlightColor);

                if (showDebug) Debug.Log($"PenOnlyLineSelector: Hovering over line with {closestLine.positionCount} points");
            }
        }
    }

    private Ray GetControllerRay()
    {
        // Use the controller transform (from Near-Far Interactor or direct reference)
        if (controllerTransform != null)
        {
            return new Ray(controllerTransform.position, controllerTransform.forward);
        }

        // Try to get from Near-Far Interactor
        if (nearFarInteractor != null)
        {
            return new Ray(nearFarInteractor.transform.position, nearFarInteractor.transform.forward);
        }

        // Fallback: use camera forward
        Camera cam = Camera.main;
        if (cam != null)
        {
            return new Ray(cam.transform.position, cam.transform.forward);
        }

        return new Ray(Vector3.zero, Vector3.forward);
    }

    private LineRenderer FindClosestLineToRay(Ray ray)
    {
        LineRenderer closestLine = null;
        float closestDistance = float.MaxValue;

        // Find all drawn lines
        LineRenderer[] allLines = FindObjectsByType<LineRenderer>(FindObjectsSortMode.None);

        foreach (var line in allLines)
        {
            if (line.gameObject.name != "DrawnLine") continue;
            if (line.positionCount < 2) continue;
            if (!line.gameObject.activeInHierarchy) continue;

            // Check distance from ray to each segment of the line
            float distance = GetDistanceFromRayToLine(ray, line);

            if (distance < selectionRadius && distance < closestDistance)
            {
                closestDistance = distance;
                closestLine = line;
            }
        }

        // Apply hysteresis - prefer keeping current selection unless new one is significantly closer
        if (currentlyHoveredLine != null && closestLine != currentlyHoveredLine)
        {
            // Check if enough time has passed since last change
            if (Time.time - lastHoverChangeTime < switchLockTime)
            {
                float currentDist = GetDistanceFromRayToLine(ray, currentlyHoveredLine);
                if (currentDist < selectionRadius)
                {
                    return currentlyHoveredLine;
                }
            }

            // Only switch if new line is significantly closer
            float currentLineDistance = GetDistanceFromRayToLine(ray, currentlyHoveredLine);
            if (currentLineDistance < selectionRadius &&
                closestDistance > currentLineDistance - switchThreshold)
            {
                return currentlyHoveredLine;
            }
        }

        // Track when we change hover target
        if (closestLine != currentlyHoveredLine)
        {
            lastHoverChangeTime = Time.time;
            currentHoverDistance = closestDistance;
        }

        return closestLine;
    }

    private float GetDistanceFromRayToLine(Ray ray, LineRenderer line)
    {
        float minDistance = float.MaxValue;

        Vector3[] positions = new Vector3[line.positionCount];
        line.GetPositions(positions);

        // Convert to world space if needed
        if (!line.useWorldSpace)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = line.transform.TransformPoint(positions[i]);
            }
        }

        // Check each LINE SEGMENT (not just points) for more accurate selection
        for (int i = 0; i < positions.Length - 1; i++)
        {
            Vector3 segmentStart = positions[i];
            Vector3 segmentEnd = positions[i + 1];

            // Find the closest distance between the ray and this line segment
            float distance = DistanceRayToSegment(ray, segmentStart, segmentEnd);

            if (distance < minDistance)
            {
                minDistance = distance;
            }
        }

        return minDistance;
    }

    /// <summary>
    /// Calculates the minimum distance between a ray and a line segment.
    /// </summary>
    private float DistanceRayToSegment(Ray ray, Vector3 segStart, Vector3 segEnd)
    {
        Vector3 rayOrigin = ray.origin;
        Vector3 rayDir = ray.direction.normalized;
        Vector3 segDir = segEnd - segStart;
        float segLength = segDir.magnitude;

        if (segLength < 0.0001f)
        {
            return DistanceRayToPoint(ray, segStart);
        }

        segDir /= segLength;

        Vector3 w0 = rayOrigin - segStart;
        float a = Vector3.Dot(rayDir, rayDir);
        float b = Vector3.Dot(rayDir, segDir);
        float c = Vector3.Dot(segDir, segDir);
        float d = Vector3.Dot(rayDir, w0);
        float e = Vector3.Dot(segDir, w0);

        float denom = a * c - b * b;

        float rayParam, segParam;

        if (Mathf.Abs(denom) < 0.0001f)
        {
            rayParam = 0;
            segParam = e / c;
        }
        else
        {
            rayParam = (b * e - c * d) / denom;
            segParam = (a * e - b * d) / denom;
        }

        rayParam = Mathf.Clamp(rayParam, 0, maxRayDistance);
        segParam = Mathf.Clamp(segParam, 0, segLength);

        Vector3 closestOnRay = rayOrigin + rayDir * rayParam;
        Vector3 closestOnSegment = segStart + segDir * segParam;

        return Vector3.Distance(closestOnRay, closestOnSegment);
    }

    private float DistanceRayToPoint(Ray ray, Vector3 point)
    {
        Vector3 rayToPoint = point - ray.origin;
        float projection = Vector3.Dot(rayToPoint, ray.direction);

        if (projection < 0)
            return Vector3.Distance(ray.origin, point);
        if (projection > maxRayDistance)
            return float.MaxValue;

        Vector3 closestOnRay = ray.origin + ray.direction * projection;
        return Vector3.Distance(closestOnRay, point);
    }

    private void SelectLine(LineRenderer line)
    {
        // Clear previous selection
        ClearSelection();

        selectedLine = line;
        originalSelectedColor = originalHoveredColor;
        SetLineColor(line, selectedColor);

        // Clear hover since it's now selected
        currentlyHoveredLine = null;

        // Show the hand menu
        ShowMenu();

        if (showDebug) Debug.Log($"PenOnlyLineSelector: Selected line with {line.positionCount} points - Menu shown");
    }

    private void ClearHoverHighlight()
    {
        if (currentlyHoveredLine != null && currentlyHoveredLine != selectedLine)
        {
            SetLineColor(currentlyHoveredLine, originalHoveredColor);
            currentlyHoveredLine = null;
        }
    }

    private void ClearSelection()
    {
        if (selectedLine != null)
        {
            SetLineColor(selectedLine, originalSelectedColor);
            selectedLine = null;
        }
    }

    private Color GetLineColor(LineRenderer line)
    {
        if (line.material.HasProperty("_Color"))
            return line.material.color;
        if (line.material.HasProperty("_BaseColor"))
            return line.material.GetColor("_BaseColor");
        return Color.black;
    }

    private void SetLineColor(LineRenderer line, Color color)
    {
        if (line.material.HasProperty("_Color"))
            line.material.color = color;
        if (line.material.HasProperty("_BaseColor"))
            line.material.SetColor("_BaseColor", color);
    }

    // ==================== MENU SHOW/HIDE ====================

    private void ShowMenu()
    {
        if (handMenuUI != null)
        {
            // Position menu at controller immediately before showing
            if (controllerTransform != null)
            {
                Vector3 initialPosition = controllerTransform.position +
                                         controllerTransform.right * menuOffset.x +
                                         controllerTransform.up * menuOffset.y +
                                         controllerTransform.forward * menuOffset.z;
                handMenuUI.transform.position = initialPosition;

                // Face user immediately
                if (menuFaceUser && Camera.main != null)
                {
                    Vector3 directionToCamera = Camera.main.transform.position - handMenuUI.transform.position;
                    directionToCamera.y = 0;
                    if (directionToCamera.sqrMagnitude > 0.001f)
                    {
                        handMenuUI.transform.rotation = Quaternion.LookRotation(-directionToCamera);
                    }
                }
            }

            handMenuUI.SetActive(true);
            ResetAllToggles();
            if (showDebug) Debug.Log("PenOnlyLineSelector: Hand menu shown (following controller)");
        }
    }

    private void HideMenu()
    {
        if (handMenuUI != null)
        {
            handMenuUI.SetActive(false);
            if (showDebug) Debug.Log("PenOnlyLineSelector: Hand menu hidden");
        }
    }

    // ==================== TOGGLE CALLBACKS ====================
    // These fire when a toggle changes value. We only act when toggled ON.

    private void OnSmoothLinearToggle(bool isOn)
    {
        if (!isOn || selectedLine == null) return;

        // Validate function selection
        if (experimentManager != null)
        {
            experimentManager.ValidateFunctionSelection("SmoothLinear");
        }

        SaveUndoState(selectedLine);
        SmoothLinear(selectedLine);
        FinishAction();
    }

    private void OnSmoothRoundToggle(bool isOn)
    {
        if (!isOn || selectedLine == null) return;

        // Validate function selection
        if (experimentManager != null)
        {
            experimentManager.ValidateFunctionSelection("SmoothRound");
        }

        SaveUndoState(selectedLine);
        SmoothRound(selectedLine);
        FinishAction();
    }

    private void OnSimplifyToggle(bool isOn)
    {
        if (!isOn || selectedLine == null) return;

        // Validate function selection
        if (experimentManager != null)
        {
            experimentManager.ValidateFunctionSelection("Simplify");
        }

        SaveUndoState(selectedLine);
        SimplifyLine(selectedLine);
        FinishAction();
    }

    private void OnChangeColorToggle(bool isOn)
    {
        if (!isOn || selectedLine == null) return;

        // Validate function selection
        if (experimentManager != null)
        {
            experimentManager.ValidateFunctionSelection("ChangeColor");
        }

        SaveUndoState(selectedLine);

        // Cycle through colors
        Color[] colors = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, Color.white, Color.black };
        Color currentColor = originalSelectedColor;

        int nextIndex = 0;
        for (int i = 0; i < colors.Length; i++)
        {
            if (ColorsAreClose(currentColor, colors[i]))
            {
                nextIndex = (i + 1) % colors.Length;
                break;
            }
        }

        Color newColor = colors[nextIndex];
        SetLineColor(selectedLine, newColor);
        originalSelectedColor = newColor;

        if (showDebug) Debug.Log($"PenOnlyLineSelector: Changed color to {newColor}");

        // Reset toggle for next use but don't finish action
        if (changeColorToggle != null) changeColorToggle.SetIsOnWithoutNotify(false);
    }

    private void OnDeleteToggle(bool isOn)
    {
        if (!isOn || selectedLine == null) return;

        // Validate function selection
        if (experimentManager != null)
        {
            experimentManager.ValidateFunctionSelection("Delete");
        }

        SaveUndoState(selectedLine, willBeDeleted: true);
        selectedLine.gameObject.SetActive(false);

        if (showDebug) Debug.Log("PenOnlyLineSelector: Line deleted");

        selectedLine = null;
        FinishAction();
    }

    private void OnUndoToggle(bool isOn)
    {
        if (!isOn) return;

        Undo();

        // Reset toggle for next use
        if (undoToggle != null) undoToggle.SetIsOnWithoutNotify(false);
    }

    private void OnCancelToggle(bool isOn)
    {
        if (!isOn) return;

        DeactivateSelectionMode();
    }

    private bool ColorsAreClose(Color a, Color b, float threshold = 0.1f)
    {
        return Mathf.Abs(a.r - b.r) < threshold &&
               Mathf.Abs(a.g - b.g) < threshold &&
               Mathf.Abs(a.b - b.b) < threshold;
    }

    private void FinishAction()
    {
        // After an action, deselect and hide menu, but stay in selection mode
        ClearSelection();
        HideMenu();
        ResetAllToggles();

        if (showDebug) Debug.Log("PenOnlyLineSelector: Action complete - select another line or press A to exit");
    }

    // ==================== UNDO SYSTEM ====================

    private void SaveUndoState(LineRenderer line, bool willBeDeleted = false)
    {
        if (line == null) return;

        // IMPORTANT: Pass the original color, not the current highlight color
        // The line might be showing highlight color (cyan/yellow) but we want to save the true original
        Color colorToSave = originalSelectedColor;

        UndoState state = new UndoState(line, willBeDeleted, colorToSave);

        if (willBeDeleted)
        {
            state.deletedLineObject = line.gameObject;
        }

        undoHistory.Push(state);

        // Limit history size
        while (undoHistory.Count > maxUndoSteps)
        {
            var temp = new Stack<UndoState>();
            for (int i = 0; i < maxUndoSteps; i++)
            {
                temp.Push(undoHistory.Pop());
            }
            undoHistory.Clear();
            while (temp.Count > 0)
            {
                undoHistory.Push(temp.Pop());
            }
        }

        if (showDebug) Debug.Log($"PenOnlyLineSelector: Undo state saved (color: {colorToSave}). History: {undoHistory.Count}");
    }

    public void Undo()
    {
        if (undoHistory.Count == 0)
        {
            if (showDebug) Debug.Log("PenOnlyLineSelector: Nothing to undo");
            return;
        }

        UndoState state = undoHistory.Pop();

        if (state.wasDeleted)
        {
            if (state.deletedLineObject != null)
            {
                state.deletedLineObject.SetActive(true);
                if (showDebug) Debug.Log("PenOnlyLineSelector: Restored deleted line");
            }
        }
        else if (state.line != null)
        {
            state.line.positionCount = state.positions.Length;
            state.line.SetPositions(state.positions);
            SetLineColor(state.line, state.color);

            // IMPORTANT: Update the stored original colors so ClearSelection doesn't overwrite
            // with the wrong color when deactivating selection mode
            if (state.line == selectedLine)
            {
                originalSelectedColor = state.color;
            }
            if (state.line == currentlyHoveredLine)
            {
                originalHoveredColor = state.color;
            }

            // Re-apply selection highlight so user sees the line is still selected
            if (state.line == selectedLine)
            {
                SetLineColor(state.line, selectedColor);
            }
            else if (state.line == currentlyHoveredLine)
            {
                SetLineColor(state.line, highlightColor);
            }

            if (showDebug) Debug.Log($"PenOnlyLineSelector: Restored line to {state.positions.Length} points with original color");
        }
    }

    // ==================== LINE MANIPULATION ====================

    private void SmoothLinear(LineRenderer line)
    {
        if (line == null || line.positionCount < 2) return;

        Vector3[] positions = new Vector3[line.positionCount];
        line.GetPositions(positions);

        Vector3 start = positions[0];
        Vector3 end = positions[positions.Length - 1];

        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);

        if (showDebug) Debug.Log("PenOnlyLineSelector: Applied Smooth Linear");
    }

    private void SmoothRound(LineRenderer line, int curveResolution = 30)
    {
        if (line == null || line.positionCount < 2) return;

        Vector3[] positions = new Vector3[line.positionCount];
        line.GetPositions(positions);

        Vector3 start = positions[0];
        Vector3 end = positions[positions.Length - 1];

        Vector3 surfaceNormal = GetSurfaceNormal(line.transform);

        if (IsCircleAttempt(positions, out Vector3 center, out float radius))
        {
            Vector3[] circlePoints = GeneratePerfectCircle(center, radius, surfaceNormal, curveResolution);
            line.positionCount = circlePoints.Length;
            line.SetPositions(circlePoints);
            if (showDebug) Debug.Log($"PenOnlyLineSelector: Applied Perfect Circle");
            return;
        }

        // Elliptical curve
        float maxDeviation = 0f;
        Vector3 deviationDirection = Vector3.zero;
        Vector3 lineDirection = (end - start).normalized;
        float lineLength = Vector3.Distance(start, end);

        for (int i = 1; i < positions.Length - 1; i++)
        {
            Vector3 toPoint = positions[i] - start;
            float projection = Vector3.Dot(toPoint, lineDirection);
            Vector3 closestPointOnLine = start + lineDirection * projection;
            Vector3 deviation = positions[i] - closestPointOnLine;
            float deviationMagnitude = deviation.magnitude;

            if (deviationMagnitude > maxDeviation)
            {
                maxDeviation = deviationMagnitude;
                deviationDirection = deviation.normalized;
            }
        }

        float curvatureAmount;
        if (maxDeviation < lineLength * 0.02f)
        {
            curvatureAmount = lineLength * 0.25f;
            deviationDirection = GetPerpendicularDirection(lineDirection, line.transform);
        }
        else
        {
            curvatureAmount = maxDeviation;
        }

        Vector3[] curvePoints = GenerateEllipticalCurve(start, end, deviationDirection, curvatureAmount, curveResolution);
        line.positionCount = curvePoints.Length;
        line.SetPositions(curvePoints);

        if (showDebug) Debug.Log("PenOnlyLineSelector: Applied Smooth Round");
    }

    private void SimplifyLine(LineRenderer line, float tolerance = 0.005f)
    {
        if (line == null || line.positionCount < 3) return;

        Vector3[] positions = new Vector3[line.positionCount];
        line.GetPositions(positions);

        List<Vector3> simplified = DouglasPeucker(new List<Vector3>(positions), tolerance);

        line.positionCount = simplified.Count;
        line.SetPositions(simplified.ToArray());

        if (showDebug) Debug.Log($"PenOnlyLineSelector: Simplified ({positions.Length} -> {simplified.Count} points)");
    }

    // ==================== HELPER FUNCTIONS ====================

    private Vector3 GetSurfaceNormal(Transform lineTransform)
    {
        if (lineTransform.parent != null)
        {
            return -lineTransform.parent.forward;
        }
        return Vector3.forward;
    }

    private bool IsCircleAttempt(Vector3[] positions, out Vector3 center, out float radius)
    {
        center = Vector3.zero;
        radius = 0f;

        if (positions.Length < 8) return false;

        Vector3 start = positions[0];
        Vector3 end = positions[positions.Length - 1];

        Vector3 sum = Vector3.zero;
        foreach (var pos in positions) sum += pos;
        center = sum / positions.Length;

        float totalRadius = 0f;
        foreach (var pos in positions) totalRadius += Vector3.Distance(pos, center);
        radius = totalRadius / positions.Length;

        float startEndDistance = Vector3.Distance(start, end);
        if (startEndDistance > radius * 0.4f) return false;

        float radiusVariance = 0f;
        foreach (var pos in positions)
        {
            float diff = Vector3.Distance(pos, center) - radius;
            radiusVariance += diff * diff;
        }
        radiusVariance /= positions.Length;
        float radiusStdDev = Mathf.Sqrt(radiusVariance);

        if (radiusStdDev > radius * 0.3f) return false;

        return true;
    }

    private Vector3[] GeneratePerfectCircle(Vector3 center, float radius, Vector3 surfaceNormal, int resolution)
    {
        Vector3[] points = new Vector3[resolution + 1];

        Vector3 tangent1 = Vector3.Cross(surfaceNormal, Vector3.up).normalized;
        if (tangent1.sqrMagnitude < 0.01f)
            tangent1 = Vector3.Cross(surfaceNormal, Vector3.right).normalized;
        Vector3 tangent2 = Vector3.Cross(surfaceNormal, tangent1).normalized;

        for (int i = 0; i <= resolution; i++)
        {
            float angle = (float)i / resolution * 2f * Mathf.PI;
            points[i] = center + tangent1 * Mathf.Cos(angle) * radius + tangent2 * Mathf.Sin(angle) * radius;
        }

        return points;
    }

    private Vector3 GetPerpendicularDirection(Vector3 lineDirection, Transform lineTransform)
    {
        if (lineTransform.parent != null)
        {
            Vector3 surfaceNormal = -lineTransform.parent.forward;
            Vector3 perpendicular = Vector3.Cross(lineDirection, surfaceNormal).normalized;
            if (perpendicular.sqrMagnitude > 0.01f) return perpendicular;
        }

        Vector3 fallback = Vector3.Cross(lineDirection, Vector3.up).normalized;
        if (fallback.sqrMagnitude < 0.01f)
            fallback = Vector3.Cross(lineDirection, Vector3.right).normalized;
        return fallback;
    }

    private Vector3[] GenerateEllipticalCurve(Vector3 start, Vector3 end, Vector3 curveDirection, float curveHeight, int resolution)
    {
        Vector3[] points = new Vector3[resolution];
        Vector3 midPoint = (start + end) / 2f;
        Vector3 curvePeak = midPoint + curveDirection * curveHeight;

        for (int i = 0; i < resolution; i++)
        {
            float t = (float)i / (resolution - 1);
            float oneMinusT = 1f - t;
            points[i] = oneMinusT * oneMinusT * start + 2f * oneMinusT * t * curvePeak + t * t * end;
        }

        return points;
    }

    private List<Vector3> DouglasPeucker(List<Vector3> points, float epsilon)
    {
        if (points.Count < 3) return points;

        float maxDistance = 0;
        int index = 0;

        for (int i = 1; i < points.Count - 1; i++)
        {
            float distance = PerpendicularDistance(points[i], points[0], points[points.Count - 1]);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                index = i;
            }
        }

        if (maxDistance > epsilon)
        {
            List<Vector3> left = DouglasPeucker(points.GetRange(0, index + 1), epsilon);
            List<Vector3> right = DouglasPeucker(points.GetRange(index, points.Count - index), epsilon);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        return new List<Vector3> { points[0], points[points.Count - 1] };
    }

    private float PerpendicularDistance(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        Vector3 line = lineEnd - lineStart;
        float lineLengthSq = line.sqrMagnitude;
        if (lineLengthSq == 0) return Vector3.Distance(point, lineStart);
        float t = Mathf.Clamp01(Vector3.Dot(point - lineStart, line) / lineLengthSq);
        return Vector3.Distance(point, lineStart + t * line);
    }
}