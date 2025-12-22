using System.Collections.Generic;
using Cysharp.Text;
using LiteNetLibManager;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace MultiplayerARPG
{
    public class ItemsContainerEntity : BaseGameEntity, IActivatableEntity
    {
        public const float GROUND_DETECTION_Y_OFFSETS = 3f;
        private static readonly RaycastHit[] s_findGroundRaycastHits = new RaycastHit[4];

        [Category(5, "Items Container Settings")]
        [Tooltip("Delay before the entity destroyed, you may set some delay to play destroyed animation by `onItemDropDestroy` event before it's going to be destroyed from the game.")]
        [SerializeField]
        protected float destroyDelay = 0f;
        [SerializeField]
        [Tooltip("Format => {0} = {Title}")]
        protected UILocaleKeySetting formatKeyCorpseTitle = new UILocaleKeySetting(UIFormatKeys.UI_FORMAT_CORPSE_TITLE);

        [Category("Events")]
        [FormerlySerializedAs("onItemsContainerDestroy")]
        [SerializeField]
        protected UnityEvent onPickedUp;

        protected SyncFieldString _dropperTitle = new SyncFieldString();
        public SyncFieldString DropperTitle => _dropperTitle;

        protected SyncFieldInt _dropperEntityId = new SyncFieldInt();
        public SyncFieldInt DropperEntityId => _dropperEntityId;

        protected SyncListCharacterItem _items = new SyncListCharacterItem();
        public SyncListCharacterItem Items => _items;

        public RewardGivenType GivenType { get; protected set; }
        public HashSet<string> Looters { get; protected set; }
        public override string EntityTitle
        {
            get
            {
                if (!string.IsNullOrEmpty(_dropperTitle.Value))
                {
                    return ZString.Format(LanguageManager.GetText(formatKeyCorpseTitle), DropperTitle.Value);
                }
                if (GameInstance.MonsterCharacterEntities.ContainsKey(_dropperEntityId.Value))
                {
                    return ZString.Format(LanguageManager.GetText(formatKeyCorpseTitle), GameInstance.MonsterCharacterEntities[_dropperEntityId.Value].EntityTitle);
                }
                return base.EntityTitle;
            }
        }

        // Private variables
        protected bool _isDestroyed;
        protected float _dropTime;
        protected float _appearDuration;

        // === Anti-dupe transaction state ===
        private bool _containerTransactionInProgress;
        private readonly HashSet<int> _lockedItemIndexes = new HashSet<int>();

        protected override void SetupNetElements()
        {
            base.SetupNetElements();
            _dropperTitle.syncMode = LiteNetLibSyncField.SyncMode.ServerToClients;
            _dropperEntityId.syncMode = LiteNetLibSyncField.SyncMode.ServerToClients;
            _items.forOwnerOnly = false;
        }

        public override void OnSetup()
        {
            base.OnSetup();
            NetworkDestroy(_appearDuration);
        }

        public void CallRpcOnPickedUp()
        {
            RPC(RpcOnPickedUp);
        }

        [AllRpc]
        protected virtual void RpcOnPickedUp()
        {
            if (onPickedUp != null)
                onPickedUp.Invoke();
        }

        public virtual bool IsAbleToLoot(BaseCharacterEntity baseCharacterEntity)
        {
            if ((Looters == null || Looters.Count == 0 || Looters.Contains(baseCharacterEntity.Id) ||
                Time.unscaledTime - _dropTime > CurrentGameInstance.itemLootLockDuration) && !_isDestroyed)
                return true;
            return false;
        }

        /// <summary>
        /// This function should be called by server only when picked up some (or all) items from this container
        /// </summary>
        public virtual void PickedUp()
        {
            if (!IsServer)
                return;
            if (Items.Count > 0)
                return;
            if (_isDestroyed)
                return;
            // Mark as destroyed
            _isDestroyed = true;
            // Tell clients that the item drop destroy to play animation at client
            CallRpcOnPickedUp();
            // Destroy this entity
            NetworkDestroy(destroyDelay);
        }

        public static ItemsContainerEntity DropItems(ItemsContainerEntity prefab, BaseGameEntity dropper, RewardGivenType givenType, IEnumerable<CharacterItem> dropItems, IEnumerable<string> looters, float appearDuration, bool randomPosition = false, bool randomRotation = false)
        {
            Vector3 dropPosition = dropper.EntityTransform.position;
            Quaternion dropRotation = dropper.EntityTransform.rotation;
            switch (GameInstance.Singleton.DimensionType)
            {
                case DimensionType.Dimension3D:
                    if (randomPosition)
                    {
                        // Random position around dropper with its height
                        dropPosition += new Vector3(Random.Range(-1f, 1f) * GameInstance.Singleton.dropDistance, GROUND_DETECTION_Y_OFFSETS, Random.Range(-1f, 1f) * GameInstance.Singleton.dropDistance);
                    }
                    if (randomRotation)
                    {
                        // Random rotation
                        dropRotation = Quaternion.Euler(Vector3.up * Random.Range(0, 360));
                    }
                    break;
                case DimensionType.Dimension2D:
                    if (randomPosition)
                    {
                        // Random position around dropper
                        dropPosition += new Vector3(Random.Range(-1f, 1f) * GameInstance.Singleton.dropDistance, Random.Range(-1f, 1f) * GameInstance.Singleton.dropDistance);
                    }
                    break;
            }
            return DropItems(prefab, dropper, dropPosition, dropRotation, givenType, dropItems, looters, appearDuration);
        }

        public static ItemsContainerEntity DropItems(ItemsContainerEntity prefab, BaseGameEntity dropper, Vector3 dropPosition, Quaternion dropRotation, RewardGivenType givenType, IEnumerable<CharacterItem> dropItems, IEnumerable<string> looters, float appearDuration)
        {
            if (prefab == null)
                return null;

            if (GameInstance.Singleton.DimensionType == DimensionType.Dimension3D)
            {
                // Find drop position on ground
                dropPosition = PhysicUtils.FindGroundedPosition(dropPosition, s_findGroundRaycastHits, GROUND_DETECTION_DISTANCE, GameInstance.Singleton.GetItemDropGroundDetectionLayerMask());
            }
            LiteNetLibIdentity spawnObj = BaseGameNetworkManager.Singleton.Assets.GetObjectInstance(
                prefab.Identity.HashAssetId,
                dropPosition, dropRotation);
            ItemsContainerEntity itemsContainerEntity = spawnObj.GetComponent<ItemsContainerEntity>();
            itemsContainerEntity.Items.AddRange(dropItems);
            itemsContainerEntity.GivenType = givenType;
            itemsContainerEntity.Looters = new HashSet<string>(looters);
            itemsContainerEntity._isDestroyed = false;
            itemsContainerEntity._dropTime = Time.unscaledTime;
            itemsContainerEntity._appearDuration = appearDuration;
            if (dropper != null)
            {
                if (!string.IsNullOrEmpty(dropper.SyncTitle))
                    itemsContainerEntity.DropperTitle.Value = dropper.SyncTitle;
                else
                    itemsContainerEntity.DropperEntityId.Value = dropper.EntityId;
            }
            BaseGameNetworkManager.Singleton.Assets.NetworkSpawn(spawnObj);
            return itemsContainerEntity;
        }

        public virtual float GetActivatableDistance()
        {
            return GameInstance.Singleton.pickUpItemDistance;
        }

        public virtual bool ShouldClearTargetAfterActivated()
        {
            return false;
        }

        public virtual bool ShouldBeAttackTarget()
        {
            return false;
        }

        public virtual bool ShouldNotActivateAfterFollowed()
        {
            return false;
        }

        public virtual bool CanActivate()
        {
            return true;
        }

        public virtual void OnActivate()
        {
            BaseUISceneGameplay.Singleton.ShowItemsContainerDialog(this);
        }

        // ==========================================================
        // AAA-SAFE PICKUP METHODS
        // ==========================================================

        /// <summary>
        /// Try to pick up a single item with full anti-dupe guard.
        /// </summary>
        public bool TryPickupItem(BaseCharacterEntity looter, int itemIndex, int amount)
        {
            if (!IsServer)
                return false;
            if (_isDestroyed || looter == null)
                return false;
            if (itemIndex < 0 || itemIndex >= Items.Count)
                return false;
            if (!IsAbleToLoot(looter))
                return false;

            // Locking rules
            if (Time.unscaledTime - _dropTime <= CurrentGameInstance.itemLootLockDuration)
            {
                // Global lock (loot-lock active)
                if (_containerTransactionInProgress)
                    return false;
                _containerTransactionInProgress = true;
            }
            else
            {
                // Per-item lock (free-for-all)
                if (_lockedItemIndexes.Contains(itemIndex))
                    return false;
                _lockedItemIndexes.Add(itemIndex);
            }

            bool success = false;
            CharacterItem pickedItem = Items[itemIndex];

            try
            {
                // Remove from container
                Items.RemoveAt(itemIndex);

                // Try add to inventory
                if (looter.IncreasingItemsWillOverwhelming(new[] { pickedItem }))
                {
                    // Rollback if full
                    Items.Insert(itemIndex, pickedItem);
                }
                else
                {
                    looter.IncreaseItems(pickedItem);
                    success = true;
                }
            }
            finally
            {
                // Release lock
                if (Time.unscaledTime - _dropTime <= CurrentGameInstance.itemLootLockDuration)
                    _containerTransactionInProgress = false;
                else
                    _lockedItemIndexes.Remove(itemIndex);
            }

            if (Items.Count == 0 && success)
                PickedUp();

            return success;
        }

        /// <summary>
        /// Try to loot all items atomically. Uses global lock always.
        /// </summary>
        public bool TryPickupAll(BaseCharacterEntity looter)
        {
            if (!IsServer)
                return false;
            if (_isDestroyed || looter == null)
                return false;
            if (!IsAbleToLoot(looter))
                return false;

            if (_containerTransactionInProgress)
                return false;

            _containerTransactionInProgress = true;
            bool anySuccess = false;
            List<CharacterItem> tempItems = new List<CharacterItem>(Items);

            try
            {
                foreach (var item in tempItems)
                {
                    if (!looter.IncreasingItemsWillOverwhelming(new[] { item }))
                    {
                        looter.IncreaseItems(item);
                        Items.Remove(item);
                        anySuccess = true;
                    }
                }
            }
            finally
            {
                _containerTransactionInProgress = false;
            }

            if (Items.Count == 0 && anySuccess)
                PickedUp();

            return anySuccess;
        }
    }
}
