Imports System
Imports System.Collections.Generic
Imports System.Linq

Public Class LocalLinkNavigationChecks
    Private Shared Count As Integer
    Private Shared Sub Check(Condition As Boolean, Message As String)
        If Not Condition Then Throw New Exception("FAIL: " & Message)
        Count += 1
    End Sub
    Public Shared Function Run() As String
        Dim FixtureSecret = New String("a"c, 32)
        Dim Peers = Enumerable.Range(1, 8).Select(Function(i) "tcp://node" & i & "." & New String("x"c, 50) & "." & New String("y"c, 50) & ".invalid:11010").ToList()
        Dim Invite As New ModLocalLink.LinkInvite With {.Network = "PCLF-" & New String("0"c, 32), .Secret = FixtureSecret, .Port = 25565, .Peers = Peers}
        Dim Code = ModLocalLink.EncodeInvite(Invite)
        Check(Code.Length > 1000, "fixture exercises a long actual PCLF1 invite")
        Dim Window As New FormMain

        NavigationContext.Reset()
        NavigationContext.ClipboardText = Code
        Window.ActivateForTest()
        Check(NavigationContext.PromptCount = 1, "valid long PCLF1 clipboard offers confirmation")
        Check(NavigationContext.JoinCount = 0 AndAlso NavigationContext.ClipboardClears = 0, "cancel does not connect or change clipboard")
        Check(NavigationContext.LastFingerprint.StartsWith("SHA256:") AndAlso NavigationContext.LastFingerprint.Length = 71, "deduplication persists a digest")
        Check(NavigationContext.Logs.All(Function(line) Not line.Contains(Code) AndAlso Not line.Contains(FixtureSecret)), "setting traces and prompt do not contain invite or secret")
        Window.ActivateForTest()
        Check(NavigationContext.PromptCount = 1 AndAlso NavigationContext.JoinCount = 0, "repeated cancelled invitation is not prompted again")

        NavigationContext.Reset()
        NavigationContext.ClipboardText = Code
        NavigationContext.PromptAnswer = 1
        Window.ActivateForTest()
        Check(NavigationContext.PromptCount = 1 AndAlso NavigationContext.JoinCount = 1, "explicit confirmation joins exactly once")
        Check(NavigationContext.JoinedCode = Code, "full long invite reaches Join without truncation")
        Check(NavigationContext.ClipboardClears = 1, "confirmed invitation clears clipboard afterwards")
        Check(NavigationContext.Logs.All(Function(line) Not line.Contains(Code) AndAlso Not line.Contains(FixtureSecret)), "confirmed invitation is not written in logs")

        NavigationContext.Reset()
        Dim Wrapped = "在 PCL 启动器中输入邀请码【" & Code & "】，即可加入联机房间！"
        NavigationContext.ClipboardText = Wrapped
        NavigationContext.PromptAnswer = 1
        Window.ActivateForTest()
        Check(NavigationContext.JoinedCode = Wrapped AndAlso NavigationContext.JoinCount = 1, "new decoder accepts full wrapped sharing text")
        NavigationContext.ClipboardText = Code
        PageLinkMain.LinkState = PageLinkMain.LinkStates.Waiting
        Window.ActivateForTest()
        Check(NavigationContext.PromptCount = 1, "wrapped and plain codes for the same room share a deduplication fingerprint")

        For Each Invalid In New String() {Nothing, "", "P1234-ABCDE-ABCDE-0205E", "PCLF1:invalid", New String("x"c, 8193)}
            NavigationContext.Reset()
            NavigationContext.ClipboardText = Invalid
            Window.ActivateForTest()
            Check(NavigationContext.PromptCount = 0 AndAlso NavigationContext.JoinCount = 0 AndAlso NavigationContext.LastFingerprint = "", "empty, legacy or invalid clipboard is ignored")
        Next

        For Each State In New PageLinkMain.LinkStates() {PageLinkMain.LinkStates.Loading, PageLinkMain.LinkStates.Finished}
            NavigationContext.Reset()
            NavigationContext.ClipboardText = Code
            PageLinkMain.LinkState = State
            Window.ActivateForTest()
            Check(NavigationContext.ClipboardReads = 0 AndAlso NavigationContext.PromptCount = 0, "active room is not interrupted by clipboard")
        Next
        NavigationContext.Reset()
        NavigationContext.ClipboardText = Code
        Window.PageCurrent = FormMain.PageType.Link
        Window.ActivateForTest()
        Check(NavigationContext.ClipboardReads = 0 AndAlso NavigationContext.PromptCount = 0, "link page does not prompt outside-page shortcut")

        For Each SubPage In New FormMain.PageSubType() {FormMain.PageSubType.InstanceMod, FormMain.PageSubType.InstanceSetup, FormMain.PageSubType.InstanceExport}
            NavigationContext.Reset()
            Window.PageCurrent = FormMain.PageType.InstanceSetup
            Window.PageCurrentSub = SubPage
            Window.ActivateForTest()
            Check(NavigationContext.RefreshCount = 1, "existing instance refresh runs before clipboard handling")
        Next
        NavigationContext.Reset()
        Window.PageCurrent = FormMain.PageType.InstanceSelect
        Window.ActivateForTest()
        Check(NavigationContext.RefreshCount = 1, "instance-folder refresh remains intact")

        NavigationContext.Reset()
        CustomEvent.Raise(CustomEvent.EventType.加入房间, Code)
        Check(NavigationContext.EventConfirmCount = 1 AndAlso NavigationContext.JoinCount = 1, "room custom event requests confirmation before joining")
        Check(NavigationContext.JoinedCode = Code, "custom event passes full PCLF1 argument without old-format truncation")
        Check(NavigationContext.Logs.All(Function(line) Not line.Contains(Code) AndAlso Not line.Contains(FixtureSecret)), "custom event success diagnostics omit invite and secret")
        NavigationContext.Reset()
        NavigationContext.EventConfirmAnswer = False
        CustomEvent.Raise(CustomEvent.EventType.加入房间, Code)
        Check(NavigationContext.EventConfirmCount = 1 AndAlso NavigationContext.JoinCount = 0, "cancelled custom-event confirmation cannot join")
        NavigationContext.Reset()
        NavigationContext.JoinFailure = New Exception("fixture failed with " & Code & " secret=" & FixtureSecret)
        CustomEvent.Raise(CustomEvent.EventType.加入房间, Code)
        Check(NavigationContext.Logs.All(Function(line) Not line.Contains(Code) AndAlso Not line.Contains(FixtureSecret)), "custom event failure does not echo raw decoder exception payload")
        NavigationContext.Reset()
        CustomEvent.Raise(CustomEvent.EventType.加入房间, Nothing)
        Check(NavigationContext.EventConfirmCount = 0 AndAlso NavigationContext.JoinCount = 1, "empty event opens interactive join rather than parsing legacy code")
        Return Count.ToString() & " navigation checks passed; actual extracted activation method and production invite codec; clipboard/UI/logging are in-memory stubs; no network or EasyTier process ran."
    End Function
End Class
