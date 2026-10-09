using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace MainGame.Scripts
{
    public enum BuildingType
    {
        Castle = 0,
        Lumbermill = 1,
        ArcheryRange = 2,
    }

    /// <summary>
    /// Bina verisi (tip, takım, maliyet). Sahnedeki binalar kendilerini kaydeder; diğer sistemler
    /// GameObject.Find("castle" + teamTag) yerine <see cref="TryFind"/> kullanır.
    /// </summary>
    [DisallowMultipleComponent]
    public class Building : MonoBehaviour, ITeamMember
    {
        [SerializeField] private BuildingType buildingType;
        [SerializeField] private Team team;

        [FormerlySerializedAs("name")]
        [SerializeField] private string displayName;

        [Tooltip("Yükseltme maliyeti (odun).")]
        [SerializeField] private int cost;

        [SerializeField] private Sprite sprite;

        [Tooltip("Yükseltme sonrası binanın canı.")]
        [SerializeField] private float upgradedHealth = 300f;

        private static readonly List<Building> ActiveBuildings = new List<Building>();

        public BuildingType BuildingType => buildingType;
        public Team Team => team;
        public string DisplayName => displayName;
        public int Cost => cost;
        public Sprite Sprite => sprite;
        public float UpgradedHealth => upgradedHealth;
        public Health Health { get; private set; }

        public static bool TryFind(Team team, BuildingType type, out Building building)
        {
            foreach (var candidate in ActiveBuildings)
            {
                if (candidate != null && candidate.team == team && candidate.buildingType == type)
                {
                    building = candidate;
                    return true;
                }
            }

            building = null;
            return false;
        }

        private void Awake()
        {
            Health = GetComponent<Health>();
        }

        private void OnEnable()
        {
            ActiveBuildings.Add(this);
        }

        private void OnDisable()
        {
            ActiveBuildings.Remove(this);
        }
    }
}
