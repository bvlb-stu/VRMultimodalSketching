using UnityEngine;
using UnityEngine.Events;
using TMPro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Manages the VR Sketch Refinement experiment.
/// Integrates with existing UI: StepManager (card carousel), XR Poke Buttons, Timer.
/// 
/// FLOW:
/// 1. User goes through Cards 1-5 (instructions) using Continue button
/// 2. When Card 6 activates → Experiment begins (OnExperimentCardActivated)
/// 3. Task flow:
///    - Show task description → User reads → Press Start → Timer runs
///    - User completes task → Press End → Shows completion
///    - Press Continue → Next task description shown → repeat
/// 
/// SETUP:
/// 1. Assign "Card 6 start experiment" to Experiment Start Card field
/// 2. On StartButton (XR Simple Interactable) → Select Entered:
///    - Keep: TimerController.ResetTimer, TimerController.StartTimer
///    - Add: ExperimentManager.OnStartButtonPressed
/// 3. On EndButton → Select Entered:
///    - Keep: TimerController.StopTimer
///    - Add: ExperimentManager.OnEndButtonPressed
/// 4. On Continue Button → Select Entered:
///    - Keep: StepManager.Next
///    - Add: ExperimentManager.OnContinuePressed
/// </summary>
public class ExperimentManager : MonoBehaviour
{
    #region Enums

    public enum InteractionCondition
    {
        PenWIMP,
        Multimodal
    }

    public enum ExperimentPhase
    {
        Instructions,
        WaitingToStart,
        Training,
        Block1_TaskReady,
        Block1_TaskActive,
        Block1_TaskComplete,    // NEW: Task done, waiting for Continue
        PostBlock1Questionnaire,
        Block2_TaskReady,
        Block2_TaskActive,
        Block2_TaskComplete,    // NEW: Task done, waiting for Continue
        PostBlock2Questionnaire,
        PostStudy,
        Completed
    }

    public enum CounterbalanceGroup
    {
        GroupA,
        GroupB
    }

    public enum TaskType
    {
        ChangeColor,
        SmoothLinear,
        SmoothRound,
        Delete,
        Combined
    }

    #endregion

    #region Inspector Fields

    [Header("Participant Info - Auto Assignment")]
    [Tooltip("If true, automatically assigns participant ID and alternates groups on each run")]
    public bool autoAssignParticipant = true;

    [Tooltip("Prefix for participant IDs (e.g., 'P' gives P001, P002...)")]
    public string participantPrefix = "P";

    [Header("Participant Info - Manual (used if autoAssign is false)")]
    [Tooltip("Manually set participant ID (only used if autoAssignParticipant is false)")]
    public string participantID = "P001";

    [Tooltip("Manually set group (only used if autoAssignParticipant is false)")]
    public CounterbalanceGroup counterbalanceGroup = CounterbalanceGroup.GroupA;

    [Header("Participant Display (Read Only)")]
    [Tooltip("Shows the current participant info at runtime")]
    [SerializeField] private string currentParticipantDisplay = "";

    // PlayerPrefs keys for persistent storage
    private const string PREF_LAST_PARTICIPANT_NUMBER = "ExperimentLastParticipantNumber";
    private const string PREF_LAST_GROUP = "ExperimentLastGroup";

    [Header("Interaction Systems")]
    public PenOnlyLineSelector wimpSystem;
    public LineGazeSelector multimodalSystem;
    public VRSurfacePencil surfacePencil;

    [Header("Sketchboard")]
    [Tooltip("The sketchboard GameObject - drawings are children of this")]
    public GameObject sketchboard;

    [Tooltip("Clear previous drawings when starting a new task")]
    public bool clearDrawingsOnTaskStart = true;

    [Header("Task Drawing Prefabs")]
    [Tooltip("Prefab drawings to spawn for each task. Index matches task order.")]
    public GameObject[] block1TaskPrefabs = new GameObject[5];

    [Tooltip("Prefab drawings for Block 2 (can be same or different from Block 1)")]
    public GameObject[] block2TaskPrefabs = new GameObject[5];

    [Header("Target Line Identification")]
    [Tooltip("Tag or name suffix to identify the correct target line in each prefab (e.g., 'Target')")]
    public string targetLineIdentifier = "_target";

    [Header("Card System")]
    [Tooltip("Card 6 GameObject - experiment begins when this activates")]
    public GameObject experimentStartCard;

    [Header("Text Displays")]
    [Tooltip("TextMeshPro for task instructions")]
    public TextMeshProUGUI taskInstructionText;

    [Tooltip("TextMeshPro for phase/condition display")]
    public TextMeshProUGUI phaseText;

    [Tooltip("TextMeshPro for step indicator (Task X/Y)")]
    public TextMeshProUGUI stepIndicatorText;

    [Header("Task Configuration")]
    public int tasksPerBlock = 5;
    public float trainingDurationSeconds = 300f;

    [Header("Data Export")]
    public string exportFolder = "ExperimentData";

    [Header("Audio Feedback")]
    public AudioSource audioSource;
    public AudioClip taskStartSound;
    public AudioClip taskCompleteSound;
    public AudioClip errorSound;
    public AudioClip blockCompleteSound;

