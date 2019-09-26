using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnetServerDll;

namespace MLAPI.Relay.Transports
{
    public class UnetTransport : Transport
    {
        private int hostId;
        private NetLibraryManager unetManager;

        public override void Disconnect(ulong connectionId)
        {
            unetManager.Disconnect(hostId, (int)connectionId, out byte error);
        }

        public override object GetConfig()
        {
            return new UnetConfig()
            {
                ConnectionConfig = new ConnectionConfig(),
                GlobalConfig = new GlobalConfig()
            };
        }

        public override IPEndPoint GetEndPoint(ulong connectionId)
        {
            unetManager.GetConnectionInfo(hostId, (int)connectionId, out string address, out int port, out byte error);

            if ((NetworkError)error == NetworkError.Ok)
            {
                return new IPEndPoint(IPAddress.Parse(address).MapToIPv6(), port);
            }

            return null;
        }

        public override NetEventType Poll(out ulong connectionId, out byte channelId, out ArraySegment<byte> payload)
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

        public override void Send(ArraySegment<byte> payload, byte channelId, ulong connectionId)
        {
            if (payload.Offset > 0)
            {
                // UNET cannot handle offsets!
                // TODO: Copy

                throw new Exception("UNET cannot handle offsets!");
            }

            unetManager.Send(hostId, (int)connectionId, channelId, payload.Array, payload.Count, out byte error);
        }

        public override void Start(object config)
        {
            UnetConfig unetConfig = null;

            if (config is UnetConfig) unetConfig = (UnetConfig)config;
            else if (config is JObject) unetConfig = ((JObject)config).ToObject<UnetConfig>();

            for (int i = 0; i < unetConfig.Channels.Count; i++)
            {
                unetConfig.ConnectionConfig.AddChannel(unetConfig.Channels[i]);
            }

            Program.DEFAULT_CHANNEL_BYTE = unetConfig.ConnectionConfig.AddChannel(QosType.ReliableSequenced);

            unetManager = new NetLibraryManager(unetConfig.GlobalConfig);

            hostId = unetManager.AddHost(new HostTopology(unetConfig.ConnectionConfig, unetConfig.MaxConnections), Program.Config.ListenPort, null);
        }

        public override RelayConfig BeforeSerializeConfig(RelayConfig config)
        {
            UnetConfig unetConfig = null;

            if (config.TransportConfig is UnetConfig) unetConfig = (UnetConfig)config.TransportConfig;
            else if (config.TransportConfig is JObject) unetConfig = ((JObject)config.TransportConfig).ToObject<UnetConfig>();

            unetConfig.Channels.Clear();

            for (int i = 0; i < unetConfig.ConnectionConfig.Channels.Count; i++)
            {
                unetConfig.Channels.Add(unetConfig.ConnectionConfig.Channels[i].QOS);
            }

            unetConfig.ConnectionConfig.Channels.Clear();

            return config;
        }

        public override RelayConfig AfterDeserializedConfig(RelayConfig config)
        {
            UnetConfig unetConfig = null;

            if (config.TransportConfig is UnetConfig) unetConfig = (UnetConfig)config.TransportConfig;
            else if (config.TransportConfig is JObject) unetConfig = ((JObject)config.TransportConfig).ToObject<UnetConfig>();

            unetConfig.ConnectionConfig.Channels.Clear();

            for (int i = 0; i < unetConfig.Channels.Count; i++)
            {
                unetConfig.ConnectionConfig.AddChannel(unetConfig.Channels[i]);
            }

            return config;
        }

        public override string ProcessSerializedJson(string json)
        {
            return json.Replace(",\n      \"ChannelCount\": 0,\n      \"SharedOrderChannelCount\": 0,\n      \"Channels\": []", "");
        }

        public class UnetConfig
        {
            public ushort MaxConnections { get; set; } = 100;
            public ConnectionConfig ConnectionConfig { get; set; } = new ConnectionConfig();
            public GlobalConfig GlobalConfig { get; set; } = new GlobalConfig();
            [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
            public List<QosType> Channels = new List<QosType>();
        }
    }
}
