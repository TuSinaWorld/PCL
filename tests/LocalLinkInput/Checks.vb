Imports System
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows
Imports System.Windows.Controls
Imports System.Xml.Linq

Public Class LocalLinkInputChecks
    Private Shared ReadOnly Results As New List(Of String)
    Private Shared Sub Check(Value As Boolean, Description As String)
        If Not Value Then Throw New Exception("FAIL: " & Description)
        Results.Add(Description)
    End Sub

    Public Shared Function Run(Repository As String) As String()
        If Thread.CurrentThread.GetApartmentState() <> ApartmentState.STA Then Throw New Exception("Real WPF input checks require STA.")
        Results.Clear()
        Dim Markup = XDocument.Load(Path.Combine(Repository, "Plain Craft Launcher 2", "Controls", "MyMsg", "MyMsgInput.xaml"))
        Dim InputElement = Markup.Descendants().Single(Function(Element) Element.Attributes().Any(Function(Attribute) Attribute.Name.LocalName = "Name" AndAlso Attribute.Value = "TextArea"))
        InputContext.XamlInputMaxLength = Integer.Parse(InputElement.Attribute("MaxLength").Value)
        Check(InputContext.XamlInputMaxLength = 1000, "markup retains the ordinary 1000-character default")
        Dim Defaults As New MyMsgBoxConverter
        Check(Defaults.InputMaxLength = 1000 AndAlso Not Defaults.Sensitive, "production converter defaults remain 1000 and non-sensitive")
        Check(Defaults.InputResultLogText = "null", "ordinary unset converter preserves null diagnostics")

        Dim FixtureSecret = New String("a"c, 32)
        Dim Peers = Enumerable.Range(1, 8).Select(Function(Index) "tcp://node" & Index & "." & New String("x"c, 50) & "." & New String("y"c, 50) & ".invalid:11010").ToList()
        Dim Invite As New ModLocalLink.LinkInvite With {.Network = "PCLF-" & New String("0"c, 32), .Secret = FixtureSecret, .Port = 25565, .Peers = Peers}
        Dim Code = ModLocalLink.EncodeInvite(Invite)
        Check(Code.StartsWith("PCLF1:") AndAlso Code.Length > 1000 AndAlso Code.Length < ModLocalLink.MaximumInviteLength, "production encoder creates a legal fixture beyond the old input limit")
        Dim Decoded = ModLocalLink.DecodeInvite(Code)
        Check(Decoded.Secret = FixtureSecret AndAlso Decoded.Peers.SequenceEqual(Peers) AndAlso ModLocalLink.EncodeInvite(Decoded) = Code, "long input fixture round-trips through the production decoder")

        InputContext.Reset(Code)
        Dim Actual = MyMsgBoxInput("fixture invitation", MaxLength:=ModLocalLink.MaximumInviteLength, Sensitive:=True)
        Check(TypeOf InputContext.LastControl.TextArea Is TextBox, "dialog fixture edits with an actual WPF TextBox")
        Check(InputContext.LastConverter.InputMaxLength = ModLocalLink.MaximumInviteLength AndAlso InputContext.LastControl.TextArea.MaxLength = ModLocalLink.MaximumInviteLength, "explicit length reaches converter and production constructor")
        Check(InputContext.LastConverter.Sensitive, "sensitive flag reaches the production converter")
        Check(Actual = Code AndAlso InputContext.LastControl.TextArea.Text = Code, "real WPF TextInput and production confirm preserve the complete long invite")
        Check(ModLocalLink.EncodeInvite(ModLocalLink.DecodeInvite(Actual)) = Code, "confirmed input remains a valid complete PCLF1 invitation")
        Check(InputContext.Logs.SequenceEqual(New String() {"输入弹框返回：[已隐藏]"}), "sensitive input uses the redacted production return log")
        Check(InputContext.Logs.All(Function(Line) Not Line.Contains(Code) AndAlso Not Line.Contains(FixtureSecret)), "sensitive logs contain neither invite nor fixture secret")
        Check(InputContext.LastConverter.InputResultLogText = "[已隐藏]", "sensitive converter log property redacts its real result")
        Check(InputContext.LastControl.TextArea.ValidateCount = 1 AndAlso InputContext.CloseCount = 1 AndAlso InputContext.TickCount = 1 AndAlso InputContext.FrmMain.DragStops = 1, "production dialog confirms once through its validation and modal path")
        Check(InputContext.Errors.Count = 0 AndAlso InputContext.WaitingMyMsgBox.Count = 0, "constructor and dialog complete without swallowed errors or queued work")

        InputContext.Reset(Code)
        Actual = MyMsgBoxInput("ordinary input")
        Check(InputContext.LastConverter.InputMaxLength = 1000 AndAlso InputContext.LastControl.TextArea.MaxLength = 1000 AndAlso Not InputContext.LastConverter.Sensitive, "omitted optional arguments keep the original 1000 and ordinary logging")
        Check(Actual = Code.Substring(0, 1000) AndAlso InputContext.LastControl.TextArea.Text.Length = 1000, "real WPF user input enforces the ordinary 1000-character limit")
        Check(InputContext.Logs.SequenceEqual(New String() {"输入弹框返回：" & Actual}), "ordinary limited input keeps its previous returned-text log")

        InputContext.Reset("12345678")
        Actual = MyMsgBoxInput("custom limit", MaxLength:=7)
        Check(Actual = "1234567" AndAlso InputContext.LastControl.TextArea.MaxLength = 7, "production constructor overrides the XAML default for a smaller custom limit")

        For Each Boundary In New Integer() {1, 65536}
            InputContext.Reset("xy")
            Actual = MyMsgBoxInput("accepted boundary", MaxLength:=Boundary)
            Check(InputContext.LastControl.TextArea.MaxLength = Boundary AndAlso Actual = If(Boundary = 1, "x", "xy"), "supported length boundary " & Boundary & " reaches real WPF input")
        Next

        Dim Rules As New Collection(Of Validate) From {New Validate}
        InputContext.Reset()
        Actual = MyMsgBoxInput("default fixture", Text:="intro", DefaultInput:=Code, ValidateRules:=Rules, HintText:="fixture hint", Button1:="accept", Button2:="", IsWarn:=True, MaxLength:=8192, Sensitive:=True)
        Check(Actual = Code AndAlso InputContext.LastControl.TextArea.Text = Code, "constructor preserves a full long default value and production confirm returns it")
        Check(InputContext.LastControl.TextArea.HintText = "fixture hint" AndAlso Object.ReferenceEquals(InputContext.LastControl.TextArea.ValidateRules, Rules), "existing hint and validation rules reach the input control")
        Check(InputContext.LastControl.LabTitle.Text = "default fixture" AndAlso InputContext.LastControl.LabText.Text = "intro" AndAlso InputContext.LastControl.PanText.Visibility = Visibility.Visible, "constructor retains the title and visible introduction")
        Check(InputContext.LastControl.Btn1.Text = "accept" AndAlso InputContext.LastControl.Btn1.ColorType = MyButton.ColorState.Red AndAlso InputContext.LastControl.Btn2.Visibility = Visibility.Collapsed, "custom buttons and warning presentation remain compatible")
        Check(InputContext.Errors.Count = 0, "long default input constructor does not swallow initialization errors")

        For Each Ordinary In New String() {"ordinary text", "中文输入 42", ""}
            InputContext.Reset(Ordinary)
            Actual = MyMsgBoxInput("ordinary fixture")
            Check(Actual = Ordinary, "ordinary input returns its exact text, including empty or Unicode content")
            Check(InputContext.Logs.SequenceEqual(New String() {"输入弹框返回：" & Ordinary}), "ordinary input preserves its exact previous diagnostic text")
            Check(InputContext.LastConverter.InputResultLogText = Ordinary, "ordinary converter returns its original text for diagnostics")
        Next
        For Each IsSensitive In New Boolean() {False, True}
            InputContext.Reset(Code, Cancel:=True)
            Actual = MyMsgBoxInput("cancel fixture", MaxLength:=8192, Sensitive:=IsSensitive)
            Check(Actual Is Nothing AndAlso InputContext.LastConverter.Result Is Nothing, "cancel returns Nothing for sensitive flag " & IsSensitive)
            Check(InputContext.Logs.SequenceEqual(New String() {"输入弹框返回：" & If(IsSensitive, "[已隐藏]", "null")}), "cancel retains ordinary null or sensitive-redacted diagnostics")
            Check(InputContext.LastControl.TextArea.ValidateCount = 0 AndAlso InputContext.CloseCount = 1, "cancel bypasses confirm validation and closes once")
        Next
        InputContext.Reset("")
        Actual = MyMsgBoxInput("sensitive empty", Sensitive:=True)
        Check(Actual = "" AndAlso InputContext.LastConverter.InputResultLogText = "[已隐藏]", "sensitive empty input is still returned and redacted")
        Dim Unformattable As New MyMsgBoxConverter With {.Sensitive = True, .Result = New ResultWithForbiddenToString}
        Check(Unformattable.InputResultLogText = "[已隐藏]", "sensitive log formatting never calls the result ToString")

        InputContext.Reset()
        Dim InvalidControlConverter As New MyMsgBoxConverter With {.Content = "invalid fixture"}
        Dim InvalidControl As New MyMsgInput(InvalidControlConverter)
        InvalidControl.TextArea.IsValidated = False
        InvalidControl.Btn1_Click()
        Check(InvalidControl.TextArea.ValidateCount = 1 AndAlso Not InvalidControlConverter.IsExited AndAlso InvalidControlConverter.Result Is Nothing AndAlso InputContext.CloseCount = 0, "production confirm still refuses invalid input")
        InvalidControl.TextArea.IsValidated = True
        InvalidControl.Btn1_Click()
        Check(InvalidControlConverter.IsExited AndAlso CStr(InvalidControlConverter.Result) = "invalid fixture" AndAlso InputContext.CloseCount = 1, "validated input can subsequently confirm")
        InvalidControl.Btn1_Click()
        InvalidControl.Btn2_Click()
        Check(InputContext.CloseCount = 1 AndAlso CStr(InvalidControlConverter.Result) = "invalid fixture", "already-exited dialog cannot overwrite or close the accepted result again")

        InputContext.Reset()
        Dim Masked As New MyMsgBoxConverter With {.Sensitive = True, .InputMasked = True, .InputMaxLength = 256, .Content = ""}
        Dim PasswordControl As New MyMsgInput(Masked)
        Check(PasswordControl.TextArea.Visibility = Visibility.Collapsed AndAlso PasswordControl.PasswordArea.Visibility = Visibility.Visible, "password entry uses the real WPF PasswordBox")
        PasswordControl.PasswordArea.Password = "SyntheticPassword123"
        PasswordControl.Btn1_Click()
        Check(CStr(Masked.Result) = "SyntheticPassword123" AndAlso Masked.InputResultLogText = "[已隐藏]", "masked input confirms correctly without logging credentials")
        For Each Invalid In New Integer() {Integer.MinValue, -1, 0, 65537, Integer.MaxValue}
            InputContext.Reset()
            Dim Failure As ArgumentOutOfRangeException = Nothing
            Try
                MyMsgBoxInput("invalid limit", MaxLength:=Invalid)
            Catch Ex As ArgumentOutOfRangeException
                Failure = Ex
            End Try
            Check(Failure IsNot Nothing AndAlso Failure.ParamName = "MaxLength", "unsupported length " & Invalid & " raises the production argument error")
            Check(InputContext.WaitingMyMsgBox.Count = 0 AndAlso InputContext.TickCount = 0 AndAlso InputContext.FrmMain.DragStops = 0, "unsupported length is rejected before queueing or modal UI work")
            Check(InputContext.Logs.Count = 0 AndAlso InputContext.Errors.Count = 0, "unsupported length emits no input value or swallowed initialization error")
        Next
        Return Results.ToArray()
    End Function
End Class
