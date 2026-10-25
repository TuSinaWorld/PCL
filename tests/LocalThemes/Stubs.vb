Imports System
Imports System.Collections.Generic
Imports System.Threading
Imports System.Windows.Controls

' No identity, sponsor, credentials, files, network, or launcher context exists in this fixture.
Public Enum LogBehavior
    Toast
    ToastIfDebug
End Enum

Public Class Logger
    Public Shared ReadOnly Errors As New List(Of String)
    Public Shared Sub Info(Message As String)
    End Sub
    Public Shared Sub Warn(Message As String, Optional Behavior As LogBehavior = LogBehavior.ToastIfDebug)
    End Sub
    Public Shared Sub [Error](Failure As Exception, Message As String, Behavior As LogBehavior)
        Errors.Add(Message & ": " & Failure.Message)
    End Sub
End Class

Public Structure OneOf(Of T1, T2)
    Private Value As T1
    Public Shared Widening Operator CType(Value As T1) As OneOf(Of T1, T2)
        Return New OneOf(Of T1, T2) With {.Value = Value}
    End Operator
    Public Function Switch(Of TResult)(First As Func(Of T1, TResult), Second As Func(Of T2, TResult)) As TResult
        Return First(Value)
    End Function
End Structure

Public Class Settings
    Public Shared ReadOnly Values As New Dictionary(Of String, Object) From {
        {"UiLauncherTheme", 0}, {"UiLauncherHue", 180}, {"UiLauncherSat", 80},
        {"UiLauncherLight", 20}, {"UiLauncherDelta", 90}, {"UiBackgroundColorful", True}}
    Public Shared ReadOnly Writes As New List(Of String)
    Public Shared Function [Get](Of T)(Name As String) As T
        Return CType(Values(Name), T)
    End Function
    Public Shared Sub [Set](Name As String, Value As Object)
        Values(Name) = Value
        Writes.Add(Name)
        If Name = "UiLauncherTheme" Then ModRecoveredTheme.ThemeRefresh(CInt(Value))
    End Sub
End Class

Public Class TestImage
    Public Property Source As String
    Public Property Opacity As Double
End Class

Public Class TestWindow
    Public IsLoaded As Boolean = True
    Public ReadOnly PanTitle As New Border
    Public ReadOnly PanForm As New Border
    Public ReadOnly ImgTitle As New TestImage
End Class

Public Class TestSlider
    Public Value As Integer
    Public IsLoaded As Boolean = True
    Public Event Change(sender As Object, user As Boolean)
    Public Sub NotifyChange()
        RaiseEvent Change(Me, True)
    End Sub
End Class

Public Class TestRadio
    Public Checked As Boolean = True
End Class

Public Module Context
    Public ReadOnly UiThreadId As Integer = Thread.CurrentThread.ManagedThreadId
    Public ReadOnly UiQueue As New Queue(Of Action)
    Public ReadOnly FrmMain As New TestWindow
    Public Const PathImage As String = "fixture/"
    Public AnimationTime As Long
    Public AniControlEnabled As Integer

    Public Function GetTimeMs() As Long
        Return AnimationTime
    End Function
    Public Sub RunInUi(Action As Action)
        If Thread.CurrentThread.ManagedThreadId = UiThreadId Then
            Action()
        Else
            SyncLock UiQueue
                UiQueue.Enqueue(Action)
            End SyncLock
        End If
    End Sub
    Public Sub DrainUiQueue()
        If Thread.CurrentThread.ManagedThreadId <> UiThreadId Then Throw New Exception("Wrong fixture UI thread")
        While UiQueue.Count > 0
            Dim Work As Action
            SyncLock UiQueue
                Work = UiQueue.Dequeue()
            End SyncLock
            Work()
        End While
    End Sub
End Module