    [Header("Events")]
    public UnityEvent<string> OnPhaseChanged;
    public UnityEvent<string> OnTaskStarted;
    public UnityEvent OnTaskCompleted;
    public UnityEvent<int> OnBlockCompleted;

    [Header("Debug")]
    public bool showDebug = true;

    #endregion

    #region Private Fields

    private ExperimentPhase currentPhase = ExperimentPhase.Instructions;
    private InteractionCondition currentCondition;
    private int currentTaskIndex = 0;
    private bool isTimerRunning = false;

    private float taskStartTime;
    private float blockStartTime;
    private float trainingStartTime;

    // Error tracking
    private int currentTaskModeErrors = 0;
    private int currentTaskLineSelectionErrors = 0;
    private int currentTaskFunctionSelectionErrors = 0;
    private int currentTaskTotalInteractions = 0;

    private List<TrialData> allTrialData = new List<TrialData>();
    private TrialData currentTrial;
    private List<TaskDefinition> currentBlockTasks = new List<TaskDefinition>();

    // Target tracking for line selection error detection
    private HashSet<LineRenderer> targetLines = new HashSet<LineRenderer>();

    // Expected function for function selection error detection
    private TaskType currentExpectedTaskType;

    #endregion

    #region Data Structures

    [System.Serializable]
    public class TrialData
    {
        public string participantID;
        public string counterbalanceGroup;
        public int blockNumber;
        public string condition;
        public int taskNumber;
        public string taskType;
        public string taskDescription;
        public float completionTimeSeconds;

        // Error metrics
        public int modeErrors;              // Speaking command without looking at target (Multimodal) or clicking empty space (WIMP)
        public int lineSelectionErrors;     // Selecting/acting on wrong line
        public int functionSelectionErrors; // Using wrong function for the task

        // Interaction tracking
        public int totalInteractions;       // Total user-system interactions during task

        public bool taskCompleted;
        public string timestamp;
        public string notes;
    }

    [System.Serializable]
    public class TaskDefinition
    {
        public TaskType taskType;
        public string description;
        public string instruction;

        public TaskDefinition(TaskType type, string desc, string instr)
        {
            taskType = type;
            description = desc;
            instruction = instr;
        }
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Auto-assign participant ID and group if enabled
        if (autoAssignParticipant)
        {
            // UNCOMMENT THE LINE BELOW TO RESET TO P1001 (then re-comment after first run)
            // ResetParticipantCounter();

            AssignNextParticipant();
        }

        // Update display
        currentParticipantDisplay = $"{participantID} - {counterbalanceGroup}";

        // Enable both systems at start so users can freely practice
        // before the experiment begins
        SetSystemsEnabled(true, true);
    }

    private void Start()
    {
        if (experimentStartCard != null)
        {
            var detector = experimentStartCard.GetComponent<CardActivationDetector>();
            if (detector == null)
            {
                detector = experimentStartCard.AddComponent<CardActivationDetector>();
            }
            detector.experimentManager = this;
        }

        UpdatePhaseDisplay();
        UpdateParticipantDisplay();
        Log($"Experiment ready. Participant: {participantID}, Group: {counterbalanceGroup}");
    }

    /// <summary>
    /// Assigns the next participant ID and alternates the group.
    /// Does NOT save to PlayerPrefs - that happens only when data is exported.
    /// </summary>
    private void AssignNextParticipant()
    {
        // Get the last SAVED participant number (default to 1000 if first run, so first participant is P1001)
        int lastSavedNumber = PlayerPrefs.GetInt(PREF_LAST_PARTICIPANT_NUMBER, 1000);
        int lastSavedGroup = PlayerPrefs.GetInt(PREF_LAST_GROUP, 1); // 0 = GroupA, 1 = GroupB (so first will be GroupA)

        // Calculate next participant (but don't save yet)
        int newNumber = lastSavedNumber + 1;
        int newGroup = (lastSavedGroup == 0) ? 1 : 0;

        // Set the values for this session
        participantID = $"{participantPrefix}{newNumber}";
        counterbalanceGroup = (newGroup == 0) ? CounterbalanceGroup.GroupA : CounterbalanceGroup.GroupB;

        // NOTE: We do NOT save to PlayerPrefs here!
        // Saving happens only in SaveParticipantProgress() after data export

        Log($"Assigned (not yet saved): {participantID}, {counterbalanceGroup}");
    }

    /// <summary>
    /// Saves the current participant to PlayerPrefs.
    /// Call this ONLY after successful data export.
    /// </summary>
    private void SaveParticipantProgress()
    {
        // Extract the number from participantID (remove prefix)
        string numberStr = participantID.Replace(participantPrefix, "");
        if (int.TryParse(numberStr, out int currentNumber))
        {
            int currentGroup = (counterbalanceGroup == CounterbalanceGroup.GroupA) ? 0 : 1;

            PlayerPrefs.SetInt(PREF_LAST_PARTICIPANT_NUMBER, currentNumber);
            PlayerPrefs.SetInt(PREF_LAST_GROUP, currentGroup);
            PlayerPrefs.Save();

            Log($"Participant progress SAVED: {participantID}, {counterbalanceGroup}");
        }
    }

