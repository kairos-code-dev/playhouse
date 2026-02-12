#nullable enable

using System.Text;
using FluentAssertions;
using Net.Zmq;
using PlayHouse.Runtime.Proto;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.ServerMesh.PlaySocket;
using Xunit;

namespace PlayHouse.Unit;

public class ZmqSendRecvTest : IDisposable
{
    private readonly Context _context;

    public ZmqSendRecvTest()
    {
        _context = new Context();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public void BasicZmqSendRecv_ShouldWork()
    {
        // Given - Router socket (server)
        var serverSocket = new Socket(_context, SocketType.Router);
        var serverIdBytes = Encoding.UTF8.GetBytes("server1");
        serverSocket.SetOption(SocketOption.Routing_Id, serverIdBytes);
        serverSocket.SetOption(SocketOption.Router_Handover, 1);
        serverSocket.SetOption(SocketOption.Rcvtimeo, 5000); // 5초 타임아웃
        serverSocket.Bind("tcp://127.0.0.1:15300");

        // Given - Router socket (client)
        var clientSocket = new Socket(_context, SocketType.Router);
        var clientIdBytes = Encoding.UTF8.GetBytes("client1");
        clientSocket.SetOption(SocketOption.Routing_Id, clientIdBytes);
        clientSocket.SetOption(SocketOption.Router_Handover, 1);
        clientSocket.SetOption(SocketOption.Immediate, 0);
        clientSocket.Connect("tcp://127.0.0.1:15300");

        // Wait for connection
        Thread.Sleep(100);

        // When - Send message from client to server
        var targetId = Encoding.UTF8.GetBytes("server1");
        var message = Encoding.UTF8.GetBytes("Hello, Server!");

        clientSocket.Send(targetId, SendFlags.SendMore);
        clientSocket.Send(message, SendFlags.None);

        // Then - Server receives message
        var recvBuffer = new byte[1024];
        var senderLen = serverSocket.Recv(recvBuffer);
        senderLen.Should().BeGreaterThan(0, "should receive sender id");
        var senderId = Encoding.UTF8.GetString(recvBuffer, 0, senderLen);
        senderId.Should().Be("client1");

        var msgLen = serverSocket.Recv(recvBuffer);
        msgLen.Should().BeGreaterThan(0, "should receive message");
        var receivedMsg = Encoding.UTF8.GetString(recvBuffer, 0, msgLen);
        receivedMsg.Should().Be("Hello, Server!");

        // Cleanup
        clientSocket.Dispose();
        serverSocket.Dispose();
    }

    [Fact]
    public void SelfConnection_ShouldWork()
    {
        // Given - Two Router sockets with same ServerId (like PlayCommunicator)
        // Server socket for receive
        var serverSocket = new Socket(_context, SocketType.Router);
        var serverIdBytes = Encoding.UTF8.GetBytes("self1");
        serverSocket.SetOption(SocketOption.Routing_Id, serverIdBytes);
        serverSocket.SetOption(SocketOption.Router_Handover, 1);
        serverSocket.SetOption(SocketOption.Rcvtimeo, 5000);
        serverSocket.Bind("tcp://127.0.0.1:15301");

        // Client socket for send (same ServerId)
        var clientSocket = new Socket(_context, SocketType.Router);
        clientSocket.SetOption(SocketOption.Routing_Id, serverIdBytes); // Same ID
        clientSocket.SetOption(SocketOption.Router_Handover, 1);
        clientSocket.SetOption(SocketOption.Immediate, 0);
        clientSocket.Connect("tcp://127.0.0.1:15301");

        // Wait for connection
        Thread.Sleep(100);

        // When - Send message to self (same ServerId)
        var targetId = Encoding.UTF8.GetBytes("self1");
        var message = Encoding.UTF8.GetBytes("Hello, Self!");

        clientSocket.Send(targetId, SendFlags.SendMore);
        clientSocket.Send(message, SendFlags.None);

        // Then - Should receive own message
        var recvBuffer = new byte[1024];
        var senderLen = serverSocket.Recv(recvBuffer);
        senderLen.Should().BeGreaterThan(0, "should receive sender id");
        var senderId = Encoding.UTF8.GetString(recvBuffer, 0, senderLen);
        senderId.Should().Be("self1");

        var msgLen = serverSocket.Recv(recvBuffer);
        msgLen.Should().BeGreaterThan(0, "should receive message");
        var receivedMsg = Encoding.UTF8.GetString(recvBuffer, 0, msgLen);
        receivedMsg.Should().Be("Hello, Self!");

        clientSocket.Dispose();
        serverSocket.Dispose();
    }

    [Fact]
    public void ZmqPlaySocket_SendRecv_ShouldWork()
    {
        // Given - Two ZmqPlaySockets with timeout
        var config = new PlaySocketConfig { ReceiveTimeout = 5000 };

        var serverSocket = new ZmqPlaySocket("server1", _context, config);
        serverSocket.Bind("tcp://127.0.0.1:15302");

        var clientSocket = new ZmqPlaySocket("client1", _context, config);
        clientSocket.Connect("tcp://127.0.0.1:15302");

        // Wait for connection
        Thread.Sleep(100);

        // When - Send RoutePacket
        var header = new RouteHeader
        {
            MsgId = "TestMessage",
            MsgSeq = 1,
            ServiceId = 100,
            From = "client1",
            StageId = "12345"
        };
        var payload = Encoding.UTF8.GetBytes("Test payload data");
        var packet = RoutePacket.Of(header, payload);

        clientSocket.Send("server1", packet);

        // Then - Server receives packet
        var receivedPacket = serverSocket.Receive();
        receivedPacket.Should().NotBeNull();
        receivedPacket!.MsgId.Should().Be("TestMessage");
        receivedPacket.MsgSeq.Should().Be(1);
        receivedPacket.StageId.Should().Be("12345");
        receivedPacket.From.Should().Be("client1");
        Encoding.UTF8.GetString(receivedPacket.Payload.DataSpan).Should().Be("Test payload data");

        receivedPacket.Dispose();
        clientSocket.Dispose();
        serverSocket.Dispose();
    }

    [Fact]
    public void ZmqPlaySocket_SelfConnection_ShouldWork()
    {
        // Given - Two ZmqPlaySockets with same ServerId (like PlayCommunicator)
        var config = new PlaySocketConfig { ReceiveTimeout = 5000 };

        // Server socket for receive
        var serverSocket = new ZmqPlaySocket("self1", _context, config);
        serverSocket.Bind("tcp://127.0.0.1:15303");

        // Client socket for send (same ServerId)
        var clientSocket = new ZmqPlaySocket("self1", _context, config);
        clientSocket.Connect("tcp://127.0.0.1:15303");

        // Wait for connection
        Thread.Sleep(100);

        // When - Send to self
        var header = new RouteHeader
        {
            MsgId = "SelfMessage",
            MsgSeq = 42,
            ServiceId = 200,
            From = "self1",
            StageId = "99999"
        };
        var payload = Encoding.UTF8.GetBytes("Self-send test");
        var packet = RoutePacket.Of(header, payload);

        clientSocket.Send("self1", packet);

        // Then - Should receive own message
        var receivedPacket = serverSocket.Receive();
        receivedPacket.Should().NotBeNull();
        receivedPacket!.MsgId.Should().Be("SelfMessage");
        receivedPacket.MsgSeq.Should().Be(42);
        receivedPacket.StageId.Should().Be("99999");
        receivedPacket.From.Should().Be("self1");
        Encoding.UTF8.GetString(receivedPacket.Payload.DataSpan).Should().Be("Self-send test");

        receivedPacket.Dispose();
        clientSocket.Dispose();
        serverSocket.Dispose();
    }

    [Fact]
    public void ZmqPlaySocket_SelfConnection_Debug()
    {
        // Debug test - check if Send throws exception with Router_Mandatory
        // when both sockets have the same ServerId

        var config = new PlaySocketConfig { ReceiveTimeout = 2000 };

        var serverSocket = new ZmqPlaySocket("debug1", _context, config);
        serverSocket.Bind("tcp://127.0.0.1:15304");

        var clientSocket = new ZmqPlaySocket("debug1", _context, config);
        clientSocket.Connect("tcp://127.0.0.1:15304");

        Thread.Sleep(200); // Wait for connection establishment

        // Prepare packet
        var header = new RouteHeader
        {
            MsgId = "DebugMessage",
            MsgSeq = 1,
            ServiceId = 1
        };
        var payload = Encoding.UTF8.GetBytes("Debug test");
        var packet = RoutePacket.Of(header, payload);

        // Check what identity the clientSocket sees for the server
        // Send should succeed if connection is established
        Exception? sendException = null;
        try
        {
            clientSocket.Send("debug1", packet);
        }
        catch (Exception ex)
        {
            sendException = ex;
        }

        sendException.Should().BeNull("Send should not throw exception after connection is established");

        // Try to receive - this should either get the message or timeout
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var received = serverSocket.Receive();
        sw.Stop();

        // If received is null, check if it was a timeout (should be ~2000ms)
        if (received == null)
        {
            sw.ElapsedMilliseconds.Should().BeGreaterThan(1500, "Receive should have waited for timeout");
            sw.ElapsedMilliseconds.Should().BeLessThan(3000, "Receive should not take too long beyond timeout");
        }
        else
        {
            received.MsgId.Should().Be("DebugMessage");
            received.Dispose();
        }

        clientSocket.Dispose();
        serverSocket.Dispose();
    }
}
