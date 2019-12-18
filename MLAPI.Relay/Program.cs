using MLAPI.Relay.Transports;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using UnetServerDll;

namespace MLAPI.Relay
{
    public static class Program
    {
        public static Transport Transport;
        public static readonly List<Room> Rooms = new List<Room>();
        public static readonly Dictionary<EndPoint, Room> ServerAddressToRoom = new Dictionary<EndPoint, Room>();
        public static byte DEFAULT_CHANNEL_BYTE = 0;

        public static byte[] MESSAGE_BUFFER;

        private static readonly byte[] ADDRESS_BYTE_ARRAY = new byte[16];

        public static RelayConfig Config = null;

        private static void Main(string[] args)
        {
            string configPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config.json");

            if (!File.Exists(configPath))
            {
                Console.WriteLine("================================");
                Console.WriteLine("There is no config. Please select the transport you would like:");
                Console.WriteLine("[R]uffles");
                Console.WriteLine("[U]net");

                ConsoleKey key = ConsoleKey.Escape;

                do
                {
                    key = Console.ReadKey(true).Key;
                } 
                while (key != ConsoleKey.R && key != ConsoleKey.U);

                if (key == ConsoleKey.U)
                {
                    Transport = new UnetTransport();

                    Console.WriteLine("================================");
                    Console.WriteLine("Please select a template for the UNET config file:");
                    Console.WriteLine("[M]LAPI - Copies the same UNET settings as the MLAPI uses by default");
                    Console.WriteLine("[H]LAPI - Copies the same UNET settings as the HLAPI uses by default");
                    Console.WriteLine("[E]mpty - Default UNET settings");

                    key = ConsoleKey.Escape;

                    do
                    {
                        key = Console.ReadKey(true).Key;
                    }
                    while (key != ConsoleKey.M && key != ConsoleKey.H && key != ConsoleKey.E);

                    if (key == ConsoleKey.M)
                    {
                        UnetTransport.UnetConfig config = (UnetTransport.UnetConfig)Transport.GetConfig();

                        config.ConnectionConfig.AddChannel(QosType.ReliableFragmentedSequenced);
                        config.ConnectionConfig.AddChannel(QosType.Reliable);
                        config.ConnectionConfig.AddChannel(QosType.UnreliableSequenced);
                        config.ConnectionConfig.AddChannel(QosType.ReliableSequenced);
                        config.ConnectionConfig.AddChannel(QosType.ReliableSequenced);
                        config.ConnectionConfig.AddChannel(QosType.UnreliableSequenced);
                        config.ConnectionConfig.AddChannel(QosType.Unreliable);

                        Config = new RelayConfig()
                        {
                            Transport = TransportType.UNET,
                            AllowTemporaryAlloc = true,
                            MaxTemporaryAlloc = 1024 * 16,
                            BandwidthGracePeriodLength = 60 * 2,
                            BandwidthLimit = -1,
                            EnableRuntimeMetaLogging = true,
                            GracePeriodBandwidthLimit = -1,
                            TransportConfig = config,
                            BufferSize = 1024 * 8,
                            ListenPort = 8888
                        };
                    }
                    else if (key == ConsoleKey.H)
                    {
                        UnetTransport.UnetConfig config = (UnetTransport.UnetConfig)Transport.GetConfig();

                        config.ConnectionConfig.AddChannel(QosType.ReliableSequenced);
                        config.ConnectionConfig.AddChannel(QosType.Unreliable);

                        Config = new RelayConfig()
                        {
                            Transport = TransportType.UNET,
                            BufferSize = 1024 * 8,
                            AllowTemporaryAlloc = true,
                            BandwidthGracePeriodLength = 60 * 2,
                            BandwidthLimit = -1,
                            EnableRuntimeMetaLogging = true,
                            GracePeriodBandwidthLimit = -1,
                            ListenPort = 8888,
                            MaxTemporaryAlloc = 1024 * 16,
                            TransportConfig = config
                        };
                    }
                    else if (key == ConsoleKey.E)
                    {
                        UnetTransport.UnetConfig config = (UnetTransport.UnetConfig)Transport.GetConfig();

                        Config = new RelayConfig()
                        {
                            Transport = TransportType.UNET,
                            BufferSize = 1024 * 8,
                            AllowTemporaryAlloc = true,
                            BandwidthGracePeriodLength = 60 * 2,
                            BandwidthLimit = -1,
                            EnableRuntimeMetaLogging = true,
                            GracePeriodBandwidthLimit = -1,
                            ListenPort = 8888,
                            MaxTemporaryAlloc = 1024 * 16,
                            TransportConfig = config
                        };
                    }

                }
                else if (key == ConsoleKey.R)
                {
                    Transport = new RufflesTransport();

                    RufflesTransport.RufflesConfig config = (RufflesTransport.RufflesConfig)Transport.GetConfig();

                    Config = new RelayConfig()
                    {
                        Transport = TransportType.Ruffles,
                        AllowTemporaryAlloc = true,
                        BandwidthGracePeriodLength = 60 * 2,
                        BandwidthLimit = -1,
                        BufferSize = 1024 * 8,
                        EnableRuntimeMetaLogging = true,
                        GracePeriodBandwidthLimit = -1,
                        ListenPort = 8888,
                        MaxTemporaryAlloc = 1024 * 16,
                        TransportConfig = config
                    };
                }

                if (Config != null)
                {
                    object config = Transport.BeforeSerializeConfig(Config);

                    string serializedJson = JsonConvert.SerializeObject(config, Formatting.Indented);

                    File.WriteAllText(configPath, Transport.ProcessSerializedJson(serializedJson));
                }
                else
                {
                    // TODO: Something went wrong. No config??
                }
            }
            else
            {
                try
                {
                    Config = JsonConvert.DeserializeObject<RelayConfig>(File.ReadAllText(configPath));

                    switch (Config.Transport)
                    {
                        case TransportType.Ruffles:
                            Transport = new RufflesTransport();
                            break;
                        case TransportType.UNET:
                            Transport = new UnetTransport();
                            break;
                    }

                    // Post deserialization job
                    Config = (RelayConfig)Transport.AfterDeserializedConfig(Config);
                }
                catch (Exception e)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] Error parsing config file: " + e);
                    Console.Read();
                    Environment.Exit(1);
                    return;
                }
            }

