using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Core
{
    public enum MessageType
    {
        StartServer,
        ConnectToServer,
        Data,
        ClientDisconnect
    }
}
