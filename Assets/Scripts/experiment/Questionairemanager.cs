using UnityEngine;
using UnityEngine.Events;
using System;

/// <summary>
/// Manages questionnaire timing and integration points for the experiment.
/// This component triggers events when questionnaires should be administered.
/// 
/// Questionnaires used:
/// - Pre-Survey: Demographics and profiling
/// - NASA-TLX: After each condition (Block 1 and Block 2)
/// - SEQ (Single Ease Question): After each condition
/// - SUS (System Usability Scale): Post-study
/// - Naturalness Rating: Custom Likert scale, post-study
/// </summary>
public class QuestionnaireManager : MonoBehaviour
{
    [Header("Events")]
    [Tooltip("Triggered when pre-survey should be administered")]
    public UnityEvent OnPreSurveyRequired;

    [Tooltip("Triggered after Block 1 - NASA-TLX + SEQ for first condition")]
    public UnityEvent<string> OnPostBlock1Required; // Parameter: condition name

    [Tooltip("Triggered after Block 2 - NASA-TLX + SEQ for second condition")]
    public UnityEvent<string> OnPostBlock2Required; // Parameter: condition name

    [Tooltip("Triggered for post-study questionnaires - SUS + Naturalness")]
    public UnityEvent OnPostStudyRequired;

    [Header("Questionnaire URLs (Optional)")]
    [Tooltip("URL for online pre-survey form")]
    public string preSurveyURL = "";

    [Tooltip("URL for NASA-TLX")]
    public string nasaTLXURL = "";

    [Tooltip("URL for SUS")]
    public string susURL = "";

    [Header("References")]
    public ExperimentManager experimentManager;

    [Header("Questionnaire Status")]
    [SerializeField] private bool preSurveyCompleted = false;
    [SerializeField] private bool block1QuestionnaireCompleted = false;
    [SerializeField] private bool block2QuestionnaireCompleted = false;
    [SerializeField] private bool postStudyCompleted = false;

    [Header("Debug")]
    public bool showDebug = true;

    // Questionnaire data storage
    private QuestionnaireData currentData;

    [System.Serializable]
    public class QuestionnaireData
    {
        // Pre-survey
        public string participantID;
        public int age;
        public string gender;
        public int vrExperienceLevel; // 1-5
        public int sketchingExperienceLevel; // 1-5
        public bool usesVoiceAssistants;

        // NASA-TLX (collected twice - once per condition)
        public NASATLXData nasaTLX_Condition1;
        public NASATLXData nasaTLX_Condition2;

        // SEQ
        public int seqRating_Condition1; // 1-7
        public int seqRating_Condition2; // 1-7

        // SUS
        public int[] susResponses = new int[10]; // 10 questions, 1-5 scale

        // Custom naturalness
        public int naturalnessRating_WIMP; // 1-7
        public int naturalnessRating_Multimodal; // 1-7
        public string naturalnessComments;
    }

    [System.Serializable]
    public class NASATLXData
    {
        public string condition;
        public int mentalDemand;     // 1-21
        public int physicalDemand;   // 1-21
        public int temporalDemand;   // 1-21
        public int performance;      // 1-21
        public int effort;           // 1-21
        public int frustration;      // 1-21

        public float CalculateOverallScore()
        {
            return (mentalDemand + physicalDemand + temporalDemand +
                    performance + effort + frustration) / 6f;
        }
    }

    private void Start()
    {
        currentData = new QuestionnaireData();

        if (experimentManager != null)
        {
            currentData.participantID = experimentManager.participantID;
        }
    }

    /// <summary>
    /// Call this to trigger the pre-survey
    /// </summary>
    public void TriggerPreSurvey()
    {
        Log("Pre-survey required");
        OnPreSurveyRequired?.Invoke();

        if (!string.IsNullOrEmpty(preSurveyURL))
        {
            Application.OpenURL(preSurveyURL);
        }
    }

    /// <summary>
    /// Call this after Block 1 completion
    /// </summary>
    public void TriggerPostBlock1Questionnaire(string conditionName)
    {
        Log($"Post-Block 1 questionnaire required for condition: {conditionName}");
        OnPostBlock1Required?.Invoke(conditionName);

        if (!string.IsNullOrEmpty(nasaTLXURL))
        {
            Application.OpenURL(nasaTLXURL);
        }
    }

