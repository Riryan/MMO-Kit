using UnityEngine;
using MultiplayerARPG;
using System.Collections;

public class HarvestableEntity_Lite : MonoBehaviour, ITargetableEntity, IClientDamageableEntity
{
    [SerializeField] private int maxHp = 3;
    [SerializeField] private float respawnDelay = 10f;

    private int currentHp;
    private bool isHarvested;

    [Header("Harvest States")]
    public GameObject fullState;
    public GameObject depletedState;

    private Collider[] _colliders;

    [Header("Harvestable Config")]
    public HarvestableLite harvestableLite;

    public string LocalId => GetInstanceID().ToString();
    public int CurrentHp => currentHp;
    public int MaxHp => maxHp;
    public bool IsDead => currentHp <= 0;
    public bool IsDestroyed => IsDead;
    public Transform EntityTransform => transform;
    public GameObject EntityGameObject => gameObject;
    public string EntityTitle => harvestableLite != null ? harvestableLite.title : "Harvestable";
    public bool SetAsTargetInOneClick() => true;
    public bool NotBeingSelectedOnClick() => false;

    private void Start()
    {
        _colliders = GetComponentsInChildren<Collider>(true);
        InitializeHarvestable();
    }

    private void InitializeHarvestable()
    {
        currentHp = maxHp;
        isHarvested = false;

        if (fullState != null) fullState.SetActive(true);
        if (depletedState != null) depletedState.SetActive(false);

        EnableColliders(true);
        GameInstance.AddClientDamageableEntity(this);

        if (GameInstance.PlayingCharacterEntity != null &&
            GameInstance.PlayingCharacterEntity.GetTargetEntity() == this)
        {
            GameInstance.PlayingCharacterEntity.SetTargetEntity(null);
        }

        var controller = PlayerCharacterController.Singleton as PlayerCharacterController;
        if (controller != null && controller.SelectedClientEntity == this)
        {
            controller.ClearSelectedClientEntity();
        }
    }

    private void EnableColliders(bool enabled)
    {
        if (_colliders == null) return;
        foreach (var col in _colliders)
        {
            if (col != null)
                col.enabled = enabled;
        }
    }

    private void OnDestroy()
    {
        GameInstance.RemoveClientDamageableEntity(this);
    }

    public void ApplyDamage(int amount)
    {
        if (!CanHarvest())
            return;

        if (IsDead) return;

        currentHp -= amount;

        if (currentHp <= 0 && !isHarvested)
        {
            isHarvested = true;

            RequestRewards();

            if (fullState != null) fullState.SetActive(false);
            if (depletedState != null) depletedState.SetActive(true);

            EnableColliders(false);
            GameInstance.RemoveClientDamageableEntity(this);

            if (GameInstance.PlayingCharacterEntity != null &&
                GameInstance.PlayingCharacterEntity.GetTargetEntity() == this)
            {
                GameInstance.PlayingCharacterEntity.SetTargetEntity(null);
            }

            var controller = PlayerCharacterController.Singleton as PlayerCharacterController;
            if (controller != null && controller.SelectedClientEntity == this)
            {
                controller.ClearSelectedClientEntity();
            }

            StartCoroutine(RespawnRoutine());
        }
    }

    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);
        InitializeHarvestable();
    }

    public void ReceiveDamage(IDamageInfo damageInfo)
    {
        if (!CanHarvest())
            return;

        ApplyDamage(1); // All hits count as 1 damage
    }

    private bool CanHarvest()
    {
        if (harvestableLite == null)
            return false;

        var player = GameInstance.PlayingCharacterEntity;
        if (player == null)
            return false;

        var equippedWeapon = player.EquipWeapons.rightHand;
        int weaponDataId = equippedWeapon.IsEmptySlot() ? 0 : equippedWeapon.dataId;

        WeaponType equippedType = null;
        if (weaponDataId > 0 && GameInstance.Items.TryGetValue(weaponDataId, out var item) && item is IWeaponItem weaponItem)
        {
            equippedType = weaponItem.WeaponType;
        }

        if (harvestableLite.requiredToolType != null && harvestableLite.requiredToolType != equippedType)
        {
            return false;
        }

        return true;
    }

    public void OnSetup() { }

    public void RequestRewards()
    {
        OnRewardItems();
    }

    public void OnRewardItems()
    {
        if (harvestableLite == null)
        {
            return;
        }

        var player = GameInstance.PlayingCharacterEntity;
        if (player == null || HarvestManager.Instance == null)
        {
            return;
        }

        int weaponDataId = 0;
        var equippedWeapon = player.EquipWeapons.rightHand;
        if (!equippedWeapon.IsEmptySlot())
            weaponDataId = equippedWeapon.dataId;

        WeaponType equippedType = null;
        if (weaponDataId > 0 && GameInstance.Items.TryGetValue(weaponDataId, out var item) && item is IWeaponItem weaponItem)
        {
            equippedType = weaponItem.WeaponType;
        }

        if (harvestableLite.requiredToolType != null && harvestableLite.requiredToolType != equippedType)
        {
            return;
        }

        HarvestManager.Instance.ServerReceiveHarvestReward(
            player.ConnectionId,
            harvestableLite.dataId,
            weaponDataId,
            0, // unused
            player.Level,
            0f // unused
        );
    }
}