    /// <summary>
    /// Resets the participant counter (for testing purposes).
    /// Call this from a debug button or console if needed.
    /// </summary>
    public void ResetParticipantCounter()
    {
        PlayerPrefs.SetInt(PREF_LAST_PARTICIPANT_NUMBER, 1000); // Reset to 1000 so next is P1001
        PlayerPrefs.SetInt(PREF_LAST_GROUP, 1); // So first participant gets GroupA
        PlayerPrefs.Save();
        Log("Participant counter reset. Next participant will be P1001, GroupA");
    }

    /// <summary>
    /// Gets info about what the next participant will be (without assigning).
    /// Useful for preview/confirmation UI.
    /// </summary>
    public (string id, CounterbalanceGroup group) PeekNextParticipant()
    {
        int lastNumber = PlayerPrefs.GetInt(PREF_LAST_PARTICIPANT_NUMBER, 1000);
        int lastGroup = PlayerPrefs.GetInt(PREF_LAST_GROUP, 1);

        int newNumber = lastNumber + 1;
        int newGroup = (lastGroup == 0) ? 1 : 0;

        string id = $"{participantPrefix}{newNumber}";
        CounterbalanceGroup group = (newGroup == 0) ? CounterbalanceGroup.GroupA : CounterbalanceGroup.GroupB;

        return (id, group);
    }

    private void Update()
    {
        if (currentPhase == ExperimentPhase.Training && trainingDurationSeconds > 0)
        {
            float elapsed = Time.time - trainingStartTime;
            if (elapsed >= trainingDurationSeconds)
            {
                EndTraining();
            }
        }
    }

    #endregion

    #region Public Methods - Called by XR Interactable Events

    /// <summary>
    /// Called automatically when Card 6 becomes active.
    /// </summary>
    public void OnExperimentCardActivated()
    {
        if (currentPhase == ExperimentPhase.Instructions)
        {
            currentPhase = ExperimentPhase.WaitingToStart;

            currentCondition = (counterbalanceGroup == CounterbalanceGroup.GroupA)
                ? InteractionCondition.PenWIMP
                : InteractionCondition.Multimodal;

            Log($"Experiment card activated. First condition: {currentCondition}");
            UpdatePhaseDisplay();

            if (taskInstructionText != null)
            {
                string conditionName = (currentCondition == InteractionCondition.PenWIMP)
                    ? "Pen + WIMP (Menu)" : "Multimodal (Gaze + Voice)";
                taskInstructionText.text = $"Welcome {participantID}!\n({counterbalanceGroup})\n\nTraining Phase\n\nYou will practice with: {conditionName}\n\nPress Start when ready to begin training!";
            }
        }
    }

    /// <summary>
    /// Called when Continue poke button is pressed.
    /// After task completion: advances to next task description.
    /// BLOCKED during active tasks (timer running).
    /// </summary>
    public void OnContinuePressed()
    {
        Log($"Continue pressed. Phase: {currentPhase}, TimerRunning: {isTimerRunning}");

        // BLOCK Continue during active tasks
        if (isTimerRunning)
        {
            Log("Continue BLOCKED - task in progress. Press End first.");
            return;
        }

        switch (currentPhase)
        {
            case ExperimentPhase.Instructions:
                // StepManager handles card advancement
                // When Card 6 activates, OnExperimentCardActivated is called
                break;

            case ExperimentPhase.Training:
                EndTraining();
                break;

            case ExperimentPhase.Block1_TaskComplete:
            case ExperimentPhase.Block2_TaskComplete:
                // Task was completed, now advance to next task description
                AdvanceToNextTask();
                break;

            case ExperimentPhase.PostBlock1Questionnaire:
                StartBlock2();
                break;

            case ExperimentPhase.PostBlock2Questionnaire:
                StartPostStudy();
                break;

            case ExperimentPhase.PostStudy:
                CompleteExperiment();
                break;
        }
    }

    /// <summary>
    /// Called when Start poke button is pressed.
    /// Starts the timer for the current task.
    /// </summary>
    public void OnStartButtonPressed()
    {
        Log($"Start pressed. Phase: {currentPhase}");

        switch (currentPhase)
        {
            case ExperimentPhase.WaitingToStart:
                StartTraining();
                break;

            case ExperimentPhase.Training:
                EndTraining();
                break;

            case ExperimentPhase.Block1_TaskReady:
                StartTaskTimer(1);
                break;

            case ExperimentPhase.Block2_TaskReady:
                StartTaskTimer(2);
                break;
        }
    }

    /// <summary>
    /// Called when End poke button is pressed.
    /// Stops timer and marks task complete.
    /// </summary>
    public void OnEndButtonPressed()
    {
        Log($"End pressed. Phase: {currentPhase}");

        if (currentPhase == ExperimentPhase.Block1_TaskActive ||
            currentPhase == ExperimentPhase.Block2_TaskActive)
        {
            CompleteCurrentTask(true);
        }
    }

