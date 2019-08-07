using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Ruffles.Configuration;
using UnetServerDll;

namespace Server.Core
{
    public class RelayConfig
    {
        public static RelayConfig CurrentConfig;

        public TransportType Transport = TransportType.Ruffles;

        public ConnectionConfig UnetConnectionConfig;
        public GlobalConfig UnetGlobalConfig;

        [JsonConverter(typeof(StringEnumConverter))]
        public SocketConfig RufflesSocketConfig;

        public ushort MaxConnections = 100;
        public ushort RelayPort = 8888;
        public ushort BufferSize = 1024 * 8;
        public bool EnableRuntimeMetaLogging = true;
        public List<string> Channels = new List<string>();
        public int BandwidthGracePrediodLength = 60;
        public int GracePeriodBandwidthLimit = 4000;
        public int BandwidthLimit = 2000;

        internal void SetChannels()
        {
            UnetConnectionConfig.Channels.Clear();

            foreach (string channel in Channels)
            {
                UnetConnectionConfig.AddChannel(StringToQosType(channel));
            }
        }

        public void UnSetChannels()
        {
            foreach (ChannelQOS channel in UnetConnectionConfig.Channels)
            {
                Channels.Add(channel.QOS.ToString());
            }

            UnetConnectionConfig.Channels.Clear();
        }


        private static readonly QosType[] fields = (QosType[])Enum.GetValues(typeof(QosType));

        private QosType StringToQosType(string s)
        {
            foreach (QosType field in fields)
            {
                if (s == field.ToString())
                    return field;
            }

            throw new InvalidConfigException($"Supplied QosType \"{s}\" is invalid");
        }
    }

    public enum TransportType
    {
        Ruffles,
        UNET
    }
}
