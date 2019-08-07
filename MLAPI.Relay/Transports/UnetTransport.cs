using System;
using System.Net;
using UnetServerDll;

namespace MLAPI.Relay.Transports
{
    public class UnetTransport : ITransport
    {
        private int hostId;
        private NetLibraryManager unetManager;

        public void Disconnect(ulong connectionId)
        {
            unetManager.Disconnect(hostId, (int)connectionId, out byte error);
        }

        public object GetConfig()
        {
            return new UnetConfig()
            {
                ConnectionConfig = new ConnectionConfig(),
                GlobalConfig = new GlobalConfig()
            };
        }

        public IPEndPoint GetEndPoint(ulong connectionId)
        {
            unetManager.GetConnectionInfo(hostId, (int)connectionId, out string address, out int port, out byte error);

            if ((NetworkError)error == NetworkError.Ok)
            {
                return new IPEndPoint(IPAddress.Parse(address).MapToIPv6(), port);
            }

            return null;
        }

        public NetEventType Poll(out ulong connectionId, out byte channelId, out ArraySegment<byte> payload)
        {
            NetworkEventType eventType = unetManager.ReceiveFromHost(hostId, out int _connectionId, out int _channelId, Program.MESSAGE_BUFFER, Program.MESSAGE_BUFFER.Length, out int receivedSize, out byte error);

            // Cast to correct types (this is fine because in UNET, under the hood, connectionIds are ushort and channelIds are byte. They are just exposed in C# as int)
            connectionId = (ulong)_connectionId;
            channelId = (byte)_channelId;

            // Wrap buffer
            payload = new ArraySegment<byte>(Program.MESSAGE_BUFFER, 0, receivedSize);

            if ((NetworkError)error == NetworkError.Timeout)
            {
                eventType = NetworkEventType.DisconnectEvent;
            }

            switch (eventType)
            {
                case NetworkEventType.DataEvent:
                    {
                        return NetEventType.Data;
                    }
                case NetworkEventType.ConnectEvent:
                    {
                        return NetEventType.Connect;
                    }
                case NetworkEventType.DisconnectEvent:
                    {
                        return NetEventType.Disconnect;
                    }
                default:
                    {
                        return NetEventType.Nothing;
                    }
            }
        }

        public void Send(ArraySegment<byte> payload, byte channelId, ulong connectionId)
        {
            if (payload.Offset > 0)
            {
                // UNET cannot handle offsets!
                // TODO: Copy

                throw new Exception("UNET cannot handle offsets!");
            }

            unetManager.Send(hostId, (int)connectionId, channelId, payload.Array, payload.Count, out byte error);
        }

        public void Start(object config)
        {
            UnetConfig unetConfig = (UnetConfig)config;

            Program.DEFAULT_CHANNEL_BYTE = unetConfig.ConnectionConfig.AddChannel(QosType.ReliableSequenced);

            unetManager = new NetLibraryManager(unetConfig.GlobalConfig);

            hostId = unetManager.AddHost(new HostTopology(unetConfig.ConnectionConfig, unetConfig.MaxConnections), Program.Config.ListenPort, null);
        }

        public class UnetConfig
        {
            public ushort MaxConnections { get; set; } = 100;
            public ConnectionConfig ConnectionConfig { get; set; } = new ConnectionConfig();
            public GlobalConfig GlobalConfig { get; set; } = new GlobalConfig();
        }
    }
}
