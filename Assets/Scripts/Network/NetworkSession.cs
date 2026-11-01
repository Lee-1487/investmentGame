using System;
using System.Threading.Tasks;
using BlackWhale.Flow;
using BlackWhale.Shared;
using UnityEngine;

namespace BlackWhale.Network
{
    // D's composition root supplies the B/C services. No fixture fallback exists in player builds.
    public sealed class NetworkSession : MonoBehaviour
    {
        public TcpGameClient Client { get; private set; }
        private TcpGameServer server;
        private bool closing, connecting;
        public event Action<ServerMessage> MessageReceived;

        public async Task Host(IMarketService market, IInformationService information,
            string displayName, int port = 7777, bool allowLan = false,
            TimerMode mode = TimerMode.Realtime, bool fixedOrderForFixture = false)
        {
            if (server != null || Client != null || connecting) throw new InvalidOperationException("Session is already active.");
            var clock = new MonotonicClock();
            var codec = new UnityWireCodec();
            var room = new GameRoom(Guid.NewGuid().ToString("N"), market, information, clock, mode, fixedOrderForFixture);
            server = new TcpGameServer(room, codec, clock, port, allowLan ? System.Net.IPAddress.Any : System.Net.IPAddress.Loopback);
            try { await Connect("127.0.0.1", displayName, server.Port); }
            catch { server?.Dispose(); server = null; throw; }
        }
        public async Task Connect(string host, string displayName, int port = 7777, string reconnectToken = null)
        {
            if (Client != null || connecting || closing) throw new InvalidOperationException("Session is already active or closing.");
            connecting = true;
            try
            {
                TcpGameClient connected = await TcpGameClient.Connect(host, port, displayName, new UnityWireCodec(), reconnectToken);
                if (closing) { connected.Dispose(); return; }
                Client = connected; Client.MessageReceived += OnMessage;
            }
            finally { connecting = false; }
        }
        public void DisconnectClient()
        { Client?.Dispose(); Client = null; }
        private void OnMessage(ServerMessage message) { MessageReceived?.Invoke(message); }
        private void Update() { server?.Pump(); Client?.Pump(); }
        private void OnDestroy()
        {
            closing = true;
            DisconnectClient();
            server?.Dispose(); server = null;
        }
    }
}
