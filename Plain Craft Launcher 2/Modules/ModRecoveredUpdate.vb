Imports System.Security.Cryptography

' Retained names prevent older internal callers from re-enabling removed backend features.
Friend Module ModRecoveredUpdate
    Friend Const IsUpdating As Boolean = False
    Friend Const IsUpdateWaitingRestart As Boolean = False
    Friend Const UnsupportedUpdateMessage As String = "本分支不支持启动器自更新，请手动替换启动器文件。"

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
        Return Nothing
    End Function

    Friend Sub UpdateCheckByButton()
        Hint(UnsupportedUpdateMessage)
    End Sub

    Friend Sub UpdateStart(Silent As Boolean, Optional UrlsOverride As IEnumerable(Of String) = Nothing)
        If Not Silent Then Hint(UnsupportedUpdateMessage)
    End Sub

    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
        ' No staging, process launch or restart is supported.
    End Sub

    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        ' Legacy --update must never wait for a process or replace any file.
    End Sub

    Friend Function GetRecoveredUpdateManifest(Optional ReleaseOnly As Boolean = False) As JObject
        Return Nothing
    End Function

    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        ' Compatibility fallback for local exports: always copy this exact executable, never a server build.
        Directory.CreateDirectory(RecoveredCacheRoot)
        CopyCurrentLauncherForExport(RecoveredLatestLauncherPath)
    End Sub

    Friend Sub CopyCurrentLauncherForExport(Destination As String)
        File.Copy(PathExe, Destination, True)
    End Sub
End Module
