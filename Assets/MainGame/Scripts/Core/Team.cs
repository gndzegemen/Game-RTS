using UnityEngine;

namespace MainGame.Scripts
{
    /// <summary>
    /// Takım bilgisi. Eskiden "TeamA"/"TeamB", "UserA"/"CanvasB" gibi tag'lerle ve
    /// ActorNumber kontrolleriyle dağınık şekilde tutuluyordu; artık tek bir veri tipi.
    /// </summary>
    public enum Team : byte
    {
        None = 0,
        A = 1,
        B = 2,
    }

    /// <summary>
    /// Takımı olan her şey (unit, bina) bu arayüzü uygular.
    /// </summary>
    public interface ITeamMember
    {
        Team Team { get; }
    }

    public static class TeamExtensions
    {
        public static bool IsEnemyOf(this Team self, Team other)
        {
            return self != Team.None && other != Team.None && self != other;
        }

        public static Team GetTeam(this Component component)
        {
            var member = component != null ? component.GetComponentInParent<ITeamMember>() : null;
            return member?.Team ?? Team.None;
        }
    }
}
