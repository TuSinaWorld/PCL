Imports System.IO
Imports System.IO.Compression
Imports System.IO.Pipes
Imports System.Net
Imports System.Net.Sockets
Imports System.Security.AccessControl
Imports System.Security.Cryptography
Imports System.Security.Principal
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Friend Module ModVlanLink
    Friend Const InvitePrefix As String = "PCLV1:"
    Friend Const MaximumInviteLength As Integer = 16384
    Friend Const ComponentResource As String = "PCL.VLanBundle.zip"
    Friend ReadOnly Root As String = Path.Combine(Paths.AppDataThenName, "VLanFork")
    Friend Class ServerProfile
        Public Host As String
        Public Port As Integer = 11510
        Public Password As String
        Public Compatibility As Boolean
        Public NoRoomPassword As Boolean
    End Class
    Friend Class RoomInvite
        Public Server As ServerProfile
        Public RoomId As UInteger
        Public RoomPassword As String = ""
        Public HostPeerId As UInteger
        Public HostIp As String
        Public GamePort As Integer
        Public ControlPort As Integer
        Public SessionKey As String
    End Class
    Friend Function RandomBytes(Count As Integer) As Byte()
        Dim Data(Count - 1) As Byte
        Using Rng = RandomNumberGenerator.Create()
            Rng.GetBytes(Data)
        End Using
        Return Data
    End Function
    Friend Function RandomToken() As String
        Return BitConverter.ToString(RandomBytes(16)).Replace("-", "").ToLowerInvariant()
    End Function
    Friend Sub ValidateServer(Value As ServerProfile)
        If Value Is Nothing Then Throw New InvalidDataException("请先设置联机服务器。")
        Value.Host = If(Value.Host, "").Trim().ToLowerInvariant()
        If Value.Host.Length > 253 OrElse Uri.CheckHostName(Value.Host) = UriHostNameType.Unknown OrElse
            Value.Host.Contains("%") OrElse Value.Port < 1 OrElse Value.Port > 65535 Then Throw New InvalidDataException("服务器地址或端口无效。")
        If Uri.CheckHostName(Value.Host) = UriHostNameType.IPv6 Then Throw New InvalidDataException("当前 VLan 服务端连接需要 IPv4 地址或可解析到 IPv4 的域名。")
        If String.IsNullOrWhiteSpace(Value.Password) OrElse Encoding.UTF8.GetByteCount(Value.Password) < 8 OrElse Encoding.UTF8.GetByteCount(Value.Password) > 256 OrElse
            Value.Password.Any(Function(C) Char.IsControl(C)) Then Throw New InvalidDataException("服务器鉴权密码须为 8–256 字节，且不能包含控制字符。")
    End Sub
    Friend Function IsVirtualIp(Value As String) As Boolean
        Dim Ip As IPAddress = Nothing
        If Not IPAddress.TryParse(Value, Ip) OrElse Ip.AddressFamily <> AddressFamily.InterNetwork Then Return False
        Dim B = Ip.GetAddressBytes()
        Return B(0) = 10 AndAlso B(1) = 10 AndAlso B(2) = 0 AndAlso B(3) >= 2 AndAlso B(3) <= 254
    End Function
    Friend Sub ValidateInvite(Value As RoomInvite)
        If Value Is Nothing Then Throw New InvalidDataException("邀请码为空。")
        ValidateServer(Value.Server)
        If Value.RoomId = 0 OrElse Value.HostPeerId = 0 OrElse Not IsVirtualIp(Value.HostIp) OrElse
            Value.GamePort < 1024 OrElse Value.GamePort > 65535 OrElse Value.ControlPort < 1024 OrElse Value.ControlPort > 65535 OrElse
            Not Regex.IsMatch(If(Value.SessionKey, ""), "\A[0-9a-f]{32}\z") Then Throw New InvalidDataException("邀请码中的房间信息无效。")
        Value.RoomPassword = If(Value.RoomPassword, "")
        If Value.RoomPassword <> "" AndAlso Not Regex.IsMatch(Value.RoomPassword, "\A[A-Za-z0-9]{6,63}\z") Then Throw New InvalidDataException("房间密码须为 6–63 位字母或数字。")
    End Sub
    Friend Function ParseJson(Text As String) As JObject
        Return JObject.Parse(Text, New JsonLoadSettings With {.DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error})
    End Function
    Private Function RequiredString(Json As JObject, Name As String) As String
        If Json(Name) Is Nothing OrElse Json(Name).Type <> JTokenType.String Then Throw New InvalidDataException("邀请码缺少有效字段。")
        Return CStr(Json(Name))
    End Function
    Friend Function EncodeInvite(Value As RoomInvite) As String
        ValidateInvite(Value)
        Dim Json As New JObject From {{"v", 1}, {"host", Value.Server.Host}, {"port", Value.Server.Port}, {"password", Value.Server.Password},
            {"compatibility", Value.Server.Compatibility}, {"roomId", Value.RoomId}, {"roomPassword", Value.RoomPassword}, {"hostPeerId", Value.HostPeerId},
            {"hostIp", Value.HostIp}, {"gamePort", Value.GamePort}, {"controlPort", Value.ControlPort}, {"sessionKey", Value.SessionKey}}
        Dim Enc = RandomBytes(32), Mac = RandomBytes(32), Iv = RandomBytes(16), Cipher As Byte()
        Using Aes = System.Security.Cryptography.Aes.Create()
            Aes.Key = Enc : Aes.IV = Iv
            Dim Plain = Encoding.UTF8.GetBytes(Json.ToString(Formatting.None))
            Using Transform = Aes.CreateEncryptor()
                Cipher = Transform.TransformFinalBlock(Plain, 0, Plain.Length)
            End Using
        End Using
        Dim Body = Enc.Concat(Mac).Concat(Iv).Concat(Cipher).ToArray(), Tag As Byte()
        Using Hmac As New HMACSHA256(Mac)
            Tag = Hmac.ComputeHash(Body)
        End Using
        Dim Code = InvitePrefix & Convert.ToBase64String(Body.Concat(Tag).ToArray()).TrimEnd("="c).Replace("+", "-").Replace("/", "_")
        If Code.Length > MaximumInviteLength Then Throw New InvalidDataException("邀请码过长。")
        Return Code
    End Function
    Friend Function DecodeInvite(Code As String) As RoomInvite
        Try
            If Code Is Nothing OrElse Code.Length > MaximumInviteLength + 200 Then Throw New InvalidDataException("邀请码为空或过长。")
            If Code.Contains("PCLF1:") Then Throw New InvalidDataException("这是旧 EasyTier 邀请码，请房主创建 VLan 房间并重新分享。")
            Dim Match = Regex.Match(Code, "PCLV1:([A-Za-z0-9_-]+)")
            If Not Match.Success OrElse Match.Value.Length > MaximumInviteLength Then Throw New InvalidDataException("请输入有效的 PCLV1 邀请码。")
            Dim Raw = Match.Groups(1).Value.Replace("-", "+").Replace("_", "/")
            Raw &= New String("="c, (4 - Raw.Length Mod 4) Mod 4)
            Dim Data = Convert.FromBase64String(Raw)
            If Data.Length < 128 OrElse (Data.Length - 112) Mod 16 <> 0 Then Throw New InvalidDataException("邀请码已损坏。")
            Dim Body = Data.Take(Data.Length - 32).ToArray(), Expected As Byte()
            Using Hmac As New HMACSHA256(Data.Skip(32).Take(32).ToArray())
                Expected = Hmac.ComputeHash(Body)
            End Using
            If Not FixedEquals(Expected, Data.Skip(Body.Length).ToArray()) Then Throw New InvalidDataException("邀请码校验失败，请重新复制。")
            Dim Plain As Byte()
            Using Aes = System.Security.Cryptography.Aes.Create()
                Aes.Key = Data.Take(32).ToArray() : Aes.IV = Data.Skip(64).Take(16).ToArray()
                Using Transform = Aes.CreateDecryptor()
                    Plain = Transform.TransformFinalBlock(Body, 80, Body.Length - 80)
                End Using
            End Using
            Dim Json = ParseJson(New UTF8Encoding(False, True).GetString(Plain))
            For Each Name In {"v", "port", "roomId", "hostPeerId", "gamePort", "controlPort"}
                If Json(Name) Is Nothing OrElse Json(Name).Type <> JTokenType.Integer Then Throw New InvalidDataException("邀请码数字字段无效。")
            Next
            If CInt(Json("v")) <> 1 OrElse Json("compatibility") Is Nothing OrElse Json("compatibility").Type <> JTokenType.Boolean Then Throw New InvalidDataException("邀请码版本不受支持。")
            Dim RoomPassword = Json("roomPassword")
            If RoomPassword IsNot Nothing AndAlso RoomPassword.Type <> JTokenType.Null AndAlso RoomPassword.Type <> JTokenType.String Then Throw New InvalidDataException("房间密码字段无效。")
            Dim Result As New RoomInvite With {.Server = New ServerProfile With {.Host = RequiredString(Json, "host"), .Port = CInt(Json("port")), .Password = RequiredString(Json, "password"), .Compatibility = CBool(Json("compatibility"))},
                .RoomId = CUInt(Json("roomId")), .HostPeerId = CUInt(Json("hostPeerId")), .HostIp = RequiredString(Json, "hostIp"), .GamePort = CInt(Json("gamePort")),
                .ControlPort = CInt(Json("controlPort")), .SessionKey = RequiredString(Json, "sessionKey"), .RoomPassword = If(RoomPassword Is Nothing OrElse RoomPassword.Type = JTokenType.Null, "", CStr(RoomPassword))}
            ValidateInvite(Result) : Return Result
        Catch Ex As InvalidDataException
            Throw
        Catch
            Throw New InvalidDataException("邀请码损坏或格式无效，请重新复制。")
        End Try
    End Function
    Friend Function FixedEquals(A As Byte(), B As Byte()) As Boolean
        If A.Length <> B.Length Then Return False
        Dim Difference As Integer = 0
        For I = 0 To A.Length - 1
            Difference = Difference Or (CInt(A(I)) Xor CInt(B(I)))
        Next
        Return Difference = 0
    End Function
    Friend Function Fingerprint(Code As String) As String
        Dim Match = System.Text.RegularExpressions.Regex.Match(Code, "PCL[VF]1:[A-Za-z0-9_-]+")
        If Match.Success Then Code = Match.Value
        Using Sha = SHA256.Create()
            Return "SHA256:" & BitConverter.ToString(Sha.ComputeHash(Encoding.UTF8.GetBytes(Code))).Replace("-", "")
        End Using
    End Function
    Friend Sub SaveProfile(Value As ServerProfile)
        ValidateServer(Value)
        Directory.CreateDirectory(Root)
        Dim Json As New JObject From {{"host", Value.Host}, {"port", Value.Port}, {"password", Value.Password}, {"compatibility", Value.Compatibility}, {"noRoomPassword", Value.NoRoomPassword}}
        Dim Data = ProtectedData.Protect(Encoding.UTF8.GetBytes(Json.ToString(Formatting.None)), Nothing, DataProtectionScope.CurrentUser)
        Dim Target = Path.Combine(Root, "server.dpapi"), Pending = Target & ".pending"
        File.WriteAllBytes(Pending, Data)
        If File.Exists(Target) Then
            File.Replace(Pending, Target, Nothing)
        Else
            File.Move(Pending, Target)
        End If
    End Sub
    Friend Function LoadProfile() As ServerProfile
        Dim Target = Path.Combine(Root, "server.dpapi")
        If Not File.Exists(Target) Then Return Nothing
        Try
            If New FileInfo(Target).Length > 16384 Then Return Nothing
            Dim Json = ParseJson(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Target), Nothing, DataProtectionScope.CurrentUser)))
            Dim Result As New ServerProfile With {.Host = CStr(Json("host")), .Port = CInt(Json("port")), .Password = CStr(Json("password")), .Compatibility = CBool(Json("compatibility")), .NoRoomPassword = If(Json("noRoomPassword") Is Nothing, False, CBool(Json("noRoomPassword")))}
            ValidateServer(Result) : Return Result
        Catch
            Return Nothing
        End Try
    End Function
    Friend Function ErrorText(Code As String) As String
        Select Case Code
            Case "ROOM_NOT_FOUND", "ROOM_EXPIRED" : Return "房间已关闭或邀请码已失效，请向房主获取新的邀请码。"
            Case "ROOM_FULL" : Return "房间已满，请等待玩家离开后重试。"
            Case "ROOM_PASSWORD_REQUIRED" : Return "此房间需要密码，请输入房间密码或更换邀请码。"
            Case "ROOM_PASSWORD_INVALID" : Return "房间密码已更改或邀请码已失效，请输入密码或更换邀请码。"
            Case "AUTHENTICATION_FAILED" : Return "服务器鉴权失败，请检查服务器密码及协议版本。"
            Case "SERVER_UNREACHABLE" : Return "无法连接联机服务器，请检查地址、端口和网络。"
            Case "ADAPTER_FAILED" : Return "虚拟网卡准备失败，请检查管理员授权、Wintun 和网络设置。"
            Case "BUSY" : Return "上一次联机操作尚未完成，请等待清理结束。"
            Case Else : Return "联机操作未能完成，请检查服务器连接后重试。（" & Code & "）"
        End Select
    End Function
    Friend Function EnsureComponent() As String
        If Not Environment.Is64BitOperatingSystem Then Throw New PlatformNotSupportedException("当前 VLan 组件需要 Windows x64。")
        Using Source = GetType(ServerProfile).Assembly.GetManifestResourceStream(ComponentResource)
            If Source Is Nothing Then Throw New IOException("此构建未包含 VLan 联机组件，请使用完整的融合测试包。")
            Using Copy As New MemoryStream()
                Source.CopyTo(Copy)
                Dim Data = Copy.ToArray(), Hash As String
                Using Sha = SHA256.Create()
                    Hash = BitConverter.ToString(Sha.ComputeHash(Data)).Replace("-", "").ToLowerInvariant()
                End Using
                If Hash <> ModVlanPackage.BundleSha256 Then Throw New IOException("联机组件校验失败。")
                Dim Target = Path.Combine(Root, "components", Hash.Substring(0, 24)), Entries As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
                Using Zip As New ZipArchive(New MemoryStream(Data), ZipArchiveMode.Read)
                    If Zip.Entries.Count > 100 Then Throw New IOException("联机组件包无效。")
                    For Each Entry In Zip.Entries
                        If Entry.Name = "" Then Continue For
                        Dim Relative = Entry.FullName.Replace("/", "\")
                        If Path.IsPathRooted(Relative) OrElse Relative.Split("\"c).Any(Function(P) P = ".." OrElse P = "." OrElse P.Contains(":")) OrElse Entry.Length > 32 * 1024 * 1024 Then Throw New IOException("联机组件包路径无效。")
                        Using Input = Entry.Open(), Output As New MemoryStream()
                            Input.CopyTo(Output)
                            Entries.Add(Relative, Output.ToArray())
                        End Using
                    Next
                End Using
                If Not Entries.ContainsKey("vlan-cli.exe") OrElse Not Entries.ContainsKey("wintun.dll") Then Throw New IOException("联机组件包不完整。")
                Dim Valid = Directory.Exists(Target)
                For Each Entry In Entries
                    Dim FilePath = Path.Combine(Target, Entry.Key)
                    If Not File.Exists(FilePath) OrElse New FileInfo(FilePath).Length <> Entry.Value.Length Then
                        Valid = False : Exit For
                    End If
                    Using Sha = SHA256.Create()
                        If Not FixedEquals(Sha.ComputeHash(File.ReadAllBytes(FilePath)), Sha.ComputeHash(Entry.Value)) Then
                            Valid = False : Exit For
                        End If
                    End Using
                Next
                If Valid Then Return Path.Combine(Target, "vlan-cli.exe")
                Dim Stage = Target & "-" & RandomToken()
                Directory.CreateDirectory(Stage)
                For Each Entry In Entries
                    Dim FilePath = Path.Combine(Stage, Entry.Key)
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath))
                    File.WriteAllBytes(FilePath, Entry.Value)
                Next
                If Directory.Exists(Target) Then Directory.Move(Target, Target & ".invalid-" & RandomToken())
                Directory.Move(Stage, Target) 'Keep the previous directory for recovery; never overwrite loaded files.
                Return Path.Combine(Target, "vlan-cli.exe")
            End Using
        End Using
    End Function
    Friend Function ReadBoundedLine(Input As Stream, Limit As Integer) As String
        Using Bytes As New MemoryStream()
            Do
                Dim B = Input.ReadByte()
                If B < 0 Then Throw New EndOfStreamException()
                If B = 10 Then Exit Do
                If Bytes.Length >= Limit Then Throw New InvalidDataException("联机响应过长。")
                Bytes.WriteByte(CByte(B))
            Loop
            Return New UTF8Encoding(False, True).GetString(Bytes.ToArray()).TrimEnd(ChrW(13))
        End Using
    End Function
