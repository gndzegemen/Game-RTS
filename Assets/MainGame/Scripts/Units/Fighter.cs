using UnityEngine;

namespace MainGame.Scripts
{
    /// <summary>
    /// Düşman takımın unit ve binalarına saldırır.
    /// (Eski sürümdeki "castle(Clone)" isim kontrolüyle OnTriggerStay'de her fizik karesinde hasar veren
    /// kod kaldırıldı; binalar artık normal hedef olarak seçilip saldırılabiliyor.)
    /// </summary>
    public class Fighter : UnitAttack
    {
        [SerializeField] private float weaponRange = 2f;
        [SerializeField] private float weaponDamage = 10f;

        protected override float Range => weaponRange;
        protected override float Damage => weaponDamage;

        public override bool CanAttack(GameObject candidate)
        {
            if (candidate == null || candidate.GetComponent<CombatTarget>() == null || candidate.GetComponent<Health>() == null)
            {
                return false;
            }

            return this.GetTeam().IsEnemyOf(candidate.transform.GetTeam());
        }
    }
}
