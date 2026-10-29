Imports System
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Shapes

' Only UI creation, validation policy, animation close, and logger sinks are fixture code.
' Text editing, MaxLength, routed text input, dispatcher frames, and modal bookkeeping are WPF.
Public Module InputContext
    Public ReadOnly WaitingMyMsgBox As New Queue(Of MyMsgBoxConverter)
    Public FrmMain As New FixtureMainWindow
    Public XamlInputMaxLength As Integer
    Public PendingInput As String
    Public CancelInput As Boolean
    Public LastConverter As MyMsgBoxConverter
    Public LastControl As MyMsgInput
    Public TickCount As Integer
    Public CloseCount As Integer
    Public ReadOnly Logs As New List(Of String)
    Public ReadOnly Errors As New List(Of String)
    Private NextUuid As Integer

    Public Sub Reset(Optional Input As String = Nothing, Optional Cancel As Boolean = False)
        If WaitingMyMsgBox.Count <> 0 Then Throw New Exception("Previous test left a queued dialog.")
        PendingInput = Input
        CancelInput = Cancel
        LastConverter = Nothing
        LastControl = Nothing
        TickCount = 0
        CloseCount = 0
        FrmMain = New FixtureMainWindow
        Logs.Clear()
        Errors.Clear()
    End Sub

    Public Function GetUuid() As Integer
        NextUuid += 1
        Return NextUuid
    End Function
    Public Function GetWPFSize(Value As Double) As Double
        Return Value
    End Function
    Public Function RunInUi() As Boolean
        Return True
    End Function
    Public Sub MyMsgBoxTick()
        TickCount += 1
        LastConverter = WaitingMyMsgBox.Dequeue()
        LastControl = New MyMsgInput(LastConverter)
        If PendingInput IsNot Nothing Then LastControl.TextArea.TypeText(PendingInput)
        Dim Button = If(CancelInput, LastControl.Btn2, LastControl.Btn1)
        Button.RaiseEvent(New RoutedEventArgs(System.Windows.Controls.Button.ClickEvent))
        If LastConverter.WaitFrame.Continue Then Throw New Exception("Fixture dialog failed to close; refusing to enter an unbounded dispatcher frame.")
    End Sub
End Module

Public Class FixtureMainWindow
    Public DragStops As Integer
    Public Sub DragStop()
        DragStops += 1
    End Sub
End Class

Public Enum LogBehavior
    Toast
End Enum
Public Class Logger
    Public Shared Sub Info(Message As String)
        InputContext.Logs.Add(Message)
    End Sub
    Public Shared Sub [Error](Failure As Exception, Message As String, Behavior As LogBehavior)
        InputContext.Errors.Add(Message & ": " & Failure.Message)
    End Sub
End Class

Public Class Validate
End Class
Public Class MyTextBox
    Inherits TextBox
    Public Property HintText As String
    Public Property ValidateRules As Collection(Of Validate)
    Public IsValidated As Boolean = True
    Public ValidateCount As Integer
    Public Sub Validate()
        ValidateCount += 1
    End Sub
    Public Sub TypeText(Value As String)
        Dim Composition As New TextComposition(InputManager.Current, Me, Value)
        Dim Arguments As New TextCompositionEventArgs(Keyboard.PrimaryDevice, Composition)
        Arguments.RoutedEvent = TextCompositionManager.TextInputEvent
        Me.RaiseEvent(Arguments)
        If Value.Length > 0 AndAlso Not Arguments.Handled Then Throw New Exception("WPF did not handle the real TextInput event.")
    End Sub
End Class

Public Class MyButton
    Inherits Button
    Public Enum ColorState
        Normal
        Red
    End Enum
    Public Property Text As String
    Public Property ColorType As ColorState
End Class

Partial Public Class MyMsgInput
    Private ReadOnly MyConverter As MyMsgBoxConverter
    Public WithEvents Btn1 As MyButton
    Public WithEvents Btn2 As MyButton
    Public TextArea As MyTextBox
    Public PasswordArea As PasswordBox = New PasswordBox()
    Public LabTitle As TextBlock
    Public LabText As TextBlock
    Public PanText As ScrollViewer
    Public ShapeLine As Rectangle
    Private Sub InitializeComponent()
        Btn1 = New MyButton With {.Name = "Btn1"}
        Btn2 = New MyButton With {.Name = "Btn2"}
        TextArea = New MyTextBox With {.MaxLength = InputContext.XamlInputMaxLength}
        LabTitle = New TextBlock
        LabText = New TextBlock
        PanText = New ScrollViewer
        ShapeLine = New Rectangle
    End Sub
    Private Sub Close()
        InputContext.CloseCount += 1
        MyConverter.WaitFrame.Continue = False
    End Sub
End Class

Public Class ResultWithForbiddenToString
    Public Overrides Function ToString() As String
        Throw New Exception("Sensitive result must never be formatted.")
    End Function
End Class
