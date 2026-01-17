using UnityEngine;
using TMPro;

public class TimerController : MonoBehaviour
{
    [Header("UI Display")]
    [Tooltip("Arrasta aqui o TextMeshPro onde o tempo será mostrado")]
    public TextMeshProUGUI timerText;

    [Header("Settings")]
    [Tooltip("Formato: true = MM:SS:ms, false = MM:SS")]
    public bool showMilliseconds = true;

    private float elapsedTime = 0f;
    private bool isRunning = false;

    void Start()
    {
        UpdateTimerDisplay();
    }

    void Update()
    {
        if (isRunning)
        {
            elapsedTime += Time.deltaTime;
            UpdateTimerDisplay();
        }
    }

    /// <summary>
    /// Inicia o cronómetro (continua de onde parou)
    /// </summary>
    public void StartTimer()
    {
        isRunning = true;
        Debug.Log("Timer Started");
    }

    /// <summary>
    /// Para o cronómetro
    /// </summary>
    public void StopTimer()
    {
        isRunning = false;
        Debug.Log("Timer Stopped at: " + GetFormattedTime());
    }

    /// <summary>
    /// Reinicia o cronómetro para zero (não inicia automaticamente)
    /// </summary>
    public void ResetTimer()
    {
        elapsedTime = 0f;
        UpdateTimerDisplay();
        Debug.Log("Timer Reset");
    }

    /// <summary>
    /// Reinicia E inicia o cronómetro (útil para o botão Start)
    /// </summary>
    public void RestartTimer()
    {
        ResetTimer();
        StartTimer();
    }

    /// <summary>
    /// Alterna entre iniciar e parar
    /// </summary>
    public void ToggleTimer()
    {
        if (isRunning)
            StopTimer();
        else
            StartTimer();
    }

    /// <summary>
    /// Retorna o tempo decorrido em segundos
    /// </summary>
    public float GetElapsedTime()
    {
        return elapsedTime;
    }

    /// <summary>
    /// Retorna o tempo formatado como string
    /// </summary>
    public string GetFormattedTime()
    {
        int minutes = Mathf.FloorToInt(elapsedTime / 60f);
        int seconds = Mathf.FloorToInt(elapsedTime % 60f);
        int milliseconds = Mathf.FloorToInt((elapsedTime * 100f) % 100f);

        if (showMilliseconds)
            return string.Format("{0:00}:{1:00}:{2:00}", minutes, seconds, milliseconds);
        else
            return string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    /// <summary>
    /// Verifica se o cronómetro está a correr
    /// </summary>
    public bool IsRunning()
    {
        return isRunning;
    }

    private void UpdateTimerDisplay()
    {
        if (timerText != null)
        {
            timerText.text = GetFormattedTime();
        }
    }
}
