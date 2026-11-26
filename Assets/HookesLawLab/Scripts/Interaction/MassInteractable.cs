using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using TMPro;
using HookesLawLab.Physics;

namespace HookesLawLab.Interaction
{
    public enum MassInteractionState
    {
        Available,
        Grabbed,
        Attaching,
        Attached,
        Detached
    }

    /// <summary>
    /// Complete unified calibration weight (50 g – 300 g) with bottom-center pivot,
    /// brushed-metal cylindrical body, permanently connected top metal hook,
    /// dedicated HookAttachmentPoint, high-contrast VR label, single Rigidbody + CapsuleCollider,
    /// smooth tray return, and Meta Quest 3S XRGrabInteractable + PC drag support.
    /// </summary>
    public class MassInteractable : MonoBehaviour
    {
        public string massId;
        public int massG;
        public float massKg;
        public float bodyHeight = 0.06f;
        public float bodyRadius = 0.036f;
        public float totalHeight = 0.10f;
        public MassInteractionState State = MassInteractionState.Available;
        public bool IsHovered { get; private set; }

        public Transform HookAttachmentPoint { get; private set; }
        public Vector3 OriginalWorldPosition { get; private set; }
        public Quaternion OriginalWorldRotation { get; private set; }

        private Transform _trayParent;
        private Vector3 _trayLocalPos;
        private Quaternion _trayLocalRot;
        private Vector3 _trayLocalScale;

        private AttachmentManager _attachmentManager;
        private XRGrabInteractable _xrGrab;
        private Rigidbody _rb;
        private CapsuleCollider _collider;

        private readonly List<Material> _metalMaterials = new List<Material>();
        private Material _selectionRingMat;
        private Renderer _selectionRingRenderer;
        private float _tableMatTopY = 1.13f;
        private Coroutine _returnCoroutine;

        private static readonly Color BrushedMetalColor = new Color(0.64f, 0.67f, 0.71f);
        private static readonly Color HoverEmissionColor = new Color(0f, 0.20f, 0.28f);

        public void BuildUnifiedWeight(
            int grams,
            AttachmentManager manager,
            Vector3 worldTrayBottomPos,
            Quaternion worldTrayRot,
            float tableMatTopY,
            TMP_FontAsset fontAsset = null)
        {
            massG = grams;
            massKg = grams / 1000f;
            massId = $"mass_{grams}g";
            _attachmentManager = manager;
            _tableMatTopY = tableMatTopY;

            // Visually proportional dimensions for 50g..300g
            int stepIndex = Mathf.Clamp((grams - 50) / 50, 0, 5); // 0..5
            bodyRadius = 0.031f + stepIndex * 0.0022f;            // 3.1cm .. 4.2cm
            bodyHeight = 0.038f + stepIndex * 0.016f;             // 3.8cm .. 11.8cm

            // Position root pivot at exact bottom-center flush on mat surface
            transform.position = new Vector3(worldTrayBottomPos.x, tableMatTopY, worldTrayBottomPos.z);
            transform.rotation = worldTrayRot;
            transform.localScale = Vector3.one;

            _trayParent = transform.parent;
            _trayLocalPos = transform.localPosition;
            _trayLocalRot = transform.localRotation;
            _trayLocalScale = Vector3.one;
            OriginalWorldPosition = transform.position;
            OriginalWorldRotation = transform.rotation;

            BuildVisualGeometry(fontAsset);
            SetupSinglePhysicsBodyAndCollider();
            SetupXRGrab();
            BuildSelectionRing();

            SetRestingOnTrayState();
        }

        private void BuildVisualGeometry(TMP_FontAsset fontAsset)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Material brushedSteel = new Material(litShader) { color = BrushedMetalColor };
            brushedSteel.SetFloat("_Metallic", 0.62f);
            brushedSteel.SetFloat("_Smoothness", 0.36f); // Matte brushed metal without excessive glare
            brushedSteel.EnableKeyword("_EMISSION");
            brushedSteel.SetColor("_EmissionColor", Color.black);
            _metalMaterials.Add(brushedSteel);

