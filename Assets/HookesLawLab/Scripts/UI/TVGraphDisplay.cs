using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using HookesLawLab.Physics;
using HookesLawLab.Interaction;
using HookesLawLab.Lab;
using HookesLawLab.Narration;

namespace HookesLawLab.UI
{
    /// <summary>
    /// Interactive Force vs. Displacement TV Graph Display mounted on 'Wall monitor screen.001',
    /// paired with the upright, ergonomic 3D Tabletop Control Console on the workbench.
    /// </summary>
    public class TVGraphDisplay : MonoBehaviour
    {
        [Serializable]
        public struct DataPoint
        {
            public int massG;
            public float massKg;
            public float forceN;
            public float displacementM;
            public float k;
        }

        public float nominalK = 25.0f;
        public List<DataPoint> Points { get; } = new List<DataPoint>();
        public float? FittedSlope { get; private set; }
        public float? R2 { get; private set; }

        private SpringSimulation _springSim;
        private AttachmentManager _attachmentManager;
        private ExtensionDisplays _extensionDisplays;
        private PlanetGravityTablet _gravityTablet;
        private AudioSource _audioSource;

        private RawImage _plotImage;
        private Texture2D _plotTexture;
        private const int PlotTexW = 1170;
        private const int PlotTexH = 830;

        private TMP_Text _statusBadgeText;
        private Image _statusBadgeDot;
        private Outline _statusBadgeOutline;
        private TMP_Text _emptyPromptText;
        private TMP_Text _slopeBadgeText;
        private RectTransform _slopeBadgeRt;
        private TMP_Text _calcKText;
        private TMP_Text _r2Text;
        private TMP_Text _liveTelemetryText;
        private TMP_Text _logHeaderText;
        private TMP_Text _logBodyText;

        private TMP_Text _consoleStatusTitle;
        private TMP_Text _consoleStatusSub;
        private float _lastOscillatingRenderTime;

        // Guided Narration UI & Events
        private GuidedExperimentManager _guideManager;
        private TMP_Text _guideStartBtnLabel;
        private TMP_Text _guidePauseBtnLabel;
        private TMP_Text _guideVolText;

        public event Action<DataPoint, float, float> OnMeasurementRecorded;
        public event Action OnRecordRejectedMoving;
        public event Action OnRecordRejectedEmpty;
        public event Action OnExperimentReset;

        public void SetGuideManager(GuidedExperimentManager guide)
        {
            if (_guideManager != null)
            {
                _guideManager.OnGuideUIStateChanged -= RefreshGuideControls;
            }
            _guideManager = guide;
            if (_guideManager != null)
            {
                _guideManager.OnGuideUIStateChanged += RefreshGuideControls;
            }
            RefreshGuideControls();
        }

        public void Initialize(
            GameObject tvScreenMeshObj,
            SpringSimulation sim,
            AttachmentManager attachmentMgr,
            ExtensionDisplays extDisplays,
            PlanetGravityTablet gravityTablet,
            Vector3 tabletopConsolePos,
            Quaternion tabletopConsoleRot,
            Vector3 viewerRefPos,
            TMP_FontAsset fontAsset = null)
        {
            _springSim = sim;
            _attachmentManager = attachmentMgr;
            _extensionDisplays = extDisplays;
            _gravityTablet = gravityTablet;

            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;

            if (tvScreenMeshObj != null)
            {
                BuildTVScreenUI(tvScreenMeshObj, viewerRefPos, fontAsset);
            }

            BuildTabletopConsole(tabletopConsolePos, tabletopConsoleRot, fontAsset);

            if (_attachmentManager != null)
            {
                _attachmentManager.OnMassChanged += RefreshUI;
            }
            if (_gravityTablet != null)
            {
                _gravityTablet.OnGravityChanged += _ => RefreshUI();
            }

            RefreshUI();
        }

