using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using Unity.XR.CoreUtils;
using TMPro;
using HookesLawLab.Physics;
using HookesLawLab.Interaction;
using HookesLawLab.UI;
using HookesLawLab.Teacher;
using HookesLawLab.Narration;

namespace HookesLawLab.Lab
{
    /// <summary>
    /// Master bootstrap for the Hooke's Law (Masses & Springs) VR & Desktop Laboratory inside Unity.
    /// Locates all named meshes in Anam.glb, wires the 120 Hz simulation, procedural spring,
    /// calibration masses, dual extension displays, wall TV graph, tabletop console, planet gravity tablet,
    /// lecture board video player, Physics Mentor guide, lab furnishing, and VR/Desktop player rig.
    /// </summary>
    public class HookesLawLabBootstrap : MonoBehaviour
    {
        [Header("Scene References (Auto-populated by Setup)")]
        public GameObject anamModelRoot;
        public VideoClip hookesLawVideoClip;
        public TMP_FontAsset uiFontAsset;
        public GuidedNarrationCatalogue guidedNarrationCatalogue;

        public const float EntranceDoorStandOffsetM = 1.30f;

        public Vector3 EntranceSpawnPosition { get; private set; }
        public Vector3 EntranceLookAtTarget { get; private set; }

        public SpringSimulation SpringSim { get; private set; }
        public SpringVisual SpringVisual { get; private set; }
        public AttachmentManager AttachmentManager { get; private set; }
        public ExtensionDisplays ExtensionDisplays { get; private set; }
        public PlanetGravityTablet GravityTablet { get; private set; }
        public TVGraphDisplay TVGraph { get; private set; }
        public BoardVideoPlayer BoardVideo { get; private set; }
        public FeaturesBoardDisplay FeaturesBoard { get; private set; }
        public PhysicsTeacher Teacher { get; private set; }
        public LabFurnishing Furnishing { get; private set; }
        public LabPlayerController PlayerController { get; private set; }
        public GuidedExperimentManager GuideManager { get; private set; }

        private void Start()
        {
            EnsureHindiFallbackFont();
            InitializeLaboratory();
        }

        private void EnsureHindiFallbackFont()
        {
            if (uiFontAsset == null) return;
            try
            {
                Font osFont = Font.CreateDynamicFontFromOSFont(new[] { "Nirmala UI", "Mangal", "Arial Unicode MS" }, 32);
                if (osFont != null)
                {
                    TMP_FontAsset fallback = TMP_FontAsset.CreateFontAsset(osFont);
                    if (fallback != null)
                    {
                        if (uiFontAsset.fallbackFontAssetTable == null)
                            uiFontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
                        uiFontAsset.fallbackFontAssetTable.Add(fallback);
                    }
                }
            }
            catch { }
        }

        private void Update()
        {
            if (SpringSim != null && SpringVisual != null)
            {
                SpringSim.StepSimulation(Time.deltaTime);
                SpringState state = SpringSim.GetState();
                SpringVisual.SetDisplacement(state.displacementM, state);
                ExtensionDisplays?.UpdateDisplays(state.displacementM);
            }

            TVGraph?.TickUpdate();
            BoardVideo?.TickUpdate();
            if (Teacher != null && PlayerController != null)
            {
                Teacher.TickUpdate(Time.deltaTime, PlayerController.PlayerHeadWorldPosition);
            }
        }

