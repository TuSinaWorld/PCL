Imports System
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

Module RemoteChecks
    Private ReadOnly OutputRoot As String = Path.Combine(Paths.AppDataThenName, "live")
    Private ReadOnly Report As New JObject From {{"remoteRelayPayloadVerified", False}, {"minecraftVerified", False}}
    Private Function Snapshot(Name As String) As JObject
        Dim Command = "[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false);$ErrorActionPreference='Stop';" &
            "$a=@(Get-NetAdapter -IncludeHidden|Where-Object {$_.Name -like '*VLan*'}|Select-Object Name,InterfaceDescription,InterfaceIndex,Status);" &
            "$r=@(Get-NetFirewallRule -PolicyStore ActiveStore|Where-Object {$_.DisplayName -like 'VLan Virtual LAN*'}|Select-Object Name,DisplayName,Enabled);" &
            "$i=@(Get-NetIPAddress -AddressFamily IPv4|Where-Object {$_.IPAddress -like '10.10.0.*'}|Select-Object InterfaceAlias,InterfaceIndex,IPAddress,PrefixLength);" &
            "$m=@(Get-NetIPInterface -AddressFamily IPv4|Where-Object {$_.InterfaceAlias -like '*VLan*'}|Select-Object InterfaceAlias,InterfaceIndex,InterfaceMetric,NlMtu,ConnectionState);" &
            "$t=@(Get-NetRoute -AddressFamily IPv4|Where-Object {$_.DestinationPrefix -eq '10.10.0.0/24' -or $_.InterfaceAlias -like '*VLan*'}|Select-Object InterfaceAlias,InterfaceIndex,DestinationPrefix,NextHop,RouteMetric);" &
            "[ordered]@{adapters=$a;rules=$r;ips=$i;interfaces=$m;routes=$t}|ConvertTo-Json -Depth 6"
        Dim Info As New Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command """ & Command & """") With {
            .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True, .StandardOutputEncoding = Encoding.UTF8}
        Using Child = Diagnostics.Process.Start(Info)
            Dim Text = Child.StandardOutput.ReadToEndAsync(), Errors = Child.StandardError.ReadToEndAsync()
            If Not Child.WaitForExit(25000) Then
                Child.Kill() : Throw New IOException("Read-only network snapshot timed out.")
            End If
            If Child.ExitCode <> 0 Then Throw New IOException("Unable to read network baseline; no automatic network cleanup will be attempted.")
            Dim Json = JObject.Parse(Text.Result)
            File.WriteAllText(Path.Combine(OutputRoot, Name & ".json"), Json.ToString())
            Return Json
        End Using
    End Function
    Private Sub Require(Value As Boolean, Message As String)
        If Not Value Then Throw New IOException(Message)
    End Sub
    Private Function Clean(Value As JObject) As Boolean
        Return {"adapters", "rules", "ips", "interfaces", "routes"}.All(Function(Key) CType(Value(Key), JArray).Count = 0)
    End Function
    Friend Sub Run(Host As String, Port As Integer, Optional RelayProbe As String = Nothing)
        Dim Password = Environment.GetEnvironmentVariable("PCL_VLAN_TEST_PASSWORD")
        Environment.SetEnvironmentVariable("PCL_VLAN_TEST_PASSWORD", Nothing)
        Dim Profile As New ModVlanLink.ServerProfile With {.Host = Host, .Port = Port, .Password = Password}
        ModVlanLink.ValidateServer(Profile)
        Host = Profile.Host
        Directory.CreateDirectory(OutputRoot)
        Dim Session As VlanSession = Nothing
        Try
            Require(Clean(Snapshot("before")), "Pre-existing VLan adapters/rules/subnet found. Close the existing VLan session first; no existing configuration was removed.")
            Console.WriteLine("REMOTE: requesting one UAC consent for real Wintun room lifecycle tests.")
            Session = New VlanSession()
            Session.Start(ModVlanLink.EnsureComponent(), CancellationToken.None)
            Session.Send("configure", New JObject From {{"host", Host}, {"port", Port.ToString()}, {"password", Password}, {"name", "PCLLive" & ModVlanLink.RandomToken()}})
            Session.WaitFor(Function(S) CBool(S("authenticated")), 25000, CancellationToken.None)
            Report("uacAndRemoteAuthentication") = True
            Console.WriteLine("PASS: elevated helper authenticated to the supplied v9 endpoint.")
            Dim Cases As New JArray(), LastRoom As UInteger, LastPassword As String = ""
            For Each Mode In {"protected-default", "unprotected-default", "protected-tcp-relay"}
                Report("currentMode") = Mode
                Dim RoomPassword = If(Mode = "unprotected-default", "", ModVlanLink.RandomToken())
                Session.Send("create", New JObject From {{"name", "PCLTest" & ModVlanLink.RandomToken()}, {"roomPassword", RoomPassword}, {"compatibility", If(Mode.EndsWith("tcp-relay"), "true", "false")}})
                Dim Ready = Session.WaitFor(Function(S) CBool(S("ready")), 55000, CancellationToken.None)
                Require(ModVlanLink.IsVirtualIp(CStr(Ready("virtualIp"))), "Unexpected virtual subnet.")
                Dim Live = Snapshot(Mode & "-ready")
                Require(CType(Live("adapters"), JArray).Count > 0 AndAlso CType(Live("rules"), JArray).Count > 0 AndAlso CType(Live("ips"), JArray).Count > 0, "Room ready without expected real network artifacts.")
                Dim Result As New JObject From {{"mode", Mode}, {"roomId", Ready("roomId")}, {"virtualIp", Ready("virtualIp")}, {"tcp", Ready("tcp")}, {"udp", Ready("udp")}, {"actualAdapterReady", True}}
                LocalGameCheck(Profile, RoomPassword, Ready)
                Result("invitationAndLocalGameBridge") = True
                If Not String.IsNullOrEmpty(RelayProbe) Then
                    If Mode = "protected-default" Then
                        RelayGameCheck(RelayProbe, Profile, RoomPassword, Ready, "default-relay")
                        Result("remoteRawUdpAndKcpWintunEcho") = True
                        Report("defaultTransportPayloadVerified") = True
                    ElseIf Mode = "unprotected-default" Then
                        RunProbe(RelayProbe, Profile, RoomPassword, Ready, 0, "join-only")
                        Result("headlessNoPasswordJoin") = True
                    ElseIf Mode = "protected-tcp-relay" Then
                        RunProbe(RelayProbe, Profile, "Wrong" & ModVlanLink.RandomToken(), Ready, 0, "expect-wrong-password")
                        Result("wrongRoomPasswordRejected") = True
                        RelayGameCheck(RelayProbe, Profile, RoomPassword, Ready, "relay")
                        Result("remoteTcpRelayAndWintunUdpEcho") = True
                        Report("remoteRelayPayloadVerified") = True
                    End If
                    Session.WaitFor(Function(S) CType(S("peers"), JArray).Count = 0 AndAlso CBool(S("ready")), 10000, CancellationToken.None)
                End If
                LastRoom = CUInt(Ready("roomId")) : LastPassword = RoomPassword
                Session.Send("leave")
                Session.WaitFor(Function(S) Not CBool(S("ready")) AndAlso CUInt(S("roomId")) = 0 AndAlso CStr(S("phase")) = "idle", 65000, CancellationToken.None)
                Require(Clean(Snapshot(Mode & "-after-leave")), "Test-owned network artifacts remain after leave.")
                Result("cleanupVerified") = True : Cases.Add(Result)
                Report("rooms") = Cases
                Console.WriteLine("PASS: " & Mode & " real adapter/IP/routes/firewall, local proxy and cleanup.")
            Next
            'Lose the launcher control channel with a REAL active virtual NIC; never kill the driver worker.
            Report("currentMode") = "control-channel-loss"
            LastPassword = ModVlanLink.RandomToken()
            Session.Send("create", New JObject From {{"name", "PCLDrop" & ModVlanLink.RandomToken()}, {"roomPassword", LastPassword}, {"compatibility", "true"}})
            Dim DropReady = Session.WaitFor(Function(S) CBool(S("ready")), 55000, CancellationToken.None)
            LastRoom = CUInt(DropReady("roomId"))
            Snapshot("control-loss-ready")
            Dim PipeField = GetType(VlanSession).GetField("Pipe", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)
            CType(PipeField.GetValue(Session), IO.Pipes.NamedPipeServerStream).Dispose()
            Dim CleanupClock = Diagnostics.Stopwatch.StartNew()
            While Not Session.HasExited AndAlso CleanupClock.ElapsedMilliseconds < 65000
                Thread.Sleep(100)
            End While
            Require(Session.HasExited AndAlso Clean(Snapshot("control-loss-after")), "Real virtual NIC cleanup did not finish after control channel loss.")
            Report("realTunControlLossCleanup") = True
            Console.WriteLine("PASS: real active Wintun helper cleaned up after lost control pipe; no force-kill.")
            Session.Dispose()
            'Authentication-only replacement checks the deleted room without another UAC or NIC.
            Session = New VlanSession()
            Session.Start(ModVlanLink.EnsureComponent(), CancellationToken.None, False)
            Session.Send("configure", New JObject From {{"host", Host}, {"port", Port.ToString()}, {"password", Password}, {"name", "PCLVerify" & ModVlanLink.RandomToken()}})
            Session.WaitFor(Function(S) CBool(S("authenticated")), 25000, CancellationToken.None)
            'This was our randomly password-protected room; it has no other invited members.
            Session.Send("join", New JObject From {{"roomId", LastRoom.ToString()}, {"roomPassword", LastPassword}})
            Dim Clock = Diagnostics.Stopwatch.StartNew()
            While Session.Failure Is Nothing AndAlso Clock.ElapsedMilliseconds < 20000
                Thread.Sleep(30)
            End While
            Require(Session.Failure = "ROOM_NOT_FOUND", "Deleted test room did not return ROOM_NOT_FOUND.")
            Report("deletedRoomRejected") = True
            Console.WriteLine("PASS: joining the deleted owned room reports ROOM_NOT_FOUND; no replacement room created.")
        Catch Ex As Exception
            Report("failure") = Ex.Message.Replace(Password, "[redacted]")
            Throw New IOException("Remote test stopped: " & CStr(Report("failure")))
        Finally
            If Session IsNot Nothing Then
                Session.Dispose()
                Dim Clock = Diagnostics.Stopwatch.StartNew()
                While Not Session.HasExited AndAlso Clock.ElapsedMilliseconds < 60000
                    Thread.Sleep(100)
                End While
                Report("helperExited") = Session.HasExited
            End If
            Try
                Report("finalCleanupVerified") = Clean(Snapshot("after"))
            Catch
                Report("finalCleanupVerified") = False
            End Try
            File.WriteAllText(Path.Combine(OutputRoot, "result.json"), Report.ToString())
            Console.WriteLine("REMOTE_REPORT=" & Path.Combine(OutputRoot, "result.json"))
        End Try
        Require(CBool(Report("helperExited")) AndAlso CBool(Report("finalCleanupVerified")), "Final cleanup has not completed; do not force-kill the driver worker.")
    End Sub
    Private Sub RunProbe(Exe As String, Profile As ModVlanLink.ServerProfile, RoomPassword As String, Ready As JObject, UdpPort As Integer, Mode As String)
        Dim Arguments = Profile.Host & " " & Profile.Port & " " & CUInt(Ready("roomId")) & " " & CUInt(Ready("peerId")) & " " & CStr(Ready("virtualIp")) & " " & UdpPort & " " & Mode
        Dim Info As New Diagnostics.ProcessStartInfo(Path.GetFullPath(Exe), Arguments) With {.UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True}
        Info.EnvironmentVariables("PCL_VLAN_TEST_PASSWORD") = Profile.Password
        Info.EnvironmentVariables("PCL_VLAN_TEST_ROOM_PASSWORD") = RoomPassword
        Using Child = Diagnostics.Process.Start(Info)
            Dim Text = Child.StandardOutput.ReadToEndAsync(), Errors = Child.StandardError.ReadToEndAsync()
            If Not Child.WaitForExit(40000) Then
                Child.Kill() : Throw New IOException("Headless test peer timed out; it has no virtual adapter.")
            End If
            Dim Lines = Text.Result.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
            Require(Lines.All(Function(Line) System.Text.RegularExpressions.Regex.IsMatch(Line, "\A(?:PASS|FAIL|RESULT)\|[A-Za-z0-9_-]+\z")), "Unexpected headless probe diagnostic format.")
            File.WriteAllLines(Path.Combine(OutputRoot, "probe-" & Mode & ".log"), Lines)
            For Each Line In Lines
                Console.WriteLine(Line)
            Next
            Require(Child.ExitCode = 0 AndAlso Lines.Contains("RESULT|PASS"), "Headless protocol probe did not pass (" & Mode & ").")
        End Using
    End Sub
    Private Sub RelayGameCheck(Exe As String, Profile As ModVlanLink.ServerProfile, RoomPassword As String, Ready As JObject, Mode As String)
        Using Echo As New UdpClient(New IPEndPoint(IPAddress.Parse(CStr(Ready("virtualIp"))), 0))
            Echo.Client.ReceiveTimeout = 35000
            Dim EchoTask = Task.Run(Sub()
                                        Try
                                            Dim Sender As New IPEndPoint(IPAddress.Any, 0)
                                            Dim Payload = Echo.Receive(Sender)
                                            Echo.Send(Payload, Payload.Length, Sender)
                                        Catch
                                            Throw New IOException("Real virtual-interface UDP echo did not complete.")
                                        End Try
                                    End Sub)
            Try
                RunProbe(Exe, Profile, RoomPassword, Ready, CType(Echo.Client.LocalEndPoint, IPEndPoint).Port, Mode)
                Require(EchoTask.Wait(5000), "Virtual-interface echo worker did not complete.")
            Finally
                Echo.Close()
            End Try
        End Using
    End Sub
    Private Sub LocalGameCheck(Profile As ModVlanLink.ServerProfile, RoomPassword As String, Ready As JObject)
        Dim Echo As New TcpListener(IPAddress.Any, 0)
        Try
            Echo.Start()
            Dim EchoTask = Task.Run(Sub()
                                        Using Client = Echo.AcceptTcpClient()
                                            Client.ReceiveTimeout = 7000
                                            Dim Buffer(4095) As Byte
                                            Dim N = Client.GetStream().Read(Buffer, 0, Buffer.Length)
                                            Client.GetStream().Write(Buffer, 0, N)
                                        End Using
                                    End Sub)
            Dim Invite As New ModVlanLink.RoomInvite With {.Server = Profile, .RoomId = CUInt(Ready("roomId")), .HostPeerId = CUInt(Ready("peerId")), .HostIp = CStr(Ready("virtualIp")), .GamePort = CType(Echo.LocalEndpoint, IPEndPoint).Port, .RoomPassword = RoomPassword, .SessionKey = ModVlanLink.RandomToken()}
            Using Control As New VlanGameBridge(), Proxy As New VlanGameBridge()
                Control.Host(Invite)
                Dim Decoded = ModVlanLink.DecodeInvite(ModVlanLink.EncodeInvite(Invite))
                Require(Decoded.RoomPassword = RoomPassword AndAlso Decoded.Server.Password = Profile.Password, "Invitation round trip mismatch.")
                VlanGameBridge.VerifyHost(Decoded, CancellationToken.None)
                Proxy.Guest(Decoded)
                Using Client As New TcpClient()
                    Client.Connect(IPAddress.Loopback, Proxy.LocalPort)
                    Client.ReceiveTimeout = 7000
                    Dim Bytes = Encoding.ASCII.GetBytes("VLan-live-local-bridge-" & ModVlanLink.RandomToken()), Received(Bytes.Length - 1) As Byte
                    Client.GetStream().Write(Bytes, 0, Bytes.Length)
                    Dim Offset = 0
                    While Offset < Received.Length
                        Dim N = Client.GetStream().Read(Received, Offset, Received.Length - Offset)
                        If N = 0 Then Exit While
                        Offset += N
                    End While
                    Require(Bytes.SequenceEqual(Received), "Local game proxy payload mismatch.")
                End Using
            End Using
            Require(EchoTask.Wait(8000), "Local echo task did not finish.")
        Finally
            Echo.Stop()
        End Try
    End Sub
End Module
