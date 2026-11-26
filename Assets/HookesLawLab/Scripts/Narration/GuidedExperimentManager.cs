using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Unity.XR.CoreUtils;
using TMPro;
using HookesLawLab.Physics;
using HookesLawLab.Interaction;
using HookesLawLab.UI;
using HookesLawLab.Teacher;
using HookesLawLab.Lab;

namespace HookesLawLab.Narration
{
    public enum GuidedStep
    {
        Idle = 0,
        Welcome = 1,
        ArriveAtTable = 2,
        PickupWeight = 3,
        WeightPickedUp = 4,
        WeightAttached = 5,
        SpringSettled = 6,
        MeasurementRecorded = 7,
        CompareGraph = 8,
        TryDifferentGravity = 9,
        ExploreTeacherAndBoard = 10,
        Completion = 11,
        Completed = 12
    }

    /// <summary>
    /// Event-driven 10-step guided voice-narration state machine for Physics Lab (PC & Meta Quest 3S).
    /// Reuses PhysicsTeacher's proven AudioSource/Resources playback path in 2D mode, gives user-initiated
    /// teacher questions strict priority, starts Welcome automatically when audio is ready, and ties popup
    /// visibility strictly to actual AudioSource playback via cancellation tokens.
    /// </summary>
    public class GuidedExperimentManager : MonoBehaviour
    {
        private struct QueuedChunk
        {
            public string id;
            public NarrationEntry entry;
            public AudioClip clip;
        }

        private class NarrationZone
        {
            public string zoneName;
            public string narrationId;
            public Vector3 worldCenter;
            public float enterRadius;
            public float exitRadius;
            public bool isInside;
        }

        public GuidedStep CurrentStep { get; private set; } = GuidedStep.Idle;
        public bool IsGuideActive => CurrentStep != GuidedStep.Idle && CurrentStep != GuidedStep.Completed;
        public int CurrentStepNumber => CurrentStep >= GuidedStep.ArriveAtTable && CurrentStep <= GuidedStep.Completion
            ? ((int)CurrentStep - 1)
            : 0;
        public bool IsPaused { get; private set; }
        public float VoiceVolume { get; private set; } = 0.9f;

        private GuidedNarrationCatalogue _catalogue;
        private AudioSource _fallbackAudioSource;

        private SpringSimulation _springSim;
        private AttachmentManager _attachmentManager;
        private TVGraphDisplay _tvGraph;
        private PlanetGravityTablet _gravityTablet;
        private PhysicsTeacher _teacher;
        private LabPlayerController _playerController;
        private XROrigin _xrOrigin;
        private TeleportationProvider _teleportProvider;

        private Vector3 _tableCenterWorld;
        private Vector3 _teacherWorldPos;
        private Vector3 _boardsCenterWorld;
        private Vector3 _tabletWorldPos;
        private readonly List<XRRayInteractor> _teleportInteractors = new List<XRRayInteractor>();
        private readonly List<NarrationZone> _zones = new List<NarrationZone>();

        // Cancellation token & chunked playback lifecycle
        private int _playbackToken;
        private bool _isPlayingEntry;
        private bool _pendingWelcomeOnReady = true;
        private bool _welcomeCompletedThisSession;
        private string _activeId = "";
        private NarrationEntry _activeEntry;
        private Action<int> _onSequenceFinished;
        private readonly Queue<QueuedChunk> _chunkQueue = new Queue<QueuedChunk>();

        // Pause / yield gates
        private bool _pausedByTeleportAim;
        private bool _pausedByAppFocus;
        private bool _pausedByTeacher;
        private bool _isTeleportLocomotionActive;

        // Stability & cooldown tracking
        private float _equilibriumStableTime;
        private float _lastPlanetNarrationTime = -10f;
        private bool _wasExceedingElasticLimit;

        // Audio-verified played tracking (populated ONLY after audio playback truly starts)
        private readonly HashSet<string> _playedThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _playedThisStep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _lastPlayedTime = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        // Experiment completion tracking (kept independent of graph clears on gravity change)
        private readonly Dictionary<(int gravityBucket, int kBucket), HashSet<int>> _recordedLoadsByCondition
            = new Dictionary<(int, int), HashSet<int>>();
        private readonly Dictionary<(int massG, int kBucket), HashSet<int>> _settledGravitiesByLoad
            = new Dictionary<(int, int), HashSet<int>>();

        private bool _hasCompletedThreeLoadGraph;
        private bool _hasCompletedGravityComparison;
        private bool _hasAcknowledgedTableEarth;

        public event Action OnGuideUIStateChanged;

