' Client-side behavior reconstructed from the supplied 2.12.8.2 assembly and adapted to 2.99.
' Deployment credentials are loaded from local configuration; no extracted secrets are committed.
Friend Module ModSecret

    Public Const VersionBranchMain As String = "OpenSource"
    Public Const RegFolder As String = "PCLDebug"
    Public OAuthClientId As String = GetRecoveredSetting("PCL_MS_CLIENT_ID", "")
    Public CurseForgeAPIKey As String = GetRecoveredSetting("PCL_CURSEFORGE_API_KEY", "")
    Public Const ClsBaseUrl As String = ""

    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If UrlWithMark Is Nothing Then Throw New ArgumentNullException(NameOf(UrlWithMark))
        If Not UrlWithMark.EndsWith("{CDN}", StringComparison.Ordinal) Then Return UrlWithMark
        Dim Url = UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
        Dim SigningSecret = GetRecoveredSetting("PCL_CDN_SIGNING_SECRET", "")
        If String.IsNullOrEmpty(SigningSecret) Then Return Url
        Dim Parsed As Uri = Nothing
        If Not Uri.TryCreate(Url, UriKind.Absolute, Parsed) OrElse
           (Parsed.Scheme <> Uri.UriSchemeHttps AndAlso Parsed.Scheme <> Uri.UriSchemeHttp) Then
            Throw New ArgumentException("CDN 地址必须为 HTTP 或 HTTPS 地址。", NameOf(UrlWithMark))
        End If
        Dim RandomPart = RandomInteger(0, Integer.MaxValue - 2).ToString("x").PadLeft(8, "0"c)
        Dim Timestamp = GetUnixTimestampUtc().ToString(Globalization.CultureInfo.InvariantCulture)
        Dim UserId = GetRecoveredSetting("PCL_CDN_SIGNING_USER_ID", "0")
        Dim PathStart = Url.IndexOf("/"c, Url.IndexOf("://", StringComparison.Ordinal) + 3)
        Dim SignedPath = If(PathStart < 0, "/", Url.Substring(PathStart))
        Dim Digest = CryptographyUtils.ComputeHash(String.Join("-", {SignedPath, Timestamp, RandomPart, UserId, SigningSecret}))
        Return Url & If(Url.Contains("?"), "&", "?") & "sign=" & String.Join("-", {Timestamp, RandomPart, UserId, Digest})
    End Function

    ''' <summary>设置 UA、Referer 及对应主机的 API 鉴权头。</summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Req Is Nothing Then Throw New ArgumentNullException(NameOf(Req))
        If Not Req.Headers.UserAgent.Any Then
            Dim Target As Uri = Nothing
            Dim Host = If(Uri.TryCreate(Url, UriKind.Absolute, Target), Target.IdnHost, "")
            If Host.Equals("baidu.com", StringComparison.OrdinalIgnoreCase) OrElse Host.EndsWith(".baidu.com", StringComparison.OrdinalIgnoreCase) OrElse
               Host.Equals("baidupcs.com", StringComparison.OrdinalIgnoreCase) OrElse Host.EndsWith(".baidupcs.com", StringComparison.OrdinalIgnoreCase) Then
                Req.Headers.TryAddWithoutValidation("User-Agent", "LogStatistic")
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.TryAddWithoutValidation("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.TryAddWithoutValidation("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders AndAlso Req.Headers.Referrer Is Nothing Then
            Req.Headers.Referrer = New Uri($"http://{VersionCode}.open.pcl2.server/")
        End If
        Dim RequestUri As Uri = Nothing
        If Uri.TryCreate(Url, UriKind.Absolute, RequestUri) AndAlso RequestUri.Scheme = Uri.UriSchemeHttps Then
            Dim Host = RequestUri.IdnHost
            If Host.Equals("api.curseforge.com", StringComparison.OrdinalIgnoreCase) OrElse
               Host.Equals("forgecdn.net", StringComparison.OrdinalIgnoreCase) OrElse Host.EndsWith(".forgecdn.net", StringComparison.OrdinalIgnoreCase) Then
                Dim ApiKey = If(CurseForgeAPIKey, "").Trim()
                If ApiKey.Length > 0 AndAlso Not Req.Headers.Contains("x-api-key") Then Req.Headers.Add("x-api-key", ApiKey)
            End If
        End If
    End Sub

End Module