        private void BuildTVScreenUI(GameObject tvScreenObj, Vector3 viewerRefPos, TMP_FontAsset fontAsset)
        {
            MeshFilter mf = tvScreenObj.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;

            LabUIHelper.ComputeScreenOverlayPose(
                tvScreenObj.transform,
                mesh,
                viewerRefPos,
                out Vector3 localCenter,
                out Quaternion localRot,
                out float w,
                out float h);

            const int W = 2048;
            const int H = 1148;

            Canvas canvas = LabUIHelper.CreateWorldCanvas(
                "TVGraphCanvas",
                tvScreenObj.transform,
                localCenter,
                localRot,
                Mathf.Max(1.0f, w),
                Mathf.Max(0.5f, h),
                W,
                H,
                interactive: true);

            RectTransform root = LabUIHelper.CreateStretchPanel(
                "TVRoot",
                canvas.transform,
                new Color(0.024f, 0.051f, 0.098f, 1f),
                LabUIHelper.SubtleCyanBorder,
                4f);

            // Header
            LabUIHelper.CreatePanel("Header", root, new Vector2(6f, 6f), new Vector2(W - 12f, 110f),
                new Color(0.035f, 0.082f, 0.157f, 1f), new Color(0.031f, 0.569f, 0.698f, 0.45f), 2f);

            LabUIHelper.CreateText("Title", root, "FORCE VS. DISPLACEMENT",
                new Vector2(42f, 20f), new Vector2(900f, 52f), 46f, Color.white,
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            LabUIHelper.CreateText("Formula", root, "HOOKE'S LAW:  F_applied = mg = k · x   |   k = F / x",
                new Vector2(42f, 74f), new Vector2(1050f, 36f), 24f, new Color(0.22f, 0.74f, 0.97f),
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            // Live status badge
            RectTransform badge = LabUIHelper.CreatePanel("StatusBadge", root, new Vector2(W - 390f, 30f), new Vector2(350f, 54f),
                new Color(0.059f, 0.09f, 0.165f, 0.92f), new Color(0.063f, 0.725f, 0.506f, 1f), 2f);
            _statusBadgeOutline = badge.GetComponent<Outline>();

            RectTransform dot = LabUIHelper.CreatePanel("Dot", badge, new Vector2(16f, 19f), new Vector2(16f, 16f),
                new Color(0.063f, 0.725f, 0.506f, 1f));
            _statusBadgeDot = dot.GetComponent<Image>();

            _statusBadgeText = LabUIHelper.CreateText("StatusTxt", badge, "STATIC EQUILIBRIUM",
                new Vector2(44f, 13f), new Vector2(295f, 30f), 20f, Color.white,
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            // Left Plot Viewport
            RectTransform plotCard = LabUIHelper.CreatePanel("PlotCard", root, new Vector2(120f, 140f), new Vector2(PlotTexW + 40f, PlotTexH + 90f),
                new Color(0.012f, 0.031f, 0.071f, 1f), new Color(0.118f, 0.161f, 0.231f, 1f), 2f);

            GameObject plotGo = new GameObject("PlotRawImage");
            plotGo.transform.SetParent(plotCard, false);
            RectTransform prt = plotGo.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 1f);
            prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            prt.anchoredPosition = new Vector2(20f, -20f);
            prt.sizeDelta = new Vector2(PlotTexW, PlotTexH);
            _plotImage = plotGo.AddComponent<RawImage>();
            _plotImage.raycastTarget = false;
            _plotTexture = new Texture2D(PlotTexW, PlotTexH, TextureFormat.RGBA32, false);
            _plotImage.texture = _plotTexture;

            LabUIHelper.CreateText("XAxisTitle", plotCard, "Extension, x (m)  [0 -> max]",
                new Vector2(20f, PlotTexH + 34f), new Vector2(PlotTexW, 38f), 26f, new Color(0.92f, 0.94f, 0.96f),
                TextAlignmentOptions.Center, FontStyles.Bold, fontAsset);

            _emptyPromptText = LabUIHelper.CreateText("EmptyPrompt", plotCard,
                "Add a mass and record a measurement\n<size=22><color=#cbd5e1>Drag a calibration weight to the spring hanger, then press [RECORD POINT].</color></size>",
                new Vector2(180f, PlotTexH * 0.42f), new Vector2(850f, 130f), 30f, new Color(0.22f, 0.74f, 0.97f),
                TextAlignmentOptions.Center, FontStyles.Bold, fontAsset);

            _slopeBadgeRt = LabUIHelper.CreatePanel("SlopeBadge", plotCard, new Vector2(450f, 195f), new Vector2(280f, 42f),
                new Color(0.012f, 0.031f, 0.071f, 0.92f), LabUIHelper.SubtleCyanBorder, 2f);
            _slopeBadgeText = LabUIHelper.CreateText("SlopeTxt", _slopeBadgeRt, "Slope k = --",
                new Vector2(6f, 7f), new Vector2(268f, 28f), 20f, new Color(0.22f, 0.74f, 0.97f),
                TextAlignmentOptions.Center, FontStyles.Bold, fontAsset);
            _slopeBadgeRt.gameObject.SetActive(false);

            TMP_Text yTitle = LabUIHelper.CreateText("YAxisTitle", root, "Applied Force, F (N)",
                new Vector2(18f, 520f), new Vector2(400f, 40f), 26f, new Color(0.92f, 0.94f, 0.96f),
                TextAlignmentOptions.Center, FontStyles.Bold, fontAsset);
            yTitle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            const float sideX = 1380f;
            const float sideW = 610f;

            // Card 1: Calculated k
            RectTransform kCard = LabUIHelper.CreatePanel("SpringKCard", root, new Vector2(sideX, 140f), new Vector2(sideW, 205f),
                new Color(0.031f, 0.078f, 0.149f, 1f), LabUIHelper.SubtleCyanBorder, 2f);
            LabUIHelper.CreateText("KTitle", kCard, "CALCULATED SPRING CONSTANT", new Vector2(24f, 18f), new Vector2(sideW - 48f, 28f),
                20f, new Color(0.7f, 0.78f, 0.86f), TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);
            _calcKText = LabUIHelper.CreateText("KVal", kCard, "-- N/m", new Vector2(24f, 52f), new Vector2(sideW - 48f, 68f),
                56f, new Color(0.22f, 0.85f, 0.98f), TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);
            _r2Text = LabUIHelper.CreateText("R2Val", kCard, $"Goodness of Fit R²: --\nNominal Spring k: {nominalK:F1} N/m",
                new Vector2(24f, 128f), new Vector2(sideW - 48f, 64f), 21f, Color.white,
                TextAlignmentOptions.Left, FontStyles.Normal, fontAsset);

            // Card 2: Live Telemetry
            RectTransform liveCard = LabUIHelper.CreatePanel("LiveCard", root, new Vector2(sideX, 365f), new Vector2(sideW, 260f),
                new Color(0.031f, 0.071f, 0.125f, 1f), new Color(0.118f, 0.161f, 0.231f, 1f), 2f);
            LabUIHelper.CreateText("LiveTitle", liveCard, "LIVE APPARATUS TELEMETRY", new Vector2(24f, 18f), new Vector2(sideW - 48f, 28f),
                20f, new Color(0.22f, 0.74f, 0.97f), TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);
            _liveTelemetryText = LabUIHelper.CreateText("LiveRows", liveCard, "", new Vector2(24f, 56f), new Vector2(sideW - 48f, 190f),
                21f, Color.white, TextAlignmentOptions.Left, FontStyles.Normal, fontAsset);

            // Card 3: Experimental Log
            RectTransform logCard = LabUIHelper.CreatePanel("LogCard", root, new Vector2(sideX, 645f), new Vector2(sideW, 275f),
                new Color(0.031f, 0.071f, 0.125f, 1f), new Color(0.118f, 0.161f, 0.231f, 1f), 2f);
            _logHeaderText = LabUIHelper.CreateText("LogTitle", logCard, "EXPERIMENTAL LOG (0 OBSERVATIONS)",
                new Vector2(24f, 18f), new Vector2(sideW - 48f, 28f), 20f, new Color(0.7f, 0.78f, 0.86f),
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);
            _logBodyText = LabUIHelper.CreateText("LogBody", logCard, "No observations recorded yet.",
                new Vector2(24f, 56f), new Vector2(sideW - 48f, 205f), 19f, new Color(0.9f, 0.93f, 0.96f),
                TextAlignmentOptions.Left, FontStyles.Normal, fontAsset);

            // Interactive On-Screen Buttons
            LabUIHelper.CreateButton("BtnRecord", root, "● RECORD MEASUREMENT", new Vector2(sideX, 940f), new Vector2(600f, 56f),
                new Color(0.02f, 0.588f, 0.412f), new Color(0.063f, 0.725f, 0.506f), new Color(0.204f, 0.827f, 0.6f),
                24f, () => RecordMeasurement(), out _, fontAsset);

            LabUIHelper.CreateButton("BtnDetach", root, "▲ DETACH", new Vector2(sideX, 1008f), new Vector2(190f, 52f),
                new Color(0.6f, 0.106f, 0.106f), new Color(0.863f, 0.149f, 0.149f), new Color(0.937f, 0.267f, 0.267f),
                19f, () => _attachmentManager?.DetachLowestMass(), out _, fontAsset);

            LabUIHelper.CreateButton("BtnClear", root, "✕ CLEAR", new Vector2(sideX + 205f, 1008f), new Vector2(190f, 52f),
                new Color(0.573f, 0.251f, 0.055f), new Color(0.706f, 0.325f, 0.035f), new Color(0.961f, 0.62f, 0.043f),
                19f, () => ClearGraph(), out _, fontAsset);

            LabUIHelper.CreateButton("BtnReset", root, "↺ RESET", new Vector2(sideX + 410f, 1008f), new Vector2(190f, 52f),
                new Color(0.118f, 0.251f, 0.686f), new Color(0.145f, 0.388f, 0.922f), new Color(0.231f, 0.51f, 0.965f),
                19f, () => ResetExperiment(), out _, fontAsset);
        }

        /// <summary>
        /// Builds the upright, forward-facing, 32°-tilted Tabletop Control Console with zero clipping,
        /// thin non-blocking border, 30% larger buttons, equal spacing, and 2048x1024 high-DPI Canvas.
        /// </summary>
        private void BuildTabletopConsole(Vector3 worldPos, Quaternion worldRot, TMP_FontAsset fontAsset)
        {
            GameObject console = new GameObject("TabletopGraphConsole");
            console.transform.SetParent(transform, false);
            console.transform.position = worldPos; // Sits flush on Upper worktop (Y = 1.115m)
            console.transform.rotation = worldRot; // +Z points away from user, -Z faces user

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Material chassisMat = new Material(litShader) { color = new Color(0.047f, 0.075f, 0.137f) };
            chassisMat.SetFloat("_Metallic", 0.78f);
            chassisMat.SetFloat("_Smoothness", 0.55f);

            Material rimMat = new Material(litShader) { color = new Color(0.031f, 0.569f, 0.698f) };
            rimMat.SetFloat("_Metallic", 0.4f);
            rimMat.SetFloat("_Smoothness", 0.6f);
            rimMat.EnableKeyword("_EMISSION");
            rimMat.SetColor("_EmissionColor", new Color(0.01f, 0.22f, 0.28f));

            const float panelW = 0.56f;
            const float panelH = 0.30f; // 1.87:1 ratio matching 2048x1024 UI
            const float tiltAngleDeg = 58f; // +58° around +X leans top back so UI faces 32° upward toward standing user

            // Low-profile flat footprint base strictly underneath the angled panel
            GameObject baseFoot = CreateDecorativeCube("ConsoleFoot", console.transform,
                new Vector3(0f, 0.008f, 0.03f), new Vector3(panelW * 0.90f, 0.016f, 0.18f), Quaternion.identity, chassisMat);

            // Rear support strut behind the tilted panel
            CreateDecorativeCube("ConsoleRearSupport", console.transform,
                new Vector3(0f, 0.065f, 0.075f), new Vector3(panelW * 0.72f, 0.11f, 0.045f), Quaternion.identity, chassisMat);

            // Angled Faceplate pivot raised so bottom edge rests cleanly at Y = +0.020m above tabletop
            GameObject faceplate = new GameObject("Faceplate");
            faceplate.transform.SetParent(console.transform, false);
            faceplate.transform.localPosition = new Vector3(0f, 0.105f, 0f);
            faceplate.transform.localRotation = Quaternion.Euler(tiltAngleDeg, 0f, 0f);

            // Backing plate strictly BEHIND the UI (+Z > 0 in faceplate local space)
            CreateDecorativeCube("FaceplateBacking", faceplate.transform,
                new Vector3(0f, 0f, 0.010f), new Vector3(panelW + 0.016f, panelH + 0.016f, 0.014f), Quaternion.identity, chassisMat);

            // Thin slim edge trim strictly BEHIND UI surface (4mm thick perimeter lip, zero overlap over Canvas)
            CreateDecorativeCube("FaceplateSlimRim", faceplate.transform,
                new Vector3(0f, 0f, 0.006f), new Vector3(panelW + 0.020f, panelH + 0.020f, 0.005f), Quaternion.identity, rimMat);

            // High-DPI 2048x1024 World-Space Canvas placed 8mm IN FRONT of backing plate (-Z faces user)
            const int W = 2048;
            const int H = 1024;
            Canvas c = LabUIHelper.CreateWorldCanvas(
                "TabletopConsoleCanvas",
                faceplate.transform,
                new Vector3(0f, 0f, -0.008f),
                Quaternion.identity,
                panelW,
                panelH,
                W,
                H,
                interactive: true);

            RectTransform root = LabUIHelper.CreateStretchPanel("ConsoleRoot", c.transform,
                new Color(0.031f, 0.059f, 0.118f, 0.99f), LabUIHelper.SubtleCyanBorder, 4f);

            // Top Status Header Bar
            RectTransform statusBar = LabUIHelper.CreatePanel("StatusBar", root, new Vector2(36f, 26f), new Vector2(W - 72f, 160f),
                new Color(0.02f, 0.051f, 0.106f, 1f), LabUIHelper.SubtleCyanBorder, 3f);

            _consoleStatusTitle = LabUIHelper.CreateText("StatusTitle", statusBar, "HOOKE'S LAW LAB CONTROLS",
                new Vector2(20f, 16f), new Vector2(W - 112f, 64f), 44f, Color.white,
                TextAlignmentOptions.Center, FontStyles.Bold, fontAsset);

            _consoleStatusSub = LabUIHelper.CreateText("StatusSub", statusBar, "SELECT A MASS BELOW OR DRAG WEIGHT TO HANGER",
                new Vector2(20f, 92f), new Vector2(W - 112f, 48f), 30f, new Color(0.75f, 0.85f, 0.95f),
                TextAlignmentOptions.Center, FontStyles.Bold, fontAsset);

            // Row 1: 5 Main Action Buttons (including Settle Spring, equal spacing)
            const float padX = 36f;
            const float gapX = 22f;
            const float row1Y = 216f;
            const float row1H = 365f;
            float actBtnW = (W - padX * 2f - gapX * 4f) / 5f; // ~377px each

            LabUIHelper.CreateButton("Btn_Record", root, "RECORD\nPOINT",
                new Vector2(padX, row1Y), new Vector2(actBtnW, row1H),
                new Color(0.02f, 0.56f, 0.39f), new Color(0.05f, 0.70f, 0.49f), new Color(0.204f, 0.827f, 0.6f),
                38f, () => RecordMeasurement(), out _, fontAsset);

            LabUIHelper.CreateButton("Btn_Settle", root, "SETTLE\nSPRING",
                new Vector2(padX + (actBtnW + gapX) * 1f, row1Y), new Vector2(actBtnW, row1H),
                new Color(0.012f, 0.412f, 0.631f), new Color(0.055f, 0.58f, 0.84f), new Color(0.22f, 0.74f, 0.97f),
                38f, () => SettleSpringNow(), out _, fontAsset);

            LabUIHelper.CreateButton("Btn_Detach", root, "DETACH\nMASS",
                new Vector2(padX + (actBtnW + gapX) * 2f, row1Y), new Vector2(actBtnW, row1H),
                new Color(0.76f, 0.14f, 0.14f), new Color(0.90f, 0.22f, 0.22f), new Color(0.973f, 0.443f, 0.443f),
                38f, () => _attachmentManager?.DetachLowestMass(), out _, fontAsset);

            LabUIHelper.CreateButton("Btn_Clear", root, "CLEAR\nGRAPH",
                new Vector2(padX + (actBtnW + gapX) * 3f, row1Y), new Vector2(actBtnW, row1H),
                new Color(0.72f, 0.36f, 0.04f), new Color(0.88f, 0.47f, 0.06f), new Color(0.984f, 0.749f, 0.141f),
                38f, () => ClearGraph(), out _, fontAsset);

            LabUIHelper.CreateButton("Btn_Reset", root, "RESET\nLAB",
                new Vector2(padX + (actBtnW + gapX) * 4f, row1Y), new Vector2(actBtnW, row1H),
                new Color(0.12f, 0.32f, 0.82f), new Color(0.18f, 0.44f, 0.94f), new Color(0.376f, 0.647f, 0.98f),
                38f, () => ResetExperiment(), out _, fontAsset);

            // Row 2: 5 Mass Selection Buttons (+30% larger, equal spacing, fully visible)
            const float row2Y = 615f;
            const float row2H = 370f;
            float massBtnW = (W - padX * 2f - gapX * 4f) / 5f;
            int[] presets = { 50, 100, 150, 200, 250 };

            for (int i = 0; i < presets.Length; i++)
            {
                int g = presets[i];
                LabUIHelper.CreateButton($"Preset_{g}g", root, $"+{g}g",
                    new Vector2(padX + i * (massBtnW + gapX), row2Y), new Vector2(massBtnW, row2H),
                    new Color(0.082f, 0.133f, 0.224f), new Color(0.141f, 0.231f, 0.376f), LabUIHelper.SubtleCyanBorder,
                    56f, () => _attachmentManager?.AttachMassByGrams(g), out _, fontAsset);
            }

            // Compact Guided Narration Bar integrated directly above console faceplate
            BuildCompactGuideBar(faceplate.transform, panelW, panelH, chassisMat, rimMat, fontAsset);
        }

        private void BuildCompactGuideBar(Transform faceplate, float panelW, float panelH, Material chassisMat, Material rimMat, TMP_FontAsset fontAsset)
        {
            const float barH = 0.086f;
            const int W = 2048;
            const int H = 310;

            GameObject barHost = new GameObject("GuideControlBar");
            barHost.transform.SetParent(faceplate, false);
            barHost.transform.localPosition = new Vector3(0f, panelH * 0.5f + barH * 0.5f + 0.018f, 0f);
            barHost.transform.localRotation = Quaternion.identity;

            CreateDecorativeCube("GuideBarBacking", barHost.transform,
                new Vector3(0f, 0f, 0.010f), new Vector3(panelW + 0.016f, barH + 0.012f, 0.014f), Quaternion.identity, chassisMat);
            CreateDecorativeCube("GuideBarRim", barHost.transform,
                new Vector3(0f, 0f, 0.006f), new Vector3(panelW + 0.020f, barH + 0.016f, 0.005f), Quaternion.identity, rimMat);

            Canvas gc = LabUIHelper.CreateWorldCanvas(
                "GuideControlCanvas",
                barHost.transform,
                new Vector3(0f, 0f, -0.008f),
                Quaternion.identity,
                panelW,
                barH,
                W,
                H,
                interactive: true);

            RectTransform gRoot = LabUIHelper.CreateStretchPanel("GuideBarRoot", gc.transform,
                new Color(0.024f, 0.055f, 0.114f, 0.99f), LabUIHelper.SubtleCyanBorder, 4f);

            const float pad = 28f;
            const float gap = 20f;
            const float by = 36f;
            const float bh = 238f;
            float x = pad;

            LabUIHelper.CreateButton("Btn_StartGuide", gRoot, "▶ START GUIDE",
                new Vector2(x, by), new Vector2(460f, bh),
                new Color(0.02f, 0.56f, 0.39f), new Color(0.05f, 0.70f, 0.49f), new Color(0.204f, 0.827f, 0.6f),
                40f, () => _guideManager?.StartGuide(), out _guideStartBtnLabel, fontAsset);
            x += 460f + gap;

            LabUIHelper.CreateButton("Btn_PauseGuide", gRoot, "⏸ PAUSE",
                new Vector2(x, by), new Vector2(310f, bh),
                new Color(0.012f, 0.412f, 0.631f), new Color(0.055f, 0.58f, 0.84f), new Color(0.22f, 0.74f, 0.97f),
                38f, () => _guideManager?.TogglePauseResume(), out _guidePauseBtnLabel, fontAsset);
            x += 310f + gap;

            LabUIHelper.CreateButton("Btn_RepeatGuide", gRoot, "↺ REPEAT",
                new Vector2(x, by), new Vector2(300f, bh),
                new Color(0.082f, 0.133f, 0.224f), new Color(0.141f, 0.231f, 0.376f), LabUIHelper.SubtleCyanBorder,
                38f, () => _guideManager?.RepeatCurrentInstruction(), out _, fontAsset);
            x += 300f + gap;

            LabUIHelper.CreateButton("Btn_StopGuide", gRoot, "■ STOP",
                new Vector2(x, by), new Vector2(260f, bh),
                new Color(0.60f, 0.12f, 0.12f), new Color(0.80f, 0.18f, 0.18f), new Color(0.973f, 0.443f, 0.443f),
                38f, () => _guideManager?.StopGuide(), out _, fontAsset);
            x += 260f + gap;

            LabUIHelper.CreateButton("Btn_GuideVolDown", gRoot, "VOL –",
                new Vector2(x, by), new Vector2(180f, bh),
                new Color(0.082f, 0.133f, 0.224f), new Color(0.141f, 0.231f, 0.376f), LabUIHelper.SubtleCyanBorder,
                36f, () => _guideManager?.AdjustVoiceVolume(-0.15f), out _, fontAsset);
            x += 180f + 12f;

            _guideVolText = LabUIHelper.CreateText("GuideVolVal", gRoot, "90%",
                new Vector2(x, by + 76f), new Vector2(150f, 80f), 42f, Color.white,
                TextAlignmentOptions.Center, FontStyles.Bold, fontAsset);
            x += 150f + 12f;

            float remW = Mathf.Max(160f, W - x - pad);
            LabUIHelper.CreateButton("Btn_GuideVolUp", gRoot, "VOL +",
                new Vector2(x, by), new Vector2(remW, bh),
                new Color(0.082f, 0.133f, 0.224f), new Color(0.141f, 0.231f, 0.376f), LabUIHelper.SubtleCyanBorder,
                36f, () => _guideManager?.AdjustVoiceVolume(0.15f), out _, fontAsset);

            RefreshGuideControls();
        }

        private void RefreshGuideControls()
        {
            if (_guideStartBtnLabel != null)
            {
                bool active = _guideManager != null && _guideManager.IsGuideActive;
                int stepNum = _guideManager != null ? _guideManager.CurrentStepNumber : 0;
                _guideStartBtnLabel.text = active ? (stepNum > 0 ? $"STEP {stepNum}/10" : "INTRO") : "▶ START GUIDE";
            }
            if (_guidePauseBtnLabel != null)
            {
                bool paused = _guideManager != null && _guideManager.IsPaused;
                _guidePauseBtnLabel.text = paused ? "▶ RESUME" : "⏸ PAUSE";
            }
            if (_guideVolText != null)
            {
                float vol = _guideManager != null ? _guideManager.VoiceVolume : 0.9f;
                _guideVolText.text = $"{Mathf.RoundToInt(vol * 100f)}%";
            }
        }

        public void SettleSpringNow()
        {
            _springSim?.SettleToEquilibrium();
            _audioSource?.PlayOneShot(LabAudioSynthesizer.GetToneClip(587.33f, 0.10f));
            RefreshUI();
        }

        private static GameObject CreateDecorativeCube(string objName, Transform parent, Vector3 localPos, Vector3 scale, Quaternion rot, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = objName;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col); // Never block VR controller rays
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public bool RecordMeasurement()
        {
            if (_springSim == null) return false;
            SpringState state = _springSim.GetState();
            if (state.massG <= 0)
            {
                _audioSource?.PlayOneShot(LabAudioSynthesizer.GetToneClip(240f, 0.18f));
                if (_consoleStatusTitle != null) _consoleStatusTitle.text = "NO WEIGHT ATTACHED";
                if (_consoleStatusSub != null) _consoleStatusSub.text = "ATTACH A WEIGHT AND LET THE SPRING SETTLE BEFORE RECORDING";
                OnRecordRejectedEmpty?.Invoke();
                return false;
            }
            if (!state.isAtEquilibrium)
            {
                _audioSource?.PlayOneShot(LabAudioSynthesizer.GetToneClip(280f, 0.18f));
                if (_consoleStatusTitle != null) _consoleStatusTitle.text = "SPRING STILL OSCILLATING";
                if (_consoleStatusSub != null) _consoleStatusSub.text = "WAIT FOR EQUILIBRIUM OR PRESS 'SETTLE SPRING', THEN RECORD";
                OnRecordRejectedMoving?.Invoke();
                return false;
            }

            float massKg = state.massKg;
            float gravity = _springSim.gravity;
            float forceN = massKg * gravity;
            float dispM = Mathf.Max(0f, state.displacementM);
            float calcK = dispM > 0.0005f ? forceN / dispM : nominalK;

            int idx = Points.FindIndex(p => Mathf.Abs(p.massKg - massKg) < 0.002f);
            DataPoint dp = new DataPoint
            {
                massG = Mathf.RoundToInt(massKg * 1000f),
                massKg = massKg,
                forceN = forceN,
                displacementM = dispM,
                k = calcK
            };
            if (idx >= 0) Points[idx] = dp;
            else Points.Add(dp);

            Points.Sort((a, b) => a.displacementM.CompareTo(b.displacementM));
            CalculateRegression();

            _audioSource?.PlayOneShot(LabAudioSynthesizer.GetChordClip(523.25f, 659.25f, 783.99f, 0.22f));
            RefreshUI();
            OnMeasurementRecorded?.Invoke(dp, gravity, _springSim.springConstant);
            return true;
        }

        public void ClearGraph()
        {
            Points.Clear();
            FittedSlope = null;
            R2 = null;
            _audioSource?.PlayOneShot(LabAudioSynthesizer.GetChordClip(440f, 330f, 0f, 0.18f));
            RefreshUI();
        }

        public void ResetExperiment()
        {
            _gravityTablet?.ResetToDefault();
            _attachmentManager?.DetachAllMasses(playSound: true, immediate: true);
            _springSim?.SettleToEquilibrium();
            _extensionDisplays?.ResetDisplays();
            Points.Clear();
            FittedSlope = null;
            R2 = null;
            _audioSource?.PlayOneShot(LabAudioSynthesizer.GetChordClip(350f, 260f, 0f, 0.22f));
            RefreshUI();
            OnExperimentReset?.Invoke();
        }

        private void CalculateRegression()
        {
            if (Points.Count < 2)
            {
                FittedSlope = Points.Count == 1 ? Points[0].k : (float?)null;
                R2 = Points.Count >= 1 ? 1.0f : (float?)null;
                return;
            }

            float sumX2 = 0f, sumXY = 0f, sumY = 0f;
            foreach (DataPoint p in Points)
            {
                sumXY += p.displacementM * p.forceN;
                sumX2 += p.displacementM * p.displacementM;
                sumY += p.forceN;
            }

            if (sumX2 > 0f)
            {
                float slope = sumXY / sumX2;
                FittedSlope = slope;
                float meanY = sumY / Points.Count;
                float ssTot = 0f, ssRes = 0f;
                foreach (DataPoint p in Points)
                {
                    float yPred = slope * p.displacementM;
                    ssTot += (p.forceN - meanY) * (p.forceN - meanY);
                    ssRes += (p.forceN - yPred) * (p.forceN - yPred);
                }
                R2 = ssTot > 0f ? Mathf.Clamp01(1f - ssRes / ssTot) : 1f;
            }
            else
            {
                FittedSlope = null;
                R2 = null;
            }
        }

        public void TickUpdate()
        {
            if (_springSim == null) return;
            if (!_springSim.IsAtEquilibrium)
            {
                if (Time.unscaledTime - _lastOscillatingRenderTime > 0.05f)
                {
                    _lastOscillatingRenderTime = Time.unscaledTime;
                    RefreshUI();
                }
            }
        }

        public void RefreshUI()
        {
            SpringState state = _springSim != null ? _springSim.GetState() : default;
            float gravity = _springSim != null ? _springSim.gravity : 9.81f;

            if (_statusBadgeText != null)
            {
                bool settled = state.isAtEquilibrium;
                Color col = settled ? new Color(0.063f, 0.725f, 0.506f) : new Color(0.961f, 0.62f, 0.043f);
                _statusBadgeText.text = settled ? "STATIC EQUILIBRIUM" : "OSCILLATING (DAMPED)";
                if (_statusBadgeDot != null) _statusBadgeDot.color = col;
                if (_statusBadgeOutline != null) _statusBadgeOutline.effectColor = col;
            }

            float? effSlope = FittedSlope ?? (Points.Count == 1 ? Points[0].k : (float?)null);
            if (_calcKText != null)
                _calcKText.text = effSlope.HasValue ? $"{effSlope.Value:F2} N/m" : "-- N/m";
            if (_r2Text != null)
            {
                string r2Str = R2.HasValue ? $"{R2.Value:F4}" : "--";
                _r2Text.text = $"Goodness of Fit R²:  {r2Str}\nNominal Spring k:    {nominalK:F1} N/m";
            }

            if (_liveTelemetryText != null)
            {
                float curMassKg = state.massG / 1000f;
                float curForce = curMassKg * gravity;
                string ratio = state.displacementM > 0.0005f ? $"{curForce / state.displacementM:F1}" : "--";
                _liveTelemetryText.text =
                    $"Active Gravity (g):        {gravity:F2} m/s²\n" +
                    $"Current Mass (m):          {state.massG} g  ({curMassKg:F3} kg)\n" +
                    $"Applied Force (F = mg):    {curForce:F3} N\n" +
                    $"Spring Extension (x):      {state.displacementM:F4} m  ({state.displacementCm:F2} cm)\n" +
                    $"Instantaneous Ratio (F/x): {ratio} N/m";
            }

            if (_logHeaderText != null)
                _logHeaderText.text = $"EXPERIMENTAL LOG ({Points.Count} OBSERVATIONS)";
            if (_logBodyText != null)
            {
                if (Points.Count == 0)
                {
                    _logBodyText.text = "<color=#94a3b8>No observations recorded yet.</color>";
                }
                else
                {
                    int start = Mathf.Max(0, Points.Count - 5);
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine("<color=#94a3b8>#   Mass     Force     Ext(x)    k(N/m)</color>");
                    for (int i = start; i < Points.Count; i++)
                    {
                        DataPoint p = Points[i];
                        sb.AppendLine($"{i + 1}   {p.massG,4}g    {p.forceN,5:F2}N    {p.displacementM,5:F3}m    <color=#38bdf8>{p.k,5:F1}</color>");
                    }
                    _logBodyText.text = sb.ToString();
                }
            }

            if (_consoleStatusTitle != null && _consoleStatusSub != null)
            {
                if (state.massG == 0)
                {
                    _consoleStatusTitle.text = "HOOKE'S LAW LAB CONTROLS";
                    _consoleStatusTitle.color = Color.white;
                    _consoleStatusSub.text = "TAP A MASS BELOW OR DRAG WEIGHT TO HANGER";
                }
                else
                {
                    float forceN = (state.massG / 1000f) * gravity;
                    _consoleStatusTitle.text = $"LOAD: {state.massG}g ({forceN:F2}N)  |  EXTENSION: {state.displacementCm:F1} cm";
                    _consoleStatusTitle.color = new Color(0.204f, 0.827f, 0.6f);
                    _consoleStatusSub.text = "READY: PRESS 'RECORD POINT' TO PLOT ON TV GRAPH";
                }
            }

            RenderPlotTexture(effSlope);
        }

        private void RenderPlotTexture(float? effectiveSlope)
        {
            if (_plotTexture == null) return;

            Color32[] px = new Color32[PlotTexW * PlotTexH];
            Color32 bg = new Color32(3, 8, 18, 255);
            Color32 grid = new Color32(0, 85, 110, 120);
            Color32 axis = new Color32(148, 163, 184, 255);
            Color32 lineCol = new Color32(56, 189, 248, 255);
            Color32 glowCol = new Color32(14, 165, 233, 95);
            Color32 whiteCol = new Color32(255, 255, 255, 255);

            for (int i = 0; i < px.Length; i++) px[i] = bg;

            float maxDisp = 0.14f;
            float maxForce = 3.5f;
            foreach (DataPoint p in Points)
            {
                if (p.displacementM > maxDisp * 0.85f) maxDisp = p.displacementM * 1.25f;
                if (p.forceN > maxForce * 0.85f) maxForce = p.forceN * 1.25f;
            }
            maxDisp = Mathf.Ceil(maxDisp * 50f) / 50f;
            maxForce = Mathf.Ceil(maxForce * 2f) / 2f;

            const int pad = 30;
            int pw = PlotTexW - pad * 2;
            int ph = PlotTexH - pad * 2;

            for (int i = 0; i <= 7; i++)
            {
                int gy = pad + Mathf.RoundToInt((ph * i) / 7f);
                DrawLine(px, PlotTexW, PlotTexH, pad, gy, pad + pw, gy, i == 0 ? axis : grid, i == 0 ? 2 : 1);
                int gx = pad + Mathf.RoundToInt((pw * i) / 7f);
                DrawLine(px, PlotTexW, PlotTexH, gx, pad, gx, pad + ph, i == 0 ? axis : grid, i == 0 ? 2 : 1);
            }

            if (effectiveSlope.HasValue && effectiveSlope.Value > 0f)
            {
                float xEnd = maxDisp;
                float yEnd = effectiveSlope.Value * xEnd;
                int ex = pad + Mathf.RoundToInt((xEnd / maxDisp) * pw);
                int ey = pad + Mathf.RoundToInt((yEnd / maxForce) * ph);
                DrawLine(px, PlotTexW, PlotTexH, pad, pad, ex, ey, glowCol, 8);
                DrawLine(px, PlotTexW, PlotTexH, pad, pad, ex, ey, lineCol, 4);

                if (_slopeBadgeRt != null && _slopeBadgeText != null)
                {
                    _slopeBadgeRt.gameObject.SetActive(true);
                    _slopeBadgeText.text = $"Slope k = {effectiveSlope.Value:F2} N/m";
                }
            }
            else if (_slopeBadgeRt != null)
            {
                _slopeBadgeRt.gameObject.SetActive(false);
            }

            foreach (DataPoint p in Points)
            {
                int cx = pad + Mathf.RoundToInt((p.displacementM / maxDisp) * pw);
                int cy = pad + Mathf.RoundToInt((p.forceN / maxForce) * ph);
                DrawCircle(px, PlotTexW, PlotTexH, cx, cy, 18, glowCol);
                DrawCircle(px, PlotTexW, PlotTexH, cx, cy, 11, lineCol);
                DrawCircle(px, PlotTexW, PlotTexH, cx, cy, 5, whiteCol);
            }

            if (_emptyPromptText != null)
            {
                _emptyPromptText.gameObject.SetActive(Points.Count == 0);
            }

            _plotTexture.SetPixels32(px);
            _plotTexture.Apply();
        }

        private static void DrawLine(Color32[] px, int w, int h, int x0, int y0, int x1, int y1, Color32 col, int thickness)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            if (steps == 0) return;
            int half = thickness / 2;
            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                int cx = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                int cy = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                for (int dy = -half; dy <= half; dy++)
                {
                    int py = cy + dy;
                    if (py < 0 || py >= h) continue;
                    for (int dx = -half; dx <= half; dx++)
                    {
                        int qx = cx + dx;
                        if (qx >= 0 && qx < w) px[py * w + qx] = col;
                    }
                }
            }
        }

        private static void DrawCircle(Color32[] px, int w, int h, int cx, int cy, int r, Color32 col)
        {
            int r2 = r * r;
            for (int dy = -r; dy <= r; dy++)
            {
                int py = cy + dy;
                if (py < 0 || py >= h) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r2) continue;
                    int qx = cx + dx;
                    if (qx >= 0 && qx < w) px[py * w + qx] = col;
                }
            }
        }
    }
}
