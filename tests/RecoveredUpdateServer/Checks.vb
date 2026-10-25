Imports System
Imports System.IO
Imports System.Text
Imports Newtonsoft.Json.Linq

Public Class RecoveredUpdateServerChecks
    Private Shared Count As Integer
    Private Shared Sub Check(Condition As Boolean, Message As String)
        If Not Condition Then Throw New Exception("FAIL: " & Message)
        Count += 1
    End Sub

    Public Shared Function Run(Root As String) As String
        Count = 0
        Directory.CreateDirectory(Root)
        Context.PathTemp = Root
        Context.PathExe = Path.Combine(Root, "current-fork.exe")
        File.WriteAllText(Context.PathExe, "current fork bytes", Encoding.UTF8)
        Context.Config("PCL_SERVER_CONFIG_URL") = "https://old-backend.invalid/config.json"
        Context.Config("PCL_UPDATE_BASE_URL") = "https://old-backend.invalid/latest.exe"
        Context.Config("PCL_UPDATE_ALLOW_AUTOMATIC") = "true"
        Context.Config("PCL_SERVER_ALLOW_SETUP_ACTIONS") = "true"
        Directory.CreateDirectory(ModRecoveredUpdate.RecoveredCacheRoot)
        Dim CachePath = Path.Combine(ModRecoveredUpdate.RecoveredCacheRoot, "ServerConfig.json")
        Dim Cached = "{""Version"":999999,""Notices"":[{""id"":1,""actions"":{""stop"":true,""setup"":{""TestFlag"":false}}}],""Update"":{""Release"":{""VersionCode"":999999,""Url"":""https://old-backend.invalid/replace.exe""}}}"
        File.WriteAllText(CachePath, Cached)
        ModRecoveredServer.ServerConfig = JObject.Parse(Cached)
        Context.Response = Cached
        Dim BeforeFiles = Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length
        ModRecoveredServer.ServerLoader.Start()
        Check(ModRecoveredServer.ServerConfig Is Nothing, "legacy config and cached notices are not consumed")
        Check(ModRecoveredServer.ServerLoader.Output = 0, "disabled loader completes locally")
        Check(Context.Requests = 0, "configured backend URL causes zero requests")
        Check(Settings.Get(Of Boolean)("TestFlag"), "cached remote settings are not applied")
        Check(File.ReadAllText(CachePath) = Cached, "disabled backend does not rewrite cached user files")
        Check(Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length = BeforeFiles, "loader creates no files")
        Check(Not ModRecoveredServer.ServerRequirement("versioncode >=", New JValue(0)), "notice requirements cannot reactivate actions")
        Check(ModRecoveredUpdate.RecoveredIsLauncherNewest() Is Nothing, "no claim about remote latest version")
        ModRecoveredUpdate.UpdateCheckByButton()
        ModRecoveredUpdate.UpdateStart(False, New String() {"https://old-backend.invalid/forced.exe"})
        ModRecoveredUpdate.UpdateStart(True, New String() {"https://old-backend.invalid/silent.exe"})
        Check(Not ModRecoveredUpdate.IsUpdating AndAlso Not ModRecoveredUpdate.IsUpdateWaitingRestart, "manual and silent entry points cannot stage updates")
        Check(ModRecoveredUpdate.GetRecoveredUpdateManifest() Is Nothing AndAlso ModRecoveredUpdate.GetRecoveredUpdateManifest(True) Is Nothing, "no manifest is exposed to callers")
        Check(Context.Requests = 0, "manual and silent update entry points cause zero requests")
        Dim Victim = Path.Combine(Root, "victim.exe")
        Dim Candidate = Path.Combine(Root, "candidate.exe")
        File.WriteAllText(Victim, "original bytes")
        File.WriteAllText(Candidate, "candidate bytes")
        ModRecoveredUpdate.UpdateReplace(Diagnostics.Process.GetCurrentProcess().Id, Victim, Candidate, True)
        ModRecoveredUpdate.UpdateReplace(-1, "invalid path" & ChrW(0), "invalid path" & ChrW(0), True)
        ModRecoveredUpdate.UpdateRestart(True)
        ModRecoveredUpdate.UpdateRestart(False)
        Check(File.ReadAllText(Victim) = "original bytes", "legacy replacement never writes target")
        Check(File.ReadAllText(Candidate) = "candidate bytes", "legacy replacement does not consume staged files")
        Check(Not File.Exists(Victim & ".bak"), "legacy replacement creates no backup")
        Dim Exported = Path.Combine(Root, "exported-launcher.exe")
        ModRecoveredUpdate.CopyCurrentLauncherForExport(Exported)
        Check(File.ReadAllBytes(Context.PathExe).SequenceEqual(File.ReadAllBytes(Exported)), "actual export helper copies exact current executable")
        ModRecoveredUpdate.DownloadLatestPCL()
        Check(File.ReadAllBytes(Context.PathExe).SequenceEqual(File.ReadAllBytes(ModRecoveredUpdate.RecoveredLatestLauncherPath)), "export compatibility copy embeds exact current executable")
        File.WriteAllText(Context.PathExe, "new current fork bytes")
        ModRecoveredUpdate.DownloadLatestPCL()
        Check(File.ReadAllBytes(Context.PathExe).SequenceEqual(File.ReadAllBytes(ModRecoveredUpdate.RecoveredLatestLauncherPath)), "export ignores stale cached launcher")
        Check(Context.Requests = 0, "local export performs no backend requests")
        Return Count.ToString() & " disabled-backend behavior checks passed; no network, launcher or native operation executed."
    End Function
End Class
