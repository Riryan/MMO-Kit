using LiteNetLibManager;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace MultiplayerARPG
{
    [RequireComponent(typeof(LiteNetLibIdentity))]
    public partial class AreaDamageEntity : BaseDamageEntity
    {
        public bool canApplyDamageToUser;
        public bool canApplyDamageToAllies;
        public UnityEvent onDestroy;

        private LiteNetLibIdentity identity;
        public LiteNetLibIdentity Identity
        {
            get
            {
                if (identity == null)
                    identity = GetComponent<LiteNetLibIdentity>();
                return identity;
            }
        }

        protected float _applyDuration;
        protected float _lastAppliedTime;

        protected readonly Dictionary<uint, DamageableHitBox> _receivingDamageHitBoxes =
            new Dictionary<uint, DamageableHitBox>();

        protected override void Awake()
        {
            base.Awake();
            Identity.onGetInstance.AddListener(OnGetInstance);
        }

        protected virtual void OnDestroy()
        {
            if (Identity != null)
                Identity.onGetInstance.RemoveListener(OnGetInstance);
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
            float areaDuration,
            float applyDuration)
        {
            Setup(instigator, weapon, simulateSeed, triggerIndex, spreadIndex,
                damageAmounts, skill, skillLevel, hitRegisterData);

            _applyDuration = Mathf.Max(0.01f, applyDuration);
            _lastAppliedTime = Time.unscaledTime;

            // Pool-controlled lifetime ONLY
            PushBack(areaDuration);
        }

        protected virtual void Update()
        {
            if (!IsServer)
                return;

            if (Time.unscaledTime - _lastAppliedTime < _applyDuration)
                return;

            _lastAppliedTime = Time.unscaledTime;

            foreach (DamageableHitBox hitBox in _receivingDamageHitBoxes.Values)
            {
                if (hitBox != null)
                    ApplyDamageTo(hitBox);
            }
        }

        protected override void OnPushBack()
        {
            _receivingDamageHitBoxes.Clear();
            onDestroy?.Invoke();
            base.OnPushBack();
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!IsServer)
                return;

            DamageableHitBox target = other.GetComponent<DamageableHitBox>();
            if (target == null)
                return;

            uint id = target.GetObjectId();
            if (!_receivingDamageHitBoxes.ContainsKey(id))
                _receivingDamageHitBoxes[id] = target;
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            if (!IsServer)
                return;

            IDamageableEntity target = other.GetComponent<IDamageableEntity>();
            if (!target.IsNull())
                _receivingDamageHitBoxes.Remove(target.GetObjectId());
        }

        public override void InitPrefab()
        {
            FxCollection.InitPrefab();

            if (Identity == null)
            {
                LiteNetLibIdentity id = gameObject.AddComponent<LiteNetLibIdentity>();
                FieldInfo prop = typeof(LiteNetLibIdentity)
                    .GetField("assetId", BindingFlags.NonPublic | BindingFlags.Instance);
                prop.SetValue(id, $"AreaDamageEntity_{name}");
            }

            Identity.PoolingSize = PoolSize;
        }
    }
}
