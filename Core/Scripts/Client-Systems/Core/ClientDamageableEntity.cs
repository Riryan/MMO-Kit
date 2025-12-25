using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace MultiplayerARPG
{
    public class ClientDamageableEntity : MonoBehaviour, IClientDamageableEntity
    {
        [SerializeField]
        private int maxHp = 100;

        public UnityEvent onDestroyed;

        private int currentHp;

        public string LocalId => gameObject.GetInstanceID().ToString(); // For IClientEntity
        public Transform EntityTransform => transform;
        public GameObject EntityGameObject => gameObject; 
        public bool SetAsTargetInOneClick() => true;      
        public bool NotBeingSelectedOnClick() => false;   
        public bool IsDestroyed => currentHp <= 0;

        public int CurrentHp => currentHp;
        public int MaxHp => maxHp;
        public bool IsDead => currentHp <= 0;
        public string EntityTitle => "Local Entity";

        private void Start()
        {
            currentHp = maxHp;

            if (GameInstance.Singleton != null)
            {
                gameObject.tag = GameInstance.Singleton.harvestableTag;
                gameObject.layer = GameInstance.Singleton.harvestableLayer;
            }
            else
            {
                Debug.LogWarning("[ClientDamageableEntity] GameInstance.Singleton is null — using default tag/layer.");
                gameObject.tag = "Untagged";
                gameObject.layer = LayerMask.NameToLayer("Default");
            }
        }

        private void OnEnable()
        {
            GameInstance.AddClientDamageableEntity(this);
        }

        private void OnDisable()
        {
            GameInstance.RemoveClientDamageableEntity(this);
        }

        private void OnDestroy()
        {
            GameInstance.RemoveClientDamageableEntity(this);
        }

        public void ApplyDamage(int amount)
        {
            if (IsDead) return;

            currentHp -= amount;

            if (currentHp <= 0)
            {
                HandleDestruction();
            }
        }

        private void HandleDestruction()
        {
            onDestroyed?.Invoke();
            Destroy(gameObject);
        }

        public void ReceiveDamage(IDamageInfo damageInfo)
        {
            // TODO: Replace with real damage resolution logic
            ApplyDamage(25); // Arbitrary test damage
        }

        public void OnSetup() { }
    }
}
