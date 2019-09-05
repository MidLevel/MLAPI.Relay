using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Ruffles.Channeling;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Core;

namespace MLAPI.Relay.Transports
{
    public class RufflesTransport : Transport
    {
        private RuffleSocket socket;

        private readonly Dictionary<ulong, IPEndPoint> endpoints = new Dictionary<ulong, IPEndPoint>();

        private NetworkEvent? pendingRecycleEvent = null;
        private Connection pendingRecycleConnection = null;

        public override void Disconnect(ulong connectionId)
        {
            socket.Disconnect(connectionId, true);
        }

        public override object GetConfig()
        {
            return new RufflesConfig()
            {
                DefaultChannelType = ChannelType.ReliableSequenced,
                SocketConfig = new SocketConfig(),
                UseDelay = true
            };
        }

        public override IPEndPoint GetEndPoint(ulong connectionId)
        {
            if (endpoints.ContainsKey(connectionId))
            {
                return endpoints[connectionId];
            }

            // TODO: Handle better
            return null;
        }


        public override NetEventType Poll(out ulong connectionId, out byte channelId, out ArraySegment<byte> payload)
        {
            if (pendingRecycleEvent != null)
            {
                pendingRecycleEvent.Value.Recycle();
                pendingRecycleEvent = null;
            }

            if (pendingRecycleConnection != null)
            {
                pendingRecycleConnection.Recycle();
                pendingRecycleConnection = null;
            }

            socket.RunInternalLoop();

            NetworkEvent @event = socket.Poll();

            channelId = @event.ChannelId;

            switch (@event.Type)
            {
                case NetworkEventType.Connect:
                    {
                        connectionId = @event.Connection.Id;
                        payload = new ArraySegment<byte>();

                        endpoints.Add(connectionId, (IPEndPoint)@event.Connection.EndPoint);

                        return NetEventType.Connect;
                    }
                case NetworkEventType.Timeout:
                case NetworkEventType.Disconnect:
                    {
                        connectionId = @event.Connection.Id;
                        payload = new ArraySegment<byte>();

                        // Will be recycled next iteration
                        pendingRecycleConnection = @event.Connection;

                        endpoints.Remove(connectionId);

                        return NetEventType.Disconnect;
                    }
                case NetworkEventType.Data:
                    {
                        connectionId = @event.Connection.Id;

                        payload = @event.Data;

                        pendingRecycleEvent = @event;

                        return NetEventType.Data;
                    }
                default:
                    {
                        payload = new ArraySegment<byte>();
                        channelId = 0;
                        connectionId = 0;
                        return NetEventType.Nothing;
                    }
            }
        }

        public override void Send(ArraySegment<byte> payload, byte channelId, ulong connectionId)
        {
            socket.Send(payload, connectionId, channelId, false);
        }

        public override void Start(object config)
        {
            RufflesConfig rufflesConfig = null;

            if (config is RufflesConfig) rufflesConfig = (RufflesConfig)config;
            else if (config is JObject) rufflesConfig = ((JObject)config).ToObject<RufflesConfig>();

            rufflesConfig.SocketConfig.DualListenPort = Program.Config.ListenPort;

            ChannelType[] channelTypes = rufflesConfig.SocketConfig.ChannelTypes;
            ChannelType[] newChannelTypes = new ChannelType[channelTypes.Length + 1];

            // Copy old channels
            for (int i = 0; i < channelTypes.Length; i++)
            {
                newChannelTypes[i] = channelTypes[i];
            }

            // Set the default channel
            newChannelTypes[newChannelTypes.Length - 1] = rufflesConfig.DefaultChannelType;

            // Set the default channel byte
            Program.DEFAULT_CHANNEL_BYTE = (byte)(newChannelTypes.Length - 1);

            // Change to the new array
            rufflesConfig.SocketConfig.ChannelTypes = newChannelTypes;

            // Start the socket
            socket = new RuffleSocket((SocketConfig)config);
        }

        public class RufflesConfig
        {
            public SocketConfig SocketConfig { get; set; } = new SocketConfig();
            [JsonConverter(typeof(StringEnumConverter))]
            public ChannelType DefaultChannelType { get; set; } = ChannelType.ReliableSequenced;
            public bool UseDelay { get; set; } = true;
        }
    }
}