    #endregion

    #region Phase Management

    private void StartTraining()
    {
        currentPhase = ExperimentPhase.Training;
        trainingStartTime = Time.time;

        // Both systems already enabled from Awake - no need to enable again

        Log("Training started");
        UpdatePhaseDisplay();
        OnPhaseChanged?.Invoke("Training");

        if (taskInstructionText != null)
        {
            taskInstructionText.text = "Training Phase\n\nPractice both techniques:\n• Pen + Menu (WIMP)\n• Gaze + Voice (Multimodal)\n\nPress Start when ready to begin the experiment!";
        }
    }

    private void EndTraining()
    {
        SetSystemsEnabled(false, false);
        Log("Training ended. Starting Block 1.");
        StartBlock1();
    }

    private void StartBlock1()
    {
        currentTaskIndex = 0;
        blockStartTime = Time.time;

        currentCondition = (counterbalanceGroup == CounterbalanceGroup.GroupA)
            ? InteractionCondition.PenWIMP
            : InteractionCondition.Multimodal;

        SetCondition(currentCondition);
        GenerateBlockTasks();

        Log($"Block 1 started: {currentCondition}");
        OnPhaseChanged?.Invoke("Block1");

        ShowTaskDescription(1);
    }

    private void StartBlock2()
    {
        currentTaskIndex = 0;
        blockStartTime = Time.time;

        currentCondition = (counterbalanceGroup == CounterbalanceGroup.GroupA)
            ? InteractionCondition.Multimodal
            : InteractionCondition.PenWIMP;

        SetCondition(currentCondition);
        GenerateBlockTasks();

        Log($"Block 2 started: {currentCondition}");
        OnPhaseChanged?.Invoke("Block2");

        ShowTaskDescription(2);
    }

    private void ShowTaskDescription(int blockNumber)
    {
        currentPhase = (blockNumber == 1) ? ExperimentPhase.Block1_TaskReady : ExperimentPhase.Block2_TaskReady;

        if (currentTaskIndex >= currentBlockTasks.Count)
        {
            EndBlock(blockNumber);
            return;
        }

        TaskDefinition task = currentBlockTasks[currentTaskIndex];

        UpdatePhaseDisplay();

        if (stepIndicatorText != null)
            stepIndicatorText.text = $"Task {currentTaskIndex + 1}/{currentBlockTasks.Count}";

        if (taskInstructionText != null)
        {
            string conditionHint = (currentCondition == InteractionCondition.PenWIMP)
                ? "(Use A button → Point at line → Trigger → Menu)"
                : "(Use X button → Look at line → Speak command)";

            taskInstructionText.text = $"{task.instruction}\n\n{conditionHint}\n\nPress Start when you're ready!";
        }

        Log($"Task {currentTaskIndex + 1} ready: {task.taskType}");
    }

    private void StartTaskTimer(int blockNumber)
    {
        currentPhase = (blockNumber == 1) ? ExperimentPhase.Block1_TaskActive : ExperimentPhase.Block2_TaskActive;

        TaskDefinition task = currentBlockTasks[currentTaskIndex];

        // Clear previous drawings
        if (clearDrawingsOnTaskStart)
        {
            ClearDrawings();
        }

        // Spawn task-specific drawings and track target lines
        SpawnTaskDrawings(blockNumber, currentTaskIndex);

        // Store expected task type for function error checking
        currentExpectedTaskType = task.taskType;

        currentTrial = new TrialData
        {
            participantID = participantID,
            counterbalanceGroup = counterbalanceGroup.ToString(),
            blockNumber = blockNumber,
            condition = currentCondition.ToString(),
            taskNumber = currentTaskIndex + 1,
            taskType = task.taskType.ToString(),
            taskDescription = task.description,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            taskCompleted = false
        };

        // Reset all error counters
        currentTaskModeErrors = 0;
        currentTaskLineSelectionErrors = 0;
        currentTaskFunctionSelectionErrors = 0;
        currentTaskTotalInteractions = 0;

        taskStartTime = Time.time;
        isTimerRunning = true;

        if (taskInstructionText != null)
            taskInstructionText.text = $"⏱️ TASK IN PROGRESS\n\n{task.description}\n\nPress End when complete!";

        UpdatePhaseDisplay();
        PlaySound(taskStartSound);
        OnTaskStarted?.Invoke(task.description);

        Log($"Task {currentTaskIndex + 1} timer started - Expected: {task.taskType}");
    }

