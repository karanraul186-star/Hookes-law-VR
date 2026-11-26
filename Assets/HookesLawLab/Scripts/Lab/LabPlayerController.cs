using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Unity.XR.CoreUtils;
using TMPro;
using HookesLawLab.Interaction;

namespace HookesLawLab.Lab
{
    /// <summary>
    /// Unified Player Rig & Interaction Controller supporting:
    /// 1) VR mode via XR Origin (XR Rig) + XRI 3.4.1 controllers/hands & automatic eye-height calibration
    ///    matching the Teacher character's eye level without double height offsets or teleport drift.
    /// 2) Desktop / Editor Play Mode fallback: eye height matched to Teacher eye level, WASD/Arrows locomotion
    ///    with collision sliding, mouse drag look, left-click/drag for calibration masses, R to reset position.
    /// </summary>
    public class LabPlayerController : MonoBehaviour
    {
        public float walkSpeed = 2.4f;
        public float sprintSpeed = 4.2f;
        public float eyeHeight = 1.72755f;
        public float vrTargetEyeHeight = 1.72755f;
        public float mouseSensitivity = 0.16f;

        private XROrigin _xrOrigin;
        private Camera _mainCamera;
        private CharacterController _charController;
        private AttachmentManager _attachmentManager;
        private TeleportationProvider _teleportProvider;

        private Vector3 _spawnPosition = new Vector3(0f, 0f, 6.345f);
        private Vector3 _targetLookAt = new Vector3(0f, 1.40f, 0f);
        private float _yaw;
        private float _pitch;

        private bool _vrHeightCalibrated;
        private bool _initialPoseAppliedInXR;
        private bool _hasPlayerMovedOrTeleported;
        private float _vrCalibratedOffsetY;
        private float _xrWaitStartTime;
        private TrackingOriginModeFlags _calibratedOriginMode;
        private bool _subscribedTrackingOrigin;
        private bool _isLooking;
        private MassInteractable _draggedMass;
        private MassInteractable _hoveredMass;
        private float _dragStartDist = 1f;
        private float _dragHangerDist = 1f;

        // Desktop HUD
        private GameObject _hudCanvasGo;
        private TMP_Text _tooltipText;
        private Image _tooltipBg;
        private static readonly Color TooltipNormalBg = new Color(0.059f, 0.09f, 0.165f, 0.85f);
        private static readonly Color TooltipHighlightBg = new Color(0.008f, 0.35f, 0.52f, 0.94f);

        public Vector3 PlayerHeadWorldPosition =>
            _mainCamera != null ? _mainCamera.transform.position : _spawnPosition + Vector3.up * eyeHeight;

        public void Initialize(
            XROrigin xrOrigin,
            AttachmentManager attachmentManager,
            Vector3 spawnPos,
            Vector3 lookAtTarget,
            TMP_FontAsset fontAsset = null)
        {
            _xrOrigin = xrOrigin;
            _attachmentManager = attachmentManager;
            _spawnPosition = spawnPos;
            _targetLookAt = lookAtTarget;

            if (_xrOrigin != null)
            {
                _mainCamera = _xrOrigin.Camera != null ? _xrOrigin.Camera : Camera.main;
                _xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                // Keep CameraYOffset aligned to calibrated offset in VR (only use vrTargetEyeHeight before XR calibration in Desktop mode)
                _xrOrigin.CameraYOffset = IsXRDeviceActive() ? 0f : vrTargetEyeHeight;
                _xrWaitStartTime = Time.unscaledTime;

                _teleportProvider = _xrOrigin.GetComponentInChildren<TeleportationProvider>(true);
                if (_teleportProvider != null)
                {
                    _teleportProvider.locomotionEnded -= OnTeleportEnded;
                    _teleportProvider.locomotionEnded += OnTeleportEnded;
                }

                _charController = _xrOrigin.GetComponent<CharacterController>();
                if (_charController == null)
                {
                    _charController = _xrOrigin.gameObject.AddComponent<CharacterController>();
                    _charController.radius = 0.30f;
                    _charController.height = 1.70f;
                    _charController.center = new Vector3(0f, 0.85f, 0f);
                }
            }
            else
            {
                _mainCamera = Camera.main;
            }

            _hasPlayerMovedOrTeleported = false;
            _initialPoseAppliedInXR = false;
            SetInitialView(_spawnPosition, _targetLookAt);
            BuildDesktopHUD(fontAsset);
        }

