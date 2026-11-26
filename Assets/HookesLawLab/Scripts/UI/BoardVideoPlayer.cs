using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using HookesLawLab.Lab;

namespace HookesLawLab.UI
{
    /// <summary>
    /// Wall-mounted Lecture Board ('Wall monitor screen') positioned cleanly above the horizontal work shelf.
    /// Displays the Hooke's Law theory screen with only two bottom VR controls: YouTube and Refresh.
    /// </summary>
    public class BoardVideoPlayer : MonoBehaviour
    {
        private const string HeaderTitleCopy = "HOOKE'S LAW LECTURE BOARD";
        private const string MainTitleCopy = "HOOKE'S LAW: MASSES & SPRINGS";
        private const string SubTitleCopy = "Physical Principle: Restoring Force Proportional to Displacement  •  F = -kx  •  mg = kx";
        private const string EqTitleCopy = "KEY EQUATIONS & CONCEPTS:";
        private const string EqBodyCopy =
            "<color=#38bdf8><b>F = -kx</b></color>\n" +
            "•  <b>F</b> : Restoring Force (Newtons)\n" +
            "•  <b>x</b> : Extension / Displacement (metres)\n" +
            "•  <b>k</b> : Spring Constant (N/m)\n" +
            "•  <b>Static Equilibrium</b> : mg = kx";

        public string youtubeUrl = "https://www.youtube.com/watch?v=YjG_12xpUwQ";

        private GameObject _boardObj;
        private GameObject _displayHost;
        private VideoPlayer _videoPlayer;
        private AudioSource _videoAudio;
        private AudioSource _sfxAudio;
        private RenderTexture _videoRT;
        private RawImage _videoDisplayImage;
        private GameObject _standbyPoster;
        private GameObject _posterCard;
        private GameObject _controlBar;

        private TMP_Text _headerTitleText;
        private TMP_Text _mainTitleText;
        private TMP_Text _subTitleText;
        private TMP_Text _eqTitleText;
        private TMP_Text _eqBodyText;
        private TMP_Text _youtubeBtnLabel;
        private TMP_Text _refreshBtnLabel;

        public Button YouTubeButton { get; private set; }
        public Button RefreshButton { get; private set; }
        public bool IsShowingTheoryScreen => _standbyPoster != null && _standbyPoster.activeSelf;

        public event Action OnBoardRefreshed;

        public void Initialize(GameObject boardScreenObj, VideoClip videoClip, Vector3 viewerRefPos, TMP_FontAsset fontAsset = null)
        {
            if (boardScreenObj == null) return;
            _boardObj = boardScreenObj;

            if (_sfxAudio == null)
            {
                _sfxAudio = gameObject.AddComponent<AudioSource>();
                _sfxAudio.playOnAwake = false;
                _sfxAudio.spatialBlend = 0f;
            }

            if (_displayHost != null)
            {
                Destroy(_displayHost);
                _displayHost = null;
            }

            ConfigureBoardMediaGuard(boardScreenObj);
            BuildLectureBoardDisplay(boardScreenObj, viewerRefPos, fontAsset);
            ApplyTheoryScreenState();
        }

        private void ConfigureBoardMediaGuard(GameObject boardObj)
        {
            // Keep references to any VideoPlayer/AudioSource attached to this board only so
            // RefreshBoard() can stop board media cleanly if external content ever plays on it.
            _videoPlayer = boardObj.GetComponent<VideoPlayer>();
            if (_videoPlayer != null)
            {
                _videoPlayer.playOnAwake = false;
                if (_videoPlayer.isPlaying) _videoPlayer.Stop();
            }

            _videoAudio = boardObj.GetComponent<AudioSource>();
            if (_videoAudio != null)
            {
                _videoAudio.playOnAwake = false;
                if (_videoAudio.isPlaying) _videoAudio.Stop();
            }
        }

