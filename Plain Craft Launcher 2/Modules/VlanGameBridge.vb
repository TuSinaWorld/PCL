Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Friend Class VlanGameBridge
    Implements IDisposable
    Private ReadOnly Cancellation As New CancellationTokenSource()
    Private Listener As TcpListener
    Private Ipv6 As TcpListener
    Private Connections As Integer
    Friend ReadOnly Property LocalPort As Integer
        Get
            Return If(Listener Is Nothing, 0, CType(Listener.LocalEndpoint, IPEndPoint).Port)
        End Get
    End Property
    Private Shared Function Proof(Invite As ModVlanLink.RoomInvite, Challenge As String) As String
        Dim Context = Invite.RoomId.ToString() & ":" & Invite.HostPeerId.ToString() & ":" & Invite.GamePort.ToString() & ":" & Challenge
        Using Hmac As New HMACSHA256(Encoding.UTF8.GetBytes(Invite.SessionKey))
            Return Convert.ToBase64String(Hmac.ComputeHash(Encoding.UTF8.GetBytes(Context)))
        End Using
    End Function
    Friend Sub Host(Invite As ModVlanLink.RoomInvite)
        Listener = New TcpListener(IPAddress.Parse(Invite.HostIp), Invite.ControlPort)
        Listener.Start(16)
        Invite.ControlPort = LocalPort
        AcceptLoop(Listener, Async Function(Client As TcpClient) As Task
                                 Using Client
                                     Client.ReceiveTimeout = 5000 : Client.SendTimeout = 5000
                                     Dim Challenge = ModVlanLink.ReadBoundedLine(Client.GetStream(), 128)
                                     If Not System.Text.RegularExpressions.Regex.IsMatch(Challenge, "\A[0-9a-f]{32}\z") Then Return
                                     Dim Data = Encoding.ASCII.GetBytes(Proof(Invite, Challenge) & vbLf)
                                     Await Client.GetStream().WriteAsync(Data, 0, Data.Length, Cancellation.Token)
                                 End Using
                             End Function)
    End Sub
    Friend Shared Sub VerifyHost(Invite As ModVlanLink.RoomInvite, Token As CancellationToken)
        Using Client As New TcpClient()
            Using Registration = Token.Register(Sub() Client.Close())
                Dim Connect = Client.ConnectAsync(Invite.HostIp, Invite.ControlPort)
                If Not Connect.Wait(8000, Token) Then Throw New IOException("未能验证游戏房主：房主已离开或网络不可达，请获取新的邀请码。")
                Connect.GetAwaiter().GetResult()
                Client.ReceiveTimeout = 5000 : Client.SendTimeout = 5000
                Dim Challenge = ModVlanLink.RandomToken(), Data = Encoding.ASCII.GetBytes(Challenge & vbLf)
                Client.GetStream().Write(Data, 0, Data.Length)
                Dim Reply = ModVlanLink.ReadBoundedLine(Client.GetStream(), 128)
                If Not ModVlanLink.FixedEquals(Encoding.ASCII.GetBytes(Reply), Encoding.ASCII.GetBytes(Proof(Invite, Challenge))) Then Throw New IOException("房间会话不匹配，邀请码可能已失效。已阻止连接其他游戏房间。")
            End Using
        End Using
    End Sub
    Friend Sub Guest(Invite As ModVlanLink.RoomInvite)
        Listener = New TcpListener(IPAddress.Loopback, 0)
        Listener.Start(32)
        StartProxy(Listener, Invite)
        Try
            Ipv6 = New TcpListener(IPAddress.IPv6Loopback, LocalPort)
            Ipv6.Server.DualMode = False
            Ipv6.Start(32)
            StartProxy(Ipv6, Invite)
        Catch
            Ipv6?.Stop() : Ipv6 = Nothing
        End Try
    End Sub
    Private Sub StartProxy(Source As TcpListener, Invite As ModVlanLink.RoomInvite)
        AcceptLoop(Source, Async Function(Local As TcpClient) As Task
                               Using Incoming As TcpClient = Local, Remote As New TcpClient()
                               Using StopLocal = Cancellation.Token.Register(Sub() Local.Close()), StopRemote = Cancellation.Token.Register(Sub() Remote.Close())
                                   Local.NoDelay = True : Remote.NoDelay = True
                                   Dim Connect = Remote.ConnectAsync(Invite.HostIp, Invite.GamePort)
                                   If Not Connect.Wait(8000, Cancellation.Token) Then Return
                                   Await Connect
                                   Dim Up = Local.GetStream().CopyToAsync(Remote.GetStream(), 81920, Cancellation.Token)
                                   Dim Down = Remote.GetStream().CopyToAsync(Local.GetStream(), 81920, Cancellation.Token)
                                   Await Task.WhenAny(Up, Down)
                                   Local.Close() : Remote.Close()
                                   Try
                                       Await Task.WhenAll(Up, Down)
                                   Catch
                                   End Try
                               End Using
                               End Using
                           End Function)
    End Sub
    Private Sub AcceptLoop(Source As TcpListener, Handler As Func(Of TcpClient, Task))
        Task.Run(Async Function()
                     While Not Cancellation.IsCancellationRequested
                         Dim Client As TcpClient
                         Try
                             Client = Await Source.AcceptTcpClientAsync()
                         Catch
                             Exit While
                         End Try
                         If Interlocked.Increment(Connections) > 32 Then
                             Interlocked.Decrement(Connections) : Client.Close() : Continue While
                         End If
                         Dim Owned = Client
                         Dim Work = Task.Run(Async Function()
                                                 Try
                                                     Await Handler(Owned)
                                                 Catch
                                                     Owned.Close()
                                                 Finally
                                                     Interlocked.Decrement(Connections)
                                                 End Try
                                             End Function)
                     End While
                 End Function)
    End Sub
    Friend Sub Broadcast()
        If Listener Is Nothing Then Return
        Try
            Using Sender As New UdpClient(AddressFamily.InterNetwork)
                Dim Data = Encoding.UTF8.GetBytes("[MOTD]PCL VLan 联机房间[/MOTD][AD]" & LocalPort & "[/AD]")
                Sender.Send(Data, Data.Length, New IPEndPoint(IPAddress.Loopback, 4445))
            End Using
        Catch
            'Discovery is best effort; the UI always provides a loopback address.
        End Try
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        Cancellation.Cancel()
        Listener?.Stop() : Ipv6?.Stop()
    End Sub
End Class
