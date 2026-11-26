using System;
using UnityEngine;

namespace HookesLawLab.Physics
{
    /// <summary>
    /// Snapshot of the Hooke's Law harmonic oscillator state.
    /// </summary>
    [Serializable]
    public struct SpringState
    {
        public float massKg;
        public int massG;
        public float springConstant;
        public float gravity;
        public float displacementM;
        public float displacementCm;
        public float appliedForceN;
        public float restoringForceN;
        public float potentialEnergyJ;
        public float frequencyHz;
        public bool isAtEquilibrium;
        public bool isExceedingElasticLimit;
        public float calculatedK;
        public float velocity;
    }

    /// <summary>
    /// High-precision, 120 Hz sub-stepped damped harmonic oscillator implementing Hooke's Law:
    /// F = -k*x, F_applied = m*g, equilibrium m*g = k*x.
    /// </summary>
    public class SpringSimulation : MonoBehaviour
    {
        [Header("Physical Constants")]
        public float gravity = 9.81f;
        public float springConstant = 25.0f;
        public float naturalLength = 0.672f;
        public float elasticLimit = 0.48f;
        public float dampingRatio = 0.40f;
        public float springEffectiveMass = 0.025f;

        [Header("Dynamic State (Read-Only)")]
        [SerializeField] private float attachedMass;
        [SerializeField] private float displacement;
        [SerializeField] private float velocity;
        [SerializeField] private float acceleration;
        [SerializeField] private bool isAtEquilibrium = true;
        [SerializeField] private bool isExceedingElasticLimit;

        private const float FixedTimeStep = 1f / 120f;
        private float _accumulator;

        public float AttachedMassKg => attachedMass;
        public float Displacement => displacement;
        public float Velocity => velocity;
        public bool IsAtEquilibrium => isAtEquilibrium;
        public bool IsExceedingElasticLimit => isExceedingElasticLimit;

        public event Action<SpringState> OnStateChanged;

        public void SetMass(float massKg, bool perturb = true)
        {
            float prevMass = attachedMass;
            attachedMass = Mathf.Max(0f, massKg);
            isAtEquilibrium = false;

            if (perturb && Mathf.Abs(attachedMass - prevMass) > 0.001f)
            {
                if (attachedMass > prevMass)
                {
                    velocity += (attachedMass - prevMass) * 0.4f;
                }
            }
        }

        public void SetSpringConstant(float k)
        {
            springConstant = Mathf.Clamp(k, 5.0f, 100.0f);
            isAtEquilibrium = false;
        }

        public void SetGravity(float g)
        {
            gravity = Mathf.Max(0f, g);
            isAtEquilibrium = false;
        }

        public void SetDamping(float d)
        {
            dampingRatio = Mathf.Clamp(d, 0.05f, 3.0f);
        }

        public void SettleToEquilibrium()
        {
            displacement = GetEquilibriumDisplacement();
            velocity = 0f;
            acceleration = 0f;
            isAtEquilibrium = true;
            Notify();
        }

        public float GetEquilibriumDisplacement()
        {
            if (springConstant <= 0f) return 0f;
            return (attachedMass * gravity) / springConstant;
        }

        public float GetAppliedForce() => attachedMass * gravity;
        public float GetRestoringForce() => -springConstant * displacement;
        public float GetRestoringForceMagnitude() => springConstant * Mathf.Abs(displacement);
        public float GetPotentialEnergy() => 0.5f * springConstant * displacement * displacement;

        public float GetFrequency()
        {
            float totalM = attachedMass > 0f
                ? attachedMass + springEffectiveMass / 3f
                : springEffectiveMass;
            if (totalM <= 0f) return 0f;
            return (1f / (2f * Mathf.PI)) * Mathf.Sqrt(springConstant / totalM);
        }

        public void StepSimulation(float deltaTime)
        {
            float clampedDt = Mathf.Min(deltaTime, 0.1f);
            _accumulator += clampedDt;

            while (_accumulator >= FixedTimeStep)
            {
                IntegrateStep(FixedTimeStep);
                _accumulator -= FixedTimeStep;
            }

            isExceedingElasticLimit = displacement >= elasticLimit;

            float targetEq = GetEquilibriumDisplacement();
            float posDiff = Mathf.Abs(displacement - targetEq);
            if (posDiff < 0.0002f && Mathf.Abs(velocity) < 0.001f)
            {
                displacement = targetEq;
                velocity = 0f;
                acceleration = 0f;
                isAtEquilibrium = true;
            }
            else
            {
                isAtEquilibrium = false;
            }

            Notify();
        }

        private void IntegrateStep(float dt)
        {
            float m = attachedMass > 0f
                ? (attachedMass + springEffectiveMass / 3f)
                : springEffectiveMass;
            float k = springConstant;

            float criticalDamping = 2f * Mathf.Sqrt(k * m);
            float c = dampingRatio * criticalDamping;

            float fGravity = attachedMass * gravity;
            float fSpring = -k * displacement;
            float fDamp = -c * velocity;

            float netForce = fGravity + fSpring + fDamp;

            if (attachedMass <= 0f && displacement <= 0f)
            {
                netForce -= displacement * k * 4.0f;
            }

            acceleration = netForce / m;
            velocity += acceleration * dt;
            displacement += velocity * dt;

            if (displacement > 0.65f)
            {
                displacement = 0.65f;
                velocity = -velocity * 0.2f;
            }
        }

        public SpringState GetState()
        {
            float appliedF = GetAppliedForce();
            float restoringF = GetRestoringForce();
            float xM = displacement;
            float calcK = xM > 0.001f ? (appliedF / xM) : springConstant;

            return new SpringState
            {
                massKg = attachedMass,
                massG = Mathf.RoundToInt(attachedMass * 1000f),
                springConstant = springConstant,
                gravity = gravity,
                displacementM = xM,
                displacementCm = xM * 100f,
                appliedForceN = appliedF,
                restoringForceN = restoringF,
                potentialEnergyJ = GetPotentialEnergy(),
                frequencyHz = GetFrequency(),
                isAtEquilibrium = isAtEquilibrium,
                isExceedingElasticLimit = isExceedingElasticLimit,
                calculatedK = calcK,
                velocity = velocity
            };
        }

        private void Notify()
        {
            OnStateChanged?.Invoke(GetState());
        }
    }
}
