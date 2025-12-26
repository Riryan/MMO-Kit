using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MultiplayerARPG
{
    public partial class MissileDamageEntity : BaseDamageEntity
    {
        public enum HitDetectionMode { Raycast, SphereCast, BoxCast }
        public HitDetectionMode hitDetectionMode = HitDetectionMode.Raycast;
        public float sphereCastRadius = 1f;
        public Vector3 boxCastSize = Vector3.one;
        public float destroyDelay;
        public UnityEvent onExploded;
        public UnityEvent onDestroy;
        [Tooltip("If > 0, explodes and applies AoE damage")]
        public float explodeDistance;

        protected float _missileDistance;
        protected float _missileSpeed;
        protected IDamageableEntity _lockingTarget;
        protected float _launchTime;
        protected float _missileDuration;

        protected Vector3? _previousPosition;
        protected readonly RaycastHit2D[] _hits2D = new RaycastHit2D[8];
        protected readonly RaycastHit[] _hits3D = new RaycastHit[8];
        protected readonly HashSet<uint> _alreadyHitObjects = new HashSet<uint>();

        protected bool _isExploded;
        protected bool _destroying;

        public virtual void Setup(
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
            Setup(instigator, weapon, simulateSeed, triggerIndex, spreadIndex,
                damageAmounts, skill, skillLevel, hitRegisterData);

            _missileDistance = missileDistance;
            _missileSpeed = missileSpeed;
            _lockingTarget = lockingTarget;

            _alreadyHitObjects.Clear();
            _isExploded = false;
            _destroying = false;

            _launchTime = Time.unscaledTime;
            _missileDuration = (missileDistance > 0f && missileSpeed > 0f)
                ? (missileDistance / missileSpeed) + 0.1f
                : 0.1f;

            // Immediate explode (edge case)
            if (missileDistance <= 0f && missileSpeed <= 0f)
            {
                Explode();
                PushBack(destroyDelay);
                _destroying = true;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _previousPosition = CacheTransform.position;
        }

        protected virtual void Update()
        {
            // SERVER ONLY
            if (!IsServer || _destroying)
                return;

            // Lifetime end
            if (Time.unscaledTime - _launchTime >= _missileDuration)
            {
                Explode();
                PushBack(destroyDelay);
                _destroying = true;
                return;
            }

            HitDetect();

            if (_destroying)
                return;

            // Move
            if (CurrentGameInstance.DimensionType == DimensionType.Dimension2D)
                CacheTransform.position += -CacheTransform.up * _missileSpeed * Time.deltaTime;
            else
                CacheTransform.position += CacheTransform.forward * _missileSpeed * Time.deltaTime;
        }

        public virtual void HitDetect()
        {
            if (_destroying || !_previousPosition.HasValue)
                return;

            int hitCount = 0;
            int layerMask = GameInstance.Singleton.GetDamageEntityHitLayerMask();
            Vector3 dir = (CacheTransform.position - _previousPosition.Value).normalized;
            float dist = Vector3.Distance(CacheTransform.position, _previousPosition.Value);

            switch (hitDetectionMode)
            {
                case HitDetectionMode.Raycast:
                    hitCount = (CurrentGameInstance.DimensionType == DimensionType.Dimension2D)
                        ? Physics2D.RaycastNonAlloc(_previousPosition.Value, dir, _hits2D, dist, layerMask)
                        : Physics.RaycastNonAlloc(_previousPosition.Value, dir, _hits3D, dist, layerMask);
                    break;

                case HitDetectionMode.SphereCast:
                    hitCount = (CurrentGameInstance.DimensionType == DimensionType.Dimension2D)
                        ? Physics2D.CircleCastNonAlloc(_previousPosition.Value, sphereCastRadius, dir, _hits2D, dist, layerMask)
                        : Physics.SphereCastNonAlloc(_previousPosition.Value, sphereCastRadius, dir, _hits3D, dist, layerMask);
                    break;

                case HitDetectionMode.BoxCast:
                    hitCount = (CurrentGameInstance.DimensionType == DimensionType.Dimension2D)
                        ? Physics2D.BoxCastNonAlloc(_previousPosition.Value,
                            new Vector2(boxCastSize.x, boxCastSize.y), 0f, dir, _hits2D, dist, layerMask)
                        : Physics.BoxCastNonAlloc(_previousPosition.Value,
                            boxCastSize * 0.5f, dir, _hits3D, CacheTransform.rotation, dist, layerMask);
                    break;
            }

            for (int i = 0; i < hitCount; ++i)
            {
                if (CurrentGameInstance.DimensionType == DimensionType.Dimension2D)
                {
                    if (_hits2D[i].transform != null)
                        TriggerEnter(_hits2D[i].transform.gameObject);
                }
                else
                {
                    if (_hits3D[i].transform != null)
                        TriggerEnter(_hits3D[i].transform.gameObject);
                }

                if (_destroying)
                    break;
            }

            _previousPosition = CacheTransform.position;
        }

        protected virtual void TriggerEnter(GameObject other)
        {
            if (_destroying)
                return;

            if (!other.GetComponent<IUnHittable>().IsNull())
                return;

            if (FindTargetHitBox(other, true, out DamageableHitBox target))
            {
                if (explodeDistance <= 0f)
                {
                    uint id = target.GetObjectId();
                    if (_alreadyHitObjects.Add(id))
                        ApplyDamageTo(target);
                }
                else
                {
                    Explode();
                }

                PushBack(destroyDelay);
                _destroying = true;
                return;
            }

            if (!CurrentGameInstance.IsDamageableLayer(other.layer) &&
                !CurrentGameInstance.IgnoreRaycastLayersValues.Contains(other.layer))
            {
                if (explodeDistance > 0f)
                    Explode();

                PushBack(destroyDelay);
                _destroying = true;
            }
        }

        protected virtual bool FindTargetHitBox(
            GameObject other,
            bool checkLockingTarget,
            out DamageableHitBox target)
        {
            target = null;

            if (!other.GetComponent<IUnHittable>().IsNull())
                return false;

            target = other.GetComponent<DamageableHitBox>();
            if (target == null || target.IsDead() || !target.CanReceiveDamageFrom(_instigator))
            {
                target = null;
                return false;
            }

            if (checkLockingTarget && _lockingTarget != null &&
                _lockingTarget.GetObjectId() != target.GetObjectId())
            {
                target = null;
                return false;
            }

            return true;
        }

        protected virtual bool FindAndApplyDamage(
            GameObject other,
            bool checkLockingTarget,
            HashSet<uint> alreadyHitObjects)
        {
            if (FindTargetHitBox(other, checkLockingTarget, out DamageableHitBox target))
            {
                uint id = target.GetObjectId();
                if (alreadyHitObjects.Contains(id))
                    return false;

                alreadyHitObjects.Add(id);
                ApplyDamageTo(target);
                return true;
            }
            return false;
        }

        protected virtual void Explode()
        {
            if (_isExploded)
                return;

            _isExploded = true;
            onExploded?.Invoke();

            if (explodeDistance <= 0f || !IsServer)
                return;

            if (CurrentGameInstance.DimensionType == DimensionType.Dimension2D)
            {
                var cols = Physics2D.OverlapCircleAll(CacheTransform.position, explodeDistance);
                foreach (var c in cols)
                    FindAndApplyDamage(c.gameObject, false, _alreadyHitObjects);
            }
            else
            {
                var cols = Physics.OverlapSphere(CacheTransform.position, explodeDistance);
                foreach (var c in cols)
                    FindAndApplyDamage(c.gameObject, false, _alreadyHitObjects);
            }
        }

        protected override void OnPushBack()
        {
            _previousPosition = null;
            onDestroy?.Invoke();
            base.OnPushBack();
        }
    }
}
