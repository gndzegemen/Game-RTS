using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MainGame.Scripts
{
    /// <summary>
    /// Oyun sahnesinin maç yöneticisi.
    /// - Sahne tüm client'larda yüklendiğinde (sunucu) her oyuncu için Camera Parent'ı player object olarak spawn eder.
    ///   Eski sürüm spawn sonrası prefab asset'inin child tag'lerini değiştiriyordu (asset'i bozuyordu); artık gerek yok.
    /// - Takım bazlı spawn noktalarının tek kaynağıdır (daha önce birkaç script'e hardcode dağılmıştı).
    /// - Bağlantı koparsa lobiye döner.
    /// </summary>
    public class RTSGameManager : MonoBehaviour
    {
        [Serializable]
        private class TeamSetup
        {
            public Vector3 cameraSpawnPosition;
            public Vector3 unitSpawnEuler;
            public Vector3 woodCutterSpawnPosition;
            public Vector3 archerSpawnPosition;
            public Vector3 swatSpawnPosition;
        }

        [Tooltip("Her oyuncu için spawn edilecek kamera/HUD prefabı (NetworkObject içermeli).")]
        [SerializeField] private GameObject cameraParent;

        [SerializeField] private string lobbySceneName = "LobbyScene";

        [SerializeField] private TeamSetup teamA = new TeamSetup
        {
            cameraSpawnPosition = new Vector3(62.48f, 28.52f, 31.99f),
            unitSpawnEuler = new Vector3(0f, -90f, 0f),
            woodCutterSpawnPosition = new Vector3(54.86f, 10.99f, 77.33f),
            archerSpawnPosition = new Vector3(54.86f, 10.99f, 51.37f),
            swatSpawnPosition = new Vector3(72.85f, 10.99f, 64.42f),
        };

        [SerializeField] private TeamSetup teamB = new TeamSetup
        {
            cameraSpawnPosition = new Vector3(10.12f, 28.52f, 31.99f),
            unitSpawnEuler = new Vector3(0f, 90f, 0f),
            woodCutterSpawnPosition = new Vector3(14.7f, 10.99f, 77.33f),
            archerSpawnPosition = new Vector3(15.5f, 10.99f, 51.4f),
            swatSpawnPosition = new Vector3(2.8f, 10.99f, 64.4f),
        };

        [Tooltip("Aynı noktada spawn olan unit'ler arasındaki mesafe.")]
        [SerializeField] private float unitSpacing = 1f;
        [SerializeField] private int unitsPerRow = 8;

        private readonly Dictionary<(Team, UnitType), int> spawnCounters = new Dictionary<(Team, UnitType), int>();
        private NetworkManager networkManager;

        public static RTSGameManager Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            networkManager = NetworkManager.Singleton;

            if (networkManager == null || !networkManager.IsListening)
            {
                Debug.LogWarning("Ağ oturumu yok. Oyunu LobbyScene üzerinden başlatın.");
                return;
            }

            // Sunucu sahneyi istemcilerden önce yükler; event'i kaçırmamak için Awake'te abone olunur.
            if (networkManager.IsServer)
            {
                networkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            }

            networkManager.OnClientStopped += OnClientStopped;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (networkManager == null)
            {
                return;
            }

            if (networkManager.SceneManager != null)
            {
                networkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            }

            networkManager.OnClientStopped -= OnClientStopped;
        }

        /// <summary>Sunucuda, satın alınan bir unit için sıradaki spawn noktasını verir.</summary>
        public void GetNextUnitSpawnPose(Team team, UnitType unitType, out Vector3 position, out Quaternion rotation)
        {
            var setup = GetSetup(team);
            rotation = Quaternion.Euler(setup.unitSpawnEuler);

            spawnCounters.TryGetValue((team, unitType), out int index);
            spawnCounters[(team, unitType)] = index + 1;

            int row = index / Mathf.Max(unitsPerRow, 1);
            int column = index % Mathf.Max(unitsPerRow, 1);
            position = GetBaseSpawnPosition(setup, unitType)
                       - Vector3.right * (column * unitSpacing)
                       + Vector3.forward * (row * unitSpacing);

            if (NavMesh.SamplePosition(position, out var hit, 5f, NavMesh.AllAreas))
            {
                position = hit.position;
            }
        }

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != gameObject.scene.name)
            {
                return;
            }

            foreach (ulong clientId in clientsCompleted)
            {
                SpawnPlayer(clientId);
            }

            foreach (ulong clientId in clientsTimedOut)
            {
                Debug.LogWarning($"Client {clientId} oyun sahnesini zamanında yükleyemedi.");
            }
        }

        private void SpawnPlayer(ulong clientId)
        {
            if (networkManager.SpawnManager.GetPlayerNetworkObject(clientId) != null)
            {
                return;
            }

            var playerPrefab = cameraParent != null ? cameraParent.GetComponent<NetworkObject>() : null;
            if (playerPrefab == null)
            {
                Debug.LogError("Camera Parent prefabında NetworkObject yok. 'Tools/RTS/Migrate To Netcode' menüsünü çalıştırın.");
                return;
            }

            var setup = GetSetup(TeamAssignment.ForClient(clientId));
            playerPrefab.InstantiateAndSpawn(networkManager, clientId, destroyWithScene: true, isPlayerObject: true,
                position: setup.cameraSpawnPosition, rotation: Quaternion.identity);
        }

        private void OnClientStopped(bool wasHost)
        {
            SceneManager.LoadScene(lobbySceneName);
        }

        private TeamSetup GetSetup(Team team)
        {
            return team == Team.B ? teamB : teamA;
        }

        private static Vector3 GetBaseSpawnPosition(TeamSetup setup, UnitType unitType)
        {
            switch (unitType)
            {
                case UnitType.Archer: return setup.archerSpawnPosition;
                case UnitType.Swat: return setup.swatSpawnPosition;
                default: return setup.woodCutterSpawnPosition;
            }
        }
    }
}
