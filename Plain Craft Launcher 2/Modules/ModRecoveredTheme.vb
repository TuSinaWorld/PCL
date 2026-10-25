' Fork-local themes reuse the recovered drawing code and bundled title patterns.
' Only theme 0's original palette survived; the other palettes are local reconstructions.
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

    Friend Structure LocalThemePalette
        Public Hue As Integer
        Public Saturation As Integer
        Public LightAdjust As Integer
        Public TitleDelta As Integer

        Public Sub New(Hue As Integer, Saturation As Integer, LightAdjust As Integer, Optional TitleDelta As Integer = 0)
            Me.Hue = Hue
            Me.Saturation = Saturation
            Me.LightAdjust = LightAdjust
            Me.TitleDelta = TitleDelta
        End Sub
    End Structure

    Friend Function GetLocalThemePalette(Id As Integer, Optional AnimationTime As Long = -1) As LocalThemePalette
        Select Case Id
            Case 1 : Return New LocalThemePalette(180, 75, -2) ' 甜柠青
            Case 2 : Return New LocalThemePalette(125, 70, -3) ' 小草绿
            Case 3 : Return New LocalThemePalette(52, 78, -4) ' 菠萝黄
            Case 4 : Return New LocalThemePalette(30, 70, -15) ' 橡木棕
            Case 5 : Return New LocalThemePalette(210, 0, -18) ' 玄素黑
            Case 6 : Return New LocalThemePalette(330, 75, 2) ' 铁杆粉
            Case 7 : Return New LocalThemePalette(275, 70, 0) ' 神秘紫
            Case 8 : Return New LocalThemePalette(43, 90, -4) ' 秋仪金
            Case 9 : Return New LocalThemePalette(25, 90, 0) ' 活跃橙
            Case 10 : Return New LocalThemePalette(5, 85, 0) ' 跳票红
            Case 11 : Return New LocalThemePalette(225, 85, -8) ' 极客蓝
            Case 12
                ' The existing 250 ms timer drives one local hue cycle every 18 seconds.
                If AnimationTime < 0 Then AnimationTime = GetTimeMs()
                Return New LocalThemePalette(CInt((AnimationTime Mod 18000) \ 50), 75, 0, 21)
            Case 13 : Return New LocalThemePalette(300, 70, 2, 90) ' 静态欧皇彩
            Case 14
                Return New LocalThemePalette(
                    Math.Max(0, Math.Min(360, Settings.Get(Of Integer)("UiLauncherHue"))),
                    Math.Max(0, Math.Min(100, Settings.Get(Of Integer)("UiLauncherSat"))),
                    Math.Max(0, Math.Min(40, Settings.Get(Of Integer)("UiLauncherLight"))) - 20,
                    Math.Max(0, Math.Min(180, Settings.Get(Of Integer)("UiLauncherDelta"))) - 90)
            Case Else : Return New LocalThemePalette(210, 85, 0) ' 龙猫蓝 / invalid legacy ID
        End Select
    End Function

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        ' Settings callbacks can originate on a worker thread. All colors/resources belong to the UI.
        RunInUi(Sub() ApplyLocalTheme(NewTheme))
    End Sub

    Private Sub ApplyLocalTheme(NewTheme As Integer)
        Try
            If NewTheme = -1 Then NewTheme = If(ThemeNow >= 0, ThemeNow, Settings.Get(Of Integer)("UiLauncherTheme"))
            If Not ThemeCheckOne(NewTheme) Then NewTheme = 0
            Dim ThemeChanged = ThemeNow <> NewTheme
            ThemeNow = NewTheme
            Dim Palette = GetLocalThemePalette(ThemeNow)
            ColorHue = Palette.Hue
            ColorSat = Palette.Saturation
            ColorLightAdjust = Palette.LightAdjust
            ColorHueTopbarDelta = Palette.TitleDelta

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
            Application.Current.Resources("ColorBrushSemiTransparent") = New SolidColorBrush(ColorSemiTransparent)
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
            If ThemeChanged Then Logger.Info("刷新本地主题：" & ThemeNow)
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
            ElseIf ThemeNow <> 12 Then
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

            FrmMain.ImgTitle.Source = If(ThemeNow >= 5 AndAlso ThemeNow <= 13, PathImage & "Themes/" & ThemeNow & ".png", Nothing)
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
                    Logger.Warn("不支持的旧主题编号，已重置为默认本地主题。", LogBehavior.ToastIfDebug)
                    Settings.Set("UiLauncherTheme", 0)
                End If
            Catch ex As Exception
                Logger.Error(ex, "检查本地主题设置失败", LogBehavior.Toast)
            End Try
        End Sub)
    End Sub

    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Return Id >= 0 AndAlso Id <= 14
    End Function

    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        ' Compatibility only: local themes have no unlock workflow or authorization grant.
        Return False
    End Function

End Module
