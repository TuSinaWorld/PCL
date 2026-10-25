' Compatibility entry points: this fork deliberately has no configuration or notice backend.
Friend Module ModRecoveredServer
    Public ServerConfig As JObject
    Public ServerLoader As New LoaderTask(Of Integer, Integer)("本地功能配置", AddressOf ServerSub)

    Friend Sub ServerSub(Loader As LoaderTask(Of Integer, Integer))
        ' Ignore legacy URLs and cached notices. This path must never read, download or execute them.
        ServerConfig = Nothing
        Loader.Output = 0
        Loader.Progress = 1
    End Sub

    Friend Function ServerRequirement(Name As String, Value As JToken) As Boolean
        Return False
    End Function
End Module