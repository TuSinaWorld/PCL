Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports System.Xml.Linq

Public Class LocalThemeChecks
    Private Shared ReadOnly Results As New List(Of String)

    Private Shared Sub Check(Value As Boolean, Description As String)
        If Not Value Then Throw New Exception("Failed: " & Description)
        Results.Add(Description)
    End Sub

    Private Shared Function Accent() As Color
        Return CType(Application.Current.Resources("ColorObject3"), Color)
    End Function

    Private Shared Function FiniteColor(Value As MyColor) As Boolean
        Return {Value.A, Value.R, Value.G, Value.B}.All(Function(Channel) Not Double.IsNaN(Channel) AndAlso Not Double.IsInfinity(Channel) AndAlso Channel >= 0 AndAlso Channel <= 255.000001)
    End Function

    Public Shared Function Run(Repository As String) As String()
        If Thread.CurrentThread.GetApartmentState() <> ApartmentState.STA Then Throw New Exception("WPF checks require STA")
        If Application.Current Is Nothing Then
            Dim FixtureApplication As New Application With {.ShutdownMode = ShutdownMode.OnExplicitShutdown}
        End If
        Results.Clear()
        Dim SeenAccents As New HashSet(Of Color)
        For Id = 0 To 14
            Check(ModRecoveredTheme.ThemeCheckOne(Id), "local theme " & Id & " available without identity or sponsor context")
            Settings.Set("UiLauncherTheme", Id)
            Check(ModRecoveredTheme.ThemeNow = Id AndAlso FiniteColor(ModRecoveredTheme.Color1) AndAlso FiniteColor(ModRecoveredTheme.Color3) AndAlso FiniteColor(ModRecoveredTheme.Color8), "theme " & Id & " applies finite display colors")
            SeenAccents.Add(Accent())
            Dim ExpectedSource = If(Id >= 5 AndAlso Id <= 13, "fixture/Themes/" & Id & ".png", Nothing)
            Check(Context.FrmMain.ImgTitle.Source = ExpectedSource, "theme " & Id & " selects its existing title pattern only")
        Next
        Check(SeenAccents.Count >= 12, "reconstructed palettes produce distinct colors instead of all default blue")
        Check(ModRecoveredTheme.GetLocalThemePalette(0).Hue = 210 AndAlso ModRecoveredTheme.GetLocalThemePalette(0).Saturation = 85, "original default palette preserved")

        For Each Id In {-200, -1, 15, 42, Integer.MaxValue}
            Check(Not ModRecoveredTheme.ThemeCheckOne(Id), "unsupported theme " & Id & " is rejected")
            Settings.Values("UiLauncherTheme") = Id
            ModRecoveredTheme.ThemeCheckAll(False)
            Check(Settings.Get(Of Integer)("UiLauncherTheme") = 0 AndAlso ModRecoveredTheme.ThemeNow = 0, "unsupported stored theme " & Id & " migrates to default")
        Next
        Check(Not ModRecoveredTheme.ThemeUnlock(8), "compatibility unlock function has no authorization grant")

        Context.AnimationTime = 0
        ModRecoveredTheme.ThemeRefresh(12)
        Dim FirstRainbow = Accent()
        Context.AnimationTime = 250
        ModRecoveredTheme.ThemeRefresh()
        Check(ModRecoveredTheme.ColorHue = 5 AndAlso Accent() <> FirstRainbow, "same-theme refresh advances theme 12 on the existing 250 ms clock")
        Check(CType(Context.FrmMain.PanTitle.Background, LinearGradientBrush).GradientStops.Count = 4, "theme 12 keeps its four-stop title gradient")
        Check(ModRecoveredTheme.GetLocalThemePalette(12, 18000).Hue = 0 AndAlso ModRecoveredTheme.GetLocalThemePalette(12, 17999).Hue = 359, "local 18-second animation cycle wraps")

        ModRecoveredTheme.ThemeRefresh(13)
        Dim StaticRainbow = Accent()
        Dim StaticStops = CType(Context.FrmMain.PanTitle.Background, LinearGradientBrush).GradientStops.Select(Function(StopValue) StopValue.Color).ToArray()
        Context.AnimationTime = 9000
        ModRecoveredTheme.ThemeRefresh()
        Check(Accent() = StaticRainbow AndAlso StaticStops.SequenceEqual(CType(Context.FrmMain.PanTitle.Background, LinearGradientBrush).GradientStops.Select(Function(StopValue) StopValue.Color)), "theme 13 stays static across clock changes")
        Check(Context.FrmMain.ImgTitle.Opacity = 0.25 AndAlso StaticStops.Distinct().Count() = 3, "theme 13 keeps its translucent pattern and multicolor gradient")

        Settings.Set("UiLauncherTheme", 14)
        Dim Ui As New SliderFixture
        Ui.SliderLauncherHue.Value = 44
        Ui.SliderLauncherSat.Value = 64
        Ui.SliderLauncherDelta.Value = 120
        Ui.SliderLauncherLight.Value = 32
        Settings.Writes.Clear()
        Ui.SliderLauncherHue.NotifyChange()
        Check(Settings.Writes.Distinct().Count() = 4 AndAlso Settings.Get(Of Integer)("UiLauncherHue") = 44 AndAlso Settings.Get(Of Integer)("UiLauncherSat") = 64 AndAlso Settings.Get(Of Integer)("UiLauncherDelta") = 120 AndAlso Settings.Get(Of Integer)("UiLauncherLight") = 32, "production slider handler persists all controls before repaint")
        Check(ModRecoveredTheme.ColorHue = 44 AndAlso ModRecoveredTheme.ColorSat = 64 AndAlso ModRecoveredTheme.ColorLightAdjust = 12 AndAlso ModRecoveredTheme.ColorHueTopbarDelta.Switch(Function(Value) Value, Function(Value) Value(0)) = 30, "custom theme reads saved hue, saturation and centered offsets")
        Settings.Writes.Clear()
        Context.AniControlEnabled = 1
        Ui.SliderLauncherHue.Value = 99
        Ui.SliderLauncherHue.NotifyChange()
        Check(Settings.Writes.Count = 0 AndAlso ModRecoveredTheme.ColorHue = 44, "settings reload guard prevents slider writes and repaint")
        Context.AniControlEnabled = 0
        Ui.RadioLauncherTheme14.Checked = False
        Ui.SliderLauncherHue.NotifyChange()
        Check(Settings.Writes.Count = 0, "inactive custom controls do not overwrite another theme")

        Settings.Values("UiLauncherHue") = 999
        Settings.Values("UiLauncherSat") = -2
        Settings.Values("UiLauncherDelta") = 500
        Settings.Values("UiLauncherLight") = -40
        ModRecoveredTheme.ThemeRefresh()
        Check(ModRecoveredTheme.ColorHue = 360 AndAlso ModRecoveredTheme.ColorSat = 0 AndAlso ModRecoveredTheme.ColorLightAdjust = -20 AndAlso ModRecoveredTheme.ColorHueTopbarDelta.Switch(Function(Value) Value, Function(Value) Value(0)) = 90 AndAlso FiniteColor(ModRecoveredTheme.Color3), "corrupt custom settings are clamped before rendering")
        Settings.Values("UiLauncherSat") = 100
        Settings.Values("UiLauncherHue") = -999
        Settings.Values("UiLauncherLight") = 500
        Settings.Values("UiLauncherDelta") = -500
        ModRecoveredTheme.ThemeRefresh()
        Check(ModRecoveredTheme.ColorHue = 0 AndAlso ModRecoveredTheme.ColorSat = 100 AndAlso ModRecoveredTheme.ColorLightAdjust = 20 AndAlso ModRecoveredTheme.ColorHueTopbarDelta.Switch(Function(Value) Value, Function(Value) Value(0)) = -90 AndAlso FiniteColor(ModRecoveredTheme.Color8), "opposite custom slider extremes remain valid")

        Settings.Values("UiBackgroundColorful") = False
        ModRecoveredTheme.ThemeRefresh()
        Check(TypeOf Context.FrmMain.PanForm.Background Is SolidColorBrush, "same-theme refresh applies colorful-background off")
        Settings.Values("UiBackgroundColorful") = True
        ModRecoveredTheme.ThemeRefresh()
        Check(TypeOf Context.FrmMain.PanForm.Background Is LinearGradientBrush, "same-theme refresh applies colorful-background on")

        ModRecoveredTheme.ThemeRefresh(0)
        Dim Worker As New Thread(Sub() ModRecoveredTheme.ThemeRefresh(9))
        Worker.Start()
        Worker.Join()
        Check(ModRecoveredTheme.ThemeNow = 0 AndAlso Context.UiQueue.Count = 1, "worker callback queues theme/resource changes onto UI context")
        Context.DrainUiQueue()
        Check(ModRecoveredTheme.ThemeNow = 9 AndAlso Context.FrmMain.PanForm.Background.IsFrozen AndAlso Context.FrmMain.PanTitle.Background.IsFrozen, "queued repaint runs with real WPF frozen brushes")
        Context.FrmMain.IsLoaded = False
        ModRecoveredTheme.ThemeRefresh(1)
        Context.FrmMain.IsLoaded = True
        ModRecoveredTheme.ThemeRefreshMain()
        Check(ModRecoveredTheme.ThemeNow = 1 AndAlso Context.FrmMain.ImgTitle.Source Is Nothing, "theme state survives a not-yet-loaded main window")

        CheckMarkupAndResources(Repository)
        Check(Logger.Errors.Count = 0, "no rendering, dispatcher, or setting errors were swallowed")
        Return Results.ToArray()
    End Function

    Private Shared Sub CheckMarkupAndResources(Repository As String)
        Dim Document = XDocument.Load(Path.Combine(Repository, "Plain Craft Launcher 2", "Pages", "PageSetup", "PageSetupUI.xaml"))
        Dim ThemeRadios = Document.Descendants().Where(Function(Element) Element.Name.LocalName = "MyRadioBox" AndAlso Element.Attributes().Any(Function(Attribute) Attribute.Name.LocalName = "SettingService.Key" AndAlso Attribute.Value = "UiLauncherTheme")).ToArray()
        Dim Ids = ThemeRadios.Select(Function(Element) Integer.Parse(Element.Attributes().Single(Function(Attribute) Attribute.Name.LocalName = "SettingService.Value").Value)).OrderBy(Function(Id) Id).ToArray()
        Check(Ids.SequenceEqual(Enumerable.Range(0, 15)), "settings markup offers each local theme exactly once and omits legacy 42")
        Check(ThemeRadios.All(Function(Element) Not Element.Attributes().Any(Function(Attribute) (Attribute.Name.LocalName = "IsEnabled" AndAlso Attribute.Value = "False") OrElse (Attribute.Name.LocalName = "Opacity" AndAlso Attribute.Value = "0"))), "all theme choices are visible and enabled")
        Check(Not Document.Descendants().Any(Function(Element) Element.Attributes().Any(Function(Attribute) Attribute.Name.LocalName = "Name" AndAlso (Attribute.Value.StartsWith("LabLauncherTheme") OrElse Attribute.Value = "RadioLauncherTheme5Gray"))), "no locking or sponsor overlay intercepts theme controls")
        Check(Not Document.Descendants().Any(Function(Element) Element.Attributes().Any(Function(Attribute) Attribute.Name.LocalName = "Name" AndAlso Attribute.Value = "PanLauncherHide")), "no Release-only sponsor panel covers the whole theme section")
        For Id = 5 To 13
            Dim ImagePath = Path.Combine(Repository, "Plain Craft Launcher 2", "Images", "Themes", Id.ToString() & ".png")
            Dim Decoder = BitmapDecoder.Create(New Uri(ImagePath, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
            Check(Decoder.Frames(0).PixelWidth > 0 AndAlso Decoder.Frames(0).PixelHeight > 0, "bundled title pattern " & Id & " decodes")
        Next
    End Sub
End Class
