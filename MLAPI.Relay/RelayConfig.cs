using MLAPI.Relay.Transports;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MLAPI.Relay
{
    public class RelayConfig
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public TransportType Transport = TransportType.UNET;
        public object TransportConfig;
        public ushort BufferSize = 1024 * 8;
        public bool EnableRuntimeMetaLogging = true;
        public int BandwidthGracePeriodLength = 60;
        public int GracePeriodBandwidthLimit = 4000;
        public int BandwidthLimit = 2000;
        public bool AllowTemporaryAlloc = true;
        public int MaxTemporaryAlloc = 1024 * 64;
        public ushort ListenPort = 8888;
        public uint TicksPerSecond = 64;
    }

    public enum TransportType
    {
        Ruffles,
        UNET
    }
}
