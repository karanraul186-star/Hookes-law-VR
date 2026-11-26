using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

namespace HookesLawLab.UI
{
    /// <summary>
    /// Shared World-Space UI factory ensuring consistent dark sci-fi laboratory styling,
    /// crisp TextMeshPro typography, reduced cyan glare, non-blocking decorative elements,
    /// and both VR (TrackedDeviceGraphicRaycaster + VRUIButtonFeedback) & Mouse (GraphicRaycaster) support.
    /// </summary>
    public static class LabUIHelper
    {
        public static readonly Color SubtleCyanBorder = new Color(0.031f, 0.569f, 0.698f, 0.92f); // #0891b2 refined non-glare cyan

        public static Canvas CreateWorldCanvas(
            string name,
            Transform parent,
            Vector3 localPos,
            Quaternion localRot,
            float widthMeters,
            float heightMeters,
            int pixelWidth,
            int pixelHeight,
            bool interactive = true)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(pixelWidth, pixelHeight);
            rt.localScale = new Vector3(widthMeters / pixelWidth, heightMeters / pixelHeight, 1f);

            if (interactive)
            {
                GraphicRaycaster gr = go.AddComponent<GraphicRaycaster>();
                gr.ignoreReversedGraphics = true;

                TrackedDeviceGraphicRaycaster xrRay = go.AddComponent<TrackedDeviceGraphicRaycaster>();
                xrRay.ignoreReversedGraphics = true;
                xrRay.blockingMask = UnityEngine.Physics.DefaultRaycastLayers;
            }

            return canvas;
        }

        public static RectTransform CreatePanel(
            string name,
            Transform parent,
            Vector2 anchoredPos,
            Vector2 size,
            Color bgColor,
            Color? borderColor = null,
            float borderThickness = 2f)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(anchoredPos.x, -anchoredPos.y);
            rt.sizeDelta = size;

            Image img = go.AddComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = false; // Decorative panels never block button raycasts

            if (borderColor.HasValue)
            {
                Outline outline = go.AddComponent<Outline>();
                outline.effectColor = borderColor.Value;
                outline.effectDistance = new Vector2(borderThickness, -borderThickness);
            }

            return rt;
        }

        public static RectTransform CreateStretchPanel(string name, Transform parent, Color bgColor, Color? borderColor = null, float borderThickness = 2f)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = go.AddComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = false; // Background never steals raycasts from child buttons

            if (borderColor.HasValue)
            {
                Outline outline = go.AddComponent<Outline>();
                outline.effectColor = borderColor.Value;
                outline.effectDistance = new Vector2(borderThickness, -borderThickness);
            }

            return rt;
        }

        public static TMP_Text CreateText(
            string name,
            Transform parent,
            string content,
            Vector2 topLeftPos,
            Vector2 size,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            FontStyles style = FontStyles.Normal,
            TMP_FontAsset fontAsset = null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(topLeftPos.x, -topLeftPos.y);
            rt.sizeDelta = size;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.fontStyle = style;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.extraPadding = true;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static Button CreateButton(
            string name,
            Transform parent,
            string label,
            Vector2 topLeftPos,
            Vector2 size,
            Color normalColor,
            Color hoverColor,
            Color borderColor,
            float fontSize,
            Action onClick,
            out TMP_Text labelComponent,
            TMP_FontAsset fontAsset = null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(topLeftPos.x, -topLeftPos.y);
            rt.sizeDelta = size;

            Image img = go.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = true; // Entire visible button surface is interactive

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = borderColor;
            outline.effectDistance = new Vector2(2.5f, -2.5f);

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = normalColor;
            cb.highlightedColor = hoverColor;
            cb.pressedColor = normalColor * 0.78f;
            cb.selectedColor = hoverColor;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;

            float lastClick = -10f;
            if (onClick != null)
            {
                btn.onClick.AddListener(() =>
                {
                    if (Time.unscaledTime - lastClick < 0.20f) return;
                    lastClick = Time.unscaledTime;
                    onClick();
                });
            }

            go.AddComponent<VRUIButtonFeedback>();

            GameObject txtGo = new GameObject("Label");
            txtGo.transform.SetParent(go.transform, false);
            RectTransform trt = txtGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 6f);
            trt.offsetMax = new Vector2(-10f, -6f);

            TextMeshProUGUI tmp = txtGo.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.extraPadding = true;
            tmp.raycastTarget = false;
            labelComponent = tmp;

            return btn;
        }

        /// <summary>
        /// Computes a local-space rotation and outward offset for a World-Space Canvas mounted on a GLB screen mesh,
        /// ensuring the UI faces into the room toward <paramref name="lookFromWorldPos"/>.
        /// </summary>
        public static void ComputeScreenOverlayPose(
            Transform meshTransform,
            Mesh mesh,
            Vector3 lookFromWorldPos,
            out Vector3 localCenter,
            out Quaternion localRotation,
            out float widthMeters,
            out float heightMeters)
        {
            Bounds b = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, new Vector3(1.6f, 0.01f, 0.9f));
            Vector3 size = b.size;

            int thinAxis = 1;
            if (size.x <= size.y && size.x <= size.z) thinAxis = 0;
            else if (size.z <= size.x && size.z <= size.y) thinAxis = 2;

            Vector3 localNormal = thinAxis == 0 ? Vector3.right : (thinAxis == 1 ? Vector3.up : Vector3.forward);
            Vector3 worldCenter = meshTransform.TransformPoint(b.center);
            Vector3 worldNormal = meshTransform.TransformDirection(localNormal).normalized;
            if (Vector3.Dot(worldNormal, lookFromWorldPos - worldCenter) < 0f)
            {
                localNormal = -localNormal;
                worldNormal = -worldNormal;
            }

            Vector3 worldUp = Vector3.up;
            Vector3 worldRight = Vector3.Cross(worldUp, -worldNormal);
            if (worldRight.sqrMagnitude < 0.001f) worldRight = Vector3.right;
            worldRight.Normalize();
            Vector3 correctedUp = Vector3.Cross(-worldNormal, worldRight).normalized;

            Quaternion worldRot = Quaternion.LookRotation(-worldNormal, correctedUp);
            localRotation = Quaternion.Inverse(meshTransform.rotation) * worldRot;

            Vector3 axisX = meshTransform.TransformVector(Vector3.right * size.x);
            Vector3 axisY = meshTransform.TransformVector(Vector3.up * size.y);
            Vector3 axisZ = meshTransform.TransformVector(Vector3.forward * size.z);
            widthMeters = Mathf.Abs(Vector3.Dot(axisX, worldRight)) + Mathf.Abs(Vector3.Dot(axisY, worldRight)) + Mathf.Abs(Vector3.Dot(axisZ, worldRight));
            heightMeters = Mathf.Abs(Vector3.Dot(axisX, correctedUp)) + Mathf.Abs(Vector3.Dot(axisY, correctedUp)) + Mathf.Abs(Vector3.Dot(axisZ, correctedUp));

            Vector3 lossy = meshTransform.lossyScale;
            float avgScale = (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;
            if (avgScale > 0.0001f)
            {
                widthMeters /= avgScale;
                heightMeters /= avgScale;
            }

            float thickness = thinAxis == 0 ? size.x : (thinAxis == 1 ? size.y : size.z);
            localCenter = b.center + localNormal * (thickness * 0.5f + 0.005f);
        }
    }
}