    /// <summary>
    /// Call this after Block 2 completion
    /// </summary>
    public void TriggerPostBlock2Questionnaire(string conditionName)
    {
        Log($"Post-Block 2 questionnaire required for condition: {conditionName}");
        OnPostBlock2Required?.Invoke(conditionName);

        if (!string.IsNullOrEmpty(nasaTLXURL))
        {
            Application.OpenURL(nasaTLXURL);
        }
    }

    /// <summary>
    /// Call this for post-study questionnaires
    /// </summary>
    public void TriggerPostStudy()
    {
        Log("Post-study questionnaires required (SUS + Naturalness)");
        OnPostStudyRequired?.Invoke();

        if (!string.IsNullOrEmpty(susURL))
        {
            Application.OpenURL(susURL);
        }
    }

    // Methods to record questionnaire responses

    public void RecordPreSurvey(int age, string gender, int vrExp, int sketchExp, bool usesVoice)
    {
        currentData.age = age;
        currentData.gender = gender;
        currentData.vrExperienceLevel = vrExp;
        currentData.sketchingExperienceLevel = sketchExp;
        currentData.usesVoiceAssistants = usesVoice;
        preSurveyCompleted = true;
        Log("Pre-survey recorded");
    }

    public void RecordNASATLX(int blockNumber, string condition, int mental, int physical, int temporal, int performance, int effort, int frustration)
    {
        NASATLXData tlx = new NASATLXData
        {
            condition = condition,
            mentalDemand = mental,
            physicalDemand = physical,
            temporalDemand = temporal,
            performance = performance,
            effort = effort,
            frustration = frustration
        };

        if (blockNumber == 1)
        {
            currentData.nasaTLX_Condition1 = tlx;
        }
        else
        {
            currentData.nasaTLX_Condition2 = tlx;
        }

        Log($"NASA-TLX recorded for block {blockNumber}. Overall score: {tlx.CalculateOverallScore():F1}");
    }

    public void RecordSEQ(int blockNumber, int rating)
    {
        if (blockNumber == 1)
        {
            currentData.seqRating_Condition1 = rating;
            block1QuestionnaireCompleted = true;
        }
        else
        {
            currentData.seqRating_Condition2 = rating;
            block2QuestionnaireCompleted = true;
        }

        Log($"SEQ recorded for block {blockNumber}: {rating}/7");
    }

    public void RecordSUS(int[] responses)
    {
        if (responses.Length == 10)
        {
            currentData.susResponses = responses;
            Log($"SUS recorded. Score: {CalculateSUSScore(responses):F1}");
        }
    }

    public void RecordNaturalness(int wimpRating, int multimodalRating, string comments)
    {
        currentData.naturalnessRating_WIMP = wimpRating;
        currentData.naturalnessRating_Multimodal = multimodalRating;
        currentData.naturalnessComments = comments;
        postStudyCompleted = true;
        Log($"Naturalness recorded - WIMP: {wimpRating}/7, Multimodal: {multimodalRating}/7");
    }

    /// <summary>
    /// Calculate SUS score from 10 responses (1-5 scale)
    /// </summary>
    public float CalculateSUSScore(int[] responses)
    {
        if (responses.Length != 10) return 0;

        float score = 0;
        for (int i = 0; i < 10; i++)
        {
            if (i % 2 == 0) // Odd questions (1,3,5,7,9)
            {
                score += responses[i] - 1;
            }
            else // Even questions (2,4,6,8,10)
            {
                score += 5 - responses[i];
            }
        }
        return score * 2.5f; // Scale to 0-100
    }

    public QuestionnaireData GetQuestionnaireData()
    {
        return currentData;
    }

    public bool AllQuestionnairesCompleted()
    {
        return preSurveyCompleted && block1QuestionnaireCompleted &&
               block2QuestionnaireCompleted && postStudyCompleted;
    }

    private void Log(string message)
    {
        if (showDebug)
        {
            Debug.Log($"[QuestionnaireManager] {message}");
        }
    }
}