using System;
using System.Collections.Generic;
using UnetServerDll;

namespace Server.Core
{
    public class RelayConfig
    {
        public static RelayConfig CurrentConfig;
        public ConnectionConfig connectionConfig;
        public GlobalConfig globalConfig;
        public ushort maxConnections;
        public ushort relayPort;
        public ushort bufferSize;
        public bool enableRuntimeMetaLogging = true;
        public List<string> channels = new List<string>();
        public int bandwidthGracePrediodLength = 60;
        public int gracePeriodBandwidthLimit = 4000;
        public int bandwidthLimit = 2000;

        internal void SetChannels()
        {
            connectionConfig.Channels.Clear();
            foreach (var channel in channels)
                connectionConfig.AddChannel(StringToQosType(channel));
        }

        public void UnSetChannels()
        {
            foreach (var channel in connectionConfig.Channels)
                channels.Add(channel.QOS.ToString());
            connectionConfig.Channels.Clear();
        }


        private static readonly QosType[] fields = (QosType[])Enum.GetValues(typeof(QosType));
        private QosType StringToQosType(string s)
        {
            foreach (var field in fields)
            {
                if (s == field.ToString())
                    return field;
            }
            throw new InvalidConfigException($"Supplied QosType \"{s}\" is invalid");
        }
    }
}
