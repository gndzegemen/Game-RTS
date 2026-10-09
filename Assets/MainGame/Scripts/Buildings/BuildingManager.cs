using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MainGame.Scripts
{
    /// <summary>
    /// Bina yükseltme paneli. UI yalnızca sahibinde çalışır; yükseltme isteği sunucuya gider,
    /// kaynak kontrolü ve uygulama sunucuda yapılır.
    /// (Dosya adı "Building Manager.cs" idi; sınıf adıyla eşleşmediği için Unity script'i yükleyemiyordu.)
    /// </summary>
    public class BuildingManager : NetworkBehaviour
    {
        [SerializeField] private GameObject upgradePanel;
        [SerializeField] private TextMeshProUGUI[] nameTexts;
        [SerializeField] private TextMeshProUGUI[] costTexts;
        [SerializeField] private Image[] images;

        [Header("Panelde gösterilecek bina şablonları (sıra: panel sırası)")]
        [SerializeField] private GameObject lumbermillPrefabA;
        [SerializeField] private GameObject archeryardPrefabA;
        [SerializeField] private GameObject castlePrefabA;

        [SerializeField] private PlayerState playerState;

        [SerializeField] private TimedMessage feedback = new TimedMessage();

        private readonly List<Building> catalog = new List<Building>();
        private bool upgradePending;

        public bool IsOpen => upgradePanel != null && upgradePanel.activeSelf;

        /// <summary>Panel kapandığında (yükseltme ya da kapat butonu). HUD menüsü buna göre geri gelir.</summary>
        public event System.Action PanelClosed;

        private void Awake()
        {
            AddToCatalog(lumbermillPrefabA);
            AddToCatalog(archeryardPrefabA);
            AddToCatalog(castlePrefabA);
        }

        private void Update()
        {
            feedback.Tick();
        }

        public void ShowUpgradePanel()
        {
            if (upgradePanel == null)
            {
                return;
            }

            upgradePanel.SetActive(true);
            feedback.Clear();
            PopulateUpgradePanel();
        }

        public void HideUpgradePanel()
        {
            if (upgradePanel != null)
            {
                upgradePanel.SetActive(false);
            }

            PanelClosed?.Invoke();
        }

        /// <summary>UI butonundan çağrılır; parametre panelde gösterilen bina şablonudur.</summary>
        public void RequestUpgrade(GameObject buildingTemplate)
        {
            if (!IsOwner || upgradePending || buildingTemplate == null || playerState == null)
            {
                return;
            }

            var building = buildingTemplate.GetComponent<Building>();
            if (building == null)
            {
                Debug.LogWarning($"{buildingTemplate.name} üzerinde Building bileşeni yok.", buildingTemplate);
                return;
            }

            // Yükseltme binanın canını UpgradedHealth'e çıkarır; can zaten oradaysa odun boşa harcanıyordu.
            if (!Building.TryFind(playerState.Team, building.BuildingType, out var ownBuilding)
                || ownBuilding.Health == null || ownBuilding.Health.IsDead)
            {
                feedback.Show($"Your {building.DisplayName} is destroyed");
                return;
            }

            if (ownBuilding.Health.CurrentHealth >= building.UpgradedHealth)
            {
                feedback.Show($"{building.DisplayName} is already at full strength");
                return;
            }

            if (!playerState.CanAfford(0, building.Cost))
            {
                feedback.Show($"Not enough wood ({building.Cost} needed, you have {playerState.Wood})");
                return;
            }

            upgradePending = true;
            RequestUpgradeRpc(building.BuildingType);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestUpgradeRpc(BuildingType buildingType)
        {
            Team team = TeamAssignment.ForClient(OwnerClientId);
            if (!Building.TryFind(team, buildingType, out var building) || building.Health == null || building.Health.IsDead)
            {
                UpgradeResultRpc(false, "Your building is destroyed");
                return;
            }

            if (building.Health.CurrentHealth >= building.UpgradedHealth)
            {
                UpgradeResultRpc(false, "This building is already at full strength");
                return;
            }

            if (playerState == null || !playerState.TrySpend(0, building.Cost))
            {
                UpgradeResultRpc(false, $"Not enough wood ({building.Cost} needed)");
                return;
            }

            building.Health.SetHealth(building.UpgradedHealth);
            UpgradeResultRpc(true, string.Empty);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void UpgradeResultRpc(bool succeeded, string message)
        {
            upgradePending = false;
            if (succeeded)
            {
                HideUpgradePanel();
            }
            else
            {
                feedback.Show(message);
            }
        }

        private void AddToCatalog(GameObject template)
        {
            var building = template != null ? template.GetComponent<Building>() : null;
            if (building != null)
            {
                catalog.Add(building);
            }
        }

        private void PopulateUpgradePanel()
        {
            if (nameTexts == null || costTexts == null || images == null)
            {
                return;
            }

            int count = Mathf.Min(catalog.Count, nameTexts.Length, costTexts.Length, images.Length);
            for (int i = 0; i < count; i++)
            {
                if (nameTexts[i] != null) nameTexts[i].text = catalog[i].DisplayName;
                if (costTexts[i] != null) costTexts[i].text = catalog[i].Cost.ToString();
                if (images[i] != null)
                {
                    images[i].sprite = catalog[i].Sprite;
                    images[i].preserveAspect = true;
                }
            }
        }
    }
}
