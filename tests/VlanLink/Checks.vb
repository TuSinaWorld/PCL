Imports System
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

Public Module Paths
    Public ReadOnly AppDataThenName As String = Path.Combine(Path.GetDirectoryName(GetType(VlanGameBridge).Assembly.Location), "s")
End Module
Module Checks
    Private Count As Integer
    Private Sub Check(Condition As Boolean, Description As String)
        If Not Condition Then Throw New Exception("FAIL: " & Description)
        Count += 1
    End Sub
    Private Sub Reject(Work As Action, Description As String)
        Try
            Work()
        Catch Ex As InvalidDataException
            Check(True, Description) : Return
        End Try
        Throw New Exception("FAIL: accepted " & Description)
    End Sub
    Private Function Fixture() As ModVlanLink.RoomInvite
        Return New ModVlanLink.RoomInvite With {.Server = New ModVlanLink.ServerProfile With {.Host = "localhost", .Port = 11510, .Password = "SyntheticFixtureOnly123"}, .RoomId = 1001, .RoomPassword = "", .HostPeerId = 23, .HostIp = "10.10.0.2", .GamePort = 25565, .ControlPort = 45678, .SessionKey = New String("a"c, 32)}
    End Function
    Sub Main()
        Try
            Dim Arguments = Environment.GetCommandLineArgs()
            If Arguments.Contains("--remote") Then
                Dim Index = Array.IndexOf(Arguments, "--remote")
                RemoteChecks.Run(Arguments(Index + 1), Integer.Parse(Arguments(Index + 2)), If(Arguments.Length > Index + 3, Arguments(Index + 3), Nothing))
                Return
            End If
            If Environment.GetCommandLineArgs().Contains("--uac-probe") Then
                Using Session As New VlanSession()
                    Console.WriteLine("Requesting Windows administrator consent; no server login or virtual NIC will be created.")
                    Session.Start(ModVlanLink.EnsureComponent(), CancellationToken.None)
                    Session.Send("status")
                    Session.WaitFor(Function(S) S("phase") IsNot Nothing, 5000, CancellationToken.None)
                    Console.WriteLine("PASS: runas helper connected, verified parent/client pipe IDs and returned machine status.")
                End Using
                Return
            End If
            RunAll()
        Catch Ex As Exception
            Console.WriteLine(Ex.GetType().FullName & ": " & Ex.Message)
            Console.WriteLine(Ex.StackTrace)
            Environment.Exit(1)
        End Try
    End Sub
    Private Sub RunAll()
        Dim Invite = Fixture()
        For Each Password In {"", "Abc123", New String("z"c, 63)}
            Invite.RoomPassword = Password
            Dim Code = ModVlanLink.EncodeInvite(Invite), Decoded = ModVlanLink.DecodeInvite("邀请【" & Code & "】加入")
            Check(Decoded.RoomPassword = Password, "optional room password round trip")
            Check(Decoded.Server.Password = Invite.Server.Password AndAlso Decoded.HostPeerId = Invite.HostPeerId AndAlso Decoded.GamePort = Invite.GamePort, "complete credentials round trip")
            Check(Not Code.Contains(Invite.Server.Password) AndAlso Not Code.Contains("localhost"), "cipher is not plain text")
            Check(Code <> ModVlanLink.EncodeInvite(Invite), "each invitation gets fresh keys and IV")
            Dim Last = Code.Length \ 2
            Dim Changed = Code.Substring(0, Last) & If(Code(Last) = "A"c, "B", "A") & Code.Substring(Last + 1)
            Reject(Sub() ModVlanLink.DecodeInvite(Changed), "corruption")
        Next
        Reject(Sub() ModVlanLink.DecodeInvite("PCLF1:AAAA"), "legacy EasyTier invitation")
        Reject(Sub() ModVlanLink.DecodeInvite("PCLV1:" & New String("a"c, 17000)), "oversize invitation")
        For Each Ip In {"127.0.0.1", "10.10.0.1", "10.10.0.255", "10.10.1.2", "::1", "8.8.8.8"}
            Invite = Fixture() : Invite.HostIp = Ip
            Reject(Sub() ModVlanLink.EncodeInvite(Invite), "non-member game IP")
        Next
        For Each Port In {0, 1023, 65536}
            Invite = Fixture() : Invite.GamePort = Port
            Reject(Sub() ModVlanLink.EncodeInvite(Invite), "invalid game port")
        Next
        For Each Password In {"a", "bad password", "underscore_", New String("a"c, 64)}
            Invite = Fixture() : Invite.RoomPassword = Password
            Reject(Sub() ModVlanLink.EncodeInvite(Invite), "invalid room password")
        Next
        Invite = Fixture() : Invite.Server.NoRoomPassword = True
        ModVlanLink.SaveProfile(Invite.Server)
        Dim Stored = ModVlanLink.LoadProfile()
        Check(Stored.Password = Invite.Server.Password AndAlso Stored.NoRoomPassword, "DPAPI profile round trip")
        Check(Not Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(ModVlanLink.Root, "server.dpapi"))).Contains(Invite.Server.Password), "DPAPI file hides credentials")
        For Each Code In {"ROOM_NOT_FOUND", "ROOM_EXPIRED", "ROOM_FULL", "ROOM_PASSWORD_REQUIRED", "ROOM_PASSWORD_INVALID", "AUTHENTICATION_FAILED", "SERVER_UNREACHABLE", "ADAPTER_FAILED"}
            Check(Not String.IsNullOrEmpty(ModVlanLink.ErrorText(Code)), "actionable error mapping")
        Next
        Dim Exe = ModVlanLink.EnsureComponent()
        Check(File.Exists(Exe) AndAlso File.Exists(Path.Combine(Path.GetDirectoryName(Exe), "wintun.dll")), "embedded component extracted")
        Check(ModVlanLink.EnsureComponent() = Exe, "verified installation reused")
        File.WriteAllText(Exe, "corrupted synthetic installation")
        Dim Repaired = ModVlanLink.EnsureComponent()
        Check(New FileInfo(Repaired).Length > 100000 AndAlso Directory.GetDirectories(Path.GetDirectoryName(Path.GetDirectoryName(Exe)), "*.invalid-*").Length > 0, "corrupted component repaired with previous directory preserved")
        If Environment.GetCommandLineArgs().Contains("--offline") Then
            Console.WriteLine("PASS: " & Count & " offline VLan codec, DPAPI, package checks; no network or virtual NIC used.")
            Return
        End If
        'Exercise the real proxy and bounded game stream transfer on loopback, without a NIC.
        Dim Echo As New TcpListener(IPAddress.Loopback, 0)
        Try
            Echo.Start()
            Dim Port = CType(Echo.LocalEndpoint, IPEndPoint).Port
            Dim EchoTask = Task.Run(Sub()
                                        Using Client = Echo.AcceptTcpClient()
                                            Dim Buffer(4095) As Byte
                                            Dim N = Client.GetStream().Read(Buffer, 0, Buffer.Length)
                                            Client.GetStream().Write(Buffer, 0, N)
                                        End Using
                                    End Sub)
            Invite = Fixture() : Invite.HostIp = "127.0.0.1" : Invite.GamePort = Port
            Using Proxy As New VlanGameBridge()
                Proxy.Guest(Invite)
                Using Client As New TcpClient()
                    Client.Connect(IPAddress.Loopback, Proxy.LocalPort)
                    Client.ReceiveTimeout = 5000
                    Dim Payload = Encoding.UTF8.GetBytes("Minecraft TCP proxy fixture"), Received(Payload.Length - 1) As Byte
                    Client.GetStream().Write(Payload, 0, Payload.Length)
                    Dim Offset = 0
                    While Offset < Received.Length
                        Dim N = Client.GetStream().Read(Received, Offset, Received.Length - Offset)
                        If N = 0 Then Exit While
                        Offset += N
                    End While
                    Check(Payload.SequenceEqual(Received), "real bidirectional game TCP proxy")
                End Using
            End Using
            EchoTask.GetAwaiter().GetResult()
        Finally
            Echo.Stop()
        End Try
        Invite = Fixture() : Invite.HostIp = "127.0.0.1" : Invite.ControlPort = 0
        Using Host As New VlanGameBridge()
            Host.Host(Invite)
            VlanGameBridge.VerifyHost(Invite, CancellationToken.None)
            Check(True, "real game host session challenge")
            Dim Wrong = Fixture() : Wrong.HostIp = "127.0.0.1" : Wrong.ControlPort = Invite.ControlPort
            Wrong.SessionKey = New String("b"c, 32)
            Try
                VlanGameBridge.VerifyHost(Wrong, CancellationToken.None)
                Throw New Exception("FAIL: wrong host session accepted")
            Catch Ex As IOException
                Check(True, "wrong room session rejected")
            End Try
            Dim OriginalPort = Invite.ControlPort
            Host.Dispose()
            Using Resumed As New VlanGameBridge()
                Resumed.Host(Invite)
                Check(Invite.ControlPort = OriginalPort, "resumed host keeps the original invitation control endpoint")
                VlanGameBridge.VerifyHost(Invite, CancellationToken.None)
                Check(True, "original session verifies after host bridge recreation")
            End Using
        End Using
        Using Session As New VlanSession()
            Session.Start(Repaired, CancellationToken.None, False)
            Session.Send("join", New JObject From {{"roomId", "1001"}})
            Dim Clock = Diagnostics.Stopwatch.StartNew()
            While Session.Failure Is Nothing AndAlso Clock.ElapsedMilliseconds < 5000
                Thread.Sleep(20)
            End While
            Check(Session.Failure = "NOT_AUTHENTICATED", "real machine CLI refuses unauthenticated room commands")
            Session.Dispose()
            Check(Session.HasExited, "real machine CLI shutdown cleans process")
        End Using
        Using Session As New VlanSession()
            Session.Start(Repaired, CancellationToken.None, False)
            Dim Field = GetType(VlanSession).GetField("Pipe", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)
            CType(Field.GetValue(Session), IO.Pipes.NamedPipeServerStream).Dispose()
            Dim Clock = Diagnostics.Stopwatch.StartNew()
            While Not Session.HasExited AndAlso Clock.ElapsedMilliseconds < 5000
                Thread.Sleep(20)
            End While
            Check(Session.HasExited, "real helper exits after lost control pipe without shutdown request")
        End Using
        Dim DropServer As New TcpListener(IPAddress.Loopback, 0)
        Try
            DropServer.Start()
            Dim Dropped = Task.Run(Sub()
                                       Using Client = DropServer.AcceptTcpClient()
                                           Client.ReceiveTimeout = 5000
                                           Client.GetStream().ReadByte()
                                       End Using
                                   End Sub)
            Using Session As New VlanSession(), Interrupted As New ManualResetEventSlim()
                AddHandler Session.Interrupted, Sub() Interrupted.Set()
                Session.Start(Repaired, CancellationToken.None, False)
                Session.Send("configure", New JObject From {{"host", "127.0.0.1"}, {"port", CType(DropServer.LocalEndpoint, IPEndPoint).Port.ToString()}, {"password", "SyntheticServerOnly123"}, {"name", "PCLReconnectFixture"}})
                Check(Interrupted.Wait(8000), "real helper immediately reports interrupted connection for game bridge shutdown")
            End Using
            Dropped.GetAwaiter().GetResult()
        Finally
            DropServer.Stop()
        End Try
        Console.WriteLine("PASS: " & Count & " VLan codec, DPAPI, package and real local TCP checks; no server credentials or virtual NIC used.")
    End Sub
End Module
