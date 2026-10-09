using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MainGame.Scripts
{
    /// <summary>
    /// Unit satın alma paneli. Adetler UI metninden parse edilmek yerine burada tutulur (model),
    /// metinler sadece gösterim içindir (view). Satın alma isteği sunucuya gider; maliyet kontrolü,
    /// kaynak düşme ve spawn sunucuda yapılır. Spawn noktaları sahnedeki RTSGameManager'dan gelir.
    /// </summary>
    public class UnitPanelManager : NetworkBehaviour
    {
        private const int MaxUnitsPerOrder = 20;

        [SerializeField] private GameObject createUnitPanel;

        [Header("Panel elemanları (sıra: Oduncu, Okçu, Swat)")]
        [SerializeField] private TextMeshProUGUI[] nameTexts;
        [SerializeField] private TextMeshProUGUI[] costTexts;
        [SerializeField] private Image[] images;

        [Header("Unit prefabları")]
        [SerializeField] private GameObject woodCutterPrefab;
        [SerializeField] private GameObject archerPrefab;
        [SerializeField] private GameObject swatPrefab;

        [Header("Adet göstergeleri")]
        [SerializeField] private TextMeshProUGUI woodCutterCount;
        [SerializeField] private TextMeshProUGUI archerCount;
        [SerializeField] private TextMeshProUGUI swatCount;

        [SerializeField] private PlayerState playerState;

        [SerializeField] private TimedMessage feedback = new TimedMessage();

        private readonly int[] orderCounts = new int[3];
        private bool purchasePending;

        public bool IsOpen => createUnitPanel != null && createUnitPanel.activeSelf;

        /// <summary>Panel kapandığında (satın alma ya da kapat butonu). HUD menüsü buna göre geri gelir.</summary>
        public event System.Action PanelClosed;

        private void Update()
        {
            feedback.Tick();
        }

        public void ShowUnitPanel()
        {
            if (createUnitPanel == null)
            {
                return;
            }

            createUnitPanel.SetActive(true);
            feedback.Clear();
            PopulateUnitPanel();
        }

        public void HideUnitPanel()
        {
            if (createUnitPanel != null)
            {
                createUnitPanel.SetActive(false);
            }

            for (int i = 0; i < orderCounts.Length; i++)
            {
                orderCounts[i] = 0;
            }

            RefreshCounts();
            PanelClosed?.Invoke();
        }

        public void IncreaseWoodCutterCount() => ChangeCount(UnitType.WoodCutter, +1);
        public void DecreaseWoodCutterCount() => ChangeCount(UnitType.WoodCutter, -1);
        public void IncreaseArcherCount() => ChangeCount(UnitType.Archer, +1);
        public void DecreaseArcherCount() => ChangeCount(UnitType.Archer, -1);
        public void IncreaseSwatCount() => ChangeCount(UnitType.Swat, +1);
        public void DecreaseSwatCount() => ChangeCount(UnitType.Swat, -1);

        /// <summary>UI butonundan çağrılır; parametre satın alınacak unit prefabıdır.</summary>
        public void RequestPurchase(GameObject unitPrefab)
        {
            if (!IsOwner || purchasePending || unitPrefab == null || playerState == null)
            {
                return;
            }

            var unit = unitPrefab.GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogWarning($"{unitPrefab.name} üzerinde Unit bileşeni yok.", unitPrefab);
                return;
            }

            int index = (int)unit.UnitType;
            if (index < 0 || index >= orderCounts.Length)
            {
                feedback.Show("This unit type cannot be purchased");
                return;
            }

            int count = orderCounts[index];
            if (count <= 0)
            {
                feedback.Show("Choose an amount with + first");
                return;
            }

            int totalCost = unit.Cost * count;
            if (!playerState.CanAfford(totalCost, 0))
            {
                feedback.Show($"Not enough gold ({totalCost} needed, you have {playerState.Coin})");
                return;
            }

            purchasePending = true;
            RequestPurchaseRpc(unit.UnitType, count);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestPurchaseRpc(UnitType unitType, int count)
        {
            var prefab = GetPrefab(unitType);
            var unit = prefab != null ? prefab.GetComponent<Unit>() : null;
            var networkPrefab = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
            var gameManager = RTSGameManager.Instance;

            if (unit == null || networkPrefab == null || gameManager == null || playerState == null
                || count <= 0 || count > MaxUnitsPerOrder)
            {
                PurchaseResultRpc(false, "Unit could not be created. Try again.");
                return;
            }

            if (!playerState.TrySpend(unit.Cost * count, 0))
            {
                PurchaseResultRpc(false, $"Not enough gold ({unit.Cost * count} needed, you have {playerState.Coin})");
                return;
            }

            Team team = TeamAssignment.ForClient(OwnerClientId);
            for (int i = 0; i < count; i++)
            {
                gameManager.GetNextUnitSpawnPose(team, unitType, out var position, out var rotation);
                networkPrefab.InstantiateAndSpawn(NetworkManager, OwnerClientId, destroyWithScene: true,
                    position: position, rotation: rotation);
            }

            PurchaseResultRpc(true, string.Empty);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PurchaseResultRpc(bool succeeded, string message)
        {
            purchasePending = false;
            if (succeeded)
            {
                HideUnitPanel();
            }
            else
            {
                feedback.Show(message);
            }
        }

        private void ChangeCount(UnitType unitType, int delta)
        {
            if (purchasePending)
            {
                return;
            }

            int index = (int)unitType;
            if (index < 0 || index >= orderCounts.Length)
            {
                return;
            }

            orderCounts[index] = Mathf.Clamp(orderCounts[index] + delta, 0, MaxUnitsPerOrder);
            RefreshCounts();
        }

        private void RefreshCounts()
        {
            SetText(woodCutterCount, orderCounts[(int)UnitType.WoodCutter].ToString());
            SetText(archerCount, orderCounts[(int)UnitType.Archer].ToString());
            SetText(swatCount, orderCounts[(int)UnitType.Swat].ToString());

            foreach (UnitType unitType in System.Enum.GetValues(typeof(UnitType)))
            {
                int index = (int)unitType;
                var prefab = GetPrefab(unitType);
                var unit = prefab != null ? prefab.GetComponent<Unit>() : null;
                if (unit != null && costTexts != null && index < costTexts.Length)
                {
                    int displayCost = unit.Cost * Mathf.Max(orderCounts[index], 1);
                    SetText(costTexts[index], displayCost.ToString());
                }
            }
        }

        private void PopulateUnitPanel()
        {
            foreach (UnitType unitType in System.Enum.GetValues(typeof(UnitType)))
            {
                int index = (int)unitType;
                var prefab = GetPrefab(unitType);
                var unit = prefab != null ? prefab.GetComponent<Unit>() : null;
                if (unit == null)
                {
                    continue;
                }

                if (nameTexts != null && index < nameTexts.Length) SetText(nameTexts[index], unit.DisplayName);
                if (costTexts != null && index < costTexts.Length) SetText(costTexts[index], unit.Cost.ToString());
                if (images != null && index < images.Length && images[index] != null)
                {
                    images[index].sprite = unit.Sprite;
                    images[index].preserveAspect = true;
                }
            }

            RefreshCounts();
        }

        private GameObject GetPrefab(UnitType unitType)
        {
            switch (unitType)
            {
                case UnitType.WoodCutter: return woodCutterPrefab;
                case UnitType.Archer: return archerPrefab;
                case UnitType.Swat: return swatPrefab;
                default: return null;
            }
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value;
            }
        }
    }
}
