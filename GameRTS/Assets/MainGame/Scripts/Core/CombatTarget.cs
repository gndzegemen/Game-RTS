using UnityEngine;

namespace MainGame.Scripts
{
    /// <summary>
    /// Savaşçı unit'lerin saldırabileceği objeleri (unit, bina) işaretler.
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatTarget : MonoBehaviour
    {
        public Team Team => this.GetTeam();
    }
}