        public void SetInitialView(Vector3 spawnPos, Vector3 targetPos)
        {
            _spawnPosition = spawnPos;
            _targetLookAt = targetPos;

            Vector3 eyeWorld = new Vector3(spawnPos.x, spawnPos.y + eyeHeight, spawnPos.z);
            Vector3 dir = (targetPos - eyeWorld).normalized;
            float horiz = Mathf.Max(0.001f, Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z));

            _yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _pitch = -Mathf.Atan2(dir.y, horiz) * Mathf.Rad2Deg;
            _pitch = Mathf.Clamp(_pitch, -80f, 80f);

            if (!IsXRDeviceActive())
            {
                Transform rigTransform = _xrOrigin != null ? _xrOrigin.transform : (_mainCamera != null ? _mainCamera.transform : transform);
                if (_charController != null) _charController.enabled = false;
                rigTransform.position = spawnPos;
                rigTransform.rotation = Quaternion.identity;
                if (_charController != null) _charController.enabled = true;

                if (_mainCamera != null)
                {
                    if (_xrOrigin != null && _xrOrigin.CameraFloorOffsetObject != null)
                    {
                        _xrOrigin.CameraFloorOffsetObject.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
                    }
                    _mainCamera.transform.localPosition = _xrOrigin != null ? Vector3.zero : new Vector3(0f, eyeHeight, 0f);
                    _mainCamera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                }
            }
            else
            {
                ApplyXREntranceHorizontalAndYaw(spawnPos, targetPos);
                // Mark XR entrance applied only if HMD already provides a valid tracked offset or height calibration is done
                if (_vrHeightCalibrated || (_mainCamera != null && _mainCamera.transform.localPosition.sqrMagnitude > 0.0001f))
                {
                    _initialPoseAppliedInXR = true;
                }
            }
        }

        private void ApplyXREntranceHorizontalAndYaw(Vector3 spawnPos, Vector3 targetPos)
        {
            if (_xrOrigin == null) return;

            Vector3 desiredFwdXZ = new Vector3(targetPos.x - spawnPos.x, 0f, targetPos.z - spawnPos.z);
            if (desiredFwdXZ.sqrMagnitude < 0.0001f) desiredFwdXZ = Vector3.back;
            desiredFwdXZ.Normalize();
            float desiredWorldYaw = Mathf.Atan2(desiredFwdXZ.x, desiredFwdXZ.z) * Mathf.Rad2Deg;

            if (_charController != null) _charController.enabled = false;

            // 1. Apply yaw-only heading correction so HMD faces desiredWorldYaw without forcing pitch or roll
            if (_mainCamera != null)
            {
                Vector3 camFwdXZ = _mainCamera.transform.forward;
                camFwdXZ.y = 0f;
                if (camFwdXZ.sqrMagnitude > 0.0001f)
                {
                    float currentCamYaw = Mathf.Atan2(camFwdXZ.x, camFwdXZ.z) * Mathf.Rad2Deg;
                    float deltaYaw = Mathf.DeltaAngle(currentCamYaw, desiredWorldYaw);
                    float nextRigYaw = _xrOrigin.transform.eulerAngles.y + deltaYaw;
                    _xrOrigin.transform.rotation = Quaternion.Euler(0f, nextRigYaw, 0f);
                }
                else
                {
                    _xrOrigin.transform.rotation = Quaternion.Euler(0f, desiredWorldYaw, 0f);
                }

                // 2. Compensate for the headset's local horizontal offset so the user appears at spawnPos XZ
                Vector3 camWorld = _mainCamera.transform.position;
                Vector3 rigPos = _xrOrigin.transform.position;
                _xrOrigin.transform.position = new Vector3(
                    rigPos.x + (spawnPos.x - camWorld.x),
                    spawnPos.y,
                    rigPos.z + (spawnPos.z - camWorld.z));
            }
            else
            {
                _xrOrigin.transform.rotation = Quaternion.Euler(0f, desiredWorldYaw, 0f);
                _xrOrigin.transform.position = spawnPos;
            }

            if (_charController != null) _charController.enabled = true;
        }

        public void ResetPosition()
        {
            _hasPlayerMovedOrTeleported = false;
            SetInitialView(_spawnPosition, _targetLookAt);
        }