        public void Initialize(
            GuidedNarrationCatalogue catalogue,
            SpringSimulation springSim,
            AttachmentManager attachmentManager,
            TVGraphDisplay tvGraph,
            PlanetGravityTablet gravityTablet,
            PhysicsTeacher teacher,
            LabPlayerController playerController,
            Vector3 tableCenterWorld,
            TMP_FontAsset fontAsset = null,
            Vector3? teacherWorldPos = null,
            Vector3? boardsCenterWorld = null,
            Vector3? tabletWorldPos = null)
        {
            _catalogue = catalogue != null
                ? catalogue
                : (Resources.Load<GuidedNarrationCatalogue>("GuidedNarrationCatalogue")
                   ?? GuidedNarrationCatalogue.CreateDefaultRuntimeCatalogue());
            _catalogue.RebuildLookup();

            _springSim = springSim;
            _attachmentManager = attachmentManager;
            _tvGraph = tvGraph;
            _gravityTablet = gravityTablet;
            _teacher = teacher;
            _playerController = playerController;
            _tableCenterWorld = tableCenterWorld;
            _teacherWorldPos = teacherWorldPos ?? new Vector3(-tableCenterWorld.x + 3.3186f, 0f, 0.005f);
            _boardsCenterWorld = boardsCenterWorld ?? new Vector3(5.8f, 1.8f, 3.0f);
            _tabletWorldPos = tabletWorldPos ?? (gravityTablet != null ? gravityTablet.transform.position : tableCenterWorld + new Vector3(-0.95f, 0f, -0.22f));

            _teacher?.SetExternalVoiceCoordinator(this);

            // Fallback AudioSource used ONLY if no PhysicsTeacher exists in scene
            if (_teacher == null)
            {
                _fallbackAudioSource = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
                _fallbackAudioSource.playOnAwake = false;
                _fallbackAudioSource.spatialBlend = 0f;
                _fallbackAudioSource.volume = VoiceVolume;
            }

            _xrOrigin = FindFirstObjectByType<XROrigin>();
            _teleportProvider = FindFirstObjectByType<TeleportationProvider>();
            _teleportInteractors.Clear();
            if (_xrOrigin != null)
            {
                foreach (XRRayInteractor r in _xrOrigin.GetComponentsInChildren<XRRayInteractor>(true))
                {
                    if (r.gameObject.name.IndexOf("teleport", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _teleportInteractors.Add(r);
                    }
                }
            }

            EnsureSingleActiveAudioListener(logIfAdjusted: true);
            BuildLocationZones();

            // Voice-only mode: destroy any stray popup hosts if present
            GameObject strayPopup = GameObject.Find("GuideSubtitleCanvasHost");
            if (strayPopup != null) Destroy(strayPopup);

            SubscribeEvents();
            LogMissingClipsChecklist();

            // Schedule automatic Welcome once scene + working teacher audio service are confirmed ready
            _pendingWelcomeOnReady = true;
            _welcomeCompletedThisSession = false;
        }

        private void BuildLocationZones()
        {
            _zones.Clear();

            _zones.Add(new NarrationZone
            {
                zoneName = "ExperimentTable",
                narrationId = "apparatus",
                worldCenter = _tableCenterWorld,
                enterRadius = 1.75f, // Spawn is ~1.95m away so user must walk/teleport up to bench
                exitRadius = 2.15f
            });

            _zones.Add(new NarrationZone
            {
                zoneName = "LectureAndFeaturesBoards",
                narrationId = "hookes_law",
                worldCenter = _boardsCenterWorld,
                enterRadius = 2.65f,
                exitRadius = 3.05f
            });

            _zones.Add(new NarrationZone
            {
                zoneName = "PlanetaryGravityStation",
                narrationId = "gravity_prompt",
                worldCenter = _tabletWorldPos,
                enterRadius = 1.15f,
                exitRadius = 1.45f
            });

            _zones.Add(new NarrationZone
            {
                zoneName = "TeacherStation",
                narrationId = "teacher_help",
                worldCenter = _teacherWorldPos,
                enterRadius = 2.35f,
                exitRadius = 2.75f
            });
        }

        private void SubscribeEvents()
        {
            if (_attachmentManager != null)
            {
                _attachmentManager.OnMassPickedUp += HandleMassPickedUp;
                _attachmentManager.OnMassAttached += HandleMassAttached;
                _attachmentManager.OnMassReturnedOutsideSnap += HandleMassReturnedOutsideSnap;
                _attachmentManager.OnMassDetached += HandleMassDetached;
            }

            if (_tvGraph != null)
            {
                _tvGraph.OnMeasurementRecorded += HandleMeasurementRecorded;
                _tvGraph.OnRecordRejectedMoving += HandleRecordRejectedMoving;
                _tvGraph.OnRecordRejectedEmpty += HandleRecordRejectedEmpty;
                _tvGraph.OnExperimentReset += HandleExperimentReset;
            }

            if (_gravityTablet != null)
            {
                _gravityTablet.OnGravityChanged += HandleGravityChanged;
            }

            if (_teacher != null)
            {
                _teacher.OnTeacherHelpOpened += HandleTeacherBusyChanged;
                _teacher.OnTeacherLessonStarted += HandleTeacherBusyChanged;
                _teacher.OnTeacherLessonEnded += HandleTeacherLessonEnded;
            }

            if (_teleportProvider != null)
            {
                _teleportProvider.locomotionStarted += HandleTeleportStarted;
                _teleportProvider.locomotionEnded += HandleTeleportEnded;
            }
        }

        private void UnsubscribeEvents()
        {
            if (_attachmentManager != null)
            {
                _attachmentManager.OnMassPickedUp -= HandleMassPickedUp;
                _attachmentManager.OnMassAttached -= HandleMassAttached;
                _attachmentManager.OnMassReturnedOutsideSnap -= HandleMassReturnedOutsideSnap;
                _attachmentManager.OnMassDetached -= HandleMassDetached;
            }

            if (_tvGraph != null)
            {
                _tvGraph.OnMeasurementRecorded -= HandleMeasurementRecorded;
                _tvGraph.OnRecordRejectedMoving -= HandleRecordRejectedMoving;
                _tvGraph.OnRecordRejectedEmpty -= HandleRecordRejectedEmpty;
                _tvGraph.OnExperimentReset -= HandleExperimentReset;
            }

            if (_gravityTablet != null)
            {
                _gravityTablet.OnGravityChanged -= HandleGravityChanged;
            }

            if (_teacher != null)
            {
                _teacher.OnTeacherHelpOpened -= HandleTeacherBusyChanged;
                _teacher.OnTeacherLessonStarted -= HandleTeacherBusyChanged;
                _teacher.OnTeacherLessonEnded -= HandleTeacherLessonEnded;
            }

            if (_teleportProvider != null)
            {
                _teleportProvider.locomotionStarted -= HandleTeleportStarted;
                _teleportProvider.locomotionEnded -= HandleTeleportEnded;
            }
        }

        private void OnDisable()
        {
            CancelAndHideActiveNarration("guide_disabled");
            UnsubscribeEvents();
        }

        private void OnDestroy()
        {
            CancelAndHideActiveNarration("guide_destroyed");
            UnsubscribeEvents();
        }

        private void HandleTeleportStarted(UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider _)
        {
            _isTeleportLocomotionActive = true;
        }

        private void HandleTeleportEnded(UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider _)
        {
            _isTeleportLocomotionActive = false;
            Vector3 pos = GetTrackedPlayerPositionXZ();
            Debug.Log($"[GuidedNarration] Teleport arrived at XZ=({pos.x:F2}, {pos.z:F2}).");
            EvaluateArrivalZones(viaTeleport: true);
        }

        private void OnApplicationPause(bool pauseStatus) => HandleSystemPause(pauseStatus);

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) HandleSystemPause(false);
            else if (UnityEngine.XR.XRSettings.isDeviceActive) HandleSystemPause(true);
        }

