using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
class EchoFixture {
    static void Main(string[] args) {
        int port = int.Parse(args[0]);
        new Thread(() => { using (var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port))) { while (true) { IPEndPoint from = null; var bytes = udp.Receive(ref from); udp.Send(bytes, bytes.Length, from); } } }) { IsBackground = true }.Start();
        var tcp = new TcpListener(IPAddress.Loopback, port); tcp.Start();
        while (true) { using (var client = tcp.AcceptTcpClient()) using (var stream = client.GetStream()) using (var payload = new System.IO.MemoryStream()) { byte[] buffer = new byte[4096]; while (payload.Length < 4096) { int n = stream.Read(buffer, 0, buffer.Length); if (n == 0) break; payload.Write(buffer, 0, n); if (buffer[n - 1] == 10) break; } var bytes = payload.ToArray(); stream.Write(bytes, 0, bytes.Length); } }
    }
}
