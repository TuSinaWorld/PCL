' The recovered server flow accepts only an explicitly configured, self-hosted JSON source.
' No original service URL or private authentication material is included here.
Friend Module ModRecoveredServer

    Public ServerConfig As JObject
    Public ServerLoader As New LoaderTask(Of Integer, Integer)("fork 配置更新", AddressOf ServerSub, Priority:=ThreadPriority.BelowNormal) With {
        .ReloadTimeout = 1000 * 60 * 60}

    Friend Function RecoveredJsonValue(Container As JObject, ParamArray Names As String()) As JToken
        If Container Is Nothing Then Return Nothing
        For Each Name In Names
            Dim Value As JToken = Nothing
            If Container.TryGetValue(Name, StringComparison.OrdinalIgnoreCase, Value) AndAlso Value.Type <> JTokenType.Null Then Return Value
        Next
        Return Nothing
    End Function

    Friend Function RecoveredJsonText(Container As JObject, ParamArray Names As String()) As String
        Dim Value = RecoveredJsonValue(Container, Names)
        Return If(Value Is Nothing, "", Value.ToString())
    End Function

    Friend Function RecoveredConfigBoolean(Name As String, DefaultValue As Boolean) As Boolean
        Dim Value = GetRecoveredSetting(Name, DefaultValue.ToString())
        Dim Result As Boolean
        If Boolean.TryParse(Value, Result) Then Return Result
        If Value = "1" Then Return True
        If Value = "0" Then Return False
        Return DefaultValue
    End Function

    Friend Sub ServerSub(Loader As LoaderTask(Of Integer, Integer))
        Dim Address = GetRecoveredSetting("PCL_SERVER_CONFIG_URL", "").Trim()
        If Address = "" Then
            ServerConfig = Nothing
            Loader.Output = 0
            Logger.Info("未配置自建服务地址，跳过联网配置更新。")
            Return
        End If
        Dim Endpoint As Uri = Nothing
        If Not Uri.TryCreate(Address, UriKind.Absolute, Endpoint) OrElse
           (Endpoint.Scheme <> Uri.UriSchemeHttps AndAlso Endpoint.Scheme <> Uri.UriSchemeHttp) OrElse
           Not String.IsNullOrEmpty(Endpoint.UserInfo) Then Throw New InvalidDataException("PCL_SERVER_CONFIG_URL 必须是 HTTP(S) JSON 地址。")
        Directory.CreateDirectory(RecoveredCacheRoot)
        Dim CachePath = Path.Combine(RecoveredCacheRoot, "ServerConfig.json")
        Dim SourcePath = Path.Combine(RecoveredCacheRoot, "ServerConfig.source")
        Dim Cached As JObject = Nothing
        If File.Exists(CachePath) AndAlso File.Exists(SourcePath) AndAlso File.ReadAllText(SourcePath) = Endpoint.AbsoluteUri Then
            Try
                Cached = JObject.Parse(File.ReadAllText(CachePath))
                ServerConfig = Cached
            Catch ex As Exception
                Logger.Warn(ex, "读取 fork 联网配置缓存失败")
            End Try
        Else
            ServerConfig = Nothing
            Dim SeenPath = Path.Combine(RecoveredCacheRoot, "Notice.seen")
            If File.Exists(SeenPath) Then File.Delete(SeenPath)
        End If
        Loader.Progress = 0.2
        Dim Fresh As JObject
        Try
            Fresh = JObject.Parse(NetRequestByClientRetry(Endpoint.AbsoluteUri, RequireJson:=True))
            File.WriteAllText(CachePath & ".pending", Fresh.ToString(Formatting.None), New UTF8Encoding(False))
            File.Copy(CachePath & ".pending", CachePath, True)
            File.Delete(CachePath & ".pending")
            File.WriteAllText(SourcePath, Endpoint.AbsoluteUri, New UTF8Encoding(False))
            ServerConfig = Fresh
        Catch ex As Exception
            If Cached Is Nothing Then Throw New IOException("获取 fork 联网配置失败，且没有与当前服务地址匹配的缓存。", ex)
            Logger.Warn(ex, "获取 fork 联网配置失败，使用当前服务地址的缓存")
            Fresh = Cached
        End Try
        Loader.Progress = 0.6
        Dim NoticeToken = RecoveredJsonValue(Fresh, "Notices", "Notice")
        Dim Notices = TryCast(NoticeToken, JArray)
        If Notices IsNot Nothing Then RecoveredProcessNotices(Notices, Endpoint, Loader)
        Loader.Progress = 1
        Dim ConfigVersion As Integer
        Integer.TryParse(RecoveredJsonText(Fresh, "Version"), ConfigVersion)
        Loader.Output = ConfigVersion
    End Sub

    Private Sub RecoveredProcessNotices(Notices As JArray, Endpoint As Uri, Loader As LoaderTask(Of Integer, Integer))
        Dim SeenPath = Path.Combine(RecoveredCacheRoot, "Notice.seen")
        Dim Seen As Integer
        If File.Exists(SeenPath) Then Integer.TryParse(File.ReadAllText(SeenPath), Seen)
        Dim Maximum = Seen
        Dim Index As Integer
        For Each Notice In Notices.OfType(Of JObject)()
            Loader.CreateCancellationToken().ThrowIfCancellationRequested()
            Index += 1
            Loader.Progress = 0.6 + 0.35 * Index / Math.Max(1, Notices.Count)
            Dim IdValue = RecoveredJsonValue(Notice, "id")
            Dim Id As Integer
            If IdValue Is Nothing OrElse Not Integer.TryParse(IdValue.ToString(), Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, Id) Then Continue For
            If Id <= Seen Then Continue For
            Maximum = Math.Max(Maximum, Id)
            Try
                Dim Requirements = TryCast(RecoveredJsonValue(Notice, "requirements"), JObject)
                If Requirements IsNot Nothing AndAlso Not Requirements.Properties().All(Function(Requirement) ServerRequirement(Requirement.Name, Requirement.Value)) Then Continue For
                Dim IsUpdateValue = RecoveredJsonValue(Notice, "isUpdate")
                Dim IsUpdateNotice = IsUpdateValue IsNot Nothing AndAlso IsUpdateValue.ToObject(Of Boolean)()
                Dim LevelValue = RecoveredJsonValue(Notice, "importantLevel")
                Dim Level = If(LevelValue Is Nothing, 2, LevelValue.ToObject(Of Integer)())
                If Settings.Get(Of Integer)(If(IsUpdateNotice, "SystemSystemUpdate", "SystemSystemActivity")) > Level OrElse (IsUpdateNotice AndAlso IsUpdating) Then Continue For
                Dim Title = RecoveredJsonText(Notice, "title")
                If Title = "" Then Title = "fork 服务公告"
                Dim Description = RecoveredJsonText(Notice, "description", "text").Replace("\n", vbCrLf)
                Dim Buttons = TryCast(RecoveredJsonValue(Notice, "buttons"), JArray)
                Dim Actions As JObject = Nothing
                Dim UserSelected As Boolean
                If Buttons IsNot Nothing AndAlso Buttons.Count > 0 Then
                    If Buttons.Count > 3 Then Throw New InvalidDataException("公告最多支持三个按钮。")
                    Dim Labels = Buttons.Select(Function(Button) If(RecoveredJsonText(TryCast(Button, JObject), "text") = "", "确定", RecoveredJsonText(TryCast(Button, JObject), "text"))).ToList()
                    While Labels.Count < 3
                        Labels.Add("")
                    End While
                    Dim Selected = MyMsgBox(Description, Title, Labels(0), Labels(1), Labels(2), ForceWait:=True)
                    If Selected >= 1 AndAlso Selected <= Buttons.Count Then
                        Actions = TryCast(RecoveredJsonValue(TryCast(Buttons(Selected - 1), JObject), "actions"), JObject)
                        UserSelected = True
                    End If
                Else
                    If Description <> "" Then Hint(Description)
                    Actions = TryCast(RecoveredJsonValue(Notice, "actions"), JObject)
                End If
                If Actions IsNot Nothing Then RecoveredNoticeActions(Actions, Endpoint, UserSelected)
            Catch ex As Exception
                Logger.Warn(ex, "处理 fork 服务公告失败", LogBehavior.ToastIfDebug)
            End Try
        Next
        File.WriteAllText(SeenPath, Maximum.ToString(Globalization.CultureInfo.InvariantCulture), New UTF8Encoding(False))
    End Sub

    Private Sub RecoveredNoticeActions(Actions As JObject, Endpoint As Uri, UserSelected As Boolean)
        For Each Action In Actions.Properties()
            Select Case Action.Name.ToLowerInvariant()
                Case "copy"
                    If UserSelected Then ClipboardSet(Action.Value.ToString())
                Case "website"
                    If UserSelected Then
                        Dim Address As Uri = Nothing
                        If Not Uri.TryCreate(Endpoint, Action.Value.ToString(), Address) OrElse
                           (Address.Scheme <> Uri.UriSchemeHttps AndAlso Address.Scheme <> Uri.UriSchemeHttp) Then Throw New InvalidDataException("公告网页地址无效。")
                        OpenWebsite(Address.AbsoluteUri)
                    End If
                Case "stop"
                    If UserSelected Then RunInUi(Sub() FrmMain.EndProgram(True))
                Case "setup"
                    If Not UserSelected AndAlso Not RecoveredConfigBoolean("PCL_SERVER_ALLOW_SETUP_ACTIONS", False) Then Continue For
                    Dim Values = TryCast(Action.Value, JObject)
                    If Values Is Nothing Then Throw New InvalidDataException("setup 操作必须为设置名称到值的对象。")
                    For Each Pair In Values.Properties()
                        ' Remote configuration may update declared settings only; no filesystem or commands are exposed.
                        If Not Settings.Entries.ContainsKey(Pair.Name) Then Throw New InvalidDataException("公告请求修改未声明的设置。")
                        If Settings.Entries(Pair.Name).Source = Settings.Sources.Instance Then Throw New InvalidDataException("公告无法修改未指定实例的设置。")
                        Dim SettingName = Pair.Name
                        Dim SettingValue = Pair.Value.ToObject(Of Object)()
                        RunInUiWait(Sub() Settings.Set(SettingName, SettingValue))
                    Next
                Case "update"
                    If UserSelected Then UpdateStart(False)
                Case "slientupdate", "silentupdate"
                    If RecoveredConfigBoolean("PCL_UPDATE_ALLOW_AUTOMATIC", False) Then UpdateStart(True)
                Case Else
                    Logger.Warn("忽略未知的 fork 公告操作：" & Action.Name)
            End Select
        Next
    End Sub

    Friend Function ServerRequirement(Name As String, Value As JToken) As Boolean
        Try
            Select Case Name.Trim().ToLowerInvariant()
                Case "d10000 <="
                    Return RandomInteger(1, 10000) <= Value.ToObject(Of Double)()
                Case "opencount <="
                    Return Settings.Get(Of Integer)("SystemCount") <= Value.ToObject(Of Double)()
                Case "opencount >="
                    Return Settings.Get(Of Integer)("SystemCount") >= Value.ToObject(Of Double)()
                Case "versioncode <="
                    Return VersionCode <= Value.ToObject(Of Double)()
                Case "versioncode >="
                    Return VersionCode >= Value.ToObject(Of Double)()
                Case "versionbranch <="
                    Return CInt(BuildType) <= Value.ToObject(Of Double)()
                Case "versionbranch >="
                    Return CInt(BuildType) >= Value.ToObject(Of Double)()
                Case "uniqueaddress ="
                    Return String.Equals(Identify, Value.ToString(), StringComparison.Ordinal)
                Case "unlockedtheme ="
                    Return ThemeCheckOne(Value.ToObject(Of Integer)())
                Case "unlockedtheme !="
                    Return Not ThemeCheckOne(Value.ToObject(Of Integer)())
                Case "setupinteger >=", "setupinteger <=", "setupboolean ="
                    Dim Conditions = TryCast(Value, JObject)
                    If Conditions Is Nothing Then Return False
                    For Each Condition In Conditions.Properties()
                        If Not Settings.Entries.ContainsKey(Condition.Name) Then Return False
                        Select Case Name.Trim().ToLowerInvariant()
                            Case "setupinteger >="
                                If Settings.Get(Of Integer)(Condition.Name) < Condition.Value.ToObject(Of Double)() Then Return False
                            Case "setupinteger <="
                                If Settings.Get(Of Integer)(Condition.Name) > Condition.Value.ToObject(Of Double)() Then Return False
                            Case "setupboolean ="
                                If Settings.Get(Of Boolean)(Condition.Name) <> Condition.Value.ToObject(Of Boolean)() Then Return False
                        End Select
                    Next
                    Return True
                Case "debug ="
                    Return ModeDebug = Value.ToObject(Of Boolean)()
                Case Else
                    Logger.Warn("未知的 fork 公告条件：" & Name)
                    Return False
            End Select
        Catch ex As Exception
            Logger.Warn(ex, "判断 fork 公告条件失败：" & Name, LogBehavior.ToastIfDebug)
            Return False
        End Try
    End Function

End Module