        private void BuildDesktopHUD(TMP_FontAsset fontAsset)
        {
            _hudCanvasGo = new GameObject("DesktopHUDCanvas");
            _hudCanvasGo.transform.SetParent(transform, false);
            Canvas canvas = _hudCanvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            CanvasScaler scaler = _hudCanvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _hudCanvasGo.AddComponent<GraphicRaycaster>();

            // Top-left title badge
            GameObject topBadge = new GameObject("TopBadge");
            topBadge.transform.SetParent(_hudCanvasGo.transform, false);
            RectTransform tbrt = topBadge.AddComponent<RectTransform>();
            tbrt.anchorMin = new Vector2(0f, 1f); tbrt.anchorMax = new Vector2(0f, 1f);
            tbrt.pivot = new Vector2(0f, 1f);
            tbrt.anchoredPosition = new Vector2(20f, -20f);
            tbrt.sizeDelta = new Vector2(560f, 76f);
            topBadge.AddComponent<Image>().color = TooltipNormalBg;
            Outline tbo = topBadge.AddComponent<Outline>();
            tbo.effectColor = new Color(0f, 0.9f, 1f, 0.6f);

            GameObject tbTxt = new GameObject("Text");
            tbTxt.transform.SetParent(topBadge.transform, false);
            RectTransform trt = tbTxt.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(16f, 8f); trt.offsetMax = new Vector2(-16f, -8f);
            TMP_Text hdr = tbTxt.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) hdr.font = fontAsset;
            hdr.text = "<b>Physics Lab — Hooke's Law (Masses & Springs)</b>\n<size=15><color=#94a3b8>WASD: Move • Drag: Look • Click/Drag Masses & Screens • R: Reset View</color></size>";
            hdr.fontSize = 20f;
            hdr.color = Color.white;

            // Top-right Reset Button
            GameObject resetGo = new GameObject("BtnResetView");
            resetGo.transform.SetParent(_hudCanvasGo.transform, false);
            RectTransform rrt = resetGo.AddComponent<RectTransform>();
            rrt.anchorMin = new Vector2(1f, 1f); rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(1f, 1f);
            rrt.anchoredPosition = new Vector2(-20f, -20f);
            rrt.sizeDelta = new Vector2(180f, 44f);
            Image rImg = resetGo.AddComponent<Image>();
            rImg.color = new Color(0.012f, 0.412f, 0.631f, 0.9f);
            Button rBtn = resetGo.AddComponent<Button>();
            rBtn.onClick.AddListener(ResetPosition);

