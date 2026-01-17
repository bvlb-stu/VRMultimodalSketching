using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// VR Pencil Drawing - Attach this script to the pencil GameObject.
/// Draws lines from tip_point when trigger is pressed while grabbing the pencil.
/// </summary>
public class VRPencilDrawing : MonoBehaviour
{
    [Header("Drawing Settings")]
    [Tooltip("The tip_point transform where drawing originates")]
    public Transform tipPoint;

    [Tooltip("The material on the tip that determines drawing color")]
    public Renderer tipRenderer;

    [Tooltip("Width of the drawn line")]
    public float lineWidth = 0.005f;

    [Tooltip("Minimum distance to add a new point")]
    public float minDistance = 0.001f;

    [Header("Line Settings")]
    [Tooltip("Parent object to hold all drawn lines (optional, for organization)")]
    public Transform lineParent;

    [Tooltip("Shader for the line material (leave empty for default Unlit/Color)")]
    public Shader lineShader;

    // Private variables
    private XRGrabInteractable grabInteractable;
    private IXRSelectInteractor currentInteractor;
    private LineRenderer currentLine;
    private List<Vector3> currentLinePoints = new List<Vector3>();
    private bool isDrawing = false;
    private InputAction triggerAction;
    private bool isGrabbed = false;

    private void Awake()
    {
        // Get the XRGrabInteractable component
        grabInteractable = GetComponent<XRGrabInteractable>();

        if (grabInteractable == null)
        {
            Debug.LogError("VRPencilDrawing: No XRGrabInteractable found on this GameObject!");
            return;
        }

        // Subscribe to grab events
        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);

        // Set default shader if not assigned
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

        // Clean up input action
        if (triggerAction != null)
        {
            triggerAction.performed -= OnTriggerPressed;
            triggerAction.canceled -= OnTriggerReleased;
            triggerAction.Disable();
            triggerAction.Dispose();
        }
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        isGrabbed = true;
        currentInteractor = args.interactorObject;

        // Try to get the trigger action from the controller
        SetupTriggerAction(args.interactorObject);
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        isGrabbed = false;
        StopDrawing();

        // Clean up trigger action
        if (triggerAction != null)
        {
            triggerAction.performed -= OnTriggerPressed;
            triggerAction.canceled -= OnTriggerReleased;
            triggerAction.Disable();
        }

        currentInteractor = null;
    }

    private void SetupTriggerAction(IXRSelectInteractor interactor)
    {
        // Try to find the ActionBasedController on the interactor
        var controllerInteractor = interactor as MonoBehaviour;
        if (controllerInteractor == null) return;



        // Fallback: Create a direct trigger action binding
        // This works with most XR controller setups
        string triggerBinding = "";

        // Determine if this is left or right hand based on name or position
        string interactorName = controllerInteractor.gameObject.name.ToLower();
        Transform parent = controllerInteractor.transform.parent;
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

        // If we couldn't determine handedness, try both
        if (string.IsNullOrEmpty(triggerBinding))
        {
            if (interactorName.Contains("left"))
            {
                triggerBinding = "<XRController>{LeftHand}/trigger";
            }
            else
            {
                triggerBinding = "<XRController>{RightHand}/trigger";
            }
        }

        // Create and enable the trigger action
        triggerAction = new InputAction("DrawTrigger", InputActionType.Button, triggerBinding);
        triggerAction.performed += OnTriggerPressed;
        triggerAction.canceled += OnTriggerReleased;
        triggerAction.Enable();
    }

    private void OnTriggerPressed(InputAction.CallbackContext context)
    {
        if (isGrabbed)
        {
            StartDrawing();
        }
    }

    private void OnTriggerReleased(InputAction.CallbackContext context)
    {
        StopDrawing();
    }

    private void Update()
    {
        if (isDrawing && currentLine != null && tipPoint != null)
        {
            UpdateDrawing();
        }
    }

    private void StartDrawing()
    {
        if (tipPoint == null)
        {
            Debug.LogWarning("VRPencilDrawing: tipPoint is not assigned!");
            return;
        }

        isDrawing = true;

        // Create a new line
        GameObject lineObj = new GameObject("DrawnLine");

        if (lineParent != null)
        {
            lineObj.transform.SetParent(lineParent);
        }

        currentLine = lineObj.AddComponent<LineRenderer>();

        // Configure line renderer
        currentLine.startWidth = lineWidth;
        currentLine.endWidth = lineWidth;
        currentLine.positionCount = 0;
        currentLine.useWorldSpace = true;
        currentLine.numCapVertices = 5;
        currentLine.numCornerVertices = 5;

        // IMPORTANT: Use TransformZ alignment so the line is visible from all angles
        // (Default "View" mode makes lines face the camera, causing gaps in VR)
        currentLine.alignment = LineAlignment.TransformZ;

        // Create material with tip color
        Material lineMaterial = new Material(lineShader);
        Color drawColor = GetTipColor();
        lineMaterial.color = drawColor;

        // For URP shaders, also set _BaseColor
        if (lineMaterial.HasProperty("_BaseColor"))
        {
            lineMaterial.SetColor("_BaseColor", drawColor);
        }

        currentLine.material = lineMaterial;

        // Clear points list
        currentLinePoints.Clear();

        // Add first point
        AddPoint(tipPoint.position);
    }

    private void UpdateDrawing()
    {
        Vector3 currentPos = tipPoint.position;

        // Only add point if we've moved enough
        if (currentLinePoints.Count == 0 ||
            Vector3.Distance(currentPos, currentLinePoints[currentLinePoints.Count - 1]) >= minDistance)
        {
            AddPoint(currentPos);
        }
    }

    private void AddPoint(Vector3 point)
    {
        currentLinePoints.Add(point);
        currentLine.positionCount = currentLinePoints.Count;
        currentLine.SetPositions(currentLinePoints.ToArray());
    }

    private void StopDrawing()
    {
        isDrawing = false;
        currentLine = null;
        currentLinePoints.Clear();
    }

    private Color GetTipColor()
    {
        if (tipRenderer != null && tipRenderer.material != null)
        {
            // Try to get color from various shader properties
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

        // Default to black if no color found
        return Color.black;
    }

    /// <summary>
    /// Call this to change the pencil/tip color at runtime
    /// </summary>
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

    /// <summary>
    /// Clear all drawn lines
    /// </summary>
    public void ClearAllLines()
    {
        if (lineParent != null)
        {
            foreach (Transform child in lineParent)
            {
                Destroy(child.gameObject);
            }
        }
        else
        {
            // Find and destroy all DrawnLine objects
            var lines = FindObjectsByType<LineRenderer>(FindObjectsSortMode.None);
            foreach (var line in lines)
            {
                if (line.gameObject.name == "DrawnLine")
                {
                    Destroy(line.gameObject);
                }
            }
        }
    }
}