        private void HandleSystemPause(bool pause)
        {
            if (pause)
            {
                _pausedByAppFocus = true;
                PauseActiveAudio();
            }
            else
            {
                _pausedByAppFocus = false;
                EnsureSingleActiveAudioListener(logIfAdjusted: false);
                if (_isPlayingEntry && !IsPaused && !_pausedByTeleportAim && !_pausedByTeacher)
                {
                    ResumeActiveAudio();
                }
            }
        }

        // ---------------------------------------------------------------------
        // Public Controls (Start / Restart Guide WITHOUT Resetting Experiment)
        // ---------------------------------------------------------------------

        public void StartGuide()
        {
            // Restarting the guide clears pending guide speech/state without resetting weights, graph, or gravity
            CancelAndHideActiveNarration("guide_restarted");
            ResetGuideTrackingOnly();
            _pendingWelcomeOnReady = false;
            IsPaused = false;

            if (IsAudioServiceReady())
            {
                BeginWelcomeOrCatchUpState();
            }
            else
            {
                _pendingWelcomeOnReady = true;
            }
            OnGuideUIStateChanged?.Invoke();
        }

        public void TogglePauseResume()
        {
            if (!_isPlayingEntry && !IsGuideActive) return;
            IsPaused = !IsPaused;
            if (IsPaused)
            {
                PauseActiveAudio();
            }
            else
            {
                if (_isPlayingEntry && !_pausedByTeleportAim && !_pausedByAppFocus && !_pausedByTeacher)
                {
                    ResumeActiveAudio();
                }
            }
            OnGuideUIStateChanged?.Invoke();
        }

        public void RepeatCurrentInstruction()
        {
            if (!IsGuideActive && _activeEntry == null)
            {
                StartGuide();
                return;
            }

            IsPaused = false;
            string id = GetNarrationIdForStep(CurrentStep) ?? _activeId;
            if (!string.IsNullOrEmpty(id))
            {
                PlaySingleNarration(id, forceReplay: true, onComplete: GetCompletionCallbackForStep(CurrentStep), reason: "user_repeat");
            }
            OnGuideUIStateChanged?.Invoke();
        }

        public void StopGuide()
        {
            _pendingWelcomeOnReady = false;
            CancelAndHideActiveNarration("user_stop");
            CurrentStep = GuidedStep.Idle;
            IsPaused = false;
            OnGuideUIStateChanged?.Invoke();
        }

        public void AdjustVoiceVolume(float delta)
        {
            VoiceVolume = Mathf.Clamp01(VoiceVolume + delta);
            _teacher?.SetVoiceVolume(VoiceVolume);
            if (_fallbackAudioSource != null) _fallbackAudioSource.volume = VoiceVolume;
            OnGuideUIStateChanged?.Invoke();
        }

        public void NotifyTeacherSpeechStarting()
        {
            _pausedByTeacher = true;
            if (_isPlayingEntry)
            {
                // Teacher questions always take priority: pause guide without cutting teacher audio
                PauseActiveAudio();
                Debug.Log("[GuidedNarration] Yielded audio for user-initiated Teacher speech.");
            }
        }

        public void NotifyTeacherSpeechEnded()
        {
            if (!_pausedByTeacher) return;
            _pausedByTeacher = false;
            if (_teacher != null && _teacher.IsTeacherBusy) return;

            if (_isPlayingEntry && !IsPaused && !_pausedByTeleportAim && !_pausedByAppFocus)
            {
                ResumeActiveAudio();
            }
        }

        // ---------------------------------------------------------------------
        // Development Diagnostics & Editor Test Actions (No Player UI)
        // ---------------------------------------------------------------------

        [ContextMenu("Test Narration - Play First Assigned Guide Clip")]
        public bool TestPlayFirstAssignedClip()
        {
            NarrationEntry playable = _catalogue?.GetFirstPlayableEntry();
            if (playable == null)
            {
                Debug.LogWarning("[GuidedNarration][Test] No playable Guide clips found.");
                LogFullDiagnostics();
                return false;
            }
            return TestPlayNarrationById(playable.id);
        }

        [ContextMenu("Test Narration - Force Replay Welcome")]
        public void TestPlayWelcomeMenu() => TestPlayNarrationById("welcome");

        public bool TestPlayNarrationById(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            IsPaused = false;
            _pausedByAppFocus = false;
            _pausedByTeleportAim = false;
            _pausedByTeacher = false;
            return PlaySingleNarration(id, forceReplay: true, onComplete: null, reason: "editor_test");
        }

        [ContextMenu("Test Narration - Log Audio & Zone Diagnostics")]
        public void LogFullDiagnostics()
        {
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            int enabledCount = 0;
            foreach (AudioListener l in listeners)
            {
                if (l != null && l.enabled && l.gameObject.activeInHierarchy) enabledCount++;
            }

            AudioSource src = GetActiveVoiceSource();
            Vector3 p = GetTrackedPlayerPositionXZ();
            Debug.Log(
                $"[GuidedNarration][Diag] Step={CurrentStep}, Token={_playbackToken}, IsPlayingEntry={_isPlayingEntry}, ActiveId={_activeId}, " +
                $"TeacherBusy={_teacher?.IsTeacherBusy}, AudioReady={IsAudioServiceReady()}, AudioListeners={enabledCount}/{listeners.Length}, ListenerVol={AudioListener.volume:F2}, ListenerPause={AudioListener.pause}, " +
                $"VoiceSrc=(enabled={src?.enabled}, mute={src?.mute}, vol={src?.volume:F2}, spatial={src?.spatialBlend:F1}, isPlaying={src?.isPlaying}), PlayerXZ=({p.x:F2},{p.z:F2})");
        }