            GameObject rTxtGo = new GameObject("Text");
            rTxtGo.transform.SetParent(resetGo.transform, false);
            RectTransform rtrt = rTxtGo.AddComponent<RectTransform>();
            rtrt.anchorMin = Vector2.zero; rtrt.anchorMax = Vector2.one;
            rtrt.offsetMin = Vector2.zero; rtrt.offsetMax = Vector2.zero;
            TMP_Text rTxt = rTxtGo.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) rTxt.font = fontAsset;
            rTxt.text = "↺ Reset Position";
            rTxt.fontSize = 18f;
            rTxt.fontStyle = FontStyles.Bold;
            rTxt.color = Color.white;
            rTxt.alignment = TextAlignmentOptions.Center;

            // Bottom interaction tooltip
            GameObject tipGo = new GameObject("InteractionTooltip");
            tipGo.transform.SetParent(_hudCanvasGo.transform, false);
            RectTransform tipRt = tipGo.AddComponent<RectTransform>();
            tipRt.anchorMin = new Vector2(0.5f, 0f); tipRt.anchorMax = new Vector2(0.5f, 0f);
            tipRt.pivot = new Vector2(0.5f, 0f);
            tipRt.anchoredPosition = new Vector2(0f, 22f);
            tipRt.sizeDelta = new Vector2(820f, 48f);
            _tooltipBg = tipGo.AddComponent<Image>();
            _tooltipBg.color = TooltipNormalBg;
            Outline tipOut = tipGo.AddComponent<Outline>();
            tipOut.effectColor = new Color(0f, 0.9f, 1f, 0.7f);

            GameObject tipTxtGo = new GameObject("Text");
            tipTxtGo.transform.SetParent(tipGo.transform, false);
            RectTransform ttrt = tipTxtGo.AddComponent<RectTransform>();
            ttrt.anchorMin = Vector2.zero; ttrt.anchorMax = Vector2.one;
            ttrt.offsetMin = new Vector2(16f, 4f); ttrt.offsetMax = new Vector2(-16f, -4f);
            _tooltipText = tipTxtGo.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) _tooltipText.font = fontAsset;
            _tooltipText.text = "Drag calibration masses to spring hanger to load • Click attached mass to detach";
            _tooltipText.fontSize = 18f;
            _tooltipText.color = Color.white;
            _tooltipText.alignment = TextAlignmentOptions.Center;
        }

        private void SetTooltip(string text, bool highlight = false)
        {
            if (_tooltipText != null) _tooltipText.text = text;
            if (_tooltipBg != null) _tooltipBg.color = highlight ? TooltipHighlightBg : TooltipNormalBg;
        }

        private void Update()
        {
            bool xrActive = IsXRDeviceActive();
            if (_hudCanvasGo != null && _hudCanvasGo.activeSelf == xrActive)
            {
                _hudCanvasGo.SetActive(!xrActive);
            }

            if (xrActive)
            {
                EnsureTrackingOriginSubscription();
                CalibrateVRHeightOnce();
                return;
            }

            UpdateDesktopLocomotion();
            UpdateDesktopMouseLookAndMassDrag();
        }

        private void LateUpdate()
        {
            // Maintain calibrated Camera Offset across teleportation, tracking updates, and resets in VR mode only
            if (IsXRDeviceActive() && _vrHeightCalibrated && _xrOrigin != null && _xrOrigin.CameraFloorOffsetObject != null)
            {
                Transform offsetTr = _xrOrigin.CameraFloorOffsetObject.transform;
                Vector3 localPos = offsetTr.localPosition;
                if (Mathf.Abs(localPos.y - _vrCalibratedOffsetY) > 0.0005f)
                {
                    offsetTr.localPosition = new Vector3(localPos.x, _vrCalibratedOffsetY, localPos.z);
                }
            }
        }

        private void EnsureTrackingOriginSubscription()
        {
            if (_subscribedTrackingOrigin) return;
            var inputSubsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputSubsystems);
            if (inputSubsystems.Count > 0)
            {
                foreach (XRInputSubsystem sub in inputSubsystems)
                {
                    sub.trackingOriginUpdated -= OnXRTrackingOriginUpdated;
                    sub.trackingOriginUpdated += OnXRTrackingOriginUpdated;
                }
                _subscribedTrackingOrigin = true;
            }
        }

        private void OnXRTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            // Re-calibrate cleanly from raw HMD height on recenter/boundary change without accumulating
            _vrHeightCalibrated = false;
            _xrWaitStartTime = Time.unscaledTime;
        }

        private void OnTeleportEnded(UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider _)
        {
            if (_xrOrigin == null) return;
            _hasPlayerMovedOrTeleported = true;
            _initialPoseAppliedInXR = true;

            // Keep XR Origin anchored on the laboratory floor (y = 0) and preserve calibrated CameraOffset
            Vector3 rigPos = _xrOrigin.transform.position;
            if (Mathf.Abs(rigPos.y - _spawnPosition.y) > 0.001f)
            {
                if (_charController != null) _charController.enabled = false;
                _xrOrigin.transform.position = new Vector3(rigPos.x, _spawnPosition.y, rigPos.z);
                if (_charController != null) _charController.enabled = true;
            }

            if (_vrHeightCalibrated && _xrOrigin.CameraFloorOffsetObject != null)
            {
                Transform offsetTr = _xrOrigin.CameraFloorOffsetObject.transform;
                offsetTr.localPosition = new Vector3(offsetTr.localPosition.x, _vrCalibratedOffsetY, offsetTr.localPosition.z);
                _xrOrigin.CameraYOffset = _vrCalibratedOffsetY;
            }
        }

        private void OnDestroy()
        {
            if (_teleportProvider != null)
            {
                _teleportProvider.locomotionEnded -= OnTeleportEnded;
            }
            if (_subscribedTrackingOrigin)
            {
                var inputSubsystems = new List<XRInputSubsystem>();
                SubsystemManager.GetSubsystems(inputSubsystems);
                foreach (XRInputSubsystem sub in inputSubsystems)
                {
                    sub.trackingOriginUpdated -= OnXRTrackingOriginUpdated;
                }
            }
        }

        private void CalibrateVRHeightOnce()
        {
            if (_xrOrigin == null || _mainCamera == null || _xrOrigin.CameraFloorOffsetObject == null)
                return;

            TrackingOriginModeFlags mode = _xrOrigin.CurrentTrackingOriginMode;

            // If OpenXR switched tracking mode after an earlier Device fallback, recalibrate immediately
            if (_vrHeightCalibrated)
            {
                if (mode != _calibratedOriginMode)
                {
                    _vrHeightCalibrated = false;
                }
                else
                {
                    return;
                }
            }

            float rawHeadY = _mainCamera.transform.localPosition.y;

            if (mode == TrackingOriginModeFlags.Floor)
            {
                // Wait for valid floor-relative HMD tracking before calibrating to prevent double height offsets
                if (rawHeadY < 0.35f || rawHeadY > 2.40f) return;
                _vrCalibratedOffsetY = Mathf.Clamp(vrTargetEyeHeight - rawHeadY, -0.45f, 0.95f);
            }
            else
            {
                // Give OpenXR up to 0.6s to settle into Floor origin mode before falling back to Device mode
                if (Time.unscaledTime - _xrWaitStartTime < 0.6f && rawHeadY < 0.1f) return;
                _vrCalibratedOffsetY = Mathf.Clamp(vrTargetEyeHeight - rawHeadY, -0.45f, 2.00f);
            }

            // Apply to CameraFloorOffsetObject AND sync XROrigin.CameraYOffset so XROrigin never fights or doubles it
            Transform offsetTr = _xrOrigin.CameraFloorOffsetObject.transform;
            offsetTr.localPosition = new Vector3(offsetTr.localPosition.x, _vrCalibratedOffsetY, offsetTr.localPosition.z);
            _xrOrigin.CameraYOffset = _vrCalibratedOffsetY;

            Vector3 rigPos = _xrOrigin.transform.position;
            if (Mathf.Abs(rigPos.y - _spawnPosition.y) > 0.005f)
            {
                if (_charController != null) _charController.enabled = false;
                _xrOrigin.transform.position = new Vector3(rigPos.x, _spawnPosition.y, rigPos.z);
                if (_charController != null) _charController.enabled = true;
            }

            _calibratedOriginMode = mode;
            _vrHeightCalibrated = true;

            // Apply one-time entrance XZ horizontal offset + yaw once valid XR tracking settles (never after teleport/move/resume)
            if (!_initialPoseAppliedInXR)
            {
                if (!_hasPlayerMovedOrTeleported)
                {
                    ApplyXREntranceHorizontalAndYaw(_spawnPosition, _targetLookAt);
                }
                _initialPoseAppliedInXR = true;
            }

            Debug.Log($"[LabPlayerController] Calibrated VR User POV to Teacher eye height {vrTargetEyeHeight:F3}m (mode={mode}, rawHeadY={rawHeadY:F3}m, offsetY={_vrCalibratedOffsetY:F3}m, camWorldY={_mainCamera.transform.position.y:F3}m).");
        }

        private void UpdateDesktopLocomotion()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || _mainCamera == null) return;

            // Avoid WASD movement while typing into custom teacher input field
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null &&
                UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null)
            {
                return;
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                ResetPosition();
                return;
            }

            Vector3 fwd = _mainCamera.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.0001f) fwd.Normalize();

            Vector3 right = _mainCamera.transform.right;
            right.y = 0f;
            if (right.sqrMagnitude > 0.0001f) right.Normalize();

            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move += fwd;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move -= fwd;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move += right;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move -= right;

            if (move.sqrMagnitude > 0.0001f)
            {
                _hasPlayerMovedOrTeleported = true;
                move.Normalize();
                float speed = (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) ? sprintSpeed : walkSpeed;
                if (_charController != null && _charController.enabled)
                {
                    _charController.Move(move * (speed * Time.deltaTime));
                    Vector3 p = _charController.transform.position;
                    _charController.transform.position = new Vector3(p.x, _spawnPosition.y, p.z);
                }
                else
                {
                    Transform rig = _xrOrigin != null ? _xrOrigin.transform : _mainCamera.transform;
                    rig.position += move * (speed * Time.deltaTime);
                }
            }
        }

        private void UpdateDesktopMouseLookAndMassDrag()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || _mainCamera == null) return;

            Vector2 mousePos = mouse.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(mousePos);

            // Handle active mass dragging
            if (_draggedMass != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    Vector3 hangerPos = _attachmentManager.SpringVisual.GetHangerSnapPosition();
                    float rayDistToHanger = AttachmentManager.DistanceRayToPoint(ray, hangerPos);
                    float t = Mathf.Clamp01(1f - (rayDistToHanger / 0.55f));
                    float easeT = Mathf.Pow(t, 1.4f);
                    float targetDist = Mathf.Lerp(_dragStartDist, _dragHangerDist, easeT);
                    Vector3 targetPos = ray.origin + ray.direction * targetDist;
                    _draggedMass.MoveToWorld(targetPos);

                    bool inSnap = _attachmentManager.CheckSnapProximity(_draggedMass, ray);
                    SetTooltip(inSnap
                        ? $"Snap Zone Reached: Release mouse to attach {_draggedMass.massG}g mass to spring"
                        : $"Carrying {_draggedMass.massG}g mass • Drag toward the glowing hanger to attach", inSnap);
                    return;
                }
                else
                {
                    MassInteractable released = _draggedMass;
                    _draggedMass = null;
                    released.SetGrabbed(false);
                    if (_attachmentManager.CheckSnapProximity(released, ray))
                    {
                        _attachmentManager.AttachMass(released, stack: true);
                        SetTooltip($"Attached {released.massG}g mass to spring hanger • Observe Hooke's Law extension!", true);
                    }
                    else
                    {
                        _attachmentManager.NotifyMassReturnedOutsideSnap(released);
                        released.ReturnToTraySmooth();
                        SetTooltip("Returned mass to tray slot", false);
                    }
                }
            }

            // Skip 3D mass raycast if pointer is over a World-Space or Screen-Space UI element
            bool overUI = UnityEngine.EventSystems.EventSystem.current != null &&
                          UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

            if (!overUI && _attachmentManager != null)
            {
                MassInteractable hitMass = RaycastMass(ray);

                if (hitMass != _hoveredMass)
                {
                    _hoveredMass?.SetHovered(false);
                    _hoveredMass = hitMass;
                    _hoveredMass?.SetHovered(true);
                }

                if (_hoveredMass != null)
                {
                    if (_hoveredMass.State == MassInteractionState.Attached)
                    {
                        SetTooltip($"Attached {_hoveredMass.massG}g Mass: Click to detach from spring", true);
                    }
                    else
                    {
                        SetTooltip($"{_hoveredMass.massG}g Calibration Mass: Click & drag toward spring hanger", false);
                    }

                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        _isLooking = false;
                        if (_hoveredMass.State == MassInteractionState.Attached)
                        {
                            _attachmentManager.DetachMass(_hoveredMass);
                            SetTooltip($"Detached {_hoveredMass.massG}g mass back to tray", false);
                        }
                        else
                        {
                            _draggedMass = _hoveredMass;
                            _draggedMass.State = MassInteractionState.Grabbed;
                            _draggedMass.SetGrabbed(true);
                            Vector3 massCenter = _draggedMass.transform.position + Vector3.up * (_draggedMass.bodyHeight * 0.5f);
                            _dragStartDist = Vector3.Distance(_mainCamera.transform.position, massCenter);
                            Vector3 attachPt = _attachmentManager.SpringVisual.SpringAttachmentPoint != null
                                ? _attachmentManager.SpringVisual.SpringAttachmentPoint.position
                                : _attachmentManager.SpringVisual.GetHangerSnapPosition();
                            _dragHangerDist = Vector3.Distance(_mainCamera.transform.position, attachPt);
                            _attachmentManager.PlayPickupSound();
                            _attachmentManager.NotifyMassPickedUp(_draggedMass);
                            SetTooltip($"Grabbed {_draggedMass.massG}g: Drag toward the glowing hanger to attach", false);
                        }
                        return;
                    }
                }
                else
                {
                    SetTooltip("Drag calibration masses to spring hanger to load • Click attached mass to detach", false);
                }
            }

            // Mouse drag to look (left drag when not over UI/mass, or right drag anytime)
            if ((mouse.leftButton.wasPressedThisFrame && !overUI) || mouse.rightButton.wasPressedThisFrame)
            {
                _isLooking = true;
            }
            if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed)
            {
                _isLooking = false;
            }

            if (_isLooking)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * mouseSensitivity;
                _pitch -= delta.y * mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch, -80f, 80f);
                _mainCamera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
        }

        private static MassInteractable RaycastMass(Ray ray)
        {
            RaycastHit[] hits = UnityEngine.Physics.RaycastAll(ray, 12f, UnityEngine.Physics.DefaultRaycastLayers);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                MassInteractable m = h.collider.GetComponentInParent<MassInteractable>();
                if (m != null) return m;
            }
            return null;
        }

        private static bool IsXRDeviceActive()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (XRDisplaySubsystem d in displays)
            {
                if (d.running) return true;
            }
            return false;
        }
    }
}