    private void CompleteCurrentTask(bool success)
    {
        if (!isTimerRunning) return;

        isTimerRunning = false;

        float completionTime = Time.time - taskStartTime;
        int blockNumber = (currentPhase == ExperimentPhase.Block1_TaskActive) ? 1 : 2;

        // Record all metrics
        currentTrial.completionTimeSeconds = completionTime;
        currentTrial.modeErrors = currentTaskModeErrors;
        currentTrial.lineSelectionErrors = currentTaskLineSelectionErrors;
        currentTrial.functionSelectionErrors = currentTaskFunctionSelectionErrors;
        currentTrial.totalInteractions = currentTaskTotalInteractions;
        currentTrial.taskCompleted = success;

        allTrialData.Add(currentTrial);

        // Clear target tracking
        targetLines.Clear();

        // Set phase to TaskComplete (NOT TaskActive)
        currentPhase = (blockNumber == 1) ? ExperimentPhase.Block1_TaskComplete : ExperimentPhase.Block2_TaskComplete;

        PlaySound(taskCompleteSound);
        OnTaskCompleted?.Invoke();

        Log($"Task {currentTaskIndex + 1} completed in {completionTime:F2}s (Mode:{currentTaskModeErrors}, Line:{currentTaskLineSelectionErrors}, Func:{currentTaskFunctionSelectionErrors}, Interactions:{currentTaskTotalInteractions})");

        if (taskInstructionText != null)
            taskInstructionText.text = $"✓ Task {currentTaskIndex + 1} Complete!\n\nTime: {completionTime:F1}s\n\nPress Continue for the next task.";

        UpdatePhaseDisplay();
    }

    private void AdvanceToNextTask()
    {
        currentTaskIndex++;

        int blockNumber = (currentPhase == ExperimentPhase.Block1_TaskComplete) ? 1 : 2;

        if (currentTaskIndex >= currentBlockTasks.Count)
            EndBlock(blockNumber);
        else
            ShowTaskDescription(blockNumber);
    }

    private void EndBlock(int blockNumber)
    {
        SetSystemsEnabled(false, false);

        float blockDuration = Time.time - blockStartTime;
        Log($"Block {blockNumber} completed in {blockDuration:F1}s");

        PlaySound(blockCompleteSound);
        OnBlockCompleted?.Invoke(blockNumber);

        if (blockNumber == 1)
        {
            currentPhase = ExperimentPhase.PostBlock1Questionnaire;
            if (taskInstructionText != null)
                taskInstructionText.text = "Block 1 Complete!\n\nPlease complete:\n• NASA-TLX questionnaire\n• SEQ (Single Ease Question)\n\nPress Continue when done.";
        }
        else
        {
            currentPhase = ExperimentPhase.PostBlock2Questionnaire;
            if (taskInstructionText != null)
                taskInstructionText.text = "Block 2 Complete!\n\nPlease complete:\n• NASA-TLX questionnaire\n• SEQ\n\nPress Continue when done.";
        }

        UpdatePhaseDisplay();
        OnPhaseChanged?.Invoke(currentPhase.ToString());
    }

    private void StartPostStudy()
    {
        currentPhase = ExperimentPhase.PostStudy;
        Log("Post-study phase.");
        UpdatePhaseDisplay();
        OnPhaseChanged?.Invoke("PostStudy");

        if (taskInstructionText != null)
            taskInstructionText.text = "Post-Study\n\nPlease complete:\n• System Usability Scale (SUS)\n• Naturalness rating\n• Brief interview\n\nPress Continue when finished.";
    }

    private void CompleteExperiment()
    {
        currentPhase = ExperimentPhase.Completed;
        ExportDataToCSV();
        Log("Experiment completed. Data exported.");
        UpdatePhaseDisplay();
        OnPhaseChanged?.Invoke("Completed");

        if (taskInstructionText != null)
            taskInstructionText.text = "Experiment Complete!\n\nThank you for participating!\n\nData has been saved.";
    }

    private void UpdatePhaseDisplay()
    {
        if (phaseText != null)
        {
            string conditionStr = "";
            if (currentPhase.ToString().Contains("Block"))
                conditionStr = (currentCondition == InteractionCondition.PenWIMP) ? " [WIMP]" : " [Multimodal]";
            phaseText.text = $"{participantID} | {counterbalanceGroup}\n{currentPhase}{conditionStr}";
        }
    }

    private void UpdateParticipantDisplay()
    {
        currentParticipantDisplay = $"{participantID} - {counterbalanceGroup}";
    }

    #endregion

    #region Task Management

    private void GenerateBlockTasks()
    {
        currentBlockTasks.Clear();

        currentBlockTasks.Add(new TaskDefinition(
            TaskType.ChangeColor,
            "Change the stroke color to RED",
            "Task 1: Color Change\n\nA stroke will appear. Change its color to RED."
        ));

        currentBlockTasks.Add(new TaskDefinition(
            TaskType.SmoothLinear,
            "Straighten the wavy line",
            "Task 2: Straighten\n\nA wavy line will appear. Make it straight."
        ));

        currentBlockTasks.Add(new TaskDefinition(
            TaskType.SmoothRound,
            "Perfect the hand-drawn circle",
            "Task 3: Perfect Circle\n\nA rough circle will appear. Smooth it into a perfect circle."
        ));

        currentBlockTasks.Add(new TaskDefinition(
            TaskType.Delete,
            "Delete the unwanted stroke",
            "Task 4: Delete\n\nAn unwanted stroke will appear. Delete it."
        ));

        currentBlockTasks.Add(new TaskDefinition(
            TaskType.Combined,
            "Change color to BLUE, then delete extra line",
            "Task 5: Combined\n\nA shape with an extra line will appear.\nChange the shape to BLUE, then delete the extra line."
        ));

        if (currentBlockTasks.Count > tasksPerBlock)
            currentBlockTasks = currentBlockTasks.GetRange(0, tasksPerBlock);
    }

