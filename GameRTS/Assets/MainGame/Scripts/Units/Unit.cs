using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace MainGame.Scripts
{
    public enum UnitType
    {
        WoodCutter = 0,
        Archer = 1,
        Swat = 2,
    }

    /// <summary>
    /// Bir unit'in kimliği, takımı, seçim durumu ve sahibinden gelen komutların ağ geçidi.
    /// Komutlar (hareket/saldırı) sahibinden sunucuya RPC ile gider, sunucu doğrulayıp uygular.
    /// Eskiden her unit kendi Update'inde mouse raycast yapıyordu (PlayerController / WoodCutterController);
    /// artık girdi tek bir yerde (UnitCommandController) işlenir.
    /// </summary>
    [DisallowMultipleComponent]
    public class Unit : NetworkBehaviour, ITeamMember
    {
        [SerializeField] private UnitType unitType;

        [FormerlySerializedAs("name")]
        [SerializeField] private string displayName;

        [SerializeField] private int cost;
        [SerializeField] private Sprite sprite;
        [SerializeField] private Sprite selectedRing;

        [Tooltip("Seçildiğinde açılan gösterge. Boş bırakılırsa adında \"Selection\" geçen child kullanılır.")]
        [SerializeField] private GameObject selectionIndicator;

        private static readonly List<Unit> SpawnedUnits = new List<Unit>();

        private Mover mover;
        private UnitAttack attack;

        public static IReadOnlyList<Unit> All => SpawnedUnits;

        public UnitType UnitType => unitType;
        public string DisplayName => displayName;
        public int Cost => cost;
        public Sprite Sprite => sprite;
        public Team Team => IsSpawned ? TeamAssignment.ForClient(OwnerClientId) : Team.None;
        public UnitAttack Attack => attack;
        public bool IsSelected { get; private set; }

        private void Awake()
        {
            mover = GetComponent<Mover>();
            attack = GetComponent<UnitAttack>();

            if (selectionIndicator == null)
            {
                selectionIndicator = FindSelectionIndicator();
            }
        }

        /// <summary>
        /// Prefablarda halka "Selection Area"/"SelectionArea" altındaki "Quad"dır ve üst obje kapalıdır.
        /// Eski kod yalnızca doğrudan child "Quad"ı arıyordu; bulamadığı için seçim hiç görünmüyordu.
        /// </summary>
        private GameObject FindSelectionIndicator()
        {
            foreach (Transform child in transform)
            {
                if (child.name.Replace(" ", string.Empty).StartsWith("Selection"))
                {
                    foreach (Transform grandChild in child)
                    {
                        grandChild.gameObject.SetActive(true);
                    }

                    return child.gameObject;
                }
            }

            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Quad")
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        public override void OnNetworkSpawn()
        {
            SpawnedUnits.Add(this);
            SetSelected(false);
        }

        public override void OnNetworkDespawn()
        {
            SpawnedUnits.Remove(this);
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectionIndicator != null)
            {
                selectionIndicator.SetActive(selected);
            }
        }

        /// <summary>Sahip client tarafından çağrılır.</summary>
        public void CommandMove(Vector3 destination)
        {
            if (IsOwner && mover != null)
            {
                RequestMoveRpc(destination);
            }
        }

        /// <summary>Sahip client tarafından çağrılır.</summary>
        public void CommandAttack(NetworkObject target)
        {
            if (IsOwner && attack != null && target != null && attack.CanAttack(target.gameObject))
            {
                RequestAttackRpc(target);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestMoveRpc(Vector3 destination)
        {
            if (mover != null)
            {
                mover.StartMoveAction(destination);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestAttackRpc(NetworkObjectReference targetReference)
        {
            if (attack != null && targetReference.TryGet(out var target) && attack.CanAttack(target.gameObject))
            {
                attack.StartAttack(target);
            }
        }
    }
}
