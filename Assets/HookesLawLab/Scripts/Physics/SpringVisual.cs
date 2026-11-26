using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace HookesLawLab.Physics
{
    /// <summary>
    /// Procedural 3D helical spring mesh with real-time deformation, bottom hanger assembly,
    /// synchronized orange pointer, laser ruler marker, 3D measurement tag, force vector arrows,
    /// and procedural centimeter scale texture on Stand_rod_centimetre_scale.
    /// </summary>
    public class SpringVisual : MonoBehaviour
    {
        [Header("Spring Geometry")]
        public Vector3 topAnchor = new Vector3(0.390f, 3.226f, -0.240f);
        public float naturalLength = 0.672f;
        public int coils = 14;
        public float radius = 0.0565f;
        public float wireRadius = 0.0065f;
        public int tubularSegments = 180;
        public int radialSegments = 8;

        public float CurrentLength { get; private set; }
        public Transform SpringAttachmentPoint { get; private set; }
        public Transform HangerSnapPoint { get; private set; }
        public Transform AttachedMassContainer { get; private set; }

        private Transform _orangePointer;
        private Vector3 _orangePointerInitialPos;
        private Renderer _rulerRenderer;
        private MeshFilter _rulerMeshFilter;

        private Mesh _springMesh;
        private Vector3[] _spinePoints;
        private Vector3[] _vertices;
        private Vector3[] _normals;
        private int[] _triangles;
        private Material _springMat;
        private Material _hangerMat;
        private Material _snapRingMat;
        private Transform _snapRingTransform;
        private Transform _hangerGroup;

        private LineRenderer _laserLine;
        private Transform _laserDot;
        private TMP_Text _pointerTagText;
        private Vector3 _rulerDirection = Vector3.left;
        private float _rulerDistance = 0.975f;

        private Transform _arrowRestoring;
        private Transform _arrowRestoringShaft;
        private Transform _arrowRestoringHead;
        private Transform _arrowGravity;
        private Transform _arrowGravityShaft;
        private Transform _arrowGravityHead;
        private GameObject _vectorGroup;

        private static readonly Color NormalSpringColor = new Color(0.816f, 0.878f, 0.925f);
        private static readonly Color ExceededSpringColor = new Color(1f, 0.333f, 0.467f);
        private static readonly Color ExceededEmissionColor = new Color(0.2f, 0f, 0.067f);

        public void Initialize(Vector3 anchorWorldPos, Transform orangePointer, GameObject rulerObject, TMP_FontAsset fontAsset = null)
        {
            topAnchor = anchorWorldPos;
            CurrentLength = naturalLength;
            _orangePointer = orangePointer;
            if (_orangePointer != null)
            {
                _orangePointerInitialPos = _orangePointer.position;
            }

            if (rulerObject != null)
            {
                _rulerRenderer = rulerObject.GetComponent<Renderer>();
                _rulerMeshFilter = rulerObject.GetComponent<MeshFilter>();
            }

            CreateMaterials();
            BuildSpringMesh();
            BuildHangerAssembly();
            ResolveRulerGeometry();
            BuildLaserMarker(fontAsset);
            BuildForceVectors();
            BuildRulerScale();

            SetDisplacement(0f, default);
        }

        private void CreateMaterials()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

            _springMat = new Material(litShader);
            _springMat.color = NormalSpringColor;
            _springMat.SetFloat("_Metallic", 0.92f);
            _springMat.SetFloat("_Smoothness", 0.82f);
            _springMat.EnableKeyword("_EMISSION");

            _hangerMat = new Material(litShader);
            _hangerMat.color = new Color(0.58f, 0.64f, 0.72f);
            _hangerMat.SetFloat("_Metallic", 0.85f);
            _hangerMat.SetFloat("_Smoothness", 0.75f);

            _snapRingMat = new Material(unlitShader);
            _snapRingMat.color = new Color(0f, 0.9f, 1f, 0.55f);
            MakeMaterialTransparent(_snapRingMat);
        }

        private static void MakeMaterialTransparent(Material mat)
        {
            if (mat == null) return;
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
        }

        private Vector3 GetPointOnSpring(float t, float length)
        {
            float r = radius;
            if (t < 0.04f)
            {
                float p = t / 0.04f;
                float angle = p * Mathf.PI * 0.5f;
                return new Vector3(
                    Mathf.Sin(angle) * r * 0.5f,
                    -p * 0.02f,
                    (1f - Mathf.Cos(angle)) * r * 0.5f
                );
            }
            if (t > 0.96f)
            {
                float p = (t - 0.96f) / 0.04f;
                float angle = (1f - p) * Mathf.PI * 0.5f;
                return new Vector3(
                    Mathf.Sin(angle) * r * 0.5f,
                    -length + (1f - p) * 0.02f,
                    Mathf.Cos(angle) * r * 0.5f
                );
            }
            {
                float p = (t - 0.04f) / 0.92f;
                float theta = p * coils * Mathf.PI * 2f;
                float y = -0.02f - p * (length - 0.04f);
                return new Vector3(
                    Mathf.Cos(theta) * r,
                    y,
                    Mathf.Sin(theta) * r
                );
            }
        }

        private void BuildSpringMesh()
        {
            GameObject springGo = new GameObject("ProceduralHelicalSpring");
            springGo.transform.SetParent(transform, false);
            springGo.transform.position = topAnchor;

            MeshFilter mf = springGo.AddComponent<MeshFilter>();
            MeshRenderer mr = springGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _springMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _springMesh = new Mesh { name = "HelicalSpringMesh" };
            _springMesh.MarkDynamic();
            mf.sharedMesh = _springMesh;

            int ringCount = tubularSegments + 1;
            int vertsPerRing = radialSegments + 1;
            _spinePoints = new Vector3[ringCount];
            _vertices = new Vector3[ringCount * vertsPerRing];
            _normals = new Vector3[ringCount * vertsPerRing];
            _triangles = new int[tubularSegments * radialSegments * 6];

            int triIdx = 0;
            for (int i = 0; i < tubularSegments; i++)
            {
                for (int j = 0; j < radialSegments; j++)
                {
                    int a = i * vertsPerRing + j;
                    int b = (i + 1) * vertsPerRing + j;
                    int c = (i + 1) * vertsPerRing + (j + 1);
                    int d = i * vertsPerRing + (j + 1);

                    _triangles[triIdx++] = a;
                    _triangles[triIdx++] = b;
                    _triangles[triIdx++] = d;
                    _triangles[triIdx++] = b;
                    _triangles[triIdx++] = c;
                    _triangles[triIdx++] = d;
                }
            }

            UpdateSpringGeometry(naturalLength);
        }

        private void UpdateSpringGeometry(float length)
        {
            int ringCount = tubularSegments + 1;
            int vertsPerRing = radialSegments + 1;

            for (int i = 0; i < ringCount; i++)
            {
                float t = (float)i / tubularSegments;
                _spinePoints[i] = GetPointOnSpring(t, length);
            }

            for (int i = 0; i < ringCount; i++)
            {
                Vector3 tangent;
                if (i == 0) tangent = (_spinePoints[1] - _spinePoints[0]).normalized;
                else if (i == ringCount - 1) tangent = (_spinePoints[i] - _spinePoints[i - 1]).normalized;
                else tangent = (_spinePoints[i + 1] - _spinePoints[i - 1]).normalized;

                if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.down;

                Vector3 upRef = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
                Vector3 normal = Vector3.Cross(tangent, upRef).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal).normalized;

                for (int j = 0; j < vertsPerRing; j++)
                {
                    float angle = ((float)j / radialSegments) * Mathf.PI * 2f;
                    Vector3 radialDir = Mathf.Cos(angle) * normal + Mathf.Sin(angle) * binormal;
                    int vIdx = i * vertsPerRing + j;
                    _vertices[vIdx] = _spinePoints[i] + radialDir * wireRadius;
                    _normals[vIdx] = radialDir;
                }
            }

            _springMesh.vertices = _vertices;
            _springMesh.normals = _normals;
            if (_springMesh.GetIndexCount(0) == 0)
            {
                _springMesh.triangles = _triangles;
            }
            _springMesh.RecalculateBounds();
        }

        private void BuildHangerAssembly()
        {
            GameObject hangerGo = new GameObject("HangerAssembly");
            hangerGo.transform.SetParent(transform, false);
            _hangerGroup = hangerGo.transform;

            // 1. Stem
            CreatePrimitivePart("HangerStem", PrimitiveType.Cylinder, _hangerGroup,
                new Vector3(0f, -0.03f, 0f), new Vector3(0.01f, 0.03f, 0.01f), Quaternion.identity, _hangerMat);

            // 2. Curved hook neck
            CreatePrimitivePart("HangerNeck", PrimitiveType.Cylinder, _hangerGroup,
                new Vector3(0f, -0.09f, 0f), new Vector3(0.009f, 0.035f, 0.009f), Quaternion.identity, _hangerMat);

            // 3. Crossbar pin & dedicated SpringAttachmentPoint
            GameObject pin = CreatePrimitivePart("HangerPin", PrimitiveType.Cylinder, _hangerGroup,
                new Vector3(0f, -0.114f, 0f), new Vector3(0.01f, 0.045f, 0.01f), Quaternion.Euler(0f, 0f, 90f), _hangerMat);
            HangerSnapPoint = pin.transform;

            GameObject attachPt = new GameObject("SpringAttachmentPoint");
            attachPt.transform.SetParent(_hangerGroup, false);
            attachPt.transform.localPosition = new Vector3(0f, -0.114f, 0f);
            SpringAttachmentPoint = attachPt.transform;

            // 4. Bottom flange / pan
            CreatePrimitivePart("HangerFlange", PrimitiveType.Cylinder, _hangerGroup,
                new Vector3(0f, -0.14f, 0f), new Vector3(0.064f, 0.004f, 0.064f), Quaternion.identity, _hangerMat);

            // 5. Snap Halo Ring (procedural torus)
            GameObject snapRing = new GameObject("SnapHaloRing");
            snapRing.transform.SetParent(_hangerGroup, false);
            snapRing.transform.localPosition = new Vector3(0f, -0.114f, 0f);
            MeshFilter rf = snapRing.AddComponent<MeshFilter>();
            MeshRenderer rr = snapRing.AddComponent<MeshRenderer>();
            rf.sharedMesh = CreateTorusMesh(0.075f, 0.0035f, 32, 10);
            rr.sharedMaterial = _snapRingMat;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _snapRingTransform = snapRing.transform;

            // 6. Attached Mass Container aligned directly with SpringAttachmentPoint
            GameObject massCont = new GameObject("AttachedMassContainer");
            massCont.transform.SetParent(_hangerGroup, false);
            massCont.transform.localPosition = new Vector3(0f, -0.114f, 0f);
            AttachedMassContainer = massCont.transform;
        }

        private void ResolveRulerGeometry()
        {
            if (_rulerRenderer != null)
            {
                Vector3 rulerCenter = _rulerRenderer.bounds.center;
                Vector3 delta = new Vector3(rulerCenter.x - topAnchor.x, 0f, rulerCenter.z - topAnchor.z);
                float dist = delta.magnitude;
                if (dist > 0.1f)
                {
                    _rulerDirection = delta / dist;
                    _rulerDistance = dist;
                    return;
                }
            }

            // Fallback based on topAnchor sign
            float sign = topAnchor.x >= 0f ? -1f : 1f;
            _rulerDirection = new Vector3(sign, 0f, 0f);
            _rulerDistance = 0.975f;
        }

        private void BuildLaserMarker(TMP_FontAsset fontAsset)
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

            GameObject laserGo = new GameObject("RulerLaserLine");
            laserGo.transform.SetParent(_hangerGroup, false);
            laserGo.transform.localPosition = new Vector3(0f, -0.114f, 0f);

            _laserLine = laserGo.AddComponent<LineRenderer>();
            _laserLine.useWorldSpace = false;
            _laserLine.positionCount = 2;
            _laserLine.startWidth = 0.004f;
            _laserLine.endWidth = 0.004f;
            Material laserMat = new Material(unlitShader);
            laserMat.color = new Color(0f, 0.9f, 1f, 0.85f);
            _laserLine.sharedMaterial = laserMat;
            _laserLine.SetPosition(0, Vector3.zero);
            _laserLine.SetPosition(1, _rulerDirection * _rulerDistance);

            // Laser dot
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "LaserDot";
            Destroy(dot.GetComponent<Collider>());
            dot.transform.SetParent(laserGo.transform, false);
            dot.transform.localPosition = _rulerDirection * _rulerDistance;
            dot.transform.localScale = Vector3.one * 0.016f;
            Material dotMat = new Material(unlitShader);
            dotMat.color = Color.cyan;
            dot.GetComponent<MeshRenderer>().sharedMaterial = dotMat;
            _laserDot = dot.transform;

            // 3D Digital Measurement Tag
            GameObject canvasGo = new GameObject("PointerTagCanvas");
            canvasGo.transform.SetParent(laserGo.transform, false);
            canvasGo.transform.localPosition = _rulerDirection * (_rulerDistance * 0.74f) + new Vector3(0f, 0.055f, 0.03f);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(260f, 96f);
            rt.localScale = Vector3.one * 0.00075f;

            GameObject bgGo = new GameObject("Background");
            bgGo.transform.SetParent(canvasGo.transform, false);
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.059f, 0.09f, 0.165f, 0.92f);
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            Outline outline = bgGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0.9f, 1f, 1f);
            outline.effectDistance = new Vector2(3f, -3f);

            GameObject txtGo = new GameObject("TagText");
            txtGo.transform.SetParent(bgGo.transform, false);
            _pointerTagText = txtGo.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) _pointerTagText.font = fontAsset;
            _pointerTagText.alignment = TextAlignmentOptions.Center;
            _pointerTagText.fontSize = 26f;
            _pointerTagText.fontStyle = FontStyles.Bold;
            _pointerTagText.color = new Color(0f, 0.94f, 1f);
            RectTransform txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(8f, 6f);
            txtRt.offsetMax = new Vector2(-8f, -6f);
        }

        private void BuildForceVectors()
        {
            _vectorGroup = new GameObject("ForceVectors");
            _vectorGroup.transform.SetParent(_hangerGroup, false);
            _vectorGroup.transform.localPosition = new Vector3(0f, -0.114f, 0f);

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

            Material restoreMat = new Material(unlitShader) { color = new Color(0f, 0.9f, 1f) };
            CreateArrow("RestoringForceArrow", _vectorGroup.transform, restoreMat, +1f,
                out _arrowRestoring, out _arrowRestoringShaft, out _arrowRestoringHead);

            Material gravMat = new Material(unlitShader) { color = new Color(0.957f, 0.447f, 0.714f) };
            CreateArrow("GravityForceArrow", _vectorGroup.transform, gravMat, -1f,
                out _arrowGravity, out _arrowGravityShaft, out _arrowGravityHead);
        }

        private void CreateArrow(string arrowName, Transform parent, Material mat, float dirY,
            out Transform root, out Transform shaft, out Transform head)
        {
            GameObject arrowGo = new GameObject(arrowName);
            arrowGo.transform.SetParent(parent, false);
            arrowGo.transform.localPosition = new Vector3(dirY > 0 ? -0.06f : 0.06f, 0f, 0f);
            root = arrowGo.transform;

            GameObject shaftGo = CreatePrimitivePart("Shaft", PrimitiveType.Cylinder, root,
                Vector3.zero, new Vector3(0.008f, 0.05f, 0.008f), Quaternion.identity, mat);
            shaft = shaftGo.transform;

            GameObject headGo = new GameObject("Head");
            headGo.transform.SetParent(root, false);
            MeshFilter mf = headGo.AddComponent<MeshFilter>();
            MeshRenderer mr = headGo.AddComponent<MeshRenderer>();
            mf.sharedMesh = CreateConeMesh(0.014f, 0.03f, 14);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (dirY < 0) headGo.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            head = headGo.transform;
        }

        private void UpdateArrowLength(Transform root, Transform shaft, Transform head, float dirY, float length)
        {
            if (root == null) return;
            bool visible = length > 0.01f;
            root.gameObject.SetActive(visible);
            if (!visible) return;

            float headLen = Mathf.Min(0.03f, length * 0.4f);
            float shaftLen = Mathf.Max(0.002f, length - headLen);
            shaft.localScale = new Vector3(0.008f, shaftLen * 0.5f, 0.008f);
            shaft.localPosition = new Vector3(0f, dirY * shaftLen * 0.5f, 0f);
            head.localPosition = new Vector3(0f, dirY * (shaftLen + headLen * 0.5f), 0f);
            head.localScale = new Vector3(1f, headLen / 0.03f, 1f);
        }

        private void BuildRulerScale()
        {
            if (_rulerRenderer == null || _rulerMeshFilter == null || _rulerMeshFilter.sharedMesh == null) return;

            Mesh mesh = Instantiate(_rulerMeshFilter.sharedMesh);
            _rulerMeshFilter.sharedMesh = mesh;

            Vector3[] verts = mesh.vertices;
            Vector2[] uvs = new Vector2[verts.Length];

            // Compute world-space Y min/max of ruler vertices so V=1 is top and V=0 is bottom
            Transform rt = _rulerRenderer.transform;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minX = float.MaxValue, maxX = float.MinValue;

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 wp = rt.TransformPoint(verts[i]);
                if (wp.y < minY) minY = wp.y;
                if (wp.y > maxY) maxY = wp.y;
                if (wp.x < minX) minX = wp.x;
                if (wp.x > maxX) maxX = wp.x;
            }

            float spanY = Mathf.Max(0.1f, maxY - minY);
            float spanX = Mathf.Max(0.05f, maxX - minX);
            bool xFlipped = topAnchor.x > 0f;

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 wp = rt.TransformPoint(verts[i]);
                float u = Mathf.Clamp01((wp.x - minX) / spanX);
                if (xFlipped) u = 1f - u;
                float v = Mathf.Clamp01((wp.y - minY) / spanY);
                uvs[i] = new Vector2(u, v);
            }
            mesh.uv = uvs;

            // Zero extension reference Y matches unloaded hanger pointer (~2.44m)
            float zeroWorldY = topAnchor.y - naturalLength - 0.114f;
            Texture2D tex = GenerateRulerTexture(minY, maxY, zeroWorldY);

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            Material rulerMat = new Material(unlitShader);
            rulerMat.mainTexture = tex;
            _rulerRenderer.sharedMaterial = rulerMat;
        }

        private Texture2D GenerateRulerTexture(float worldMinY, float worldMaxY, float zeroWorldY)
        {
            const int W = 256;
            const int H = 2048;
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            Color32[] pixels = new Color32[W * H];

            Color32 bg = new Color32(248, 250, 252, 255);
            Color32 border = new Color32(15, 23, 42, 255);
            Color32 zeroBlue = new Color32(2, 132, 199, 255);
            Color32 tickDark = new Color32(15, 23, 42, 255);
            Color32 tickMed = new Color32(51, 65, 85, 255);
            Color32 tickLight = new Color32(148, 163, 184, 255);

            for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

            // Outer border
            DrawRect(pixels, W, H, 0, 0, W, 4, border);
            DrawRect(pixels, W, H, 0, H - 4, W, 4, border);
            DrawRect(pixels, W, H, 0, 0, 4, H, border);
            DrawRect(pixels, W, H, W - 4, 0, 4, H, border);

            // Header band at top (high Y in texture)
            DrawRect(pixels, W, H, 8, H - 150, W - 16, 142, border);

            float spanM = Mathf.Max(0.1f, worldMaxY - worldMinY);
            float pixelsPerMeter = H / spanM;
            float pixelsPerCm = pixelsPerMeter / 100f;
            float zeroTexY = ((zeroWorldY - worldMinY) / spanM) * H;

            // Zero line (blue highlight band)
            int zy = Mathf.RoundToInt(zeroTexY);
            DrawRect(pixels, W, H, 8, zy - 4, W - 16, 8, zeroBlue);

            // Negative cm ticks upward
            for (int cm = 1; cm <= 20; cm++)
            {
                int py = Mathf.RoundToInt(zeroTexY + cm * pixelsPerCm);
                if (py >= H - 165) break;
                bool isFive = cm % 5 == 0;
                int len = isFive ? 60 : 32;
                int thick = isFive ? 3 : 2;
                DrawRect(pixels, W, H, 10, py - thick / 2, len, thick, isFive ? tickMed : tickLight);
            }

            // Positive extension cm ticks downward (decreasing texY)
            for (int cm = 1; cm <= 110; cm++)
            {
                int py = Mathf.RoundToInt(zeroTexY - cm * pixelsPerCm);
                if (py < 10) break;
                bool isTen = cm % 10 == 0;
                bool isFive = cm % 5 == 0;
                int len = isTen ? 105 : (isFive ? 65 : 32);
                int thick = isTen ? 4 : (isFive ? 3 : 2);
                Color32 col = isTen ? tickDark : (isFive ? tickMed : tickLight);
                DrawRect(pixels, W, H, 10, py - thick / 2, len, thick, col);
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static void DrawRect(Color32[] pixels, int texW, int texH, int x, int y, int w, int h, Color32 col)
        {
            int x0 = Mathf.Clamp(x, 0, texW);
            int x1 = Mathf.Clamp(x + w, 0, texW);
            int y0 = Mathf.Clamp(y, 0, texH);
            int y1 = Mathf.Clamp(y + h, 0, texH);
            for (int py = y0; py < y1; py++)
            {
                int row = py * texW;
                for (int px = x0; px < x1; px++)
                {
                    pixels[row + px] = col;
                }
            }
        }

        public void SetDisplacement(float displacementM, SpringState state)
        {
            float length = Mathf.Max(0.08f, naturalLength + displacementM);
            CurrentLength = length;

            UpdateSpringGeometry(length);

            float hangerY = topAnchor.y - length;
            _hangerGroup.position = new Vector3(topAnchor.x, hangerY, topAnchor.z);

            if (_orangePointer != null)
            {
                _orangePointer.position = new Vector3(
                    _orangePointerInitialPos.x,
                    _orangePointerInitialPos.y - displacementM,
                    _orangePointerInitialPos.z
                );
            }

            if (_pointerTagText != null)
            {
                _pointerTagText.text = $"x = {displacementM * 100f:F2} cm\n<color=#94a3b8>F = {state.appliedForceN:F2} N</color>";
            }

            const float vectorScale = 0.08f;
            float restoringLen = Mathf.Max(0.001f, Mathf.Abs(state.restoringForceN) * vectorScale);
            float gravityLen = Mathf.Max(0.001f, state.appliedForceN * vectorScale);

            UpdateArrowLength(_arrowRestoring, _arrowRestoringShaft, _arrowRestoringHead, +1f, restoringLen);
            UpdateArrowLength(_arrowGravity, _arrowGravityShaft, _arrowGravityHead, -1f, gravityLen);

            if (state.isExceedingElasticLimit)
            {
                _springMat.color = ExceededSpringColor;
                _springMat.SetColor("_EmissionColor", ExceededEmissionColor);
            }
            else
            {
                _springMat.color = NormalSpringColor;
                _springMat.SetColor("_EmissionColor", Color.black);
            }
        }

        public void SetLaserVisible(bool visible)
        {
            if (_laserLine != null) _laserLine.gameObject.SetActive(visible);
        }

        public void SetVectorsVisible(bool visible)
        {
            if (_vectorGroup != null) _vectorGroup.SetActive(visible);
        }

        public void SetSnapHaloHighlight(bool active)
        {
            if (_snapRingTransform == null || _snapRingMat == null) return;
            _snapRingTransform.localScale = Vector3.one * (active ? 1.25f : 1.0f);
            _snapRingMat.color = active
                ? new Color(0f, 1f, 1f, 0.95f)
                : new Color(0f, 0.9f, 1f, 0.4f);
        }

        public Vector3 GetHangerSnapPosition()
        {
            return HangerSnapPoint != null
                ? HangerSnapPoint.position
                : _hangerGroup.position + new Vector3(0f, -0.114f, 0f);
        }

        private static GameObject CreatePrimitivePart(string partName, PrimitiveType type, Transform parent,
            Vector3 localPos, Vector3 localScale, Quaternion localRot, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = partName;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        public static Mesh CreateTorusMesh(float majorRadius, float minorRadius, int majorSegments, int minorSegments)
        {
            Mesh mesh = new Mesh { name = "ProceduralTorus" };
            int vertCount = (majorSegments + 1) * (minorSegments + 1);
            Vector3[] verts = new Vector3[vertCount];
            Vector3[] norms = new Vector3[vertCount];
            int[] tris = new int[majorSegments * minorSegments * 6];

            int v = 0;
            for (int i = 0; i <= majorSegments; i++)
            {
                float u = ((float)i / majorSegments) * Mathf.PI * 2f;
                Vector3 center = new Vector3(Mathf.Cos(u) * majorRadius, 0f, Mathf.Sin(u) * majorRadius);
                Vector3 radial = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u));

                for (int j = 0; j <= minorSegments; j++)
                {
                    float w = ((float)j / minorSegments) * Mathf.PI * 2f;
                    Vector3 offset = (radial * Mathf.Cos(w) + Vector3.up * Mathf.Sin(w)) * minorRadius;
                    verts[v] = center + offset;
                    norms[v] = offset.normalized;
                    v++;
                }
            }

            int t = 0;
            int ringStride = minorSegments + 1;
            for (int i = 0; i < majorSegments; i++)
            {
                for (int j = 0; j < minorSegments; j++)
                {
                    int a = i * ringStride + j;
                    int b = (i + 1) * ringStride + j;
                    int c = (i + 1) * ringStride + (j + 1);
                    int d = i * ringStride + (j + 1);
                    tris[t++] = a; tris[t++] = d; tris[t++] = b;
                    tris[t++] = b; tris[t++] = d; tris[t++] = c;
                }
            }

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.triangles = tris;
            return mesh;
        }

        private static Mesh CreateConeMesh(float radius, float height, int segments)
        {
            Mesh mesh = new Mesh { name = "ProceduralCone" };
            Vector3[] verts = new Vector3[segments + 2];
            int[] tris = new int[segments * 6];

            verts[0] = new Vector3(0f, height * 0.5f, 0f);
            verts[1] = new Vector3(0f, -height * 0.5f, 0f);
            for (int i = 0; i < segments; i++)
            {
                float a = ((float)i / segments) * Mathf.PI * 2f;
                verts[i + 2] = new Vector3(Mathf.Cos(a) * radius, -height * 0.5f, Mathf.Sin(a) * radius);
            }

            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris[t++] = 0; tris[t++] = next + 2; tris[t++] = i + 2;
                tris[t++] = 1; tris[t++] = i + 2; tris[t++] = next + 2;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
