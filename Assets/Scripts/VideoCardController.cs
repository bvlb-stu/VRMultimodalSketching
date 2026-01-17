using UnityEngine;
using UnityEngine.Video;

public class VideoCardController : MonoBehaviour
{
    private VideoPlayer videoPlayer;

    void Awake()
    {
        videoPlayer = GetComponent<VideoPlayer>();
    }

    // This runs automatically when the Carousel activates this card object
    void OnEnable()
    {
        if (videoPlayer != null)
        {
            videoPlayer.Play();
        }
    }

    // This runs automatically when the user clicks "Continue" and this card is hidden
    void OnDisable()
    {
        if (videoPlayer != null)
        {
            videoPlayer.Stop(); // Or .Pause() if you want to resume later
        }
    }
}