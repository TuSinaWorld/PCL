' Device identity remains necessary for local settings encryption; sponsorship is unsupported.
Friend Module ModRecoveredIdentity
    Friend Const UnsupportedSponsorshipMessage As String = "本分支不支持赞助权益与土豆码。"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Friend ReadOnly Property PotatoFeatures As List(Of String)
        Get
            Return New List(Of String)()
        End Get
    End Property

    Friend Function CheckPotatoCode(Key As String) As OneOf(Of List(Of String), String)
        Return UnsupportedSponsorshipMessage
    End Function

    Public Sub InputPotatoCode(IsUpdating As Boolean)
        Hint(UnsupportedSponsorshipMessage)
    End Sub

    Friend Sub GeneratePotatoCode()
        Hint(UnsupportedSponsorshipMessage)
    End Sub

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
