using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace MainGame.Scripts
{
    /// <summary>
    /// Sunucu otoriteli can sistemi. Can değeri NetworkVariable ile tüm client'lara senkronize edilir,
    /// hasar yalnızca sunucuda uygulanır. Görsel taraf (HealthBar, UI) <see cref="HealthChanged"/> event'ini dinler.
    /// </summary>
    public class Health : NetworkBehaviour
    {
        [FormerlySerializedAs("health")]
        [SerializeField] private float maxHealth = 100f;

        [Tooltip("Bu obje yok edildiğinde, onu yok eden oyuncuya verilecek altın.")]
        [SerializeField] private int coinReward = 100;

        private readonly NetworkVariable<float> currentHealth = new NetworkVariable<float>();
        private readonly NetworkVariable<float> networkMaxHealth = new NetworkVariable<float>();

        /// <summary>(current, max)</summary>
        public event Action<float, float> HealthChanged;

        public float CurrentHealth => IsSpawned ? currentHealth.Value : maxHealth;
        public float MaxHealth => IsSpawned ? networkMaxHealth.Value : maxHealth;
        public bool IsDead => IsSpawned && currentHealth.Value <= 0f;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                networkMaxHealth.Value = maxHealth;
                currentHealth.Value = maxHealth;
            }

            currentHealth.OnValueChanged += OnHealthValueChanged;
            networkMaxHealth.OnValueChanged += OnHealthValueChanged;
            RaiseHealthChanged();
        }

        public override void OnNetworkDespawn()
        {
            currentHealth.OnValueChanged -= OnHealthValueChanged;
            networkMaxHealth.OnValueChanged -= OnHealthValueChanged;

            // Sahneye yerleştirilmiş objeler (ağaç, bina) yok edilmeden despawn edilir; her makinede gizlenir.
            if (NetworkObject.InScenePlaced && NetworkManager != null && !NetworkManager.ShutdownInProgress)
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>Yalnızca sunucuda çağrılır.</summary>
        public void TakeDamage(float damage, ulong attackerClientId)
        {
            if (!IsServer || IsDead || damage <= 0f)
            {
                return;
            }

            currentHealth.Value = Mathf.Max(currentHealth.Value - damage, 0f);

            if (currentHealth.Value <= 0f)
            {
                Die(attackerClientId);
            }
        }

        /// <summary>Yalnızca sunucuda çağrılır (ör. bina yükseltme). Gerekirse maksimum canı da yükseltir.</summary>
        public void SetHealth(float value)
        {
            if (!IsServer || IsDead)
            {
                return;
            }

            networkMaxHealth.Value = Mathf.Max(networkMaxHealth.Value, value);
            currentHealth.Value = Mathf.Clamp(value, 0f, networkMaxHealth.Value);
        }

        public float GetHealth()
        {
            return CurrentHealth;
        }

        /// <summary>Ölen objenin ödülünü, onu yok eden oyuncuya verir. Alt sınıflar farklı ödül verebilir.</summary>
        protected virtual void GrantReward(PlayerState killer)
        {
            killer.AddCoin(coinReward);
        }

        private void Die(ulong attackerClientId)
        {
            if (PlayerState.TryGetForClient(attackerClientId, out var killer))
            {
                GrantReward(killer);
            }

            // NGO, sahneye yerleştirilmiş ağ objelerinin yok edilmesine karşı uyarır (Despawn(false) önerir).
            NetworkObject.Despawn(destroy: !NetworkObject.InScenePlaced);
        }

        private void OnHealthValueChanged(float previous, float current)
        {
            RaiseHealthChanged();
        }

        private void RaiseHealthChanged()
        {
            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }
    }
}