        private void InitializeLaboratory()
        {
            if (anamModelRoot == null)
            {
                anamModelRoot = GameObject.Find("Anam");
            }
            if (anamModelRoot == null)
            {
                Debug.LogError("[HookesLawLab] Could not find 'Anam' model root in the scene!");
                return;
            }

            // 1. Inspect named meshes inside Anam.glb
            Transform springTopEye = null;
            Transform orangePointer = null;
            GameObject rulerScaleObj = null;
            GameObject readerValueObj = null;
            GameObject meterObj = null;
            GameObject benchMainBody = null;
            GameObject weightStorageMatObj = null;
            GameObject wallTvScreenObj = null;
            GameObject boardScreenObj = null;
            Transform teacherLeftEye = null;
            Transform teacherRightEye = null;
            Transform teacherHead = null;

            var teacherParts = new List<Transform>();

            foreach (Transform t in anamModelRoot.GetComponentsInChildren<Transform>(true))
            {
                string rawName = t.gameObject.name;
                string lower = rawName.ToLowerInvariant();
                string clean = lower.Replace('_', ' ');

                // Hide static non-interactive GLB spring, pre-hung weight, and fractured GLB calibration mass trios
                if (clean.Contains("helical experiment spring") ||
                    clean.Contains("spring lower hook") ||
                    clean.Contains("spring lower stem") ||
                    clean.Contains("suspended experiment weight") ||
                    clean.Contains("monitor heading") ||
                    clean.Contains("monitor metrics") ||
                    clean.Contains("calibration mass") ||
                    clean.StartsWith("mass handle") ||
                    clean.StartsWith("mass marking"))
                {
                    Renderer r = t.GetComponent<Renderer>();
                    if (r != null) r.enabled = false;
                    Collider existingCol = t.GetComponent<Collider>();
                    if (existingCol != null) existingCol.enabled = false;
                }

                if (clean.Contains("spring top eye")) springTopEye = t;
                if (clean.Contains("orange measurement pointer")) orangePointer = t;
                if (clean.Contains("stand rod centimetre scale")) rulerScaleObj = t.gameObject;
                if (clean == "reader value") readerValueObj = t.gameObject;
                if (clean == "meter") meterObj = t.gameObject;
                if (clean == "bench main body") benchMainBody = t.gameObject;
                if (clean == "weight storage mat") weightStorageMatObj = t.gameObject;

                if (clean.Contains("wall monitor screen") && (clean.Contains("001") || lower.Contains(".001")))
                {
                    wallTvScreenObj = t.gameObject;
                }
                else if (clean.StartsWith("wall monitor screen") && !clean.Contains("001"))
                {
                    boardScreenObj = t.gameObject;
                }

                if (lower.Contains("eyeleft")) teacherLeftEye = t;
                if (lower.Contains("eyeright")) teacherRightEye = t;
                if (lower.Contains("wolf3d_head")) teacherHead = t;

                if (lower.Contains("wolf3d") || lower.Contains("eyeleft") || lower.Contains("eyeright") ||
                    lower.Contains("eye_catchlight") || lower.Contains("iris_") || lower.Contains("pupil") ||
                    lower.Contains("writing_pad") || lower.Contains("pen_on_pad") || lower.Contains("paper_rule"))
                {
                    teacherParts.Add(t);
                }
            }

            // Determine glTF->Unity X-mirror sign from Spring top eye (Three.js: -0.390, Unity glTFast: +0.390)
            Vector3 topAnchorPos = springTopEye != null
                ? GetRendererCenter(springTopEye.gameObject, new Vector3(0.390f, 3.226f, -0.240f))
                : new Vector3(0.390f, 3.226f, -0.240f);
            float xSign = topAnchorPos.x >= 0f ? -1f : 1f; // -1 if glTFast negated X, +1 if not

            Vector3 ToUnityPos(float threeX, float threeY, float threeZ) => new Vector3(threeX * xSign, threeY, threeZ);

            // Compute experiment center & shared entrance spawn from actual doorway geometry
            TryComputeEntranceSpawn(anamModelRoot, out Vector3 spawnPos, out Vector3 expCenter);
            EntranceSpawnPosition = spawnPos;
            EntranceLookAtTarget = expCenter;

            // Preserve workbench-centric viewing reference for tabletop displays/tablet/console alignment
            Vector3 benchRefOrigin = new Vector3(expCenter.x, 0f, expCenter.z + 1.95f);
            Vector3 viewerRefPos = benchRefOrigin + Vector3.up * 1.65f;

            // 2. Spring Simulation
            SpringSim = gameObject.AddComponent<SpringSimulation>();
            SpringSim.springConstant = 25.0f;
            SpringSim.gravity = 9.81f;
            SpringSim.dampingRatio = 0.40f;
            SpringSim.naturalLength = 0.672f;
            SpringSim.elasticLimit = 0.48f;

            // 3. Procedural Helical Spring Visual & Hanger
            GameObject springVisualGo = new GameObject("SpringVisual");
            springVisualGo.transform.SetParent(transform, false);
            SpringVisual = springVisualGo.AddComponent<SpringVisual>();
            SpringVisual.Initialize(topAnchorPos, orangePointer, rulerScaleObj, uiFontAsset);

            // 4. Unified Calibration Masses (50g..300g ascending, flush on Weight storage mat) & Attachment Manager
            GameObject attachGo = new GameObject("AttachmentManager");
            attachGo.transform.SetParent(transform, false);
            AttachmentManager = attachGo.AddComponent<AttachmentManager>();

            float matTopY = 1.130f;
            float matCenterX = -0.35f * xSign;
            float matCenterZ = 0.39f;
            float matWidthX = 1.40f;
            if (weightStorageMatObj != null)
            {
                Renderer matRend = weightStorageMatObj.GetComponent<Renderer>();
                if (matRend != null)
                {
                    matTopY = matRend.bounds.max.y;
                    matCenterX = matRend.bounds.center.x;
                    matCenterZ = matRend.bounds.center.z;
                    matWidthX = matRend.bounds.size.x;
                }

                // Add solid physics collider on Weight storage mat (on Ignore Raycast so it never blocks UI rays)
                int ignoreLayer = LayerMask.NameToLayer("Ignore Raycast");
                weightStorageMatObj.layer = ignoreLayer >= 0 ? ignoreLayer : 2;
                if (weightStorageMatObj.GetComponent<Collider>() == null)
                {
                    weightStorageMatObj.AddComponent<BoxCollider>();
                }
            }

            // Place 6 weights in strict ascending order left-to-right from standing player's perspective (+X -> -X when facing -Z)
            int[] orderedGrams = { 50, 100, 150, 200, 250, 300 };
            float span = Mathf.Min(1.15f, matWidthX - 0.22f);
            float startX = matCenterX - xSign * (span * 0.5f); // Left side from viewer facing -Z
            float stepX = (span / (orderedGrams.Length - 1)) * xSign;

            var interactables = new List<MassInteractable>();
            for (int i = 0; i < orderedGrams.Length; i++)
            {
                int grams = orderedGrams[i];
                Vector3 trayBottomPos = new Vector3(startX + stepX * i, matTopY, matCenterZ);

                GameObject weightGo = new GameObject($"CalibrationMass_{grams}g");
                weightGo.transform.SetParent(transform, false);

                MassInteractable mi = weightGo.AddComponent<MassInteractable>();
                mi.BuildUnifiedWeight(grams, AttachmentManager, trayBottomPos, Quaternion.identity, matTopY, uiFontAsset);
                interactables.Add(mi);
            }

            AttachmentManager.Initialize(SpringSim, SpringVisual, interactables);

            // 5. Dual Extension Displays (Display 1 & Display 2)
            GameObject extGo = new GameObject("ExtensionDisplays");
            extGo.transform.SetParent(transform, false);
            ExtensionDisplays = extGo.AddComponent<ExtensionDisplays>();
            ExtensionDisplays.Initialize(SpringSim, SpringVisual, readerValueObj, meterObj, viewerRefPos, uiFontAsset);
            AttachmentManager.OnMassChanged += () => ExtensionDisplays.UpdateDisplays();

            // 6. Planet Gravity Selector Tablet
            GameObject tabletGo = new GameObject("PlanetGravityTablet");
            tabletGo.transform.SetParent(transform, false);
            GravityTablet = tabletGo.AddComponent<PlanetGravityTablet>();
            Vector3 tabletPos = ToUnityPos(0.95f, 1.44f, -0.22f);
            Quaternion tabletRot = Quaternion.LookRotation((tabletPos - viewerRefPos).normalized, Vector3.up);
            GravityTablet.Initialize(SpringSim, tabletPos, tabletRot, uiFontAsset);

            // 7. Wall TV Graph Display & Tabletop Control Console
            GameObject tvGo = new GameObject("TVGraphDisplay");
            tvGo.transform.SetParent(transform, false);
            TVGraph = tvGo.AddComponent<TVGraphDisplay>();
            // Place flush on Upper worktop (Y=1.115m), forward (+Z=0.58m) so it clears Large slotted weight 0 (Z=0.26) & Calibration mass 300g (X=-0.24)
            Vector3 consolePos = ToUnityPos(0.60f, 1.115f, 0.58f);
            Vector3 consoleLookDir = (consolePos - benchRefOrigin);
            consoleLookDir.y = 0f;
            Quaternion consoleRot = consoleLookDir.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(consoleLookDir.normalized, Vector3.up)
                : Quaternion.identity;
            TVGraph.Initialize(wallTvScreenObj, SpringSim, AttachmentManager, ExtensionDisplays, GravityTablet,
                consolePos, consoleRot, viewerRefPos, uiFontAsset);

            // 8. Board Video Player & Wall Features Board
            GameObject boardGo = new GameObject("BoardVideoPlayer");
            boardGo.transform.SetParent(transform, false);
            BoardVideo = boardGo.AddComponent<BoardVideoPlayer>();
            BoardVideo.Initialize(boardScreenObj, hookesLawVideoClip, viewerRefPos, uiFontAsset);

            GameObject featuresGo = new GameObject("FeaturesBoardDisplay");
            featuresGo.transform.SetParent(transform, false);
            FeaturesBoard = featuresGo.AddComponent<FeaturesBoardDisplay>();
            FeaturesBoard.Initialize(anamModelRoot, boardScreenObj, viewerRefPos, uiFontAsset);

            // 9. Prof. Anam AI Physics Teacher
            Vector3 teacherPos = ToUnityPos(-3.3186f, 0f, 0.005f);
            GameObject teacherRigGo = new GameObject("PhysicsTeacherRig");
            teacherRigGo.transform.SetParent(transform, false);
            teacherRigGo.transform.position = teacherPos;
            foreach (Transform tp in teacherParts)
            {
                if (tp != null) tp.SetParent(teacherRigGo.transform, true);
            }

            GameObject teacherCompGo = new GameObject("PhysicsTeacher");
            teacherCompGo.transform.SetParent(transform, false);
            Teacher = teacherCompGo.AddComponent<PhysicsTeacher>();
            Vector3 dialogPos = ToUnityPos(-2.70f, 1.58f, 0.22f);
            Teacher.Initialize(teacherRigGo.transform, dialogPos, SpringSim, uiFontAsset);

            // 10. Lab Furnishing, Lighting, Collisions, and Teleportation Area
            GameObject furnGo = new GameObject("LabFurnishing");
            furnGo.transform.SetParent(transform, false);
            Furnishing = furnGo.AddComponent<LabFurnishing>();
            Furnishing.BuildEnvironmentAndCollisions(anamModelRoot, xSign);

            // Measure Teacher's actual eye level in world space as the primary VR height reference
            float teacherEyeHeight = 1.72755f;
            if (teacherLeftEye != null && teacherLeftEye.GetComponent<Renderer>() != null)
            {
                teacherEyeHeight = teacherLeftEye.GetComponent<Renderer>().bounds.center.y;
            }
            else if (teacherRightEye != null && teacherRightEye.GetComponent<Renderer>() != null)
            {
                teacherEyeHeight = teacherRightEye.GetComponent<Renderer>().bounds.center.y;
            }
            else if (teacherHead != null && teacherHead.GetComponent<Renderer>() != null)
            {
                Bounds hb = teacherHead.GetComponent<Renderer>().bounds;
                teacherEyeHeight = hb.min.y + hb.size.y * 0.66f;
            }
            teacherEyeHeight = Mathf.Clamp(teacherEyeHeight, 1.60f, 1.76f);

            // 11. Unified XR & Desktop Player Controller (match BOTH Desktop & VR User POV to Teacher eye level)
            XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
            GameObject playerCtrlGo = new GameObject("LabPlayerController");
            playerCtrlGo.transform.SetParent(transform, false);
            PlayerController = playerCtrlGo.AddComponent<LabPlayerController>();
            PlayerController.eyeHeight = teacherEyeHeight;
            PlayerController.vrTargetEyeHeight = teacherEyeHeight;
            PlayerController.Initialize(xrOrigin, AttachmentManager, spawnPos, expCenter, uiFontAsset);

            // 12. Guided Voice Narration & Experiment Progression Manager
            GameObject guideGo = new GameObject("GuidedExperimentManager");
            guideGo.transform.SetParent(transform, false);
            GuideManager = guideGo.AddComponent<GuidedExperimentManager>();
            Vector3 boardsFocusWorld = FeaturesBoard != null && FeaturesBoard.BoardHost != null
                ? Vector3.Lerp(
                    boardScreenObj != null ? boardScreenObj.transform.position : FeaturesBoard.BoardHost.transform.position,
                    FeaturesBoard.BoardHost.transform.position,
                    0.5f)
                : (boardScreenObj != null ? boardScreenObj.transform.position : ToUnityPos(-5.8f, 1.8f, 3.0f));
            GuideManager.Initialize(
                guidedNarrationCatalogue,
                SpringSim,
                AttachmentManager,
                TVGraph,
                GravityTablet,
                Teacher,
                PlayerController,
                expCenter,
                uiFontAsset,
                teacherPos,
                boardsFocusWorld,
                tabletPos);
            TVGraph.SetGuideManager(GuideManager);

            Debug.Log($"[HookesLawLab] Full Hooke's Law Laboratory initialized! EntranceSpawn={spawnPos:F2}, LookAt={expCenter:F2}");
        }