        private void BuildLectureBoardDisplay(GameObject boardObj, Vector3 viewerRefPos, TMP_FontAsset fontAsset)
        {
            MeshFilter mf = boardObj.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;

            LabUIHelper.ComputeScreenOverlayPose(
                boardObj.transform,
                mesh,
                viewerRefPos,
                out Vector3 localCenter,
                out Quaternion localRot,
                out float origW,
                out float origH);

            // Hide original small board screen renderer so our enlarged non-overlapping display replaces it cleanly
            Renderer origRend = boardObj.GetComponent<Renderer>();
            if (origRend != null) origRend.enabled = false;

            // Hide adjacent small housing bezel so the enlarged frame never intersects it
            if (boardObj.transform.parent != null)
            {
                foreach (Transform sibling in boardObj.transform.parent)
                {
                    string sn = sibling.name.ToLowerInvariant().Replace('_', ' ');
                    if (sn.StartsWith("wall monitor housing") && !sn.Contains("001"))
                    {
                        Renderer hr = sibling.GetComponent<Renderer>();
                        if (hr != null) hr.enabled = false;
                    }
                }
            }

            float dispW = Mathf.Max(1.66f, origW) * 1.30f; // ~2.16m
            float dispH = Mathf.Max(0.93f, origH) * 1.44f; // ~1.34m (includes bottom YouTube / Refresh bar)

            Vector3 worldScreenCenter = boardObj.transform.TransformPoint(localCenter);
            Quaternion worldScreenRot = boardObj.transform.rotation * localRot;
            Vector3 outward = worldScreenRot * -Vector3.forward; // Canvas front points -Z local

            float targetCenterY = Mathf.Max(worldScreenCenter.y + 0.255f, 1.84f);
            Vector3 displayPos = new Vector3(worldScreenCenter.x, targetCenterY, worldScreenCenter.z) + outward * 0.045f;

            _displayHost = new GameObject("BoardDisplayHost");
            _displayHost.transform.SetParent(transform, false);
            _displayHost.transform.position = displayPos;
            _displayHost.transform.rotation = worldScreenRot;

            // Sleek dark-navy chassis strictly BEHIND the UI Canvas (zero collider so XR rays never blocked)
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material chassisMat = new Material(litShader) { color = new Color(0.047f, 0.075f, 0.137f) };
            chassisMat.SetFloat("_Metallic", 0.7f);
            chassisMat.SetFloat("_Smoothness", 0.5f);

            GameObject chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chassis.name = "BoardDisplayBacking";
            Collider col = chassis.GetComponent<Collider>();
            if (col != null) Destroy(col);
            chassis.transform.SetParent(_displayHost.transform, false);
            chassis.transform.localPosition = new Vector3(0f, 0f, 0.014f);
            chassis.transform.localScale = new Vector3(dispW + 0.03f, dispH + 0.03f, 0.020f);
            MeshRenderer cmr = chassis.GetComponent<MeshRenderer>();
            cmr.sharedMaterial = chassisMat;
            cmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // High-resolution 2048 x 1280 World-Space Canvas
            const int W = 2048;
            const int H = 1280;

            Canvas canvas = LabUIHelper.CreateWorldCanvas(
                "BoardVideoCanvas",
                _displayHost.transform,
                new Vector3(0f, 0f, -0.004f),
                Quaternion.identity,
                dispW,
                dispH,
                W,
                H,
                interactive: true);

            RectTransform root = LabUIHelper.CreateStretchPanel("BoardRoot", canvas.transform,
                new Color(0.02f, 0.047f, 0.098f, 1f), LabUIHelper.SubtleCyanBorder, 4f);

            // 1. Top Header Bar (height = 92px; no playback status/timer)
            LabUIHelper.CreatePanel("Header", root, new Vector2(8f, 8f), new Vector2(W - 16f, 92f),
                new Color(0.043f, 0.114f, 0.212f, 1f), new Color(0.031f, 0.569f, 0.698f, 0.45f), 2f);

            _headerTitleText = LabUIHelper.CreateText("HdrLeft", root, HeaderTitleCopy,
                new Vector2(36f, 28f), new Vector2(W - 72f, 48f), 34f, new Color(0.22f, 0.74f, 0.97f),
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            // 2. Main Theory Screen Area (y = 108..1036, height = 928px)
            const float viewY = 108f;
            const float viewH = 928f;

            // Non-interactive surface kept disabled unless external board media attaches to it
            GameObject vidGo = new GameObject("VideoSurface");
            vidGo.transform.SetParent(root, false);
            RectTransform vrt = vidGo.AddComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0f, 1f);
            vrt.anchorMax = new Vector2(0f, 1f);
            vrt.pivot = new Vector2(0f, 1f);
            vrt.anchoredPosition = new Vector2(16f, -viewY);
            vrt.sizeDelta = new Vector2(W - 32f, viewH);
            _videoDisplayImage = vidGo.AddComponent<RawImage>();
            _videoDisplayImage.raycastTarget = false; // Never act as a hidden raycast/click target
            vidGo.SetActive(false);

            // Theory Screen
            RectTransform poster = LabUIHelper.CreatePanel("StandbyPoster", root,
                new Vector2(16f, viewY), new Vector2(W - 32f, viewH),
                new Color(0.024f, 0.063f, 0.125f, 1f));
            _standbyPoster = poster.gameObject;

            _mainTitleText = LabUIHelper.CreateText("MainTitle", poster, MainTitleCopy,
                new Vector2(40f, 84f), new Vector2(W - 112f, 88f), 70f, Color.white,
                TextAlignmentOptions.Center, FontStyles.Bold, fontAsset);

            _subTitleText = LabUIHelper.CreateText("SubTitle", poster, SubTitleCopy,
                new Vector2(40f, 190f), new Vector2(W - 112f, 52f), 34f, new Color(0.75f, 0.83f, 0.92f),
                TextAlignmentOptions.Center, FontStyles.Normal, fontAsset);

            const float cardW = 1360f;
            const float cardH = 510f;
            RectTransform card = LabUIHelper.CreatePanel("PosterCard", poster,
                new Vector2((W - 32f - cardW) * 0.5f, 296f), new Vector2(cardW, cardH),
                new Color(0.039f, 0.086f, 0.169f, 0.96f), LabUIHelper.SubtleCyanBorder, 3f);
            _posterCard = card.gameObject;

            _eqTitleText = LabUIHelper.CreateText("EqTitle", card, EqTitleCopy,
                new Vector2(76f, 44f), new Vector2(cardW - 152f, 56f), 42f, Color.white,
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            _eqBodyText = LabUIHelper.CreateText("EqBody", card, EqBodyCopy,
                new Vector2(76f, 120f), new Vector2(cardW - 152f, 340f), 38f, new Color(0.94f, 0.96f, 0.99f),
                TextAlignmentOptions.Left, FontStyles.Normal, fontAsset);

            // 3. Integrated Bottom Control Bar — Only YouTube and Refresh
            const float barW = W - 32f;
            const float barH = 218f;
            RectTransform ctrlBar = LabUIHelper.CreatePanel("IntegratedControlBar", root,
                new Vector2(16f, 1046f), new Vector2(barW, barH),
                new Color(0.035f, 0.082f, 0.157f, 1f), LabUIHelper.SubtleCyanBorder, 3f);
            _controlBar = ctrlBar.gameObject;

            const float btnW = 640f;
            const float btnH = 140f;
            const float gap = 140f;
            float totalBtnsW = (btnW * 2f) + gap;
            float startX = (barW - totalBtnsW) * 0.5f;
            float btnY = (barH - btnH) * 0.5f;

            YouTubeButton = LabUIHelper.CreateButton("YouTubeBtn", ctrlBar, "YOUTUBE",
                new Vector2(startX, btnY), new Vector2(btnW, btnH),
                new Color(0.52f, 0.12f, 0.12f), new Color(0.75f, 0.14f, 0.14f), new Color(0.94f, 0.28f, 0.28f),
                44f, OpenYouTubeLink, out _youtubeBtnLabel, fontAsset);

            RefreshButton = LabUIHelper.CreateButton("RefreshBtn", ctrlBar, "REFRESH",
                new Vector2(startX + btnW + gap, btnY), new Vector2(btnW, btnH),
                new Color(0.055f, 0.19f, 0.33f), new Color(0.11f, 0.30f, 0.49f), LabUIHelper.SubtleCyanBorder,
                44f, RefreshBoard, out _refreshBtnLabel, fontAsset);
        }

        public void OpenYouTubeLink()
        {
            _sfxAudio?.PlayOneShot(LabAudioSynthesizer.GetToneClip(800f, 0.1f));
            if (!string.IsNullOrWhiteSpace(youtubeUrl))
            {
                Application.OpenURL(youtubeUrl);
            }
        }

        /// <summary>
        /// Stops any media playing on this lecture board and restores the original theory screen
        /// in place without recreating UI objects, duplicating listeners, or affecting the rest of the lab.
        /// </summary>
        public void RefreshBoard()
        {
            _sfxAudio?.PlayOneShot(LabAudioSynthesizer.GetToneClip(560f, 0.08f));
            ApplyTheoryScreenState();
            OnBoardRefreshed?.Invoke();
        }

        private void ApplyTheoryScreenState()
        {
            if (_boardObj != null)
            {
                VideoPlayer[] players = _boardObj.GetComponentsInChildren<VideoPlayer>(true);
                for (int i = 0; i < players.Length; i++)
                {
                    if (players[i] == null) continue;
                    if (players[i].isPlaying) players[i].Stop();
                    players[i].time = 0d;
                }

                AudioSource[] audioSources = _boardObj.GetComponentsInChildren<AudioSource>(true);
                for (int i = 0; i < audioSources.Length; i++)
                {
                    if (audioSources[i] == null) continue;
                    if (audioSources[i].isPlaying) audioSources[i].Stop();
                }
            }

            if (_displayHost != null)
            {
                VideoPlayer[] hostPlayers = _displayHost.GetComponentsInChildren<VideoPlayer>(true);
                for (int i = 0; i < hostPlayers.Length; i++)
                {
                    if (hostPlayers[i] == null) continue;
                    if (hostPlayers[i].isPlaying) hostPlayers[i].Stop();
                    hostPlayers[i].time = 0d;
                }
            }

            if (_videoDisplayImage != null)
            {
                _videoDisplayImage.texture = null;
                _videoDisplayImage.gameObject.SetActive(false);
            }

            if (_videoRT != null)
            {
                _videoRT.Release();
                Destroy(_videoRT);
                _videoRT = null;
            }

            if (_headerTitleText != null) _headerTitleText.text = HeaderTitleCopy;
            if (_mainTitleText != null) _mainTitleText.text = MainTitleCopy;
            if (_subTitleText != null) _subTitleText.text = SubTitleCopy;
            if (_eqTitleText != null) _eqTitleText.text = EqTitleCopy;
            if (_eqBodyText != null) _eqBodyText.text = EqBodyCopy;
            if (_youtubeBtnLabel != null) _youtubeBtnLabel.text = "YOUTUBE";
            if (_refreshBtnLabel != null) _refreshBtnLabel.text = "REFRESH";

            if (_standbyPoster != null) _standbyPoster.SetActive(true);
            if (_posterCard != null) _posterCard.SetActive(true);
            if (_controlBar != null) _controlBar.SetActive(true);
            if (YouTubeButton != null) YouTubeButton.gameObject.SetActive(true);
            if (RefreshButton != null) RefreshButton.gameObject.SetActive(true);
        }

        public void TickUpdate()
        {
            // No runtime timer or scrubber readout needed when only YouTube and Refresh exist.
        }

        private void OnDestroy()
        {
            if (_videoRT != null)
            {
                _videoRT.Release();
                Destroy(_videoRT);
                _videoRT = null;
            }
        }
    }
}
