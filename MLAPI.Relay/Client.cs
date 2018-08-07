using System;

namespace Server.Core
{
    public class Client
    {
        public bool isServer;
        public byte hostId;
        public ushort connectionId;
        private bool lastCheck = true;
        public bool bandwidthGraceperiod => lastCheck = DateTime.UtcNow.Ticks - connectTick >= RelayConfig.CurrentConfig.bandwidthGracePrediodLength * TimeSpan.TicksPerSecond;
        public long connectTick;
    }
}