            Program.MESSAGE_BUFFER = new byte[Config.BufferSize];

            try
            {
                Console.WriteLine("[INFO] Starting server...");
                Transport.Start(Config.TransportConfig);
                Console.WriteLine("[INFO] Server started!");
            }
            catch (DllNotFoundException e)
            {
                Console.WriteLine("[FATAL] Could not locate one or more shared libraries! Message: \n" + e);
            }
            catch (Exception e)
            {
                Console.WriteLine("[FATAL] An unexpected error occurred! Message: \n" + e);
            }

            Stopwatch watch = new Stopwatch();

            while (true)
            {
                try
                {
                    watch.Restart();

                    RunLoop();

                    int timeBetweenTicks = (int)((1f / Config.TicksPerSecond) * 1000f);
                    int timeToSleep = timeBetweenTicks - (int)watch.ElapsedMilliseconds;

                    if (timeToSleep > 0)
                    {
                        Thread.Sleep(timeToSleep);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("[ERROR] Exception during loop: " + e);
                }
            }
        }

        private static void RunLoop()
        {
            NetEventType eventType;
            do
            {
                eventType = Transport.Poll(out ulong connectionId, out byte channelId, out ArraySegment<byte> payload);

                switch (@eventType)
                {
                    case NetEventType.Data:
                        {
                            // Last byte is the messageType
                            MessageType messageType = (MessageType)payload.Array[payload.Offset + payload.Count - 1];

                            switch (messageType)
                            {
                                case MessageType.StartServer:
                                    {
                                        // Check if they are already connected or perhaps are already hosting, if so return
                                        if (HasPeer(connectionId) || ServerAddressToRoom.ContainsKey(Transport.GetEndPoint(connectionId)))
                                        {
                                            return;
                                        }

                                        Client client;

                                        // Create the room
                                        Room room = new Room(client = new Client()
                                        {
                                            ConnectionId = connectionId,
                                            IsServer = true,
                                            OutgoingBytes = 0,
                                            ConnectTime = DateTime.UtcNow
                                        });

                                        // Add the room
                                        Rooms.Add(room);

                                        // Resolve the endpoint
                                        IPEndPoint endpoint = Transport.GetEndPoint(connectionId);

                                        if (endpoint != null)
                                        {
                                            // Make address IPv6
                                            endpoint = new IPEndPoint(endpoint.Address.MapToIPv6(), endpoint.Port);

                                            if (Config.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Server started from " + endpoint);

                                            ServerAddressToRoom.Add(endpoint, room);
                                        }

                                        byte[] ipv6AddressBuffer;
                                        IPAddress ipAddress = endpoint.Address;

                                        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                                        {
                                            ipv6AddressBuffer = ipAddress.GetAddressBytes();
                                        }
                                        else if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                        {
                                            byte[] ipv4Address = ipAddress.GetAddressBytes();
                                            ipv6AddressBuffer = new byte[16] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, ipv4Address[0], ipv4Address[1], ipv4Address[2], ipv4Address[3] };
                                        }
                                        else
                                        {
                                            // TODO: Throw wrong type
                                            ipv6AddressBuffer = null;
                                        }

                                        // TODO: Throw if address is not 16 bytes. It should always be
                                        // Write the address
                                        for (int i = 0; i < ipv6AddressBuffer.Length; i++) MESSAGE_BUFFER[i] = ipv6AddressBuffer[i];

                                        // Write the port
                                        for (byte i = 0; i < sizeof(ushort); i++) MESSAGE_BUFFER[16 + i] = ((byte)(endpoint.Port >> (i * 8)));

                                        // Write the message type
                                        MESSAGE_BUFFER[18] = (byte)MessageType.AddressReport;

                                        // Send connect to client
                                        Transport.Send(new ArraySegment<byte>(MESSAGE_BUFFER, 0, 19), DEFAULT_CHANNEL_BYTE, connectionId);
                                    }
                                    break;
                                case MessageType.ConnectToServer:
                                    {
                                        // Check if they are already connected or perhaps are already hosting, if so return
                                        if (HasPeer(connectionId) || ServerAddressToRoom.ContainsKey(Transport.GetEndPoint(connectionId)))
                                        {
                                            return;
                                        }

                                        if (payload.Count < 1 + 2 + 16)
                                        {
                                            // Invalid size
                                            return;
                                        }
                                        // 1 for messageType, 2 for port, 16 for address
                                        int addressOffset = payload.Count - (1 + 2 + 16);
                                        // 1 for messageType, 2 for port
                                        int portOffset = payload.Count - (1 + 2);

                                        // Copy the address
                                        for (int i = 0; i < 16; i++) ADDRESS_BYTE_ARRAY[i] = payload.Array[payload.Offset + addressOffset + i];

                                        // Read port
                                        ushort port = (ushort)(((ushort)payload.Array[payload.Offset + portOffset]) |
                                                                ((ushort)payload.Array[payload.Offset + portOffset + 1] << 8));


                                        // Parse address
                                        IPAddress address = new IPAddress(ADDRESS_BYTE_ARRAY);

                                        // Create endpoint
                                        IPEndPoint endpoint = new IPEndPoint(address, port);

                                        if (Config.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Connection requested to address " + endpoint);

                                        if (ServerAddressToRoom.ContainsKey(endpoint))
                                        {
                                            if (Config.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Connection approved");

                                            // Get the room they want to join
                                            Room room = ServerAddressToRoom[endpoint];

                                            // Create a client for them
                                            Client client = new Client()
                                            {
                                                ConnectionId = connectionId,
                                                IsServer = false,
                                                ConnectTime = DateTime.UtcNow,
                                                OutgoingBytes = 0
                                            };

                                            // Handle the connect
                                            room.HandleClientConnect(client);
                                        }
                                    }
                                    break;
                                case MessageType.Data:
                                    {
                                        foreach (Room room in Rooms)
                                        {
                                            if (room.HasPeer(connectionId, out bool isServer))
                                            {
                                                // Found a matching client in room
                                                if (isServer)
                                                {
                                                    // The server is sending data

                                                    ulong destination = (((ulong)payload.Array[payload.Offset + payload.Count - 9]) |
                                                                        ((ulong)payload.Array[payload.Offset + payload.Count - 8] << 8) |
                                                                        ((ulong)payload.Array[payload.Offset + payload.Count - 7] << 16) |
                                                                        ((ulong)payload.Array[payload.Offset + payload.Count - 6] << 24) |
                                                                        ((ulong)payload.Array[payload.Offset + payload.Count - 5] << 32) |
                                                                        ((ulong)payload.Array[payload.Offset + payload.Count - 4] << 40) |
                                                                        ((ulong)payload.Array[payload.Offset + payload.Count - 3] << 48) |
                                                                        ((ulong)payload.Array[payload.Offset + payload.Count - 2] << 56));


                                                    // Safety check. Make sure who they want to send to ACTUALLY belongs to their room
                                                    if (room.HasPeer(destination, out isServer) && !isServer)
                                                    {
                                                        // We strip the connectionId. Since this is from the server, a source is not needed.
                                                        payload.Array[payload.Offset + payload.Count - 9] = (byte)MessageType.Data;          // [data, data, data, dest1, dest2, dest3, dest4, dest5, dest6, dest7, dest8, mtype_r, none, none, none] => [{data, data, data, mtype_s}, dest2, dest3, dest4, dest5, dest6, dest7, dest8, mtype_r, none, none, none]

                                                        // Send a shrunk version of the array.
                                                        room.Send(destination, connectionId, channelId, new ArraySegment<byte>(payload.Array, payload.Offset, payload.Count - 8));
                                                    }
                                                }
                                                else
                                                {
                                                    // A client is sending data

                                                    int writeOffset = 0;
                                                    byte[] buffer = null;

                                                    if (payload.Array.Length < payload.Count + sizeof(ulong))
                                                    {
                                                        if (Program.MESSAGE_BUFFER.Length < payload.Count + sizeof(ulong))
                                                        {
                                                            // Message buffer cannot fit the payload either.

                                                            // Check if we can alloc temporary memory
                                                            if (Config.AllowTemporaryAlloc && Config.MaxTemporaryAlloc >= payload.Count + sizeof(ulong))
                                                            {
                                                                // We are allowed to alloc this amount!

                                                                // Alloc a large enough buffer
                                                                writeOffset = 0;
                                                                buffer = new byte[payload.Count + sizeof(ulong)];
                                                            }
                                                        }
                                                        else
                                                        {
                                                            // Message buffer can store it!
                                                            writeOffset = 0;
                                                            buffer = Program.MESSAGE_BUFFER;
                                                        }

                                                        if (buffer != null)
                                                        {
                                                            // We have something alloced. Lets copy the current payload!
                                                            Buffer.BlockCopy(payload.Array, payload.Offset, buffer, writeOffset, payload.Count);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        buffer = payload.Array;
                                                        writeOffset = payload.Offset;
                                                    }

                                                    if (buffer != null)
                                                    {
                                                        // Write the connectionId at the end of the recieved message (because optimization)
                                                        // We use -1 because we actually want to overwrite the last byte in the message, since that is the old messageType location. It will be moved forward now.
                                                        for (byte i = 0; i < sizeof(ulong); i++) buffer[writeOffset + payload.Count - 1 + i] = ((byte)(connectionId >> (i * 8)));

                                                        // Write the message type at the new end
                                                        buffer[writeOffset + payload.Count + 7] = (byte)MessageType.Data;

                                                        // Send the expanded array
                                                        room.Send(room.ServerConnectionId, connectionId, channelId, new ArraySegment<byte>(payload.Array, payload.Offset, payload.Count + sizeof(ulong)));
                                                    }
                                                    else
                                                    {
                                                        // Message is too large. Drop it
                                                    }
                                                }

                                                // Exit loop when room is found
                                                break;
                                            }
                                        }
                                    }
                                    break;
                                case MessageType.ClientDisconnect:
                                    {
                                        ulong clientConnectionId = (((ulong)payload.Array[payload.Offset]) |
                                                                    ((ulong)payload.Array[payload.Offset + 1] << 8) |
                                                                    ((ulong)payload.Array[payload.Offset + 2] << 16) |
                                                                    ((ulong)payload.Array[payload.Offset + 3] << 24) |
                                                                    ((ulong)payload.Array[payload.Offset + 4] << 32) |
                                                                    ((ulong)payload.Array[payload.Offset + 5] << 40) |
                                                                    ((ulong)payload.Array[payload.Offset + 6] << 48) |
                                                                    ((ulong)payload.Array[payload.Offset + 7] << 56));

                                        if (Config.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Client disconnect request");

                                        foreach (Room room in Rooms)
                                        {
                                            if (room.ServerConnectionId == connectionId && room.HandleClientDisconnect(clientConnectionId, true))
                                            {
                                                // Only disconnect one. A peer can only be in 1 room
                                                break;
                                            }
                                        }
                                    }
                                    break;
                            }
                        }
                        break;
                    case NetEventType.Disconnect:
                        {
                            if (Config.EnableRuntimeMetaLogging) Console.WriteLine("[INFO] Peer disconnected");

                            foreach (Room room in Rooms)
                            {
                                if (room.HandleClientDisconnect(connectionId))
                                {
                                    // Each connection can only have 1 room
                                    break;
                                }
                            }
                        }
                        break;
                }
            }
            while (eventType != NetEventType.Nothing);
        }

        private static bool HasPeer(ulong connectionId)
        {
            foreach (Room room in Rooms)
            {
                if (room.HasPeer(connectionId, out _))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class InvalidConfigException : Exception
    {
        public InvalidConfigException() { }
        public InvalidConfigException(string issue) : base(issue) { }
    }
}
