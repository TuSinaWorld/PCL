Imports System
Imports System.Diagnostics
Imports System.IO
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text

Public Class EmbeddedConfigurationChecks
    Private Shared Count As Integer
    Private Shared AssemblyRoot As String
    Private Shared ConfigurationRoot As String
    Private Shared ReadOnly Flags As BindingFlags = BindingFlags.Static Or BindingFlags.NonPublic Or BindingFlags.Public

    <DllImport("kernel32.dll", EntryPoint:="SetEnvironmentVariableW", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function SetNativeEnvironmentVariable(Name As String, Value As String) As Boolean
    End Function

    Private Shared Sub Check(Result As Boolean, Description As String)
        If Not Result Then Throw New Exception("Failed: " & Description)
        Count += 1
    End Sub

    Private Shared Function LoadFixture(Name As String, Optional LocalJson As String = Nothing) As Type
        Dim ConfigPath = Path.Combine(ConfigurationRoot, Name & ".json")
        If LocalJson IsNot Nothing Then File.WriteAllText(ConfigPath, LocalJson, New UTF8Encoding(False))
        Environment.SetEnvironmentVariable("PCL_RECOVERED_CONFIG", ConfigPath)
        Return Assembly.LoadFrom(Path.Combine(AssemblyRoot, Name & ".dll")).GetType("ModRecoveredConfig", True)
    End Function

    Private Shared Function Setting(ModuleType As Type, Name As String, Optional DefaultValue As String = "fixture-default") As String
        Return CStr(ModuleType.GetMethod("GetRecoveredSetting", Flags).Invoke(Nothing, New Object() {Name, DefaultValue}))
    End Function

    Public Shared Function Run(OutputRoot As String, RunRoot As String) As String
        AssemblyRoot = OutputRoot
        ConfigurationRoot = RunRoot
        Dim Messages As New StringWriter()
        Dim Listener As New TextWriterTraceListener(Messages)
        Trace.Listeners.Add(Listener)
        Try
            Dim EmbeddedOnly = LoadFixture("embedded-only")
            Check(Setting(EmbeddedOnly, "PCL_MS_CLIENT_ID") = "SYNTHETIC_ONLY_embedded_oauth", "embedded OAuth works with no local configuration file")
            Check(Setting(EmbeddedOnly, "PCL_CURSEFORGE_API_KEY") = "SYNTHETIC_ONLY_embedded_api", "embedded API key works with no local configuration file")
            Check(Setting(EmbeddedOnly, "oauthclientid") = "SYNTHETIC_ONLY_embedded_oauth", "whitelisted embedded field lookup ignores case")
            Check(Setting(EmbeddedOnly, "PCL_CDN_SIGNING_SECRET") = "fixture-default", "CDN signing secret has no embedded source")
            Check(Setting(EmbeddedOnly, "PCL_CDN_SIGNING_USER_ID") = "fixture-default", "CDN signing user has no embedded source")
            Check(Setting(EmbeddedOnly, "PCL_CUSTOM_SETTING") = "fixture-default", "arbitrary setting has no embedded source")
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "SYNTHETIC_ONLY_env_oauth")
            Environment.SetEnvironmentVariable("PCL_CURSEFORGE_API_KEY", "SYNTHETIC_ONLY_env_api")
            Check(Setting(EmbeddedOnly, "PCL_MS_CLIENT_ID") = "SYNTHETIC_ONLY_env_oauth", "environment overrides embedded OAuth")
            Check(Setting(EmbeddedOnly, "PCL_CURSEFORGE_API_KEY") = "SYNTHETIC_ONLY_env_api", "environment overrides embedded API key")
            ' Framework's managed setter deletes empty values; use the native API to exercise a present empty variable.
            Check(SetNativeEnvironmentVariable("PCL_MS_CLIENT_ID", ""), "native empty environment variable created")
            Check(Setting(EmbeddedOnly, "PCL_MS_CLIENT_ID") = "", "present empty environment variable disables embedded OAuth")
            Check(Setting(EmbeddedOnly, "pcl_ms_client_id") = "", "present empty environment lookup ignores query case")
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", Nothing)
            Check(SetNativeEnvironmentVariable("pcl_ms_client_id", ""), "native lowercase empty environment variable created")
            Check(Setting(EmbeddedOnly, "PCL_MS_CLIENT_ID") = "", "lowercase empty environment name disables uppercase setting lookup")
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", Nothing)
            Environment.SetEnvironmentVariable("PCL_CURSEFORGE_API_KEY", Nothing)

            Dim Local = LoadFixture("local-overrides", "{""OAuthClientId"":""SYNTHETIC_ONLY_alias_oauth"",""PCL_MS_CLIENT_ID"":""SYNTHETIC_ONLY_direct_oauth"",""CurseForgeAPIKey"":""SYNTHETIC_ONLY_local_api"",""CdnSigningSecret"":""SYNTHETIC_ONLY_local_cdn"",""CdnSigningUserId"":""37"",""PCL_CUSTOM_SETTING"":""SYNTHETIC_ONLY_local_custom""}")
            Check(Setting(Local, "PCL_MS_CLIENT_ID") = "SYNTHETIC_ONLY_direct_oauth", "direct local name overrides alias and embedded OAuth")
            Check(Setting(Local, "pcl_curseforge_api_key") = "SYNTHETIC_ONLY_local_api", "local alias overrides embedded API key ignoring case")
            Check(Setting(Local, "PCL_CDN_SIGNING_SECRET") = "SYNTHETIC_ONLY_local_cdn", "local CDN secret remains supported")
            Check(Setting(Local, "PCL_CDN_SIGNING_USER_ID") = "37", "local CDN user remains supported")
            Check(Setting(Local, "pcl_custom_setting") = "SYNTHETIC_ONLY_local_custom", "local arbitrary settings remain supported")
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", "SYNTHETIC_ONLY_env_local_oauth")
            Check(Setting(Local, "PCL_MS_CLIENT_ID") = "SYNTHETIC_ONLY_env_local_oauth", "environment overrides both local and embedded OAuth")
            Check(SetNativeEnvironmentVariable("PCL_MS_CLIENT_ID", ""), "native empty local override created")
            Check(Setting(Local, "PCL_MS_CLIENT_ID") = "", "empty environment variable disables local and embedded OAuth")
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", Nothing)
            Environment.SetEnvironmentVariable("PCL_CDN_SIGNING_SECRET", "SYNTHETIC_ONLY_env_cdn")
            Check(Setting(Local, "PCL_CDN_SIGNING_SECRET") = "SYNTHETIC_ONLY_env_cdn", "environment CDN configuration remains supported")
            Environment.SetEnvironmentVariable("PCL_CDN_SIGNING_SECRET", Nothing)

            Dim Cleared = LoadFixture("local-empty", "{""OAuthClientId"":"""",""CurseForgeAPIKey"":null}")
            Check(Setting(Cleared, "PCL_MS_CLIENT_ID") = "", "explicit local empty string disables embedded OAuth")
            Check(Setting(Cleared, "PCL_CURSEFORGE_API_KEY") = "", "explicit local null disables embedded API key")
            Dim PartialLocal = LoadFixture("local-missing", "{""OAuthClientId"":""SYNTHETIC_ONLY_partial_local""}")
            Check(Setting(PartialLocal, "PCL_MS_CLIENT_ID") = "SYNTHETIC_ONLY_partial_local", "present local field overrides embedded value")
            Check(Setting(PartialLocal, "PCL_CURSEFORGE_API_KEY") = "SYNTHETIC_ONLY_embedded_api", "missing local field falls back to embedded value")
            Dim InvalidLocal = LoadFixture("local-invalid", "{""OAuthClientId"":""SYNTHETIC_ONLY_invalid_local"",invalid")
            Check(Setting(InvalidLocal, "PCL_MS_CLIENT_ID") = "SYNTHETIC_ONLY_embedded_oauth", "malformed local file falls back to embedded value")
            Dim OversizeLocal = LoadFixture("local-oversize", New String("x"c, 1024 * 1024 + 1))
            Check(Setting(OversizeLocal, "PCL_MS_CLIENT_ID") = "SYNTHETIC_ONLY_embedded_oauth", "oversize local file falls back to embedded value")

            Dim EmptyEmbedded = LoadFixture("embedded-empty")
            Check(Setting(EmptyEmbedded, "PCL_MS_CLIENT_ID") = "", "embedded empty string is preserved")
            Check(Setting(EmptyEmbedded, "PCL_CURSEFORGE_API_KEY") = "fixture-default", "embedded null returns caller default")
            Dim PartialEmbedded = LoadFixture("embedded-missing")
            Check(Setting(PartialEmbedded, "PCL_MS_CLIENT_ID") = "SYNTHETIC_ONLY_partial_oauth", "partial embedded object supplies its valid field")
            Check(Setting(PartialEmbedded, "PCL_CURSEFORGE_API_KEY") = "fixture-default", "missing embedded field returns caller default")

            For Each Name In New String() {"no-resource", "wrong-resource-name", "embedded-corrupt", "embedded-unknown", "embedded-extra", "embedded-env-name", "embedded-number", "embedded-boolean", "embedded-object", "embedded-array-value", "embedded-duplicate", "embedded-case-duplicate", "embedded-null-root", "embedded-array-root", "embedded-trailing", "embedded-invalid-utf8", "embedded-oversize"}
                Dim Invalid = LoadFixture(Name)
                If Name = "no-resource" Then Check(Invalid.Assembly.GetManifestResourceNames().Length = 0, "missing resource fixture actually has no manifest resources")
                Check(Setting(Invalid, "PCL_MS_CLIENT_ID") = "fixture-default", Name & " safely returns OAuth default")
                Check(Setting(Invalid, "PCL_CURSEFORGE_API_KEY") = "fixture-default", Name & " safely returns API default")
                Check(Setting(Invalid, "PCL_CDN_SIGNING_SECRET") = "fixture-default", Name & " never supplies embedded CDN secret")
                Check(Setting(Invalid, "PCL_CDN_SIGNING_USER_ID") = "fixture-default", Name & " never supplies embedded CDN user")
                Check(Setting(Invalid, "PCL_CUSTOM_SETTING") = "fixture-default", Name & " never supplies arbitrary embedded setting")
            Next
            Dim Second = LoadFixture("embedded-second")
            Check(Setting(Second, "PCL_MS_CLIENT_ID") = "SYNTHETIC_ONLY_second_oauth", "second module assembly reads its own OAuth resource")
            Check(Setting(Second, "PCL_CURSEFORGE_API_KEY") = "SYNTHETIC_ONLY_second_api", "second module assembly reads its own API resource")
            Check(Setting(EmbeddedOnly, "PCL_MS_CLIENT_ID") = "SYNTHETIC_ONLY_embedded_oauth", "first module assembly retains its own resource after another assembly loads")
            Check(Second.Assembly IsNot EmbeddedOnly.Assembly, "production modules were compiled into distinct assemblies")
            Dim DateString = LoadFixture("embedded-date-string")
            Check(Setting(DateString, "PCL_MS_CLIENT_ID") = "2030-01-02T03:04:05Z", "JSON string values are not coerced into date tokens")
            Check(Setting(DateString, "PCL_CURSEFORGE_API_KEY") = "SYNTHETIC_ONLY_date_fixture_api", "valid string resource remains usable in full")
            Trace.Flush()
            Check(Not Messages.ToString().Contains("SYNTHETIC_ONLY_"), "configuration failures never trace credential values")
            Check(Messages.ToString().Contains("InvalidDataException") AndAlso Messages.ToString().Contains("JsonReaderException"), "failure logging paths were exercised without values")
            Return Count.ToString() & " checks passed across 27 compiled production module assemblies; synthetic fixtures only; no network or target executable ran."
        Finally
            Trace.Listeners.Remove(Listener)
            Listener.Dispose()
            Messages.Dispose()
        End Try
    End Function
End Class
