Imports System.ComponentModel
Imports System.Runtime.InteropServices
Imports System.Security.Principal

Public Class PageOtherTest
    Private IsLoad As Boolean
    Private Shared CleanupRunning As Integer
    Private Shared MemoryRunning As Integer

    Private Sub MeLoaded() Handles Me.Loaded
        If IsLoad Then Return
        IsLoad = True
        TextDownloadUrl.ValidateRules.Add(New ValidateFunc(AddressOf ValidateDownloadUrl))
        TextDownloadName.ValidateRules.Add(New ValidateFunc(AddressOf ValidateDownloadName))
        TextDownloadFolder.ValidateRules.Add(New ValidateFunc(AddressOf ValidateDownloadFolder))
        Dim Folder = Settings.Get(Of String)("CacheDownloadFolder")
        TextDownloadFolder.Text = If(String.IsNullOrWhiteSpace(Folder), Path.Combine(Paths.Base, "PCL", "MyDownload"), Folder)
        LabCave.Text = GetRandomCave()
        LabHint.Text = GetRandomHint()
        StartButtonRefresh()
    End Sub

    Private Sub StartButtonRefresh() Handles TextDownloadUrl.TextChanged, TextDownloadName.TextChanged, TextDownloadFolder.TextChanged
        If Not IsLoad OrElse BtnDownloadStart Is Nothing Then Return
        TextDownloadUrl.Validate()
        TextDownloadName.Validate()
        TextDownloadFolder.Validate()
        BtnDownloadStart.IsEnabled = TextDownloadUrl.IsValidated AndAlso TextDownloadName.IsValidated AndAlso TextDownloadFolder.IsValidated
        BtnDownloadOpen.IsEnabled = TextDownloadFolder.IsValidated
    End Sub

    Private Sub TextDownloadUrl_TextChanged(sender As Object, e As TextChangedEventArgs) Handles TextDownloadUrl.TextChanged
        If Not IsLoad OrElse Not String.IsNullOrWhiteSpace(TextDownloadName.Text) Then Return
        Dim Address As Uri = Nothing
        If Uri.TryCreate(TextDownloadUrl.Text.Trim(), UriKind.Absolute, Address) Then
            Dim Name = Uri.UnescapeDataString(Path.GetFileName(Address.AbsolutePath))
            If ValidateDownloadName(Name) = "" Then TextDownloadName.Text = Name
        End If
    End Sub

    Private Sub TextDownloadUrl_KeyDown(sender As Object, e As KeyEventArgs) Handles TextDownloadUrl.KeyDown, TextDownloadName.KeyDown, TextDownloadFolder.KeyDown
        If e.Key = Key.Enter AndAlso BtnDownloadStart.IsEnabled Then
            e.Handled = True
            BtnDownloadStart_Click()
        End If
    End Sub

    Private Shared Function ValidateDownloadUrl(Value As String) As String
        Dim Address As Uri = Nothing
        If String.IsNullOrWhiteSpace(Value) OrElse Not Uri.TryCreate(Value.Trim(), UriKind.Absolute, Address) OrElse
            (Address.Scheme <> Uri.UriSchemeHttp AndAlso Address.Scheme <> Uri.UriSchemeHttps) OrElse String.IsNullOrWhiteSpace(Address.Host) Then
            Return "请输入完整的 HTTP 或 HTTPS 下载地址。"
        End If
        Return ""
    End Function

    Private Shared Function ValidateDownloadName(Value As String) As String
        If String.IsNullOrWhiteSpace(Value) Then Return "请输入文件名。"
        If Value.Length > 200 OrElse Value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 OrElse
            Value.EndsWith(".") OrElse Value.EndsWith(" ") OrElse Value = "." OrElse Value = ".." Then Return "文件名包含无效字符或过长。"
        Dim Stem = Value.Split("."c)(0)
        If System.Text.RegularExpressions.Regex.IsMatch(Stem, "^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) Then Return "不能使用 Windows 保留的设备名称。"
        Return ""
    End Function

    Private Shared Function ValidateDownloadFolder(Value As String) As String
        Try
            If String.IsNullOrWhiteSpace(Value) Then Return "请输入完整的目标文件夹路径。"
            Dim Normalized = Value.Replace("/", "\")
            '当前下载引擎要求盘符路径，不支持 UNC 网络共享。
            If Normalized.Length < 3 OrElse Not Char.IsLetter(Normalized(0)) OrElse Normalized(1) <> ":"c OrElse Normalized(2) <> "\"c Then Return "请选择本地磁盘中的完整文件夹路径。"
            Dim FullPath = Path.GetFullPath(Normalized)
            If File.Exists(FullPath) Then Return "该路径是文件，请选择文件夹。"
            Return ""
        Catch ex As Exception
            Return "文件夹路径无效。"
        End Try
    End Function

    Private Sub BtnDownloadStart_Click() Handles BtnDownloadStart.Click
        If Not BtnDownloadStart.IsEnabled Then Return
        Settings.Set("CacheDownloadFolder", TextDownloadFolder.Text)
        StartCustomDownload(TextDownloadUrl.Text.Trim(), TextDownloadName.Text, TextDownloadFolder.Text)
    End Sub

    Public Shared Sub StartCustomDownload(Url As String, FileName As String, Optional Folder As String = Nothing)
        Try
            Dim Validation = ValidateDownloadUrl(Url)
            If Validation <> "" Then Throw New ArgumentException(Validation)
            Validation = ValidateDownloadName(FileName)
            If Validation <> "" Then Throw New ArgumentException(Validation)
            If String.IsNullOrWhiteSpace(Folder) Then
                Dim Selected = RunInUiWait(Function() Dialogs.SaveFile("选择文件保存位置", FileName))
                If String.IsNullOrWhiteSpace(Selected) Then Return
                FileName = Path.GetFileName(Selected)
                Folder = Path.GetDirectoryName(Selected)
            End If
            Validation = ValidateDownloadName(FileName)
            If Validation <> "" Then Throw New ArgumentException(Validation)
            Validation = ValidateDownloadFolder(Folder)
            If Validation <> "" Then Throw New ArgumentException(Validation)
            Folder = Path.GetFullPath(Folder)
            Dim Target = Path.GetFullPath(Path.Combine(Folder, FileName))
            If File.Exists(Target) AndAlso RunInUiWait(Function() MyMsgBox("目标文件已存在：" & vbCrLf & Target & vbCrLf & vbCrLf & "是否重新下载并覆盖？", "覆盖文件", "覆盖", "取消", IsWarn:=True)) <> 1 Then Return
            Directory.CreateDirectory(Folder)
            CheckPermissionWithException(Folder)
            Dim Download As New LoaderDownload("下载 " & FileName, New List(Of NetFile) From {
                New NetFile({Url.Trim()}, Target, SimulateBrowserHeaders:=True)
            })
            Dim Loader As New LoaderCombo(Of Integer)("自定义下载 " & FileName & " (" & GetUuid() & ")", New LoaderBase() {Download}) With {
                .OnStateChanged = AddressOf LoaderStateChangedHintOnly
            }
            Loader.Start()
            LoaderTaskbarAdd(Loader)
            RunInUi(
            Sub()
                FrmMain?.BtnExtraDownload.ShowRefresh()
                FrmMain?.BtnExtraDownload.Ribble()
            End Sub)
            Hint("已添加下载任务：" & FileName, HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "开始自定义下载失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub BtnDownloadSelect_Click() Handles BtnDownloadSelect.Click
        Dim Folder = Dialogs.SelectFolder("选择目标文件夹", False).FirstOrDefault()
        If Folder IsNot Nothing Then
            TextDownloadFolder.Text = Folder
            Settings.Set("CacheDownloadFolder", Folder)
        End If
    End Sub

    Private Sub BtnDownloadOpen_Click() Handles BtnDownloadOpen.Click
        Try
            If ValidateDownloadFolder(TextDownloadFolder.Text) <> "" Then Return
            Directory.CreateDirectory(TextDownloadFolder.Text)
            StartProcess(Path.GetFullPath(TextDownloadFolder.Text))
        Catch ex As Exception
            Logger.Warn(ex, "打开下载文件夹失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub BtnJrrp_Click() Handles BtnJrrp.Click
        Jrrp()
    End Sub

    Public Shared Sub Jrrp()
        '按本地日期和设备识别码生成稳定结果，不使用官方校验盐或网络服务。
        Dim Input = Date.Today.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture) & ":" & Identify
        Dim Value As Integer
        Using Hash = System.Security.Cryptography.SHA256.Create()
            Value = CInt(BitConverter.ToUInt32(Hash.ComputeHash(Encoding.UTF8.GetBytes(Input)), 0) Mod 1001UI)
        End Using
        Dim Score = If(Value >= 970, 100, CInt(Math.Round(Value / 969.0 * 99)))
        Dim Comment As String
        Select Case Score
            Case 100
                Comment = "！今天也是充满好运的一天！"
            Case Is >= 90
                Comment = "！好评如潮！"
            Case Is >= 65
                Comment = "！今天运气不错呢！"
            Case Is >= 50
                Comment = "！还行啦，还行啦。"
            Case Is >= 20
                Comment = "。明天会更好！"
            Case Else
                Comment = "。随机数不影响现实中的好运气。"
        End Select
        RunInUi(Sub() MyMsgBox("你今天的人品值是：" & Score & Comment, "今日人品 - " & Date.Today.ToString("yyyy/M/d"), IsWarn:=Score < 20))
    End Sub

    Private Sub BtnClear_Click() Handles BtnClear.Click
        RubbishClear()
    End Sub

    Public Shared Sub RubbishClear()
        If Interlocked.CompareExchange(CleanupRunning, 1, 0) <> 0 Then
            Hint("缓存清理正在进行，请稍候。")
            Return
        End If
        RunInNewThread(
        Sub()
            Try
                If HasRunningMinecraft OrElse McLaunchLoader.State = LoadState.Loading Then
                    Hint("请先关闭运行中的游戏。")
                    Return
                End If
                If HasDownloadingTask() Then
                    Hint("请等待所有下载任务完成后再清理。")
                    Return
                End If
                Dim Root = GetCleanupRoot()
                Dim Targets = New String() {Path.Combine(Root, "Cache"), Path.Combine(Root, "Download")}
                Dim Caption = "将清理以下 PCL 缓存和下载临时文件夹：" & vbCrLf & String.Join(vbCrLf, Targets) &
                    vbCrLf & vbCrLf & "请确认其中没有自行保存的重要文件。正在使用的文件会跳过，建议清理后重启 PCL。"
                If RunInUiWait(Function() MyMsgBox(Caption, "清理 PCL 缓存", "清理", "取消", IsWarn:=True)) <> 1 Then Return
                If HasRunningMinecraft OrElse McLaunchLoader.State = LoadState.Loading OrElse HasDownloadingTask() Then
                    Hint("游戏或下载任务已经开始，本次清理已取消。")
                    Return
                End If
                RunInUi(Sub() SetClearButtonEnabled(False))
                Dim Count As Integer = 0, Skipped As Integer = 0, Bytes As Long = 0
                For Each Target In Targets
                    DeleteCacheContents(Root, Target, Count, Skipped, Bytes)
                Next
                Logger.Info($"PCL 缓存清理完成：删除 {Count} 个文件，跳过 {Skipped} 项")
                Hint($"已清理 {StringUtils.FormatByteSize(Bytes)}，删除 {Count} 个文件" & If(Skipped > 0, $"，跳过 {Skipped} 个使用中或受保护的项目。", "。"), HintType.Green)
            Catch ex As Exception
                Logger.Warn(ex, "清理 PCL 缓存失败", LogBehavior.Toast)
            Finally
                Interlocked.Exchange(CleanupRunning, 0)
                RunInUi(Sub() SetClearButtonEnabled(True))
            End Try
        End Sub, "Toolbox Cache Cleanup", ThreadPriority.BelowNormal)
    End Sub

    Private Shared Sub SetClearButtonEnabled(Enabled As Boolean)
        If FrmOtherTest?.BtnClear IsNot Nothing Then FrmOtherTest.BtnClear.IsEnabled = Enabled
    End Sub

    Private Shared Function GetCleanupRoot() As String
        Dim Root = Path.GetFullPath(PathTemp).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        Dim Expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PCL")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        If Not String.Equals(Root, Expected, StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException("当前使用自定义缓存目录，请手动检查并清理该目录：" & vbCrLf & Root)
        End If
        EnsureNoReparsePoints(Root)
        Return Root
    End Function

    Private Shared Sub EnsureNoReparsePoints(Target As String)
        Dim Current As New DirectoryInfo(Target)
        While Current IsNot Nothing
            If Current.Exists AndAlso (Current.Attributes And FileAttributes.ReparsePoint) <> 0 Then Throw New IOException("缓存路径包含链接，已停止清理：" & Current.FullName)
            Current = Current.Parent
        End While
    End Sub

    Private Shared Sub DeleteCacheContents(Root As String, Target As String, ByRef Count As Integer, ByRef Skipped As Integer, ByRef Bytes As Long)
        Dim FullTarget = Path.GetFullPath(Target)
        If Not FullTarget.StartsWith(Root & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) Then Throw New IOException("清理目标超出了 PCL 缓存目录。")
        If Not Directory.Exists(FullTarget) Then Return
        EnsureNoReparsePoints(FullTarget)
        Dim Entries As String()
        Try
            Entries = Directory.GetFileSystemEntries(FullTarget)
        Catch ex As IOException
            Skipped += 1
            Return
        Catch ex As UnauthorizedAccessException
            Skipped += 1
            Return
        End Try
        For Each Entry In Entries
            Try
                EnsureNoReparsePoints(FullTarget)
                Dim Attributes = File.GetAttributes(Entry)
                If (Attributes And FileAttributes.ReparsePoint) <> 0 Then
                    Skipped += 1
                ElseIf (Attributes And FileAttributes.Directory) <> 0 Then
                    DeleteCacheContents(Root, Entry, Count, Skipped, Bytes)
                    If Not Directory.EnumerateFileSystemEntries(Entry).Any() Then Directory.Delete(Entry, False)
                Else
                    Dim Length = New FileInfo(Entry).Length
                    File.Delete(Entry)
                    Bytes += Length
                    Count += 1
                End If
            Catch ex As IOException
                Skipped += 1
            Catch ex As UnauthorizedAccessException
                Skipped += 1
            End Try
        Next
    End Sub

    Private Sub BtnMemory_Click() Handles BtnMemory.Click
        RunInNewThread(Sub() MemoryOptimize(True), "Toolbox Memory Optimization")
    End Sub

    Public Shared Sub MemoryOptimize(ShowHint As Boolean)
        If Interlocked.CompareExchange(MemoryRunning, 1, 0) <> 0 Then
            If ShowHint Then Hint("内存整理正在进行，请稍候。")
            Return
        End If
        Try
            Dim Released As Long
            If WindowsUtils.HasAdminRole() Then
                Dim Before = CLng(My.Computer.Info.AvailablePhysicalMemory)
                MemoryOptimizeInternal(ShowHint)
                Released = CLng(My.Computer.Info.AvailablePhysicalMemory) - Before
            Else
                Dim Result = RunAsAdmin("--memory")
                If Result < 0 Then Throw New InvalidOperationException("管理员进程未能完成内存整理。")
                Released = CLng(Result) * 1024
            End If
            Logger.Info("内存整理完成，可用物理内存变化：" & StringUtils.FormatByteSize(Released))
            If ShowHint Then Hint("内存整理完成，目前可用内存 " & StringUtils.FormatByteSize(CLng(My.Computer.Info.AvailablePhysicalMemory)) &
                                  If(Released > 0, "，增加约 " & StringUtils.FormatByteSize(Released) & "。", "。"), HintType.Green)
        Catch ex As Win32Exception When ex.NativeErrorCode = 1223
            If ShowHint Then Hint("已取消管理员权限请求。")
        Catch ex As Exception
            Logger.Warn(ex, "内存整理失败", If(ShowHint, LogBehavior.Toast, LogBehavior.ToastIfDebug))
        Finally
            Interlocked.Exchange(MemoryRunning, 0)
        End Try
    End Sub

    <StructLayout(LayoutKind.Sequential, Pack:=1)>
    Private Structure PrivilegeToken
        Public PrivilegeCount As Integer
        Public Luid As Long
        Public Attributes As Integer
    End Structure

    Public Shared Sub MemoryOptimizeInternal(ShowHint As Boolean)
        If Not WindowsUtils.HasAdminRole() Then Throw New InvalidOperationException("内存整理需要管理员权限。")
        Using Identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query Or TokenAccessLevels.AdjustPrivileges)
            Dim Desired As New PrivilegeToken With {.PrivilegeCount = 1, .Attributes = 2}
            Dim Previous As New PrivilegeToken
            Dim PreviousLength As Integer
            If Not LookupPrivilegeValue(Nothing, "SeProfileSingleProcessPrivilege", Desired.Luid) Then Throw New Win32Exception(Marshal.GetLastWin32Error())
            If Not AdjustTokenPrivileges(Identity.Token, False, Desired, Marshal.SizeOf(GetType(PrivilegeToken)), Previous, PreviousLength) OrElse Marshal.GetLastWin32Error() <> 0 Then Throw New Win32Exception(Marshal.GetLastWin32Error())
            Try
                If ShowHint Then Hint("正在整理系统内存……")
                For Command As Integer = 2 To 4
                    Dim MemoryCommand = Command
                    Dim Status = NtSetSystemInformation(80, MemoryCommand, Marshal.SizeOf(GetType(Integer)))
                    If Status <> 0 Then Throw New InvalidOperationException("内存整理操作失败，NTSTATUS 0x" & Status.ToString("X8"))
                Next
            Finally
                RestoreTokenPrivileges(Identity.Token, False, Previous, 0, IntPtr.Zero, IntPtr.Zero)
            End Try
        End Using
    End Sub

    <DllImport("ntdll.dll", ExactSpelling:=True)>
    Private Shared Function NtSetSystemInformation(SystemInformationClass As Integer, ByRef SystemInformation As Integer, SystemInformationLength As Integer) As Integer
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function LookupPrivilegeValue(SystemName As String, Name As String, ByRef Luid As Long) As Boolean
    End Function

    <DllImport("advapi32.dll", ExactSpelling:=True, SetLastError:=True)>
    Private Shared Function AdjustTokenPrivileges(TokenHandle As IntPtr, DisableAllPrivileges As Boolean, ByRef NewState As PrivilegeToken,
                                                 BufferLength As Integer, ByRef PreviousState As PrivilegeToken, ByRef ReturnLength As Integer) As Boolean
    End Function

    <DllImport("advapi32.dll", EntryPoint:="AdjustTokenPrivileges", ExactSpelling:=True, SetLastError:=True)>
    Private Shared Function RestoreTokenPrivileges(TokenHandle As IntPtr, DisableAllPrivileges As Boolean, ByRef NewState As PrivilegeToken,
                                                  BufferLength As Integer, PreviousState As IntPtr, ReturnLength As IntPtr) As Boolean
    End Function

    Private Sub BtnCave_Click() Handles BtnCave.Click
        LabCave.Text = GetRandomCave()
    End Sub

    Private Sub CardCave_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles LabCave.MouseLeftButtonUp
        LabCave.Text = GetRandomCave()
    End Sub

    Private Sub BtnHint_Click() Handles BtnHint.Click
        LabHint.Text = GetRandomHint()
    End Sub

    Public Shared Function GetRandomCave() As String
        If RandomInteger(0, 7) = 0 Then Return GetRandomPresetHint()
        Return RandomOne(CaveMessages).Replace("\n", vbCrLf)
    End Function

    Public Shared Function GetRandomHint() As String
        Try
            Dim FilePath = Path.Combine(Paths.Base, "PCL", "hints.txt")
            If File.Exists(FilePath) Then
                Dim Hints = File.ReadAllLines(FilePath, Encoding.UTF8).Where(Function(Line) Not String.IsNullOrWhiteSpace(Line)).ToArray()
                If Hints.Length > 0 Then Return RandomOne(Hints).Replace("\n", vbCrLf)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取自定义提示失败", LogBehavior.ToastIfDebug)
        End Try
        Return GetRandomPresetHint()
    End Function

    Public Shared Function GetRandomPresetHint() As String
        Return RandomOne(PresetHints)
    End Function

    Private Shared ReadOnly PresetHints As String() = {
        "在版本选择页面，右键某个版本也能进入版本设置页面。",
        "在版本设置中可以调整分类，以将特定版本隐藏。",
        "在版本设置中可以开启第三方登录验证，例如 Little Skin 登录。",
        "PCL 会根据剩余内存与 Mod 数量自动调整游戏的内存分配。",
        "在高级启动选项中，可以设置在游戏启动前运行指定程序。",
        "版本设置只对当前版本生效，全局设置对所有版本生效。",
        "在版本选择页面添加文件夹，可以导入已有的 Minecraft 安装。",
        "点击版本旁的心形图标，可以把它加入收藏。",
        "下载 Mod 时，可以选择目标版本的 Mod 文件夹。",
        "将 Mod 或整合包拖进 PCL 窗口，可以开始安装。",
        "在 PCL 文件夹下新建 UTF-8 编码的 hints.txt，可以自定义提示内容。",
        "离线皮肤的设置通常只对单人游戏有效。",
        "个性化设置中可以隐藏不需要的功能。",
        "拖拽窗口边缘可以调整 PCL 的窗口大小。",
        "将鼠标指向下载页左侧的选项卡，可以找到刷新按钮。",
        "启动游戏遇到问题时，可以先查看启动日志和错误报告。",
        "下载完成的任务可以在任务管理页面查看。",
        "适时休息、喝水，再开始下一场冒险。",
        "需要更换 Java 时，可以在游戏版本的设置中选择。",
        "百宝箱下载器支持 HTTP 和 HTTPS 地址，不会自动运行下载的文件。"
    }

    'CAVE_MESSAGES_START
    '从 PageOtherTest.GetRandomCave 的非敏感业务文案恢复，平铺后随机选择。
    Private Shared ReadOnly CaveMessages As String() = {
        "来硬的！",
        "钻石！",
        "我们需要再深入些！",
        "结束了？",
        "见鬼去吧！",
        "君临天下！",
        "与火共舞！",
        "本地的酿造厂！",
        "为什么会变成这样呢？",
        "信标工程师！",
        "不稳定的同盟！",
        "天空即为极限！",
        "甜蜜的梦！",
        "探索的时光！",
        "狙击手的对决！",
        "这是？工作台！",
        "永恒的伙伴！",
        "腥味十足的生意！",
        "结束了。",
        "开始了？",
        "这交易不错！",
        "你的世界！",
        "/summon Creeper ~ ~ ~ {Fuse:0}",
        "MC-98587!",
        "紫黑格子波纹疾走！",
        "命令方块不适合作为武器！",
        "Don't try Minecraft Legend!",
        "新增了一堆 Bug！",
        "Also try Create!",
        "这是刻意的游戏设计！",
        "附魔神圣橡胶树树苗！",
        "有频道的 AE 不是好 AE！",
        "你好中国！",
        "/give @a hugs 64",
        "这是特性！",
        "Minecraft Legend!",
        "Creeper?",
        "Minecraft 2.0!",
        "Hello, Herobrine!",
        "Herobrine...xia?",
        "It's a FEATURE! Not a BUG!",
        "我 Mojang 绝不跳票！",
        "苦力怕！不是爬行者！",
        "蠢虫？毒虫？蠹虫？",
        "粉色羊是隐性纯合子！",
        "Can't keep up! Did the system time change or is the server overloaded?",
        "比钻石更强！",
        "BUGJANG!",
        "猪灵劲曲！",
        "床里面藏着 TNT！",
        "这么多留言中你偏偏看见我的，爱你么么哒 ╰(￣ω￣ｏ)！",
        "我铸剑，你冰球，我们都有光明的未来！",
        "Deadline 是第一生产力！",
        "核电，轻而易举啊！",
        "今天也努力当咸鱼了呢（瘫）",
        "MC 是我们的青春啊！",
        "烧烤炉制作教程：机械动力+瓦尔基里+我的世界+低配电脑",
        "(=｀ω ´=)",
        "Also try 新闻主页！",
        "由于目标计算机积极拒绝，无法连接。",
        "夹子！",
        "菌变，极限模式，随机种子，直接开始！",
        "猫酱补药载旧活新整了！",
        "龙猫得了 MVP！",
        "我的世界，启动！",
        "Look familiar？见此消息者，视为自愿加入超级地球！",
        "《龙猫你玩 MC 吗》",
        "正于此地，愿你找到想要的书。",
        "Also try SCP-CN-660-J!",
        "正于此地，愿你找到想要的 Mod 和整合包！",
        "Also try SCP-CN-048-J!",
        "愿光之种在每个人的心中萌发！",
        "任天堂就是世界的主宰！",
        "比撒的给台！",
        "To be continued.",
        "脑子放假去了！",
        "黄瓜鸡蛋粥，好吃又美味！",
        "刷到此条的，一切都会如愿，所想所念一定能成功！",
        "当你觉得我咕了，但是我没咕，这也是一种咕！",
        "没有钻石块的可以用下界合金块代替！",
        "心脏停跳文学社！",
        "Vamos!",
        "50382 警告！",
        "Priority Crash Launcher!",
        "自己都不相信自己，那怎么会成功？",
        "Minecraft saves the world!",
        "那就当做没有 Bug 好了！",
        "试试泰拉瑞亚！",
        "向骰子低头！",
        "隐藏主题解锁给点提示行不行啊 QAQ！",
        "破产了启动器！",
        "泡菜龙启动器！",
        "劈柴驴启动器！",
        "碰瓷狼启动器！",
        "等泰拉瑞亚 2、半条命 3、巫师 4、辐射 5、老滚 6 发售了我就能玩上了，我真幸运！",
        "Keep your DETERMINATION.",
        "我东方永不过气！",
        "PPCCLL!",
        "想拉萌新朋友一起玩 MC 的话选一些简单的整合包更好入手哦！上手有任务指导合成有 JEI 内容还比原版丰富，肯定会喜欢玩的！",
        "陌生人，虽然不认识你，但祝你早安，午安，晚安，事事平安 :)",
        "希望这个游戏可以给你带来更多的快乐！",
        "Also try Celeste!",
        "Also try The Witness!",
        "结束了？结束了。真结束了吗？如结束。",
        "一定不要贪小便宜去非微软平台购买 MC 账号，百分之九十五是黑卡账户！",
        "要恰饭的嘛！",
        "人生没有标准答案，去做自己认为正确的事！",
        "帅得体面，猫得明白！",
        "王泪天下第一！",
        "FLAG IS WIN!",
        "凌波微步，快乐的舞步！",
        "这个叫 TAS 的人是不是开挂了？",
        "For Super Earth!",
        "爽啊！",
        "支持正版 MC，加油！",
        "失之东隅，收之桑榆。",
        "让我们拜请白日铸炉！",
        "我再发回声洞我就是勾，骗你的我本来就是 qwq",
        "你好，我是末影龙，如果你看到这条留言，说明我已经被你打败了 awa，不过游戏还没结束。还有一个叫凋灵的 BOSS 等着你，出发吧史蒂夫，祝君好运！",
        "请为坠落的龙猫取名……",
        "小友，你在期待什么？",
        "我说有认知偏差。我说有认知偏",
        "你的旅途……到此为止……",
        "Also try PCL CE!",
        "Also try HMCL!",
        "那么，我们开始吧？",
        "突然想起来有回声洞！",
        "我可以一个人很好，但有你会更好。",
        "You should try our sister launcher, PLC!",
        "AAA专业投稿",
        "咕咕嘎嘎！",
        "众所周知，我的世界的开发商是 BugJump。",
        "别杀怪物，你这个海豚！",
        "我永远都不相信我的今日人品会是 0！",
        "千万得点！",
        "是的，这是一条用来凑数的信息。",
        "你说的对但是你说的对所以你说的对！",
        "你必封印在众人梦中散布瘟疫的障目之光。",
        "看到这个评论的人请去喝一杯水！",
        "相信我，一切都会变好的！",
        "Professional Crush Launcher!",
        "长风破浪会有时，直挂云帆济沧海！",
        "要断章取义 ——取自《不要断章取义》",
        "Ciallo～(∠・ω< )⌒☆",
        "我跟你讲你这样是要被夹的！",
        "当重新创建一个存档，不要忘记创建这个存档最初的意义。",
        "Missing No.",
        "管管孩子，救救游戏！",
        "今天好累啊！",
        "我想要说的前人们都说过了！",
        "希望人有事.jpg",
        "修了的叫 Bug，没修的叫特性。",
        "我想 PCL 玩家都是些很有趣的，很好的人，祝大家天天开心！",
        "我是绝对不会为了回声洞发电的……吗？",
        "多线程被卡住了不玩手机玩什么呀！（绝望）",
        "Ba+2Na=Banana",
        "你被强化了！快送！",
        "这里可以写广告嘛？",
        "你每天会忘记上千件事，为何不把这件事也忘记？",
        "我差一血打死怪，怪差一血不打死我！",
        "不打钱就削土豆！",
        "众所周知，1+1=王！",
        "世界就是一个巨大的瑞士卷，有的人去得了瑞士，有的人只取得了卷。",
        "如果你能看到我这条，说明我们有缘，加我一起玩啊！",
        "PCL 服务器第一朵蘑菇云始于 Candy_Pink！",
        "龙腾猫跃 简称 龙猫 但 不是 龙猫",
        "世界和平！",
        "Minecraft 2.0 正在启动中！",
        """44 45 4C 45 54 45 20 53 59 53 54 45\n4D 20 33 32 20 54 4F 20 57 49 4E !""",
        "お－お－お－お－お－お－お－お－お－お－お－お",
        "方！兴！衢！不！是！方！兴！衡！",
        "开学哩！",
        "--...-....-...- -.-.-.--..---.. --.-.--..-...-. -..----.--.....",
        "别点了！手不累吗？",
        "很好的 MOD 使我存档损坏",
        "叶底藏花一度，梦里踏雪几回。",
        "另类本身就是一种出类拔萃！",
        "麻辣鸡翅真好吃！",
        "咩！（你好！）",
        "已深度思考（用时 0 秒）\n\n服务器繁忙，请稍后再试。",
        "For the Emperor!",
        "阿玛忒拉斯！",
        "如果你看到了这句话一定会认真的读下去，读到最后就会发现这句话没有任何意义，恭喜浪费了一分钟！",
        "Wake up.",
        "It's time to go to school.",
        "反复下载的是游戏，回不到的是童年。",
        "汉中乃北伐剑锋，文长可敢担太守之责？",
        "有的人毕业了，有的人还没开学。",
        "在 38 度的太阳底下放寒假。",
        "请输入文本",
        "Made in Abyss!",
        "这就是被央视点名表扬的游戏吗？",
        "吃水不忘挖井人，不要忘记这超赞的 PCL 是龙猫在支撑……",
        "我的世界我做主！",
        "撞车欧灵打出93杠16的战绩得取10000分，出两次所向披靡一次聚变部署",
        "早上好，中午好，晚上好！",
        "希望能在一些地方留下自己的一点痕迹。",
        "如果你看到这个，说明我的存在是有意义的，我实现了我的价值，虽然很小。",
        "MC，启动！",
        "各个国家有各个国家的国歌（用粤语说",
        "挖矿免费玩！",
        "宁可没用，不能没有！",
        "龙猫是龙猫，龙猫是龙猫，但龙猫不是龙猫！",
        "拜托，反复点击回声洞来看自己的投稿也太逊了！",
        "埋骨何须桑梓地，人生无处不青山。",
        "基岩真好吃！",
        "弱小和无知不是生存的障碍，傲慢才是。——《三体》",
        "在没有错误日志的情况下诊断任何问题无异于闭眼开车。",
        "有的说，但是没得说。",
        "希望可爱的龙猫猫能更容易看懂自己写的代码。",
        "PCL 的代码只有龙猫和上帝知道，然而龙猫忘记了。",
        "挖三填一：刚进入 MC 的玩家可能都不知道这个游戏存在危险，只知道东看看西瞅瞅，所以到了晚上的时候才知道 MC 的可怕。",
        "Also try Terraria.",
        "龙猫画的饼很大！",
        "事实证明，乱打序顺会不影响的人阅读。",
        "你知道龙猫 MC 的 ID 是 LTCat 吗？",
        "据我所知，这些材料至少得用一背包或者一大箱子的石头，都有这么多石头了，干嘛还要去做刷石机？",
        "私はLです。",
        "龙腾猫跃 > 龙猫 > lm",
        "你知道吗：2022 年 3 月 10 日 Mojang 账号是最后自愿迁移至 Microsoft 账号的期限。",
        "日常踩雷！",
        "当你看到这条回声洞，你就看到了这条回声洞。",
        "《关于解锁档位为了投稿回声洞，然后发现不知道说啥这件事》",
        "不要尝试在岩浆里喝热水！",
        "博士，您还有许多事情需要处理，现在还不能休息哦。",
        "真的有人会看这些吗？",
        "我的人品值很差，只有 1 的 100 倍（bushi）",
        "NeoForge yyds!",
        "Also try Minecraft with RTX!",
        "public static void main(String[] args) {}",
        "Ctrl + S",
        "腐竹，我刷沙机忘关了！",
        "反手一个超级加倍，闷声发大财。",
        "腐竹，我不小心在末地种了个蘑菇！",
        "↑↑↓↓←→←→BABA",
        "腐竹，我忘记做处死装置了！",
        "38324？14122！",
        "我的评价是：我不予评价。",
        "建筑，我所欲也，红石，亦我所欲也；二者不可得兼……喊俩大佬就可以得兼了。",
        "我的梦想是天天咕咕咕（逃",
        "锟斤拷锟斤拷䵣笓靹攮濄魊！",
        "* It fills you with determination.",
        "点击千万别点，可以获得所有主题。",
        "I want to play a game.",
        "如果忧郁是种天赋，那我天赋异禀。",
        "温馨提示：你可点击红色按钮，剩余要靠你的智慧啦~",
        "康神开播了？",
        "使用左键以和铁傀儡友好交流！",
        "PCL (゜-゜)つロ 亁杯~",
        "THE END IS NEVER THE END IS NEVER THE END IS NEVER THE END",
        "雄火龙又双叒叕被绿了。",
        "净他妈扯淡！",
        "乌拉！！！！！",
        "不要尝试带着猫挑衅苦力怕！",
        "Welcome to……O…PCL!",
        "快来试试 PCL 下载器！",
        "这根本不是起泡胶制作教程！",
        "新的 Bug 删除了，旧的 Bug 增加了。",
        "我知道你想睡觉了，但是，不熬夜游戏不会健康。",
        "Death is not an escape.",
        "你知道吗？你不知道。",
        "欸我 10000000 Mods 的整合包打不开了，这启动器不行啊。",
        "你知道吗？人会对看到的文字进行自动排序。别读了，你再读一遍也是这样。",
        "回声~回声~回声~",
        "360：我 TM 觉得你很可疑",
        "我讲两句，咳咳！饿了要吃饭，渴了要喝水，困了要睡觉，鼠了要埋掉。",
        "爷爷，你关注的龙猫终于更新啦！",
        "半命无出三，故吾曰 G 胖不三。PCL 不出三，是龙猫不三乎？曰：非然也。",
        "让人类永远保持理智是一种奢望！",
        "点千万别点会发生好玩的事！",
        "PCL 是我的世界启动器，不是下载器！",
        "我已启动。——鸡煲",
        "俺不中嘞，这网咋这么卡嘞 QAQ",
        "hi，我是 QZTJX，很高兴你能看到我的留言。",
        "KO NO DIO DA!",
        "我是史蒂夫，我为我童年代言。",
        "* 还剩 17 个。=)",
        "哇！金色传说！",
        "赶紧去给我开 EVA！",
        "我绝对不会因为回声洞给龙腾猫跃发电的！",
        "我绝对不会发回声洞的！",
        "爷想被夹！",
        "淡黄的长裙~蓬松的头发~",
        "奇怪的知识增加了！",
        "小朋友，你是否有很多问号？",
        "生鱼忧患，死鱼安乐。",
        "在？不在。",
        "Ease my mind.",
        "小善，如果你是女孩子的话啊……",
        "啊啊啊啊，宝宝你是一个一个一个……",
        "Get Over Here!",
        "孩子们，假期要开始加速了，没抖音还问！",
        "震惊龙猫一整天！",
        "你看这个光影多棒啊，开一下试试，反正电脑好，诶我主机怎么烧",
        "PCL 高速下载器！",
        "右键开始游戏按钮是没有彩蛋的！",
        "> 点此启动内置小游戏 <",
        "衬衫的价格是九镑十五便士。",
        "「胜败乃兵家常事，但是下一次我们会赢回来的！」",
        "少女折寿中... φ(*￣0￣)",
        "然则天下之事，但知其一，不知其二者多矣，可据理臆断欤？",
        "Java (TM) SE binary 未响应\n· 尝试恢复此程序\n· 等待程序响应\n· 关闭程序",
        "我这里有负荆请罪 IV、弹射物吸引 V、经验腐蚀的钻石甲，要吗？（",
        "Plain musiC pLayer 2!",
        "犹豫，就会白给！",
        "PCL YES!",
        "龙猫是龙还是猫？",
        "我发言了，就这样~喵~（粗犷大叔音）",
        "我爱吃薯条 awa",
        "恭喜你，你被恭喜了！",
        "一支笔一盏灯一个奇迹！加油，开学生！",
        "你这什么游戏啊？手机能玩吗？要不要钱啊？",
        "Press F to enter the tank!",
        "多行不义必自毙。",
        "活跃橙好难拿！",
        "进攻 D 点！",
        "我是我无敌的名无名，可以在 B 站上搜我，请记住我，我不想我噶了没人认识我。",
        "汤米，你……是肿瘤男孩！",
        "在我没想到好的留言之前请不要把我放到回声洞中！",
        "咕咕咕鸽鸽鸽嗝！",
        "3T3B",
        "没通关过。",
        "在游戏尽头的城市！",
        "幻翼感受到了动能……",
        "喵喵喵，在这里放一只梵猫~他会盯着你的 awa",
        "我的刀盾~",
        "现在核电发电量是10A",
        "/kill @e[type=creeper]",
        "/gamemode 1",
        "你炸出了 TNT 之神,我将赐予你 TNT！",
        "不残血不会玩.jpg",
        "Mojang 作为英语或瑞典语时的读法不一样哦。",
        "主页自定义看起来很难，但是你仔细研究发现你可以套娃。",
        "众所周知，别人的世界和我的世界不是同一款游戏。",
        "PCL 盒子！",
        "你的好友 xxx 正在游戏\nWallpaper Engine",
        "第五人格牛福！",
        "装数据包时不要把数据包的 zip 文件也当成资源包装上！",
        "破产了启动器，你值得拥有。",
        "Wryyyyyyyyyyyyyy",
        "您，人？神！砰砰砰！",
        "龙腾猫跃最棒了！トトロが一番跳ねる！",
        "欧皇！！！",
        "这启动器也太好用了其实捐RMB你可以获得更多功能但不捐基础功能也是很多的",
        "好想要正版账号",
        "我东洋天下第一！",
        "Mojang 在瑞典语中意思是东西！",
        "所以……我不知道有啥好说的，反正氵一个留言就行了 qwq",
        "众所周知，地狱有水。",
        "Also try 看云模拟器（bushi）！",
        "游戏一小时，看云 59min。",
        "我们需要再深入些。抱歉，今天不行。",
        "Non terrae plus ultra",
        "TIPS: 大量的红键会使屏幕起火！",
        "喵呜~你好可爱~",
        "如果游戏没有声音可以尝试按下 F3+S！",
        "今早雾霾蔽日，但是不要害怕，太阳依旧在云端！",
        "犹豫就会白给！",
        "你知道吗？PCL 的前身是 PCL1！",
        "Mojang AB = Mojang And Bug",
        "Point Cloud Library 启动器！",
        "SCP 基金会已介入调查！",
        "很快就到你家门口挖矿！",
        "10 岁以下的儿童不宜食用小块块！（PS：我的世界 ESRB 分级为 10+）",
        "Removed Herobrine!",
        "PCL 是个我的世界启动器吗？不，是音乐播放器，Mod 下载器和下载软件。",
        "龙猫的解密也太难了点吧！",
        "一定要点上面那个千万别点！！！",
        "手持两把锟斤拷，口中疾呼烫烫烫。脚踏千朵屯屯屯，笑看万物锘锘锘。",
        "白帝圣剑！御剑跟着我！",
        "今晚，深渊结算。",
        "(let ([s ""(let ([s ~s]) (printf s s)""]) (printf s s)",
        "花开花落，再灿烂的星光也终将消失。",
        "DNF IS TRUE!!!",
        "欢迎使用史上最复杂的解密启动器！（doge）",
        "原始人，起洞！",
        "某黑客：你会编程吗？龙猫：我不会，PCL 不是我编的。",
        "* (通过对着语音转换系统乱叫，这只龙猫偶然编出了这个软件。)",
        "宇宙多么浩瀚，偏偏我们在此相遇！",
        "UGxhaW4gQ3JhZnQgTGF1bmNoZXIgMu+8gQ==",
        "人品测试，每天一次！",
        "彭翠兰启动器！",
        "生而为人，我很抱歉！（看到这条留言的人一定很温柔吧！）",
        "没错，是百分制√",
        "ckya blyat!",
        "好快の刀！",
        "freedom d↓ve！",
        "这是一条回声！",
        "泷泽萝拉哒！！！",
        "今天你猫车了吗？",
        "我国有一套完整的未成年人保护法！",
        "在内群有个人经常会把龙猫称为游戏《黎明杀机》里面的夹子杀手……",
        "回声洞　　　　　　　投稿",
        "初音未来，我好喜欢你啊（",
        "典明粥的制作配方：花京院典明的爱心宝宝粥*1 + 替身使者 死神13 的便便*1",
        "二次元，金发，吸血鬼，可爱……没错，这些都是在形容迪奥·布兰度。",
        "我在想为什么没有人一开始就用最强攻击呢？",
        "花儿在绽放，小鸟在歌唱……",
        "就 该 在 地 狱 中 燃 烧",
        "腾讯** 百度网盘**",
        "我的世界一直都不只是一款马赛克游戏！",
        "聆听怒海潮生，长空雷震，獠牙在耳，万籁黄昏，最动人，莫过喧嚣红尘。",
        "这次一定！",
        "繚乱！虹ヶ咲！",
        "PCL 中还有两种隐藏主题：真·滑稽彩和星空蓝。要怎么获得他们呢？提示：你猜！",
        "秒了，有什么好说的。",
        "塔塔开！塔塔开！",
        "平角裤，平角裤！",
        "白日放鸽须纵酒，龙猫作伴好还乡。",
        "龙猫说快改完 Bug 了，他一定没改完，就像老婆饼里没有老婆，康师傅牛肉面里没有牛肉。",
        "当你觉得臭鸽子龙猫又要咕咕咕的时候他真的咕咕咕了，这亦是一种不咕。",
        "December=Dec",
        "左手画圆，右手画方！",
        "咕咕咕咕？咕咕咕咕咕咕！咕咕咕咕咕咕咕？？？咕咕咕！（咕咕咕咕咕咕咕咕咕",
        ".. -....- .-.. --- ...- . -....- .--. -.-. .-.. ..---",
        "芜湖，起飞！",
        "努力不一定会成功，但一定会有结果，无论好坏输赢，都不会后悔，因为，你也曾为此奋斗劳累拼命，人生千姿百态，努力一次，谁知怎样呢。",
        "I see the player you mean.",
        "催更催更，我是急急国王！",
        "精 神 小 伙",
        "总有些话，是反的，是倒的！",
        "大图？高清？",
        "我的世界是好游戏啊，再差的电脑也能带起来，再好的电脑也带不动……",
        "龙猫：太复杂了，不修了，这是特性，特性！",
        "Why do you see this? Shouldn't you play your game?",
        "你给路达哟！不说了开游戏了，林肯死大头！",
        "龙猫的今日人品我们谁也不知道！",
        "我们是如何走到这一地步的？",
        "有个按钮可以让启动器缩小，旋转……",
        "众所周知，电子游戏不需要视力！",
        "We're no strangers to love~",
        "还记得第一次玩我的世界的时候吗？那曾是很美好的回忆……",
        "该说什么好呢 说什么好呢 什么好呢 么好呢 好呢 呢 （回声真大",
        "如果你找不到第三方登录的话不要到版本选择点那三个点，更不要点那个设置，就算你点了那千万不要往下翻。",
        "千万别点千万别点！",
        "El Primo!!!",
        "在时间的流逝中，没有什么事是一成不变的。",
        "The Escapists 2？彳亍，开始逃狱！",
        "熬夜对身体不好，所以我建议你们……玩通宵。",
        "啊wee改哈鞥嫦娥我刚不疤痕处哈维楚王嗡阿格王朔！！！",
        "对于一个像我一样身高两米一的巨人来说，挖三填一是不可取的。",
        "众所周知，柯南和基德才是真爱！",
        "雷石东直放站！",
        "现在玩的嗨，待会被夹更嗨！",
        "GrandpaBr!",
        "人群当中钻出来一个光！头！",
        "啊呜呜呜~~",
        "伊莉雅：美游来过这里吗？",
        "买不起正版的穷鬼举个爪。",
        "17 张牌你能秒我？你能秒杀我？",
        "有——回——声——吗—— 有-回-声-吗- 有回声吗",
        "Notch is coming back!",
        "夹子启动器！",
        "我不会告诉你，鼠标悬浮在隐藏主题上，会显示出什么不可告人的秘密！",
        "国足 NB！",
        "结束了？不，你还有很多事要做，现在还不能休息哦……",
        "勇士总冠军，库里 FMVP！",
        "握~着~我~的~抱~枕~",
        "我爱吃滑稽果！",
        "跟你们讲一个笑话：龙猫",
        "你每天要忘记成千上万件事，为什么不把这件事也忘了。",
        "迫害群友需谨慎，不然夹子就离你不远了 awa",
        "Minecraft: Dungeons 又名 我的裤子动了！",
        "100 年清朝老兵，申请出战！",
        "你现在不能睡觉，你的朋友在开派对！",
        "木叶飞舞之处，火亦生生不息！",
        "我爱学习，学习使我妈快乐，我妈快乐，全家快乐！",
        "人被逼急了啥都能做出来，除了数学题！",
        "M to the C to the V！",
        "* 保持你的决心，FUCK！",
        "向鸽者文明致敬！",
        "* 今天是多么美好的一天啊。小鸟在歌唱，花朵在绽放。在这样的一天里，像你这样的孩子……应当被龙猫夹起来扔垃圾桶里。",
        "关掉，关掉，一定要关掉！",
        "远古残骸真的存在吗？",
        "啊呦 EVERYBODY 在你头上暴扣！",
        "Plain Craft Launcher→PCL→PC L→电脑 L→电脑垃圾\n思考.jpg",
        "月色与雪色之间，你是第三种绝色。",
        "群服务器，时不时来群里 Can't keep up！",
        "或许你并不是不想睡觉，而是周围有怪物在游荡！",
        "我们的 LTW 真是太好玩了！",
        "阿瓦达啃大瓜！",
        "非酋该怎么在这个世界上生存（小声）……",
        "妇 科 圣 手",
        "少年没有乌托邦！",
        "嗨，同志，您知道列宁格勒和斯大林格勒在哪吗？我在地图上找不到它。",
        "生活就像打电话，不是你先挂就是我先挂！",
        "好好睡一觉， 就是人生的重启方式呀。",
        "为什么披萨会考糊？",
        "老鼠偷了大米，人们说它狡猾；人类偷了蜂蜜，却说蜜蜂勤劳。",
        "千万别点千万别点千万别点！",
        "CHINA!!! CHINA!!! CHINA!!!",
        "看着龙猫的秀发，我不禁陷入沉思……哦！原来龙猫没有头发！",
        "休息区里有一个特殊的休息室，通往整个后室里最棒的派对喔！=)",
        "咖啡党永不为奴！",
        "再点一下吧！",
        "这个回声洞莫得 CD，定个小目标，点它一亿下！",
        "Tip: 小鸽子们不要挑食哦，不管是烤鸭还是欧芹都要吃~",
        "到点了，Visual Studio 上号！",
        "人间，处处是仙境，何欲而求天？已有大于未有，莫要失去而追悔莫及！",
        "芜湖~咕咕咕起飞~",
        "死机蓝！",
        "Tip: 热知识：这是一条…烫烫烫烫烫！的热知识！",
        "再次点击查看下一位沙雕网友乱七八糟的留言！",
        "咕咕咕——\n翻译：鸽，下次也不一定。",
        "在 MC 中，沙子可以下落，说明 MC 还是很科学的！（确信",
        "唔咕，要饭大失败……（眼神死",
        "你蓝了，你白了，你没了！",
        "这是回声洞~是回声洞~回声洞~声洞~洞~",
        "往往结束才是开始！",
        "检测到 Minecraft 进程意外退出，错误分析已开始……",
        "你不能游荡，周围有怪物在休息！",
        "玻璃，放错，退游，一气呵成！",
        "你已被服务器封禁！理由：请自证 1145 CPS！",
        "当你打开这条回声洞的时候……还不快去想想你的作业写完了没！",
        "看到这条留言，请为疫情中献身的人们默哀一分钟，并献出至高的敬意。",
        "投稿回声洞的人数为 n，你看到这条的概率是 n 分之一，所以能看到这条的都是欧皇！",
        "汀！汀！莱万汀！汀！汀！莱万汀！",
        "直到我的钱包中了一箭！",
        "阿能我老婆！阿噗噜派！",
        "游艺街✘ 戒赌街✔",
        "忠告：请不要在喝水的时候看回声洞，否则所造成的一切后果与 PCL II 无关。",
        "生活枯燥无味，龙猫模仿人类！",
        "龙猫不是鸽子，只是太忙了（确信）！",
        "在这个时候，你的人品值会是：100！100！100！！！！！",
        "问：龙猫今天的人品值是多少？\n答：我连宇宙尽头在哪里都不知道，怎么会知道这个。",
        "愿世界永葆和平！",
        "ENDERMAN PENTA KILL! ACE!",
        "众里寻他千百度。蓦然回首，那人却在，灯火阑珊处。",
        "前进，然后变得更好！",
        "小丑竟是你自己！",
        "敲传统木鱼，见观音如来。\n敲电子木鱼，见初音未来，法号弥苦。",
        "The cake is a lie.",
        "别戳了，这里没有你想找的东西！",
        "lm：蓝猫",
        "主不在乎。",
        "抱歉，今天不行？不吃这套，谢谢。",
        "做工程是不可能不咕的啦，这辈子都不可能不咕的啦。",
        "挖三填一！",
        "Make Minecraft Great Again!",
        "众所周知，阳光菇不爱阳光。",
        "要是哪一天我电脑打 MC 炸了我都不稀奇！",
        "你笨拙的表现犹如黏糊的麦片粥，继续努力吧！",
        "恭喜你，你的鼠标左键没坏！",
        "炮造毕，何不置珍珠？",
        "祝我下个池子出水大叔！大叔，为了你，对蓝色恶魔使用石头吧！",
        "Minecraft 1.7.10 - 单人游戏（未响应）",
        "我趣，是吴奇隆！",
        "悲しみの...向こうへと...",
        "* 移除了 Herobrine\n* 修复了一个 Bug\n* 增加了一个Bug",
        "长官，我们双脚着地，率先踏入地狱！",
        "Plain Craft Launcher 的中文翻译是：普通飞行器发射器！",
        "特别是其搭载 690 战术核显卡的改进版本，一发就可以摧毁一个航母战斗群。",
        "天上的卡兹不说话，地上的刀哥想妈妈（doge）！",
        "要用咕咕对抗咕咕。——鲁迅",
        "We are the universe. We are everything you think isn't you.\n——终末之诗",
        "众所周知，在服务器中按 Alt+F4 可以开启飞行！",
        "下降率大点没事！",
        "快门一按，行车中断，造成事故，移交法办！",
        "中国联通提醒您：警惕移动电信诈骗！",
        "就我个人来说，PCL 很好用对我的意义，不能不说非常重大。这样看来，就我个人来说，PCL 很好用对我的意义，不能不说非常重大。而这些并不是完全重要，更加重要的问题是，莎士比亚曾经说过，意志命运往往背道而驰，决心到最后会全部推倒。这句话语虽然很短, 但令我浮想联翩。",
        "夹子，夹子，更多的夹子，夹子在蔓延……",
        "谁言别后终无悔，寒月清宵绮梦回；深知身在情长在，前尘不共彩云飞。",
        "Bugjump 自古特性多，可与育碧争霸王！",
        "众所周知，Bug 修掉一个还会有第二个 Bug 伴随着修掉的 Bug 出现！",
        "芜湖，我直接成为懒狗起飞！",
        "邪王真眼是最强的！",
        "反馈 Bug 前……先想一想这是不是特性！",
        "这是回声洞还是回字洞？",
        "歪比巴卜？",
        "为什么不试试在调试选项中把动画速度调成 0.1x 或是 3x 呢？",
        "神社倒闭之日。",
        "爱丽丝做的布朗尼果然好吃呢！面团口感湿润但却不发黏，有种清爽的甜味。可可粉是用万豪顿牌的吗？",
        "大大大~大工ong~，你的铠铠甲怎怎么漏漏漏 漏~电啊~\n——面对二阶段深海骑士的格林",
        "错误代码：-118\n无法载入网页（未知错误）",
        "获得成就：别人的世界！",
        "你这圣遗物怎么不强化（",
        "你知道吗，当你看了这条信息会发现看了跟不看一个样！",
        "你知道吗？其实你什么都不知道！",
        "冷知识：这其实是一条热知识！",
        "回声洞里面没有米勒星球！",
        "你说对，但原神，米自研，冒险游，提瓦特，神选中，授神眼，引元素。扮角色，邂同伴，击强敌，找亲人，掘真相。",
        "孤独是山峰给予征服者的礼物。",
        "你知道吗：千万别惹玄素，否则会被夹得很惨！",
        "她牵着对立的手。因为她们将会继续前进。\n因此，就像这样，命运的齿轮在这里继续运转……而远方不会再有等候着的命运。",
        "众所周知，塔科夫是一款恐怖游戏。",
        "Click Circle!",
        "问君能有几多愁？恰似一缸龙猫向东流。",
        "这游戏真凡尔赛！",
        "以声之色，塑花之形，将你之名，刻于我心！",
        "Make Minecraft great again!",
        "任何罪恶终将绳之以法！",
        "看到我了吗？你没有！ヽ(•̀ω•́ )ゝ",
        "Non terrae plus ultra!",
        "中继器是直放站！",
        "Hex Dragon！",
        "什么？Java 版不支持光追？显卡白买了……",
        "育碧就是一颗大土豆！",
        "夹了！都给我夹了！",
        "自从接触了 CraftTweaker，GPT3 人工智能都被折磨疯了。",
        "我起了，一枪秒了，能怎样？",
        "结束了？开始了？不，还没开始呢，咕咕咕！",
        "今日大无语事件：外卖点了个黑椒牛排套餐，结果商家忘了放牛排……",
        "手握两把锟斤拷，口中疾呼烫烫烫，脚踏千朵屯屯屯，笑看万物锘锘锘！",
        "虽然上面那个按钮叫千万别点，但是还有好多人去点它！",
        "damedane, dameyo……",
        "你说得对，但是，后面忘了。",
        "哇！白色普通！",
        "PCL 的蓝色图标是用硫酸铜染色的！",
        "记得使用 Steam 启动 PCL 哦！",
        "果断，就会白给！",
        "各位同学们，作业写完了吗？",
        "I am the storm that is approoooooooooooooooaching!!!!!!!",
        "你不崩谁崩？",
        "天云轻轨交通二号线！开！通！啦！",
        "前有 IDM，后有 XDM，今有 PDM！",
        "玩了红石以后腰不酸了腿不痛了。就是脑袋有点凉。",
        "野蜂飞过，经过了平凡与伟大。却追随着无悔！",
        "竜神の剣を喰らえ！",
        "你有没有听见孩子们的悲鸣！",
        "您这辈子都别想进入 Grand Theft Auto V 在线模式！",
        "我刚太认真写反馈，结果把笔头含嘴里了！",
        "友谊是魔法！",
        "不懂就问，我要问什么？",
        "年轻人不讲武德！",
        "打五把 CSGO！",
        "* 没人知道龙猫收到多少回声洞投稿。\n* 没人知道有人投了什么投稿。\n* 没人知道龙猫的心态。\n* 因为你根本不在乎龙猫。",
        "巨硬公司™ Hugehard™ Huge hardsoft！",
        "建议您 50 包邮并往里面塞 200 元，更容易卖出！",
        "建议您白送我，更容易卖出！",
        "怎样才能让龙猫选择你的投稿？当然是多发几遍！（bushi",
        "什么？这不是饼干，这是我生产的压缩毛巾……",
        "You should try our sister game, Minceraft!",
        "番茄条+土豆酱！",
        "* 你没有看见什么留言，这里只有几根鸽子留下的羽毛。",
        "龙猫什么时候才能整点大活呢（小声",
        "奇变偶不变，_________！",
        "巴山楚水凄凉地，Q 得 cm△t",
        "井底之蛙，不曾见过大海之辽阔，却知晓天空之蓝！",
        "0 errors, 0 warnings!",
        "这个好诶！",
        "我们都是阴沟里的虫子，但总还是得有人仰望星空。",
        "开学愉快！",
        "祂从天空陨落，于是人们看到了神明……",
        "蛋白不会做蛋糕，但他会做糕蛋~",
        "皇帝家是干什么的呢……人人都在叫这个叫皇帝的人，想必干活也一定是用的……额……金锄头？不不不，可能是钴锄头……",
        "温馨提示：按 Alt+F4 有惊喜！",
        "天不生我键盘侠，键道万古如长夜，键来！",
        "E！S！M！跑！！！",
        "你的无畏来源于无知！",
        "我的人品必不可能是 0！系统有 Bug！",
        "二次元，金发，吸血鬼，可爱……没错，这次真的是芙兰朵露了！",
        "祝各位音游人们在今后的打歌过程中好运连连，后宫成群（",
        "如果今天是你的生日，那我祝你生日快乐，如果不是，那我祝你早上中午下午晚上好！",
        "你 要 被 夹",
        "我保留了千万别点，这样你才知道你用的是 PCL。",
        "海皮咳嗽是一个……高 Ping 战士快乐基地。",
        "龙咕（",
        "4 月 1 日打开 PCL 有惊喜！",
        "鸡汤来咯~~~~",
        "猜猜你要点多少次才能再次看到我！",
        "天苍苍，地茫茫，龙猫走路像牛羊。",
        "你充 Minecoin 吗？",
        "今天是个看人品值的好日子啊~",
        "虽然 Java 版不支持光追，但是现在基岩版白送了！哈哈哈哈！",
        "啥时候出 PCL III？\n回：不可能的……",
        "这里没有人~我们都是鬼~",
        "Long may the sunshine!",
        "If you can.",
        "Tell me your secret.",
        "近朱者赤，近墨者黑。近网易者，就是个寄吧！",
        "【新华字典】里一共有几个字？",
        "寻找远古城市，可以在高海拔地区例如冰封山峰、尖峭山峰、草旬等生物群系往下挖！",
        "我的化学老师说没有 PCl2 这种化学物质！",
        "真的会有傻子买 23.33 来玩回声洞吗？",
        "这解密主题到底怎么解啊啊啊啊啊啊啊！",
        "或许有意义的人生，才是完美的人生！",
        "（阴暗的爬行）（蠕动）（尖叫）（同化）（不分对象攻击）",
        "在吗？明天 DDL 了。",
        "V 我 50 吃个 KFC 谢谢喵！",
        "《我 们 拥 有 最 真 实 的 物 理 引 擎》",
        "爱上一个人是快乐的，但是爱下去让你痛苦了，就要学着放弃，对吧。",
        "我有 20 铁嗷，你怕不怕！（不是",
        "《关于升级后不知道干啥……》",
        "Grove Street, home At least before I fu**ed up these things.",
        "想来把昆特牌吗？我可是村子里最厉害的！",
        "我曾背井离乡，后来全村的人都渴死了。",
        "为 PCL 和伟大的咕咕咕事业而欢呼！",
        "胜利之风，正从我 DIO 背后吹来！",
        "今日人品？",
        "你每天都会忘掉很多事，为什么不把这件事也忘掉呢？",
        "点击千万别点，会送正版哦！",
        "我超！冰！",
        "再次点击这里以查看更多的 PCL 作者和各位沙雕网友乱七八糟的留言！",
        "glvE Me l0m c0iNs 0r Rep0rTinG U",
        "不要再打羊驼了啊！！！",
        "温馨提示：当你拿打火石右键苦力怕时，苦力怕将会消失。",
        "嗨嗨嗨，我的世界好玩吗，不说了，喝鸡汤去了。",
        "你打开内群，试图在群文件找些有用的东西，却发现里面都是错误报告……",
        "沙雕解谜：整个 PCL II 将为你闪烁",
        "TECHNOBLADE YOU NERDSSSSSSS!",
        "你知道吗？龙腾猫跃这个名字实际上是从龙腾虎跃这个名字为基础改过来的哦~",
        "Bug 变 Feature，妙啊！",
        "你在狗叫什么？",
        "听我说谢谢你~因为有你~温暖了四季~",
        "不要相信灰色，直接上！",
        "点一下不够就点两下！",
        "今日人品：100！",
        "鱼人节快乐！",
        "滑稽节是一个节日呢！",
        "ASCII 总是三位数！",
        "帮助的英文是 Help！",
        "砸反馈就完事了！",
        "属于宇宙的数字！",
        "众所周知，十大主题一定有十一个！",
        "回声洞能带来灵感！",
        "！读着倒要话候时有",
        "从罗马开始！",
        "MCBBS 的本体是箱子！",
        "化学，文档，网格！",
        "网址就是来路！",
        "越过屏障！",
        "卢恩与去路！",
        "地下埋藏着宝藏！",
        "从老线索中发现新东西！",
        "重组碎片……",
        "橙色线，藏着线和点！",
        "于历史中发掘秘密！",
        "线索在游戏之外！",
        "穷举不能让你变得更强！",
        "OBSIDIAN！",
        "深蓝色的极客！",
        "不要忽视背景！",
        "结束了？开始了。",
        "开始了？结束了。"
    }
    'CAVE_MESSAGES_END
End Class
