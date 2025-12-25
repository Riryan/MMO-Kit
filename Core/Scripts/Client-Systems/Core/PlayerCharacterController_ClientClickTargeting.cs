using UnityEngine;

namespace MultiplayerARPG
{
    public partial class PlayerCharacterController
    {
        public IClientDamageableEntity SelectedClientEntity { get; private set; }

        protected void CheckClientDamageableEntityClick(int hitCount, Vector3 fallbackPosition)
        {
            for (int i = 0; i < hitCount; ++i)
            {
                var hitTransform = _physicFunctions.GetRaycastTransform(i);
                if (hitTransform == null)
                    continue;

                var gameEntity = hitTransform.GetComponentInParent<BaseGameEntity>();
                if (gameEntity != null && gameEntity is ITargetableEntity targetable)
                {
                    if (!targetable.NotBeingSelectedOnClick())
                    {
                        GameInstance.PlayingCharacterEntity.SetTargetEntity(gameEntity);
                        if (targetable.SetAsTargetInOneClick())
                            _isFollowingTarget = true;
                        SelectedClientEntity = null;
                        return;
                    }
                }

                var clientDamageable = hitTransform.GetComponentInParent<IClientDamageableEntity>();
                if (clientDamageable != null && !clientDamageable.IsDead)
                {
                    if (clientDamageable is HarvestableEntity_Lite harvestTarget)
                    {
                        var player = GameInstance.PlayingCharacterEntity;
                        var equippedWeapon = player.EquipWeapons.rightHand;
                        int weaponDataId = equippedWeapon.IsEmptySlot() ? 0 : equippedWeapon.dataId;

                        WeaponType equippedType = null;
                        if (weaponDataId > 0 &&
                            GameInstance.Items.TryGetValue(weaponDataId, out var item) &&
                            item is IWeaponItem weaponItem)
                        {
                            equippedType = weaponItem.WeaponType;
                        }

                        if (harvestTarget.harvestableLite != null &&
                            harvestTarget.harvestableLite.requiredToolType != null &&
                            harvestTarget.harvestableLite.requiredToolType != equippedType)
                        {
                            Debug.LogWarning($"[ClickTargeting] Ignoring harvestable {harvestTarget.harvestableLite.title}, missing tool: {harvestTarget.harvestableLite.requiredToolType.Title}");
                            continue;
                        }
                    }

                    if (!clientDamageable.NotBeingSelectedOnClick())
                    {
                        SelectedClientEntity = clientDamageable;
                        if (clientDamageable.SetAsTargetInOneClick())
                            _isFollowingTarget = true;
                        GameInstance.PlayingCharacterEntity.SetTargetEntity(null);
                        return;
                    }
                }
            }

            GameInstance.PlayingCharacterEntity.SetTargetEntity(null);
            SelectedClientEntity = null;
            _targetPosition = fallbackPosition;
        }
        
    }
}
