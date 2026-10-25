Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net.Http
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

' Standalone EasyTier integration. No PCL service, remote commands or deployment secrets.
Friend Module ModLocalLink
    Friend Const ComponentVersion As String = "2.6.4"
    Friend Const InvitePrefix As String = "PCLF1:"
    Friend Const NodesUrl As String = "https://uptime.easytier.cn/api/nodes?page=1&per_page=1000"
    Friend Const MaximumInviteLength As Integer = 8192

    Friend Class LinkInvite
        Friend Network As String
        Friend Secret As String
        Friend Port As Integer
        Friend Peers As List(Of String)
    End Class

    Friend Class ComponentPackage
        Friend Architecture As String
        Friend Sha256 As String
        Friend ReadOnly Property Url As String
            Get
                Return "https://github.com/EasyTier/EasyTier/releases/download/v" & ComponentVersion & "/easytier-windows-" & Architecture & "-v" & ComponentVersion & ".zip"
            End Get
        End Property
    End Class

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativeSystemInfo
        Public Architecture As UShort
        Public Reserved As UShort
        Public PageSize As UInteger
        Public MinimumAddress As IntPtr
        Public MaximumAddress As IntPtr
        Public ActiveMask As UIntPtr
        Public ProcessorCount As UInteger
        Public ProcessorType As UInteger
        Public AllocationGranularity As UInteger
        Public ProcessorLevel As UShort
        Public ProcessorRevision As UShort
    End Structure
    <DllImport("kernel32.dll")>
    Private Sub GetNativeSystemInfo(ByRef Value As NativeSystemInfo)
    End Sub

    Friend Function NativeArchitecture() As String
        Dim Info As New NativeSystemInfo
        GetNativeSystemInfo(Info)
        Select Case Info.Architecture
            Case 0 : Return "i686"
            Case 9 : Return "x86_64"
            Case 12 : Return "arm64"
            Case Else : Throw New PlatformNotSupportedException("当前系统架构没有受支持的 EasyTier 组件。")
        End Select
    End Function

    Friend Function PackageFor(Architecture As String) As ComponentPackage
        Dim Hash As String
        Select Case Architecture
            Case "x86_64" : Hash = "27af91e270e554709b048bd32327fefd2dfce5062ae1e8701af7550c6f525f84"
            Case "i686" : Hash = "bf557daeccc5525d95b8a230c339d75554fb52d82d1e050e9a5202c92c02e09e"
            Case "arm64" : Hash = "37023f8a3451c9234b17ee2089a03dc344ce90d803b5b359cb6c46682b0549b4"
            Case Else : Throw New PlatformNotSupportedException("不支持的 EasyTier 架构。")
        End Select
        Return New ComponentPackage With {.Architecture = Architecture, .Sha256 = Hash}
    End Function

    Private Function RandomToken() As String
        Dim Bytes(15) As Byte
        Using Random = RandomNumberGenerator.Create()
            Random.GetBytes(Bytes)
        End Using
        Return BitConverter.ToString(Bytes).Replace("-", "").ToLowerInvariant()
    End Function

    Friend Function CreateInvite(Port As Integer, Peers As IEnumerable(Of String)) As LinkInvite
        Dim Result As New LinkInvite With {.Network = "PCLF-" & RandomToken(), .Secret = RandomToken(), .Port = Port, .Peers = NormalizePeers(Peers)}
        ValidateInvite(Result)
        Return Result
    End Function

    Friend Function NormalizePeers(Values As IEnumerable(Of String)) As List(Of String)
        If Values Is Nothing Then Throw New InvalidDataException("联机节点为空。")
        Dim Result As New List(Of String)
        Dim InputCount As Integer = 0
        For Each Raw In Values
            InputCount += 1
            If InputCount > 8 Then Throw New InvalidDataException("联机节点最多为 8 个。")
            If Raw Is Nothing Then Throw New InvalidDataException("联机节点格式无效。")
            Dim Value = Raw.Trim()
            If Value.Length = 0 OrElse Value.Length > 512 OrElse Value.Any(Function(C) Char.IsControl(C) OrElse Char.IsWhiteSpace(C) OrElse C = """"c OrElse C = "'"c OrElse C = "\"c) Then Throw New InvalidDataException("联机节点地址包含不支持的字符。")
            Dim Address As Uri = Nothing
            If Not Uri.TryCreate(Value, UriKind.Absolute, Address) OrElse String.IsNullOrEmpty(Address.Host) OrElse Address.Port <= 0 OrElse Address.Port > 65535 OrElse
               Address.UserInfo <> "" OrElse Address.Query <> "" OrElse Address.Fragment <> "" OrElse (Address.AbsolutePath <> "" AndAlso Address.AbsolutePath <> "/") Then Throw New InvalidDataException("联机节点必须是带端口的连接地址。")
            Select Case Address.Scheme.ToLowerInvariant()
                Case "tcp", "udp", "ws", "wss", "quic"
                Case Else : Throw New InvalidDataException("联机节点只支持 TCP、UDP、WS、WSS 和 QUIC。")
            End Select
            If Not Result.Contains(Value, StringComparer.OrdinalIgnoreCase) Then Result.Add(Value)
        Next
        If Result.Count = 0 Then Throw New InvalidDataException("没有可用的联机节点。")
        Return Result
    End Function

    Friend Function ParseCustomPeers(Value As String) As List(Of String)
        Return NormalizePeers(If(Value, "").Split(New Char() {","c, "，"c}, StringSplitOptions.RemoveEmptyEntries))
    End Function

    Private Sub ValidateInvite(Value As LinkInvite)
        If Value Is Nothing OrElse Value.Port < 1024 OrElse Value.Port > 65535 Then Throw New InvalidDataException("房间端口必须在 1024 到 65535 之间。")
        If Not System.Text.RegularExpressions.Regex.IsMatch(If(Value.Network, ""), "\APCLF-[0-9a-f]{32}\z") OrElse
           Not System.Text.RegularExpressions.Regex.IsMatch(If(Value.Secret, ""), "\A[0-9a-f]{32}\z") Then Throw New InvalidDataException("房间名称或密钥格式无效。")
        Value.Peers = NormalizePeers(Value.Peers)
    End Sub

    Friend Function EncodeInvite(Value As LinkInvite) As String
        ValidateInvite(Value)
        Dim Data As New JObject(New JProperty("v", 1), New JProperty("network", Value.Network), New JProperty("secret", Value.Secret), New JProperty("port", Value.Port), New JProperty("peers", New JArray(Value.Peers)))
        Return InvitePrefix & Convert.ToBase64String(Encoding.UTF8.GetBytes(Data.ToString(Formatting.None))).TrimEnd("="c).Replace("+", "-").Replace("/", "_")
    End Function

    Friend Function GetInviteFingerprint(Text As String) As String
        Dim Canonical = EncodeInvite(DecodeInvite(Text))
        Using Digest = SHA256.Create()
            Return "SHA256:" & BitConverter.ToString(Digest.ComputeHash(Encoding.UTF8.GetBytes(Canonical))).Replace("-", "")
        End Using
    End Function

    Friend Function DecodeInvite(Text As String) As LinkInvite
        Dim Value = If(Text, "").Trim()
        If Value.Length > MaximumInviteLength Then Throw New InvalidDataException("邀请码过长。")
        Dim PrefixAt = Value.IndexOf(InvitePrefix, StringComparison.Ordinal)
        If PrefixAt < 0 Then Throw New InvalidDataException("此联机功能使用 PCLF1 邀请码；请让房主使用同版 fork 重新创建房间。")
        Value = Value.Substring(PrefixAt + InvitePrefix.Length)
        Dim EndAt = 0
        While EndAt < Value.Length AndAlso (Char.IsLetterOrDigit(Value(EndAt)) OrElse Value(EndAt) = "-"c OrElse Value(EndAt) = "_"c)
            EndAt += 1
        End While
        Dim Encoded = Value.Substring(0, EndAt)
        Dim Suffix = Value.Substring(EndAt).Trim()
        If Suffix.Length > 0 AndAlso Not {"】", "]", "】，即可加入联机房间！", "】，即可加入联机房间!"}.Contains(Suffix) Then Throw New InvalidDataException("邀请码包含多余字符。")
        If Encoded.Length = 0 OrElse Not System.Text.RegularExpressions.Regex.IsMatch(Encoded, "\A[A-Za-z0-9_-]+\z") Then Throw New InvalidDataException("邀请码编码无效。")
        Try
            Dim Base64 = Encoded.Replace("-", "+").Replace("_", "/")
            Base64 = Base64.PadRight(((Base64.Length + 3) \ 4) * 4, "="c)
            Dim Utf8 As New UTF8Encoding(False, True)
            Dim Raw = Utf8.GetString(Convert.FromBase64String(Base64))
            Dim Json As JObject
            Using Reader As New JsonTextReader(New StringReader(Raw)) With {.MaxDepth = 8, .DateParseHandling = DateParseHandling.None}
                Json = JObject.Load(Reader, New JsonLoadSettings With {.DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error})
                If Reader.Read() Then Throw New InvalidDataException("邀请码包含多余 JSON 数据。")
            End Using
            If Json.Properties().Count() <> 5 OrElse Json("v") Is Nothing OrElse Json("v").Type <> JTokenType.Integer OrElse CInt(Json("v")) <> 1 OrElse
               Json("network") Is Nothing OrElse Json("network").Type <> JTokenType.String OrElse Json("secret") Is Nothing OrElse Json("secret").Type <> JTokenType.String OrElse
               Json("port") Is Nothing OrElse Json("port").Type <> JTokenType.Integer OrElse Not TypeOf Json("peers") Is JArray Then Throw New InvalidDataException("邀请码字段无效。")
            Dim Peers = CType(Json("peers"), JArray)
            If Peers.Any(Function(P) P.Type <> JTokenType.String) Then Throw New InvalidDataException("邀请码节点字段无效。")
            Dim Result As New LinkInvite With {.Network = CStr(Json("network")), .Secret = CStr(Json("secret")), .Port = CInt(Json("port")), .Peers = Peers.Values(Of String)().ToList()}
            ValidateInvite(Result)
            Return Result
        Catch ex As InvalidDataException
            Throw
        Catch ex As Exception
            Throw New InvalidDataException("邀请码损坏或格式无效。")
        End Try
    End Function

    Friend Function SelectPublicPeers(JsonText As String) As List(Of String)
        If JsonText Is Nothing OrElse JsonText.Length > 4 * 1024 * 1024 Then Throw New InvalidDataException("节点清单过大或为空。")
        Dim Root = JObject.Parse(JsonText)
        Dim Items = TryCast(Root.SelectToken("data.items"), JArray)
        If Items Is Nothing OrElse Items.Count > 10000 Then Throw New InvalidDataException("节点清单格式无效。")
        Dim Candidates As New List(Of Tuple(Of Boolean, Double, String))
        For Each Node In Items.OfType(Of JObject)()
            Try
                If Not Node.Value(Of Boolean)("is_active") OrElse Not Node.Value(Of Boolean)("is_approved") Then Continue For
                Dim Tags = TryCast(Node("tags"), JArray)
                If Tags Is Nothing OrElse Not Tags.Values(Of String)().Any(Function(T) T IsNot Nothing AndAlso T.Replace(" ", "").Replace("　", "").ToUpperInvariant().Contains("MC")) Then Continue For
                Dim Address = NormalizePeers(New String() {Node.Value(Of String)("address")})(0)
                Dim Health = If(Node("health_percentage_24h") Is Nothing, 100.0, Node.Value(Of Double)("health_percentage_24h"))
                Dim Load = If(Node("usage_percentage") Is Nothing, 50.0, Node.Value(Of Double)("usage_percentage"))
                If Double.IsNaN(Health) OrElse Double.IsInfinity(Health) OrElse Double.IsNaN(Load) OrElse Double.IsInfinity(Load) OrElse Health <= 0 OrElse Load >= 100 Then Continue For
                Candidates.Add(Tuple.Create(Node.Value(Of Boolean)("allow_relay"), Load + (100 - Math.Min(100, Health)), Address))
            Catch ex As Exception
                ' Ignore malformed entries without echoing server-controlled values.
            End Try
        Next
        Dim Ranked = Candidates.OrderBy(Function(N) N.Item2).ToList()
        Dim Selected = Ranked.Where(Function(N) Not N.Item1).Take(1).Concat(Ranked.Where(Function(N) N.Item1).Take(3)).Select(Function(N) N.Item3).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        If Selected.Count = 0 AndAlso Ranked.Count > 0 Then Selected.Add(Ranked(0).Item3)
        Return NormalizePeers(Selected)
    End Function

    Friend Function FetchPublicNodes(Token As CancellationToken) As String
        Using Deadline = CancellationTokenSource.CreateLinkedTokenSource(Token), Client As New HttpClient()
            Deadline.CancelAfter(8000)
            Client.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Fork-Link/1")
            Using Response = Client.GetAsync(NodesUrl, HttpCompletionOption.ResponseHeadersRead, Deadline.Token).GetAwaiter().GetResult()
                Response.EnsureSuccessStatusCode()
                Using Input = Response.Content.ReadAsStreamAsync().GetAwaiter().GetResult(), Output As New MemoryStream()
                    Dim Buffer(16383) As Byte
                    Do
                        Dim Count = Input.ReadAsync(Buffer, 0, Buffer.Length, Deadline.Token).GetAwaiter().GetResult()
                        If Count = 0 Then Exit Do
                        If Output.Length + Count > 4 * 1024 * 1024 Then Throw New InvalidDataException("节点清单过大。")
                        Output.Write(Buffer, 0, Count)
                    Loop
                    Return New UTF8Encoding(False, True).GetString(Output.ToArray())
                End Using
            End Using
        End Using
    End Function

    Friend Function FileSha256(Path As String) As String
        Using Stream = File.OpenRead(Path), Hash = SHA256.Create()
            Return BitConverter.ToString(Hash.ComputeHash(Stream)).Replace("-", "").ToLowerInvariant()
        End Using
    End Function

    Friend Function InstalledDirectory(Root As String, Package As ComponentPackage) As String
        Return Path.Combine(Root, "v" & ComponentVersion & "-" & Package.Architecture)
    End Function

    Friend Function IsInstalled(DirectoryPath As String, Package As ComponentPackage) As Boolean
        Try
            RejectReparseParents(DirectoryPath)
            Dim Manifest = JObject.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "pclf-component.json")))
            If CStr(Manifest("packageSha256")) <> Package.Sha256 Then Return False
            Dim Files = TryCast(Manifest("files"), JObject)
            If Files Is Nothing OrElse Not Files.ContainsKey("easytier-core.exe") OrElse Not Files.ContainsKey("easytier-cli.exe") OrElse Not Files.ContainsKey("Packet.dll") Then Return False
            For Each Entry In Files.Properties()
                If Entry.Name <> Path.GetFileName(Entry.Name) Then Return False
                Dim FilePath = Path.Combine(DirectoryPath, Entry.Name)
                If Not File.Exists(FilePath) OrElse (File.GetAttributes(FilePath) And FileAttributes.ReparsePoint) <> 0 OrElse FileSha256(FilePath) <> CStr(Entry.Value) Then Return False
            Next
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub RejectReparseParents(Value As String)
        Dim Current = New DirectoryInfo(Path.GetFullPath(Value))
        While Current IsNot Nothing
            If Current.Exists AndAlso (Current.Attributes And FileAttributes.ReparsePoint) <> 0 Then Throw New IOException("组件目录不能经过目录联接或符号链接。")
            Current = Current.Parent
        End While
    End Sub

    Friend Sub ExtractRuntime(ArchivePath As String, Destination As String, Token As CancellationToken)
        RejectReparseParents(Destination)
        Directory.CreateDirectory(Destination)
        Dim Total As Long = 0
        Dim Names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Using Archive = ZipFile.OpenRead(ArchivePath)
            If Archive.Entries.Count > 128 Then Throw New InvalidDataException("组件压缩包包含过多文件。")
            For Each Entry In Archive.Entries
                Token.ThrowIfCancellationRequested()
                Dim Relative = Entry.FullName.Replace("\", "/")
                Dim Parts = Relative.TrimEnd("/"c).Split("/"c)
                If Relative.StartsWith("/", StringComparison.Ordinal) OrElse Parts.Any(Function(P) P = "" OrElse P = "." OrElse P = ".." OrElse P.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) OrElse
                   ((Entry.ExternalAttributes >> 16) And &HF000) = &HA000 OrElse (Entry.ExternalAttributes And CInt(FileAttributes.ReparsePoint)) <> 0 Then Throw New InvalidDataException("组件压缩包路径无效。")
                If Relative.EndsWith("/", StringComparison.Ordinal) Then Continue For
                Total += Entry.Length
                If Total > 256L * 1024 * 1024 OrElse Entry.Length > 100L * 1024 * 1024 Then Throw New InvalidDataException("组件压缩包展开后过大。")
                Dim Name = Parts(Parts.Length - 1)
                Dim Extension = Path.GetExtension(Name)
                If Name <> "easytier-core.exe" AndAlso Name <> "easytier-cli.exe" AndAlso Not Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) AndAlso Not Extension.Equals(".sys", StringComparison.OrdinalIgnoreCase) Then Continue For
                If Not Names.Add(Name) Then Throw New InvalidDataException("组件压缩包包含重复文件。")
                Using Source = Entry.Open(), Output = New FileStream(Path.Combine(Destination, Name), FileMode.CreateNew)
                    Dim Buffer(65535) As Byte
                    Dim Count As Integer
                    Dim Written As Long = 0
                    Do
                        Token.ThrowIfCancellationRequested()
                        Count = Source.Read(Buffer, 0, Buffer.Length)
                        If Count = 0 Then Exit Do
                        Written += Count
                        If Written > Entry.Length Then Throw New InvalidDataException("组件文件长度无效。")
                        Output.Write(Buffer, 0, Count)
                    Loop
                    If Written <> Entry.Length Then Throw New InvalidDataException("组件文件长度无效。")
                End Using
            Next
        End Using
        If Not Names.Contains("easytier-core.exe") OrElse Not Names.Contains("easytier-cli.exe") OrElse Not Names.Contains("Packet.dll") Then Throw New InvalidDataException("组件压缩包缺少必要文件。")
    End Sub

    Friend Function InstallPackage(ArchivePath As String, Root As String, Package As ComponentPackage, Token As CancellationToken) As String
        Token.ThrowIfCancellationRequested()
        If FileSha256(ArchivePath) <> Package.Sha256 Then Throw New InvalidDataException("EasyTier 下载包 SHA256 校验失败。")
        RejectReparseParents(Root)
        Directory.CreateDirectory(Root)
        Dim Target = InstalledDirectory(Root, Package)
        Dim Stage = Path.Combine(Root, ".pending-" & Guid.NewGuid().ToString("N"))
        Dim Backup = Path.Combine(Root, ".previous-" & Guid.NewGuid().ToString("N"))
        Try
            ExtractRuntime(ArchivePath, Stage, Token)
            Dim Files As New JObject()
            For Each Item In Directory.GetFiles(Stage)
                Token.ThrowIfCancellationRequested()
                Files.Add(Path.GetFileName(Item), FileSha256(Item))
            Next
            File.WriteAllText(Path.Combine(Stage, "pclf-component.json"), New JObject(New JProperty("packageSha256", Package.Sha256), New JProperty("files", Files)).ToString(), New UTF8Encoding(False))
            Token.ThrowIfCancellationRequested()
            If Directory.Exists(Target) Then Directory.Move(Target, Backup)
            Try
                Directory.Move(Stage, Target)
            Catch
                If Directory.Exists(Backup) AndAlso Not Directory.Exists(Target) Then Directory.Move(Backup, Target)
                Throw
            End Try
            Return Target
        Finally
            If Directory.Exists(Stage) Then Directory.Delete(Stage, True)
            ' Preserve the previous installation for recovery; no existing runtime is recursively deleted.
        End Try
    End Function

    Friend Function RunCli(Executable As String, RpcPort As Integer, Token As CancellationToken, Optional TimeoutMs As Integer = 4000) As String
        If RpcPort < 1 OrElse RpcPort > 65535 Then Throw New ArgumentOutOfRangeException("RpcPort")
        Return RunOwnedCommand(Executable, "-o json -p 127.0.0.1:" & RpcPort.ToString(CultureInfo.InvariantCulture) & " peer", Token, TimeoutMs)
    End Function

    Friend Function RunOwnedCommand(Executable As String, Arguments As String, Token As CancellationToken, TimeoutMs As Integer) As String
        Token.ThrowIfCancellationRequested()
        If TimeoutMs < 1 Then Throw New ArgumentOutOfRangeException("TimeoutMs")
        Dim Output As New StringBuilder(), Overflow As Boolean = False
        Dim Gate As New Object()
        Using Child As New Process()
            Child.StartInfo = New ProcessStartInfo(Executable, Arguments) With {.UseShellExecute = False, .CreateNoWindow = True, .WindowStyle = ProcessWindowStyle.Hidden, .WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(Executable)), .RedirectStandardOutput = True, .RedirectStandardError = True, .StandardOutputEncoding = Encoding.UTF8, .StandardErrorEncoding = Encoding.UTF8}
            AddHandler Child.OutputDataReceived,
                Sub(Sender As Object, E As DataReceivedEventArgs)
                    If E.Data Is Nothing Then Return
                    SyncLock Gate
                        If Output.Length + E.Data.Length > 1024 * 1024 Then
                            Overflow = True
                        Else
                            Output.AppendLine(E.Data)
                        End If
                    End SyncLock
                End Sub
            AddHandler Child.ErrorDataReceived, Sub(Sender As Object, E As DataReceivedEventArgs)
                                                  ' Drain stderr without including configuration or process output in errors.
                                              End Sub
            Dim Started As Boolean = False
            Try
                Started = Child.Start()
                Child.BeginOutputReadLine()
                Child.BeginErrorReadLine()
                Dim Clock = Stopwatch.StartNew()
                While Not Child.WaitForExit(40)
                    Token.ThrowIfCancellationRequested()
                    If Clock.ElapsedMilliseconds >= TimeoutMs Then Throw New TimeoutException("EasyTier CLI 响应超时。")
                    SyncLock Gate
                        If Overflow Then Throw New InvalidDataException("EasyTier CLI 输出过大。")
                    End SyncLock
                End While
                Child.WaitForExit() ' Process has exited; drain asynchronous output handlers.
                Token.ThrowIfCancellationRequested()
                If Child.ExitCode <> 0 Then Throw New IOException("EasyTier CLI 执行失败（退出码 " & Child.ExitCode.ToString(CultureInfo.InvariantCulture) & "）。")
                SyncLock Gate
                    If Overflow Then Throw New InvalidDataException("EasyTier CLI 输出过大。")
                    Return Output.ToString()
                End SyncLock
            Finally
                If Started AndAlso Not Child.HasExited Then
                    Child.Kill()
                    Child.WaitForExit(2000)
                End If
            End Try
        End Using
    End Function

    Friend Function ParseCliPeers(Value As String) As JArray
        If String.IsNullOrWhiteSpace(Value) OrElse Value.Length > 1024 * 1024 Then Throw New InvalidDataException("EasyTier CLI 没有返回有效节点信息。")
        Try
            Dim Result = JArray.Parse(Value)
            If Result.Count > 4096 OrElse Result.Any(Function(Row) Not TypeOf Row Is JObject OrElse Row("hostname") Is Nothing OrElse Row("cost") Is Nothing OrElse Row("lat_ms") Is Nothing OrElse Row("nat_type") Is Nothing) Then Throw New InvalidDataException("EasyTier CLI 节点格式无效。")
            Return Result
        Catch ex As InvalidDataException
            Throw
        Catch ex As Exception
            Throw New InvalidDataException("EasyTier CLI 节点格式无效。")
        End Try
    End Function

    Friend Function HasLocalPeer(Rows As JArray) As Boolean
        Return Rows IsNot Nothing AndAlso Rows.OfType(Of JObject)().Any(Function(Row) String.Equals(CStr(Row("cost")), "Local", StringComparison.OrdinalIgnoreCase))
    End Function

    Friend Function CanFinishRoom(IsHost As Boolean, LocalPeerReady As Boolean, HostPing As Double) As Boolean
        ' A new host must publish its invitation before any player can connect.
        If IsHost Then Return LocalPeerReady
        Return HostPing > 0 AndAlso Not Double.IsNaN(HostPing) AndAlso Not Double.IsInfinity(HostPing)
    End Function
End Module