            Material hookSteel = new Material(litShader) { color = new Color(0.75f, 0.78f, 0.82f) };
            hookSteel.SetFloat("_Metallic", 0.72f);
            hookSteel.SetFloat("_Smoothness", 0.44f);
            hookSteel.EnableKeyword("_EMISSION");
            hookSteel.SetColor("_EmissionColor", Color.black);
            _metalMaterials.Add(hookSteel);

            // 1. Cylindrical weight body (bottom at y = 0, top at y = bodyHeight)
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "WeightBody";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, bodyHeight * 0.5f, 0f);
            body.transform.localScale = new Vector3(bodyRadius * 2f, bodyHeight * 0.5f, bodyRadius * 2f);
            MeshRenderer bodyMr = body.GetComponent<MeshRenderer>();
            bodyMr.sharedMaterial = brushedSteel;
            bodyMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Subtle top collar ring where hook joins the cylinder
            GameObject collar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            collar.name = "TopCollar";
            Destroy(collar.GetComponent<Collider>());
            collar.transform.SetParent(transform, false);
            collar.transform.localPosition = new Vector3(0f, bodyHeight + 0.002f, 0f);
            collar.transform.localScale = new Vector3(bodyRadius * 0.48f, 0.003f, bodyRadius * 0.48f);
            MeshRenderer colMr = collar.GetComponent<MeshRenderer>();
            colMr.sharedMaterial = hookSteel;
            colMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 2. Permanently connected metal hook stem + torus loop
            GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "HookStem";
            Destroy(stem.GetComponent<Collider>());
            stem.transform.SetParent(transform, false);
            stem.transform.localPosition = new Vector3(0f, bodyHeight + 0.008f, 0f);
            stem.transform.localScale = new Vector3(0.0075f, 0.010f, 0.0075f);
            MeshRenderer stemMr = stem.GetComponent<MeshRenderer>();
            stemMr.sharedMaterial = hookSteel;
            stemMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            const float hookRadius = 0.014f;
            const float hookWire = 0.0038f;
            float hookCenterY = bodyHeight + 0.025f;

            GameObject hookLoop = new GameObject("MetalHookLoop");
            hookLoop.transform.SetParent(transform, false);
            hookLoop.transform.localPosition = new Vector3(0f, hookCenterY, 0f);
            hookLoop.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Upright ring facing front
            MeshFilter hmf = hookLoop.AddComponent<MeshFilter>();
            hmf.sharedMesh = SpringVisual.CreateTorusMesh(hookRadius, hookWire, 28, 10);
            MeshRenderer hmr = hookLoop.AddComponent<MeshRenderer>();
            hmr.sharedMaterial = hookSteel;
            hmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 3. Dedicated HookAttachmentPoint at top inner curve of the metal hook
            GameObject hookAttach = new GameObject("HookAttachmentPoint");
            hookAttach.transform.SetParent(transform, false);
            hookAttach.transform.localPosition = new Vector3(0f, hookCenterY + hookRadius - hookWire * 0.5f, 0f);
            HookAttachmentPoint = hookAttach.transform;

            totalHeight = hookCenterY + hookRadius + hookWire;

            // 4. High-contrast VR-readable front mass label ("50 g" .. "300 g")
            GameObject labelHolder = new GameObject("MassLabel");
            labelHolder.transform.SetParent(transform, false);
            labelHolder.transform.localPosition = new Vector3(0f, bodyHeight * 0.5f, -(bodyRadius + 0.0018f));
            labelHolder.transform.localRotation = Quaternion.identity;

            float plaqueW = bodyRadius * 1.45f;
            float plaqueH = Mathf.Min(bodyHeight * 0.72f, 0.036f);

            Canvas c = labelHolder.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.sortingOrder = 5;
            RectTransform crt = labelHolder.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(256f, 128f);
            crt.localScale = new Vector3(plaqueW / 256f, plaqueH / 128f, 1f);

