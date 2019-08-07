using System;
using System.Net;

namespace MLAPI.Relay.Transports
{
    public interface ITransport
    {
        void Send(ArraySegment<byte> payload, byte channelName, ulong connectionId);
        void Disconnect(ulong connectionId);
        NetEventType Poll(out ulong connectionId, out byte channelName, out ArraySegment<byte> payload);
        IPEndPoint GetEndPoint(ulong connectionId);
        object GetConfig();
        void Start(object config);
    }

    public enum NetEventType
    {
        Connect,
        Disconnect,
        Data,
        Nothing
    }
}
