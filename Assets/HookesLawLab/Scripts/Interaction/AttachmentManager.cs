using System;
using System.Collections.Generic;
using UnityEngine;
using HookesLawLab.Physics;
using HookesLawLab.Lab;

namespace HookesLawLab.Interaction
{
    /// <summary>
    /// Handles proximity snapping (HookAttachmentPoint -> SpringAttachmentPoint),
    /// vertical multi-mass stacking, smooth detachment, and full reset of calibration weights.
    /// </summary>
    public class AttachmentManager : MonoBehaviour
    {
        public float snapThreshold = 0.35f;
        public bool audioEnabled = true;

        public SpringSimulation SpringSim { get; private set; }
        public SpringVisual SpringVisual { get; private set; }
        public List<MassInteractable> Masses { get; } = new List<MassInteractable>();
        public List<MassInteractable> AttachedMasses { get; } = new List<MassInteractable>();

        public event Action OnMassChanged;
        public event Action<MassInteractable> OnMassPickedUp;
        public event Action<MassInteractable, bool> OnMassSnapZoneChanged;
        public event Action<MassInteractable> OnMassAttached;
        public event Action<MassInteractable> OnMassReturnedOutsideSnap;
        public event Action<MassInteractable> OnMassDetached;

        private AudioSource _audioSource;
        private readonly Dictionary<MassInteractable, bool> _snapStateCache = new Dictionary<MassInteractable, bool>();

        public void Initialize(SpringSimulation sim, SpringVisual visual, IEnumerable<MassInteractable> masses)
        {
            SpringSim = sim;
            SpringVisual = visual;
            Masses.Clear();
            Masses.AddRange(masses);
            Masses.Sort((a, b) => a.massG.CompareTo(b.massG));

            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;
        }

        public void PlaySnapSound()
        {
            if (!audioEnabled || _audioSource == null) return;
            _audioSource.PlayOneShot(LabAudioSynthesizer.GetChordClip(660f, 990f, 1320f, 0.22f));
        }

        public void PlayPickupSound()
        {
            if (!audioEnabled || _audioSource == null) return;
            _audioSource.PlayOneShot(LabAudioSynthesizer.GetToneClip(440f, 0.09f, 0.14f));
        }

        public void PlayDetachSound()
        {
            if (!audioEnabled || _audioSource == null) return;
            _audioSource.PlayOneShot(LabAudioSynthesizer.GetChordClip(550f, 370f, 0f, 0.16f));
        }

        public void NotifyMassPickedUp(MassInteractable interactable)
        {
            if (interactable != null) OnMassPickedUp?.Invoke(interactable);
        }

        public void NotifyMassReturnedOutsideSnap(MassInteractable interactable)
        {
            if (interactable != null)
            {
                _snapStateCache[interactable] = false;
                OnMassReturnedOutsideSnap?.Invoke(interactable);
            }
        }

        public bool CheckSnapProximity(MassInteractable interactable, Ray? pointerRay = null)
        {
            if (interactable == null || interactable.State == MassInteractionState.Attached || SpringVisual == null)
                return false;

            Vector3 springAttachPos = SpringVisual.SpringAttachmentPoint != null
                ? SpringVisual.SpringAttachmentPoint.position
                : SpringVisual.GetHangerSnapPosition();

            Vector3 hookPos = interactable.HookAttachmentPoint != null
                ? interactable.HookAttachmentPoint.position
                : interactable.transform.position;

            float hookDist = Vector3.Distance(springAttachPos, hookPos);
            float bodyDist = Vector3.Distance(springAttachPos, interactable.transform.position);
            bool inRange = Mathf.Min(hookDist, bodyDist) <= snapThreshold;

            if (!inRange && pointerRay.HasValue)
            {
                float rayDist = DistanceRayToPoint(pointerRay.Value, springAttachPos);
                if (rayDist <= snapThreshold * 0.85f)
                {
                    inRange = true;
                }
            }

            if (inRange)
            {
                interactable.State = MassInteractionState.Attaching;
                SpringVisual.SetSnapHaloHighlight(true);
            }
            else
            {
                if (interactable.State == MassInteractionState.Attaching)
                {
                    interactable.State = MassInteractionState.Grabbed;
                }
                SpringVisual.SetSnapHaloHighlight(false);
            }

            bool prevSnap = _snapStateCache.TryGetValue(interactable, out bool cached) && cached;
            if (prevSnap != inRange)
            {
                _snapStateCache[interactable] = inRange;
                OnMassSnapZoneChanged?.Invoke(interactable, inRange);
            }

            return inRange;
        }