    #endregion

    #region Error Tracking & Metrics

    /// <summary>
    /// Record a MODE ERROR.
    /// - Multimodal: Speaking a command while not looking at a target
    /// - WIMP: Clicking/triggering in empty space (no line targeted)
    /// </summary>
    public void RecordModeError()
    {
        if (isTimerRunning)
        {
            currentTaskModeErrors++;
            currentTaskTotalInteractions++;
            PlaySound(errorSound);
            Log($"MODE ERROR: No target. Total: {currentTaskModeErrors}");
        }
    }

    /// <summary>
    /// Record a LINE SELECTION ERROR.
    /// - User selected/acted on the wrong line (not the target line)
    /// Call this when user selects a line that isn't in the targetLines set.
    /// </summary>
    public void RecordLineSelectionError()
    {
        if (isTimerRunning)
        {
            currentTaskLineSelectionErrors++;
            currentTaskTotalInteractions++;
            PlaySound(errorSound);
            Log($"LINE SELECTION ERROR: Wrong line. Total: {currentTaskLineSelectionErrors}");
        }
    }

    /// <summary>
    /// Record a FUNCTION SELECTION ERROR.
    /// - User activated the wrong function for the current task
    /// </summary>
    public void RecordFunctionSelectionError()
    {
        if (isTimerRunning)
        {
            currentTaskFunctionSelectionErrors++;
            currentTaskTotalInteractions++;
            PlaySound(errorSound);
            Log($"FUNCTION SELECTION ERROR: Wrong function. Total: {currentTaskFunctionSelectionErrors}");
        }
    }

    /// <summary>
    /// Record a successful/valid interaction (no error, just tracking total interactions).
    /// Call this for any valid user-system interaction.
    /// </summary>
    public void RecordInteraction()
    {
        if (isTimerRunning)
        {
            currentTaskTotalInteractions++;
            Log($"Interaction recorded. Total: {currentTaskTotalInteractions}");
        }
    }

    /// <summary>
    /// Check if the selected line is a valid target.
    /// Returns true if valid, false if wrong line (and records LINE SELECTION ERROR).
    /// </summary>
    public bool ValidateLineSelection(LineRenderer selectedLine)
    {
        if (!isTimerRunning) return false;
        if (selectedLine == null) return false;

        Log($"ValidateLineSelection: Checking line, targetLines.Count={targetLines.Count}");

        // If we have target lines tracked, validate against them
        if (targetLines.Count > 0)
        {
            if (targetLines.Contains(selectedLine))
            {
                Log("ValidateLineSelection: CORRECT - Line is a target");
                return true;
            }
            else
            {
                // Wrong line selected - record error
                Log("ValidateLineSelection: WRONG - Line is NOT a target, recording error");
                RecordLineSelectionError();
                return false;
            }
        }

        // No target tracking - assume any line is valid (user-drawn scenario)
        Log("ValidateLineSelection: No targets tracked, allowing any line");
        return true;
    }

    /// <summary>
    /// Check if the activated function matches the expected task.
    /// Returns true if valid, false if wrong function (and records FUNCTION SELECTION ERROR).
    /// </summary>
    public bool ValidateFunctionSelection(string functionName)
    {
        if (!isTimerRunning) return false;

        bool isValid = false;

        switch (currentExpectedTaskType)
        {
            case TaskType.ChangeColor:
                isValid = functionName.ToLower().Contains("color") || functionName.ToLower().Contains("colour");
                break;
            case TaskType.SmoothLinear:
                isValid = functionName.ToLower().Contains("straight") || functionName.ToLower().Contains("linear");
                break;
            case TaskType.SmoothRound:
                isValid = functionName.ToLower().Contains("round") || functionName.ToLower().Contains("smooth") || functionName.ToLower().Contains("curve");
                break;
            case TaskType.Delete:
                isValid = functionName.ToLower().Contains("delete") || functionName.ToLower().Contains("remove") || functionName.ToLower().Contains("erase");
                break;
            case TaskType.Combined:
                // Combined tasks accept multiple functions
                isValid = functionName.ToLower().Contains("color") || functionName.ToLower().Contains("colour") ||
                          functionName.ToLower().Contains("delete") || functionName.ToLower().Contains("remove");
                break;
        }

        if (isValid)
        {
            RecordInteraction();
            Log($"Valid function: {functionName} for task {currentExpectedTaskType}");
        }
        else
        {
            RecordFunctionSelectionError();
            Log($"WRONG function: {functionName}, expected for task {currentExpectedTaskType}");
        }

        return isValid;
    }

    /// <summary>
    /// Register a line as a target line for the current task.
    /// Call this when spawning task drawings.
    /// </summary>
    public void RegisterTargetLine(LineRenderer line)
    {
        if (line != null)
        {
            targetLines.Add(line);
            Log($"Registered target line: {line.gameObject.name}");
        }
    }

