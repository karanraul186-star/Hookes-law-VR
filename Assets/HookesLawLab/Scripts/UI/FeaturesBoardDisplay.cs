using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace HookesLawLab.UI
{
    /// <summary>
    /// Wall-mounted "EXPLORE THE PHYSICS LAB" features board positioned on the empty grey wall
    /// to the left of the Theory/YouTube Lecture Board, cleanly framed between the vertical cyan light strips.
    /// Information-only World-Space display with large, readable VR typography.
    /// </summary>
    public class FeaturesBoardDisplay : MonoBehaviour
    {
        private struct FeatureSection
        {
            public string number;
            public string heading;
            public string description;
        }

        private static readonly FeatureSection[] Sections = new FeatureSection[]
        {
            new FeatureSection
            {
                number = "01",
                heading = "01 - WEIGHTS & SPRING",
                description = "Pick up different masses and attach them to the spring. Observe the stretch and record your measurements."
            },
            new FeatureSection
            {
                number = "02",
                heading = "02 - INTERACTIVE GRAPH",
                description = "View your recorded measurements on the force-extension graph. Explore how increasing force changes the spring's extension."
            },
            new FeatureSection
            {
                number = "03",
                heading = "03 - PLANET GRAVITY",
                description = "Switch between Earth, Mars, and Jupiter. Keep the same mass attached and compare how gravity affects the spring."
            },
            new FeatureSection
            {
                number = "04",
                heading = "04 - TALK TO THE TEACHER",
                description = "Use voice conversation to ask the virtual teacher questions about Hooke's Law and the experiment."
            },
            new FeatureSection
            {
                number = "05",
                heading = "05 - THEORY & YOUTUBE",
                description = "Visit the theory board to read equations and explanations. Select YouTube to watch the linked educational video."
            }
        };

        private GameObject _boardHost;

        public GameObject BoardHost => _boardHost;

        public void Initialize(
            GameObject anamModelRoot,
            GameObject theoryBoardScreenObj,
            Vector3 viewerRefPos,
            TMP_FontAsset fontAsset = null)
        {
            // Reuse/replace any existing FeaturesBoardHost so duplicates are never created
            if (_boardHost != null)
            {
                Destroy(_boardHost);
                _boardHost = null;
            }
            GameObject strayExisting = GameObject.Find("FeaturesBoardHost");
            if (strayExisting != null)
            {
                Destroy(strayExisting);
            }

            if (theoryBoardScreenObj == null) return;

            ComputeWallPlacement(
                anamModelRoot,
                theoryBoardScreenObj,
                viewerRefPos,
                out Vector3 boardPos,
                out Quaternion boardRot,
                out float dispW,
                out float dispH);

            BuildFeaturesBoardUI(boardPos, boardRot, dispW, dispH, fontAsset);
        }

        private static void ComputeWallPlacement(
            GameObject anamModelRoot,
            GameObject theoryBoardScreenObj,
            Vector3 viewerRefPos,
            out Vector3 boardPos,
            out Quaternion boardRot,
            out float dispW,
            out float dispH)
        {
            MeshFilter mf = theoryBoardScreenObj.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;

            LabUIHelper.ComputeScreenOverlayPose(
                theoryBoardScreenObj.transform,
                mesh,
                viewerRefPos,
                out Vector3 localCenter,
                out Quaternion localRot,
                out _,
                out _);

            Vector3 refWorldCenter = theoryBoardScreenObj.transform.TransformPoint(localCenter);
            boardRot = theoryBoardScreenObj.transform.rotation * localRot;

            Vector3 outward = (boardRot * -Vector3.forward).normalized; // Points from wall into room
            Vector3 screenLeft = -(boardRot * Vector3.right).normalized; // Points LEFT when looking at wall from room

            // Collect vertical cyan light strips and wall inset panels on the SAME wall surface
            var leftStrips = new List<(float leftDist, float outwardProj, float minOutward)>();
            float wallInsetSurfaceOutward = float.MinValue;

            if (anamModelRoot != null)
            {
                foreach (Transform t in anamModelRoot.GetComponentsInChildren<Transform>(true))
                {
                    Renderer r = t.GetComponent<Renderer>();
                    if (r == null) continue;

                    string clean = t.gameObject.name.ToLowerInvariant().Replace('_', ' ');
                    Bounds b = r.bounds;
                    Vector3 delta = b.center - refWorldCenter;
                    float outDelta = Vector3.Dot(delta, outward);
                    float leftDist = Vector3.Dot(delta, screenLeft);

                    if (clean.StartsWith("side wall blue strip") && Mathf.Abs(outDelta) < 0.45f && leftDist > 0.25f)
                    {
                        leftStrips.Add((leftDist, Vector3.Dot(b.center, outward), 0f));
                    }
                    else if (clean.StartsWith("side wall inset") && Mathf.Abs(outDelta) < 0.45f && leftDist > 1.0f)
                    {
                        // Front face of inset along outward direction
                        float halfExtentOut =
                            Mathf.Abs(b.extents.x * outward.x) +
                            Mathf.Abs(b.extents.y * outward.y) +
                            Mathf.Abs(b.extents.z * outward.z);
                        float frontOut = Vector3.Dot(b.center, outward) + halfExtentOut;
                        if (frontOut > wallInsetSurfaceOutward)
                        {
                            wallInsetSurfaceOutward = frontOut;
                        }
                    }
                }
            }

            leftStrips.Sort((a, b) => a.leftDist.CompareTo(b.leftDist));

            // Default fallback aligned with the grey wall bay to the left of the Theory Board
            float centerLeftDist = 3.515f;
            dispW = 1.66f;
            dispH = 1.72f;
            const float centerY = 1.78f;

            // If wall cyan strips are found (strip.003 behind theory board edge, strip.002 & strip.001 framing the open bay),
            // center neatly inside the bay [strip.002 .. strip.001] while keeping both cyan strips completely visible.
            if (leftStrips.Count >= 3)
            {
                float stripNear = leftStrips[1].leftDist;
                float stripFar = leftStrips[2].leftDist;
                float bayWidth = Mathf.Abs(stripFar - stripNear);
                if (bayWidth >= 1.35f)
                {
                    centerLeftDist = 0.5f * (stripNear + stripFar);
                    // Preserve cyan strip width (~0.025m) + comfortable breathing margin (~0.02m each side)
                    dispW = Mathf.Clamp(bayWidth - 0.085f, 1.38f, 1.66f);
                }
            }

            // Compute flush wall offset (+1.9cm proud of inset wall face so backing clears wall with zero flickering)
            Vector3 lateralAnchor = refWorldCenter + screenLeft * centerLeftDist;
            float baseOutwardProj = wallInsetSurfaceOutward > float.MinValue
                ? wallInsetSurfaceOutward
                : Vector3.Dot(refWorldCenter, outward) - 0.047f; // Wall monitor screen sits ~4.7cm proud of wall inset

            float currentOutwardProj = Vector3.Dot(lateralAnchor, outward);
            float backingFrontTarget = baseOutwardProj + 0.019f; // Backing front ~1.9cm off grey wall inset; backing back ~0.3cm clear
            boardPos = lateralAnchor + outward * (backingFrontTarget - currentOutwardProj);
            boardPos.y = centerY;
        }

        private void BuildFeaturesBoardUI(
            Vector3 boardPos,
            Quaternion boardRot,
            float dispW,
            float dispH,
            TMP_FontAsset fontAsset)
        {
            _boardHost = new GameObject("FeaturesBoardHost");
            _boardHost.transform.SetParent(transform, false);
            _boardHost.transform.position = boardPos;
            _boardHost.transform.rotation = boardRot;

            // Thin dark-navy chassis flush against the wall (no collider so rays/locomotion never blocked)
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material chassisMat = new Material(litShader) { color = new Color(0.047f, 0.075f, 0.137f) };
            chassisMat.SetFloat("_Metallic", 0.7f);
            chassisMat.SetFloat("_Smoothness", 0.5f);

            GameObject chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chassis.name = "FeaturesBoardBacking";
            Collider col = chassis.GetComponent<Collider>();
            if (col != null) Destroy(col);
            chassis.transform.SetParent(_boardHost.transform, false);
            chassis.transform.localPosition = new Vector3(0f, 0f, 0.008f);
            chassis.transform.localScale = new Vector3(dispW + 0.03f, dispH + 0.03f, 0.016f);
            MeshRenderer cmr = chassis.GetComponent<MeshRenderer>();
            cmr.sharedMaterial = chassisMat;
            cmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // High-resolution World-Space Canvas (~1 px = 1 mm)
            const int W = 1660;
            const int H = 1720;

            Canvas canvas = LabUIHelper.CreateWorldCanvas(
                "FeaturesBoardCanvas",
                _boardHost.transform,
                new Vector3(0f, 0f, -0.002f),
                Quaternion.identity,
                dispW,
                dispH,
                W,
                H,
                interactive: false);

            // Outer dark-navy board with subtle cyan frame matching the Theory Board
            RectTransform root = LabUIHelper.CreateStretchPanel(
                "FeaturesBoardRoot",
                canvas.transform,
                new Color(0.02f, 0.047f, 0.098f, 1f),
                LabUIHelper.SubtleCyanBorder,
                4f);

            const float padX = 44f;
            const float contentW = W - (padX * 2f);

            // Header section
            LabUIHelper.CreatePanel(
                "HeaderPanel",
                root,
                new Vector2(padX, 28f),
                new Vector2(contentW, 184f),
                new Color(0.035f, 0.090f, 0.176f, 1f),
                new Color(0.031f, 0.569f, 0.698f, 0.55f),
                2f);

            LabUIHelper.CreateText(
                "FeaturesTitle",
                root,
                "EXPLORE THE PHYSICS LAB",
                new Vector2(padX + 24f, 48f),
                new Vector2(contentW - 48f, 76f),
                60f,
                Color.white,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                fontAsset);

            LabUIHelper.CreateText(
                "FeaturesSubtitle",
                root,
                "Five activities to discover, experiment, and learn.",
                new Vector2(padX + 24f, 134f),
                new Vector2(contentW - 48f, 52f),
                32f,
                new Color(0.22f, 0.74f, 0.97f),
                TextAlignmentOptions.Center,
                FontStyles.Normal,
                fontAsset);

            // Five spacious horizontal rows (all visible at once without scrolling or tabs)
            const float startY = 236f;
            const float rowH = 268f;
            const float rowGap = 22f;

            for (int i = 0; i < Sections.Length; i++)
            {
                FeatureSection sec = Sections[i];
                float rowY = startY + i * (rowH + rowGap);

                RectTransform rowPanel = LabUIHelper.CreatePanel(
                    $"FeatureRow_{sec.number}",
                    root,
                    new Vector2(padX, rowY),
                    new Vector2(contentW, rowH),
                    new Color(0.027f, 0.071f, 0.141f, 0.98f),
                    LabUIHelper.SubtleCyanBorder,
                    2.5f);

                // Left numbered icon badge
                const float badgeSize = 148f;
                float badgeY = (rowH - badgeSize) * 0.5f;
                RectTransform badge = LabUIHelper.CreatePanel(
                    $"Badge_{sec.number}",
                    rowPanel,
                    new Vector2(30f, badgeY),
                    new Vector2(badgeSize, badgeSize),
                    new Color(0.047f, 0.133f, 0.251f, 1f),
                    new Color(0.22f, 0.74f, 0.97f, 0.92f),
                    2.5f);

                LabUIHelper.CreateText(
                    $"BadgeNum_{sec.number}",
                    badge,
                    sec.number,
                    new Vector2(0f, 34f),
                    new Vector2(badgeSize, 80f),
                    56f,
                    new Color(0.22f, 0.74f, 0.97f),
                    TextAlignmentOptions.Center,
                    FontStyles.Bold,
                    fontAsset);

                // Right text column: cyan heading + crisp white description
                const float textX = 210f;
                float textW = contentW - textX - 32f;

                LabUIHelper.CreateText(
                    $"Heading_{sec.number}",
                    rowPanel,
                    sec.heading,
                    new Vector2(textX, 34f),
                    new Vector2(textW, 50f),
                    35f,
                    new Color(0.22f, 0.74f, 0.97f),
                    TextAlignmentOptions.Left,
                    FontStyles.Bold,
                    fontAsset);

                LabUIHelper.CreateText(
                    $"Desc_{sec.number}",
                    rowPanel,
                    sec.description,
                    new Vector2(textX, 94f),
                    new Vector2(textW, 146f),
                    30f,
                    new Color(0.95f, 0.97f, 0.99f),
                    TextAlignmentOptions.TopLeft,
                    FontStyles.Normal,
                    fontAsset);
            }
        }
    }
}