            GameObject bgGo = new GameObject("LabelBg");
            bgGo.transform.SetParent(labelHolder.transform, false);
            RectTransform brt = bgGo.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.047f, 0.075f, 0.137f, 0.94f);
            bgImg.raycastTarget = false;

            Outline outl = bgGo.AddComponent<Outline>();
            outl.effectColor = new Color(0.055f, 0.647f, 0.914f, 0.85f);
            outl.effectDistance = new Vector2(3f, -3f);

            GameObject txtGo = new GameObject("LabelText");
            txtGo.transform.SetParent(bgGo.transform, false);
            RectTransform trt = txtGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(6f, 4f); trt.offsetMax = new Vector2(-6f, -4f);
            TMP_Text tmp = txtGo.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.text = $"{massG} g";
            tmp.fontSize = 68f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.extraPadding = true;
            tmp.raycastTarget = false;
        }

        private void SetupSinglePhysicsBodyAndCollider()
        {
            // Single CapsuleCollider covering cylindrical body + metal hook
            _collider = gameObject.AddComponent<CapsuleCollider>();
            _collider.direction = 1; // Y axis
            _collider.radius = bodyRadius;
            _collider.height = Mathf.Max(bodyRadius * 2f, totalHeight);
            _collider.center = new Vector3(0f, totalHeight * 0.5f, 0f);

            // Single Rigidbody on root
            _rb = gameObject.AddComponent<Rigidbody>();
            _rb.mass = Mathf.Max(0.05f, massKg);
            _rb.linearDamping = 3.0f;
            _rb.angularDamping = 4.0f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        private void SetupXRGrab()
        {
            GameObject attachGo = new GameObject("GrabAttachPoint");
            attachGo.transform.SetParent(transform, false);
            attachGo.transform.localPosition = new Vector3(0f, bodyHeight * 0.55f, 0f);

            _xrGrab = gameObject.AddComponent<XRGrabInteractable>();
            _xrGrab.attachTransform = attachGo.transform;
            _xrGrab.useDynamicAttach = true;
            _xrGrab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            _xrGrab.trackPosition = true;
            _xrGrab.trackRotation = true;
            _xrGrab.smoothPosition = true;
            _xrGrab.smoothPositionAmount = 16f;
            _xrGrab.attachEaseInTime = 0.06f;
            _xrGrab.throwOnDetach = false;

            _xrGrab.selectEntered.AddListener(OnXRSelectEntered);
            _xrGrab.selectExited.AddListener(OnXRSelectExited);
            _xrGrab.hoverEntered.AddListener(_ => SetHovered(true));
            _xrGrab.hoverExited.AddListener(_ => SetHovered(false));
        }

        private void BuildSelectionRing()
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _selectionRingMat = new Material(unlitShader);
            _selectionRingMat.color = new Color(0.15f, 0.78f, 0.96f, 0.85f);
            _selectionRingMat.SetFloat("_Surface", 1f);
            _selectionRingMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _selectionRingMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _selectionRingMat.SetInt("_ZWrite", 0);
            _selectionRingMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _selectionRingMat.renderQueue = 3100;

            GameObject ring = new GameObject("SelectionRing");
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.003f, 0f);

            MeshFilter mf = ring.AddComponent<MeshFilter>();
            mf.sharedMesh = SpringVisual.CreateTorusMesh(bodyRadius * 1.18f, 0.0024f, 32, 8);
            _selectionRingRenderer = ring.AddComponent<MeshRenderer>();
            _selectionRingRenderer.sharedMaterial = _selectionRingMat;
            _selectionRingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _selectionRingRenderer.enabled = false;
        }

        private void OnXRSelectEntered(SelectEnterEventArgs args)
        {
            if (_returnCoroutine != null)
            {
                StopCoroutine(_returnCoroutine);
                _returnCoroutine = null;
            }

            if (State == MassInteractionState.Attached && _attachmentManager != null)
            {
                _attachmentManager.DetachMass(this, returnToTray: false);
            }

            State = MassInteractionState.Grabbed;
            SetGrabbedPhysicsState();
            SetGrabbed(true);
            _attachmentManager?.PlayPickupSound();
            _attachmentManager?.NotifyMassPickedUp(this);
        }

        private void OnXRSelectExited(SelectExitEventArgs args)
        {
            SetGrabbed(false);
            if (_attachmentManager != null && _attachmentManager.CheckSnapProximity(this))
            {
                _attachmentManager.AttachMass(this, stack: true);
            }
            else
            {
                _attachmentManager?.NotifyMassReturnedOutsideSnap(this);
                ReturnToTraySmooth();
            }
        }

        private void Update()
        {
            if (State == MassInteractionState.Grabbed || State == MassInteractionState.Attaching)
            {
                // Clamp weight so it never sinks below table surface while carried
                if (transform.position.y < _tableMatTopY)
                {
                    Vector3 p = transform.position;
                    p.y = _tableMatTopY;
                    transform.position = p;
                }
                _attachmentManager?.CheckSnapProximity(this);
            }
        }

        public void SetRestingOnTrayState()
        {
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
                _rb.constraints = RigidbodyConstraints.None;
            }
            if (_collider != null) _collider.enabled = true;
            State = MassInteractionState.Available;
        }

        public void SetGrabbedPhysicsState()
        {
            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.useGravity = false;
                _rb.constraints = RigidbodyConstraints.FreezeRotation;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            if (_collider != null) _collider.enabled = true;
        }

        public void SetAttachedPhysicsState()
        {
            if (_returnCoroutine != null)
            {
                StopCoroutine(_returnCoroutine);
                _returnCoroutine = null;
            }
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
                _rb.constraints = RigidbodyConstraints.None;
            }
            if (_collider != null) _collider.enabled = true;
            State = MassInteractionState.Attached;
        }

        public void SetHovered(bool hovered)
        {
            if (IsHovered == hovered) return;
            IsHovered = hovered;

            if (_selectionRingMat != null && _selectionRingRenderer != null)
            {
                bool show = hovered || State == MassInteractionState.Grabbed || State == MassInteractionState.Attaching;
                _selectionRingRenderer.enabled = show;
                _selectionRingMat.color = hovered
                    ? new Color(0.15f, 0.82f, 0.98f, 0.85f)
                    : new Color(0.22f, 0.74f, 0.97f, 0.95f);
            }

            foreach (Material m in _metalMaterials)
            {
                if (m != null && m.HasProperty("_EmissionColor"))
                {
                    m.SetColor("_EmissionColor", hovered ? HoverEmissionColor : Color.black);
                }
            }
        }

        public void SetGrabbed(bool grabbed)
        {
            if (_selectionRingMat != null && _selectionRingRenderer != null)
            {
                bool show = grabbed || IsHovered;
                _selectionRingRenderer.enabled = show;
                _selectionRingMat.color = grabbed
                    ? new Color(0.22f, 0.74f, 0.97f, 0.95f)
                    : new Color(0.15f, 0.82f, 0.98f, 0.85f);
            }
        }

        public void MoveToWorld(Vector3 worldPos)
        {
            if (_returnCoroutine != null)
            {
                StopCoroutine(_returnCoroutine);
                _returnCoroutine = null;
            }
            if (_rb != null && !_rb.isKinematic)
            {
                _rb.isKinematic = true;
            }
            worldPos.y = Mathf.Max(_tableMatTopY, worldPos.y);
            transform.position = worldPos;
            transform.rotation = OriginalWorldRotation;
        }

        public void ReturnToTraySmooth(float duration = 0.28f)
        {
            if (!gameObject.activeInHierarchy)
            {
                ReturnToTrayImmediate();
                return;
            }

            if (_returnCoroutine != null) StopCoroutine(_returnCoroutine);
            _returnCoroutine = StartCoroutine(SmoothReturnRoutine(duration));
        }

        private IEnumerator SmoothReturnRoutine(float duration)
        {
            State = MassInteractionState.Detached;
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
            transform.SetParent(_trayParent, true);

            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                transform.position = Vector3.Lerp(startPos, OriginalWorldPosition, ease);
                transform.rotation = Quaternion.Slerp(startRot, OriginalWorldRotation, ease);
                yield return null;
            }

            ReturnToTrayImmediate();
            _returnCoroutine = null;
        }

        public void ReturnToTrayImmediate()
        {
            if (_returnCoroutine != null)
            {
                StopCoroutine(_returnCoroutine);
                _returnCoroutine = null;
            }

            transform.SetParent(_trayParent, false);
            transform.localPosition = _trayLocalPos;
            transform.localRotation = _trayLocalRot;
            transform.localScale = _trayLocalScale;
            SetHovered(false);
            SetGrabbed(false);
            SetRestingOnTrayState();
        }
    }
}
