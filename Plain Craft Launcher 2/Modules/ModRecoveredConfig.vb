Imports System.Diagnostics

''' <summary>Fork-specific deployment values, separated from recovered source code.</summary>
Friend Module ModRecoveredConfig
    Private ReadOnly LocalConfiguration As New Lazy(Of JObject)(AddressOf LoadLocalConfiguration, True)
    Private ReadOnly EmbeddedConfiguration As New Lazy(Of JObject)(AddressOf LoadEmbeddedConfiguration, True)

    Friend Function GetRecoveredSetting(Name As String, Optional DefaultValue As String = "") As String
        If String.IsNullOrWhiteSpace(Name) Then Throw New ArgumentException("配置名称不能为空。", NameOf(Name))
        Dim EnvironmentValue = Environment.GetEnvironmentVariable(Name)
        If EnvironmentValue IsNot Nothing Then Return EnvironmentValue
        ' On .NET Framework the single-value API also returns Nothing for a present empty variable.
        For Each EnvironmentName As String In Environment.GetEnvironmentVariables().Keys
            If String.Equals(EnvironmentName, Name, StringComparison.OrdinalIgnoreCase) Then Return ""
        Next
        Dim Config = LocalConfiguration.Value
        Dim Value = Config.GetValue(Name, StringComparison.OrdinalIgnoreCase)
        If Value Is Nothing Then Value = Config.GetValue(GetLocalName(Name), StringComparison.OrdinalIgnoreCase)
        ' An explicitly cleared local value must not reactivate an embedded credential.
        If Value IsNot Nothing Then Return If(Value.Type = JTokenType.Null, "", Value.ToString())
        Dim EmbeddedName = GetEmbeddedName(GetLocalName(Name))
        If EmbeddedName IsNot Nothing Then
            Value = EmbeddedConfiguration.Value.GetValue(EmbeddedName, StringComparison.OrdinalIgnoreCase)
            If Value IsNot Nothing AndAlso Value.Type <> JTokenType.Null Then Return Value.ToString()
        End If
        Return DefaultValue
    End Function

    Private Function GetEmbeddedName(Name As String) As String
        Select Case Name.ToUpperInvariant()
            Case "OAUTHCLIENTID" : Return "OAuthClientId"
            Case "CURSEFORGEAPIKEY" : Return "CurseForgeAPIKey"
            Case Else : Return Nothing
        End Select
    End Function

    Private Function LoadEmbeddedConfiguration() As JObject
        Try
            ' Resolve the resource on this module's assembly, not the entry or calling assembly.
            Using Resource = GetType(ModRecoveredConfig).Assembly.GetManifestResourceStream("PCL.EmbeddedClientConfig.json")
                If Resource Is Nothing Then Return New JObject()
                If Not Resource.CanSeek OrElse Resource.Length > 1024 * 1024 Then Throw New InvalidDataException("内嵌配置超过 1 MB。")
                Using Reader As New StreamReader(Resource, New UTF8Encoding(False, True))
                    Using JsonReader As New Newtonsoft.Json.JsonTextReader(Reader) With {.DateParseHandling = Newtonsoft.Json.DateParseHandling.None}
                        Dim Config = JObject.Load(JsonReader, New JsonLoadSettings With {.DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error})
                        If JsonReader.Read() Then Throw New InvalidDataException("内嵌配置包含额外内容。")
                        Dim Names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                        For Each Entry In Config.Properties()
                            If GetEmbeddedName(Entry.Name) Is Nothing OrElse Not Names.Add(Entry.Name) Then Throw New InvalidDataException("内嵌配置包含非白名单字段或重复字段。")
                            If Entry.Value.Type <> JTokenType.String AndAlso Entry.Value.Type <> JTokenType.Null Then Throw New InvalidDataException("内嵌配置字段类型无效。")
                        Next
                        Return Config
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ' Startup configuration failures never expose resource contents or credential values.
            Trace.WriteLine("无法读取 PCL 内嵌客户端配置：" & ex.GetType().Name)
            Return New JObject()
        End Try
    End Function

    Private Function GetLocalName(Name As String) As String
        Select Case Name.ToUpperInvariant()
            Case "PCL_MS_CLIENT_ID" : Return "OAuthClientId"
            Case "PCL_CURSEFORGE_API_KEY" : Return "CurseForgeAPIKey"
            Case "PCL_CDN_SIGNING_SECRET" : Return "CdnSigningSecret"
            Case "PCL_CDN_SIGNING_USER_ID" : Return "CdnSigningUserId"
            Case Else : Return Name
        End Select
    End Function

    Private Function LoadLocalConfiguration() As JObject
        Dim ConfigPath = Environment.GetEnvironmentVariable("PCL_RECOVERED_CONFIG")
        If String.IsNullOrWhiteSpace(ConfigPath) Then
            ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PCL", "recovered-config.local.json")
        End If
        Try
            If Not File.Exists(ConfigPath) Then Return New JObject()
            If New FileInfo(ConfigPath).Length > 1024 * 1024 Then Throw New InvalidDataException("配置文件超过 1 MB。")
            Return JObject.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8))
        Catch ex As Exception
            ' Avoid invoking Settings/Logger during their own static initialization, and never log values.
            Trace.WriteLine("无法读取 PCL 本地恢复配置：" & ex.GetType().Name)
            Return New JObject()
        End Try
    End Function
End Module
