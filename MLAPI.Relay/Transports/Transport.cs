using System;
using System.Net;

namespace MLAPI.Relay.Transports
{
    public abstract class Transport
    {
        public abstract void Send(ArraySegment<byte> payload, byte channelId, ulong connectionId);
        public abstract void Disconnect(ulong connectionId);
        public abstract NetEventType Poll(out ulong connectionId, out byte channelId, out ArraySegment<byte> payload);
        public abstract IPEndPoint GetEndPoint(ulong connectionId);
        public abstract void Start(object config);
        public abstract object GetConfig();

        public virtual RelayConfig BeforeSerializeConfig(RelayConfig config)
        {
            return config;
        }

        public virtual string ProcessSerializedJson(string json)
        {
            return json;
        }

        public virtual RelayConfig AfterDeserializedConfig(RelayConfig config)
        {
            return config;
        }
    }

    public enum NetEventType
    {
        Connect,
        Disconnect,
        Data,
        Nothing
    }
}
