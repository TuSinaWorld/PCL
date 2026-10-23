. (Join-Path $PSScriptRoot 'TestSupport.ps1')
$methods = Get-ProductionSection '    <StructLayout(' '    Private Sub BtnCave_Click'
$test = @'
    Private Shared Sub Hint(Message As String)
        Throw New Exception("No system-memory operation may run during this test.")
    End Sub
    Public Shared Sub Main()
        If Marshal.SizeOf(GetType(PrivilegeToken)) <> 16 Then Throw New Exception("Unexpected TOKEN_PRIVILEGES size")
        If Marshal.OffsetOf(GetType(PrivilegeToken), "Luid").ToInt32() <> 4 Then Throw New Exception("Unexpected LUID offset")
        If Marshal.OffsetOf(GetType(PrivilegeToken), "Attributes").ToInt32() <> 12 Then Throw New Exception("Unexpected attributes offset")
        Dim Flags = System.Reflection.BindingFlags.NonPublic Or System.Reflection.BindingFlags.Static
        Dim Method = GetType(InteropTests).GetMethod("LookupPrivilegeValue", Flags)
        Dim Attribute = CType(Method.GetCustomAttributes(GetType(DllImportAttribute), False)(0), DllImportAttribute)
        If Attribute.CharSet <> CharSet.Unicode OrElse Not Attribute.SetLastError Then Throw New Exception("Privilege lookup marshalling mismatch")
        Method = GetType(InteropTests).GetMethod("NtSetSystemInformation", Flags)
        If Not Method.GetParameters()(1).ParameterType.IsByRef Then Throw New Exception("Memory-list command is not ByRef")
        If Marshal.SizeOf(GetType(Integer)) <> 4 Then Throw New Exception("Memory-list command has unexpected width")
        Console.WriteLine("PASS: native interop declarations compile; TOKEN_PRIVILEGES layout and command width checked; no native call executed.")
    End Sub
'@
$stub = @'
Public Class WindowsUtils
    Public Shared Function HasAdminRole() As Boolean
        Return False
    End Function
End Class
'@
$nl = [Environment]::NewLine
$text = (@('Imports System','Imports System.ComponentModel','Imports System.Runtime.InteropServices','Imports System.Security.Principal','Public Class InteropTests') -join $nl) + $nl + $methods + $test + $nl + 'End Class' + $nl + $stub + $nl
$path = Join-Path $OutputRoot 'InteropTests.vb'
[System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
$executable = Join-Path $OutputRoot 'InteropTests.exe'
Compile-TestProgram -SourcePath $path -OutputPath $executable
if ($LASTEXITCODE -ne 0) { throw 'Interop helper compilation failed' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Interop declaration checks failed' }

