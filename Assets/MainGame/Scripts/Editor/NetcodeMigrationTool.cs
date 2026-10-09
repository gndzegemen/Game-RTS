using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MainGame.Scripts.EditorTools
{
    /// <summary>
    /// Photon -> Netcode for GameObjects geçişinin asset tarafını yapar (idempotent, tekrar çalıştırılabilir):
    /// 1. Ağ prefablarındaki kayıp script'leri (eski PhotonView vb.) temizler, NetworkObject /
    ///    NetworkTransform / NetworkAnimator ekler.
    /// 2. Ağ prefablarını NGO'nun varsayılan NetworkPrefabsList'ine kaydeder.
    /// 3. LobbyScene'e NetworkManager + UnityTransport kurar.
    /// 4. Oyun sahnesindeki ağaç ve binalara NetworkObject ekler.
    /// Menü: Tools/RTS/Migrate To Netcode. Batch: -executeMethod MainGame.Scripts.EditorTools.NetcodeMigrationTool.RunBatch
    /// </summary>
    public static class NetcodeMigrationTool
    {
        private const string PrefabFolder = "Assets/MainGame/Prefabs/Resources/";
        private const string PlayerPrefabPath = PrefabFolder + "Camera Parent.prefab";
        private const string PrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string LobbyScenePath = "Assets/MainGame/Scenes/LobbyScene.unity";
        private const string GameScenePath = "Assets/MainGame/Scenes/Environment_Free.unity";
        private const string NetworkManagerObjectName = "Netcode NetworkManager";

        private static readonly string[] UnitPrefabPaths =
        {
            PrefabFolder + "SwatUnit.prefab",
            PrefabFolder + "ArcherUnit.prefab",
            PrefabFolder + "WoodCutterUnit.prefab",
        };

        private static readonly string[] BuildingPrefabPaths =
        {
            PrefabFolder + "castleA.prefab",
            PrefabFolder + "castleB.prefab",
            PrefabFolder + "lumbermillA.prefab",
            PrefabFolder + "lumbermillB.prefab",
            PrefabFolder + "archeryrangeA.prefab",
            PrefabFolder + "archeryrangeB.prefab",
        };

        [MenuItem("Tools/RTS/Migrate To Netcode")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Migrate();
            EditorUtility.DisplayDialog("Netcode", "Geçiş tamamlandı. Ayrıntılar Console'da.", "Tamam");
        }

        public static void RunBatch()
        {
            try
            {
                Migrate();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Projedeki TMP Essential Resources eski TextMeshPro 3.0.6 sürümünden kalma; Unity 6'daki uGUI 2.0
        /// sürümüyle güncellenir (editörün açılışta gösterdiği "TMP Importer" penceresinin yaptığı işlem).
        /// </summary>
        [MenuItem("Tools/RTS/Update TMP Essential Resources")]
        public static void ImportTmpEssentials()
        {
            TMPro.TMP_PackageResourceImporter.ImportResources(importEssentials: true, importExamples: false, interactive: false);
        }

        /// <summary>Batch için: paket importu asenkron olduğundan bitince editörü kapatır (-quit kullanmayın).</summary>
        public static void ImportTmpEssentialsBatch()
        {
            AssetDatabase.importPackageCompleted += _ => EditorApplication.Exit(0);
            AssetDatabase.importPackageFailed += (_, error) =>
            {
                Debug.LogError("[NetcodeMigration] TMP import failed: " + error);
                EditorApplication.Exit(1);
            };
            ImportTmpEssentials();
        }

        private static void Migrate()
        {
            foreach (var path in UnitPrefabPaths)
            {
                ProcessPrefab(path, isUnit: true);
            }

            foreach (var path in BuildingPrefabPaths)
            {
                ProcessPrefab(path, isUnit: false);
            }

            ProcessPrefab(PlayerPrefabPath, isUnit: false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var networkPrefabPaths = UnitPrefabPaths.Concat(BuildingPrefabPaths).Append(PlayerPrefabPath).ToArray();
            var prefabsList = EnsurePrefabsList(networkPrefabPaths);
            SetupLobbyScene(prefabsList);
            SetupGameScene();

            // Asset'ler yüklenirken NGO bileşenlerinin OnValidate'i çalışır (GlobalObjectIdHash, animator
            // geçiş tablosu vb.). Yeniden serileştirme bu değerlerin diske yazılmasını garanti eder.
            AssetDatabase.SaveAssets();
            AssetDatabase.ForceReserializeAssets(networkPrefabPaths.Concat(new[] { PrefabsListPath, LobbyScenePath, GameScenePath }));
            AssetDatabase.SaveAssets();
            Debug.Log("[NetcodeMigration] Tamamlandı.");
        }

        private static void ProcessPrefab(string path, bool isUnit)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning($"[NetcodeMigration] Prefab bulunamadı: {path}");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int removed = RemoveMissingScripts(root);
                GetOrAdd<NetworkObject>(root);

                if (isUnit)
                {
                    var networkTransform = GetOrAdd<NetworkTransform>(root);
                    networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
                    networkTransform.SyncScaleX = false;
                    networkTransform.SyncScaleY = false;
                    networkTransform.SyncScaleZ = false;

                    var animator = root.GetComponent<Animator>();
                    if (animator != null)
                    {
                        var networkAnimator = GetOrAdd<NetworkAnimator>(root);
                        networkAnimator.Animator = animator;
                        networkAnimator.AuthorityMode = NetworkAnimator.AuthorityModes.Server;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[NetcodeMigration] {path}: {removed} kayıp script silindi, ağ bileşenleri hazır.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static NetworkPrefabsList EnsurePrefabsList(IEnumerable<string> prefabPaths)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabsListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabsListPath);
            }

            foreach (var path in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponent<NetworkObject>() != null && !list.Contains(prefab))
                {
                    list.Add(new NetworkPrefab { Prefab = prefab });
                }
            }

            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            return list;
        }

        private static void SetupLobbyScene(NetworkPrefabsList prefabsList)
        {
            var scene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);

            var networkManager = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
            if (networkManager == null)
            {
                var go = new GameObject(NetworkManagerObjectName);
                networkManager = go.AddComponent<NetworkManager>();
            }

            var transport = GetOrAdd<UnityTransport>(networkManager.gameObject);
            transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");

            if (networkManager.NetworkConfig == null)
            {
                networkManager.NetworkConfig = new NetworkConfig();
            }

            var config = networkManager.NetworkConfig;
            config.NetworkTransport = transport;
            config.PlayerPrefab = null;
            config.ConnectionApproval = true;
            config.EnableSceneManagement = true;
            config.Prefabs.NetworkPrefabsLists.RemoveAll(entry => entry == null);
            if (!config.Prefabs.NetworkPrefabsLists.Contains(prefabsList))
            {
                config.Prefabs.NetworkPrefabsLists.Add(prefabsList);
            }

            RemoveMissingScriptsInScene(scene);
            EditorUtility.SetDirty(networkManager);
            EditorUtility.SetDirty(transport);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[NetcodeMigration] LobbyScene: NetworkManager hazır.");
        }

        private static void SetupGameScene()
        {
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            int removed = RemoveMissingScriptsInScene(scene);
            int added = 0;

            foreach (var health in Object.FindObjectsByType<Health>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (health.GetComponent<NetworkObject>() == null)
                {
                    health.gameObject.AddComponent<NetworkObject>();
                    added++;
                }
            }

            foreach (var networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                InvokeOnValidate(networkObject);
                if (PrefabUtility.IsPartOfPrefabInstance(networkObject))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(networkObject);
                }

                EditorUtility.SetDirty(networkObject);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[NetcodeMigration] {GameScenePath}: {removed} kayıp script silindi, {added} NetworkObject eklendi.");
        }

        private static int RemoveMissingScripts(GameObject root)
        {
            int removed = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var go = transform.gameObject;
                // İç içe prefab instance'larının bileşenleri kendi prefab asset'inde düzeltilmelidir.
                if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsAddedGameObjectOverride(go))
                {
                    continue;
                }

                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            }

            return removed;
        }

        private static int RemoveMissingScriptsInScene(UnityEngine.SceneManagement.Scene scene)
        {
            int removed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                removed += RemoveMissingScripts(root);
            }

            return removed;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent(out T component) ? component : go.AddComponent<T>();
        }

        /// <summary>NetworkObject.OnValidate internal olduğu için reflection ile çağrılır (GlobalObjectIdHash üretimi).</summary>
        private static void InvokeOnValidate(Object target)
        {
            var method = target.GetType().GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            try
            {
                method?.Invoke(target, null);
            }
            catch (TargetInvocationException exception)
            {
                Debug.LogWarning($"[NetcodeMigration] {target.name} OnValidate: {exception.InnerException?.Message}");
            }
        }
    }
}
