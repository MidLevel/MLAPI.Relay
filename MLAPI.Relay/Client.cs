using System;
using Ruffles.Connections;

namespace Server.Core
{
    public class Client
    {
        public bool isServer;
        public byte hostId;
        public ushort connectionId;
        public Connection connection;
        private bool lastCheck = true;
        public bool bandwidthGraceperiod => lastCheck = DateTime.UtcNow.Ticks - connectTick >= RelayConfig.CurrentConfig.BandwidthGracePrediodLength * TimeSpan.TicksPerSecond;
        public long connectTick;
    }
}