    /// <summary>
    /// Check if a line is a target (without recording error).
    /// </summary>
    public bool IsTargetLine(LineRenderer line)
    {
        return targetLines.Contains(line);
    }

    /// <summary>
    /// Get the expected task type for the current task.
    /// </summary>
    public TaskType GetExpectedTaskType()
    {
        return currentExpectedTaskType;
    }

    #endregion

    #region System Control

    /// <summary>
    /// Clears all drawings from the sketchboard.
    /// Destroys all child GameObjects (drawn lines).
    /// </summary>
    public void ClearDrawings()
    {
        if (sketchboard == null)
        {
            Log("Cannot clear drawings - sketchboard not assigned");
            return;
        }

        int childCount = sketchboard.transform.childCount;

        // Destroy all children (drawn lines)
        for (int i = childCount - 1; i >= 0; i--)
        {
            Transform child = sketchboard.transform.GetChild(i);
            Destroy(child.gameObject);
        }

        Log($"Cleared {childCount} drawings from sketchboard");
    }

    /// <summary>
    /// Spawns the pre-made drawings for the current task.
    /// Instantiates children of the prefab as children of the sketchboard.
    /// Tracks which lines are targets (marked with targetLineIdentifier) for error counting.
    /// </summary>
    private void SpawnTaskDrawings(int blockNumber, int taskIndex)
    {
        if (sketchboard == null)
        {
            Log("Cannot spawn task drawings - sketchboard not assigned");
            return;
        }

        // Clear previous target tracking
        targetLines.Clear();

        GameObject[] prefabs = (blockNumber == 1) ? block1TaskPrefabs : block2TaskPrefabs;

        if (prefabs == null || taskIndex >= prefabs.Length || prefabs[taskIndex] == null)
        {
            Log($"No prefab assigned for Block {blockNumber} Task {taskIndex + 1}");
            return;
        }

        GameObject prefab = prefabs[taskIndex];

        // Check if the prefab itself has a LineRenderer (single line prefab)
        LineRenderer prefabLine = prefab.GetComponent<LineRenderer>();
        if (prefabLine != null)
        {
            GameObject spawned = Instantiate(prefab, sketchboard.transform);
            spawned.transform.localPosition = Vector3.zero;
            spawned.transform.localRotation = Quaternion.identity;
            spawned.transform.localScale = Vector3.one;

            // Check if this is a target (case-insensitive)
            bool isTarget = prefab.name.ToLower().Contains(targetLineIdentifier.ToLower());
            Log($"Single prefab '{prefab.name}' isTarget={isTarget}");

            // Name must be "DrawnLine" for selection to work
            spawned.name = "DrawnLine";

            LineRenderer lr = spawned.GetComponent<LineRenderer>();
            FixLineRenderer(lr);

            // Track as target
            if (isTarget)
            {
                targetLines.Add(lr);
                Log($"Target line spawned for Block {blockNumber} Task {taskIndex + 1}");
            }

            spawned.SetActive(true);
            Log($"Spawned single line for Block {blockNumber} Task {taskIndex + 1}");
            return;
        }

        // If the prefab is a container with multiple DrawnLine children
        int spawnedCount = 0;
        int targetCount = 0;
        int nonTargetCount = 0;
        foreach (Transform child in prefab.transform)
        {
            GameObject spawnedChild = Instantiate(child.gameObject, sketchboard.transform);

            spawnedChild.transform.localPosition = child.localPosition;
            spawnedChild.transform.localRotation = child.localRotation;
            spawnedChild.transform.localScale = child.localScale;

            // Check if this child is a target (name contains identifier) BEFORE renaming
            // Use case-insensitive comparison
            string childNameLower = child.name.ToLower();
            string identifierLower = targetLineIdentifier.ToLower();
            bool isTarget = childNameLower.Contains(identifierLower);
            Log($"Checking child '{child.name}' (lower: '{childNameLower}') for '{targetLineIdentifier}' (lower: '{identifierLower}'): isTarget={isTarget}");

            // Name must be "DrawnLine" for selection to work
            spawnedChild.name = "DrawnLine";

            LineRenderer lr = spawnedChild.GetComponent<LineRenderer>();
            if (lr != null)
            {
                FixLineRenderer(lr);

                // Track as target or non-target
                if (isTarget)
                {
                    targetLines.Add(lr);
                    targetCount++;
                    Log($"REGISTERED TARGET: {child.name}");
                }
                else
                {
                    nonTargetCount++;
                    Log($"Non-target line: {child.name}");
                }
            }

            // Also check grandchildren
            foreach (Transform grandchild in spawnedChild.transform)
            {
                LineRenderer grandLr = grandchild.GetComponent<LineRenderer>();
                if (grandLr != null)
                {
                    // Check original name before renaming (case-insensitive)
                    bool isGrandchildTarget = grandchild.name.ToLower().Contains(targetLineIdentifier.ToLower());
                    grandchild.gameObject.name = "DrawnLine";
                    FixLineRenderer(grandLr);

                    if (isGrandchildTarget)
                    {
                        targetLines.Add(grandLr);
                        targetCount++;
                        Log($"REGISTERED TARGET (grandchild): {grandchild.name}");
                    }
                }
            }

            spawnedChild.SetActive(true);
            spawnedCount++;
        }

        Log($"Spawned {spawnedCount} drawings for Block {blockNumber} Task {taskIndex + 1} (Targets: {targetCount}, Non-targets: {nonTargetCount})");
    }

