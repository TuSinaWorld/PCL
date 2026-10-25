Imports System.Diagnostics

''' <summary>Fork-specific deployment values, separated from recovered source code.</summary>
Friend Module ModRecoveredConfig
    Private ReadOnly LocalConfiguration As New Lazy(Of JObject)(AddressOf LoadLocalConfiguration, True)

    Friend Function GetRecoveredSetting(Name As String, Optional DefaultValue As String = "") As String
        If String.IsNullOrWhiteSpace(Name) Then Throw New ArgumentException("配置名称不能为空。", NameOf(Name))
        Dim EnvironmentValue = Environment.GetEnvironmentVariable(Name)
        If EnvironmentValue IsNot Nothing Then Return EnvironmentValue
        Dim Config = LocalConfiguration.Value
        Dim Value = Config.GetValue(Name, StringComparison.OrdinalIgnoreCase)
        If Value Is Nothing Then Value = Config.GetValue(GetLocalName(Name), StringComparison.OrdinalIgnoreCase)
        If Value Is Nothing OrElse Value.Type = JTokenType.Null Then Return DefaultValue
        Return Value.ToString()
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