        // ---------------------------------------------------------------------
        // Per-Frame Update
        // ---------------------------------------------------------------------

        private void Update()
        {
            // 0. Yield if user opened or is speaking to PhysicsTeacher
            bool teacherBusyNow = _teacher != null && _teacher.IsTeacherBusy;
            if (teacherBusyNow && !_pausedByTeacher)
            {
                NotifyTeacherSpeechStarting();
            }
            else if (!teacherBusyNow && _pausedByTeacher)
            {
                NotifyTeacherSpeechEnded();
            }

            // 1. Entry automatic Welcome once audio service is ready (never repeat after sleep/tracking loss)
            if (_pendingWelcomeOnReady && !_welcomeCompletedThisSession)
            {
                if (!IsTeleportAimingActive() && !_isTeleportLocomotionActive && IsAudioServiceReady() && !teacherBusyNow)
                {
                    _pendingWelcomeOnReady = false;
                    BeginWelcomeOrCatchUpState();
                }
            }

            // 2. Teleport aiming gate
            bool teleportAiming = IsTeleportAimingActive();
            if (teleportAiming != _pausedByTeleportAim)
            {
                _pausedByTeleportAim = teleportAiming;
                if (_pausedByTeleportAim)
                {
                    if (_isPlayingEntry)
                    {
                        PauseActiveAudio();
                    }
                }
                else if (!IsPaused && !_pausedByAppFocus && !_pausedByTeacher && _isPlayingEntry)
                {
                    if (IsEntryStillRelevant(_activeId))
                    {
                        ResumeActiveAudio();
                    }
                    else
                    {
                        CancelAndHideActiveNarration("irrelevant_after_teleport");
                    }
                }
            }

            // 3. Elastic limit interrupt
            if (IsGuideActive && _springSim != null)
            {
                bool exceeding = _springSim.IsExceedingElasticLimit;
                if (exceeding && !_wasExceedingElasticLimit && !teacherBusyNow && !teleportAiming)
                {
                    PlaySingleNarration("elastic_limit", forceReplay: false, onComplete: null, reason: "elastic_limit");
                }
                _wasExceedingElasticLimit = exceeding;
            }

            // 4. Actual audio playback lifecycle driving popup visibility & chunk progression
            UpdateAudioPlaybackLifecycle();

            // 5. Location-based zone checks (never while aiming teleport ray or teleporting)
            if (!teleportAiming && !_isTeleportLocomotionActive && !teacherBusyNow)
            {
                EvaluateArrivalZones(viaTeleport: false);
            }

            // 6. Experiment state checks (stability window, etc.)
            if (IsGuideActive && !teleportAiming && !_isTeleportLocomotionActive && !teacherBusyNow)
            {
                EvaluateStepConditions();
            }
        }

        private void UpdateAudioPlaybackLifecycle()
        {
            if (!_isPlayingEntry) return;

            bool anyPauseGate = IsPaused || _pausedByTeleportAim || _pausedByAppFocus || _pausedByTeacher;
            if (anyPauseGate) return;

            if (IsCurrentAudioPlaying()) return;

            // Current clip finished (allow 1-frame grace after Play only if audio just finished cleanly)
            if (_chunkQueue.Count > 0)
            {
                int tokenBefore = _playbackToken;
                QueuedChunk next = _chunkQueue.Dequeue();
                StartChunkAudio(next, tokenBefore);
                return;
            }

            // Final chunk completed -> hide & clear popup immediately, then run completion callback with token check
            int completedToken = _playbackToken;
            string finishedId = _activeId;
            Action<int> cb = _onSequenceFinished;

            ClearPlaybackStateAndHidePopup();
            Debug.Log($"[GuidedNarration] Audio playback COMPLETED: id='{finishedId}', token={completedToken}.");

            cb?.Invoke(completedToken);
        }

        // ---------------------------------------------------------------------
        // Workflow State Machine & Early-Action Synchronization
        // ---------------------------------------------------------------------

        private void BeginWelcomeOrCatchUpState()
        {
            // Skip obsolete steps if the user already acted before Welcome started
            if (TryCatchUpToLiveExperimentState()) return;

            if (IsPlayerAtTable())
            {
                TransitionToStep(GuidedStep.ArriveAtTable, "entry_already_at_table");
                return;
            }

            TransitionToStep(GuidedStep.Welcome, "auto_entry_welcome");
        }

        private bool TryCatchUpToLiveExperimentState()
        {
            if (_attachmentManager != null && _attachmentManager.AttachedMasses.Count > 0)
            {
                if (_springSim != null && _springSim.IsAtEquilibrium)
                    TransitionToStep(GuidedStep.SpringSettled, "catchup_already_settled");
                else
                    TransitionToStep(GuidedStep.WeightAttached, "catchup_already_attached");
                return true;
            }
            return false;
        }

