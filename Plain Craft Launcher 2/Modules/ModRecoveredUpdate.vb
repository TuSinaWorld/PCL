Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text.RegularExpressions

' Recovered from the 2.12.8.2 client flow, adapted to the public 2.99 APIs.
' All download locations come from the owner's explicit recovered configuration.
Friend Module ModRecoveredUpdate

    Private ReadOnly RecoveredUpdateLock As New Object
    Private RecoveredCheckingUpdate As Boolean
    Friend IsUpdating As Boolean
    Friend IsUpdateWaitingRestart As Boolean
    Private RecoveredStagedLauncher As String

    Public ReadOnly Property RecoveredCacheRoot As String
        Get
            Dim Identity = Path.GetFullPath(PathExe).ToUpperInvariant()
            Dim Key As String
            Using Hash = SHA256.Create()
                Key = BitConverter.ToString(Hash.ComputeHash(Encoding.UTF8.GetBytes(Identity))).Replace("-", "").Substring(0, 16)
            End Using
            Return Path.Combine(PathTemp, "RecoveredFork", Key)
        End Get
    End Property

    Public ReadOnly Property RecoveredLatestLauncherPath As String
        Get
            Return Path.Combine(RecoveredCacheRoot, "LatestFork.exe")
        End Get
    End Property

    Friend Function RecoveredIsLauncherNewest() As Boolean?
        Dim Manifest = GetRecoveredUpdateManifest()
        If Manifest Is Nothing Then Return Nothing
        Dim RemoteCode = RecoveredJsonValue(Manifest, "VersionCode", "Code")
        If RemoteCode Is Nothing Then Return Nothing
        Dim ParsedCode As Integer
        If Not Integer.TryParse(RemoteCode.ToString(), Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, ParsedCode) OrElse ParsedCode < 0 Then Return Nothing
        Return ParsedCode <= VersionCode
    End Function

    Friend Sub UpdateCheckByButton()
        SyncLock RecoveredUpdateLock
            If RecoveredCheckingUpdate Then
                Hint("正在检查更新，请稍候……")
                Return
            End If
            RecoveredCheckingUpdate = True
        End SyncLock
        RunInThread(
        Sub()
            Try
                If IsUpdateWaitingRestart Then
                    UpdateRestart(True)
                    Return
                End If
                Hint("正在检查自定义更新源……")
                ServerLoader.WaitForExit(IsForceRestart:=True)
                Dim Newest = RecoveredIsLauncherNewest()
                If Newest = True Then
                    Hint("当前 fork 已是更新源中的最新版本。", HintType.Green)
                ElseIf Newest = False Then
                    If MyMsgBox("更新源提供了新版本，是否下载并安装？", "启动器更新", "下载更新", "取消") = 1 Then UpdateStart(False)
                ElseIf GetRecoveredUpdateManifest() IsNot Nothing AndAlso RecoveredHasUpdateLocation() Then
                    If MyMsgBox("更新源未提供可比较的版本编号，是否下载指定的更新？", "启动器更新", "下载更新", "取消") = 1 Then UpdateStart(False)
                Else
                    Hint("尚未配置可用的 fork 更新源。", HintType.Red)
                End If
            Catch ex As Exception
                Logger.Error(ex, "检查自定义更新源失败", LogBehavior.Toast)
            Finally
                SyncLock RecoveredUpdateLock
                    RecoveredCheckingUpdate = False
                End SyncLock
            End Try
        End Sub)
    End Sub

    Friend Sub UpdateStart(Silent As Boolean, Optional UrlsOverride As IEnumerable(Of String) = Nothing)
        If Silent AndAlso Not RecoveredConfigBoolean("PCL_UPDATE_ALLOW_AUTOMATIC", False) Then
            Logger.Info("自定义更新源请求后台更新，但本地未启用自动更新。")
            Return
        End If
        SyncLock RecoveredUpdateLock
            If IsUpdating Then
                If Not Silent Then Hint("更新正在下载中……")
                Return
            End If
            If IsUpdateWaitingRestart Then
                If Not Silent Then RunInThread(Sub() UpdateRestart(True))
                Return
            End If
            IsUpdating = True
        End SyncLock
        Dim Manifest As JObject = Nothing
        Dim PackagePath As String = Nothing
        Dim StagePath As String = Nothing
        Dim Extension As String = Nothing
        Dim UpdateLoader As LoaderCombo(Of String) = Nothing
        Try
            Dim Prepare As New LoaderTask(Of String, List(Of NetFile))("解析 fork 更新信息",
            Sub(Task)
                If ServerLoader.State = LoadState.Loading Then ServerLoader.WaitForExit()
                Manifest = GetRecoveredUpdateManifest()
                If Manifest Is Nothing Then Manifest = New JObject()
                Dim Urls = RecoveredUpdateUrls(Manifest, UrlsOverride)
                If Urls.Count = 0 Then Throw New InvalidOperationException("未配置更新下载地址。请设置 PCL_UPDATE_BASE_URL，或在自建服务配置中提供 DownloadUrls。")
                Extension = Path.GetExtension(New Uri(Urls(0)).AbsolutePath).ToLowerInvariant()
                Dim FormatValue = RecoveredJsonValue(Manifest, "Format")
                If FormatValue IsNot Nothing AndAlso Extension <> ".delta" Then Extension = "." & FormatValue.ToString().TrimStart("."c).ToLowerInvariant()
                If Extension <> ".exe" AndAlso Extension <> ".zip" AndAlso Extension <> ".delta" Then Throw New InvalidDataException("更新只支持 exe、zip 或带基准散列的 delta 文件。")
                PackagePath = Path.Combine(RequestTaskTempFolder(), "ForkUpdate" & Extension)
                Dim StageDirectory = Path.Combine(Path.GetDirectoryName(PathExe), "PCL", "RecoveredUpdate")
                Directory.CreateDirectory(StageDirectory)
                StagePath = Path.Combine(StageDirectory, Guid.NewGuid().ToString("N") & ".exe")
                Dim Hash = RecoveredPackageHash(Manifest, Extension = ".delta")
                Task.Output = New List(Of NetFile) From {New NetFile(Urls, PackagePath, New FileChecker With {.MinSize = 1, .Hash = Hash})}
            End Sub) With {.ProgressWeight = 0.1}
            Dim Download As New LoaderDownload("下载 fork 更新", New List(Of NetFile)) With {.ProgressWeight = 1}
            Dim PrepareRestart As New LoaderTask(Of String, Integer)("准备 fork 更新替换",
            Sub(Task)
                RecoveredPrepareExecutable(PackagePath, Extension, StagePath, Manifest)
                SyncLock RecoveredUpdateLock
                    RecoveredStagedLauncher = StagePath
                    IsUpdateWaitingRestart = True
                End SyncLock
                Task.Output = 1
            End Sub) With {.ProgressWeight = 0.1}
            UpdateLoader = New LoaderCombo(Of String)("更新 fork 启动器", New LoaderBase() {Prepare, Download, PrepareRestart})
            UpdateLoader.OnStateChanged =
            Sub(Loader)
                If Loader.State = LoadState.Finished OrElse Loader.State = LoadState.Failed OrElse Loader.State = LoadState.Canceled Then
                    SyncLock RecoveredUpdateLock
                        IsUpdating = False
                    End SyncLock
                End If
                If Loader.State = LoadState.Failed Then
                    Logger.Error(Loader.Error, "下载或准备 fork 更新失败", If(Silent, LogBehavior.ToastIfDebug, LogBehavior.Toast))
                ElseIf Loader.State = LoadState.Finished AndAlso Not Silent Then
                    Hint("更新已准备好，退出启动器时将完成替换。", HintType.Green)
                    If McLaunchLoader.State <> LoadState.Loading AndAlso Not HasDownloadingTask() AndAlso
                       MyMsgBox("更新已准备好，是否立即重启启动器？", "更新完成", "立即重启", "稍后重启") = 1 Then UpdateRestart(True)
                End If
            End Sub
            If Not Silent Then
                LoaderTaskbarAdd(UpdateLoader)
                RunInUi(
                Sub()
                    If FrmMain IsNot Nothing Then FrmMain.BtnExtraDownload.ShowRefresh()
                End Sub)
            End If
            UpdateLoader.Start()
        Catch ex As Exception
            SyncLock RecoveredUpdateLock
                IsUpdating = False
            End SyncLock
            Logger.Error(ex, "创建 fork 更新任务失败", If(Silent, LogBehavior.ToastIfDebug, LogBehavior.Toast))
        End Try
    End Sub

    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
        Try
            Dim Staged As String
            SyncLock RecoveredUpdateLock
                If Not IsUpdateWaitingRestart OrElse String.IsNullOrEmpty(RecoveredStagedLauncher) Then Return
                Staged = RecoveredStagedLauncher
            End SyncLock
            If TriggerRestartAndByEnd AndAlso (McLaunchLoader.State = LoadState.Loading OrElse HasDownloadingTask()) Then
                Hint("更新已准备好，将在下载或游戏启动任务结束后退出时完成替换。")
                Return
            End If
            RecoveredValidateExecutable(Staged, Nothing)
            Dim Arguments = "--update " & Process.GetCurrentProcess().Id.ToString(Globalization.CultureInfo.InvariantCulture) &
                            " " & RecoveredQuoteArgument(Path.GetFullPath(PathExe)) &
                            " " & RecoveredQuoteArgument(Path.GetFullPath(PathExe)) &
                            " " & TriggerRestartAndByEnd.ToString()
            Dim Helper As New ProcessStartInfo(Staged, Arguments) With {
                .WorkingDirectory = Path.GetDirectoryName(Staged), .UseShellExecute = False,
                .CreateNoWindow = True, .WindowStyle = ProcessWindowStyle.Hidden}
            Process.Start(Helper)
            SyncLock RecoveredUpdateLock
                IsUpdateWaitingRestart = False
            End SyncLock
            If TriggerRestartAndByEnd Then RunInUi(Sub() FrmMain.EndProgram(False))
        Catch ex As Exception
            Logger.Error(ex, "启动 fork 更新替换程序失败；暂存更新仍保留", LogBehavior.Alert)
        End Try
    End Sub

    ' Called only in the newly downloaded launcher's --update command-line mode.
    ' The old process is allowed to exit normally; unrelated processes are never killed.
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        Try
            Dim HelperPath = Path.GetFullPath(PathExe)
            Dim TargetDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(HelperPath), "..", ".."))
            Dim OldPath = Path.GetFullPath(If(Path.IsPathRooted(OldFileName), OldFileName, Path.Combine(TargetDirectory, OldFileName)))
            Dim NewPath = Path.GetFullPath(If(Path.IsPathRooted(NewFileName), NewFileName, Path.Combine(TargetDirectory, NewFileName)))
            If Not String.Equals(Path.GetDirectoryName(OldPath), Path.GetDirectoryName(NewPath), StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(Path.GetExtension(NewPath), ".exe", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(HelperPath, NewPath, StringComparison.OrdinalIgnoreCase) Then Throw New InvalidOperationException("更新目标路径无效。")
            Try
                Using OldProcess = Process.GetProcessById(ProcessId)
                    If OldProcess.Id = Process.GetCurrentProcess().Id Then Throw New InvalidOperationException("更新程序不能替换自身。")
                    If Not OldProcess.WaitForExit(60000) Then Throw New TimeoutException("原启动器仍在运行，已取消替换。")
                End Using
            Catch ex As ArgumentException
                ' The original process has already exited.
            End Try
            RecoveredValidateExecutable(HelperPath, Nothing)
            Dim PendingPath = NewPath & ".recovered-new"
            Dim BackupPath = OldPath & ".recovered-backup"
            File.Copy(HelperPath, PendingPath, True)
            Dim LastFailure As Exception = Nothing
            Dim Replaced As Boolean
            For Attempt = 0 To 5
                Try
                    If File.Exists(NewPath) Then
                        File.Replace(PendingPath, NewPath, BackupPath, True)
                    Else
                        File.Move(PendingPath, NewPath)
                    End If
                    Replaced = True
                    Exit For
                Catch ex As IOException
                    LastFailure = ex
                    Thread.Sleep(250 * (Attempt + 1))
                End Try
            Next
            If Not Replaced Then Throw New IOException("无法替换启动器，原文件和暂存更新已保留。", LastFailure)
            If Not String.Equals(OldPath, NewPath, StringComparison.OrdinalIgnoreCase) AndAlso File.Exists(OldPath) Then
                If File.Exists(BackupPath) Then File.Delete(BackupPath)
                File.Move(OldPath, BackupPath)
            End If
            If TriggerRestart Then Process.Start(New ProcessStartInfo(NewPath, "--wait") With {.WorkingDirectory = Path.GetDirectoryName(NewPath), .UseShellExecute = False})
        Catch ex As Exception
            MsgBox("fork 更新替换失败，原启动器和更新文件将保留。" & vbCrLf & ex.Message, MsgBoxStyle.Critical, "更新失败")
        End Try
    End Sub

    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        Directory.CreateDirectory(RecoveredCacheRoot)
        If ServerConfig Is Nothing AndAlso Not String.IsNullOrWhiteSpace(GetRecoveredSetting("PCL_SERVER_CONFIG_URL", "")) Then
            ServerLoader.WaitForExit(LoaderToSyncProgress:=LoaderToSyncProgress)
        End If
        Dim Manifest = GetRecoveredUpdateManifest(True)
        Dim Urls = RecoveredUpdateUrls(Manifest, Nothing, AllowPatch:=False)
        If Manifest Is Nothing OrElse Urls.Count = 0 Then
            ' Offline exports embed this fork, never a silently fetched official launcher.
            File.Copy(PathExe, RecoveredLatestLauncherPath, True)
            Logger.Info("未配置整合包用启动器更新源，使用当前 fork 构建。")
            Return
        End If
        Dim CacheInfoPath = Path.Combine(RecoveredCacheRoot, "LatestFork.manifest.json")
        Dim Identity = Manifest.ToString(Formatting.None)
        If File.Exists(RecoveredLatestLauncherPath) AndAlso File.Exists(CacheInfoPath) AndAlso File.ReadAllText(CacheInfoPath) = Identity Then
            RecoveredValidateExecutable(RecoveredLatestLauncherPath, RecoveredJsonText(Manifest, "ExecutableSha256"))
            Return
        End If
        Dim Extension = Path.GetExtension(New Uri(Urls(0)).AbsolutePath).ToLowerInvariant()
        Dim FormatValue = RecoveredJsonValue(Manifest, "Format")
        If FormatValue IsNot Nothing Then Extension = "." & FormatValue.ToString().TrimStart("."c).ToLowerInvariant()
        If Extension <> ".exe" AndAlso Extension <> ".zip" Then Throw New InvalidDataException("整合包导出用启动器必须提供完整的 exe 或 zip 文件。")
        Dim PackagePath = Path.Combine(RequestTaskTempFolder(), "ExportLauncher" & Extension)
        NetDownloadByLoader(Urls, PackagePath, LoaderToSyncProgress, New FileChecker With {.MinSize = 1, .Hash = RecoveredPackageHash(Manifest)})
        Dim Prepared = RecoveredLatestLauncherPath & ".pending"
        RecoveredPrepareExecutable(PackagePath, Extension, Prepared, Manifest)
        File.Copy(Prepared, RecoveredLatestLauncherPath, True)
        File.Delete(Prepared)
        File.WriteAllText(CacheInfoPath, Identity, New UTF8Encoding(False))
    End Sub

    Friend Function GetRecoveredUpdateManifest(Optional ReleaseOnly As Boolean = False) As JObject
        Dim Root = ServerConfig
        Dim Updates = TryCast(RecoveredJsonValue(Root, "Update", "Updates"), JObject)
        Dim Manifest = TryCast(RecoveredJsonValue(Updates, If(ReleaseOnly, "Release", BuildType.ToString()), "Release"), JObject)
        If Manifest Is Nothing AndAlso Updates IsNot Nothing AndAlso RecoveredJsonValue(Updates, "DownloadUrls", "Urls", "Url") IsNot Nothing Then Manifest = Updates
        If Manifest Is Nothing Then
            Dim BaseUrl = GetRecoveredSetting("PCL_UPDATE_BASE_URL", "").Trim()
            Dim Address As Uri = Nothing
            If Uri.TryCreate(BaseUrl, UriKind.Absolute, Address) AndAlso
               (Path.GetExtension(Address.AbsolutePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) OrElse Path.GetExtension(Address.AbsolutePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)) Then
                Manifest = New JObject(New JProperty("DownloadUrls", New JArray(BaseUrl)))
            End If
        End If
        Return If(Manifest Is Nothing, Nothing, CType(Manifest.DeepClone(), JObject))
    End Function

    Private Function RecoveredHasUpdateLocation() As Boolean
        Return RecoveredUpdateUrls(GetRecoveredUpdateManifest(), Nothing).Count > 0
    End Function

    Private Function RecoveredUpdateUrls(Manifest As JObject, OverrideUrls As IEnumerable(Of String), Optional AllowPatch As Boolean = True) As List(Of String)
        Dim UrlValues As IEnumerable(Of String) = OverrideUrls
        If UrlValues Is Nothing AndAlso Manifest IsNot Nothing Then
            Dim UrlToken = RecoveredJsonValue(Manifest, "DownloadUrls", "Urls", "Url")
            Dim PatchToken = RecoveredJsonValue(Manifest, "PatchUrls")
            Dim BaseHash = RecoveredJsonText(Manifest, "BaseSha256")
            If AllowPatch AndAlso PatchToken IsNot Nothing AndAlso Regex.IsMatch(BaseHash, "\A[0-9a-fA-F]{64}\z") AndAlso
               String.Equals(RecoveredFileHash(PathExe), BaseHash, StringComparison.OrdinalIgnoreCase) Then UrlToken = PatchToken
            If UrlToken IsNot Nothing Then
                UrlValues = If(TypeOf UrlToken Is JArray, UrlToken.Values(Of String)(), New String() {UrlToken.ToString()})
            End If
        End If
        If UrlValues Is Nothing Then Return New List(Of String)
        Return UrlValues.Where(Function(Value) Not String.IsNullOrWhiteSpace(Value)).Select(Function(Value) RecoveredResolveUpdateUrl(ArgumentReplace(Value, AddressOf Uri.EscapeDataString))).Distinct().ToList()
    End Function

    Private Function RecoveredResolveUpdateUrl(Value As String) As String
        Dim Address As Uri = Nothing
        If Not Uri.TryCreate(Value, UriKind.Absolute, Address) Then
            Dim BaseAddress As Uri = Nothing
            If Not Uri.TryCreate(GetRecoveredSetting("PCL_UPDATE_BASE_URL", ""), UriKind.Absolute, BaseAddress) OrElse
               Not Uri.TryCreate(BaseAddress, Value, Address) Then Throw New InvalidDataException("相对更新地址需要配置 PCL_UPDATE_BASE_URL。")
        End If
        If (Address.Scheme <> Uri.UriSchemeHttps AndAlso Address.Scheme <> Uri.UriSchemeHttp) OrElse Not String.IsNullOrEmpty(Address.UserInfo) Then Throw New InvalidDataException("更新地址必须是 HTTP(S) URL。")
        Return Address.AbsoluteUri
    End Function

    Private Function RecoveredPackageHash(Manifest As JObject, Optional IsPatch As Boolean = False) As String
        Dim ConfigHash = If(IsPatch, RecoveredJsonText(Manifest, "PatchSha256"), RecoveredJsonText(Manifest, "Sha256", "Hash"))
        Dim Hash = GetRecoveredSetting("PCL_UPDATE_SHA256", ConfigHash)
        If String.IsNullOrWhiteSpace(Hash) Then Return Nothing
        If Not Regex.IsMatch(Hash, "\A[0-9a-fA-F]{64}\z") Then Throw New InvalidDataException("更新文件的 SHA256 格式无效。")
        Return Hash
    End Function

    Private Sub RecoveredPrepareExecutable(Package As String, Extension As String, Destination As String, Manifest As JObject)
        If Extension = ".zip" Then
            Using Archive = ZipFile.OpenRead(Package)
                Dim EntryName = RecoveredJsonText(Manifest, "Executable")
                Dim Entries = Archive.Entries.Where(Function(Item) Item.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList()
                Dim Entry As ZipArchiveEntry = Nothing
                If Not String.IsNullOrEmpty(EntryName) Then
                    Entry = Entries.FirstOrDefault(Function(Item) String.Equals(Item.FullName.Replace("\", "/"), EntryName.Replace("\", "/"), StringComparison.OrdinalIgnoreCase))
                Else
                    Entry = Entries.FirstOrDefault(Function(Item) String.Equals(Item.Name, Path.GetFileName(PathExe), StringComparison.OrdinalIgnoreCase))
                    If Entry Is Nothing AndAlso Entries.Count = 1 Then Entry = Entries(0)
                End If
                If Entry Is Nothing Then Throw New InvalidDataException("更新压缩包中无法确定启动器，请设置 Executable 字段。")
                Using Input = Entry.Open(), Output = File.Create(Destination)
                    Input.CopyTo(Output)
                End Using
            End Using
        ElseIf Extension = ".delta" Then
            Dim BaseHash = RecoveredJsonText(Manifest, "BaseSha256")
            If String.IsNullOrEmpty(BaseHash) OrElse Not String.Equals(BaseHash, RecoveredFileHash(PathExe), StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("增量补丁与当前 fork 构建不匹配。")
            MsDelta.Apply(PathExe, Package, Destination)
        ElseIf Extension = ".exe" Then
            File.Copy(Package, Destination, True)
        Else
            Throw New InvalidDataException("不支持的更新格式。")
        End If
        RecoveredValidateExecutable(Destination, RecoveredJsonText(Manifest, "ExecutableSha256"))
    End Sub

    Private Sub RecoveredValidateExecutable(FilePath As String, ExpectedHash As String)
        If Not File.Exists(FilePath) OrElse New FileInfo(FilePath).Length < 1024 Then Throw New InvalidDataException("更新后的启动器文件为空或过小。")
        ' GetAssemblyName inspects metadata and does not execute the downloaded assembly.
        AssemblyName.GetAssemblyName(FilePath)
        If Not String.IsNullOrWhiteSpace(ExpectedHash) AndAlso
           (Not Regex.IsMatch(ExpectedHash, "\A[0-9a-fA-F]{64}\z") OrElse Not String.Equals(RecoveredFileHash(FilePath), ExpectedHash, StringComparison.OrdinalIgnoreCase)) Then Throw New InvalidDataException("更新后的启动器 SHA256 不匹配。")
    End Sub

    Private Function RecoveredFileHash(FilePath As String) As String
        Using Input = File.OpenRead(FilePath), Hash = SHA256.Create()
            Return BitConverter.ToString(Hash.ComputeHash(Input)).Replace("-", "")
        End Using
    End Function

    Private Function RecoveredQuoteArgument(Value As String) As String
        If Value.IndexOf(""""c) >= 0 OrElse Value.IndexOfAny(New Char() {ChrW(0), ChrW(10), ChrW(13)}) >= 0 Then Throw New ArgumentException("更新参数含无效字符。")
        Return """" & Value & """"
    End Function

End Module
