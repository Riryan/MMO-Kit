using LiteNetLibManager;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace MultiplayerARPG
{
    [RequireComponent(typeof(LiteNetLibIdentity))]
    public partial class AreaBuffEntity : BaseBuffEntity
    {
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
        protected readonly Dictionary<uint, BaseCharacterEntity> _receivingBuffCharacters =
            new Dictionary<uint, BaseCharacterEntity>();

        private bool _serverDestroyed;

        protected override void Awake()
        {
            base.Awake();
            gameObject.layer = PhysicLayers.IgnoreRaycast;
            Identity.onGetInstance.AddListener(OnGetInstance);
        }

        protected virtual void OnDestroy()
        {
            if (Identity != null)
                Identity.onGetInstance.RemoveListener(OnGetInstance);
        }

        public virtual void Setup(
            EntityInfo buffApplier,
            BaseSkill skill,
            int skillLevel,
            bool applyBuffToEveryone,
            float areaDuration,
            float applyDuration)
        {
            base.Setup(buffApplier, skill, skillLevel, applyBuffToEveryone);

            _applyDuration = Mathf.Max(0.01f, applyDuration);
            _lastAppliedTime = Time.unscaledTime;
            _serverDestroyed = false;

            // PoolDescriptor lifetime (kept for compatibility)
            PushBack(areaDuration);

            // HARD server-side lifetime guard (prevents leaks)
            if (IsServer)
                Invoke(nameof(ServerForceDestroy), areaDuration + 0.1f);
        }

        protected virtual void Update()
        {
            // Server-only logic
            if (!IsServer || _serverDestroyed)
                return;

            if (Time.unscaledTime - _lastAppliedTime < _applyDuration)
                return;

            _lastAppliedTime = Time.unscaledTime;

            foreach (BaseCharacterEntity entity in _receivingBuffCharacters.Values)
            {
                if (entity != null)
                    ApplyBuffTo(entity);
            }
        }

        private void ServerForceDestroy()
        {
            if (!IsServer || _serverDestroyed)
                return;

            _serverDestroyed = true;
            Cleanup();
            Identity.NetworkDestroy();
        }

        protected override void OnPushBack()
        {
            Cleanup();
            if (onDestroy != null)
                onDestroy.Invoke();
        }

        private void Cleanup()
        {
            _receivingBuffCharacters.Clear();
            CancelInvoke(nameof(ServerForceDestroy));
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            TriggerEnter(other.gameObject);
        }

        protected virtual void OnTriggerEnter2D(Collider2D other)
        {
            TriggerEnter(other.gameObject);
        }

        protected virtual void TriggerEnter(GameObject other)
        {
            if (!IsServer)
                return;

            BaseCharacterEntity target = other.GetComponent<BaseCharacterEntity>();
            if (target == null)
                return;

            if (_receivingBuffCharacters.ContainsKey(target.ObjectId))
                return;

            _receivingBuffCharacters[target.ObjectId] = target;
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            TriggerExit(other.gameObject);
        }

        protected virtual void OnTriggerExit2D(Collider2D other)
        {
            TriggerExit(other.gameObject);
        }

        protected virtual void TriggerExit(GameObject other)
        {
            if (!IsServer)
                return;

            BaseCharacterEntity target = other.GetComponent<BaseCharacterEntity>();
            if (target == null)
                return;

            _receivingBuffCharacters.Remove(target.ObjectId);
        }

        public override void InitPrefab()
        {
            if (this == null)
            {
                Debug.LogWarning("AreaBuffEntity is null");
                return;
            }

            FxCollection.InitPrefab();

            if (Identity == null)
            {
                LiteNetLibIdentity id = gameObject.AddComponent<LiteNetLibIdentity>();
                FieldInfo prop = typeof(LiteNetLibIdentity)
                    .GetField("assetId", BindingFlags.NonPublic | BindingFlags.Instance);
                prop.SetValue(id, $"AreaBuffEntity_{name}");
            }

            Identity.PoolingSize = PoolSize;
        }

        protected override void PushBack()
        {
            OnPushBack();
            Identity.NetworkDestroy();
        }
    }
}
