using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MainGame.Scripts
{
    /// <summary>
    /// Lobi akışı (eski Photon "NetworkManager" sınıfı). Netcode for GameObjects + Unity Transport kullanır:
    /// - Oda oluştur  -> Host başlatılır, oda LAN'da yayınlanır.
    /// - Oda listesi  -> LAN'daki odalar UDP broadcast ile bulunur, seçilen odaya client olarak bağlanılır.
    /// - Oyuncu isimleri bağlantı onayı (connection approval) payload'ı ile gelir ve named message ile dağıtılır.
    /// - Oyunu başlat -> Host, NetworkSceneManager ile oyun sahnesini herkes için yükler.
    /// Sınıf adı Unity.Netcode.NetworkManager ile çakışmaması için değiştirildi.
    /// </summary>
    public class LobbyManager : MonoBehaviour
    {
        private const string LobbyStateMessage = "RTS.LobbyState";
        private const int MaxPlayerNameLength = 24;

        [Header("Connection Status")]
        public Text connectionStatusText;

        [Header("Login Panel")]
        public InputField playerNameInput;
        public GameObject loginPanel;

        [Header("Game Options Panel")]
        public GameObject gameOptionsPanel;

        [Header("Create Room Panel")]
        public GameObject createRoomPanel;
        public InputField roomNameInputField;
        public InputField maxPlayerInputField;

        [Header("Inside Room Panel")]
        public GameObject insideRoomPanel;
        public Text roomInfoText;
        public GameObject playerListPrefab;
        public GameObject playerListContent;
        public GameObject startGameButton;

        [Header("Room List Panel")]
        public GameObject roomListPanel;
        public GameObject roomListEntryPrefab;
        public GameObject roomListParentGameObject;

        [Header("Join Random Room Panel (bağlanılıyor ekranı)")]
        public GameObject joinRandomRoomPanel;

        [Header("Network")]
        [SerializeField] private string gameSceneName = "Environment_Free";
        [SerializeField] private ushort gamePort = 7777;

        private readonly Dictionary<ulong, string> playerNames = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> pendingPlayerNames = new Dictionary<ulong, string>();
        private readonly Dictionary<string, GameObject> roomListGameObjects = new Dictionary<string, GameObject>();
        private readonly Dictionary<ulong, GameObject> playerListGameObjects = new Dictionary<ulong, GameObject>();

        private NetworkManager networkManager;
        private LanRoomDiscovery discovery;
        private string playerName;
        private string roomName;
        private int maxPlayers = TeamAssignment.MaxPlayers;
        private bool messageHandlerRegistered;

        #region Unity

        private void Awake()
        {
            networkManager = ResolveNetworkManager();
            discovery = new LanRoomDiscovery();
            discovery.RoomsChanged += RefreshRoomListView;
        }

        private void Start()
        {
            if (networkManager == null)
            {
                Debug.LogError("Sahnede NetworkManager bulunamadı. 'Tools/RTS/Migrate To Netcode' menüsünü çalıştırın.");
                return;
            }

            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.ConnectionApprovalCallback = ApproveConnection;
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            networkManager.OnClientStopped += OnClientStopped;

            // Oyundan lobiye dönüldüğünde ağ oturumu kapanmış olur; giriş yapılmışsa doğrudan seçeneklere geç.
            ActivatePanel(string.IsNullOrEmpty(PlayerPrefs.GetString(nameof(playerName))) ? loginPanel.name : gameOptionsPanel.name);
            playerName = PlayerPrefs.GetString(nameof(playerName));
            if (playerNameInput != null && !string.IsNullOrEmpty(playerName))
            {
                playerNameInput.text = playerName;
            }
        }

        private void Update()
        {
            discovery.Tick(Time.unscaledTime);

            if (connectionStatusText != null)
            {
                connectionStatusText.text = "Connection Status: " + DescribeConnectionState();
            }
        }

        private void OnDestroy()
        {
            discovery.RoomsChanged -= RefreshRoomListView;
            discovery.Dispose();

            if (networkManager == null)
            {
                return;
            }

            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            networkManager.OnClientStopped -= OnClientStopped;

            // Oyun başladıktan sonra yeni bağlantıları reddet.
            networkManager.ConnectionApprovalCallback = RejectLateJoin;
            UnregisterMessageHandler();
        }

        #endregion

        #region UI callbacks

        public void OnLoginButtonClicked()
        {
            string enteredName = playerNameInput != null ? playerNameInput.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(enteredName))
            {
                Debug.Log("Player Name is invalid");
                return;
            }

            playerName = Truncate(enteredName, MaxPlayerNameLength);
            PlayerPrefs.SetString(nameof(playerName), playerName);
            ActivatePanel(gameOptionsPanel.name);
        }

        public void OnCreateRoomButtonClicked()
        {
            if (networkManager == null || networkManager.IsListening)
            {
                return;
            }

            roomName = roomNameInputField != null ? roomNameInputField.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(roomName))
            {
                roomName = "Room " + Random.Range(1000, 10000);
            }

            maxPlayers = TeamAssignment.MaxPlayers;
            if (maxPlayerInputField != null && int.TryParse(maxPlayerInputField.text, out int requestedMaxPlayers))
            {
                maxPlayers = Mathf.Clamp(requestedMaxPlayers, 1, TeamAssignment.MaxPlayers);
            }

            GetTransport().SetConnectionData("127.0.0.1", gamePort, "0.0.0.0");
            networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(playerName ?? string.Empty);

            if (!networkManager.StartHost())
            {
                Debug.LogError("Host başlatılamadı. Port kullanımda olabilir: " + gamePort);
                return;
            }

            RegisterMessageHandler();
            discovery.StartAdvertising(() => new LanRoomDiscovery.AdvertisedRoom
            {
                Name = roomName,
                Port = gamePort,
                Players = playerNames.Count,
                MaxPlayers = maxPlayers,
            });

            Debug.Log(roomName + " is created.");
            ActivatePanel(insideRoomPanel.name);
            RefreshInsideRoomView();
        }

        public void OnCancelButtonClicked()
        {
            ActivatePanel(gameOptionsPanel.name);
        }

        public void OnShowRoomListButtonClicked()
        {
            discovery.StartSearching();
            RefreshRoomListView();
            ActivatePanel(roomListPanel.name);
        }

        public void OnBackButtonClicked()
        {
            discovery.StopSearching();
            ActivatePanel(gameOptionsPanel.name);
        }

        public void OnLeaveGameButtonClicked()
        {
            if (networkManager != null && networkManager.IsListening)
            {
                networkManager.Shutdown();
            }

            LeaveRoomLocally();
        }

        public void OnStartGameButtonClicked()
        {
            if (networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            discovery.StopAdvertising();
            var status = networkManager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"Oyun sahnesi yüklenemedi ({status}). '{gameSceneName}' Build Settings'te ekli mi?");
            }
        }

        public void ActivatePanel(string panelToBeActivated)
        {
            loginPanel.SetActive(panelToBeActivated.Equals(loginPanel.name));
            gameOptionsPanel.SetActive(panelToBeActivated.Equals(gameOptionsPanel.name));
            createRoomPanel.SetActive(panelToBeActivated.Equals(createRoomPanel.name));
            insideRoomPanel.SetActive(panelToBeActivated.Equals(insideRoomPanel.name));
            roomListPanel.SetActive(panelToBeActivated.Equals(roomListPanel.name));
            joinRandomRoomPanel.SetActive(panelToBeActivated.Equals(joinRandomRoomPanel.name));
        }

        #endregion

        #region Netcode callbacks

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false; // Oyuncu objeleri oyun sahnesinde RTSGameManager tarafından spawn edilir.

            bool isHostItself = request.ClientNetworkId == NetworkManager.ServerClientId;
            if (!isHostItself && networkManager.ConnectedClientsIds.Count >= maxPlayers)
            {
                response.Approved = false;
                response.Reason = "Oda dolu.";
                return;
            }

            string requestedName = request.Payload != null && request.Payload.Length > 0
                ? Encoding.UTF8.GetString(request.Payload)
                : "Player " + request.ClientNetworkId;

            pendingPlayerNames[request.ClientNetworkId] = Truncate(requestedName, MaxPlayerNameLength);
            response.Approved = true;
        }

        private static void RejectLateJoin(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Approved = false;
            response.Reason = "Oyun zaten başladı.";
        }

        private void OnClientConnected(ulong clientId)
        {
            if (networkManager.IsServer)
            {
                if (!pendingPlayerNames.TryGetValue(clientId, out string connectedName))
                {
                    connectedName = "Player " + clientId;
                }

                pendingPlayerNames.Remove(clientId);
                playerNames[clientId] = connectedName;
                Debug.Log(connectedName + " joined to " + roomName);
                BroadcastLobbyState();
                RefreshInsideRoomView();
            }
            else if (clientId == networkManager.LocalClientId)
            {
                ActivatePanel(insideRoomPanel.name);
                RefreshInsideRoomView();
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (networkManager.IsServer && clientId != NetworkManager.ServerClientId)
            {
                playerNames.Remove(clientId);
                pendingPlayerNames.Remove(clientId);
                BroadcastLobbyState();
                RefreshInsideRoomView();
            }
        }

        private void OnClientStopped(bool wasHost)
        {
            if (!string.IsNullOrEmpty(networkManager.DisconnectReason))
            {
                Debug.Log("Bağlantı kapandı: " + networkManager.DisconnectReason);
            }

            LeaveRoomLocally();
        }

        #endregion

        #region Lobby state messaging

        private void RegisterMessageHandler()
        {
            if (!messageHandlerRegistered && networkManager.CustomMessagingManager != null)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(LobbyStateMessage, OnLobbyStateReceived);
                messageHandlerRegistered = true;
            }
        }

        private void UnregisterMessageHandler()
        {
            if (messageHandlerRegistered && networkManager != null && networkManager.CustomMessagingManager != null)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyStateMessage);
            }

            messageHandlerRegistered = false;
        }

        private void BroadcastLobbyState()
        {
            if (!networkManager.IsServer)
            {
                return;
            }

            using var writer = new FastBufferWriter(256, Allocator.Temp, 16 * 1024);
            writer.WriteValueSafe(roomName ?? string.Empty);
            writer.WriteValueSafe(maxPlayers);
            writer.WriteValueSafe(playerNames.Count);
            foreach (var pair in playerNames)
            {
                writer.WriteValueSafe(pair.Key);
                writer.WriteValueSafe(pair.Value);
            }

            networkManager.CustomMessagingManager.SendNamedMessageToAll(LobbyStateMessage, writer);
        }

        private void OnLobbyStateReceived(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager.IsServer)
            {
                return; // Host kendi durumunu zaten biliyor.
            }

            reader.ReadValueSafe(out string receivedRoomName);
            reader.ReadValueSafe(out int receivedMaxPlayers);
            reader.ReadValueSafe(out int count);

            roomName = receivedRoomName;
            maxPlayers = receivedMaxPlayers;
            playerNames.Clear();
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out ulong clientId);
                reader.ReadValueSafe(out string clientName);
                playerNames[clientId] = clientName;
            }

            RefreshInsideRoomView();
        }

        #endregion

        #region Views

        private void JoinRoom(LanRoomDiscovery.RoomInfo room)
        {
            if (networkManager.IsListening)
            {
                return;
            }

            discovery.StopSearching();
            GetTransport().SetConnectionData(room.Address, room.Port);
            networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(playerName ?? string.Empty);

            if (!networkManager.StartClient())
            {
                Debug.LogError("Client başlatılamadı.");
                return;
            }

            RegisterMessageHandler();
            roomName = room.Name;
            SetConnectingText("Connecting to " + room.Name + "...");
            ActivatePanel(joinRandomRoomPanel.name);
        }

        private void LeaveRoomLocally()
        {
            discovery.StopAdvertising();
            UnregisterMessageHandler();
            playerNames.Clear();
            pendingPlayerNames.Clear();
            RefreshInsideRoomView();
            ActivatePanel(gameOptionsPanel.name);
        }

        private void RefreshInsideRoomView()
        {
            foreach (var entry in playerListGameObjects.Values)
            {
                Destroy(entry);
            }

            playerListGameObjects.Clear();

            bool inRoom = networkManager != null && networkManager.IsListening;
            if (startGameButton != null)
            {
                startGameButton.SetActive(inRoom && networkManager.IsServer);
            }

            if (!inRoom)
            {
                return;
            }

            if (roomInfoText != null)
            {
                roomInfoText.text = "Room Name: " + roomName + " " +
                                    "Players/Max.Players: " + playerNames.Count + "/" + maxPlayers;
            }

            foreach (var pair in playerNames)
            {
                GameObject entry = Instantiate(playerListPrefab, playerListContent.transform);
                entry.transform.localScale = Vector3.one;
                SetChildText(entry, "PlayerNameText", pair.Value);

                var indicator = entry.transform.Find("PlayerIndicator");
                if (indicator != null)
                {
                    indicator.gameObject.SetActive(pair.Key == networkManager.LocalClientId);
                }

                playerListGameObjects[pair.Key] = entry;
            }
        }

        private void RefreshRoomListView()
        {
            foreach (var entry in roomListGameObjects.Values)
            {
                Destroy(entry);
            }

            roomListGameObjects.Clear();

            foreach (var room in discovery.Rooms)
            {
                GameObject entry = Instantiate(roomListEntryPrefab, roomListParentGameObject.transform);
                entry.transform.localScale = Vector3.one;
                SetChildText(entry, "RoomNameText", room.Name);
                SetChildText(entry, "RoomPlayersText", room.Players + " / " + room.MaxPlayers);

                var joinButton = entry.transform.Find("JoinRoomButton");
                if (joinButton != null && joinButton.TryGetComponent(out Button button))
                {
                    var roomToJoin = room;
                    button.interactable = room.Players < room.MaxPlayers;
                    button.onClick.AddListener(() => JoinRoom(roomToJoin));
                }

                roomListGameObjects[room.SessionId] = entry;
            }
        }

        private void SetConnectingText(string message)
        {
            var text = joinRandomRoomPanel != null ? joinRandomRoomPanel.GetComponentInChildren<Text>(true) : null;
            if (text != null)
            {
                text.text = message;
            }
        }

        private static void SetChildText(GameObject root, string childName, string value)
        {
            var child = root.transform.Find(childName);
            if (child != null && child.TryGetComponent(out Text text))
            {
                text.text = value;
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// NetworkManager DontDestroyOnLoad'dur. Oyundan lobiye dönüldüğünde sahnedeki yeni kopya yok edilir,
        /// ilk oluşturulan örnek kullanılmaya devam eder.
        /// </summary>
        private static NetworkManager ResolveNetworkManager()
        {
            var singleton = NetworkManager.Singleton;
            foreach (var candidate in FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (singleton != null && candidate != singleton)
                {
                    Destroy(candidate.gameObject);
                }
            }

            return singleton != null ? singleton : FindAnyObjectByType<NetworkManager>();
        }

        private UnityTransport GetTransport()
        {
            return (UnityTransport)networkManager.NetworkConfig.NetworkTransport;
        }

        private string DescribeConnectionState()
        {
            if (networkManager == null || !networkManager.IsListening)
            {
                return "Disconnected";
            }

            if (networkManager.IsHost)
            {
                return "Hosting";
            }

            return networkManager.IsConnectedClient ? "Connected" : "Connecting";
        }

        private static string Truncate(string value, int maxLength)
        {
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        #endregion
    }
}
