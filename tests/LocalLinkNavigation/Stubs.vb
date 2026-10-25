Imports System
Imports System.Collections.Generic

Public Module NavigationContext
    Public ClipboardText As String
    Public ClipboardReads As Integer
    Public ClipboardClears As Integer
    Public PromptCount As Integer
    Public PromptAnswer As Integer = 2
    Public LastFingerprint As String = ""
    Public JoinedCode As String
    Public JoinCount As Integer
    Public RefreshCount As Integer
    Public EventConfirmCount As Integer
    Public EventConfirmAnswer As Boolean = True
    Public JoinFailure As Exception
    Public Logs As New List(Of String)
    Public McInstanceListLoader As Object
    Public McFolderSelected As String = "fixture-minecraft"
    Public FrmInstanceMod As New FakeInstanceMod

    Public Sub Reset()
        ClipboardText = Nothing
        ClipboardReads = 0
        ClipboardClears = 0
        PromptCount = 0
        PromptAnswer = 2
        LastFingerprint = ""
        JoinedCode = Nothing
        JoinCount = 0
        RefreshCount = 0
        EventConfirmCount = 0
        EventConfirmAnswer = True
        JoinFailure = Nothing
        Logs.Clear()
        PageLinkMain.LinkState = PageLinkMain.LinkStates.Waiting
    End Sub

    Public Function ClipboardGetText() As String
        ClipboardReads += 1
        Return ClipboardText
    End Function
    Public Sub ClipboardSet(Text As String, ShowSuccessHint As Boolean)
        If Text IsNot Nothing Then Throw New Exception("Navigation should only clear the fixture clipboard.")
        ClipboardClears += 1
        ClipboardText = Text
    End Sub
    Public Function MyMsgBox(Prompt As String, Title As String, Confirm As String, Cancel As String) As Integer
        PromptCount += 1
        Logs.Add(Prompt)
        Return PromptAnswer
    End Function
    Public Sub RunInThread(Action As Action)
        Action()
    End Sub
    Public Sub RunInUi(Action As Action)
        Action()
    End Sub
    Public Sub LoaderFolderRun(Loader As Object, Folder As String, Type As LoaderFolderRunType, MaxDepth As Integer, ExtraPath As String)
        RefreshCount += 1
    End Sub
End Module

Public Enum LoaderFolderRunType
    RunOnUpdated
End Enum
Public Class FakeInstanceMod
    Public Sub ReloadModList()
        NavigationContext.RefreshCount += 1
    End Sub
End Class
Public Class PageInstanceLeft
    Public Shared Sub ReloadCurrentJava()
        NavigationContext.RefreshCount += 1
    End Sub
End Class
Public Class Logger
    Public Shared Sub Info(Message As String)
        NavigationContext.Logs.Add(Message)
    End Sub
    Public Shared Sub [Error](ErrorValue As Exception, Message As String, Optional Behavior As LogBehavior = LogBehavior.Alert)
        NavigationContext.Logs.Add(Message)
        NavigationContext.Logs.Add(ErrorValue.ToString())
    End Sub
End Class
Public Enum LogBehavior
    Alert
End Enum
Public Class Settings
    Public Shared Function [Get](Of T)(Key As String) As T
        If Key <> "LinkLastAutoJoinInviteCode" Then Throw New Exception("Unexpected fixture setting.")
        Return CType(CObj(NavigationContext.LastFingerprint), T)
    End Function
    Public Shared Sub [Set](Key As String, Value As String)
        If Key <> "LinkLastAutoJoinInviteCode" Then Throw New Exception("Unexpected fixture setting.")
        NavigationContext.LastFingerprint = Value
        ' Model production's unencrypted setting trace, to detect accidental code logging.
        NavigationContext.Logs.Add(Value)
    End Sub
End Class
Public Class PageLinkMain
    Public Enum LinkStates
        Waiting
        Loading
        Finished
    End Enum
    Public Shared LinkState As LinkStates
    Public Shared Function ValidateCodeFormat(Code As String) As String
        Try
            ModLocalLink.DecodeInvite(Code)
            Return Nothing
        Catch ex As Exception
            Return ex.Message
        End Try
    End Function
    Public Shared Sub Join(Code As String)
        NavigationContext.JoinCount += 1
        NavigationContext.JoinedCode = Code
        If NavigationContext.JoinFailure IsNot Nothing Then Throw NavigationContext.JoinFailure
    End Sub
    Public Shared Sub Join()
        NavigationContext.JoinCount += 1
    End Sub
End Class
Partial Public Class CustomEvent
    Private Shared Function EventSafetyConfirm(Message As String) As Boolean
        NavigationContext.EventConfirmCount += 1
        NavigationContext.Logs.Add(Message)
        Return NavigationContext.EventConfirmAnswer
    End Function
End Class
Public Module FixtureStringExtensions
    <Runtime.CompilerServices.Extension>
    Public Function BeforeFirst(Value As String, Separator As String) As String
        Dim Index = Value.IndexOf(Separator, StringComparison.Ordinal)
        Return If(Index < 0, Value, Value.Substring(0, Index))
    End Function
    <Runtime.CompilerServices.Extension>
    Public Function AfterFirst(Value As String, Separator As String) As String
        Dim Index = Value.IndexOf(Separator, StringComparison.Ordinal)
        Return If(Index < 0, "", Value.Substring(Index + Separator.Length))
    End Function
End Module
Partial Public Class FormMain
    Public Event Activated()
    Public Enum PageType
        Launch
        Link
        InstanceSetup
        InstanceSelect
    End Enum
    Public Enum PageSubType
        InstanceMod
        InstanceSetup
        InstanceExport
    End Enum
    Public PageCurrent As PageType = PageType.Launch
    Public PageCurrentSub As PageSubType
    Public Sub ActivateForTest()
        RaiseEvent Activated()
    End Sub
End Class
