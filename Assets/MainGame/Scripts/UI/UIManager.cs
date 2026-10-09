using TMPro;
using UnityEngine;

namespace MainGame.Scripts
{
    /// <summary>
    /// Oyuncu HUD'u: menü butonları ve üst bilgi (kale canı, odun, altın).
    /// Eskiden her karede GameObject.Find + GetComponent yapıp her karede yeni coroutine başlatıyordu;
    /// artık yalnızca değer değiştiğinde (event ile) güncellenir.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [SerializeField] private GameObject menuButton;
        [SerializeField] private GameObject unitButton;
        [SerializeField] private GameObject closeButton;
        [SerializeField] private GameObject buildingButton;

        [SerializeField] private PlayerState playerState;

        [SerializeField] private TextMeshProUGUI healthBar;
        [SerializeField] private TextMeshProUGUI woodBar;
        [SerializeField] private TextMeshProUGUI coinBar;

        [Tooltip("Kalelerden biri yıkılınca gösterilen sonuç metni (Victory / Defeat).")]
        [SerializeField] private TextMeshProUGUI gameOverText;

        [SerializeField] private UnitPanelManager unitPanel;
        [SerializeField] private BuildingManager buildingPanel;

        private Health castleHealth;
        private bool ownCastleSeen;
        private bool enemyCastleSeen;
        private bool gameOver;

        private void Awake()
        {
            // Satın alma butonları eskiden doğrudan ShowMenuButton'ı da çağırıyordu; satın alma başarısız olunca
            // panel açık kalırken menü butonu da görünüyordu. Menü artık panel gerçekten kapandığında geri gelir.
            if (unitPanel == null) unitPanel = transform.root.GetComponentInChildren<UnitPanelManager>(true);
            if (buildingPanel == null) buildingPanel = transform.root.GetComponentInChildren<BuildingManager>(true);

            if (gameOverText != null)
            {
                gameOverText.gameObject.SetActive(false);
            }
        }

        private void Start()
        {
            ShowMenuButton();
        }

        private void OnEnable()
        {
            if (playerState != null)
            {
                playerState.ResourcesChanged += RefreshResources;
            }

            if (unitPanel != null) unitPanel.PanelClosed += ShowMenuButton;
            if (buildingPanel != null) buildingPanel.PanelClosed += ShowMenuButton;
        }

        private void OnDisable()
        {
            if (playerState != null)
            {
                playerState.ResourcesChanged -= RefreshResources;
            }

            if (unitPanel != null) unitPanel.PanelClosed -= ShowMenuButton;
            if (buildingPanel != null) buildingPanel.PanelClosed -= ShowMenuButton;

            BindCastle(null);
        }

        private void Update()
        {
            if (playerState == null || !playerState.IsSpawned)
            {
                return;
            }

            // Kale sahneyle birlikte ağ üzerinde spawn olur; bağlanana kadar (ve yıkılırsa) tekrar dener.
            if (castleHealth == null)
            {
                if (Building.TryFind(playerState.Team, BuildingType.Castle, out var castle) && castle.Health != null && castle.Health.IsSpawned)
                {
                    BindCastle(castle.Health);
                    RefreshResources();
                }
            }

            CheckGameOver();
        }

        /// <summary>Kale yıkıldığında oyun bitmiyordu; artık iki tarafta da sonuç gösterilir.</summary>
        private void CheckGameOver()
        {
            if (gameOver)
            {
                return;
            }

            Team ownTeam = playerState.Team;
            Team enemyTeam = ownTeam == Team.A ? Team.B : Team.A;
            bool ownAlive = IsCastleAlive(ownTeam);
            bool enemyAlive = IsCastleAlive(enemyTeam);
            ownCastleSeen |= ownAlive;
            enemyCastleSeen |= enemyAlive;

            if (ownCastleSeen && !ownAlive)
            {
                ShowGameOver("DEFEAT\n<size=50%>Your castle has fallen</size>", new Color(1f, 0.35f, 0.3f));
            }
            else if (enemyCastleSeen && !enemyAlive)
            {
                ShowGameOver("VICTORY!\n<size=50%>The enemy castle is destroyed</size>", new Color(1f, 0.85f, 0.3f));
            }
        }

        private static bool IsCastleAlive(Team team)
        {
            return Building.TryFind(team, BuildingType.Castle, out var castle)
                   && castle.Health != null && castle.Health.IsSpawned && !castle.Health.IsDead;
        }

        private void ShowGameOver(string message, Color color)
        {
            gameOver = true;
            if (unitPanel != null) unitPanel.HideUnitPanel();
            if (buildingPanel != null) buildingPanel.HideUpgradePanel();
            HideMenuButton();

            if (gameOverText != null)
            {
                gameOverText.text = message;
                gameOverText.color = color;
                gameOverText.gameObject.SetActive(true);
            }
        }

        public void ShowMenuButton()
        {
            SetMenuState(menuVisible: true, optionsVisible: false);
        }

        public void HideMenuButton()
        {
            SetMenuState(menuVisible: false, optionsVisible: false);
        }

        public void ShowMenuOptions()
        {
            SetMenuState(menuVisible: false, optionsVisible: true);
        }

        private void SetMenuState(bool menuVisible, bool optionsVisible)
        {
            if (gameOver)
            {
                menuVisible = optionsVisible = false;
            }

            menuButton.SetActive(menuVisible);
            unitButton.SetActive(optionsVisible);
            closeButton.SetActive(optionsVisible);
            buildingButton.SetActive(optionsVisible);
        }

        private void BindCastle(Health health)
        {
            if (castleHealth != null)
            {
                castleHealth.HealthChanged -= OnCastleHealthChanged;
            }

            castleHealth = health;

            if (castleHealth != null)
            {
                castleHealth.HealthChanged += OnCastleHealthChanged;
                OnCastleHealthChanged(castleHealth.CurrentHealth, castleHealth.MaxHealth);
            }
        }

        private void OnCastleHealthChanged(float current, float max)
        {
            if (healthBar != null)
            {
                healthBar.text = Mathf.CeilToInt(current).ToString();
            }
        }

        private void RefreshResources()
        {
            if (playerState == null || !playerState.IsSpawned)
            {
                return;
            }

            if (woodBar != null)
            {
                woodBar.text = playerState.Wood.ToString();
            }

            if (coinBar != null)
            {
                coinBar.text = playerState.Coin.ToString();
            }
        }
    }
}
