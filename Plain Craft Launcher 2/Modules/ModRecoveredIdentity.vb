Imports System.Security.Cryptography

' Identity generation and verification-only support recovered from the 2.12.8.2 sample.
Friend Module ModRecoveredIdentity

    ' Public verification key, not a signing key. It is also published in MeloongCore at
    ' 4d389f4375aa60e977f512690219a1fa4476838a/Shared/Utils/CryptographyUtils.cs.
    ' The supplied assembly uses the identical 72-byte ECCPUBLICBLOB.
    Private Const RecoveredPublicVerificationKey As String = "RUNTMSAAAAC4QTUNAewh23Q4Q6koHkyIrDIIZUSbua23sf2DiZmIRwSzadISDRyTVTbuWniH3KR7rKj8XBsabms1be6i3c+S"
    Private ReadOnly PotatoFeatureLock As New Object
    Private _PotatoFeatures As List(Of String)

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            ' The supplied release returns None; no rank-granting implementation is available.
            Return DonationRank.None
        End Get
    End Property

    Friend ReadOnly Property PotatoFeatures As List(Of String)
        Get
            SyncLock PotatoFeatureLock
                If _PotatoFeatures Is Nothing Then
                    _PotatoFeatures = New List(Of String)
                    Try
                        For Each Code In If(Settings.Get(Of String)("Potatoes"), "").Split({"|"c}, StringSplitOptions.RemoveEmptyEntries)
                            Dim Validation = CheckPotatoCode(Code)
                            If Validation.IsT1() Then _PotatoFeatures.AddRange(Validation.AsT1())
                        Next
                        _PotatoFeatures = _PotatoFeatures.Distinct().ToList()
                    Catch ex As Exception
                        ' A missing/corrupt configuration must never be interpreted as authorization.
                        _PotatoFeatures.Clear()
                        Logger.Warn(ex, "读取已验证的赞助功能失败", LogBehavior.ToastIfDebug)
                    End Try
                End If
                Return _PotatoFeatures
            End SyncLock
        End Get
    End Property

    ''' <summary>Validate a code for this device. A string result describes a validation failure.</summary>
    Friend Function CheckPotatoCode(Key As String) As OneOf(Of List(Of String), String)
        If String.IsNullOrEmpty(Key) Then Return "输入的土豆码为空。"
        If Key.Length < 30 AndAlso Key.ContainsIgnoreCase(Identify) Then
            Return $"你输入的是识别码，不是土豆码。{vbCrLf}要获取土豆码，请在爱发电私信发送【土豆 {Identify}】。"
        End If
        If Key.Length > 85 AndAlso Key.Length < 90 AndAlso Key.EndsWithF("=") Then
            Return $"你输入的是解锁码，不是土豆码。{vbCrLf}要获取土豆码，请在爱发电私信发送【土豆】。"
        End If
        If Key.Length > 150 AndAlso Key.Length < 165 AndAlso Key.EndsWithF("11") Then
            Return $"你输入的是更新密钥，不是土豆码。{vbCrLf}要获取土豆码，请在爱发电私信发送【土豆】。"
        End If
        If Key.Length < 4 OrElse Not Key.StartsWithF("CD", ignoreCase:=True) OrElse Not Key.Contains("#") Then
            Return $"你输入的不是土豆码。{vbCrLf}要获取土豆码，请在爱发电私信发送【土豆 {Identify}】。"
        End If
        If Key(2) > Versions.PotatoVersion Then
            Return $"这个土豆码得在新版 PCL 上才能使用。{vbCrLf}请更新 PCL，或是在爱发电私信发送【下载】，然后在新下载的 PCL 上输入土豆码。"
        End If
        If Versions.PotatoVersion > Key(3) Then
            Return $"这个土豆码不太新鲜，已经过期了。{vbCrLf}请在爱发电私信重新发送【土豆 {Identify}】，获取更新鲜的土豆码。"
        End If
        If Not Key.StartsWithF("CD") Then
            Return $"土豆码区分大小写，但你把一些大写的东西输成小写的了。{vbCrLf}你可以选中文本，然后右键复制粘贴，不必手动输入。"
        End If

        Try
            Dim Parts = Key.Substring(4).Replace("p", "=").Replace("%", "p").Split("#"c)
            If Parts.Length <> 2 Then Throw New FormatException("土豆码的签名和内容格式有误")
            Dim Payload = Encoding.UTF8.GetString(Convert.FromBase64String(Parts(1)))
            Dim PublicKey = ModRecoveredConfig.GetRecoveredSetting("PCL_POTATO_PUBLIC_KEY", RecoveredPublicVerificationKey)
            If String.IsNullOrWhiteSpace(PublicKey) Then Throw New CryptographicException("未配置土豆码验证公钥")
            If Convert.FromBase64String(PublicKey).Length <> 72 Then Throw New CryptographicException("验证公钥必须为 P-256 ECCPUBLICBLOB")
            If Convert.FromBase64String(Parts(0)).Length <> 64 Then Throw New CryptographicException("签名必须为 P-256 的 64 字节格式")
            ' Preserve the original device/version/payload binding and ECDSA verification.
            ' The current MeloongCore requires the public key explicitly instead of supplying a default.
            CryptographyUtils.EcdsaVerify(Identify & Key(2) & Key(3) & Payload, Parts(0), PublicKey)
            Return Payload.Split({","c}, StringSplitOptions.RemoveEmptyEntries).ToList()
        Catch ex As Exception
            ' Do not copy codes or signed payloads into diagnostic logs.
            Logger.Warn("土豆码校验失败（格式、公钥或签名不匹配）", LogBehavior.ToastIfDebug)
            Return $"土豆码有误，请检查私信显示的识别码应当为 {Identify}，并核对输入内容。{vbCrLf}错误原因：{ex.Message}"
        End Try
    End Function

    Public Sub InputPotatoCode(IsUpdating As Boolean)
        ' This is the complete surviving input behavior of the supplied release.
        Hint("请使用 PCL 快照版输入土豆码！如果你赞助过 PCL，可以在爱发电私信发送【下载】获取 PCL 快照版！")
    End Sub

    Friend Sub GeneratePotatoCode()
        ' Intentionally empty, as in the sample: no generator or private signing key was recovered.
    End Sub

    ''' <summary>Reproduce the sample's local device ID while storing the per-install seed in PCLDebug.</summary>
    Friend Function GetIdentify() As String
        Try
            If ApplicationStartTick < 1 Then Return "0000-0000-0000-0000"
            Dim HardwareId As String
            Try
                Dim Entry = RegistryUtils.Read("HKEY_LOCAL_MACHINE\SYSTEM\HardwareConfig", "LastConfig")
                HardwareId = CStr(If(Entry.Item1, "Unknown")).ToUpperInvariant().Trim("{"c, "}"c)
            Catch ex As Exception
                Logger.Warn(ex, "获取主板标识码失败", LogBehavior.ToastIfDebug)
                HardwareId = "Unknown"
            End Try

            Dim Seed As String
            Try
                Seed = If(Settings.Get(Of String)("Identify"), "")
            Catch
                Seed = ""
            End Try
            If Seed.Length < 3 Then
                Seed = GetTimeMs().ToString() & My.Computer.Info.AvailablePhysicalMemory.ToString()
                ' Settings retains RegFolder=PCLDebug; this never writes the official PCL registry branch.
                Settings.Set("Identify", Seed)
            End If
            Dim Digest = (HardwareId & Seed).GetStableHashCode().ToString("X").EnsureLength("7"c, 16)
            Return $"{Digest.Substring(4, 4)}-{Digest.Substring(12, 4)}-{Digest.Substring(0, 4)}-{Digest.Substring(8, 4)}"
        Catch ex As Exception
            Logger.Error(ex, "PCL 无法获取设备识别码，这可能会导致部分设置无法正常存储。", LogBehavior.AlertThenFeedback)
            Return "0000-0000-0000-0000"
        End Try
    End Function

End Module
