using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnetServerDll;

namespace Server.Core
{
    class Program
    {
        public static NetLibraryManager unet;
        public static List<Room> rooms = new List<Room>();
        public static Dictionary<string, Room> addressToRoom = new Dictionary<string, Room>();
        public static byte[] messageBuffer;

        public static byte GetReliableChannel()
        {
            for (byte i = 0; i < RelayConfig.CurrentConfig.connectionConfig.Channels.Count; i++)
            {
                switch (RelayConfig.CurrentConfig.connectionConfig.Channels[i].QOS)
                {
                    case QosType.Reliable:
                    case QosType.ReliableFragmented:
                    case QosType.ReliableFragmentedSequenced:
                    case QosType.ReliableSequenced:
                        return i;
                }
            }
            throw new InvalidConfigException("A reliable channel is required");
        }

        internal static ushort connectedPeers = 0;

        static void Main(string[] args)
        {
            Console.Title = "MLAPI.Relay";
            string configPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config.json");
            RelayConfig relayConfig = null;
            if (!File.Exists(configPath))
            {
                Console.WriteLine("================================");
                Console.WriteLine("There is no config. Please select a template for the config file:");
                Console.WriteLine("[M]LAPI");
                Console.WriteLine("[H]LAPI");
                Console.WriteLine("[E]mpty");
                ConsoleKey key = ConsoleKey.Escape;
                do key = Console.ReadKey(true).Key;
                while (key != ConsoleKey.M && key != ConsoleKey.H && key != ConsoleKey.E);

                if (key == ConsoleKey.M)
                {
                    ConnectionConfig cConfig = new ConnectionConfig()
                    {
                        SendDelay = 0
                    };
                    cConfig.AddChannel(QosType.ReliableFragmentedSequenced);
                    cConfig.AddChannel(QosType.Reliable);
                    cConfig.AddChannel(QosType.UnreliableSequenced);
                    cConfig.AddChannel(QosType.ReliableSequenced);
                    cConfig.AddChannel(QosType.ReliableSequenced);
                    cConfig.AddChannel(QosType.UnreliableSequenced);
                    cConfig.AddChannel(QosType.Unreliable);
                    relayConfig = new RelayConfig()
                    {
                        bufferSize = 4096,
                        connectionConfig = cConfig,
                        globalConfig = new GlobalConfig(),
                        maxConnections = ushort.MaxValue - 1,
                        relayPort = 8888
                    };
                }
                else if (key == ConsoleKey.H)
                {
                    ConnectionConfig cConfig = new ConnectionConfig();
                    cConfig.AddChannel(QosType.ReliableSequenced);
                    cConfig.AddChannel(QosType.Unreliable);
                    relayConfig = new RelayConfig()
                    {
                        bufferSize = 4096,
                        connectionConfig = cConfig,
                        globalConfig = new GlobalConfig(),
                        maxConnections = ushort.MaxValue - 1,
                        relayPort = 8888
                    };
                }
                else if (key == ConsoleKey.E)
                {
                    ConnectionConfig cConfig = new ConnectionConfig();
                    relayConfig = new RelayConfig()
                    {
                        bufferSize = 1024,
                        connectionConfig = cConfig,
                        globalConfig = new GlobalConfig(),
                        maxConnections = ushort.MaxValue - 1,
                        relayPort = 8888
                    };
                }

                relayConfig.UnSetChannels();
                File.WriteAllText(configPath, JsonConvert.SerializeObject(relayConfig, Formatting.Indented).Replace(@",
    ""ChannelCount"": 0,
    ""SharedOrderChannelCount"": 0,
    ""Channels"": []", ""));
            }
            else
            {
                try
                {
                    relayConfig = JsonConvert.DeserializeObject<RelayConfig>(File.ReadAllText(configPath));
                    relayConfig.SetChannels();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] Error parsing config file: " + ex.Message);
                    Console.Read();
                    return;
                }
            }

            try
            {
                Start(relayConfig);
            }
            catch(DllNotFoundException e)
            {
                ReportError("[FATAL] Could not locate one or more shared libraries! Message: \n" + e.Message);
            }
            catch(Exception e)
            {
                ReportError("[FATAL] An unexpected error occurred! Message: \n" + e.Message);
            }
        }

        private static void ReportError(string errorMessage)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(errorMessage);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static string ReadLine(int bufferSize)
        {
            byte[] inputBuffer = new byte[bufferSize];
            Stream inputStream = Console.OpenStandardInput(inputBuffer.Length);
            Console.SetIn(new StreamReader(inputStream, Console.InputEncoding, false, inputBuffer.Length));
            return Console.ReadLine();
        }

        private static ushort lastConnectedClients;
        private static long lastPrintedConnectedClientsTick;
        private static Timer statisticsReportTimer = null;
        static void Start(RelayConfig rConfig)
        {
            RelayConfig.CurrentConfig = rConfig;
            Console.WriteLine("================================");
            Console.WriteLine("[INFO] Starting relay...");
            unet = new NetLibraryManager(rConfig.globalConfig);

            messageBuffer = new byte[RelayConfig.CurrentConfig.bufferSize];
            HostTopology hostTopology = new HostTopology(RelayConfig.CurrentConfig.connectionConfig, RelayConfig.CurrentConfig.maxConnections);
            unet.AddHost(hostTopology, RelayConfig.CurrentConfig.relayPort, null);
            Console.WriteLine("[INFO] Relay started!");
            Console.WriteLine("[INFO] Press [Q] to quit the application");
            while ((!Console.KeyAvailable || Console.ReadKey(true).Key != ConsoleKey.Q))
            {
                NetworkEventType @event = unet.Receive(out int hostId, out int connectionId, out int channelId, messageBuffer, RelayConfig.CurrentConfig.bufferSize, out int receivedSize, out byte errorByte);
                NetworkError error = (NetworkError)errorByte;

                if (error != NetworkError.Ok)
                {
                    ConsoleColor previousColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] Relay encountered UNET transport error \"" + error.ToString() + "\" during a \"" + @event.ToString() + "\" event.");
                    Console.ForegroundColor = previousColor;
                }

                switch (@event)
                {
                    case NetworkEventType.DataEvent:
                        {
                            MessageType mType = (MessageType)messageBuffer[receivedSize - 1];
                            switch (mType)
                            {
                                case MessageType.StartServer:
                                    {
                                        //Check if they are already connected or perhaps are already hosting, if so return
                                        Client client;
                                        Room room = new Room(client = new Client()
                                        {
                                            connectionId = (ushort)connectionId,
                                            hostId = (byte)hostId,
                                            isServer = true,
                                            connectTick = DateTime.UtcNow.Ticks
                                        });
                                        rooms.Add(room);
                                        unet.GetConnectionInfo(hostId, connectionId, out string address, out int port, out byte byteError);
                                        if (RelayConfig.CurrentConfig.enableRuntimeMetaLogging) Console.WriteLine("[INFO] Server started from address " + address);
                                        addressToRoom.Add(address + ":" + port, room);
                                    }
                                    break;
                                case MessageType.ConnectToServer:
                                    {
                                        //Check if they are already connected or perhaps are already hosting; if so, return
                                        byte addressLength = (byte)(receivedSize - 1);//messageBuffer[1]; // address length + ip type
                                        string addressAndPort = Encoding.ASCII.GetString(messageBuffer, 0, addressLength);
                                        addressAndPort = addressAndPort.AsIPv6CompatString();
                                        if (RelayConfig.CurrentConfig.enableRuntimeMetaLogging) Console.WriteLine("[INFO] Connection requested to address " + addressAndPort);
                                        if (addressToRoom.ContainsKey(addressAndPort))
                                        {
                                            if (RelayConfig.CurrentConfig.enableRuntimeMetaLogging) Console.WriteLine("[INFO] Connection approved");
                                            Room room = addressToRoom[addressAndPort];
                                            Client client = new Client()
                                            {
                                                connectionId = (ushort)connectionId,
                                                hostId = (byte)hostId,
                                                isServer = false,
                                                connectTick = DateTime.UtcNow.Ticks
                                            };
                                            room.HandleClientConnect(client);
                                        }
                                    }
                                    break;
                                case MessageType.Data:
                                    {
                                        foreach (var room in rooms)
                                        {
                                            if (room.HasPeer((ushort)connectionId, out bool isServer))
                                            {
                                                // Found a matching client in room
                                                if (isServer)
                                                {
                                                    ushort destination = (ushort)(messageBuffer[receivedSize - 3] | (messageBuffer[receivedSize - 2] << 8));
                                                    //Safety check. Make sure who they want to send to ACTUALLY belongs to their room
                                                    if (room.HasPeer(destination, out isServer) && !isServer)
                                                    {
                                                        // TODO: Use unsafe to make messageBuffer look smaller
                                                        //ReverseOffset(messageBuffer, 2, receivedSize);
                                                        messageBuffer[receivedSize - 3] = (byte)MessageType.Data;          // [data, data, data, dest1, dest2, mtype_r, none, none, none] => [{data, data, data, mtype_s}, dest2, mtype_r, none, none, none]
                                                        room.Send(hostId, destination, connectionId, channelId, messageBuffer, receivedSize - 2, out errorByte);
                                                    }
                                                }
                                                else
                                                {
                                                    // Read client message
                                                    //ForwardOffset(messageBuffer, 2, receivedSize);
                                                    messageBuffer.ToBytes((ushort)connectionId, receivedSize - 1); // Put connection id at the end of the recieved message (because optimization)
                                                    messageBuffer[receivedSize + 1] = (byte)MessageType.Data; // Set message type
                                                    room.Send(hostId, room.ServerID, connectionId, channelId, messageBuffer, receivedSize + 2, out errorByte);
                                                }
                                            }
                                        }
                                    }
                                    break;
                                case MessageType.ClientDisconnect:
                                    {
                                        ushort cid = messageBuffer.FromBytes(1);
                                        if (RelayConfig.CurrentConfig.enableRuntimeMetaLogging) Console.WriteLine("[INFO] Client disconnect request");
                                        foreach (Room room in rooms)
                                            if (room.HandleClientDisconnect((ushort)cid, true))
                                            {
                                                --connectedPeers;
                                                break;
                                            }
                                    }
                                    break;
                            }
                        }
                        break;
                    case NetworkEventType.DisconnectEvent:
                        {
                            if (RelayConfig.CurrentConfig.enableRuntimeMetaLogging) Console.WriteLine("[INFO] Peer disconnected");
                            foreach (Room room in rooms)
                                if (room.HandleClientDisconnect((ushort)connectionId))
                                {
                                    --connectedPeers;
                                    break;
                                }
                        }
                        break;
                    case NetworkEventType.Nothing:
                        Thread.Sleep(1);
                        break;
                    case NetworkEventType.ConnectEvent:
                        {
                            connectedPeers++;
                        }
                        break;
                }
                if (lastConnectedClients != connectedPeers && (DateTime.UtcNow.Ticks - lastPrintedConnectedClientsTick) > 30 * TimeSpan.TicksPerSecond)
                {
                    lastPrintedConnectedClientsTick = DateTime.UtcNow.Ticks;
                    lastConnectedClients = connectedPeers;
                    PrintStatusUpdate();
                }
            }
            Console.WriteLine("[INFO] Relay shutting down...");
        }

        private static void PrintStatusUpdate()
        {
            if (RelayConfig.CurrentConfig.enableRuntimeMetaLogging) Console.WriteLine("[STATUS] Connected peers: " + connectedPeers + " / " + RelayConfig.CurrentConfig.maxConnections);
        }
    }

    public class InvalidConfigException : Exception
    {
        public InvalidConfigException() { }
        public InvalidConfigException(string issue) : base(issue) { }
    }
}
