using Unity.Netcode;

namespace MainGame.Scripts
{
    /// <summary>
    /// Bir client'ın hangi takımda olduğunu belirleyen tek nokta.
    /// Oyun 1v1 host modelinde çalışır: host (server client) A takımı, bağlanan oyuncu B takımı.
    /// Takım sahiplikten (OwnerClientId) türetildiği için ayrıca senkronize edilmesine gerek yoktur.
    /// </summary>
    public static class TeamAssignment
    {
        public const int MaxPlayers = 2;

        public static Team ForClient(ulong clientId)
        {
            return clientId == NetworkManager.ServerClientId ? Team.A : Team.B;
        }

        public static Team LocalTeam
        {
            get
            {
                var networkManager = NetworkManager.Singleton;
                return networkManager != null && networkManager.IsListening
                    ? ForClient(networkManager.LocalClientId)
                    : Team.None;
            }
        }
    }
}
