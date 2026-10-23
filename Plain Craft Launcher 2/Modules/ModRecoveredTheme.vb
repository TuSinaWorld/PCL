' Recovered from the supplied 2.12.8.2 assembly. The sample itself forces theme 0.
' Do not replace the sample's authorization stubs with unconditional success.
Friend Module ModRecoveredTheme

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    Public ColorHue As Integer = 210
    Public ColorSat As Integer = 85
    Public ColorLightAdjust As Integer = 0
    Public ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            ' This assignment is present in the supplied binary, including when another theme is requested.
            NewTheme = 0
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If Not ThemeCheckOne(If(NewTheme >= 0, NewTheme, ThemeNow)) Then Return
            If NewTheme >= 0 Then ThemeNow = NewTheme
            ColorHue = 210
            ColorSat = 85
            ColorLightAdjust = 0
            Select Case ThemeDontClick
                Case 1
                    ColorLightAdjust = 999
                Case 2
                    ColorHue = RandomInteger(0, 359)
                    ColorSat = RandomInteger(40, 70)
                    ColorLightAdjust = RandomInteger(-20, 20)
            End Select

            Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
            Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
            Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
            Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
            Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
            Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
            Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
            Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)
            ColorSemiTransparent = New MyColor(1, Color8)

            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
            If ThemeNow <> 12 AndAlso ThemeNow <> 14 AndAlso ThemeDontClick <> 2 Then Logger.Info($"刷新主题：{ThemeNow}")
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub

    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then Return
            Dim Brush As New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            If ThemeNow = 5 Then
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 25)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 15)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 25)})
                FrmMain.PanTitle.Background = Brush
                FrmMain.PanTitle.Background.Freeze()
            ElseIf ThemeNow <> 12 AndAlso ThemeDontClick <> 2 Then
                Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust)})
                FrmMain.PanTitle.Background = Brush
                FrmMain.PanTitle.Background.Freeze()
            Else
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue - 21, ColorSat, 53 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.33, .Color = New MyColor().FromHSL2(ColorHue - 7, ColorSat, 47 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.67, .Color = New MyColor().FromHSL2(ColorHue + 7, ColorSat, 47 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + 21, ColorSat, 53 + ColorLightAdjust)})
                FrmMain.PanTitle.Background = Brush
            End If

            FrmMain.ImgTitle.Source = If(ThemeNow >= 5 AndAlso ThemeNow <> 14, $"{PathImage}Themes/{ThemeNow}.png", Nothing)
            FrmMain.ImgTitle.Opacity = If(ThemeNow = 13, 0.25, 0.5)
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub

    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        RunInUi(
        Sub()
            Try
                Dim SelectedTheme = Settings.Get(Of Integer)("UiLauncherTheme")
                If Not ThemeCheckOne(SelectedTheme) Then
                    Logger.Warn($"检测到尚未解锁的主题：{SelectedTheme}，已重置为默认主题", LogBehavior.ToastIfDebug)
                    Settings.Set("UiLauncherTheme", 0)
                End If
                If EffectSetup AndAlso FrmSetupUI IsNot Nothing Then
                    ' The binary computes this list but contains no further settings-UI update.
                    Dim AvailableThemes As New List(Of Integer)
                    For Id As Integer = 0 To 14
                        If ThemeCheckOne(Id) Then AvailableThemes.Add(Id)
                    Next
                    AvailableThemes.Add(42)
                End If
            Catch ex As Exception
                If TypeOf If(ex.InnerException, ex) Is FormatException Then
                    Logger.Error(ex, "解锁的主题列表存档已损坏，主题解锁已被重置", LogBehavior.Alert)
                    Settings.Set("UiLauncherThemeHide2", "")
                Else
                    Logger.Error(ex, "检查主题失败", LogBehavior.AlertThenFeedback)
                End If
            End Try
        End Sub)
    End Sub

    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        If {0, 1, 2, 3, 4, 42}.Contains(Id) Then Return True
        If PotatoFeatures.Contains("Theme" & Id.ToString(Globalization.CultureInfo.InvariantCulture)) Then Return True
        Select Case Id
            Case 8
                Return CurrentRank >= DonationRank.Rank23
            Case 14
                Return Enumerable.Range(5, 9).Count(Function(ThemeId) ThemeCheckOne(ThemeId)) >= 5
            Case Else
                If If(Settings.Get(Of String)("UiLauncherThemeHide"), "").Contains("7") Then ThemeUnlock(7, ShowDoubleHint:=False)
                Return If(Settings.Get(Of String)("UiLauncherThemeHide2"), "").Split("|"c).Contains(Id.ToString(Globalization.CultureInfo.InvariantCulture))
        End Select
    End Function

    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        ' No unlocking implementation survived in the supplied assembly.
        Return False
    End Function

End Module