    /// <summary>
    /// Fixes a LineRenderer to use local space and have a valid material.
    /// </summary>
    private void FixLineRenderer(LineRenderer lr)
    {
        if (lr == null) return;

        // CRITICAL: Use local space so it follows the sketchboard
        lr.useWorldSpace = false;

        // Fix missing material (purple = missing shader/material)
        if (lr.sharedMaterial == null || lr.sharedMaterial.shader == null ||
            lr.sharedMaterial.shader.name == "Hidden/InternalErrorShader")
        {
            // Create a new valid material
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader != null)
            {
                Material newMat = new Material(shader);

                // Try to preserve the original color if possible
                Color originalColor = Color.black;
                if (lr.sharedMaterial != null)
                {
                    if (lr.sharedMaterial.HasProperty("_Color"))
                        originalColor = lr.sharedMaterial.color;
                    else if (lr.sharedMaterial.HasProperty("_BaseColor"))
                        originalColor = lr.sharedMaterial.GetColor("_BaseColor");
                }

                newMat.color = originalColor;
                if (newMat.HasProperty("_BaseColor"))
                {
                    newMat.SetColor("_BaseColor", originalColor);
                }

                lr.material = newMat;

                Log($"Fixed material for LineRenderer: {lr.gameObject.name}");
            }
        }
    }

    private void SetCondition(InteractionCondition condition)
    {
        currentCondition = condition;

        switch (condition)
        {
            case InteractionCondition.PenWIMP:
                SetSystemsEnabled(true, false);
                break;
            case InteractionCondition.Multimodal:
                SetSystemsEnabled(false, true);
                break;
        }

        Log($"Condition set: {condition}");
    }

    private void SetSystemsEnabled(bool wimp, bool multimodal)
    {
        if (wimpSystem != null)
        {
            wimpSystem.enabled = wimp;
            if (!wimp && wimpSystem.handMenuUI != null)
                wimpSystem.handMenuUI.SetActive(false);
        }

        if (multimodalSystem != null)
            multimodalSystem.enabled = multimodal;
    }

    #endregion

    #region Data Export

    private string GetExportPath()
    {
        string folder = Path.Combine(Application.persistentDataPath, exportFolder);
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"experiment_{participantID}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }

    public void ExportDataToCSV()
    {
        string path = GetExportPath();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("ParticipantID,CounterbalanceGroup,BlockNumber,Condition,TaskNumber,TaskType,TaskDescription,CompletionTime_Seconds,ModeErrors,LineSelectionErrors,FunctionSelectionErrors,TotalInteractions,TaskCompleted,Timestamp,Notes");

        foreach (var trial in allTrialData)
        {
            sb.AppendLine($"{trial.participantID}," +
                         $"{trial.counterbalanceGroup}," +
                         $"{trial.blockNumber}," +
                         $"{trial.condition}," +
                         $"{trial.taskNumber}," +
                         $"{trial.taskType}," +
                         $"\"{trial.taskDescription}\"," +
                         $"{trial.completionTimeSeconds:F3}," +
                         $"{trial.modeErrors}," +
                         $"{trial.lineSelectionErrors}," +
                         $"{trial.functionSelectionErrors}," +
                         $"{trial.totalInteractions}," +
                         $"{trial.taskCompleted}," +
                         $"{trial.timestamp}," +
                         $"\"{trial.notes ?? ""}\"");
        }

        try
        {
            File.WriteAllText(path, sb.ToString());
            Log($"Data exported to: {path}");

            // Only increment participant counter AFTER successful data export
            if (autoAssignParticipant)
            {
                SaveParticipantProgress();
            }
        }
        catch (Exception e)
        {
            Log($"ERROR exporting data: {e.Message}");
            // Don't save participant progress if export failed
        }
    }

    #endregion

    #region Utility

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    private void Log(string message)
    {
        if (showDebug)
            Debug.Log($"[ExperimentManager] {message}");
    }

    #endregion

    #region Public Getters

    public ExperimentPhase CurrentPhase => currentPhase;
    public InteractionCondition CurrentCondition => currentCondition;
    public bool IsTimerRunning => isTimerRunning;
    public int CurrentTaskIndex => currentTaskIndex;
    public float CurrentTaskTime => isTimerRunning ? Time.time - taskStartTime : 0f;

    #endregion
}

/// <summary>
/// Helper component - detects when Card 6 becomes active.
/// Automatically added by ExperimentManager.
/// </summary>
public class CardActivationDetector : MonoBehaviour
{
    public ExperimentManager experimentManager;

    private void OnEnable()
    {
        if (experimentManager != null)
        {
            experimentManager.OnExperimentCardActivated();
        }
    }
}