        public void AttachMass(MassInteractable interactable, bool stack = true)
        {
            if (interactable == null || SpringVisual == null || SpringSim == null) return;
            if (AttachedMasses.Contains(interactable)) return;

            if (!stack && AttachedMasses.Count > 0)
            {
                DetachAllMasses(playSound: false, immediate: false);
            }

            AttachedMasses.Add(interactable);
            _snapStateCache[interactable] = false;
            interactable.SetAttachedPhysicsState();
            interactable.SetHovered(false);
            interactable.SetGrabbed(false);

            Transform container = SpringVisual.AttachedMassContainer;
            interactable.transform.SetParent(container, false);
            interactable.transform.localRotation = Quaternion.identity;

            RepackAttachedStack();

            float totalMassKg = GetTotalAttachedMassKg();
            SpringSim.SetMass(totalMassKg, perturb: true);

            SpringVisual.SetSnapHaloHighlight(false);
            PlaySnapSound();
            OnMassAttached?.Invoke(interactable);
            OnMassChanged?.Invoke();
        }

        private void RepackAttachedStack()
        {
            float currentDropY = 0f;
            foreach (MassInteractable m in AttachedMasses)
            {
                // Align HookAttachmentPoint cleanly with current attachment vertical level
                float hookLocalY = m.HookAttachmentPoint != null
                    ? m.HookAttachmentPoint.localPosition.y
                    : m.totalHeight;
                m.transform.localPosition = new Vector3(0f, currentDropY - hookLocalY, 0f);
                m.transform.localRotation = Quaternion.identity;

                // Next weight hangs right below the bottom of this cylinder plus a small hook link
                currentDropY -= (hookLocalY + 0.010f);
            }
        }

        public void AttachMassByGrams(int grams)
        {
            MassInteractable target = Masses.Find(m => m.massG == grams);
            if (target != null && !AttachedMasses.Contains(target))
            {
                AttachMass(target, stack: true);
            }
        }

        public void DetachMass(MassInteractable interactable, bool returnToTray = true)
        {
            if (interactable == null) return;
            if (!AttachedMasses.Remove(interactable)) return;

            if (returnToTray)
            {
                interactable.ReturnToTraySmooth();
                PlayDetachSound();
            }

            RepackAttachedStack();
            SpringSim.SetMass(GetTotalAttachedMassKg(), perturb: true);
            OnMassDetached?.Invoke(interactable);
            OnMassChanged?.Invoke();
        }

        public void DetachLowestMass()
        {
            if (AttachedMasses.Count == 0) return;
            DetachMass(AttachedMasses[AttachedMasses.Count - 1], returnToTray: true);
        }

        public void DetachAllMasses(bool playSound = true, bool immediate = false)
        {
            foreach (MassInteractable m in Masses)
            {
                if (m == null) continue;
                if (immediate)
                    m.ReturnToTrayImmediate();
                else if (AttachedMasses.Contains(m) || m.State != MassInteractionState.Available)
                    m.ReturnToTraySmooth();
            }

            AttachedMasses.Clear();
            if (playSound) PlayDetachSound();
            SpringSim?.SetMass(0f, perturb: true);
            SpringVisual?.SetSnapHaloHighlight(false);
            OnMassChanged?.Invoke();
        }

        public float GetTotalAttachedMassKg()
        {
            float sum = 0f;
            foreach (MassInteractable m in AttachedMasses) sum += m.massKg;
            return sum;
        }

        public int GetTotalAttachedMassG()
        {
            return Mathf.RoundToInt(GetTotalAttachedMassKg() * 1000f);
        }

        public static float DistanceRayToPoint(Ray ray, Vector3 point)
        {
            return Vector3.Cross(ray.direction, point - ray.origin).magnitude;
        }
    }
}
