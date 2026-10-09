using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace MainGame.Scripts
{
    /// <summary>
    /// Photon'daki oda listesinin yerine geçen basit LAN oda keşfi (UDP broadcast).
    /// Host <see cref="StartAdvertising"/> ile isteklere yanıt verir; oda listesi açıkken
    /// <see cref="StartSearching"/> periyodik olarak yayın yapar ve gelen yanıtları toplar.
    /// Thread kullanmaz; <see cref="Tick"/> her karede ana thread'den çağrılmalıdır.
    /// </summary>
    public sealed class LanRoomDiscovery : IDisposable
    {
        public struct RoomInfo
        {
            public string SessionId;
            public string Name;
            public string Address;
            public ushort Port;
            public int Players;
            public int MaxPlayers;
            public float LastSeen;
        }

        public struct AdvertisedRoom
        {
            public string Name;
            public ushort Port;
            public int Players;
            public int MaxPlayers;
        }

        private const int DiscoveryPort = 47777;
        private const string RequestToken = "RTS_DISCOVER_V1";
        private const string ResponsePrefix = "RTS_ROOM_V1";
        private const char Separator = '|';
        private const float BroadcastInterval = 1.5f;
        private const float RoomTimeout = 5f;

        private readonly Dictionary<string, RoomInfo> rooms = new Dictionary<string, RoomInfo>();
        private readonly List<string> staleRooms = new List<string>();

        private UdpClient advertiser;
        private Func<AdvertisedRoom> advertisedRoomProvider;
        private string sessionId;

        private UdpClient searcher;
        private float nextBroadcastTime;

        public event Action RoomsChanged;

        public IEnumerable<RoomInfo> Rooms => rooms.Values;
        public bool IsSearching => searcher != null;
        public bool IsAdvertising => advertiser != null;

        public void StartAdvertising(Func<AdvertisedRoom> roomProvider)
        {
            StopAdvertising();
            advertisedRoomProvider = roomProvider;
            sessionId = Guid.NewGuid().ToString("N");

            try
            {
                advertiser = new UdpClient();
                advertiser.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                advertiser.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            }
            catch (SocketException exception)
            {
                Debug.LogWarning($"LAN oda yayını başlatılamadı (port {DiscoveryPort}): {exception.Message}");
                StopAdvertising();
            }
        }

        public void StopAdvertising()
        {
            advertiser?.Close();
            advertiser = null;
            advertisedRoomProvider = null;
        }

        public void StartSearching()
        {
            StopSearching();

            try
            {
                searcher = new UdpClient(0) { EnableBroadcast = true };
                nextBroadcastTime = 0f;
            }
            catch (SocketException exception)
            {
                Debug.LogWarning($"LAN oda araması başlatılamadı: {exception.Message}");
                searcher = null;
            }
        }

        public void StopSearching()
        {
            searcher?.Close();
            searcher = null;

            if (rooms.Count > 0)
            {
                rooms.Clear();
                RoomsChanged?.Invoke();
            }
        }

        public void Tick(float time)
        {
            if (advertiser != null)
            {
                AnswerDiscoveryRequests();
            }

            if (searcher != null)
            {
                if (time >= nextBroadcastTime)
                {
                    SendDiscoveryRequest();
                    nextBroadcastTime = time + BroadcastInterval;
                }

                bool changed = ReadDiscoveryResponses(time);
                changed |= RemoveStaleRooms(time);

                if (changed)
                {
                    RoomsChanged?.Invoke();
                }
            }
        }

        public void Dispose()
        {
            StopAdvertising();
            StopSearching();
        }

        private void AnswerDiscoveryRequests()
        {
            try
            {
                while (advertiser != null && advertiser.Available > 0)
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = advertiser.Receive(ref remote);
                    if (Encoding.UTF8.GetString(data) != RequestToken || advertisedRoomProvider == null)
                    {
                        continue;
                    }

                    var room = advertisedRoomProvider();
                    string response = string.Join(Separator.ToString(), ResponsePrefix, sessionId,
                        room.Port.ToString(CultureInfo.InvariantCulture),
                        room.Players.ToString(CultureInfo.InvariantCulture),
                        room.MaxPlayers.ToString(CultureInfo.InvariantCulture),
                        room.Name ?? string.Empty);
                    byte[] bytes = Encoding.UTF8.GetBytes(response);
                    advertiser.Send(bytes, bytes.Length, remote);
                }
            }
            catch (SocketException)
            {
                // Windows'ta erişilemeyen uç noktalar ICMP "connection reset" üretebilir; yok sayılır.
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void SendDiscoveryRequest()
        {
            byte[] request = Encoding.UTF8.GetBytes(RequestToken);
            TrySend(request, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            // Aynı makinede test ederken broadcast'ın geri dönmediği ağlar için.
            TrySend(request, new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
        }

        private void TrySend(byte[] data, IPEndPoint endPoint)
        {
            try
            {
                searcher?.Send(data, data.Length, endPoint);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private bool ReadDiscoveryResponses(float time)
        {
            bool changed = false;

            try
            {
                while (searcher != null && searcher.Available > 0)
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = searcher.Receive(ref remote);
                    if (TryParseResponse(Encoding.UTF8.GetString(data), remote.Address, time, out var room))
                    {
                        bool isNew = !rooms.TryGetValue(room.SessionId, out var existing);
                        if (!isNew)
                        {
                            // Aynı oda hem loopback hem LAN adresinden yanıt verebilir; ilk adres korunur.
                            room.Address = existing.Address;
                        }

                        changed |= isNew || existing.Players != room.Players || existing.Name != room.Name;
                        rooms[room.SessionId] = room;
                    }
                }
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            return changed;
        }

        private bool RemoveStaleRooms(float time)
        {
            staleRooms.Clear();
            foreach (var pair in rooms)
            {
                if (time - pair.Value.LastSeen > RoomTimeout)
                {
                    staleRooms.Add(pair.Key);
                }
            }

            foreach (var key in staleRooms)
            {
                rooms.Remove(key);
            }

            return staleRooms.Count > 0;
        }

        private static bool TryParseResponse(string message, IPAddress address, float time, out RoomInfo room)
        {
            room = default;
            string[] parts = message.Split(new[] { Separator }, 6);
            if (parts.Length != 6 || parts[0] != ResponsePrefix)
            {
                return false;
            }

            if (!ushort.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort port) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int players) ||
                !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxPlayers))
            {
                return false;
            }

            room = new RoomInfo
            {
                SessionId = parts[1],
                Port = port,
                Players = players,
                MaxPlayers = maxPlayers,
                Name = parts[5],
                Address = address.ToString(),
                LastSeen = time,
            };
            return true;
        }
    }
}
