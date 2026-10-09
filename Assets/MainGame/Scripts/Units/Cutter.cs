using UnityEngine;

namespace MainGame.Scripts
{
    /// <summary>
    /// Oduncu: yalnızca ağaçları keser.
    /// </summary>
    public class Cutter : UnitAttack
    {
        [SerializeField] private float axRange = 2f;
        [SerializeField] private float axDamage = 10f;

        protected override float Range => axRange;
        protected override float Damage => axDamage;

        public override bool CanAttack(GameObject candidate)
        {
            return candidate != null
                   && candidate.GetComponent<Tree>() != null
                   && candidate.GetComponent<HealthTree>() != null;
        }
    }
}
