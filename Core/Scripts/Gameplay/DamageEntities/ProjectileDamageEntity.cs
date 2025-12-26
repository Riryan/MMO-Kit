using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace MultiplayerARPG
{
    public partial class ProjectileDamageEntity : MissileDamageEntity
    {
        public UnityEvent onProjectileDisappear = new UnityEvent();

        [Header("Configuration")]
        public LayerMask hitLayers;
        [FormerlySerializedAs("ProjectileObject")]
        public GameObject projectileObject;
        public bool hasGravity;
        public Vector3 customGravity;

        [Header("Angle / Speed")]
        public bool useAngle;
        [Range(0, 89)]
        public float angle;
        public bool recalculateSpeed;

        [Header("Effects")]
        public bool instantiateImpact;
        public GameObject impactEffect;
        public bool useNormal;
        public bool stickToHitObject;
        public bool instantiateDisappear;
        public GameObject disappearEffect;

        private Vector3 _initialPosition;
        private Vector3 _defaultImpactEffectPosition;
        private Vector3 _bulletVelocity;
        private Vector3 _normal;
        private Vector3 _hitPos;

        public override void Setup(
            EntityInfo instigator,
            CharacterItem weapon,
            int simulateSeed,
            byte triggerIndex,
            byte spreadIndex,
            Dictionary<DamageElement, MinMaxFloat> damageAmounts,
            BaseSkill skill,
            int skillLevel,
            HitRegisterData hitRegisterData,
            float missileDistance,
            float missileSpeed,
            IDamageableEntity lockingTarget)
        {
            base.Setup(instigator, weapon, simulateSeed, triggerIndex, spreadIndex,
                damageAmounts, skill, skillLevel, hitRegisterData,
                missileDistance, missileSpeed, lockingTarget);

            _initialPosition = CacheTransform.position;

            // Client-only visual setup
            if (IsClient)
            {
                if (projectileObject)
                    projectileObject.SetActive(true);

                if (impactEffect && !instantiateImpact)
                {
                    impactEffect.SetActive(false);
                    _defaultImpactEffectPosition = impactEffect.transform.localPosition;
                }

                if (disappearEffect && !instantiateDisappear)
                    disappearEffect.SetActive(false);
            }

            // Server-side trajectory
            Vector3 targetPos = _initialPosition + CacheTransform.forward * missileDistance;
            if (lockingTarget != null && lockingTarget.CurrentHp > 0)
                targetPos = lockingTarget.GetTransform().position;

            float dist = Vector3.Distance(_initialPosition, targetPos);
            float yOffset = -CacheTransform.forward.y;

            Vector3 gravity = Vector3.zero;
            if (hasGravity)
                gravity = customGravity != Vector3.zero ? customGravity : Physics.gravity;

            if (recalculateSpeed)
                missileSpeed = LaunchSpeed(dist, yOffset, gravity.magnitude, angle * Mathf.Deg2Rad);

            if (useAngle)
                CacheTransform.eulerAngles = new Vector3(
                    CacheTransform.eulerAngles.x - angle,
                    CacheTransform.eulerAngles.y,
                    CacheTransform.eulerAngles.z);

            _bulletVelocity = CacheTransform.forward * missileSpeed;
        }

        protected override void Update()
        {
            // SERVER ONLY
            if (!IsServer || _destroying)
                return;

            if (hasGravity)
            {
                Vector3 gravity = customGravity != Vector3.zero ? customGravity : Physics.gravity;
                _bulletVelocity += gravity * Time.deltaTime;
            }

            HitDetect();

            if (_destroying)
                return;

            CacheTransform.rotation = Quaternion.LookRotation(_bulletVelocity);
            CacheTransform.position += _bulletVelocity * Time.deltaTime;

            if (Vector3.Distance(_initialPosition, CacheTransform.position) > _missileDistance &&
                Time.unscaledTime - _launchTime >= _missileDuration)
            {
                NoImpact();
            }
        }

        public override void HitDetect()
        {
            if (!IsServer || _destroying || !_previousPosition.HasValue)
                return;

            int hitCount = 0;
            Vector3 dir = (CacheTransform.position - _previousPosition.Value).normalized;
            float dist = Vector3.Distance(CacheTransform.position, _previousPosition.Value);

            switch (hitDetectionMode)
            {
                case HitDetectionMode.Raycast:
                    hitCount = Physics.RaycastNonAlloc(_previousPosition.Value, dir, _hits3D, dist, hitLayers);
                    break;
                case HitDetectionMode.SphereCast:
                    hitCount = Physics.SphereCastNonAlloc(_previousPosition.Value, sphereCastRadius, dir, _hits3D, dist, hitLayers);
                    break;
                case HitDetectionMode.BoxCast:
                    hitCount = Physics.BoxCastNonAlloc(_previousPosition.Value,
                        boxCastSize * 0.5f, dir, _hits3D, CacheTransform.rotation, dist, hitLayers);
                    break;
            }

            for (int i = 0; i < hitCount; ++i)
            {
                RaycastHit hit = _hits3D[i];
                if (hit.transform == null)
                    continue;

                if (!hit.transform.gameObject.GetComponent<IUnHittable>().IsNull())
                    continue;

                if (useNormal)
                    _normal = hit.normal;

                _hitPos = hit.point;
                Impact(hit.collider.gameObject);

                if (_destroying)
                    break;
            }

            _previousPosition = CacheTransform.position;
        }

        private void Impact(GameObject hitted)
        {
            if (_destroying)
                return;

            if (FindTargetHitBox(hitted, true, out DamageableHitBox target))
            {
                if (explodeDistance <= 0f && _alreadyHitObjects.Add(target.GetObjectId()))
                    ApplyDamageTo(target);

                OnHit(hitted);
                return;
            }

            if (hitted.GetComponent<DamageableEntity>() != null)
                return;

            OnHit(hitted);
        }

        private void OnHit(GameObject hitted)
        {
            // Client FX only
            if (IsClient && impactEffect)
            {
                if (projectileObject)
                    projectileObject.SetActive(false);

                if (instantiateImpact)
                {
                    Quaternion rot = useNormal
                        ? Quaternion.FromToRotation(Vector3.forward, _normal)
                        : Quaternion.identity;

                    GameObject fx = Object.Instantiate(impactEffect, _hitPos, rot);
                    if (stickToHitObject)
                        fx.transform.SetParent(hitted.transform);
                }
                else
                {
                    if (useNormal)
                        impactEffect.transform.rotation =
                            Quaternion.FromToRotation(Vector3.forward, _normal);

                    impactEffect.transform.position = _hitPos;
                    if (stickToHitObject)
                        impactEffect.transform.SetParent(hitted.transform);

                    impactEffect.SetActive(true);
                }
            }

            if (explodeDistance > 0f)
                Explode();

            PushBack(destroyDelay);
            _destroying = true;
        }

        private void NoImpact()
        {
            if (_destroying)
                return;

            if (IsClient && disappearEffect)
            {
                onProjectileDisappear?.Invoke();

                if (projectileObject)
                    projectileObject.SetActive(false);

                if (instantiateDisappear)
                    Object.Instantiate(disappearEffect, transform.position, CacheTransform.rotation);
                else
                    disappearEffect.SetActive(true);
            }

            PushBack();
            _destroying = true;
        }

        protected override void OnPushBack()
        {
            if (impactEffect && stickToHitObject && !instantiateImpact)
            {
                impactEffect.transform.SetParent(CacheTransform);
                impactEffect.transform.localPosition = _defaultImpactEffectPosition;
            }

            base.OnPushBack();
        }

        public float LaunchSpeed(float distance, float yOffset, float gravity, float angle)
        {
            return (distance * Mathf.Sqrt(gravity) * Mathf.Sqrt(1f / Mathf.Cos(angle))) /
                   Mathf.Sqrt(2f * distance * Mathf.Sin(angle) + 2f * yOffset * Mathf.Cos(angle));
        }
    }
}