        private bool IsPlayerAtTable()
        {
            Vector3 p = GetTrackedPlayerPositionXZ();
            float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(_tableCenterWorld.x, _tableCenterWorld.z));
            return d <= 1.75f;
        }

        private void EvaluateArrivalZones(bool viaTeleport)
        {
            Vector3 playerPos = GetTrackedPlayerPositionXZ();

            for (int i = 0; i < _zones.Count; i++)
            {
                NarrationZone zone = _zones[i];
                float dist = Vector2.Distance(
                    new Vector2(playerPos.x, playerPos.z),
                    new Vector2(zone.worldCenter.x, zone.worldCenter.z));

                if (!zone.isInside && dist <= zone.enterRadius)
                {
                    zone.isInside = true;
                    Debug.Log($"[GuidedNarration] Reached area '{zone.zoneName}' (dist={dist:F2}m, viaTeleport={viaTeleport}, step={CurrentStep}).");

                    if (zone.zoneName == "ExperimentTable" && (CurrentStep == GuidedStep.Welcome || CurrentStep == GuidedStep.Idle))
                    {
                        if (!TryCatchUpToLiveExperimentState())
                        {
                            TransitionToStep(GuidedStep.ArriveAtTable, $"arrived_table_{(viaTeleport ? "teleport" : "walk")}");
                        }
                        break;
                    }
                    else if (zone.zoneName == "TeacherStation" && CurrentStep == GuidedStep.ExploreTeacherAndBoard && !_isPlayingEntry)
                    {
                        // User arrived near teacher during Step 9; completion can trigger once teacher help played
                        break;
                    }
                }
                else if (zone.isInside && dist > zone.exitRadius)
                {
                    zone.isInside = false;
                }
            }
        }

        private void EvaluateStepConditions()
        {
            if (CurrentStep == GuidedStep.ArriveAtTable)
            {
                // If Earth wasn't initially selected, watch until Earth is selected
                if (_gravityTablet != null && _gravityTablet.SelectedPlanet.id == "earth" && _hasAcknowledgedTableEarth && !_isPlayingEntry)
                {
                    TransitionToStep(GuidedStep.PickupWeight, "earth_confirmed_at_table");
                }
            }
            else if (CurrentStep == GuidedStep.WeightAttached && _springSim != null)
            {
                if (_springSim.AttachedMassKg > 0.001f && _springSim.IsAtEquilibrium)
                {
                    _equilibriumStableTime += Time.deltaTime;
                    if (_equilibriumStableTime >= 0.25f)
                    {
                        TransitionToStep(GuidedStep.SpringSettled, "spring_stabilized");
                    }
                }
                else
                {
                    _equilibriumStableTime = 0f;
                }
            }
            else if (CurrentStep == GuidedStep.TryDifferentGravity && _springSim != null)
            {
                if (_springSim.AttachedMassKg > 0.001f && _springSim.IsAtEquilibrium)
                {
                    RecordSettledGravitySample();
                }
            }
        }

        private void TransitionToStep(GuidedStep nextStep, string reason)
        {
            if (CurrentStep == nextStep && nextStep != GuidedStep.Welcome) return;

            CurrentStep = nextStep;
            _playedThisStep.Clear();
            _equilibriumStableTime = 0f;

            // Only interrupt outdated speech cleanly via token cancellation
            CancelAndHideActiveNarration($"step_{nextStep}");

            int expectedTokenAfterStart;
            switch (nextStep)
            {
                case GuidedStep.Welcome:
                    if (PlaySingleNarration("welcome", false, token =>
                    {
                        if (token != _playbackToken || CurrentStep != GuidedStep.Welcome) return;
                        if (IsPlayerAtTable()) TransitionToStep(GuidedStep.ArriveAtTable, "welcome_finished_at_table");
                    }, reason))
                    {
                        _welcomeCompletedThisSession = true;
                    }
                    break;

                case GuidedStep.ArriveAtTable:
                    bool earthActive = _gravityTablet == null || _gravityTablet.SelectedPlanet.id == "earth";
                    if (earthActive)
                    {
                        _hasAcknowledgedTableEarth = true;
                        // Chunk 1: Apparatus overview + Chunk 2: Acknowledge Earth -> seamlessly continue to Step 2
                        PlayChunkedNarration(new[] { "apparatus", "gravity_earth" }, false, token =>
                        {
                            if (token != _playbackToken || CurrentStep != GuidedStep.ArriveAtTable) return;
                            TransitionToStep(GuidedStep.PickupWeight, "earth_already_selected_ready_pickup");
                        }, reason);
                    }
                    else
                    {
                        _hasAcknowledgedTableEarth = false;
                        PlaySingleNarration("apparatus", false, null, reason);
                    }
                    break;

                case GuidedStep.PickupWeight:
                    PlaySingleNarration("pickup_prompt", false, null, reason);
                    break;

                case GuidedStep.WeightPickedUp:
                    PlaySingleNarration("pickup_success", false, null, reason);
                    break;

                case GuidedStep.WeightAttached:
                    PlaySingleNarration("attach_success", false, token =>
                    {
                        if (token != _playbackToken || CurrentStep != GuidedStep.WeightAttached) return;
                        if (_springSim != null && _springSim.AttachedMassKg > 0.001f && _springSim.IsAtEquilibrium)
                        {
                            TransitionToStep(GuidedStep.SpringSettled, "attach_finished_already_settled");
                        }
                    }, reason);
                    break;

                case GuidedStep.SpringSettled:
                    PlaySingleNarration("record_prompt", false, null, reason);
                    break;

                case GuidedStep.MeasurementRecorded:
                    PlaySingleNarration("record_success", false, token =>
                    {
                        if (token != _playbackToken || CurrentStep != GuidedStep.MeasurementRecorded) return;
                        if (_hasCompletedThreeLoadGraph)
                        {
                            TransitionToStep(GuidedStep.CompareGraph, "three_loads_completed");
                        }
                        else
                        {
                            // Wait or gently transition ready for next load; no forced interruption if user already acted
                            PlaySingleNarration("try_different_loads", false, null, "prompt_next_load");
                        }
                    }, reason);
                    break;

                case GuidedStep.CompareGraph:
                    PlaySingleNarration("graph_explanation", false, token =>
                    {
                        if (token != _playbackToken || CurrentStep != GuidedStep.CompareGraph) return;
                        TransitionToStep(GuidedStep.TryDifferentGravity, "graph_explanation_done");
                    }, reason);
                    break;

                case GuidedStep.TryDifferentGravity:
                    expectedTokenAfterStart = _playbackToken + 1;
                    PlaySingleNarration("gravity_prompt", false, null, reason);
                    _ = expectedTokenAfterStart;
                    break;

                case GuidedStep.ExploreTeacherAndBoard:
                    PlaySingleNarration("teacher_help", false, token =>
                    {
                        if (token != _playbackToken || CurrentStep != GuidedStep.ExploreTeacherAndBoard) return;
                        TransitionToStep(GuidedStep.Completion, "teacher_board_intro_done");
                    }, reason);
                    break;

                case GuidedStep.Completion:
                    PlaySingleNarration("completion", false, token =>
                    {
                        if (token != _playbackToken || CurrentStep != GuidedStep.Completion) return;
                        CurrentStep = GuidedStep.Completed;
                        OnGuideUIStateChanged?.Invoke();
                    }, reason);
                    break;
            }

            OnGuideUIStateChanged?.Invoke();
        }

        private string GetNarrationIdForStep(GuidedStep step)
        {
            switch (step)
            {
                case GuidedStep.Welcome: return "welcome";
                case GuidedStep.ArriveAtTable: return "apparatus";
                case GuidedStep.PickupWeight: return "pickup_prompt";
                case GuidedStep.WeightPickedUp: return "pickup_success";
                case GuidedStep.WeightAttached: return "attach_success";
                case GuidedStep.SpringSettled: return "record_prompt";
                case GuidedStep.MeasurementRecorded: return "record_success";
                case GuidedStep.CompareGraph: return "graph_explanation";
                case GuidedStep.TryDifferentGravity: return "gravity_prompt";
                case GuidedStep.ExploreTeacherAndBoard: return "teacher_help";
                case GuidedStep.Completion: return "completion";
                default: return null;
            }
        }

        private Action<int> GetCompletionCallbackForStep(GuidedStep step)
        {
            return token =>
            {
                if (token != _playbackToken || CurrentStep != step) return;
                if (step == GuidedStep.Completion)
                {
                    CurrentStep = GuidedStep.Completed;
                    OnGuideUIStateChanged?.Invoke();
                }
            };
        }

        // ---------------------------------------------------------------------
        // Experiment Interaction Handlers
        // ---------------------------------------------------------------------

        private void HandleMassPickedUp(MassInteractable mass)
        {
            if (CurrentStep == GuidedStep.Completion || CurrentStep == GuidedStep.Completed || CurrentStep == GuidedStep.Idle) return;

            // Early action: skip welcome/table/pickup prompt directly to Step 3 (WeightPickedUp)
            if (CurrentStep < GuidedStep.WeightPickedUp)
            {
                TransitionToStep(GuidedStep.WeightPickedUp, "mass_picked_up");
            }
            else if (CurrentStep == GuidedStep.MeasurementRecorded && !_hasCompletedThreeLoadGraph)
            {
                // User picked up next mass for 2nd/3rd reading
                TransitionToStep(GuidedStep.WeightPickedUp, "next_mass_picked_up");
            }
        }

        private void HandleMassAttached(MassInteractable mass)
        {
            if (CurrentStep == GuidedStep.Completion || CurrentStep == GuidedStep.Completed || CurrentStep == GuidedStep.Idle) return;

            // Step 4 ONLY triggers on confirmed attachment (never just snap proximity)
            if (CurrentStep <= GuidedStep.SpringSettled || CurrentStep == GuidedStep.MeasurementRecorded)
            {
                TransitionToStep(GuidedStep.WeightAttached, "confirmed_attachment");
            }
        }

        private void HandleMassReturnedOutsideSnap(MassInteractable mass)
        {
            if (CurrentStep == GuidedStep.WeightPickedUp)
            {
                PlaySingleNarration("return_to_table", false, null, "returned_to_table");
            }
        }

        private void HandleMassDetached(MassInteractable mass)
        {
            // Keep experiment open and free
        }

        private void HandleMeasurementRecorded(TVGraphDisplay.DataPoint dp, float gravity, float springK)
        {
            if (dp.massG <= 0) return;

            int gravBucket = Mathf.RoundToInt(gravity * 100f);
            int kBucket = Mathf.RoundToInt(springK * 10f);
            var key = (gravBucket, kBucket);

            if (!_recordedLoadsByCondition.TryGetValue(key, out HashSet<int> distinctLoads))
            {
                distinctLoads = new HashSet<int>();
                _recordedLoadsByCondition[key] = distinctLoads;
            }
            distinctLoads.Add(dp.massG);

            if (distinctLoads.Count >= 3)
            {
                _hasCompletedThreeLoadGraph = true;
            }

            if (CurrentStep != GuidedStep.Idle && CurrentStep < GuidedStep.CompareGraph)
            {
                TransitionToStep(GuidedStep.MeasurementRecorded, "measurement_recorded");
            }
            else if (CurrentStep == GuidedStep.MeasurementRecorded && _hasCompletedThreeLoadGraph)
            {
                TransitionToStep(GuidedStep.CompareGraph, "three_loads_recorded");
            }
        }

        private void HandleRecordRejectedMoving()
        {
            if (!IsGuideActive || (_teacher != null && _teacher.IsTeacherBusy)) return;
            PlaySingleNarration("record_rejected_moving", false, null, "record_rejected_moving");
        }

        private void HandleRecordRejectedEmpty()
        {
            if (!IsGuideActive || (_teacher != null && _teacher.IsTeacherBusy)) return;
            PlaySingleNarration("record_rejected_empty", false, null, "record_rejected_empty");
        }

        private void HandleGravityChanged(PlanetGravityTablet.PlanetInfo planet)
        {
            if (CurrentStep == GuidedStep.ArriveAtTable && planet.id == "earth")
            {
                _hasAcknowledgedTableEarth = true;
                PlaySingleNarration("gravity_earth", true, token =>
                {
                    if (token != _playbackToken || CurrentStep != GuidedStep.ArriveAtTable) return;
                    TransitionToStep(GuidedStep.PickupWeight, "earth_selected_at_table");
                }, "earth_selected_to_begin");
                return;
            }

            if (!IsGuideActive) return;

            // Step 8: user changes gravity with a mass attached (debounce rapid switches & discard stale planet prompts)
            bool hasAttachedMass = _springSim != null && _springSim.AttachedMassKg > 0.001f;
            if (!hasAttachedMass && CurrentStep < GuidedStep.TryDifferentGravity) return;

            if (Time.unscaledTime - _lastPlanetNarrationTime < 1.25f)
            {
                Debug.Log($"[GuidedNarration] Debounced rapid planet selection '{planet.id}'.");
                return;
            }

            string id = planet.id switch
            {
                "earth" => "gravity_earth",
                "moon" => "gravity_moon",
                "mars" => "gravity_mars",
                "jupiter" => "gravity_jupiter",
                _ => null
            };
            if (string.IsNullOrEmpty(id)) return;

            _lastPlanetNarrationTime = Time.unscaledTime;

            if (hasAttachedMass)
            {
                RecordSettledGravitySample();
            }

            // Discard outdated planet prompt cleanly via new token
            PlaySingleNarration(id, forceReplay: true, onComplete: token =>
            {
                if (token != _playbackToken) return;
                if (CurrentStep == GuidedStep.TryDifferentGravity && _hasCompletedGravityComparison)
                {
                    TransitionToStep(GuidedStep.ExploreTeacherAndBoard, "gravity_comparison_complete");
                }
            }, reason: $"gravity_changed_{planet.id}");
        }

        private void RecordSettledGravitySample()
        {
            if (_springSim == null || _springSim.AttachedMassKg <= 0.001f) return;
            int massG = Mathf.RoundToInt(_springSim.AttachedMassKg * 1000f);
            int gravBucket = Mathf.RoundToInt(_springSim.gravity * 100f);
            int kBucket = Mathf.RoundToInt(_springSim.springConstant * 10f);

            var key = (massG, kBucket);
            if (!_settledGravitiesByLoad.TryGetValue(key, out HashSet<int> gravs))
            {
                gravs = new HashSet<int>();
                _settledGravitiesByLoad[key] = gravs;
            }
            if (gravs.Add(gravBucket) && gravs.Count >= 2)
            {
                _hasCompletedGravityComparison = true;
            }
        }

        private void HandleTeacherBusyChanged() => NotifyTeacherSpeechStarting();
        private void HandleTeacherLessonEnded() => NotifyTeacherSpeechEnded();
        private void HandleExperimentReset() => StopGuide();

        public void ResetGuideProgress()
        {
            StopGuide();
            ResetGuideTrackingOnly();
        }

        private void ResetGuideTrackingOnly()
        {
            _playedThisSession.Clear();
            _playedThisStep.Clear();
            _lastPlayedTime.Clear();
            _recordedLoadsByCondition.Clear();
            _settledGravitiesByLoad.Clear();
            for (int i = 0; i < _zones.Count; i++) _zones[i].isInside = false;
            _hasCompletedThreeLoadGraph = false;
            _hasCompletedGravityComparison = false;
            _hasAcknowledgedTableEarth = false;
            _equilibriumStableTime = 0f;
            _wasExceedingElasticLimit = false;
        }

        // ---------------------------------------------------------------------
        // Core Audio Playback & Token-Protected Popup Engine
        // ---------------------------------------------------------------------

        private bool PlaySingleNarration(string id, bool forceReplay, Action<int> onComplete, string reason)
        {
            return PlayChunkedNarration(new[] { id }, forceReplay, onComplete, reason);
        }

        private bool PlayChunkedNarration(string[] ids, bool forceReplay, Action<int> onSequenceComplete, string reason)
        {
            if (ids == null || ids.Length == 0 || _catalogue == null) return false;

            if (_teacher != null && _teacher.IsTeacherBusy)
            {
                Debug.Log($"[GuidedNarration] Skipped '{ids[0]}' ({reason}) because user-initiated Teacher conversation is active.");
                return false;
            }

            if (!IsAudioServiceReady())
            {
                Debug.LogError($"[GuidedNarration] Playback FAILED for '{ids[0]}' ({reason}): Audio service not ready.");
                CancelAndHideActiveNarration("audio_service_not_ready");
                return false;
            }

            var chunksToPlay = new List<QueuedChunk>();
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                NarrationEntry entry = _catalogue.GetEntry(id);
                if (entry == null)
                {
                    Debug.LogWarning($"[GuidedNarration] Missing NarrationEntry id='{id}'.");
                    continue;
                }

                if (!forceReplay && i == 0)
                {
                    if (entry.replayRule == NarrationReplayRule.OncePerSession && _playedThisSession.Contains(id)) return false;
                    if (entry.replayRule == NarrationReplayRule.OncePerStep && _playedThisStep.Contains(id)) return false;
                    if (entry.replayRule == NarrationReplayRule.Debounced &&
                        _lastPlayedTime.TryGetValue(id, out float prevTime) &&
                        (Time.unscaledTime - prevTime) < entry.minCooldownSeconds)
                        return false;
                }

                if (_isPlayingEntry && _activeEntry != null && entry.priority < _activeEntry.priority && !forceReplay)
                {
                    return false;
                }

                AudioClip clip = _catalogue.ResolveClip(id);
                if (!GuidedNarrationCatalogue.IsPlayableClip(clip))
                {
                    Debug.LogError($"[GuidedNarration] AudioClip MISSING or unplayable for id='{id}' (expected Resources/Guide/{id}).");
                    CancelAndHideActiveNarration($"missing_clip_{id}");
                    return false;
                }

                chunksToPlay.Add(new QueuedChunk
                {
                    id = id,
                    entry = entry,
                    clip = clip
                });
            }

            if (chunksToPlay.Count == 0) return false;

            EnsureSingleActiveAudioListener(logIfAdjusted: true);
            CancelAndHideActiveNarration($"replaced_by_{chunksToPlay[0].id}");

            int myToken = ++_playbackToken;
            _onSequenceFinished = onSequenceComplete;
            _chunkQueue.Clear();
            for (int i = 1; i < chunksToPlay.Count; i++)
            {
                _chunkQueue.Enqueue(chunksToPlay[i]);
            }

            if (!StartChunkAudio(chunksToPlay[0], myToken))
            {
                ClearPlaybackStateAndHidePopup();
                return false;
            }

            return true;
        }

        private bool StartChunkAudio(QueuedChunk chunk, int token)
        {
            if (token != _playbackToken) return false;

            if (AudioListener.volume <= 0.001f) AudioListener.volume = 1.0f;

            bool started = false;
            if (_teacher != null)
            {
                started = _teacher.PlayGuidedClip(chunk.clip, VoiceVolume);
            }
            else if (_fallbackAudioSource != null)
            {
                _fallbackAudioSource.Stop();
                _fallbackAudioSource.clip = chunk.clip;
                _fallbackAudioSource.volume = VoiceVolume;
                _fallbackAudioSource.Play();
                started = _fallbackAudioSource.isPlaying;
            }

            if (!started)
            {
                Debug.LogError($"[GuidedNarration] AudioSource failed to start clip '{chunk.id}' (token={token}). Not marking as played; clearing popup.");
                return false;
            }

            _isPlayingEntry = true;
            _activeId = chunk.id;
            _activeEntry = chunk.entry;

            // Mark played ONLY after real audio playback entered isPlaying == true
            _playedThisSession.Add(chunk.id);
            _playedThisStep.Add(chunk.id);
            _lastPlayedTime[chunk.id] = Time.unscaledTime;

            Debug.Log($"[GuidedNarration] Playback STARTED: id='{chunk.id}', dur={chunk.clip.length:F2}s, token={token}.");
            return true;
        }

        private void CancelAndHideActiveNarration(string reason)
        {
            _playbackToken++;
            _chunkQueue.Clear();
            _onSequenceFinished = null;

            if (_isPlayingEntry)
            {
                Debug.Log($"[GuidedNarration] Cancelled active narration '{_activeId}' (reason={reason}).");
            }

            if (_teacher != null) _teacher.StopGuidedClip();
            if (_fallbackAudioSource != null && _fallbackAudioSource.isPlaying) _fallbackAudioSource.Stop();

            ClearPlaybackStateAndHidePopup();
        }

        private void ClearPlaybackStateAndHidePopup()
        {
            _isPlayingEntry = false;
            _activeId = "";
            _activeEntry = null;
            _onSequenceFinished = null;
        }

        private void PauseActiveAudio()
        {
            if (_teacher != null) _teacher.PauseGuidedClip();
            if (_fallbackAudioSource != null && _fallbackAudioSource.isPlaying) _fallbackAudioSource.Pause();
        }

        private void ResumeActiveAudio()
        {
            if (_teacher != null) _teacher.UnPauseGuidedClip();
            if (_fallbackAudioSource != null && _fallbackAudioSource.clip != null && !_fallbackAudioSource.isPlaying) _fallbackAudioSource.UnPause();
        }

        private bool IsCurrentAudioPlaying()
        {
            if (_teacher != null) return _teacher.IsGuidedClipPlaying();
            return _fallbackAudioSource != null && _fallbackAudioSource.isPlaying;
        }

        private AudioSource GetActiveVoiceSource()
        {
            return _teacher != null ? _teacher.VoiceSource : _fallbackAudioSource;
        }

        private bool IsAudioServiceReady()
        {
            if (_catalogue == null) return false;
            if (AudioListener.pause) return false;
            if (_teacher != null && !_teacher.IsAudioServiceReady) return false;
            if (_teacher == null && _fallbackAudioSource == null) return false;

            Camera playerCam = _xrOrigin != null && _xrOrigin.Camera != null ? _xrOrigin.Camera : Camera.main;
            return playerCam != null;
        }

        private void EnsureSingleActiveAudioListener(bool logIfAdjusted)
        {
            Camera playerCam = _xrOrigin != null && _xrOrigin.Camera != null ? _xrOrigin.Camera : Camera.main;
            AudioListener[] allListeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);

            AudioListener primary = playerCam != null ? playerCam.GetComponent<AudioListener>() : null;
            if (primary == null && playerCam != null)
            {
                primary = playerCam.gameObject.AddComponent<AudioListener>();
                if (logIfAdjusted)
                {
                    Debug.Log($"[GuidedNarration] Added AudioListener to player camera '{playerCam.name}'.");
                }
            }

            if (primary != null && !primary.enabled) primary.enabled = true;

            foreach (AudioListener listener in allListeners)
            {
                if (listener == null) continue;
                if (primary == null && listener.enabled && listener.gameObject.activeInHierarchy)
                {
                    primary = listener;
                }
                if (listener != primary && listener.enabled)
                {
                    listener.enabled = false;
                    if (logIfAdjusted)
                    {
                        Debug.LogWarning($"[GuidedNarration] Disabled duplicate AudioListener on '{listener.gameObject.name}'.");
                    }
                }
            }

            if (AudioListener.pause) AudioListener.pause = false;
        }

        private Vector3 GetTrackedPlayerPositionXZ()
        {
            if (_playerController != null) return _playerController.PlayerHeadWorldPosition;
            if (_xrOrigin != null && _xrOrigin.Camera != null) return _xrOrigin.Camera.transform.position;
            if (_xrOrigin != null) return _xrOrigin.transform.position;
            return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        }

        private bool IsEntryStillRelevant(string id)
        {
            if (id == "welcome") return CurrentStep == GuidedStep.Welcome;
            if (id == "pickup_prompt") return CurrentStep == GuidedStep.PickupWeight;
            if (id == "pickup_success") return CurrentStep == GuidedStep.WeightPickedUp;
            return true;
        }

        private bool IsTeleportAimingActive()
        {
            foreach (XRRayInteractor r in _teleportInteractors)
            {
                if (r != null && r.gameObject.activeInHierarchy) return true;
            }
            return false;
        }

        private void LogMissingClipsChecklist()
        {
            if (_catalogue == null) return;
            List<string> missing = _catalogue.GetMissingClipIds();
            if (missing.Count > 0)
            {
                Debug.LogWarning($"[GuidedNarration] Missing {missing.Count}/{_catalogue.entries.Count} AudioClip(s): {string.Join(", ", missing)}");
            }
            else
            {
                Debug.Log($"[GuidedNarration] Verified all {_catalogue.entries.Count} Guide AudioClips present and playable.");
            }
        }
    }
}

