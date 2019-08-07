using System;
using System.Collections.Generic;
using System.Net;

namespace MLAPI.Relay
{
    public sealed class Room
    {
        private static ulong _roomIdCounter = 0;
        private static readonly Queue<ulong> _releasedRoomIds = new Queue<ulong>();

        public bool IsValid { get; private set; } = true;

        public ulong GenerateRoomId()
        {
            if (_releasedRoomIds.Count > 0)
            {
                return _releasedRoomIds.Dequeue();
            }
            else
            {
                ulong newRoomId = _roomIdCounter;

                _roomIdCounter++;

                return newRoomId;
            }
        }

        public ulong ServerConnectionId { get => IsValid ? Server.ConnectionId : 0UL; }
        public ulong RoomId { get; private set; }
        public Client Server { get; private set; }

        //Key connectionId, value Client
        private readonly Dictionary<ulong, Client> connectedClients = new Dictionary<ulong, Client>();

        public bool HandleClientDisconnect(ulong connectionId, bool serverDisconnect = false)
        {
            ValidityCheck();

            if (Server.ConnectionId == connectionId)
            {
                // The server just disconnected, tell all the clients in the room
                foreach (Client client in connectedClients.Values)
                {
                    // Disconnects the client
                    Program.Transport.Disconnect(client.ConnectionId);
                }

                // Delete the room
                Program.Rooms.Remove(this);

                foreach (EndPoint key in Program.ServerAddressToRoom.Keys)
                {
                    if (Program.ServerAddressToRoom[key] == this)
                    {
                        // Remove ourself from the reverse lookup table
                        Program.ServerAddressToRoom.Remove(key);
                        break;
                    }
                }

                // Release roomId since the room should be considered useless
                _releasedRoomIds.Enqueue(RoomId);
                connectedClients.Clear();
                IsValid = false;

                return true;
            }
            else if (connectedClients.ContainsKey(connectionId))
            {
                // A client is attempting to disconnect
                Client client = connectedClients[connectionId];

                if (serverDisconnect)
                {
                    // The server requested this disconnect. Just throw them out.
                    Program.Transport.Disconnect(connectionId);
                }
                else
                {
                    // The client was the one that disconnected. Notify the server!

                    // Write the connectionId of the client that disconnected at the beginning of the buffer.
                    for (byte i = 0; i < sizeof(ulong); i++) Program.MESSAGE_BUFFER[i] = ((byte)(connectionId >> (i * 8)));

                    // Write the message type suffixed
                    Program.MESSAGE_BUFFER[sizeof(ulong)] = (byte)MessageType.ClientDisconnect;

                    // Send the message to the server
                    Program.Transport.Send(new ArraySegment<byte>(Program.MESSAGE_BUFFER, 0, sizeof(ulong) + sizeof(byte)), Program.DEFAULT_CHANNEL_BYTE, Server.ConnectionId);
                }

                // Remove the disconnected client from the list
                connectedClients.Remove(connectionId);

                return true;
            }

            return false;
        }

        public bool Send(ulong toConnectionId, ulong fromConnectionId, byte channelName, ArraySegment<byte> data)
        {
            if (Program.Config.BandwidthLimit > 0)
            {
                // Bandwidth control logic
                ulong taxedId = Server.ConnectionId == fromConnectionId ? toConnectionId : fromConnectionId;

                if (connectedClients.ContainsKey(taxedId))
                {
                    Client clientToBeTaxed = connectedClients[taxedId];

                    int bandwidthLimit = clientToBeTaxed.IsInBandwidthGracePeriod ? Program.Config.GracePeriodBandwidthLimit : Program.Config.BandwidthLimit;

                    if (clientToBeTaxed.OutgoingBytes / (DateTime.UtcNow - clientToBeTaxed.ConnectTime).TotalSeconds > bandwidthLimit)
                    {
                        // Client used too much bandwidth. Disconnect them
                        Console.WriteLine("[INFO] Bandwidth exceeded, client disconnected for overdue. The client is " + (clientToBeTaxed.IsInBandwidthGracePeriod ? "" : "not ") + "on grace period");
                        HandleClientDisconnect(taxedId, true);

                        return false;
                    }

                    // This includes relay overhead!!
                    // TODO: Strip overhead
                    clientToBeTaxed.OutgoingBytes += (ulong)data.Count;
                }
            }

            // Send the data
            Program.Transport.Send(data, channelName, toConnectionId);

            return true;
        }

        public void HandleClientConnect(Client client)
        {
            ValidityCheck();

            // Inform server of new connection

            // Write the messageType
            Program.MESSAGE_BUFFER[0] = (byte)MessageType.ConnectToServer;   // Event type to send to both server and client

            // Send event to client
            Program.Transport.Send(new ArraySegment<byte>(Program.MESSAGE_BUFFER, 0, 1), Program.DEFAULT_CHANNEL_BYTE, client.ConnectionId);

            // Write the messageType
            Program.MESSAGE_BUFFER[8] = (byte)MessageType.ConnectToServer;

            // Write the connectionId
            for (byte i = 0; i < sizeof(ulong); i++) Program.MESSAGE_BUFFER[i] = ((byte)(client.ConnectionId >> (i * 8)));

            // Send connect to client
            Program.Transport.Send(new ArraySegment<byte>(Program.MESSAGE_BUFFER, 0, 9), Program.DEFAULT_CHANNEL_BYTE, Server.ConnectionId);

            // Add client to active clients list
            connectedClients.Add(client.ConnectionId, client);
        }

        public bool HasPeer(ulong connectionId, out bool isServer)
        {
            ValidityCheck();

            if (isServer = Server.ConnectionId == connectionId)
            {
                return true;
            }

            return connectedClients.ContainsKey(connectionId);
        }

        private void ValidityCheck()
        {
            if (!IsValid) throw new ObjectDisposedException("Attempt to use an invalid room!");
        }

        public Room(Client server)
        {
            this.Server = server;
            RoomId = GenerateRoomId();
        }
    }
}
