using Newtonsoft.Json;
using Ruffles.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using UnetServerDll;

namespace Server.Core
{
    class Program
    {
        public static NetLibraryManager unet;
        public static RuffleSocket ruffles;

        public static List<Room> rooms = new List<Room>();
        public static Dictionary<string, Room> addressToRoom = new Dictionary<string, Room>();
        public static byte[] messageBuffer;

        public static byte GetReliableChannel()
        {
            for (byte i = 0; i < RelayConfig.CurrentConfig.UnetConnectionConfig.Channels.Count; i++)
            {
                switch (RelayConfig.CurrentConfig.UnetConnectionConfig.Channels[i].QOS)
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
                Console.WriteLine("There is no config. Please select the transport you would like:");
                Console.WriteLine("[R]uffles");
                Console.WriteLine("[U]net");
                ConsoleKey key = ConsoleKey.Escape;
                do key = Console.ReadKey(true).Key;
                while (key != ConsoleKey.R && key != ConsoleKey.U);

                if (key == ConsoleKey.U)
                {
                    Console.WriteLine("================================");
                    Console.WriteLine("There is no config. Please select a template for the config file:");
                    Console.WriteLine("[M]LAPI");
                    Console.WriteLine("[H]LAPI");
                    Console.WriteLine("[E]mpty");
                    key = ConsoleKey.Escape;
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
                            Transport = TransportType.UNET,
                            BufferSize = 4096,
                            RufflesSocketConfig = new Ruffles.Configuration.SocketConfig(),
                            UnetConnectionConfig = cConfig,
                            UnetGlobalConfig = new GlobalConfig(),
                            MaxConnections = ushort.MaxValue - 1,
                            RelayPort = 8888
                        };
                    }
                    else if (key == ConsoleKey.H)
                    {
                        ConnectionConfig cConfig = new ConnectionConfig();
                        cConfig.AddChannel(QosType.ReliableSequenced);
                        cConfig.AddChannel(QosType.Unreliable);

                        relayConfig = new RelayConfig()
                        {
                            Transport = TransportType.UNET,
                            BufferSize = 4096,
                            RufflesSocketConfig = new Ruffles.Configuration.SocketConfig(),
                            UnetConnectionConfig = cConfig,
                            UnetGlobalConfig = new GlobalConfig(),
                            MaxConnections = ushort.MaxValue - 1,
                            RelayPort = 8888
                        };
                    }
                    else if (key == ConsoleKey.E)
                    {
                        relayConfig = new RelayConfig()
                        {
                            Transport = TransportType.UNET,
                            BufferSize = 1024,
                            RufflesSocketConfig = new Ruffles.Configuration.SocketConfig(),
                            UnetConnectionConfig = new ConnectionConfig(),
                            UnetGlobalConfig = new GlobalConfig(),
                            MaxConnections = ushort.MaxValue - 1,
                            RelayPort = 8888
                        };
                    }

                    relayConfig.UnSetChannels();
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(relayConfig, Formatting.Indented).Replace(@",
    ""ChannelCount"": 0,
    ""SharedOrderChannelCount"": 0,
    ""Channels"": []", ""));
                }
                else if (key == ConsoleKey.R)
                {
                    relayConfig = new RelayConfig()
                    {
                        Transport = TransportType.Ruffles,
                        BufferSize = 1024 * 8,
                        RufflesSocketConfig = new Ruffles.Configuration.SocketConfig(),
                        UnetConnectionConfig = new ConnectionConfig(),
                        UnetGlobalConfig = new GlobalConfig(),
                        MaxConnections = ushort.MaxValue - 1,
                        RelayPort = 8888
                    };
                }
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

            if (rConfig.Transport == TransportType.UNET)
            {
                unet = new NetLibraryManager(rConfig.UnetGlobalConfig);
                HostTopology hostTopology = new HostTopology(RelayConfig.CurrentConfig.UnetConnectionConfig, RelayConfig.CurrentConfig.MaxConnections);
                unet.AddHost(hostTopology, RelayConfig.CurrentConfig.RelayPort, null);
            }
            else
            {
                rConfig.RufflesSocketConfig.DualListenPort = rConfig.RelayPort;
                rConfig.RufflesSocketConfig.MaxConnections = rConfig.MaxConnections;
                ruffles = new RuffleSocket(rConfig.RufflesSocketConfig);
            }

            messageBuffer = new byte[RelayConfig.CurrentConfig.BufferSize];
            Console.WriteLine("[INFO] Relay started!");
            Console.WriteLine("[INFO] Press [Q] to quit the application");

            while ((!Console.KeyAvailable || Console.ReadKey(true).Key != ConsoleKey.Q))
            {
                if (rConfig.Transport == TransportType.UNET)
                {
                    UnetServerDll.NetworkEventType @event = unet.Receive(out int hostId, out int connectionId, out int channelId, messageBuffer, RelayConfig.CurrentConfig.BufferSize, out int receivedSize, out byte errorByte);

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
                        case UnetServerDll.NetworkEventType.DataEvent:
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
                                            if (RelayConfig.CurrentConfig.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Server started from address " + address);
                                            addressToRoom.Add(address + ":" + port, room);
                                        }
                                        break;
                                    case MessageType.ConnectToServer:
                                        {
                                            //Check if they are already connected or perhaps are already hosting; if so, return
                                            byte addressLength = (byte)(receivedSize - 1);//messageBuffer[1]; // address length + ip type
                                            string addressAndPort = Encoding.ASCII.GetString(messageBuffer, 0, addressLength);
                                            addressAndPort = addressAndPort.AsIPv6CompatString();
                                            if (RelayConfig.CurrentConfig.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Connection requested to address " + addressAndPort);
                                            if (addressToRoom.ContainsKey(addressAndPort))
                                            {
                                                if (RelayConfig.CurrentConfig.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Connection approved");
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
                                            foreach (Room room in rooms)
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
                                                        room.Send(hostId, (int)room.ServerID, connectionId, channelId, messageBuffer, receivedSize + 2, out errorByte);
                                                    }
                                                }
                                            }
                                        }
                                        break;
                                    case MessageType.ClientDisconnect:
                                        {
                                            ushort cid = messageBuffer.FromBytesUInt16(1);
                                            if (RelayConfig.CurrentConfig.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Client disconnect request");
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
                        case UnetServerDll.NetworkEventType.DisconnectEvent:
                            {
                                if (RelayConfig.CurrentConfig.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Peer disconnected");
                                foreach (Room room in rooms)
                                    if (room.HandleClientDisconnect((ushort)connectionId))
                                    {
                                        --connectedPeers;
                                        break;
                                    }
                            }
                            break;
                        case UnetServerDll.NetworkEventType.Nothing:
                            Thread.Sleep(1);
                            break;
                        case UnetServerDll.NetworkEventType.ConnectEvent:
                            {
                                connectedPeers++;
                            }
                            break;
                    }
                }
                else if (rConfig.Transport == TransportType.Ruffles)
                {
                    ruffles.RunInternalLoop();

                    NetworkEvent @event = ruffles.Poll();

                    switch (@event.Type)
                    {
                        case Ruffles.Core.NetworkEventType.Data:
                            {
                                try
                                {
                                    int receivedSize = @event.Data.Count;
                                    Buffer.BlockCopy(@event.Data.Array, @event.Data.Offset, messageBuffer, 0, @event.Data.Count);

                                    MessageType mType = (MessageType)messageBuffer[receivedSize - 1];

                                    switch (mType)
                                    {
                                        case MessageType.StartServer:
                                            {
                                                //Check if they are already connected or perhaps are already hosting, if so return
                                                Client client;
                                                Room room = new Room(client = new Client()
                                                {
                                                    connection = @event.Connection,
                                                    isServer = true,
                                                    connectTick = DateTime.UtcNow.Ticks
                                                });
                                                rooms.Add(room);

                                                if (RelayConfig.CurrentConfig.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Server started from address " + ((IPEndPoint)@event.Connection.EndPoint).Address.MapToIPv6());
                                                addressToRoom.Add(((IPEndPoint)@event.Connection.EndPoint).Address.MapToIPv6() + ":" + ((IPEndPoint)@event.Connection.EndPoint).Port, room);
                                            }
                                            break;
                                        case MessageType.ConnectToServer:
                                            {
                                                //Check if they are already connected or perhaps are already hosting; if so, return
                                                byte addressLength = (byte)(receivedSize - 1);//messageBuffer[1]; // address length + ip type
                                                string addressAndPort = Encoding.ASCII.GetString(messageBuffer, 0, addressLength);
                                                addressAndPort = addressAndPort.AsIPv6CompatString();
                                                if (RelayConfig.CurrentConfig.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Connection requested to address " + addressAndPort);
                                                if (addressToRoom.ContainsKey(addressAndPort))
                                                {
                                                    if (RelayConfig.CurrentConfig.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Connection approved");
                                                    Room room = addressToRoom[addressAndPort];
                                                    Client client = new Client()
                                                    {
                                                        connection = @event.Connection,
                                                        isServer = false,
                                                        connectTick = DateTime.UtcNow.Ticks
                                                    };
                                                    room.HandleClientConnect(client);
                                                }
                                            }
                                            break;
                                        case MessageType.Data:
                                            {
                                                foreach (Room room in rooms)
                                                {
                                                    if (room.HasPeer(@event.Connection, out bool isServer))
                                                    {
                                                        // Found a matching client in room
                                                        if (isServer)
                                                        {
                                                            ulong destination = (((ulong)messageBuffer[receivedSize - 9]) |
                                                                                ((ulong)messageBuffer[receivedSize - 8] << 8) |
                                                                                ((ulong)messageBuffer[receivedSize - 7] << 16) |
                                                                                ((ulong)messageBuffer[receivedSize - 6] << 24) |
                                                                                ((ulong)messageBuffer[receivedSize - 5] << 32) |
                                                                                ((ulong)messageBuffer[receivedSize - 4] << 40) |
                                                                                ((ulong)messageBuffer[receivedSize - 3] << 48) |
                                                                                ((ulong)messageBuffer[receivedSize - 2] << 56));

                                                            //Safety check. Make sure who they want to send to ACTUALLY belongs to their room
                                                            if (room.HasPeer(destination, out isServer) && !isServer)
                                                            {
                                                                messageBuffer[receivedSize - 9] = (byte)MessageType.Data;          // [data, data, data, dest1, dest2, dest3, dest4, dest5, dest6, dest7, dest8, mtype_r, none, none, none] => [{data, data, data, mtype_s}, dest2, dest3, dest4, dest5, dest6, dest7, dest8, mtype_r, none, none, none]
                                                                room.Send(destination, @event.Connection.Id, @event.ChannelId, messageBuffer, 0, receivedSize - 8);
                                                            }
                                                        }
                                                        else
                                                        {
                                                            // Read client message
                                                            messageBuffer.ToBytes((ulong)@event.Connection.Id, receivedSize - 7); // Put connection id at the end of the recieved message (because optimization)
                                                            messageBuffer[receivedSize + 1] = (byte)MessageType.Data; // Set message type
                                                            room.Send(room.ServerID, @event.Connection.Id, @event.ChannelId, messageBuffer, 0, receivedSize + 8);
                                                        }
                                                    }
                                                }
                                            }
                                            break;
                                        case MessageType.ClientDisconnect:
                                            {
                                                ulong cid = messageBuffer.FromBytesUInt64(1);
                                                if (RelayConfig.CurrentConfig.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Client disconnect request");
                                                foreach (Room room in rooms)
                                                    if (room.HandleClientDisconnect(cid, true))
                                                    {
                                                        --connectedPeers;
                                                        break;
                                                    }
                                            }
                                            break;
                                    }
                                }
                                finally
                                {
                                    @event.Recycle();
                                }
                            }
                            break;
                        case Ruffles.Core.NetworkEventType.Timeout:
                        case Ruffles.Core.NetworkEventType.Disconnect:
                            {
                                if (RelayConfig.CurrentConfig.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Peer disconnected");
                                foreach (Room room in rooms)
                                    if (room.HandleClientDisconnect(@event.Connection.Id))
                                    {
                                        --connectedPeers;
                                        break;
                                    }

                                @event.Connection.Recycle();
                            }
                            break;
                        case Ruffles.Core.NetworkEventType.Nothing:
                            Thread.Sleep(1);
                            break;
                        case Ruffles.Core.NetworkEventType.Connect:
                            {
                                connectedPeers++;
                            }
                            break;
                    }
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
            if (RelayConfig.CurrentConfig.EnableRuntimeMetaLogging) Console.WriteLine("[STATUS] Connected peers: " + connectedPeers + " / " + RelayConfig.CurrentConfig.MaxConnections);
        }
    }

    public class InvalidConfigException : Exception
    {
        public InvalidConfigException() { }
        public InvalidConfigException(string issue) : base(issue) { }
    }
}
