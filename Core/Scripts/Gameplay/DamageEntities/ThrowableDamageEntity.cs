using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MultiplayerARPG
{
    public class ThrowableDamageEntity : BaseDamageEntity
    {
        public bool canApplyDamageToUser;
        public bool canApplyDamageToAllies;
        public float destroyDelay;
        public UnityEvent onExploded;
        public UnityEvent onDestroy;
        public float explodeDistance;

        public Rigidbody CacheRigidbody { get; private set; }
        public Rigidbody2D CacheRigidbody2D { get; private set; }

        protected float _throwForce;
        protected float _lifetime;
        protected float _throwedTime;
        protected bool _isExploded;
        protected bool _destroying;

        protected readonly HashSet<uint> _alreadyHitObjects = new HashSet<uint>();

        protected Collider[] _colliders;
        protected Collider2D[] _colliders2D;

        protected bool _exittedThrower;
        protected bool _readyToHitWalls;

        protected override void Awake()
        {
            base.Awake();

            CacheRigidbody = GetComponent<Rigidbody>();
            CacheRigidbody2D = GetComponent<Rigidbody2D>();

            _colliders = GetComponents<Collider>();
            _colliders2D = GetComponents<Collider2D>();

            _readyToHitWalls = true;
            SetReadyToHitWalls(false);
            _exittedThrower = false;
        }

        public void SetReadyToHitWalls(bool isReady)
        {
            if (_readyToHitWalls == isReady)
                return;

            for (int i = 0; i < _colliders.Length; ++i)
                _colliders[i].isTrigger = !isReady;

            for (int i = 0; i < _colliders2D.Length; ++i)
                _colliders2D[i].isTrigger = !isReady;

            _readyToHitWalls = isReady;
        }

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
            float throwForce,
            float lifetime)
        {
            Setup(instigator, weapon, simulateSeed, triggerIndex, spreadIndex,
                damageAmounts, skill, skillLevel, hitRegisterData);

            _throwForce = throwForce;
            _lifetime = lifetime;
            _throwedTime = Time.unscaledTime;

            _alreadyHitObjects.Clear();
            _isExploded = false;
            _destroying = false;

            // Apply force (visual + physics, but damage is server-only)
            if (CurrentGameInstance.DimensionType == DimensionType.Dimension2D)
            {
                CacheRigidbody2D.velocity = Vector2.zero;
                CacheRigidbody2D.angularVelocity = 0f;
                CacheRigidbody2D.AddForce(CacheTransform.forward * _throwForce, ForceMode2D.Impulse);
            }
            else
            {
                CacheRigidbody.velocity = Vector3.zero;
                CacheRigidbody.angularVelocity = Vector3.zero;
                CacheRigidbody.AddForce(CacheTransform.forward * _throwForce, ForceMode.Impulse);
            }
        }

        protected virtual void Update()
        {
            // SERVER ONLY lifetime
            if (!IsServer || _destroying)
                return;

            if (Time.unscaledTime - _throwedTime >= _lifetime)
            {
                Explode();
                PushBack(destroyDelay);
                _destroying = true;
            }
        }

        protected virtual void Explode()
        {
            if (_isExploded)
                return;

            _isExploded = true;
            onExploded?.Invoke();

            if (!IsServer)
                return;

            ApplyExplosionDamage();
        }

        protected virtual void ApplyExplosionDamage()
        {
            _alreadyHitObjects.Clear();

            if (CurrentGameInstance.DimensionType == DimensionType.Dimension2D)
            {
                var cols = Physics2D.OverlapCircleAll(CacheTransform.position, explodeDistance);
                foreach (var c in cols)
                    TryApplyDamage(c.gameObject);
            }
            else
            {
                var cols = Physics.OverlapSphere(CacheTransform.position, explodeDistance);
                foreach (var c in cols)
                    TryApplyDamage(c.gameObject);
            }
        }

        protected bool TryApplyDamage(GameObject other)
        {
            if (!FindTargetHitBox(other, out DamageableHitBox target))
                return false;

            uint id = target.GetObjectId();
            if (_alreadyHitObjects.Contains(id))
                return false;

            target.ReceiveDamageWithoutConditionCheck(
                CacheTransform.position,
                _instigator,
                _damageAmounts,
                _weapon,
                _skill,
                _skillLevel,
                Random.Range(0, 255));

            _alreadyHitObjects.Add(id);
            return true;
        }

        protected bool FindTargetHitBox(GameObject other, out DamageableHitBox target)
        {
            target = null;

            if (!other.GetComponent<IUnHittable>().IsNull())
                return false;

            target = other.GetComponent<DamageableHitBox>();
            if (target == null || target.IsDead() || target.IsImmune || target.IsInSafeArea)
                return false;

            if (target.GetObjectId() == _instigator.ObjectId)
                return canApplyDamageToUser;

            if (target.DamageableEntity is BaseCharacterEntity c && c.IsAlly(_instigator))
                return canApplyDamageToAllies;

            return true;
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            HandleEnter(other.transform, other.isTrigger);
        }

        protected virtual void OnTriggerEnter2D(Collider2D other)
        {
            HandleEnter(other.transform, other.isTrigger);
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            HandleExit(other.transform);
        }

        protected virtual void OnTriggerExit2D(Collider2D other)
        {
            HandleExit(other.transform);
        }

        protected void HandleEnter(Transform other, bool otherIsTrigger)
        {
            if (otherIsTrigger || _exittedThrower)
                return;

            if (!_instigator.TryGetEntity(out BaseGameEntity entity))
            {
                _exittedThrower = true;
                SetReadyToHitWalls(true);
                return;
            }

            if (other.root != entity.EntityTransform.root)
            {
                _exittedThrower = true;
                SetReadyToHitWalls(true);
            }
        }

        protected void HandleExit(Transform other)
        {
            if (_exittedThrower)
                return;

            if (!_instigator.TryGetEntity(out BaseGameEntity entity))
            {
                _exittedThrower = true;
                SetReadyToHitWalls(true);
                return;
            }

            if (other.root == entity.EntityTransform.root)
            {
                _exittedThrower = true;
                SetReadyToHitWalls(true);
            }
        }

        protected override void OnPushBack()
        {
            _readyToHitWalls = true;
            SetReadyToHitWalls(false);
            _exittedThrower = false;

            onDestroy?.Invoke();
            base.OnPushBack();
        }
    }
}
