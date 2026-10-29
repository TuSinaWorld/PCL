Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.Net.Sockets
Imports System.Reflection
Imports System.Threading
Imports PCL

Public Class PageLinkMain
    Private RoomInvite As ModVlanLink.RoomInvite
    Private Shared LinkCancellation As CancellationTokenSource = New CancellationTokenSource()

    '===============================
    '  状态机与前端页面
    '===============================

#Region "状态管理"

    Public Enum LinkStates
        Waiting
        Loading
        Failed
        Finished
    End Enum
    Public Shared LinkState As LinkStates = LinkStates.Waiting

    ''' <summary>
    ''' 切换到指定的状态。
    ''' </summary>
    Private Sub ChangeState(NewState As LinkStates)
        If LinkState = NewState Then Return
        Dim OldState = LinkState
        LinkState = NewState
        Logger.Info($"主状态由 {OldState} 变更为 {NewState}")
        '触发状态切换
        RunInUi(
        Sub()
            Select Case NewState
                Case LinkStates.Waiting
                    SwitchToWaiting(OldState)
                Case LinkStates.Loading
                    SwitchToLoading(OldState)
                Case LinkStates.Failed
                    SwitchToFailed(OldState)
                Case LinkStates.Finished
                    SwitchToFinished(OldState)
            End Select
        End Sub)
    End Sub

    '让加载器的 成功/失败 事件触发状态切换
    Private Sub LoaderInit() Handles Me.Initialized
        PageLoaderInit(Load, PanLoad, PanContent, PanAlways, LinkLoader, AutoRun:=False)
        AddHandler LinkLoader.OnStateChangedUi,
        Sub(Loader As LoaderBase, NewState As LoadState, OldState As LoadState)
            Select Case NewState
                Case LoadState.Finished
                    ChangeState(LinkStates.Finished)
                Case LoadState.Failed
                    Telemetry("联机失败", "Exception", FilterUserName(Loader.Error.GetDisplay(True), "*"))
                    ChangeState(LinkStates.Failed)
            End Select
        End Sub
    End Sub

    '页面动画兼容
    Private Sub UpdatePanelVisibility() Handles Me.PageEnter
        FrmLinkMain.PanSelect.Visibility = If(LinkState = LinkStates.Waiting, Visibility.Visible, Visibility.Collapsed)
        FrmLinkMain.PanFinish.Visibility = If(LinkState = LinkStates.Finished, Visibility.Visible, Visibility.Collapsed)
    End Sub

#End Region

#Region "初始页面 (Waiting)"

    '由任意状态切换到 Waiting
    Private Sub SwitchToWaiting(OldState As LinkStates)
        '加载器与进程状态
        LinkCancellation.Cancel()
        LinkLoader.Cancel()
        SyncLock LinkLoader.LockState
            LinkLoader.State = LoadState.Waiting
        End SyncLock
        ProcessStop()
        '页面切换
        If OldState <> LinkStates.Finished Then
            '从完成页面退出时，PageOnContentExit 结束会触发 PageEnter 事件，这会调用 UpdatePanelVisibility
            '如果在现在提前调用了，会丢失完成页面的退出动画
            UpdatePanelVisibility()
        End If
        PageOnContentExit()
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '创建
    Private Sub Create_MouseLeftButtonUp() Handles PanSelectCreate.MouseLeftButtonUp
        Profile = ModVlanLink.LoadProfile()
        If Profile Is Nothing Then Profile = PageSetupLink.ConfigureServer()
        If Profile Is Nothing Then Return
        '输入端口号
        Dim Port As String = MyMsgBoxInput("输入端口", $"在单人游戏的暂停菜单选择 {vbLQ}对局域网开放{vbRQ}，然后输入端口数字。{vbCrLf}甚至可以输入其他游戏的端口……嗯……",
            ValidateRules:=New Collection(Of Validate) From {New ValidateInteger(1024, 65535)},
            DefaultInput:=DetectGamePort(), HintText:="端口号")
        If Port Is Nothing Then Return
        '基础信息
        IsServerSide = True
        ServerPort = Port
        RoomInvite = Nothing
        Logger.Info($"尝试创建 fork 房间，端口 {ServerPort}")
        '启动
        ChangeState(LinkStates.Loading)
    End Sub

    '加入
    Private Shared LastCode As String = Nothing
    ''' <summary>
    ''' 输入邀请码，切换到联机页并立即加入房间。
    ''' </summary>
    Public Shared Sub Join() Handles PanSelectJoin.MouseLeftButtonUp
        Dim Code As String = MyMsgBoxInput("输入邀请码", "输入房主发给你的 PCLV1 邀请码，双方需使用兼容的 fork。",
            HintText:=If(String.IsNullOrEmpty(LastCode), "", "使用上一次的邀请码"), MaxLength:=ModVlanLink.MaximumInviteLength, Sensitive:=True)
        If Not String.IsNullOrEmpty(LastCode) AndAlso Code IsNot Nothing AndAlso Code = "" Then Code = LastCode
        If Code Is Nothing Then Return
        Join(Code)
    End Sub
    Public Sub JoinInternal(Code As String)
        If Code Is Nothing Then Return
        '基础格式校验
        Dim ValidateResult = ValidateCodeFormat(Code)
        If ValidateResult IsNot Nothing Then
            Hint(ValidateResult, HintType.Red)
            Return
        End If
        RoomInvite = ModVlanLink.DecodeInvite(Code)
        Dim Choice = MyMsgBox("将连接服务器：" & RoomInvite.Server.Host & ":" & RoomInvite.Server.Port & vbCrLf & "邀请码包含服务器连接凭据，请仅使用可信朋友发来的邀请码。", "加入联机房间", "仅本次加入", "加入并记住", "取消")
        If Choice = 3 Then Return
        Profile = RoomInvite.Server
        If Choice = 2 Then ModVlanLink.SaveProfile(Profile)
        '基础信息
        IsServerSide = False
        ServerPort = RoomInvite.GamePort
        Logger.Info("尝试使用 PCLV1 邀请码加入 fork 房间")
        '启动
        LastCode = Code
        ChangeState(LinkStates.Loading)
    End Sub
    Public Shared Function ValidateCodeFormat(Code As String) As String
        Try
            ModVlanLink.DecodeInvite(Code)
            Return Nothing
        Catch ex As Exception
            Return ex.Message
        End Try
    End Function

    '自动加入

    ''' <summary>
    ''' 切换到联机页并立即加入指定房间。
    ''' </summary>
    Public Shared Sub Join(Code As String)
        If LinkState <> LinkStates.Waiting Then
            Hint("你已经在联机房间中了！", HintType.Red)
        ElseIf FrmMain.PageCurrent = FormMain.PageType.Link Then
            FrmLinkMain.JoinInternal(Code)
        Else
            AutoJoinCode = Code
            FrmMain.PageChange(FormMain.PageType.Link, FormMain.PageSubType.LinkMain)
        End If
    End Sub
    '在进入页面时自动尝试加入房间
    Private Shared AutoJoinCode As String = Nothing
    Private Sub PanSelectJoin_Loaded() Handles PanSelectJoin.Loaded
        If AutoJoinCode Is Nothing Then Return
        If LinkState = LinkStates.Waiting Then JoinInternal(AutoJoinCode)
        AutoJoinCode = Nothing
    End Sub

#End Region

#Region "加载页面 (Loading | Failed)"

    '由 Waiting 或 Failed 状态切换到 Loading
    Private Sub SwitchToLoading(OldState As LinkStates)
        '清理变量
        LinkCancellation.Cancel()
        LinkCancellation = New CancellationTokenSource()
        '加载器与进程状态
        LinkLoader.Start(IsForceRestart:=True)
        '页面切换会由 Loader 调用 MyPageRight 来触发
        'UI 更新
        UpdateProgressBar(0)
        LabLoadTitle.Text = If(IsServerSide, "创建房间中", "加入房间中")
        UpdateLoadingPage("正在初始化……", "准备初始化")
    End Sub

    '由 Loading 状态切换到 Failed
    Private Sub SwitchToFailed(OldState As LinkStates)
        '加载器与进程状态
        LinkCancellation.Cancel()
        LinkLoader.Cancel()
        ProcessStop()
        'UI 更新
        UpdateProgressBar(1)
        '显示错误信息
        LabLoadTitle.Text = FailReason
        Dim Brief As String = LinkLoader.Error.GetDisplay(False)
        Dim InnerEx As Exception = LinkLoader.Error
        If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Do Until InnerEx.InnerException Is Nothing
            InnerEx = InnerEx.InnerException
            If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Loop
        LabLoadDesc.Text = If(Brief.StartsWithF("$"), Brief.TrimStart("$"), LinkLoader.Error.GetDisplay(True))
        Logger.Warn(LinkLoader.Error, LabLoadTitle.Text)
    End Sub

    '===============================
    '  页面 UI
    '===============================

    Private Sub UpdateLoadingPage(Title As String, FailBrief As String)
        If FailReason = FailBrief & "失败" Then Return
        FailReason = FailBrief & "失败"
        Logger.Info($"开始步骤：{Title}")
        RunInUiWait(
        Sub()
            If FrmLinkMain Is Nothing OrElse Not FrmLinkMain.LabLoadDesc.IsLoaded Then Return
            FrmLinkMain.LabLoadDesc.Text = Title
        End Sub)
    End Sub
    Private FailReason As String = "准备初始化"

    '取消加载
    Private Sub BtnLoadCancel_Click() Handles BtnLoadCancel.Click
        ChangeState(LinkStates.Waiting)
    End Sub

    '点击重试
    Private Sub CardLoad_MouseLeftButtonUp() Handles CardLoad.MouseLeftButtonUp
        If Not IsServerSide AndAlso Session IsNot Nothing Then
            Select Case Session.Failure
                Case "ROOM_NOT_FOUND", "ROOM_EXPIRED"
                    ChangeState(LinkStates.Waiting)
                    Join()
                    Return
                Case "ROOM_PASSWORD_REQUIRED", "ROOM_PASSWORD_INVALID"
                    Dim Password = MyMsgBoxInput("房间密码", "此密码只用于当前房间，不是服务器鉴权密码。也可取消并输入新的邀请码。", MaxLength:=63, Sensitive:=True, MaskInput:=True)
                    If Password Is Nothing Then Return
                    If Not System.Text.RegularExpressions.Regex.IsMatch(Password, "\A[A-Za-z0-9]{6,63}\z") Then
                        Hint("房间密码须为 6–63 位字母或数字。", HintType.Red) : Return
                    End If
                    RoomInvite.RoomPassword = Password
            End Select
        End If
        ChangeState(LinkStates.Loading)
    End Sub

    '进度条
    Private Sub UpdateProgressBar(Optional Value As Double = -1)
        If Value = -1 Then Value = LinkLoader.Progress
        Dim DisplayingProgress As Double = ColumnProgressA.Width.Value
        If Math.Round(Value - DisplayingProgress, 3) = 0 Then Return
        If DisplayingProgress <= Value Then
            Dim NewProgress As Double = If(Value = 1, 1, DisplayingProgress +
                (Value - DisplayingProgress) ^ 2 * 0.5)
            AniStart({
                AaGridLengthWidth(ColumnProgressA, NewProgress - ColumnProgressA.Width.Value, 200),
                AaGridLengthWidth(ColumnProgressB, (1 - NewProgress) - ColumnProgressB.Width.Value, 200)
            }, "Link Progress")
        Else
            ColumnProgressA.Width = New GridLength(Value, GridUnitType.Star)
            ColumnProgressB.Width = New GridLength(1 - Value, GridUnitType.Star)
            AniStop("Link Progress")
        End If
    End Sub
    Private Sub CardLoad_SizeChanged() Handles CardLoad.SizeChanged
        RectProgressClip.Rect = New Rect(0, 0, CardLoad.ActualWidth, 12)
    End Sub

#End Region

#Region "完成页面 (Finished)"

    '由 Loading 状态切换到 Finished
    Private Sub SwitchToFinished(OldState As LinkStates)
        '由于只可能从加载器的完成事件触发，不需要管加载器
        UpdatePanelVisibility() '页面的实际切换会由 Loader 调用 MyPageRight 来触发

        'UI 更新
        If IsServerSide Then
            LabFinishTitle.Text = "已创建房间"
            LabFinishDesc.Text = "邀请码包含服务器连接凭据，请仅分享给可信好友。" & vbCrLf & "请保持游戏对局域网开放，退出启动器将结束联机。"
            If RoomInvite.RoomPassword = "" Then LabFinishDesc.Text &= vbCrLf & "当前房间无密码，同一服务器的其他已鉴权用户也可能加入。"
            BtnFinishExit.Text = "关闭"
            BtnFinishPing.ToolTip = "网络延迟"
            BtnFinishCopy.Visibility = Visibility.Visible

            '下边栏
            BtnFinishPort.Visibility = Visibility.Visible
            LabFinishPort.Visibility = Visibility.Visible
            BtnFinishIp.Visibility = Visibility.Collapsed
            LabFinishIp.Visibility = Visibility.Collapsed
            LabFinishPort.Text = ServerPort
        Else
            LabFinishTitle.Text = "已加入房间"
            LabFinishDesc.Text = "在 Minecraft 多人游戏列表中查找 PCL VLan 房间；也可复制本机连接地址。"
            BtnFinishExit.Text = "离开"
            BtnFinishPing.ToolTip = "与房主的延迟"
            BtnFinishCopy.Visibility = Visibility.Collapsed
            '下边栏
            BtnFinishPort.Visibility = Visibility.Collapsed
            LabFinishPort.Visibility = Visibility.Collapsed
            LabFinishIp.Text = ClientAddress
            BtnFinishIp.Visibility = Visibility.Visible
            LabFinishIp.Visibility = Visibility.Visible
        End If
        Update() '立即刷新
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '退出
    Private Sub BtnFinishExit_Click() Handles BtnFinishExit.Click
        TryExit(False, False)
    End Sub
    ''' <summary>
    ''' 退出联机。
    ''' 返回是否弹出了警告窗口并且玩家选择了取消。
    ''' </summary>
    Public Function TryExit(Silent As Boolean, Closing As Boolean) As Boolean
        If LinkState = LinkStates.Waiting OrElse LinkState = LinkStates.Failed Then
            If Closing AndAlso Session IsNot Nothing AndAlso Not Session.HasExited Then
                FinishClosingAfterCleanup() : Return True
            End If
            Return False
        End If
        If Not Silent Then
            If IsServerSide AndAlso CurrentPeopleCount > 1 Then
                If MyMsgBox("你确定要关闭联机房间吗？" & vbCrLf & "所有玩家都需要重新输入邀请码才可加入游戏！", "退出联机", "确定", "取消", IsWarn:=True) = 2 Then Return True
            ElseIf Closing Then
                If MyMsgBox(If(IsServerSide, "你确定要关闭联机房间吗？", "你确定要离开联机房间吗？"), "退出联机", "确定", "取消") = 2 Then Return True
            End If
        End If
        ChangeState(LinkStates.Waiting)
        If Closing AndAlso Session IsNot Nothing AndAlso Not Session.HasExited Then
            FinishClosingAfterCleanup()
            Return True
        End If
        Return False
    End Function

    '复制邀请码
    Private Sub Copy() Handles BtnFinishCopy.Click
        Dim CodeText As String = $"在 PCL 启动器中输入邀请码【{GetInviteCode()}】，即可加入联机房间！"
        ClipboardSet(CodeText, False)
        Settings.Set("LinkLastAutoJoinInviteCode", ModVlanLink.Fingerprint(CodeText))
        Hint("已复制邀请码！", HintType.Green)
    End Sub
    Private Function GetInviteCode() As String
        Return ModVlanLink.EncodeInvite(RoomInvite)
    End Function

    '复制 IP
    Private Sub BtnFinishIp_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles BtnFinishIp.MouseLeftButtonUp
        ClipboardSet(ClientAddress, False)
        Hint("已复制服务器地址！", HintType.Green)
    End Sub

#End Region

    '===============================
    ' VLan backend. Room metadata stays in the invitation; the v9 server is unchanged.
    Private Profile As ModVlanLink.ServerProfile
    Private Shared Session As VlanSession
    Private Shared Bridge As VlanGameBridge
    Private Shared ReadOnly BridgeGate As New Object()
    Private IsServerSide As Boolean
    Private ServerPort As Integer
    Private ClientAddress As String
    Private ClosingPending As Boolean
    Private CurrentPeopleCount As Integer = 1
    Private RoomPasswordFailure As Boolean
    Private RevalidationNeeded As Integer
    Private ConnectionEpoch As Integer
    Private WithEvents LinkLoader As New LoaderCombo(Of Integer)("VLan 联机", {
        New LoaderTask(Of Integer, Integer)("准备 VLan 房间", AddressOf InitLaunch) With {.ProgressWeight = 1}
    })
    Private Sub InitLaunch(Task As LoaderTask(Of Integer, Integer))
        Dim Token = LinkCancellation.Token
        If Session IsNot Nothing AndAlso Not Session.HasExited Then
            UpdateLoadingPage("正在等待上一次联机安全清理……", "清理旧连接")
            Dim CleanupClock = Stopwatch.StartNew()
            While Not Session.HasExited
                Token.ThrowIfCancellationRequested()
                If CleanupClock.ElapsedMilliseconds > 60000 Then Throw New IOException("网卡清理尚未完成，请稍后重试；没有启动第二个组件。")
                Thread.Sleep(100)
            End While
        End If
        UpdateLoadingPage("正在校验并准备 VLan 组件……", "准备组件")
        Dim Exe = ModVlanLink.EnsureComponent()
        Token.ThrowIfCancellationRequested()
        Session = New VlanSession()
        Dim OwnedSession = Session
        Interlocked.Exchange(RevalidationNeeded, 0)
        AddHandler OwnedSession.Interrupted, Sub()
                                                If Not Object.ReferenceEquals(Session, OwnedSession) Then Return
                                                SyncLock BridgeGate
                                                    Interlocked.Increment(ConnectionEpoch)
                                                    Bridge?.Dispose()
                                                    Interlocked.Exchange(RevalidationNeeded, 1)
                                                End SyncLock
                                            End Sub
        Try
            UpdateLoadingPage("请在 Windows 管理员授权窗口中允许联机组件运行……", "管理员授权")
            Session.Start(Exe, Token)
            UpdateLoadingPage("正在连接服务器并完成鉴权……", "连接服务器")
            Session.Send("configure", New Newtonsoft.Json.Linq.JObject From {{"host", Profile.Host}, {"port", Profile.Port.ToString()}, {"password", Profile.Password}, {"name", "PCL" & ModVlanLink.RandomToken()}})
            Session.WaitFor(Function(S) CBool(S("authenticated")), 20000, Token)
            UpdateLoadingPage("正在加入房间并准备虚拟网卡……", "准备房间")
            If IsServerSide Then
                Dim Password = If(Profile.NoRoomPassword, "", ModVlanLink.RandomToken())
                Session.Send("create", New Newtonsoft.Json.Linq.JObject From {{"name", "PCL" & ModVlanLink.RandomToken()}, {"roomPassword", Password}, {"compatibility", If(Profile.Compatibility, "true", "false")}})
                Dim Ready = Session.WaitFor(Function(S) CBool(S("ready")), 50000, Token)
                RoomInvite = New ModVlanLink.RoomInvite With {.Server = Profile, .RoomId = CUInt(Ready("roomId")), .HostPeerId = CUInt(Ready("peerId")), .HostIp = CStr(Ready("virtualIp")), .GamePort = ServerPort, .RoomPassword = Password, .SessionKey = ModVlanLink.RandomToken()}
                SyncLock BridgeGate
                    Token.ThrowIfCancellationRequested()
                    If Threading.Volatile.Read(RevalidationNeeded) <> 0 Then Throw New IOException("连接在建房期间发生变化，请等待清理后重试。")
                    Bridge = New VlanGameBridge()
                    Bridge.Host(RoomInvite)
                End SyncLock
                ModVlanLink.ValidateInvite(RoomInvite)
                Try
                    Using Probe As New TcpClient()
                        If Not Probe.ConnectAsync(RoomInvite.HostIp, ServerPort).Wait(3000) Then Throw New IOException()
                    End Using
                Catch
                    Throw New IOException("游戏端口尚未监听，请先在 Minecraft 中对局域网开放。")
                End Try
            Else
                Session.Send("join", New Newtonsoft.Json.Linq.JObject From {{"roomId", RoomInvite.RoomId.ToString()}, {"roomPassword", RoomInvite.RoomPassword}})
                Dim Ready = Session.WaitFor(Function(S) CBool(S("ready")), 50000, Token)
                If CUInt(Ready("roomId")) <> RoomInvite.RoomId Then Throw New IOException("房间会话不匹配，请获取新的邀请码。")
                UpdateLoadingPage("正在验证游戏房主与邀请码会话……", "验证房主")
                VlanGameBridge.VerifyHost(RoomInvite, Token)
                SyncLock BridgeGate
                    Token.ThrowIfCancellationRequested()
                    If Threading.Volatile.Read(RevalidationNeeded) <> 0 Then Throw New IOException("连接在加入期间发生变化，请等待清理后重试。")
                    Bridge = New VlanGameBridge()
                    Bridge.Guest(RoomInvite)
                    ClientAddress = "127.0.0.1:" & Bridge.LocalPort
                End SyncLock
            End If
        Catch
            If Object.ReferenceEquals(Session, OwnedSession) Then ProcessStop() Else OwnedSession.BeginStop()
            Throw
        End Try
    End Sub
    Public Shared Sub ProcessStop()
        SyncLock BridgeGate
            Bridge?.Dispose() : Bridge = Nothing
        End SyncLock
        Session?.BeginStop()
    End Sub
    Private Sub FinishClosingAfterCleanup()
        If ClosingPending Then Return
        ClosingPending = True
        Hint("正在清理联机组件，请稍候……", HintType.Blue)
        RunInNewThread(Sub()
                           Dim Clock = Stopwatch.StartNew()
                           While Session IsNot Nothing AndAlso Not Session.HasExited AndAlso Clock.ElapsedMilliseconds < 60000
                               Thread.Sleep(100)
                           End While
                           RunInUi(Sub()
                                       ClosingPending = False
                                       If Session IsNot Nothing AndAlso Not Session.HasExited Then
                                           MyMsgBox("网卡清理尚未完成，启动器已保留运行。请稍后重试关闭。", "正在清理联机")
                                       Else
                                           FrmMain.EndProgram(False)
                                       End If
                                   End Sub)
                       End Sub, "VLan cleanup before closing")
    End Sub
    Private Function DetectGamePort() As String
        Try
            If McInstanceSelected Is Nothing Then Return ""
            Dim LogPath = System.IO.Path.Combine(McInstanceSelected.PathIndie, "logs", "latest.log")
            If Not System.IO.File.Exists(LogPath) OrElse System.IO.File.GetLastWriteTimeUtc(LogPath) < DateTime.UtcNow.AddHours(-12) Then Return ""
            Dim Lines As New List(Of String)()
            Using Input As New System.IO.FileStream(LogPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite)
                Dim TailOnly = Input.Length > 262144
                If TailOnly Then Input.Seek(-262144, System.IO.SeekOrigin.End)
                Using Reader As New System.IO.StreamReader(Input)
                If TailOnly Then Reader.ReadLine()
                While Not Reader.EndOfStream
                    Lines.Add(Reader.ReadLine())
                    If Lines.Count > 500 Then Lines.RemoveAt(0)
                End While
                End Using
            End Using
            For Each Line In Lines.AsEnumerable().Reverse()
                Dim Match = System.Text.RegularExpressions.Regex.Match(Line, "(?:Started serving on|Local game hosted on port)\s+(\d{4,5})", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                If Match.Success Then Return Match.Groups(1).Value
            Next
        Catch
        End Try
        Return ""
    End Function
    Private Sub StartTimerThread() Handles Me.Loaded
        Static Started As Boolean
        If Started Then Return
        Started = True
        RunInNewThread(Sub()
                           While True
                               Thread.Sleep(1000)
                               Try
                                   If LinkState = LinkStates.Loading Then RunInUi(AddressOf UpdateProgressBar)
                                   If LinkState = LinkStates.Finished Then Update()
                               Catch
                                   'Never include invitation material in a timer failure.
                               End Try
                           End While
                       End Sub, "VLan status")
    End Sub
    Private Sub Update()
        Dim Current = Session?.Snapshot
        If Session Is Nothing Then Return
        If Session.Failure IsNot Nothing OrElse Session.HasExited Then
            RunInUi(Sub()
                        If LinkState <> LinkStates.Finished Then Return
                        MyMsgBox(If(Session.Failure Is Nothing, "联机组件已退出。", ModVlanLink.ErrorText(Session.Failure)), "联机连接已结束")
                        ChangeState(LinkStates.Waiting)
                    End Sub)
            Return
        End If
        If Current Is Nothing Then Return
        If Not IsServerSide AndAlso Session.PeerHasLeft(RoomInvite.HostPeerId) Then
            RunInUi(Sub()
                        If LinkState <> LinkStates.Finished Then Return
                        MyMsgBox("游戏房主已离开，请向房主获取新的邀请码。", "联机结束")
                        ChangeState(LinkStates.Waiting)
                    End Sub)
            Return
        End If
        Dim Members = TryCast(Current("peers"), Newtonsoft.Json.Linq.JArray)
        CurrentPeopleCount = 1 + If(Members Is Nothing, 0, Members.Count)
        If Not CBool(Current("ready")) Then
            RunInUi(Sub() LabFinishPing.Text = "正在重连")
            Return
        End If
        If Not IsServerSide AndAlso (Members Is Nothing OrElse Not Members.Any(Function(P) CUInt(P("peerId")) = RoomInvite.HostPeerId AndAlso CStr(P("virtualIp")) = RoomInvite.HostIp)) Then
            RunInUi(Sub()
                        If LinkState <> LinkStates.Finished Then Return
                        MyMsgBox("原游戏房主已不在房间中，请获取新的邀请码。", "联机结束")
                        ChangeState(LinkStates.Waiting)
                    End Sub)
            Return
        End If
        If Interlocked.CompareExchange(RevalidationNeeded, 2, 1) = 1 Then
            RevalidateBridge(Current)
            Return
        End If
        If Threading.Volatile.Read(RevalidationNeeded) <> 0 Then Return
        If Not IsServerSide Then Bridge?.Broadcast()
        RunInUi(Sub()
                    If LinkState <> LinkStates.Finished Then Return
                    LabFinishPing.Text = If(CurrentPeopleCount = 1, "等待玩家", "已连接")
                    If Members IsNot Nothing AndAlso Members.Count > 0 Then
                        Dim Peer = If(IsServerSide, Members.First, Members.FirstOrDefault(Function(P) CUInt(P("peerId")) = RoomInvite.HostPeerId))
                        If Peer IsNot Nothing Then
                            Dim Rtt = CInt(Peer("rtt"))
                            LabFinishPing.Text = If(Rtt < 0, "采样中", Rtt & " ms")
                            BtnFinishPing.ToolTip = "最近 60 秒探测超时率：" & CDbl(Peer("timeoutPercent")).ToString("0.0") & "%（非原始丢包率）"
                        End If
                    End If
                    Dim Connected = 1 + If(Members Is Nothing, 0, Members.Where(Function(P) CInt(P("rtt")) >= 0 AndAlso Not CBool(P("outage"))).Count())
                    LabFinishPlayer.Text = Connected & "/" & CurrentPeopleCount & " 人"
                    BtnFinishPlayer.ToolTip = "已探测连接 / 房间成员（可能包含正在重连的成员）" & vbCrLf & If(Members Is Nothing, "等待玩家", String.Join(vbCrLf, Members.Select(Function(P) CStr(P("name")) & " · " & CStr(P("virtualIp")))))
                    HintFinish.Text = "通过你的 VLan 服务器中继 · " & If(CStr(Current("tcp")) = "3", "TCP Relay", "Raw UDP / KCP")
                    HintFinish.Visibility = Visibility.Visible
                End Sub)
    End Sub
    Private Sub RevalidateBridge(Current As Newtonsoft.Json.Linq.JObject)
        Dim OwnedSession = Session, Invite = RoomInvite, HostRole = IsServerSide, Token = LinkCancellation.Token
        Dim Epoch = Threading.Volatile.Read(ConnectionEpoch)
        RunInUi(Sub() LabFinishPing.Text = "正在重新验证房主")
        RunInNewThread(Sub()
                           Dim Replacement As VlanGameBridge = Nothing
                           Try
                               Token.ThrowIfCancellationRequested()
                               If CUInt(Current("roomId")) <> Invite.RoomId Then Throw New IOException("原房间已失效，请重新创建或获取新的邀请码。")
                               If HostRole Then
                                   If CUInt(Current("peerId")) <> Invite.HostPeerId OrElse CStr(Current("virtualIp")) <> Invite.HostIp Then Throw New IOException("原房主身份已失效，请重新创建房间。")
                                   Replacement = New VlanGameBridge()
                                   Replacement.Host(Invite)
                               Else
                                   VlanGameBridge.VerifyHost(Invite, Token)
                                   Replacement = New VlanGameBridge()
                                   Replacement.Guest(Invite)
                               End If
                               Token.ThrowIfCancellationRequested()
                               SyncLock BridgeGate
                                   Token.ThrowIfCancellationRequested()
                                   If Not Object.ReferenceEquals(Session, OwnedSession) OrElse Epoch <> Threading.Volatile.Read(ConnectionEpoch) Then Throw New OperationCanceledException()
                                   Bridge = Replacement
                                   Interlocked.Exchange(RevalidationNeeded, 0)
                               End SyncLock
                               If Not HostRole Then
                                   ClientAddress = "127.0.0.1:" & Replacement.LocalPort
                                   RunInUi(Sub() LabFinishIp.Text = ClientAddress)
                               End If
                           Catch Ex As Exception
                               Replacement?.Dispose()
                               If Token.IsCancellationRequested OrElse Not Object.ReferenceEquals(Session, OwnedSession) Then Return
                               If Epoch <> Threading.Volatile.Read(ConnectionEpoch) Then Return
                               RunInUi(Sub()
                                           If LinkState <> LinkStates.Finished Then Return
                                           MyMsgBox("重连后的房间或游戏房主未通过验证，请重新加入。", "联机结束")
                                           ChangeState(LinkStates.Waiting)
                                       End Sub)
                           End Try
                       End Sub, "VLan room revalidation")
    End Sub
End Class
