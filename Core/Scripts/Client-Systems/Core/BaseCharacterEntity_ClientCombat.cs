using UnityEngine;

namespace MultiplayerARPG
{
    public partial class BaseCharacterEntity
    {
        public void TryAutoAttackSelectedClient()
        {
            if (GameInstance.PlayingCharacterEntity != this)
                return;

            var controller = PlayerCharacterController.Singleton as PlayerCharacterController;
            if (controller == null || controller.SelectedClientEntity == null)
                return;

            var target = controller.SelectedClientEntity;

            if (target is HarvestableEntity_Lite harvestTarget)
            {
                var equippedWeapon = EquipWeapons.rightHand;
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
                    Debug.LogWarning($"[Combat] Can't harvest {harvestTarget.harvestableLite.title} without tool: {harvestTarget.harvestableLite.requiredToolType.Title}");
                    return;
                }
            }

            // Stub damage — you’ll likely replace this later
            int damage = 25;
            target.ApplyDamage(damage);
        }

        public ITargetableEntity GetUnifiedTargetEntity()
        {

            BaseGameEntity netTarget = GetTargetEntity();
            if (netTarget != null && netTarget.gameObject != null && netTarget.gameObject.activeInHierarchy)
                return netTarget;
            if (this == GameInstance.PlayingCharacterEntity)
            {
                var controller = PlayerCharacterController.Singleton as PlayerCharacterController;
                if (controller?.SelectedClientEntity != null &&
                    controller.SelectedClientEntity.EntityGameObject != null &&
                    controller.SelectedClientEntity.EntityGameObject.activeInHierarchy)
                {
                    return controller.SelectedClientEntity;
                }
            }

            return null;
        }
                
    }
}