        /// <summary>
        /// Computes the shared laboratory entrance spawn point (~1.3m inside the doorway, clear of walls/furniture)
        /// and look-at target (main spring experiment table) directly from the scene's actual doorway and bench geometry.
        /// </summary>
        public static bool TryComputeEntranceSpawn(GameObject anamRoot, out Vector3 spawnPos, out Vector3 lookAtTarget)
        {
            lookAtTarget = new Vector3(0f, 1.40f, 0f);
            spawnPos = new Vector3(0f, 0f, 6.345f);
            if (anamRoot == null) return false;

            Transform benchMainBody = null;
            Transform doorFrame = null;
            Transform doorLeftLeaf = null;
            Transform doorRightLeaf = null;
            Transform frontWall = null;

            foreach (Transform t in anamRoot.GetComponentsInChildren<Transform>(true))
            {
                string clean = t.gameObject.name.ToLowerInvariant().Replace('_', ' ');
                if (clean == "bench main body") benchMainBody = t;
                else if (clean == "closed door left leaf") doorLeftLeaf = t;
                else if (clean == "closed door right leaf") doorRightLeaf = t;
                else if (clean == "closed door frame") doorFrame = t;
                else if (clean == "vr front wall") frontWall = t;
            }

            if (benchMainBody != null)
            {
                Vector3 bc = GetRendererCenter(benchMainBody.gameObject, benchMainBody.position);
                lookAtTarget = new Vector3(bc.x, 1.40f, bc.z);
            }

            bool hasDoorBounds = false;
            Bounds doorBounds = default;

            void EncapsulateRenderer(Transform tr)
            {
                if (tr == null) return;
                Renderer r = tr.GetComponent<Renderer>();
                if (r == null) return;
                if (!hasDoorBounds)
                {
                    doorBounds = r.bounds;
                    hasDoorBounds = true;
                }
                else
                {
                    doorBounds.Encapsulate(r.bounds);
                }
            }

            // Prefer the actual closed door leaves (inner face at Z ≈ 7.645m), fall back to door frame or front wall
            EncapsulateRenderer(doorLeftLeaf);
            EncapsulateRenderer(doorRightLeaf);
            if (!hasDoorBounds) EncapsulateRenderer(doorFrame);
            if (!hasDoorBounds) EncapsulateRenderer(frontWall);

            if (!hasDoorBounds)
            {
                Transform fallbackDoor = doorLeftLeaf ?? doorRightLeaf ?? doorFrame;
                if (fallbackDoor == null) return false;
                Vector3 fallbackToTable = new Vector3(lookAtTarget.x - fallbackDoor.position.x, 0f, lookAtTarget.z - fallbackDoor.position.z);
                Vector3 fallbackInward = fallbackToTable.sqrMagnitude > 0.01f ? fallbackToTable.normalized : Vector3.back;
                spawnPos = new Vector3(fallbackDoor.position.x, 0f, fallbackDoor.position.z) + fallbackInward * EntranceDoorStandOffsetM;
                spawnPos.y = 0f;
                return true;
            }

            Vector3 doorCenterXZ = new Vector3(doorBounds.center.x, 0f, doorBounds.center.z);
            Vector3 toTableXZ = new Vector3(lookAtTarget.x - doorCenterXZ.x, 0f, lookAtTarget.z - doorCenterXZ.z);
            Vector3 inwardDir = toTableXZ.sqrMagnitude > 0.01f ? toTableXZ.normalized : Vector3.back;

            float halfExtentAlongInward =
                Mathf.Abs(inwardDir.x) * doorBounds.extents.x +
                Mathf.Abs(inwardDir.z) * doorBounds.extents.z;

            Vector3 doorInnerSurfaceXZ = doorCenterXZ + inwardDir * halfExtentAlongInward;
            spawnPos = doorInnerSurfaceXZ + inwardDir * EntranceDoorStandOffsetM;
            spawnPos.y = 0f;
            return true;
        }

        private static Vector3 GetRendererCenter(GameObject go, Vector3 fallback)
        {
            Renderer r = go.GetComponent<Renderer>();
            return r != null ? r.bounds.center : fallback;
        }
    }
}
