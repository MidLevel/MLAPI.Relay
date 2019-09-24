
![](https://i.imgur.com/dJdKQYn.png)

_Please note that the MLAPI.Relay was an experimental implementation of a relay. This was written in the span of a few days. Might not be stable (probably is), but should be fairly performant. It has no fancy features to support client meshing, and direct connections for peer to peer_

## Introduction
_The documentation and the relay itself is currently WIP. This is subject to change._

The MLAPI.Relay is a relay designed for the UNET Transport to relay traffic between peers that are hidden behind a NAT. Relaying traffic can be expensive but will allow communication between peers, regardless of the host's NAT configuration. The MLAPI.Relay works just like the NetworkTransport. Despite the naming, the MLAPI.Relay does not have to be used in conjunction with the MLAPI library, but rather with any game built on the NetworkTransport (including the HLAPI). The MLAPI.Relay includes default configurations for use with the MLAPI, HLAPI as well as an empty template for custom setups. To use the relay, simply replace "NetworkTransport" with "RelayTransport" where the following NetworkTransport methods are used:
* Connect
* ConnectEndPoint
* ConnectWithSimulator
* AddHost
* AddHostWithSimulator
* AddWebsocketHost
* Disconnect
* Send
* QueueMessageForSending
* SendQueuedMessages
* Receive
* ReceiveFromHost

## Features
* Written in .NET Core for Cross platform
* Allows you to limit bandwidth (optional)

## Matchmaking
Unlike Unity's relay, the MLAPI.Relay does not require you to use any specific matchmaker. The MLAPI.Relay will pass the destination address when connecting rather than a relay-specific roomId. This allows you to run the MLAPI.Relay with any matchmaker (or without a matchmaker all together).

## Setup requirements
The MLAPI.Relay ***requires*** there be at least _one_ reliable channel type (regardless of what subtype of reliable channel that is).

## Configuration
The relay has a configuration file called *config.json* which consists of the three following parts:

### connectionConfig

This is the NetworkTransport connectionConfig. These options have to match up with your game's connectionConfig.

### globalConfig

This is the NetworkTransport GlobalConfig. This tells the NetworkTransport how it should work.

### relayConfig

Relay config contains many different fields:

* **maxConnections** is the maximum amount of connections the relay can support. 
* **port** is the the port on which the relay will operate
* **bufferSize** is the size of the buffer that will be allocated for messages (both inbound and outbound)
* **channels** is the list of channelTypes that should be used (in the order they are added). Note that some high level libraries have default channels that are added in addition to the user channels. Due to this tendency, the relay has templates for the default channel configurations of both the MLAPI and the HLAPI. When using these templates, the channels added are only the library channels; additional user channels must be added manually. **NOTE:** mismatched channel configurations *will* result in errors and will prevent clients and/or hosts from connecting to the relay!
* **bandwidthGracePeriodLength** is the length of the bandwidth grace period from the point when a client connects.
* **gracePeriodBandwidthLimit** is the amount of bytes per second that is allowed during the bandwidth grace period for each client. If this 0 or less, the traffic will not be limited during the grace period.
* **bandwidthLimit** is the amount of bytes per second that is allowed for each client after their respective the grace periods. If this is 0 or less, no limit will be set.

### Quickstart Guide

See below for a basic setup guide for building and deploying the MLAPI relay to a Amazon EC2 instance (Free tier - Windows server 2012)

* Clone the repository
* Open the MLAPI.Relay.sln file inside an IDE (Visual Studio 2019)
* Build the release, following the onscreen prompts for the required config
* Copy the contents of the build directory i.e. bin\release onto the Amazon EC2 instance
* Install dotnet-sdk-2.2.402-win-x64.exe on the server - https://dotnet.microsoft.com/download/dotnet-core/2.2
* Run "dotnet MLAPI.Relay.dll" in the Release\netcoreapp2.0 folder to start the server
* Ensure that firewall rules for inbound and outbound traffic have been setup to allow connections for your desired port (default 8888)
* Ensure the EC2 instance has the correct security settings to allow connections from any IPs you wish to connect to the server

Now you should be ready to connect to the server from unity. This can be achieved with MLAPI by creating an empty game object with the NetworkingManager script (adding the UNET transport) and configuring the settings to match the config.json settings used in the relay. The connect address should be your normal player hosted server address, the relay address should be the address to the relay (has to be set on both client and server). Then enable relaying and use NetworkingManager.Singleton.StartHost() on the Server and NetworkingManager.Singleton.StartClient() on the client and they should connect to the relay.
