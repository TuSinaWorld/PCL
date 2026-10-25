Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports Newtonsoft.Json.Linq

Public Module LocalLinkChecks
    Private Count As Integer
    Private Sub Check(Condition As Boolean, Name As String)
        If Not Condition Then Throw New Exception("FAIL: " & Name)
        Count += 1
    End Sub
    Private Sub Reject(Action As Action, Name As String)
        Dim Rejected As Boolean = False
        Try
            Action()
        Catch
            Rejected = True
        End Try
        Check(Rejected, Name)
    End Sub
    Private Function TokenFor(Json As String) As String
        Return "PCLF1:" & Convert.ToBase64String(Encoding.UTF8.GetBytes(Json)).TrimEnd("="c).Replace("+", "-").Replace("/", "_")
    End Function
    Private Sub MakeZip(Path As String, Names As String(), Optional Symlink As Boolean = False)
        Using Archive = ZipFile.Open(Path, ZipArchiveMode.Create)
            For Each Name In Names
                Dim Entry = Archive.CreateEntry(Name)
                If Symlink Then Entry.ExternalAttributes = -1610612736 ' Unix symlink mode.
                Using Writer As New StreamWriter(Entry.Open())
                    Writer.Write("fixture-runtime")
                End Using
            Next
        End Using
    End Sub
    Public Function Run(Root As String, Fixture As String) As String
        Directory.CreateDirectory(Root)
        Count = 0
        Dim Peers = New String() {"tcp://127.0.0.1:11010", "udp://[::1]:11010"}
        Dim Invite = ModLocalLink.CreateInvite(25565, Peers)
        Dim Encoded = ModLocalLink.EncodeInvite(Invite)
        Dim Decoded = ModLocalLink.DecodeInvite(Encoded)
        Check(Decoded.Port = 25565 AndAlso Decoded.Network = Invite.Network AndAlso Decoded.Secret = Invite.Secret AndAlso Decoded.Peers.SequenceEqual(Peers), "invite roundtrip carries actual peers")
        Check(Invite.Network.Length = 37 AndAlso Invite.Secret.Length = 32, "128 bit network and secret")
        Check(ModLocalLink.CreateInvite(25565, Peers).Secret <> Invite.Secret, "fresh cryptographic secret")
        Check(ModLocalLink.DecodeInvite("在 PCL 启动器中输入邀请码【" & Encoded & "】，即可加入联机房间！").Secret = Invite.Secret, "wrapped invite")
        Check(ModLocalLink.GetInviteFingerprint(Encoded) = ModLocalLink.GetInviteFingerprint("在 PCL 启动器中输入邀请码【" & Encoded & "】，即可加入联机房间！"), "share text and token share canonical non-secret fingerprint")
        Check(ModLocalLink.DecodeInvite(Encoded).Network = Invite.Network, "case preserved")
        Reject(Sub() ModLocalLink.DecodeInvite("P1234-ABCDE-ABCDE-0205E"), "legacy code rejected with migration message")
        Reject(Sub() ModLocalLink.DecodeInvite(New String("x"c, 8193)), "invite size cap")
        Reject(Sub() ModLocalLink.DecodeInvite(Encoded & "="), "non base64url suffix rejected")
        Dim Json = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Encoded.Substring(6).Replace("-", "+").Replace("_", "/").PadRight(((Encoded.Length - 6 + 3) \ 4) * 4, "="c))))
        Json("v") = 2
        Reject(Sub() ModLocalLink.DecodeInvite(TokenFor(Json.ToString())), "unsupported version")
        Json("v") = 1
        Json("port") = 65536
        Reject(Sub() ModLocalLink.DecodeInvite(TokenFor(Json.ToString())), "port range")
        Reject(Sub() ModLocalLink.DecodeInvite(TokenFor("{""v"":1,""v"":1}")), "duplicate JSON properties")
        Reject(Sub() ModLocalLink.ParseCustomPeers("tcp://host:11010 -w attacker"), "argument injection rejected")
        Reject(Sub() ModLocalLink.ParseCustomPeers("file://host:11010"), "unsupported scheme rejected")
        Reject(Sub() ModLocalLink.ParseCustomPeers("tcp://user:pass@host:11010"), "userinfo rejected")
        Reject(Sub() ModLocalLink.ParseCustomPeers("tcp://host:11010/path"), "peer path rejected")
        Reject(Sub() ModLocalLink.NormalizePeers(Enumerable.Repeat("tcp://host:11010", 9)), "peer input count bounded")
        Check(ModLocalLink.ParseCustomPeers("tcp://host:11010，udp://host:11010").Count = 2, "custom peer separator")
        Dim Nodes = "{""data"":{""items"":[{""is_active"":true,""is_approved"":true,""tags"":[""MC 中继""],""address"":""tcp://relay:11010"",""allow_relay"":true,""usage_percentage"":10},{""is_active"":true,""is_approved"":true,""tags"":[""MC""],""address"":""udp://discover:11010"",""allow_relay"":false},{""is_active"":false,""is_approved"":true,""tags"":[""MC""],""address"":""tcp://inactive:11010""},{""is_active"":true,""is_approved"":true,""tags"":[""MC""],""address"":""file://bad:11010""}]}}"
        Dim Selected = ModLocalLink.SelectPublicPeers(Nodes)
        Check(Selected.Count = 2 AndAlso Selected(0) = "udp://discover:11010", "public discovery plus relay with spaced tags")
        Reject(Sub() ModLocalLink.SelectPublicPeers("{""data"":{""items"":[]}}"), "empty node response")
        Check(ModLocalLink.ParseCliPeers("[{""hostname"":""Server-x"",""ipv4"":""10.114.114.114"",""cost"":""p2p"",""lat_ms"":""1.20"",""nat_type"":""FullCone""}]").Count = 1, "official CLI flat JSON array")
        Reject(Sub() ModLocalLink.ParseCliPeers(""), "empty CLI output")
        Reject(Sub() ModLocalLink.ParseCliPeers("{}"), "CLI wrapper rejected")
        Reject(Sub() ModLocalLink.ParseCliPeers("[{}]"), "malformed CLI row")
        Dim SelfOnly = ModLocalLink.ParseCliPeers("[{""hostname"":""Server-local"",""ipv4"":""10.114.114.114"",""cost"":""Local"",""lat_ms"":""0"",""nat_type"":""Unknown""}]")
        Check(ModLocalLink.HasLocalPeer(SelfOnly), "single host CLI snapshot identifies its own core")
        Check(Not ModLocalLink.HasLocalPeer(New JArray()) AndAlso Not ModLocalLink.HasLocalPeer(Nothing), "no local CLI snapshot is not ready")
        Check(ModLocalLink.CanFinishRoom(True, ModLocalLink.HasLocalPeer(SelfOnly), 0), "host can publish invitation with zero players")
        Check(Not ModLocalLink.CanFinishRoom(True, False, 10), "host requires its own valid CLI snapshot")
        Check(Not ModLocalLink.CanFinishRoom(False, True, 0), "joining client still waits for its host")
        Check(ModLocalLink.CanFinishRoom(False, True, 1.2), "client with a reachable host becomes ready")
        Check(Not ModLocalLink.CanFinishRoom(False, True, Double.NaN) AndAlso Not ModLocalLink.CanFinishRoom(False, True, Double.PositiveInfinity), "invalid host latency cannot complete a join")
        For Each Architecture In {"x86_64", "i686", "arm64"}
            Dim Package = ModLocalLink.PackageFor(Architecture)
            Check(Package.Sha256.Length = 64 AndAlso Package.Url.EndsWith("easytier-windows-" & Architecture & "-v2.6.4.zip"), "pinned " & Architecture & " package")
        Next
        Reject(Sub() ModLocalLink.PackageFor("unknown"), "unknown architecture")
        Dim GoodZip = Path.Combine(Root, "good.zip")
        MakeZip(GoodZip, {"runtime/easytier-core.exe", "runtime/easytier-cli.exe", "runtime/Packet.dll", "runtime/wintun.dll", "runtime/WinDivert64.sys", "runtime/easytier-web.exe"})
        Dim Extracted = Path.Combine(Root, "extracted")
        ModLocalLink.ExtractRuntime(GoodZip, Extracted, CancellationToken.None)
        Check(Directory.GetFiles(Extracted).Length = 5 AndAlso Not File.Exists(Path.Combine(Extracted, "easytier-web.exe")), "all native dependencies extracted, web executable excluded")
        Dim BadZip = Path.Combine(Root, "traversal.zip")
        MakeZip(BadZip, {"../escape.exe"})
        Reject(Sub() ModLocalLink.ExtractRuntime(BadZip, Path.Combine(Root, "bad"), CancellationToken.None), "ZIP traversal rejected")
        BadZip = Path.Combine(Root, "absolute.zip")
        MakeZip(BadZip, {"C:/escape.exe"})
        Reject(Sub() ModLocalLink.ExtractRuntime(BadZip, Path.Combine(Root, "bad2"), CancellationToken.None), "ZIP drive path rejected")
        BadZip = Path.Combine(Root, "duplicate.zip")
        MakeZip(BadZip, {"a/Packet.dll", "b/Packet.dll"})
        Reject(Sub() ModLocalLink.ExtractRuntime(BadZip, Path.Combine(Root, "bad3"), CancellationToken.None), "ZIP flattened duplicates rejected")
        BadZip = Path.Combine(Root, "symlink.zip")
        MakeZip(BadZip, {"a/Packet.dll"}, True)
        Reject(Sub() ModLocalLink.ExtractRuntime(BadZip, Path.Combine(Root, "bad4"), CancellationToken.None), "ZIP symlink rejected")
        Dim PackageFixture = New ModLocalLink.ComponentPackage With {.Architecture = "x86_64", .Sha256 = ModLocalLink.FileSha256(GoodZip)}
        Dim Installed = ModLocalLink.InstallPackage(GoodZip, Path.Combine(Root, "install"), PackageFixture, CancellationToken.None)
        Check(ModLocalLink.IsInstalled(Installed, PackageFixture), "atomic installed manifest verified")
        File.AppendAllText(Path.Combine(Installed, "easytier-core.exe"), "tampered")
        Check(Not ModLocalLink.IsInstalled(Installed, PackageFixture), "installed runtime tampering detected")
        ModLocalLink.InstallPackage(GoodZip, Path.Combine(Root, "install"), PackageFixture, CancellationToken.None)
        Check(ModLocalLink.IsInstalled(Installed, PackageFixture) AndAlso Directory.GetDirectories(Path.Combine(Root, "install"), ".previous-*").Length = 1, "replace runtime preserves previous install")
        Reject(Sub() ModLocalLink.InstallPackage(GoodZip, Path.Combine(Root, "wrong-hash"), ModLocalLink.PackageFor("x86_64"), CancellationToken.None), "wrong package SHA rejected before install")
        Using Cancel As New CancellationTokenSource()
            Cancel.Cancel()
            Reject(Sub() ModLocalLink.ExtractRuntime(GoodZip, Path.Combine(Root, "canceled"), Cancel.Token), "ZIP extraction cancellation")
        End Using
        Check(ModLocalLink.RunOwnedCommand(Fixture, "ok", CancellationToken.None, 2000).Trim() = "fixture-ok", "real child stdout capture")
        Reject(Sub() ModLocalLink.RunOwnedCommand(Fixture, "fail", CancellationToken.None, 2000), "real child nonzero exit")
        Dim PidPath = Path.Combine(Root, "timeout.pid")
        Reject(Sub() ModLocalLink.RunOwnedCommand(Fixture, "sleep " & PidPath, CancellationToken.None, 200), "real child timeout")
        Check(ChildStopped(PidPath), "timed-out owned child stopped")
        PidPath = Path.Combine(Root, "cancel.pid")
        Using Cancel As New CancellationTokenSource()
            Cancel.CancelAfter(200)
            Reject(Sub() ModLocalLink.RunOwnedCommand(Fixture, "sleep " & PidPath, Cancel.Token, 4000), "real child cancellation")
        End Using
        Check(ChildStopped(PidPath), "canceled owned child stopped")
        Reject(Sub() ModLocalLink.RunOwnedCommand(Fixture, "overflow", CancellationToken.None, 2000), "child output bounded")
        Return Count.ToString() & " LocalLink behavior checks passed."
    End Function
    Public Function InstallOfficial(ArchivePath As String, Root As String, Optional Architecture As String = "x86_64") As String
        Return ModLocalLink.InstallPackage(ArchivePath, Root, ModLocalLink.PackageFor(Architecture), CancellationToken.None)
    End Function
    Public Function VerifyOfficial(DirectoryPath As String, Architecture As String) As Boolean
        Return ModLocalLink.IsInstalled(DirectoryPath, ModLocalLink.PackageFor(Architecture))
    End Function
    Public Function OfficialPackageHash(Architecture As String) As String
        Return ModLocalLink.PackageFor(Architecture).Sha256
    End Function
    Public Function QueryOfficial(DirectoryPath As String, RpcPort As Integer) As String
        Dim Result = ModLocalLink.RunCli(Path.Combine(DirectoryPath, "easytier-cli.exe"), RpcPort, CancellationToken.None)
        ModLocalLink.ParseCliPeers(Result)
        Return Result
    End Function
    Public Function HostIsReady(Json As String) As Boolean
        Return ModLocalLink.CanFinishRoom(True, ModLocalLink.HasLocalPeer(ModLocalLink.ParseCliPeers(Json)), 0)
    End Function
    Public Function SavePublicFixture(Destination As String) As String
        Dim Json = ModLocalLink.FetchPublicNodes(CancellationToken.None)
        Dim Selected = ModLocalLink.SelectPublicPeers(Json)
        File.WriteAllText(Destination, Json, New UTF8Encoding(False))
        Return New JArray(Selected).ToString()
    End Function
    Private Function ChildStopped(PidPath As String) As Boolean
        If Not File.Exists(PidPath) Then Return False
        Try
            Using Child = Process.GetProcessById(Integer.Parse(File.ReadAllText(PidPath)))
                Return Child.HasExited
            End Using
        Catch ex As ArgumentException
            Return True
        End Try
    End Function
End Module
