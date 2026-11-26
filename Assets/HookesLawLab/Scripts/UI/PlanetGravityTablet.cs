using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using HookesLawLab.Physics;
using HookesLawLab.Lab;

namespace HookesLawLab.UI
{
    /// <summary>
    /// Physical 3D futuristic tablet situated on the laboratory workbench beside the EXTENSION display.
    /// Provides high-DPI 2048x1440 World-Space UI for selecting Earth (9.81), Moon (1.62), Mars (3.71), or Jupiter (24.79 m/s^2).
    /// </summary>
    public class PlanetGravityTablet : MonoBehaviour
    {
        [Serializable]
        public struct PlanetInfo
        {
            public string id;
            public string name;
            public float gravity;
            public float ratioG;
            public string tag;
            public Color themeColor;
        }

        public static readonly PlanetInfo[] Planets = new PlanetInfo[]
        {
            new PlanetInfo { id = "earth", name = "Earth", gravity = 9.81f, ratioG = 1.00f, tag = "Standard Lab (1.00 g)", themeColor = new Color(0.22f, 0.74f, 0.97f) },
            new PlanetInfo { id = "moon", name = "Moon", gravity = 1.62f, ratioG = 0.17f, tag = "Lunar Surface (0.17 g)", themeColor = new Color(0.796f, 0.835f, 0.882f) },
            new PlanetInfo { id = "mars", name = "Mars", gravity = 3.71f, ratioG = 0.38f, tag = "Martian Surface (0.38 g)", themeColor = new Color(0.973f, 0.443f, 0.443f) },
            new PlanetInfo { id = "jupiter", name = "Jupiter", gravity = 24.79f, ratioG = 2.53f, tag = "Jovian Cloudtops (2.53 g)", themeColor = new Color(0.984f, 0.749f, 0.141f) }
        };

        public PlanetInfo SelectedPlanet { get; private set; } = Planets[0];
        public event Action<PlanetInfo> OnGravityChanged;

        private SpringSimulation _springSim;
        private AudioSource _audioSource;
        private TMP_Text _activeBannerTitle;
        private TMP_Text _activeBannerSub;
        private float _lastSelectTime = -10f;

        private struct CardUI
        {
            public string id;
            public Image bg;
            public Outline border;
            public Image badgeBg;
            public TMP_Text badgeTxt;
        }
        private readonly CardUI[] _cards = new CardUI[4];

        public void Initialize(SpringSimulation sim, Vector3 worldPos, Quaternion worldRot, TMP_FontAsset fontAsset = null)
        {
            _springSim = sim;
            transform.position = worldPos;
            transform.rotation = worldRot;
            transform.localScale = Vector3.one * 1.5f;

            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;

            BuildChassis();
            BuildScreenUI(fontAsset);
            SelectPlanet("earth", notify: false);
        }

        private void BuildChassis()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Material chassisMat = new Material(litShader) { color = new Color(0.035f, 0.067f, 0.118f) };
            chassisMat.SetFloat("_Metallic", 0.85f);
            chassisMat.SetFloat("_Smoothness", 0.75f);

            Material rearMat = new Material(litShader) { color = new Color(0.92f, 0.94f, 0.96f) };

            Material trimMat = new Material(litShader) { color = new Color(0.031f, 0.569f, 0.698f) };
            trimMat.SetFloat("_Metallic", 0.4f);
            trimMat.SetFloat("_Smoothness", 0.6f);
            trimMat.EnableKeyword("_EMISSION");
            trimMat.SetColor("_EmissionColor", new Color(0.01f, 0.18f, 0.24f));

            Material standMat = new Material(litShader) { color = new Color(0.118f, 0.161f, 0.231f) };
            standMat.SetFloat("_Metallic", 0.9f);

            const float w = 0.29f;
            const float h = 0.215f;
            const float d = 0.016f;

