using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using Meta.WitAi;
using Meta.WitAi.Lib;
using Oculus.Voice;

/// <summary>
/// Stores the state of a line for undo functionality
/// </summary>
[System.Serializable]
public class UndoState
{
    public LineRenderer line;
    public Vector3[] positions;
    public Color color;
    public bool wasDeleted;
    public GameObject deletedLineObject;
    public Transform parent;

    public UndoState(LineRenderer lineRenderer, bool deleted = false, Color? originalColor = null)
    {
        line = lineRenderer;
        wasDeleted = deleted;

        if (lineRenderer != null)
        {
            positions = new Vector3[lineRenderer.positionCount];
            lineRenderer.GetPositions(positions);

            // Use provided original color if available, otherwise get from material
            if (originalColor.HasValue)
            {
                color = originalColor.Value;
            }
            else
            {
                // Get color from material (fallback)
                if (lineRenderer.material.HasProperty("_Color"))
                    color = lineRenderer.material.color;
                else if (lineRenderer.material.HasProperty("_BaseColor"))
                    color = lineRenderer.material.GetColor("_BaseColor");
                else
                    color = Color.black;
            }

            parent = lineRenderer.transform.parent;
        }
    }
}

/// <summary>
/// Gaze-based line selection with voice command manipulation.
/// Flow: Press X -> Gaze at line (highlights) -> Speak command -> Command executes -> Mode deactivates
/// 
/// Requires Meta Voice SDK (Wit.ai) to be set up in your project.
/// </summary>
public class LineGazeSelector : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The camera used for gaze (usually Main Camera)")]
    public Camera gazeCamera;

    [Tooltip("Reference to the VRSurfacePencil to check if drawing")]
    public VRSurfacePencil surfacePencil;

    [Tooltip("Reference to AppVoiceExperience for voice recognition")]
    public AppVoiceExperience voiceExperience;

    [Tooltip("Reference to ExperimentManager for error tracking (optional)")]
    public ExperimentManager experimentManager;

    [Header("Gaze Settings")]
    [Tooltip("Maximum distance for gaze raycast")]
    public float maxGazeDistance = 10f;

    [Tooltip("Radius for line detection - increase for easier selection")]
    public float selectionRadius = 0.05f;

    [Header("Selection Stability")]
    [Tooltip("Only switch lines if looking away from current for this long (seconds)")]
    public float deselectDelay = 0.2f;

    [Header("Visual Feedback")]
    [Tooltip("Color for highlighted line (hovering)")]
    public Color highlightColor = Color.yellow;

    [Tooltip("Color for selected line")]
    public Color selectedColor = Color.cyan;

    [Header("Audio Feedback")]
    [Tooltip("Sound when selection mode activates")]
    public AudioClip activateSound;

    [Tooltip("Sound when voice is recognized")]
    public AudioClip recognizedSound;

    [Tooltip("Sound when command fails")]
    public AudioClip errorSound;

    [Header("Reticle/Crosshair")]
    [Tooltip("Optional: Prefab for the reticle that shows where gaze hits the board")]
    public GameObject reticlePrefab;

    [Tooltip("Color of the reticle when not hovering over a line")]
    public Color reticleDefaultColor = Color.white;

    [Tooltip("Color of the reticle when hovering over a line")]
    public Color reticleHoverColor = Color.green;

    [Tooltip("Size of the reticle")]
    public float reticleSize = 0.025f;

    [Tooltip("Layer mask for raycasting to find the whiteboard surface")]
    public LayerMask whiteboardLayerMask = -1;

    [Header("Debug")]
    public bool showDebug = true;

    [Header("Undo Settings")]
    [Tooltip("Maximum number of undo steps to store")]
    public int maxUndoSteps = 20;

    // State
    private bool isSelectionModeActive = false;
    private LineRenderer currentlyHoveredLine = null;
    private LineRenderer selectedLine = null;
    private Color originalHoveredColor;
    private Color originalSelectedColor;
    private bool isListening = false;

    // Selection stability
    private float lastValidHoverTime = 0f;
    private LineRenderer lastValidLine = null;

    // Undo history - stores line states before modifications
    private Stack<UndoState> undoHistory = new Stack<UndoState>();

    // Input
    private InputAction toggleSelectionAction;

    // Audio
    private AudioSource audioSource;

    // Reticle
    private GameObject reticleInstance;
    private Renderer reticleRenderer;

    private void Awake()
    {
        if (gazeCamera == null)
        {
            gazeCamera = Camera.main;
        }

        // Setup X button input (left controller)
        toggleSelectionAction = new InputAction("ToggleSelection", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
        toggleSelectionAction.performed += OnToggleSelection;
        toggleSelectionAction.Enable();

        // Setup audio source
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        // Find Voice Experience if not assigned
        if (voiceExperience == null)
        {
            voiceExperience = FindFirstObjectByType<AppVoiceExperience>();
        }

        // Setup voice callbacks
        SetupVoiceCallbacks();

        // Create reticle
        CreateReticle();
    }

    private void OnDestroy()
    {
        if (toggleSelectionAction != null)
        {
            toggleSelectionAction.performed -= OnToggleSelection;
            toggleSelectionAction.Disable();
            toggleSelectionAction.Dispose();
        }

        RemoveVoiceCallbacks();

        // Destroy reticle
        if (reticleInstance != null)
        {
            Destroy(reticleInstance);
        }
    }

    private void SetupVoiceCallbacks()
    {
        if (voiceExperience == null)
        {
            Debug.LogWarning("LineGazeSelector: No AppVoiceExperience found! Voice commands won't work.");
            return;
        }

        voiceExperience.VoiceEvents.OnRequestCompleted.AddListener(OnVoiceRequestCompleted);
        voiceExperience.VoiceEvents.OnFullTranscription.AddListener(OnFullTranscription);
        voiceExperience.VoiceEvents.OnStoppedListening.AddListener(OnStoppedListening);
        voiceExperience.VoiceEvents.OnError.AddListener(OnVoiceError);

        if (showDebug) Debug.Log("LineGazeSelector: Voice callbacks setup complete");
    }

    private void RemoveVoiceCallbacks()
    {
        if (voiceExperience == null) return;

        voiceExperience.VoiceEvents.OnRequestCompleted.RemoveListener(OnVoiceRequestCompleted);
        voiceExperience.VoiceEvents.OnFullTranscription.RemoveListener(OnFullTranscription);
        voiceExperience.VoiceEvents.OnStoppedListening.RemoveListener(OnStoppedListening);
        voiceExperience.VoiceEvents.OnError.RemoveListener(OnVoiceError);
    }

    private void CreateReticle()
    {
        if (reticlePrefab != null)
        {
            // Use provided prefab
            reticleInstance = Instantiate(reticlePrefab);
            reticleInstance.name = "GazeReticle";
        }
        else
        {
            // Create a simple crosshair programmatically
            reticleInstance = new GameObject("GazeReticle");

            // Create outer ring
            //GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            //ring.transform.SetParent(reticleInstance.transform);
            //ring.transform.localScale = new Vector3(reticleSize, 0.001f, reticleSize);
            //ring.transform.localPosition = Vector3.zero;
            //ring.transform.localRotation = Quaternion.identity;

            //// Remove collider
            //col = ring.GetComponent<Collider>();
            //if (col != null) Destroy(col);

            // Create center dot
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.transform.SetParent(reticleInstance.transform);
            dot.transform.localScale = new Vector3(reticleSize * 0.3f, reticleSize * 0.3f, reticleSize * 0.3f);
            dot.transform.localPosition = Vector3.zero;

            // Remove collider
            Collider col = dot.GetComponent<Collider>();
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

        if (showDebug) Debug.Log("LineGazeSelector: Reticle created");
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

        Renderer[] renderers = reticleInstance.GetComponentsInChildren<Renderer>();
        foreach (var rend in renderers)
        {
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

    private void UpdateReticle(Ray gazeRay)
    {
        if (reticleInstance == null) return;

        // Raycast to find where the gaze hits the whiteboard/surface
        RaycastHit hit;
        if (Physics.Raycast(gazeRay, out hit, maxGazeDistance, whiteboardLayerMask))
        {
            // Position reticle at hit point, slightly offset to avoid z-fighting
            reticleInstance.transform.position = hit.point + hit.normal * 0.001f;

            // Orient reticle to face along the surface normal
            reticleInstance.transform.rotation = Quaternion.LookRotation(hit.normal) * Quaternion.Euler(0, 0, 0);

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

    private void Update()
    {
        // Check if drawing started - deactivate selection mode
        if (isSelectionModeActive && surfacePencil != null && IsDrawing())
        {
            DeactivateSelectionMode();
            return;
        }

        if (isSelectionModeActive)
        {
            UpdateGazeSelection();

            // Update reticle position
            Ray gazeRay = new Ray(gazeCamera.transform.position, gazeCamera.transform.forward);
            UpdateReticle(gazeRay);
        }
        else
        {
            // Hide reticle when not in selection mode
            HideReticle();
        }
    }

    private bool IsDrawing()
    {
        return surfacePencil != null && surfacePencil.IsCurrentlyDrawing;
    }

    private void OnToggleSelection(InputAction.CallbackContext context)
    {
        if (isSelectionModeActive)
        {
            DeactivateSelectionMode();
        }
        else
        {
            // Only activate if not currently drawing
            if (surfacePencil == null || !IsDrawing())
            {
                ActivateSelectionMode();
            }
        }
    }

    private void ActivateSelectionMode()
    {
        isSelectionModeActive = true;

        // Play activation sound
        if (activateSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(activateSound);
        }

        // Start listening for voice commands
        StartVoiceListening();

        if (showDebug) Debug.Log("LineGazeSelector: Selection mode ACTIVATED - Look at a line and speak a command");
    }

    private void DeactivateSelectionMode()
    {
        isSelectionModeActive = false;

        // Stop voice listening
        StopVoiceListening();

        // Clear hover highlight
        ClearHoverHighlight();

        // Clear selection
        ClearSelection();

        // Hide reticle
        HideReticle();

        if (showDebug) Debug.Log("LineGazeSelector: Selection mode DEACTIVATED");
    }

    private void StartVoiceListening()
    {
        if (voiceExperience == null)
        {
            Debug.LogWarning("LineGazeSelector: Cannot start voice - no AppVoiceExperience assigned");
            return;
        }

        if (!isListening)
        {
            isListening = true;
            voiceExperience.Activate();

            if (showDebug) Debug.Log("LineGazeSelector: Voice listening STARTED");
        }
    }

    private void StopVoiceListening()
    {
        if (voiceExperience == null) return;

        if (isListening)
        {
            isListening = false;
            voiceExperience.Deactivate();

            if (showDebug) Debug.Log("LineGazeSelector: Voice listening STOPPED");
        }
    }

    private void UpdateGazeSelection()
    {
        Ray gazeRay = new Ray(gazeCamera.transform.position, gazeCamera.transform.forward);

        if (showDebug)
        {
            Debug.DrawRay(gazeRay.origin, gazeRay.direction * maxGazeDistance, Color.magenta);
        }

        // Find the closest line to the gaze ray
        LineRenderer closestLine = FindClosestLineToRay(gazeRay);

        // Update hover state
        if (closestLine != currentlyHoveredLine)
        {
            // Clear previous hover
            ClearHoverHighlight();

            // Set new hover
            if (closestLine != null)
            {
                currentlyHoveredLine = closestLine;
                selectedLine = closestLine; // Auto-select on gaze
                originalHoveredColor = GetLineColor(closestLine);
                originalSelectedColor = originalHoveredColor;
                SetLineColor(closestLine, highlightColor);

                if (showDebug) Debug.Log($"LineGazeSelector: Gazing at line with {closestLine.positionCount} points");
            }
            else
            {
                selectedLine = null;
            }
        }
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

            // Check distance from ray to the line
            float distance = GetDistanceFromRayToLine(ray, line);

            if (distance < selectionRadius && distance < closestDistance)
            {
                closestDistance = distance;
                closestLine = line;
            }
        }

        // Simple stability: if we found a line, update tracking
        if (closestLine != null)
        {
            lastValidHoverTime = Time.time;
            lastValidLine = closestLine;
            return closestLine;
        }

        // If no line found but we recently had one, keep it briefly (prevents flicker when blinking/moving)
        if (lastValidLine != null && Time.time - lastValidHoverTime < deselectDelay)
        {
            // Verify the last line is still reasonably close
            float lastLineDistance = GetDistanceFromRayToLine(ray, lastValidLine);
            if (lastLineDistance < selectionRadius * 1.5f) // Slightly more forgiving
            {
                return lastValidLine;
            }
        }

        // Clear tracking if nothing found
        lastValidLine = null;
        return null;
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

        // Check distance to each POINT first (simple and reliable)
        for (int i = 0; i < positions.Length; i++)
        {
            float dist = DistanceRayToPoint(ray, positions[i]);
            if (dist < minDistance)
            {
                minDistance = dist;
            }
        }

        // Also check line segments for lines with fewer points
        if (positions.Length < 50) // Only for simpler lines to avoid performance issues
        {
            for (int i = 0; i < positions.Length - 1; i++)
            {
                float dist = DistanceRayToSegment(ray, positions[i], positions[i + 1]);
                if (dist < minDistance)
                {
                    minDistance = dist;
                }
            }
        }

        return minDistance;
    }

    /// <summary>
    /// Distance from ray to a single point.
    /// </summary>
    private float DistanceRayToPoint(Ray ray, Vector3 point)
    {
        Vector3 rayToPoint = point - ray.origin;
        float projection = Vector3.Dot(rayToPoint, ray.direction);

        // Point is behind the ray
        if (projection < 0)
            return float.MaxValue;

        // Point is too far
        if (projection > maxGazeDistance)
            return float.MaxValue;

        Vector3 closestOnRay = ray.origin + ray.direction * projection;
        return Vector3.Distance(closestOnRay, point);
    }

    /// <summary>
    /// Calculates the minimum distance between a ray and a line segment.
    /// </summary>
    private float DistanceRayToSegment(Ray ray, Vector3 segStart, Vector3 segEnd)
    {
        // Simple approach: sample points along the segment
        float minDist = float.MaxValue;
        int samples = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(segStart, segEnd) / 0.01f)); // Sample every 1cm
        samples = Mathf.Min(samples, 10); // Cap at 10 samples per segment

        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            Vector3 point = Vector3.Lerp(segStart, segEnd, t);
            float dist = DistanceRayToPoint(ray, point);
            if (dist < minDist)
            {
                minDist = dist;
            }
        }

        return minDist;
    }

    // ==================== VOICE CALLBACKS ====================

    private void OnFullTranscription(string transcription)
    {
        if (showDebug) Debug.Log($"LineGazeSelector: Transcription received: \"{transcription}\"");

        // Process the voice command
        ProcessVoiceCommand(transcription);
    }

    private void OnStoppedListening()
    {
        if (showDebug) Debug.Log("LineGazeSelector: Stopped listening");

        // Deactivate selection mode when user stops talking
        if (isSelectionModeActive)
        {
            // Small delay to allow transcription to process
            Invoke(nameof(DelayedDeactivate), 0.5f);
        }
    }

    private void DelayedDeactivate()
    {
        DeactivateSelectionMode();
    }

    private void OnVoiceRequestCompleted()
    {
        if (showDebug) Debug.Log("LineGazeSelector: Voice request completed");
    }

    private void OnVoiceError(string error, string message)
    {
        Debug.LogError($"LineGazeSelector: Voice error - {error}: {message}");

        if (errorSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(errorSound);
        }
    }

    // ==================== COMMAND PROCESSING ====================

    private void ProcessVoiceCommand(string transcription)
    {
        string command = transcription.ToLower().Trim();
        bool commandRecognized = false;

        // Undo command works globally - no line selection needed
        if (ContainsAny(command, "undo", "go back", "revert", "undo that"))
        {
            Undo();
            commandRecognized = true;
        }
        else if (selectedLine == null)
        {
            if (showDebug) Debug.Log("LineGazeSelector: No line selected - cannot apply command (except undo)");

            // MODE ERROR - Speaking command while not looking at a target
            if (experimentManager != null)
            {
                experimentManager.RecordModeError();
            }

            // Play error sound
            if (errorSound != null && audioSource != null)
            {
                audioSource.PlayOneShot(errorSound);
            }
            return;
        }
        // Commands below require a selected line - validate line selection first
        else if (ContainsAny(command, "change color", "color", "change colour", "colour",
                             "red", "blue", "green", "yellow", "orange", "purple", "pink",
                             "white", "black", "cyan", "magenta"))
        {
            // Validate line selection (records LINE SELECTION ERROR if wrong)
            if (experimentManager != null)
            {
                experimentManager.ValidateLineSelection(selectedLine);
                experimentManager.ValidateFunctionSelection("ChangeColor");
            }

            Color newColor = ExtractColorFromCommand(command);
            ChangeLineColor(selectedLine, newColor);
            commandRecognized = true;
        }
        else if (ContainsAny(command, "smooth straight", "straighten", "make straight", "linear", "smooth linear"))
        {
            if (experimentManager != null)
            {
                experimentManager.ValidateLineSelection(selectedLine);
                experimentManager.ValidateFunctionSelection("SmoothLinear");
            }

            SmoothLinear(selectedLine);
            commandRecognized = true;
        }
        else if (ContainsAny(command, "smooth round", "round", "curve", "make curved", "smooth curve"))
        {
            if (experimentManager != null)
            {
                experimentManager.ValidateLineSelection(selectedLine);
                experimentManager.ValidateFunctionSelection("SmoothRound");
            }

            SmoothRound(selectedLine);
            commandRecognized = true;
        }
        else if (ContainsAny(command, "simplify", "reduce", "less points", "fewer points"))
        {
            if (experimentManager != null)
            {
                experimentManager.ValidateLineSelection(selectedLine);
                experimentManager.ValidateFunctionSelection("Simplify");
            }

            SimplifyLine(selectedLine);
            commandRecognized = true;
        }
        else if (ContainsAny(command, "delete", "remove", "erase", "clear"))
        {
            if (experimentManager != null)
            {
                experimentManager.ValidateLineSelection(selectedLine);
                experimentManager.ValidateFunctionSelection("Delete");
            }

            DeleteLine(selectedLine);
            commandRecognized = true;
        }

        if (commandRecognized)
        {
            if (recognizedSound != null && audioSource != null)
            {
                audioSource.PlayOneShot(recognizedSound);
            }
            if (showDebug) Debug.Log($"LineGazeSelector: Command recognized and executed: \"{command}\"");
        }
        else
        {
            // Command not recognized - counts as FUNCTION SELECTION ERROR
            if (experimentManager != null)
            {
                experimentManager.RecordFunctionSelectionError();
            }

            if (errorSound != null && audioSource != null)
            {
                audioSource.PlayOneShot(errorSound);
            }
            if (showDebug) Debug.Log($"LineGazeSelector: Command not recognized: \"{command}\"");
        }
    }

    private bool ContainsAny(string text, params string[] keywords)
    {
        foreach (string keyword in keywords)
        {
            if (text.Contains(keyword))
            {
                return true;
            }
        }
        return false;
    }

    private Color ExtractColorFromCommand(string command)
    {
        if (command.Contains("red")) return Color.red;
        if (command.Contains("green")) return Color.green;
        if (command.Contains("blue")) return Color.blue;
        if (command.Contains("yellow")) return Color.yellow;
        if (command.Contains("orange")) return new Color(1f, 0.5f, 0f);
        if (command.Contains("purple") || command.Contains("violet")) return new Color(0.5f, 0f, 1f);
        if (command.Contains("pink")) return new Color(1f, 0.4f, 0.7f);
        if (command.Contains("white")) return Color.white;
        if (command.Contains("black")) return Color.black;
        if (command.Contains("cyan")) return Color.cyan;
        if (command.Contains("magenta")) return Color.magenta;
        if (command.Contains("gray") || command.Contains("grey")) return Color.gray;
        if (command.Contains("brown")) return new Color(0.6f, 0.3f, 0.1f);

        // Default: random color
        return new Color(Random.value, Random.value, Random.value);
    }

    // ==================== HELPER FUNCTIONS ====================

    private void ClearHoverHighlight()
    {
        if (currentlyHoveredLine != null)
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

    // ==================== UNDO SYSTEM ====================

    /// <summary>
    /// Saves the current state of a line before modification
    /// </summary>
    private void SaveUndoState(LineRenderer line, bool willBeDeleted = false)
    {
        if (line == null) return;

        // IMPORTANT: Pass the original color, not the current highlight color
        // The line might be showing highlight color (yellow) but we want to save the true original
        Color colorToSave = originalSelectedColor;

        UndoState state = new UndoState(line, willBeDeleted, colorToSave);

        // If deleting, store reference to the GameObject
        if (willBeDeleted)
        {
            state.deletedLineObject = line.gameObject;
        }

        undoHistory.Push(state);

        // Limit history size
        if (undoHistory.Count > maxUndoSteps)
        {
            // Convert to array, remove oldest, convert back
            var states = undoHistory.ToArray();
            undoHistory.Clear();
            for (int i = 0; i < states.Length - 1; i++)
            {
                undoHistory.Push(states[states.Length - 1 - i]);
            }
        }

        if (showDebug) Debug.Log($"Undo state saved (color: {colorToSave}). History size: {undoHistory.Count}");
    }

    /// <summary>
    /// Undoes the last line modification
    /// </summary>
    public void Undo()
    {
        if (undoHistory.Count == 0)
        {
            if (showDebug) Debug.Log("Undo: Nothing to undo");
            return;
        }

        UndoState state = undoHistory.Pop();

        if (state.wasDeleted)
        {
            // Restore a deleted line
            if (state.deletedLineObject != null)
            {
                state.deletedLineObject.SetActive(true);
                if (showDebug) Debug.Log("Undo: Restored deleted line");
            }
            else
            {
                // Recreate the line if GameObject was destroyed
                RestoreDeletedLine(state);
            }
        }
        else if (state.line != null)
        {
            // Restore line positions and color
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
            if (state.line == selectedLine || state.line == currentlyHoveredLine)
            {
                SetLineColor(state.line, highlightColor);
            }

            if (showDebug) Debug.Log($"Undo: Restored line to {state.positions.Length} points with original color");
        }

        if (showDebug) Debug.Log($"Undo complete. Remaining history: {undoHistory.Count}");
    }

    /// <summary>
    /// Recreates a deleted line from saved state
    /// </summary>
    private void RestoreDeletedLine(UndoState state)
    {
        if (state.positions == null || state.positions.Length == 0) return;

        GameObject lineObj = new GameObject("DrawnLine");
        if (state.parent != null)
        {
            lineObj.transform.SetParent(state.parent);
        }

        LineRenderer newLine = lineObj.AddComponent<LineRenderer>();
        newLine.positionCount = state.positions.Length;
        newLine.SetPositions(state.positions);
        newLine.useWorldSpace = true;
        newLine.startWidth = 0.003f;
        newLine.endWidth = 0.003f;
        newLine.numCapVertices = 4;
        newLine.numCornerVertices = 4;

        // Create material
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

        Material mat = new Material(shader);
        mat.color = state.color;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", state.color);

        newLine.material = mat;

        if (showDebug) Debug.Log("Undo: Recreated deleted line");
    }

    /// <summary>
    /// Clears all undo history
    /// </summary>
    public void ClearUndoHistory()
    {
        undoHistory.Clear();
        if (showDebug) Debug.Log("Undo history cleared");
    }

    // ==================== LINE MANIPULATION FUNCTIONS ====================

    /// <summary>
    /// Changes the line color
    /// </summary>
    public void ChangeLineColor(LineRenderer line, Color newColor)
    {
        if (line == null) return;

        // Save state before modification
        SaveUndoState(line);

        SetLineColor(line, newColor);
        originalSelectedColor = newColor;
        originalHoveredColor = newColor;

        if (showDebug) Debug.Log($"Applied: Change Color to {newColor}");
    }

    /// <summary>
    /// Smooth Linear - Simply draws a straight line from start to end
    /// </summary>
    public void SmoothLinear(LineRenderer line)
    {
        if (line == null || line.positionCount < 2) return;

        // Save state before modification
        SaveUndoState(line);

        Vector3[] positions = new Vector3[line.positionCount];
        line.GetPositions(positions);

        Vector3 start = positions[0];
        Vector3 end = positions[positions.Length - 1];

        // Create a straight line with just 2 points
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);

        if (showDebug) Debug.Log("Applied: Smooth Linear (straight line from start to end)");
    }

    /// <summary>
    /// Smooth Round - Draws an elliptical curve from start to end
    /// OR if the user drew a circle (start ≈ end), creates a perfect circle
    /// </summary>
    public void SmoothRound(LineRenderer line, int curveResolution = 30)
    {
        if (line == null || line.positionCount < 2) return;

        // Save state before modification
        SaveUndoState(line);

        Vector3[] positions = new Vector3[line.positionCount];
        line.GetPositions(positions);

        Vector3 start = positions[0];
        Vector3 end = positions[positions.Length - 1];

        // Get the surface normal from the sketchboard (parent of the line)
        Vector3 surfaceNormal = GetSurfaceNormal(line.transform);

        // Check if this looks like a circle attempt
        if (IsCircleAttempt(positions, out Vector3 center, out float radius))
        {
            // Generate a perfect circle using the SURFACE normal, not calculated normal
            Vector3[] circlePoints = GeneratePerfectCircle(center, radius, surfaceNormal, curveResolution);

            line.positionCount = circlePoints.Length;
            line.SetPositions(circlePoints);

            if (showDebug) Debug.Log($"Applied: Perfect Circle (radius: {radius:F3}, center: {center}, normal: {surfaceNormal})");
            return;
        }

        // Not a circle - do normal elliptical curve

        // Calculate the original line's curvature direction and magnitude
        float maxDeviation = 0f;
        Vector3 deviationDirection = Vector3.zero;

        // Find the point that deviates most from the straight line
        Vector3 lineDirection = (end - start).normalized;
        float lineLength = Vector3.Distance(start, end);

        for (int i = 1; i < positions.Length - 1; i++)
        {
            // Project point onto the straight line
            Vector3 toPoint = positions[i] - start;
            float projection = Vector3.Dot(toPoint, lineDirection);
            Vector3 closestPointOnLine = start + lineDirection * projection;

            // Calculate deviation
            Vector3 deviation = positions[i] - closestPointOnLine;
            float deviationMagnitude = deviation.magnitude;

            if (deviationMagnitude > maxDeviation)
            {
                maxDeviation = deviationMagnitude;
                deviationDirection = deviation.normalized;
            }
        }

        // If line is already straight (or nearly straight), use a default curvature
        float defaultCurvature = lineLength * 0.25f;
        float minCurvatureThreshold = lineLength * 0.02f;

        float curvatureAmount;
        if (maxDeviation < minCurvatureThreshold)
        {
            curvatureAmount = defaultCurvature;
            deviationDirection = GetPerpendicularDirection(lineDirection, line.transform);

            if (showDebug) Debug.Log($"Line was straight - applying default curvature: {curvatureAmount:F3}");
        }
        else
        {
            curvatureAmount = maxDeviation;

            if (showDebug) Debug.Log($"Using original curvature: {curvatureAmount:F3}");
        }

        // Generate elliptical curve points
        Vector3[] curvePoints = GenerateEllipticalCurve(start, end, deviationDirection, curvatureAmount, curveResolution);

        line.positionCount = curvePoints.Length;
        line.SetPositions(curvePoints);

        if (showDebug) Debug.Log($"Applied: Smooth Round (elliptical curve with {curvePoints.Length} points)");
    }

    /// <summary>
    /// Gets the surface normal from the line's parent (sketchboard)
    /// For a Quad, the visible face points in -forward direction
    /// </summary>
    private Vector3 GetSurfaceNormal(Transform lineTransform)
    {
        if (lineTransform.parent != null)
        {
            return -lineTransform.parent.forward;
        }
        return Vector3.forward;
    }

    /// <summary>
    /// Detects if the drawn line is an attempt to draw a circle
    /// </summary>
    private bool IsCircleAttempt(Vector3[] positions, out Vector3 center, out float radius)
    {
        center = Vector3.zero;
        radius = 0f;

        if (positions.Length < 8) return false; // Need enough points to detect a circle

        Vector3 start = positions[0];
        Vector3 end = positions[positions.Length - 1];

        // Calculate the bounding box and approximate center
        Vector3 min = positions[0];
        Vector3 max = positions[0];
        Vector3 sum = Vector3.zero;

        foreach (var pos in positions)
        {
            min = Vector3.Min(min, pos);
            max = Vector3.Max(max, pos);
            sum += pos;
        }

        center = sum / positions.Length;
        Vector3 boundingSize = max - min;

        // Calculate average radius
        float totalRadius = 0f;
        foreach (var pos in positions)
        {
            totalRadius += Vector3.Distance(pos, center);
        }
        radius = totalRadius / positions.Length;

        // Check 1: Start and end points should be close (closed shape)
        float startEndDistance = Vector3.Distance(start, end);
        float closedThreshold = radius * 0.4f; // Start/end should be within 40% of radius

        if (startEndDistance > closedThreshold)
        {
            if (showDebug) Debug.Log($"Circle detection: Not closed (gap: {startEndDistance:F3}, threshold: {closedThreshold:F3})");
            return false;
        }

        // Check 2: Bounding box should be roughly square (aspect ratio close to 1)
        float maxDim = Mathf.Max(boundingSize.x, Mathf.Max(boundingSize.y, boundingSize.z));
        float minDim = Mathf.Min(
            boundingSize.x > 0.001f ? boundingSize.x : float.MaxValue,
            Mathf.Min(
                boundingSize.y > 0.001f ? boundingSize.y : float.MaxValue,
                boundingSize.z > 0.001f ? boundingSize.z : float.MaxValue
            )
        );

        // For a 2D circle on a surface, one dimension will be very small
        // So we check the two largest dimensions
        float[] dims = { boundingSize.x, boundingSize.y, boundingSize.z };
        System.Array.Sort(dims);
        float aspectRatio = dims[2] > 0.001f ? dims[1] / dims[2] : 0f;

        if (aspectRatio < 0.5f || aspectRatio > 2f)
        {
            if (showDebug) Debug.Log($"Circle detection: Not square enough (aspect ratio: {aspectRatio:F2})");
            return false;
        }

        // Check 3: Points should be roughly equidistant from center (low variance in radius)
        float radiusVariance = 0f;
        foreach (var pos in positions)
        {
            float dist = Vector3.Distance(pos, center);
            float diff = dist - radius;
            radiusVariance += diff * diff;
        }
        radiusVariance /= positions.Length;
        float radiusStdDev = Mathf.Sqrt(radiusVariance);
        float varianceThreshold = radius * 0.3f; // Allow 30% standard deviation

        if (radiusStdDev > varianceThreshold)
        {
            if (showDebug) Debug.Log($"Circle detection: Radius too variable (stdDev: {radiusStdDev:F3}, threshold: {varianceThreshold:F3})");
            return false;
        }

        if (showDebug) Debug.Log($"Circle detection: SUCCESS! Radius: {radius:F3}, Center: {center}");

        return true;
    }

    /// <summary>
    /// Generates points for a perfect circle on the sketchboard surface
    /// </summary>
    private Vector3[] GeneratePerfectCircle(Vector3 center, float radius, Vector3 surfaceNormal, int resolution)
    {
        Vector3[] points = new Vector3[resolution + 1]; // +1 to close the circle

        // Create two perpendicular vectors that lie ON the surface (perpendicular to the normal)
        Vector3 tangent1 = Vector3.Cross(surfaceNormal, Vector3.up).normalized;
        if (tangent1.sqrMagnitude < 0.01f)
        {
            // Surface normal is parallel to up, use a different reference
            tangent1 = Vector3.Cross(surfaceNormal, Vector3.right).normalized;
        }
        Vector3 tangent2 = Vector3.Cross(surfaceNormal, tangent1).normalized;

        for (int i = 0; i <= resolution; i++)
        {
            float angle = (float)i / resolution * 2f * Mathf.PI;
            float x = Mathf.Cos(angle) * radius;
            float y = Mathf.Sin(angle) * radius;

            // Place point on the surface plane using the two tangent vectors
            points[i] = center + tangent1 * x + tangent2 * y;
        }

        return points;
    }

    /// <summary>
    /// Gets a perpendicular direction for creating curves on straight lines
    /// </summary>
    private Vector3 GetPerpendicularDirection(Vector3 lineDirection, Transform lineTransform)
    {
        // Try to use the parent surface's normal (for lines on sketchboard)
        if (lineTransform.parent != null)
        {
            // For a Quad, -forward is the surface normal
            Vector3 surfaceNormal = -lineTransform.parent.forward;

            // Get perpendicular within the surface plane
            Vector3 perpendicular = Vector3.Cross(lineDirection, surfaceNormal).normalized;

            if (perpendicular.sqrMagnitude > 0.01f)
            {
                return perpendicular;
            }
        }

        // Fallback: find any perpendicular direction
        Vector3 fallbackPerp = Vector3.Cross(lineDirection, Vector3.up).normalized;
        if (fallbackPerp.sqrMagnitude < 0.01f)
        {
            fallbackPerp = Vector3.Cross(lineDirection, Vector3.right).normalized;
        }

        return fallbackPerp;
    }

    /// <summary>
    /// Generates points along an elliptical curve from start to end
    /// </summary>
    private Vector3[] GenerateEllipticalCurve(Vector3 start, Vector3 end, Vector3 curveDirection, float curveHeight, int resolution)
    {
        Vector3[] points = new Vector3[resolution];

        Vector3 midPoint = (start + end) / 2f;
        Vector3 curvePeak = midPoint + curveDirection * curveHeight;

        for (int i = 0; i < resolution; i++)
        {
            float t = (float)i / (resolution - 1);

            // Quadratic Bezier curve: P = (1-t)²·Start + 2(1-t)t·Control + t²·End
            float oneMinusT = 1f - t;
            float oneMinusT2 = oneMinusT * oneMinusT;
            float t2 = t * t;

            Vector3 point = oneMinusT2 * start + 2f * oneMinusT * t * curvePeak + t2 * end;
            points[i] = point;
        }

        return points;
    }

    /// <summary>
    /// Simplifies the line by reducing points (Douglas-Peucker algorithm)
    /// </summary>
    public void SimplifyLine(LineRenderer line, float tolerance = 0.005f)
    {
        if (line == null || line.positionCount < 3) return;

        // Save state before modification
        SaveUndoState(line);

        Vector3[] positions = new Vector3[line.positionCount];
        line.GetPositions(positions);

        List<Vector3> simplified = DouglasPeucker(new List<Vector3>(positions), tolerance);

        line.positionCount = simplified.Count;
        line.SetPositions(simplified.ToArray());

        if (showDebug) Debug.Log($"Applied: Simplify ({positions.Length} -> {simplified.Count} points)");
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
        else
        {
            return new List<Vector3> { points[0], points[points.Count - 1] };
        }
    }

    private float PerpendicularDistance(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        Vector3 line = lineEnd - lineStart;
        float lineLengthSq = line.sqrMagnitude;

        if (lineLengthSq == 0) return Vector3.Distance(point, lineStart);

        float t = Mathf.Clamp01(Vector3.Dot(point - lineStart, line) / lineLengthSq);
        Vector3 projection = lineStart + t * line;

        return Vector3.Distance(point, projection);
    }

    /// <summary>
    /// Deletes the selected line (can be undone)
    /// </summary>
    public void DeleteLine(LineRenderer line)
    {
        if (line == null) return;

        // Save state before deletion (mark as deleted)
        SaveUndoState(line, willBeDeleted: true);

        if (showDebug) Debug.Log("Applied: Delete Line");

        if (line == selectedLine)
        {
            selectedLine = null;
        }
        if (line == currentlyHoveredLine)
        {
            currentlyHoveredLine = null;
        }

        // Deactivate instead of destroy so we can undo
        line.gameObject.SetActive(false);
    }
}