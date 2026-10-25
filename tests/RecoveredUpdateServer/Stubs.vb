Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Threading
Imports Newtonsoft.Json.Linq

Public Enum LoadState
    Waiting
    Loading
    Finished
    Failed
    Canceled
End Enum
Public Enum LogBehavior
    None
    ToastIfDebug
    Toast
    Alert
End Enum
Public Enum HintType
    Green
    Red
End Enum
Public Class Logger
    Public Shared Sub Info(Message As String)
    End Sub
    Public Shared Sub Warn(Message As String)
    End Sub
    Public Shared Sub Warn(Failure As Exception, Message As String, Optional Behavior As LogBehavior = LogBehavior.None)
    End Sub
    Public Shared Sub [Error](Failure As Exception, Message As String, Optional Behavior As LogBehavior = LogBehavior.None)
    End Sub
End Class
Public Class Settings
    Public Enum Sources
        Normal
        Registry
        Instance
    End Enum
    Public Class Setting
        Public Value As Object
        Public Source As Sources
        Public Sub New(Value As Object, Optional Source As Sources = Sources.Normal)
            Me.Value = Value
            Me.Source = Source
        End Sub
    End Class
    Public Shared Entries As New Dictionary(Of String, Setting) From {{"SystemCount", New Setting(10)}, {"SystemSystemUpdate", New Setting(0)}, {"SystemSystemActivity", New Setting(0)}, {"TestFlag", New Setting(True)}}
    Public Shared Function [Get](Of T)(Name As String) As T
        Return CType(Entries(Name).Value, T)
    End Function
    Public Shared Sub [Set](Name As String, Value As Object)
        If Name = "UiFixture" AndAlso Not Context.UiDispatchActive Then Throw New InvalidOperationException("UI setting callback called from a background thread")
        Entries(Name).Value = Value
    End Sub
End Class
Public Class LoaderBase
    Public State As LoadState
    Public [Error] As Exception
    Public Progress As Double
    Public ProgressWeight As Double
    Public ReloadTimeout As Integer
    Public OnStateChanged As Action(Of LoaderBase)
    Public Overridable Sub Start()
    End Sub
    Public Overridable Sub WaitForExit(Optional Input As Object = Nothing, Optional LoaderToSyncProgress As LoaderBase = Nothing, Optional IsForceRestart As Boolean = False)
        Start()
    End Sub
    Public Function CreateCancellationToken() As CancellationToken
        Return CancellationToken.None
    End Function
End Class
Public Class LoaderTask(Of TIn, TOut)
    Inherits LoaderBase
    Public Output As TOut
    Private Work As Action(Of LoaderTask(Of TIn, TOut))
    Public Sub New(Name As String, LoadDelegate As Action(Of LoaderTask(Of TIn, TOut)), Optional InputDelegate As Func(Of TIn) = Nothing, Optional Priority As ThreadPriority = ThreadPriority.Normal)
        Work = LoadDelegate
    End Sub
    Public Overrides Sub Start()
        State = LoadState.Loading
        Work(Me)
        State = LoadState.Finished
    End Sub
End Class
Public Class LoaderCombo(Of T)
    Inherits LoaderBase
    Public Sub New(Name As String, Items As IEnumerable(Of LoaderBase))
    End Sub
End Class
Public Class NetFile
    Public Sub New(Urls As IEnumerable(Of String), LocalPath As String, Optional Checker As FileChecker = Nothing)
    End Sub
End Class
Public Class FileChecker
    Public MinSize As Long
    Public Hash As String
End Class
Public Class LoaderDownload
    Inherits LoaderBase
    Public Sub New(Name As String, Files As IEnumerable(Of NetFile))
    End Sub
End Class
Public Class TestButton
    Public Sub ShowRefresh()
    End Sub
End Class
Public Class TestWindow
    Public BtnExtraDownload As New TestButton
    Public Sub EndProgram(Warning As Boolean)
        Throw New Exception("Tests must never exit or replace a launcher.")
    End Sub
End Class
Public Class MsDelta
    Public Shared Sub Apply(OldFile As String, Patch As String, Destination As String)
        Throw New Exception("No native patch operation is allowed in this harness.")
    End Sub
End Class
Public Module Context
    Public Enum BuildTypes
        Release = 50
        Snapshot = 0
    End Enum
    Public PathExe As String
    Public PathTemp As String
    Public Const VersionCode As Integer = 413
    Public Const BuildType As BuildTypes = BuildTypes.Release
    Public Const Identify As String = "TEST-ID"
    Public Const ModeDebug As Boolean = False
    Public FrmMain As New TestWindow
    Public McLaunchLoader As New LoaderBase
    Public Config As New Dictionary(Of String, String)
    Public Response As String
    Public Requests As Integer
    Public UiDispatchActive As Boolean
    Public UiDispatchCalls As Integer
    Public Function GetRecoveredSetting(Name As String, DefaultValue As String) As String
        Return If(Config.ContainsKey(Name), Config(Name), DefaultValue)
    End Function
    Public Function NetRequestByClientRetry(Url As String, Optional RequireJson As Boolean = False) As String
        Requests += 1
        If Response Is Nothing Then Throw New IOException("Simulated network outage")
        Return Response
    End Function
    Public Sub NetDownloadByLoader(Urls As IEnumerable(Of String), Destination As String, Progress As LoaderBase, Check As FileChecker)
        Throw New Exception("No real download is allowed in this harness.")
    End Sub
    Public Function RequestTaskTempFolder() As String
        Return PathTemp
    End Function
    Public Function ArgumentReplace(Value As String, Optional Escape As Func(Of String, String) = Nothing) As String
        Return Value
    End Function
    Public Sub RunInThread(Action As Action)
        Action()
    End Sub
    Public Sub RunInUi(Action As Action)
        Action()
    End Sub
    Public Sub RunInUiWait(Action As Action)
        UiDispatchCalls += 1
        UiDispatchActive = True
        Try
            Action()
        Finally
            UiDispatchActive = False
        End Try
    End Sub
    Public Sub Hint(Message As String, Optional Type As HintType = HintType.Green)
    End Sub
    Public Function HasDownloadingTask() As Boolean
        Return False
    End Function
    Public Function MyMsgBox(Content As String, Title As String, Optional Button1 As String = "OK", Optional Button2 As String = "", Optional Button3 As String = "", Optional ForceWait As Boolean = False) As Integer
        Return 2
    End Function
    Public Function ThemeCheckOne(Id As Integer) As Boolean
        Return Id = 0
    End Function
    Public Function RandomInteger(Minimum As Integer, Maximum As Integer) As Integer
        Return Minimum
    End Function
    Public Sub ClipboardSet(Value As String)
    End Sub
    Public Sub OpenWebsite(Value As String)
        Throw New Exception("No browser actions are allowed in this harness.")
    End Sub
    Public Sub LoaderTaskbarAdd(Of T)(Loader As LoaderCombo(Of T))
    End Sub
End Module