            CreateDecorativeBox("TabletChassis", new Vector3(0f, 0f, 0f), new Vector3(w, h, d), Quaternion.identity, chassisMat);
            CreateDecorativeBox("RearBacking", new Vector3(0f, 0f, d * 0.5f + 0.001f), new Vector3(w * 0.96f, h * 0.96f, 0.003f), Quaternion.identity, rearMat);
            CreateDecorativeBox("SlimRim", new Vector3(0f, 0f, 0.002f), new Vector3(w + 0.004f, h + 0.004f, 0.004f), Quaternion.identity, trimMat);
            CreateDecorativeBox("StandBase", new Vector3(0f, -h * 0.5f - 0.04f, 0.05f), new Vector3(0.24f, 0.015f, 0.18f), Quaternion.Euler(-14f, 0f, 0f), standMat);
            CreateDecorativeBox("StandStrut", new Vector3(0f, -h * 0.45f, 0.04f), new Vector3(0.032f, 0.12f, 0.032f), Quaternion.identity, standMat);
        }

        private void CreateDecorativeBox(string partName, Vector3 localPos, Vector3 scale, Quaternion localRot, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col); // Never block VR controller rays
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = scale;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void BuildScreenUI(TMP_FontAsset fontAsset)
        {
            const float screenW = 0.29f * 0.93f;
            const float screenH = 0.215f * 0.92f;
            const int pxW = 2048;
            const int pxH = 1440;

            Canvas canvas = LabUIHelper.CreateWorldCanvas(
                "PlanetTabletCanvas",
                transform,
                new Vector3(0f, 0f, -0.011f),
                Quaternion.identity,
                screenW,
                screenH,
                pxW,
                pxH,
                interactive: true);

            RectTransform root = LabUIHelper.CreateStretchPanel(
                "Root",
                canvas.transform,
                new Color(0.02f, 0.047f, 0.094f, 1f),
                LabUIHelper.SubtleCyanBorder,
                5f);

            // Header
            LabUIHelper.CreatePanel("Header", root, Vector2.zero, new Vector2(pxW, 120f), new Color(0.031f, 0.09f, 0.169f, 1f));
            LabUIHelper.CreateText("Title", root, "PLANET GRAVITY SELECTOR", new Vector2(72f, 28f), new Vector2(1000f, 68f),
                48f, new Color(0.22f, 0.74f, 0.97f), TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);
            LabUIHelper.CreateText("Sub", root, "HOOKE'S LAW FIELD CONTROLLER", new Vector2(pxW - 860f, 36f), new Vector2(780f, 56f),
                30f, new Color(0.58f, 0.65f, 0.75f), TextAlignmentOptions.Right, FontStyles.Bold, fontAsset);

            // Active status banner
            RectTransform banner = LabUIHelper.CreatePanel("StatusBanner", root, new Vector2(76f, 152f), new Vector2(pxW - 152f, 200f),
                new Color(0.031f, 0.11f, 0.204f, 0.96f), LabUIHelper.SubtleCyanBorder, 4f);

            _activeBannerTitle = LabUIHelper.CreateText("ActiveTitle", banner, "ACTIVE GRAVITY: EARTH — 9.81 m/s²",
                new Vector2(56f, 28f), new Vector2(pxW - 280f, 80f), 54f, Color.white,
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            _activeBannerSub = LabUIHelper.CreateText("ActiveSub", banner, "Relative to Earth: 1.00g  •  x = (m · g) / k  •  Scale: 1.00×",
                new Vector2(56f, 116f), new Vector2(pxW - 280f, 60f), 34f, new Color(0.74f, 0.84f, 0.94f),
                TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

            // 2x2 Planet Cards
            Vector2[] positions = new Vector2[]
            {
                new Vector2(76f, 390f),
                new Vector2(1060f, 390f),
                new Vector2(76f, 890f),
                new Vector2(1060f, 890f)
            };

            for (int i = 0; i < 4; i++)
            {
                PlanetInfo p = Planets[i];
                Vector2 pos = positions[i];
                GameObject cardGo = new GameObject($"Card_{p.id}");
                cardGo.transform.SetParent(root, false);
                RectTransform crt = cardGo.AddComponent<RectTransform>();
                crt.anchorMin = new Vector2(0f, 1f);
                crt.anchorMax = new Vector2(0f, 1f);
                crt.pivot = new Vector2(0f, 1f);
                crt.anchoredPosition = new Vector2(pos.x, -pos.y);
                crt.sizeDelta = new Vector2(912f, 440f);

                Image cardImg = cardGo.AddComponent<Image>();
                cardImg.color = new Color(0.039f, 0.086f, 0.149f, 0.95f);
                cardImg.raycastTarget = true;

                Outline cardBorder = cardGo.AddComponent<Outline>();
                cardBorder.effectColor = new Color(0.118f, 0.227f, 0.373f, 1f);
                cardBorder.effectDistance = new Vector2(3f, -3f);

                Button btn = cardGo.AddComponent<Button>();
                btn.targetGraphic = cardImg;
                string pid = p.id;
                btn.onClick.AddListener(() => SelectPlanet(pid, notify: true));
                cardGo.AddComponent<VRUIButtonFeedback>();

                // Procedural planet circle sprite
                GameObject iconGo = new GameObject("PlanetIcon");
                iconGo.transform.SetParent(cardGo.transform, false);
                RectTransform irt = iconGo.AddComponent<RectTransform>();
                irt.anchorMin = new Vector2(0f, 0.5f);
                irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.anchoredPosition = new Vector2(164f, 0f);
                float iconSize = p.id == "jupiter" ? 208f : (p.id == "earth" ? 192f : 168f);
                irt.sizeDelta = new Vector2(iconSize, iconSize);
                RawImage iconImg = iconGo.AddComponent<RawImage>();
                iconImg.texture = GeneratePlanetTexture(p.id);
                iconImg.raycastTarget = false;

                LabUIHelper.CreateText("Name", crt, p.name, new Vector2(310f, 56f), new Vector2(400f, 80f),
                    64f, Color.white, TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

                LabUIHelper.CreateText("Gravity", crt, $"{p.gravity:F2} m/s²", new Vector2(310f, 152f), new Vector2(520f, 84f),
                    64f, new Color(0.22f, 0.74f, 0.97f), TextAlignmentOptions.Left, FontStyles.Bold, fontAsset);

                LabUIHelper.CreateText("Tag", crt, p.tag, new Vector2(310f, 256f), new Vector2(560f, 60f),
                    32f, new Color(0.75f, 0.82f, 0.90f), TextAlignmentOptions.Left, FontStyles.Normal, fontAsset);

                RectTransform badgeRt = LabUIHelper.CreatePanel("Badge", crt, new Vector2(912f - 220f, 48f), new Vector2(180f, 64f),
                    new Color(0.118f, 0.227f, 0.373f, 1f));
                Image badgeImg = badgeRt.GetComponent<Image>();
                badgeImg.raycastTarget = false;
                TMP_Text badgeTxt = LabUIHelper.CreateText("BadgeTxt", badgeRt, "SELECT", Vector2.zero, new Vector2(180f, 64f),
                    28f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold, fontAsset);

                _cards[i] = new CardUI
                {
                    id = p.id,
                    bg = cardImg,
                    border = cardBorder,
                    badgeBg = badgeImg,
                    badgeTxt = badgeTxt
                };
            }

            // Footer
            LabUIHelper.CreatePanel("Footer", root, new Vector2(12f, pxH - 76f), new Vector2(pxW - 24f, 64f),
                new Color(0.031f, 0.09f, 0.169f, 1f));
            LabUIHelper.CreateText("FooterTxt", root,
                "SELECT CELESTIAL BODY TO ALTER FIELD • REAL-TIME NET FORCE: F = mg - kx • EXTENSION x = mg/k",
                new Vector2(0f, pxH - 68f), new Vector2(pxW, 48f), 28f, new Color(0.22f, 0.74f, 0.97f),
                TextAlignmentOptions.Center, FontStyles.Bold, fontAsset);
        }

        public void SelectPlanet(string planetId, bool notify = true)
        {
            if (notify && Time.unscaledTime - _lastSelectTime < 0.18f) return;
            _lastSelectTime = Time.unscaledTime;

            PlanetInfo target = Planets[0];
            bool found = false;
            foreach (PlanetInfo p in Planets)
            {
                if (p.id == planetId)
                {
                    target = p;
                    found = true;
                    break;
                }
            }
            if (!found) return;

            SelectedPlanet = target;
            if (_springSim != null)
            {
                _springSim.SetGravity(target.gravity);
            }

            if (_activeBannerTitle != null)
            {
                _activeBannerTitle.text = $"ACTIVE GRAVITY: {target.name.ToUpper()} — {target.gravity:F2} m/s²";
            }
            if (_activeBannerSub != null)
            {
                float ratio = target.gravity / 9.81f;
                _activeBannerSub.text = $"Relative to Earth: {ratio:F2}g  •  Theoretical Extension: x = (m · g) / k  •  Scale: {ratio:F2}×";
            }

            foreach (CardUI c in _cards)
            {
                bool sel = c.id == target.id;
                if (c.bg != null)
                    c.bg.color = sel ? new Color(0.02f, 0.25f, 0.38f, 0.96f) : new Color(0.039f, 0.086f, 0.149f, 0.92f);
                if (c.border != null)
                    c.border.effectColor = sel ? new Color(0.055f, 0.647f, 0.914f, 1f) : new Color(0.118f, 0.227f, 0.373f, 1f);
                if (c.badgeBg != null)
                    c.badgeBg.color = sel ? new Color(0.055f, 0.647f, 0.914f, 1f) : new Color(0.15f, 0.22f, 0.32f, 1f);
                if (c.badgeTxt != null)
                {
                    c.badgeTxt.text = sel ? "ACTIVE" : "SELECT";
                    c.badgeTxt.color = sel ? Color.white : new Color(0.75f, 0.82f, 0.90f);
                }
            }

            if (notify)
            {
                if (_audioSource != null)
                {
                    float baseFreq = target.gravity < 5f ? 440f : (target.gravity < 15f ? 587.33f : 783.99f);
                    _audioSource.PlayOneShot(LabAudioSynthesizer.GetChordClip(baseFreq, baseFreq * 1.5f, baseFreq * 2f, 0.25f));
                }
                OnGravityChanged?.Invoke(target);
            }
        }

        public void ResetToDefault() => SelectPlanet("earth", notify: false);

        private static Texture2D GeneratePlanetTexture(string id)
        {
            const int S = 256;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[S * S];
            float half = S * 0.5f;
            float r = half - 6f;

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = x - half;
                    float dy = y - half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > r)
                    {
                        pixels[y * S + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    float nx = dx / r;
                    float ny = dy / r;
                    float light = Mathf.Clamp01(0.65f - nx * 0.35f + ny * 0.35f);
                    Color baseCol;

                    if (id == "earth")
                    {
                        bool land = Mathf.Sin(nx * 4.2f + ny * 2.1f) * Mathf.Cos(ny * 3.7f - nx) > 0.18f;
                        bool cloud = Mathf.Sin(nx * 7f - ny * 4f) > 0.68f;
                        baseCol = cloud ? Color.white : (land ? new Color(0.063f, 0.725f, 0.506f) : new Color(0.114f, 0.306f, 0.847f));
                    }
                    else if (id == "moon")
                    {
                        float crater = Mathf.Sin(nx * 9f) * Mathf.Cos(ny * 9f);
                        baseCol = crater > 0.55f ? new Color(0.4f, 0.45f, 0.52f) : new Color(0.78f, 0.82f, 0.88f);
                    }
                    else if (id == "mars")
                    {
                        baseCol = ny > 0.72f
                            ? new Color(0.96f, 0.97f, 0.98f)
                            : (Mathf.Sin(nx * 5f + ny * 3f) > 0.3f ? new Color(0.55f, 0.16f, 0.14f) : new Color(0.86f, 0.24f, 0.2f));
                    }
                    else
                    {
                        float band = Mathf.Sin(ny * 10f);
                        bool redSpot = ((nx - 0.28f) * (nx - 0.28f) / 0.08f + (ny + 0.25f) * (ny + 0.25f) / 0.03f) < 1f;
                        baseCol = redSpot ? new Color(0.75f, 0.15f, 0.12f) : (band > 0 ? new Color(0.95f, 0.86f, 0.65f) : new Color(0.76f, 0.43f, 0.12f));
                    }

                    Color lit = baseCol * Mathf.Lerp(0.32f, 1.15f, light);
                    lit.a = 1f;
                    pixels[y * S + x] = lit;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}
