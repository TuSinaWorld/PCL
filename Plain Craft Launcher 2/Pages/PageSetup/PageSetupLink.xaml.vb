Public Class PageSetupLink
    Private Refreshing As Boolean
    Private Sub OnLoaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        Reload()
    End Sub
    Public Sub Reload()
        Refreshing = True
        Dim Profile = ModVlanLink.LoadProfile()
        LabServer.Text = If(Profile Is Nothing, "尚未设置服务器；使用朋友的邀请码可直接加入。", If(Profile Is Nothing, "", Profile.Host & ":" & Profile.Port))
        CheckCompatibility.IsChecked = Profile IsNot Nothing AndAlso Profile.Compatibility
        CheckNoPassword.IsChecked = Profile IsNot Nothing AndAlso Profile.NoRoomPassword
        Refreshing = False
    End Sub
    Friend Shared Function ConfigureServer() As ModVlanLink.ServerProfile
        Dim Previous = ModVlanLink.LoadProfile()
        Dim Host = MyMsgBoxInput("联机服务器", "填写你自己的 VLan v9 服务器地址，不需要协议前缀。", DefaultInput:=If(Previous Is Nothing, "", Previous.Host), MaxLength:=253)
        If Host Is Nothing Then Return Nothing
        Dim Port = MyMsgBoxInput("服务器端口", "填写服务器 TCP/UDP 共同使用的端口。", DefaultInput:=If(Previous Is Nothing, "11510", Previous.Port.ToString()), ValidateRules:=New ObjectModel.Collection(Of Validate) From {New ValidateInteger(1, 65535)})
        If Port Is Nothing Then Return Nothing
        Dim Password = MyMsgBoxInput("服务器鉴权密码", "此密码用于服务器鉴权，与房间密码不同；将在本机加密保存。", DefaultInput:=If(Previous Is Nothing, "", Previous.Password), MaxLength:=256, Sensitive:=True, MaskInput:=True)
        If Password Is Nothing Then Return Nothing
        Try
            Dim Profile As New ModVlanLink.ServerProfile With {.Host = Host, .Port = Integer.Parse(Port), .Password = Password, .Compatibility = Previous IsNot Nothing AndAlso Previous.Compatibility, .NoRoomPassword = Previous IsNot Nothing AndAlso Previous.NoRoomPassword}
            ModVlanLink.SaveProfile(Profile)
            Return Profile
        Catch Ex As Exception
            MyMsgBox(Ex.Message, "服务器设置无效")
            Return Nothing
        End Try
    End Function
    Private Sub Configure() Handles BtnConfigure.Click
        ConfigureServer()
        Reload()
    End Sub
    Private Sub TestConnection() Handles BtnTest.Click
        Dim Profile = ModVlanLink.LoadProfile()
        If Profile Is Nothing Then
            Profile = ConfigureServer()
            If Profile Is Nothing Then Return
        End If
        BtnTest.IsEnabled = False
        RunInNewThread(Sub()
                           Try
                               Using Session As New VlanSession()
                                   Session.Start(ModVlanLink.EnsureComponent(), Threading.CancellationToken.None, False)
                                   Session.Send("configure", New Newtonsoft.Json.Linq.JObject From {{"host", Profile.Host}, {"port", Profile.Port.ToString()}, {"password", Profile.Password}, {"name", "PCL" & ModVlanLink.RandomToken()}})
                                   Session.WaitFor(Function(S) CBool(S("authenticated")), 20000, Threading.CancellationToken.None)
                               End Using
                               RunInUi(Sub() Hint("服务器连接与鉴权成功。", HintType.Green))
                           Catch Ex As Exception
                               RunInUi(Sub() MyMsgBox(Ex.Message, "连接测试未完成"))
                           Finally
                               RunInUi(Sub() BtnTest.IsEnabled = True)
                           End Try
                       End Sub, "VLan server test")
    End Sub
    Private Sub OptionsChanged() Handles CheckCompatibility.Click, CheckNoPassword.Click
        If Refreshing Then Return
        Dim Profile = ModVlanLink.LoadProfile()
        If Profile Is Nothing Then
            Profile = ConfigureServer()
            If Profile Is Nothing Then Reload() : Return
        End If
        Profile.Compatibility = CheckCompatibility.IsChecked.GetValueOrDefault()
        Profile.NoRoomPassword = CheckNoPassword.IsChecked.GetValueOrDefault()
        ModVlanLink.SaveProfile(Profile)
    End Sub
    Public Sub Reset()
        Dim Target = System.IO.Path.Combine(ModVlanLink.Root, "server.dpapi")
        If System.IO.File.Exists(Target) Then System.IO.File.Delete(Target)
        Reload()
    End Sub
End Class
