using System;
using System.Collections.Generic;
using System.Text;
using Ruffles.Connections;

namespace Server.Core
{
    public sealed class Room
    {
        private static ushort roomIdCounter = 0;
        private static readonly Queue<ushort> releasedRoomIds = new Queue<ushort>();
        private bool isValid = true;
        public ushort GenerateRoomId()
        {
            if (releasedRoomIds.Count > 0)
                return releasedRoomIds.Dequeue();
            else
            {
                ushort newRoomId = roomIdCounter;
                roomIdCounter++;
                return newRoomId;
            }
        }

        public ulong ServerID { get => isValid ? server.connection == null ? server.connectionId : server.connection.Id : (ulong)0; }

        private readonly ushort roomId;
        private readonly Client server;
        private readonly List<Client> clients = new List<Client>();
        //Key conId, key Client
        private readonly Dictionary<ulong, Client> clientLookup = new Dictionary<ulong, Client>();

        public bool HandleClientDisconnect(ushort connectionId, bool serverDisconnect = false)
        {
            ValidityCheck();
            if (server.connectionId == connectionId)
            {
                // The fuckin server just disconnected, tell people
                foreach (Client client in clients) Program.unet.Disconnect(client.hostId, client.connectionId, out byte error);

                Program.rooms.Remove(this); // Delet this
                foreach (var key in Program.addressToRoom.Keys)
                {
                    if (Program.addressToRoom[key] == this)
                    {
                        // Remove ourself from the reverse lookup table
                        Program.addressToRoom.Remove(key);
                        break;
                    }
                }

                // Release room id since the room should be considered useless
                releasedRoomIds.Enqueue(roomId);
                clients.Clear();
                clientLookup.Clear();
                isValid = false;
                return true;
            }
            else
            {
                // A client is attempting to disconnect
                foreach (var client in clients)
                {
                    if (client.connectionId == connectionId)
                    {
                        if (serverDisconnect)
                        {
                            Program.unet.Disconnect(client.hostId, client.connectionId, out byte error);
                        }
                        else
                        {
                            // We have a disconnection! Notify server
                            Program.messageBuffer[2] = (byte)MessageType.ClientDisconnect;
                            Program.messageBuffer.ToBytes(client.connectionId);
                            Program.unet.Send(server.hostId, server.connectionId, Program.GetReliableChannel(), Program.messageBuffer, 3, out byte error);
                        }

                        // Remove the disconnected client from the list
                        clientLookup.Remove(client.connectionId);
                        clients.Remove(client);
                        return true;
                    }
                }
            }
            return false;
        }

        public bool HandleClientDisconnect(ulong connectionId, bool serverDisconnect = false)
        {
            ValidityCheck();
            if (server.connection.Id == connectionId)
            {
                // The fuckin server just disconnected, tell people
                foreach (Client client in clients) Program.ruffles.Disconnect(client.connection.Id, true);

                Program.rooms.Remove(this); // Delet this

                foreach (var key in Program.addressToRoom.Keys)
                {
                    if (Program.addressToRoom[key] == this)
                    {
                        // Remove ourself from the reverse lookup table
                        Program.addressToRoom.Remove(key);
                        break;
                    }
                }

                // Release room id since the room should be considered useless
                releasedRoomIds.Enqueue(roomId);
                clients.Clear();
                clientLookup.Clear();
                isValid = false;
                return true;
            }
            else
            {
                // A client is attempting to disconnect
                foreach (var client in clients)
                {
                    if (client.connectionId == connectionId)
                    {
                        if (serverDisconnect)
                        {
                            Program.ruffles.Disconnect(client.connection.Id, true);
                        }
                        else
                        {
                            // We have a disconnection! Notify server
                            Program.messageBuffer[8] = (byte)MessageType.ClientDisconnect;
                            Program.messageBuffer.ToBytes(client.connection.Id);
                            Program.ruffles.Send(new ArraySegment<byte>(Program.messageBuffer, 0, 9), server.connection.Id, Program.GetReliableChannel(), false);
                        }

                        // Remove the disconnected client from the list
                        clientLookup.Remove(client.connectionId);
                        clients.Remove(client);
                        return true;
                    }
                }
            }
            return false;
        }

        public bool Send(int hostId, int toConnectionId, int fromConnectionId, int channelId, byte[] data, int size, out byte error) =>
            Send(hostId, toConnectionId, fromConnectionId, channelId, data, 0, size, out error);

