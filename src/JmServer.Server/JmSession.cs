using JmServer.Domain;
using SuperSocket.Server;

namespace JmServer.Server;

public sealed class JmSession : AppSession
{
    public bool HandshakeComplete { get; set; }

    public DeviceIdentity? Identity { get; set; }
}
