using System;

namespace MLAPI.Relay
{
    public class Client
    {
        public bool IsServer { get; set; }
        public ulong ConnectionId { get; set; }
        public bool IsInBandwidthGracePeriod => (DateTime.UtcNow - ConnectTime).TotalSeconds >= Program.Config.BandwidthGracePeriodLength;
        public DateTime ConnectTime { get; set; }
        public ulong OutgoingBytes { get; set; }
    }
}
