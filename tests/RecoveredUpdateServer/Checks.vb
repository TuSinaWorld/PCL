Imports System
Imports System.IO
Imports System.IO.Compression
Imports System.Reflection
Imports Newtonsoft.Json.Linq

Public Class RecoveredUpdateServerChecks
    Private Shared Count As Integer
    Private Shared Sub Check(Value As Boolean, Description As String)
        If Not Value Then Throw New Exception("Failed: " & Description)
        Count += 1
    End Sub
    Public Shared Function Run(Root As String) As String
        Directory.CreateDirectory(Root)
        Context.PathTemp = Root
        Context.PathExe = GetType(RecoveredUpdateServerChecks).Assembly.Location
        Context.Config.Clear()
        Context.Response = Nothing
        Context.Requests = 0
        ModRecoveredServer.ServerSub(ModRecoveredServer.ServerLoader)
        Check(Context.Requests = 0 AndAlso ModRecoveredServer.ServerConfig Is Nothing, "unconfigured server makes no network request")
        Check(Not ModRecoveredServer.RecoveredConfigBoolean("PCL_UPDATE_ALLOW_AUTOMATIC", False), "automatic updates default to disabled")
        ModRecoveredUpdate.UpdateStart(True)
        Check(Not ModRecoveredUpdate.IsUpdating AndAlso Not ModRecoveredUpdate.IsUpdateWaitingRestart, "unsolicited silent update cannot start")
        Check(ModRecoveredServer.ServerRequirement("versioncode >=", New JValue(400)), "current version lower bound")
        Check(Not ModRecoveredServer.ServerRequirement("versioncode <=", New JValue(400)), "current version upper bound")
        Check(ModRecoveredServer.ServerRequirement("opencount >=", New JValue(10)), "open count condition")
        Check(ModRecoveredServer.ServerRequirement("uniqueaddress =", New JValue("TEST-ID")), "device equality condition")
        Check(ModRecoveredServer.ServerRequirement("setupinteger >=", JObject.Parse("{""SystemCount"":10}")), "integer setting condition")
        Check(ModRecoveredServer.ServerRequirement("setupboolean =", JObject.Parse("{""TestFlag"":true}")), "boolean setting condition")
        Check(Not ModRecoveredServer.ServerRequirement("unknown", New JValue(True)), "unknown condition fails closed")
        Check(Not ModRecoveredServer.ServerRequirement("setupinteger <=", JObject.Parse("{""unknown"":1}")), "unknown setting condition fails closed")
        Context.Config("PCL_SERVER_CONFIG_URL") = "http://127.0.0.1:54321/config.json"
        Context.Response = "{""Version"":1,""Update"":{""Release"":{""VersionCode"":415,""DownloadUrls"":[""launcher.zip""]}}}"
        ModRecoveredServer.ServerSub(ModRecoveredServer.ServerLoader)
        Check(Context.Requests = 1 AndAlso ModRecoveredServer.ServerConfig IsNot Nothing, "configured server parses JSON")
        Check(ModRecoveredUpdate.RecoveredIsLauncherNewest().GetValueOrDefault(True) = False, "new version detected from self-hosted manifest")
        Dim RemoteManifest = CType(ModRecoveredServer.ServerConfig("Update")("Release"), JObject)
        RemoteManifest("VersionCode") = New JValue("2.99.1")
        Check(Not ModRecoveredUpdate.RecoveredIsLauncherNewest().HasValue, "display version cannot crash logger update-status checks")
        RemoteManifest("VersionCode") = New JValue("2147483648")
        Check(Not ModRecoveredUpdate.RecoveredIsLauncherNewest().HasValue, "out-of-range version code returns unknown")
        RemoteManifest("VersionCode") = New JValue(-1)
        Check(Not ModRecoveredUpdate.RecoveredIsLauncherNewest().HasValue, "negative version code returns unknown")
        RemoteManifest("VersionCode") = New JValue(415)
        Context.Response = "{""Version"":""fixture-version"",""Notices"":[{""id"":""bad-id""},{""id"":2147483648},{""id"":5,""description"":""fixture-notice""}],""Link"":{""Version"":1}}"
        ModRecoveredServer.ServerSub(ModRecoveredServer.ServerLoader)
        Check(ModRecoveredServer.ServerConfig("Link") IsNot Nothing, "malformed notices cannot fail valid link configuration loading")
        Check(File.ReadAllText(Path.Combine(ModRecoveredUpdate.RecoveredCacheRoot, "Notice.seen")) = "5", "valid notices after malformed ids are still processed")
        Settings.Entries("UiFixture") = New Settings.Setting(0)
        Settings.Entries("InstanceFixture") = New Settings.Setting(0, Settings.Sources.Instance)
        Dim ActionsMethod = GetType(ModRecoveredServer).GetMethod("RecoveredNoticeActions", BindingFlags.Static Or BindingFlags.NonPublic)
        ActionsMethod.Invoke(Nothing, New Object() {JObject.Parse("{""setup"":{""UiFixture"":7}}"), New Uri("https://example.invalid/config.json"), True})
        Check(Settings.Get(Of Integer)("UiFixture") = 7 AndAlso Context.UiDispatchCalls > 0, "notice settings dispatch their UI callbacks through the UI helper")
        Dim InstanceRejected As Boolean
        Try
            ActionsMethod.Invoke(Nothing, New Object() {JObject.Parse("{""setup"":{""InstanceFixture"":7}}"), New Uri("https://example.invalid/config.json"), True})
        Catch ex As TargetInvocationException
            InstanceRejected = TypeOf ex.InnerException Is InvalidDataException
        End Try
        Check(InstanceRejected AndAlso Settings.Get(Of Integer)("InstanceFixture") = 0, "notice rejects instance settings without an instance context")
        Context.Response = Nothing
        ModRecoveredServer.ServerSub(ModRecoveredServer.ServerLoader)
        Check(ModRecoveredServer.ServerConfig IsNot Nothing, "same endpoint falls back to cached JSON")
        Context.Config("PCL_SERVER_CONFIG_URL") = "http://127.0.0.1:54322/other.json"
        Dim Rejected As Boolean
        Try
            ModRecoveredServer.ServerSub(ModRecoveredServer.ServerLoader)
        Catch ex As IOException
            Rejected = True
        End Try
        Check(Rejected AndAlso ModRecoveredServer.ServerConfig Is Nothing, "new endpoint cannot inherit another source's cache")
        Context.Config("PCL_UPDATE_BASE_URL") = "https://example.invalid/updates/"
        Dim UrlMethod = GetType(ModRecoveredUpdate).GetMethod("RecoveredUpdateUrls", BindingFlags.Static Or BindingFlags.NonPublic)
        Dim Manifest = JObject.Parse("{""DownloadUrls"":[""launcher.zip""]}")
        Dim Urls = CType(UrlMethod.Invoke(Nothing, New Object() {Manifest, Nothing, False}), System.Collections.Generic.List(Of String))
        Check(Urls(0) = "https://example.invalid/updates/launcher.zip", "relative download URLs use only explicit update base")
        Dim ArchivePath = Path.Combine(Root, "fixture.zip")
        Using Archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Create)
            Dim Entry = Archive.CreateEntry("../../escape.exe")
            Using Target = Entry.Open(), Source = File.OpenRead(Context.PathExe)
                Source.CopyTo(Target)
            End Using
        End Using
        Dim PreparedPath = Path.Combine(Root, "prepared.bin")
        Dim PrepareMethod = GetType(ModRecoveredUpdate).GetMethod("RecoveredPrepareExecutable", BindingFlags.Static Or BindingFlags.NonPublic)
        PrepareMethod.Invoke(Nothing, New Object() {ArchivePath, ".zip", PreparedPath, New JObject()})
        Check(File.Exists(PreparedPath) AndAlso Not File.Exists(Path.Combine(Path.GetDirectoryName(Root), "escape.exe")), "zip entry paths cannot escape the selected output")
        Dim HashRejected As Boolean
        Try
            Dim ValidateMethod = GetType(ModRecoveredUpdate).GetMethod("RecoveredValidateExecutable", BindingFlags.Static Or BindingFlags.NonPublic)
            ValidateMethod.Invoke(Nothing, New Object() {PreparedPath, New String("0"c, 64)})
        Catch ex As TargetInvocationException
            HashRejected = TypeOf ex.InnerException Is InvalidDataException
        End Try
        Check(HashRejected, "wrong executable SHA256 is rejected")
        Return Count.ToString() & " checks passed; no target executable, network, update replacement, or native patch was executed."
    End Function
End Class