End Module

Friend Class VlanSession
    Implements IDisposable
    Private Pipe As NamedPipeServerStream
    Private Child As Process
    Private Reader As Thread
    Private ReadOnly Gate As New Object()
    Private LastStatus As JObject
    Private LastError As String
    Private RequestNumber As Integer
    Private Stopping As Boolean
    Private StartupComplete As Boolean
    Friend Event Interrupted()
    Private ReadOnly Departed As New HashSet(Of UInteger)()
    Friend Function PeerHasLeft(Pid As UInteger) As Boolean
        SyncLock Gate
            Return Departed.Contains(Pid)
        End SyncLock
    End Function
    Friend ReadOnly Property HasExited As Boolean
        Get
            Return If(Child Is Nothing, StartupComplete, Child.HasExited)
        End Get
    End Property
    Friend ReadOnly Property Snapshot As JObject
        Get
            SyncLock Gate
                Return If(LastStatus Is Nothing, Nothing, CType(LastStatus.DeepClone(), JObject))
            End SyncLock
        End Get
    End Property
    Friend ReadOnly Property Failure As String
        Get
            SyncLock Gate
                Return LastError
            End SyncLock
        End Get
    End Property
    Friend Sub Start(Exe As String, Token As CancellationToken, Optional Elevate As Boolean = True)
        Try
        If Stopping Then Throw New IOException("联机启动已取消。")
        Token.ThrowIfCancellationRequested()
        Dim Security As New PipeSecurity()
        Security.AddAccessRule(New PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow))
        Security.AddAccessRule(New PipeAccessRule(New SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, Nothing), PipeAccessRights.FullControl, AccessControlType.Allow))
        Security.AddAccessRule(New PipeAccessRule(New SecurityIdentifier(WellKnownSidType.LocalSystemSid, Nothing), PipeAccessRights.FullControl, AccessControlType.Allow))
        Dim Name = "PCL-VLAN-IPC-" & Guid.NewGuid().ToString("N")
        Pipe = New NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, Security)
        Dim Connection = Pipe.WaitForConnectionAsync()
        Try
            Dim Info As New ProcessStartInfo(Exe, "--machine-pipe " & Name & " --parent-pid " & Process.GetCurrentProcess().Id) With {
                .UseShellExecute = Elevate, .Verb = If(Elevate, "runas", ""), .CreateNoWindow = True, .WindowStyle = ProcessWindowStyle.Hidden, .WorkingDirectory = Path.GetDirectoryName(Exe)}
            Child = Process.Start(Info)
        Catch Ex As ComponentModel.Win32Exception When Ex.NativeErrorCode = 1223
            Pipe.Dispose() : Throw New IOException("管理员授权已取消，请点击重试后重新授权。")
        Catch
            Pipe.Dispose() : Throw New IOException("无法启动 VLan 联机组件。")
        End Try
        Token.ThrowIfCancellationRequested()
        Dim Clock = Stopwatch.StartNew()
        While Not Connection.IsCompleted
            Token.ThrowIfCancellationRequested()
            If Child.HasExited Then Throw New IOException("联机辅助进程未能建立控制连接。")
            If Clock.ElapsedMilliseconds > 20000 Then Throw New TimeoutException("等待联机组件连接超时。")
            Thread.Sleep(25)
        End While
        Connection.GetAwaiter().GetResult()
        Dim ClientPid As UInteger
        If Not GetNamedPipeClientProcessId(Pipe.SafePipeHandle, ClientPid) OrElse ClientPid <> CUInt(Child.Id) Then Throw New IOException("联机组件身份校验失败。")
        Reader = New Thread(AddressOf ReadEvents) With {.IsBackground = True, .Name = "VLan machine events"}
        Reader.Start()
        Finally
            StartupComplete = True
        End Try
    End Sub
    <Runtime.InteropServices.DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function GetNamedPipeClientProcessId(Handle As Microsoft.Win32.SafeHandles.SafePipeHandle, ByRef Pid As UInteger) As Boolean
    End Function
    Private Sub ReadEvents()
        Try
            Do
                Dim Json = ModVlanLink.ParseJson(ModVlanLink.ReadBoundedLine(Pipe, 65536))
                If Json("v")?.Type <> JTokenType.Integer OrElse CInt(Json("v")) <> 1 Then Throw New InvalidDataException()
                SyncLock Gate
                    Select Case CStr(Json("event"))
                        Case "status" : LastStatus = Json
                        Case "error" : If LastError Is Nothing Then LastError = CStr(Json("code"))
                        Case "peer_left" : Departed.Add(CUInt(Json("peerId")))
                        Case "peer_joined" : Departed.Remove(CUInt(Json("peerId")))
                        Case "reconnecting"
                            If LastStatus IsNot Nothing Then LastStatus("ready") = False
                            RaiseEvent Interrupted()
                    End Select
                End SyncLock
            Loop
        Catch
            SyncLock Gate
                If Not Stopping AndAlso LastError Is Nothing Then LastError = "COMPONENT_DISCONNECTED"
            End SyncLock
        End Try
    End Sub
    Friend Sub Send(Op As String, Optional Fields As JObject = Nothing)
        SyncLock Gate
            Dim Json = If(Fields, New JObject())
            RequestNumber += 1
            Json("v") = 1 : Json("requestId") = RequestNumber.ToString(Globalization.CultureInfo.InvariantCulture) : Json("op") = Op
            Dim Data = Encoding.UTF8.GetBytes(Json.ToString(Formatting.None) & vbLf)
            If Data.Length > 16384 Then Throw New InvalidDataException("联机请求过长。")
            Dim Write = Pipe.WriteAsync(Data, 0, Data.Length)
            If Not Write.Wait(3000) Then
                Pipe.Dispose() : Throw New TimeoutException("发送联机操作超时。")
            End If
            Write.GetAwaiter().GetResult()
        End SyncLock
    End Sub
    Friend Function WaitFor(Predicate As Func(Of JObject, Boolean), Milliseconds As Integer, Token As CancellationToken) As JObject
        Dim Clock = Stopwatch.StartNew()
        Do
            Token.ThrowIfCancellationRequested()
            Dim Code = Failure
            If Code IsNot Nothing Then Throw New IOException(ModVlanLink.ErrorText(Code))
            If HasExited Then Throw New IOException("联机辅助进程已退出。")
            Dim Current = Snapshot
            If Current IsNot Nothing AndAlso Predicate(Current) Then Return Current
            If Clock.ElapsedMilliseconds > Milliseconds Then Throw New TimeoutException("联机操作超时，暂时无法确认房间状态，请检查网络后重试。")
            Thread.Sleep(50)
        Loop
    End Function
    Friend Sub BeginStop()
        SyncLock Gate
            If Stopping Then Return
            Stopping = True
        End SyncLock
        ThreadPool.QueueUserWorkItem(Sub() Dispose())
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock Gate
            Stopping = True
        End SyncLock
        Try
            If Pipe IsNot Nothing AndAlso Pipe.IsConnected AndAlso Not HasExited Then Send("shutdown")
        Catch
        End Try
        'Closing IPC also asks the helper to shut down if explicit shutdown was lost.
        Try
            Pipe?.Dispose()
        Catch
        End Try
        'Do not kill a process whose Wintun lifecycle worker is still cleaning up.
        Try
            If Child IsNot Nothing Then Child.WaitForExit(8000)
        Catch
        End Try
    End Sub
End Class
