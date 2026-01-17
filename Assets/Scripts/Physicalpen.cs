using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// A VR pen that physically stops at surfaces (like a whiteboard).
/// Uses a simple kinematic approach for stability.
/// 
/// SETUP:
/// 1. Create empty GameObject "Pen" 
/// 2. Add child "PenVisual" with the 3D model
/// 3. Add child "TipPoint" (empty transform at pen tip position)
/// 4. Add this script to "Pen"
/// 5. Add XR Grab Interactable to "Pen" with Movement Type = Instantaneous
/// 6. Set Rigidbody to Is Kinematic = true
/// 7. Set "Surface Layer Mask" to the whiteboard's layer
/// 
/// WORKS WITH: VRSurfacePencil (assign TipPoint as the Tip Point reference)
/// </summary>
public class PhysicalPen : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The visual representation of the pen (child object with the 3D model)")]
    public Transform penVisual;

    [Tooltip("Transform at the tip of the pen (for drawing detection)")]
    public Transform tipPoint;

    [Tooltip("The surface/whiteboard transform to check distance against")]
    public Transform whiteboardSurface;

    [Header("Collision Settings")]
    [Tooltip("Layer mask for surfaces the pen should collide with")]
    public LayerMask surfaceLayerMask = -1;

    [Tooltip("Maximum distance to check for surface")]
    public float maxCheckDistance = 0.5f;

    [Tooltip("Offset to keep pen tip slightly above surface")]
    public float surfaceOffset = 0.002f;

    [Header("Debug")]
    public bool showDebug = true;
    public bool showGizmos = true;

    // State
    private bool isTouchingSurface = false;
    private Vector3 surfacePoint;
    private Vector3 surfaceNormal;

    // References
    private XRGrabInteractable grabInteractable;
    private Rigidbody rb;
    private bool isGrabbed = false;

    // Store original parent relationship
    private Vector3 visualLocalPos;
    private Quaternion visualLocalRot;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();

        // Ensure rigidbody is kinematic for stability
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Store visual's local transform
        if (penVisual != null)
        {
            visualLocalPos = penVisual.localPosition;
            visualLocalRot = penVisual.localRotation;
        }
    }

    private void Start()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnGrab);
            grabInteractable.selectExited.AddListener(OnRelease);
        }

        if (showDebug)
        {
            Debug.Log($"PhysicalPen: Started. LayerMask value: {surfaceLayerMask.value}");
            if (whiteboardSurface != null)
                Debug.Log($"PhysicalPen: Whiteboard assigned: {whiteboardSurface.name}, Layer: {whiteboardSurface.gameObject.layer}");
            else
                Debug.LogWarning("PhysicalPen: No whiteboard surface assigned!");
        }
    }

    private void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnGrab);
            grabInteractable.selectExited.RemoveListener(OnRelease);
        }
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        isGrabbed = true;
        if (showDebug) Debug.Log("PhysicalPen: Grabbed");
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        isGrabbed = false;
        isTouchingSurface = false;

        // Reset visual to local position
        if (penVisual != null)
        {
            penVisual.localPosition = visualLocalPos;
            penVisual.localRotation = visualLocalRot;
        }

        if (showDebug) Debug.Log("PhysicalPen: Released");
    }

    private void LateUpdate()
    {
        if (penVisual == null || tipPoint == null) return;

        // Always reset visual to follow controller first
        penVisual.localPosition = visualLocalPos;
        penVisual.localRotation = visualLocalRot;

        if (isGrabbed)
        {
            CheckAndConstrainToSurface();
        }
    }

    private void CheckAndConstrainToSurface()
    {
        Vector3 tipPos = tipPoint.position;

        // METHOD 1: Direct whiteboard reference (most reliable)
        if (whiteboardSurface != null)
        {
            // Get the whiteboard's forward direction (assuming it faces outward)
            // The whiteboard's local Z- or Z+ should point "out" from the drawing surface
            Vector3 boardNormal = -whiteboardSurface.forward; // Adjust sign if needed
            Vector3 boardPosition = whiteboardSurface.position;

            // Calculate distance from tip to the whiteboard plane
            Vector3 tipToBoard = tipPos - boardPosition;
            float distanceToPlane = Vector3.Dot(tipToBoard, boardNormal);

            if (showDebug && Time.frameCount % 60 == 0)
            {
                Debug.Log($"PhysicalPen: Distance to board plane: {distanceToPlane:F4}m");
            }

            // If tip is past the surface (negative distance means behind the plane)
            if (distanceToPlane < surfaceOffset)
            {
                isTouchingSurface = true;

                // Calculate how much to push back
                float correction = surfaceOffset - distanceToPlane;

                // Move the visual back along the surface normal
                penVisual.position += boardNormal * correction;

                surfacePoint = tipPos + boardNormal * (-distanceToPlane);
                surfaceNormal = boardNormal;

                if (showDebug && Time.frameCount % 30 == 0)
                {
                    Debug.Log($"PhysicalPen: CONSTRAINED! Correction: {correction:F4}m");
                }
            }
            else
            {
                isTouchingSurface = false;
            }

            return;
        }

        // METHOD 2: Raycast (fallback if no direct reference)
        // Cast ray from tip toward where the surface should be
        // Try multiple directions to find the surface

        Vector3[] rayDirections = new Vector3[]
        {
            -tipPoint.up,           // Tip pointing down
            tipPoint.forward,       // Tip pointing forward
            -tipPoint.forward,      // Tip pointing backward
            tipPoint.up,            // Tip pointing up
            Vector3.down,           // World down
            Vector3.forward,        // World forward
        };

        foreach (Vector3 dir in rayDirections)
        {
            RaycastHit hit;
            if (Physics.Raycast(tipPos, dir, out hit, maxCheckDistance, surfaceLayerMask))
            {
                float distToSurface = hit.distance;

                if (showDebug && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"PhysicalPen: Raycast hit {hit.collider.name} at distance {distToSurface:F4}m, dir: {dir}");
                }

                // If very close to or past surface
                if (distToSurface < surfaceOffset * 5)
                {
                    isTouchingSurface = true;
                    surfacePoint = hit.point;
                    surfaceNormal = hit.normal;

                    // Push visual back
                    float correction = surfaceOffset - distToSurface;
                    if (correction > 0)
                    {
                        penVisual.position += hit.normal * correction;

                        if (showDebug)
                        {
                            Debug.Log($"PhysicalPen: RAYCAST CONSTRAINED! Correction: {correction:F4}m");
                        }
                    }
                    return;
                }
            }
        }

        isTouchingSurface = false;

        if (showDebug && Time.frameCount % 120 == 0)
        {
            Debug.Log($"PhysicalPen: No surface detected. Tip pos: {tipPos}, LayerMask: {surfaceLayerMask.value}");
        }
    }

    /// <summary>
    /// Returns true if the pen tip is currently touching a surface
    /// </summary>
    public bool IsTouchingSurface => isTouchingSurface;

    /// <summary>
    /// Returns the current surface contact point (world space)
    /// </summary>
    public Vector3 SurfaceContactPoint => surfacePoint;

    /// <summary>
    /// Returns the surface normal at contact point
    /// </summary>
    public Vector3 SurfaceNormal => surfaceNormal;

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        // Draw tip
        if (tipPoint != null)
        {
            Gizmos.color = isTouchingSurface ? Color.red : Color.green;
            Gizmos.DrawWireSphere(tipPoint.position, 0.005f);

            // Draw ray directions being checked
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(tipPoint.position, -tipPoint.up * 0.05f);
            Gizmos.DrawRay(tipPoint.position, tipPoint.forward * 0.05f);
        }

        // Draw surface contact
        if (isTouchingSurface)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(surfacePoint, 0.008f);
            Gizmos.DrawRay(surfacePoint, surfaceNormal * 0.05f);
        }

        // Draw whiteboard reference
        if (whiteboardSurface != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(whiteboardSurface.position, new Vector3(0.3f, 0.3f, 0.01f));
            Gizmos.DrawRay(whiteboardSurface.position, -whiteboardSurface.forward * 0.1f);
        }
    }
}