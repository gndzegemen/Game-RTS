using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace MainGame.Scripts
{
    /// <summary>
    /// Oyuncunun kaynakları (odun/altın). Eski "User" sınıfının yerini alır.
    /// Değerler sunucuda tutulur ve NetworkVariable ile senkronize edilir; harcama sunucuda doğrulanır.
    /// Diğer sistemler oyuncuyu tag ile aramak yerine <see cref="TryGetForClient"/> / <see cref="Local"/> kullanır.
    /// </summary>
    public class PlayerState : NetworkBehaviour, ITeamMember
    {
        [FormerlySerializedAs("wood")]
        [SerializeField] private int startingWood = 500;

        [FormerlySerializedAs("coin")]
        [SerializeField] private int startingCoin = 3000;

        private static readonly Dictionary<ulong, PlayerState> PlayersByClient = new Dictionary<ulong, PlayerState>();

        private readonly NetworkVariable<int> wood = new NetworkVariable<int>();
        private readonly NetworkVariable<int> coin = new NetworkVariable<int>();

        /// <summary>Bu makinedeki oyuncunun durumu.</summary>
        public static PlayerState Local { get; private set; }

        public event Action ResourcesChanged;

        public Team Team => IsSpawned ? TeamAssignment.ForClient(OwnerClientId) : Team.None;
        public int Wood => wood.Value;
        public int Coin => coin.Value;

        public static bool TryGetForClient(ulong clientId, out PlayerState playerState)
        {
            return PlayersByClient.TryGetValue(clientId, out playerState) && playerState != null;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                wood.Value = startingWood;
                coin.Value = startingCoin;
            }

            PlayersByClient[OwnerClientId] = this;
            if (IsOwner)
            {
                Local = this;
            }

            wood.OnValueChanged += OnResourceValueChanged;
            coin.OnValueChanged += OnResourceValueChanged;
            ResourcesChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            wood.OnValueChanged -= OnResourceValueChanged;
            coin.OnValueChanged -= OnResourceValueChanged;

            if (PlayersByClient.TryGetValue(OwnerClientId, out var registered) && registered == this)
            {
                PlayersByClient.Remove(OwnerClientId);
            }

            if (Local == this)
            {
                Local = null;
            }
        }

        public bool CanAfford(int coinCost, int woodCost)
        {
            return coinCost >= 0 && woodCost >= 0 && coin.Value >= coinCost && wood.Value >= woodCost;
        }

        /// <summary>Yalnızca sunucuda. Yeterli kaynak varsa düşer ve true döner.</summary>
        public bool TrySpend(int coinCost, int woodCost)
        {
            if (!IsServer || coinCost < 0 || woodCost < 0 || !CanAfford(coinCost, woodCost))
            {
                return false;
            }

            coin.Value -= coinCost;
            wood.Value -= woodCost;
            return true;
        }

        /// <summary>Yalnızca sunucuda.</summary>
        public void AddWood(int amount)
        {
            if (IsServer && amount > 0)
            {
                wood.Value += amount;
            }
        }

        /// <summary>Yalnızca sunucuda.</summary>
        public void AddCoin(int amount)
        {
            if (IsServer && amount > 0)
            {
                coin.Value += amount;
            }
        }

        private void OnResourceValueChanged(int previous, int current)
        {
            ResourcesChanged?.Invoke();
        }
    }
}
