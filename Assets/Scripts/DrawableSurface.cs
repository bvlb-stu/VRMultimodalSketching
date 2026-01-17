using UnityEngine;

/// <summary>
/// Drawable Surface - Attach to any object you want to draw on (like a whiteboard).
/// This script helps with setup and provides a Clear function.
/// </summary>
public class DrawableSurface : MonoBehaviour
{
    [Header("Optional Settings")]
    [Tooltip("Custom color for the surface (white by default)")]
    public Color surfaceColor = Color.white;

    private void Awake()
    {
        // Ensure this object has the correct tag
        if (!gameObject.CompareTag("DrawableSurface"))
        {
            Debug.LogWarning($"DrawableSurface: '{gameObject.name}' needs the 'DrawableSurface' tag! Attempting to set it...");
            // Note: Tag must exist in Tag Manager first
        }

        // Ensure there's a collider
        if (GetComponent<Collider>() == null)
        {
            Debug.LogWarning($"DrawableSurface: '{gameObject.name}' needs a Collider component for drawing to work!");
        }
    }

    /// <summary>
    /// Clears all lines drawn on this surface
    /// </summary>
    public void ClearDrawings()
    {
        // Find and destroy all DrawnLine children
        var linesToDestroy = new System.Collections.Generic.List<GameObject>();

        foreach (Transform child in transform)
        {
            if (child.name == "DrawnLine")
            {
                linesToDestroy.Add(child.gameObject);
            }
        }

        foreach (var line in linesToDestroy)
        {
            if (Application.isPlaying)
            {
                Destroy(line);
            }
            else
            {
                DestroyImmediate(line);
            }
        }

        Debug.Log($"DrawableSurface: Cleared {linesToDestroy.Count} lines from '{gameObject.name}'");
    }

    /// <summary>
    /// Creates a basic whiteboard setup on this object
    /// </summary>
    [ContextMenu("Setup As Whiteboard")]
    public void SetupAsWhiteboard()
    {
        // Add box collider if none exists
        if (GetComponent<Collider>() == null)
        {
            gameObject.AddComponent<BoxCollider>();
        }

        // Set up renderer with white material
        var renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            // Try to create a simple white material
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            if (shader != null)
            {
                Material mat = new Material(shader);
                mat.color = surfaceColor;
                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", surfaceColor);
                }
                renderer.material = mat;
            }
        }

        Debug.Log($"DrawableSurface: '{gameObject.name}' set up as whiteboard. Don't forget to add the 'DrawableSurface' tag!");
    }
}