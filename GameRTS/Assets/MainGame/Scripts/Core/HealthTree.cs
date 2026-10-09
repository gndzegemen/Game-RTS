using UnityEngine;

namespace MainGame.Scripts
{
    /// <summary>
    /// Ağaç canı. Kesildiğinde altın yerine odun verir.
    /// </summary>
    public class HealthTree : Health
    {
        [SerializeField] private int woodReward = 50;

        protected override void GrantReward(PlayerState killer)
        {
            killer.AddWood(woodReward);
        }
    }
}
