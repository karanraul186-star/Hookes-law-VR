using UnityEngine;
using TMPro;
using HookesLawLab.Physics;

namespace HookesLawLab.UI
{
    /// <summary>
    /// Synchronizes Display 1 ('Reader value') and Display 2 ('Meter') with the live Hooke's Law extension (in metres).
    /// </summary>
    public class ExtensionDisplays : MonoBehaviour
    {
        private SpringSimulation _springSim;
        private SpringVisual _springVisual;

        private TMP_Text _display1Text;
        private TMP_Text _display2Text;
        private string _lastRenderedStr;

        public void Initialize(SpringSimulation sim, SpringVisual visual, GameObject display1Obj, GameObject display2Obj,
            Vector3 viewerRefPos, TMP_FontAsset fontAsset = null)
        {
            _springSim = sim;
            _springVisual = visual;

            if (display1Obj != null)
            {
                _display1Text = CreateDisplayOverlay(display1Obj, "Display1_ReaderOverlay", false, viewerRefPos, fontAsset);
            }

            if (display2Obj != null)
            {
                _display2Text = CreateDisplayOverlay(display2Obj, "Display2_MeterOverlay", true, viewerRefPos, fontAsset);
            }

            UpdateDisplays(0f, force: true);
        }

        private TMP_Text CreateDisplayOverlay(GameObject targetObj, string canvasName, bool showHeader,
            Vector3 viewerRefPos, TMP_FontAsset fontAsset)
        {
            MeshFilter mf = targetObj.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;

            LabUIHelper.ComputeScreenOverlayPose(
                targetObj.transform,
                mesh,
                viewerRefPos,
                out Vector3 localCenter,
                out Quaternion localRot,
                out float w,
                out float h);

            int pxW = showHeader ? 1136 : 1024;
            int pxH = showHeader ? 496 : 512;

            Canvas canvas = LabUIHelper.CreateWorldCanvas(
                canvasName,
                targetObj.transform,
                localCenter,
                localRot,
                Mathf.Max(0.05f, w),
                Mathf.Max(0.03f, h),
                pxW,
                pxH,
                interactive: false);

            RectTransform bg = LabUIHelper.CreateStretchPanel(
                "LCD",
                canvas.transform,
                new Color(0.02f, 0.063f, 0.102f, 1f),
                new Color(0.051f, 0.22f, 0.36f, 1f),
                6f);

            if (showHeader)
            {
                LabUIHelper.CreateText(
                    "Header",
                    bg,
                    "EXTENSION",
                    new Vector2(0f, 36f),
                    new Vector2(pxW, 130f),
                    100f,
                    new Color(0.22f, 0.74f, 0.97f),
                    TextAlignmentOptions.Center,
                    FontStyles.Bold,
                    fontAsset);

                return LabUIHelper.CreateText(
                    "Value",
                    bg,
                    "0.000 m",
                    new Vector2(0f, 210f),
                    new Vector2(pxW, 240f),
                    156f,
                    Color.white,
                    TextAlignmentOptions.Center,
                    FontStyles.Bold,
                    fontAsset);
            }

            return LabUIHelper.CreateText(
                "Value",
                bg,
                "0.000 m",
                new Vector2(0f, 136f),
                new Vector2(pxW, 240f),
                184f,
                new Color(0.35f, 0.92f, 1f),
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                fontAsset);
        }

        public void UpdateDisplays(float? extensionOverride = null, bool force = false)
        {
            float ext = extensionOverride.HasValue
                ? Mathf.Max(0f, extensionOverride.Value)
                : (_springVisual != null
                    ? Mathf.Max(0f, _springVisual.CurrentLength - _springVisual.naturalLength)
                    : (_springSim != null ? Mathf.Max(0f, _springSim.Displacement) : 0f));

            string displayStr = $"{ext:F3} m";
            if (!force && displayStr == _lastRenderedStr) return;
            _lastRenderedStr = displayStr;

            if (_display1Text != null) _display1Text.text = displayStr;
            if (_display2Text != null) _display2Text.text = displayStr;
        }

        public void ResetDisplays()
        {
            UpdateDisplays(0f, force: true);
        }
    }
}