        public bool Send(int hostId, int toConnectionId, int fromConnectionId, int channelId, byte[] data, int offset, int size, out byte error)
        {
            if (RelayConfig.CurrentConfig.BandwidthLimit > 0)
            {
                // Bandwidth control logic
                int taxedId = server.connectionId == fromConnectionId ? toConnectionId : fromConnectionId;

                int bandwidthLimit = clientLookup[(ulong)taxedId].bandwidthGraceperiod ? RelayConfig.CurrentConfig.GracePeriodBandwidthLimit : RelayConfig.CurrentConfig.BandwidthLimit;

                // bytes / dTime / tps = bytes * tps / dTime
                if (((Program.unet.GetOutgoingUserBytesCountForConnection(hostId, taxedId, out error) * TimeSpan.TicksPerSecond) / (DateTime.UtcNow.Ticks - clientLookup[(ulong)taxedId].connectTick)) > bandwidthLimit)
                {
                    // Naughty user ;))
                    Console.WriteLine("[INFO] Bandwidth exceeded, client disconnected for overdue. The client is " + (clientLookup[(ulong)taxedId].bandwidthGraceperiod ? "" : "not ") + "on grace period");
                    HandleClientDisconnect((ushort)taxedId, true);
                    return Program.unet.Disconnect(hostId, taxedId, out error);
                }
            }
            return Program.unet.Send(hostId, toConnectionId, channelId, data, offset, size, out error);
        }

        public void Send(ulong connectionId, ulong toConnectionId, ulong fromConnectionId, byte channelId, byte[] data, int size) =>
            Send(toConnectionId, fromConnectionId, channelId, data, 0, size);

        public void Send(ulong toConnectionId, ulong fromConnectionId, byte channelId, byte[] data, int offset, int size)
        {
            if (RelayConfig.CurrentConfig.BandwidthLimit > 0)
            {
                // TODO: Add bandwidth checks
            }

            Program.ruffles.Send(new ArraySegment<byte>(data, offset, size), toConnectionId, channelId, false);
        }

        public void HandleClientConnect(Client client)
        {
            ValidityCheck();
            // Inform server of new connection
            Program.messageBuffer[0] = (byte)MessageType.ConnectToServer;   // Event type to send to both server and client

            // Send event to client
            if (RelayConfig.CurrentConfig.Transport == TransportType.UNET)
            {
                Program.unet.Send(server.hostId, client.connectionId, Program.GetReliableChannel(), Program.messageBuffer, 1, out byte error);

                Program.messageBuffer[2] = Program.messageBuffer[0];
                Program.messageBuffer.ToBytes(client.connectionId);          // Data for server
                // Send event + data to server

                Program.unet.Send(server.hostId, server.connectionId, Program.GetReliableChannel(), Program.messageBuffer, 3, out error);
            }
            else if (RelayConfig.CurrentConfig.Transport == TransportType.Ruffles)
            {
                Program.ruffles.Send(new ArraySegment<byte>(Program.messageBuffer, 0, 1), client.connection.Id, Program.GetReliableChannel(), false);

                Program.messageBuffer[8] = Program.messageBuffer[0];
                Program.messageBuffer.ToBytes(client.connection.Id);          // Data for server
                // Send event + data to server

                Program.ruffles.Send(new ArraySegment<byte>(Program.messageBuffer, 0, 9), client.connection.Id, Program.GetReliableChannel(), false);
            }

            // Add client to active clients list
            clients.Add(client);
            clientLookup.Add(client.connectionId, client);
        }

        public bool HasPeer(ushort connectionId, out bool isServer)
        {
            ValidityCheck();
            if (isServer = server.connectionId == connectionId)
            {
                return true;
            }

            foreach (Client cli in clients)
            {
                if (cli.connectionId == connectionId)
                {
                    return true;
                }
            }
            return false;
        }

        public bool HasPeer(Connection connection, out bool isServer)
        {
            ValidityCheck();
            if (isServer = server.connection == connection)
            {
                return true;
            }

            foreach (Client cli in clients)
            {
                if (cli.connection == connection)
                {
                    return true;
                }
            }
            return false;
        }

        public bool HasPeer(ulong connectionId, out bool isServer)
        {
            ValidityCheck();
            if (isServer = server.connection.Id == connectionId)
            {
                return true;
            }

            foreach (Client cli in clients)
            {
                if (cli.connection.Id == connectionId)
                {
                    return true;
                }
            }
            return false;
        }

        private void ValidityCheck()
        {
            if (!isValid) throw new ObjectDisposedException("Attempt to use an invalid room!");
        }

        public Room(Client server)
        {
            this.server = server;
            roomId = GenerateRoomId();
        }
    }
}
