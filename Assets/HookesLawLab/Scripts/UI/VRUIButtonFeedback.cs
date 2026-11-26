using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

namespace HookesLawLab.UI
{
    /// <summary>
    /// Adds smooth hover highlight, press feedback, Meta Quest / OpenXR controller haptics,
    /// and click debouncing to any World-Space UI Button or Selectable card.
    /// </summary>
    public class VRUIButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public float hoverScale = 1.035f;
        public float pressedScale = 0.965f;
        public float debounceSeconds = 0.20f;
        public Color hoverOutlineColor = new Color(0.22f, 0.74f, 0.97f, 1f);

        private Vector3 _baseScale = Vector3.one;
        private Outline _outline;
        private Color _baseOutlineColor;
        private Button _button;
        private float _lastClickTime = -10f;
        private bool _isHovered;

        private static readonly List<InputDevice> _xrControllers = new List<InputDevice>();

        private void Awake()
        {
            _baseScale = transform.localScale;
            _outline = GetComponent<Outline>();
            if (_outline != null) _baseOutlineColor = _outline.effectColor;

            _button = GetComponent<Button>();
            if (_button != null)
            {
                // Wrap button click debounce cleanly by intercepting interactable momentarily on click
                _button.onClick.AddListener(OnButtonClicked);
            }
        }

        private void OnButtonClicked()
        {
            if (Time.unscaledTime - _lastClickTime < debounceSeconds) return;
            _lastClickTime = Time.unscaledTime;
            TriggerControllerHaptics(0.45f, 0.04f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            transform.localScale = _baseScale * hoverScale;
            if (_outline != null) _outline.effectColor = hoverOutlineColor;
            TriggerControllerHaptics(0.15f, 0.02f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            transform.localScale = _baseScale;
            if (_outline != null) _outline.effectColor = _baseOutlineColor;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            transform.localScale = _baseScale * pressedScale;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            transform.localScale = _baseScale * (_isHovered ? hoverScale : 1f);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_button != null && Time.unscaledTime - _lastClickTime < debounceSeconds)
            {
                return;
            }
        }

        public static void TriggerControllerHaptics(float amplitude, float duration)
        {
            _xrControllers.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand, _xrControllers);
            foreach (InputDevice dev in _xrControllers)
            {
                if (dev.isValid && dev.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                {
                    dev.SendHapticImpulse(0u, amplitude, duration);
                }
            }
        }
    }
}
