Imports System
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Reflection
Imports Newtonsoft.Json.Linq

Public Module HeaderTestContext
    Public Const VersionBaseName As String = "2.99.0.0"
    Public Const VersionCode As Integer = 413
    Public Const BuildType As Integer = 50
    Public Function RandomInteger(Minimum As Integer, Maximum As Integer) As Integer
        Return 42
    End Function
    Public Function GetUnixTimestampUtc() As Long
        Return 1700000000L
    End Function
End Module

' Records only test-fixture input in memory; no actual key is printed or read by the harness.
Public Class CryptographyUtils
    Public Shared LastInput As String
    Public Shared Function ComputeHash(Input As String) As String
        LastInput = Input
        Return "fixture-digest"
    End Function
End Class

Public Class ConfigHeaderChecks
    Private Shared Count As Integer
    Private Shared Sub Check(Result As Boolean, Description As String)
        If Not Result Then Throw New Exception("Failed: " & Description)
        Count += 1
    End Sub
    Private Shared Function SignedRequest(Url As String, Optional Browser As Boolean = False) As HttpRequestMessage
        Dim Request As New HttpRequestMessage(HttpMethod.Get, Url)
        ModSecret.SecretHeadersSign(Url, Request, Browser)
        Return Request
    End Function
    Private Shared Function HasApiKey(Url As String) As Boolean
        Using Request = SignedRequest(Url)
            Return Request.Headers.Contains("x-api-key")
        End Using
    End Function
    Public Shared Function Run() As String
        Check(ModRecoveredConfig.GetRecoveredSetting("PCL_MS_CLIENT_ID", "missing") = "fixture-local-oauth", "OAuth alias from explicitly selected local JSON")
        Check(ModRecoveredConfig.GetRecoveredSetting("PCL_CURSEFORGE_API_KEY", "missing") = "fixture-local-api", "CurseForge alias from local JSON")
        Check(ModRecoveredConfig.GetRecoveredSetting("pcl_custom_setting", "missing") = "fixture-local-custom", "local JSON lookup ignores case")
        Check(ModRecoveredConfig.GetRecoveredSetting("PCL_UNDECLARED_TEST_SETTING", "fixture-default") = "fixture-default", "missing setting returns caller default")
        Check(ModSecret.OAuthClientId = "fixture-local-oauth" AndAlso ModSecret.CurseForgeAPIKey = "fixture-local-api", "startup API fields read local deployment settings")
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "fixture-env-oauth")
        Check(ModRecoveredConfig.GetRecoveredSetting("PCL_MS_CLIENT_ID", "missing") = "fixture-env-oauth", "environment overrides local JSON")
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "")
        Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", Nothing)
        Check(HasApiKey("https://api.curseforge.com/v1/mods"), "key added to exact HTTPS API host")
        Check(HasApiKey("https://API.CURSEFORGE.COM/v1/mods"), "API host check ignores case")
        Check(HasApiKey("https://edge.forgecdn.net/file"), "key added to configured CDN domain family")
        Check(Not HasApiKey("https://api.curseforge.com.evil.invalid/path"), "API lookalike suffix receives no key")
        Check(Not HasApiKey("https://forgecdn.net.evil.invalid/path"), "CDN lookalike suffix receives no key")
        Check(Not HasApiKey("https://evil.invalid/api.curseforge.com/forgecdn.net"), "domain in URL path receives no key")
        Check(Not HasApiKey("http://api.curseforge.com/v1/mods"), "HTTP API address receives no key")
        Check(Not HasApiKey("https://api.curseforge.com@evil.invalid/path"), "userinfo lookalike receives no key")
        Using Request = New HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods")
            Request.Headers.UserAgent.ParseAdd("FixtureAgent/1.0")
            Request.Headers.Referrer = New Uri("https://manual.invalid/")
            Request.Headers.Add("x-api-key", "fixture-manual-key")
            ModSecret.SecretHeadersSign(Request.RequestUri.AbsoluteUri, Request)
            Check(Request.Headers.UserAgent.ToString() = "FixtureAgent/1.0", "manual User-Agent preserved")
            Check(Request.Headers.Referrer.AbsoluteUri = "https://manual.invalid/", "manual Referer preserved")
            Check(Request.Headers.GetValues("x-api-key").Single() = "fixture-manual-key", "manual API header preserved")
        End Using
        Using Request = SignedRequest("https://ordinary.invalid/", True)
            Check(Request.Headers.UserAgent.ToString().Contains("Mozilla/5.0"), "browser mode adds browser UA")
            Check(Request.Headers.Referrer Is Nothing, "browser mode omits injected Referer")
        End Using
        Using Request = SignedRequest("https://www.baidu.com/path")
            Check(Request.Headers.UserAgent.ToString() = "LogStatistic", "Baidu host receives special UA")
        End Using
        Using Request = SignedRequest("https://evil.invalid/baidu.com")
            Check(Request.Headers.UserAgent.ToString() <> "LogStatistic", "Baidu name in path does not trigger special UA")
        End Using
        Dim Unmarked = "https://cdn.invalid/a b?already=1"
        Check(ModSecret.SecretCdnSign(Unmarked) = Unmarked, "unmarked CDN URL remains byte-for-byte unchanged")
        Dim Signed = ModSecret.SecretCdnSign("https://cdn.invalid/a b?already=1{CDN}")
        Check(Signed = "https://cdn.invalid/a%20b?already=1&sign=1700000000-0000002a-37-fixture-digest", "marked CDN URL signs with test deployment settings")
        Check(CryptographyUtils.LastInput = "/a%20b?already=1-1700000000-0000002a-37-fixture-local-signing", "CDN digest input uses local secret and deterministic random/time")
        Environment.SetEnvironmentVariable("PCL_CDN_SIGNING_SECRET", "fixture-env-signing")
        ModSecret.SecretCdnSign("https://cdn.invalid/test{CDN}")
        Check(CryptographyUtils.LastInput.EndsWith("fixture-env-signing", StringComparison.Ordinal), "environment secret overrides local signing secret")
        Environment.SetEnvironmentVariable("PCL_CDN_SIGNING_SECRET", Nothing)
        Dim ConfigurationField = GetType(ModRecoveredConfig).GetField("LocalConfiguration", BindingFlags.Static Or BindingFlags.NonPublic)
        Dim Configuration = CType(ConfigurationField.GetValue(Nothing), Lazy(Of JObject)).Value
        Dim SavedSecret = Configuration("CdnSigningSecret")
        Configuration.Remove("CdnSigningSecret")
        Check(ModSecret.SecretCdnSign("https://cdn.invalid/a b{CDN}") = "https://cdn.invalid/a%20b", "missing signing secret strips marker without adding signature")
        Configuration("CdnSigningSecret") = SavedSecret
        Dim InvalidAddressRejected As Boolean
        Try
            ModSecret.SecretCdnSign("file:///fixture{CDN}")
        Catch ex As ArgumentException
            InvalidAddressRejected = True
        End Try
        Check(InvalidAddressRejected, "configured signing rejects non-HTTP CDN URLs")
        Dim Saved = ModSecret.CurseForgeAPIKey
        ModSecret.CurseForgeAPIKey = ""
        Check(Not HasApiKey("https://api.curseforge.com/v1/mods"), "missing API deployment key emits no header")
        ModSecret.CurseForgeAPIKey = Saved
        Return Count.ToString() & " checks passed; only fixture configuration was used; no network or target executable ran."
    End Function
End Class
