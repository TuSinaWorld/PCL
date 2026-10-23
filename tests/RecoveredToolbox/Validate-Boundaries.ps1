. (Join-Path $PSScriptRoot 'TestSupport.ps1')
$methods = (Get-ProductionSection '    Private Shared Function ValidateDownloadUrl' '    Private Sub BtnDownloadStart_Click') +
           (Get-ProductionSection '    Private Shared Function GetCleanupRoot' '    Private Sub BtnMemory_Click')
$testMain = @'
    Private Shared PathTemp As String
    Private Shared Checks As Integer
    Private Shared Sub Check(Condition As Boolean, Name As String)
        If Not Condition Then Throw New Exception("FAIL: " & Name)
        Checks += 1
    End Sub
    Public Shared Sub Main(Args As String())
        Dim Fixture = Args(0)
        Dim Root = Path.Combine(Fixture, "PCL")
        Dim Cache = Path.Combine(Root, "Cache")
        Dim Outside = Path.Combine(Fixture, "outside")
        Directory.CreateDirectory(Cache)
        Directory.CreateDirectory(Outside)
        File.WriteAllText(Path.Combine(Outside, "sentinel.txt"), "keep")
        File.WriteAllBytes(Path.Combine(Cache, "normal.bin"), New Byte() {1, 2, 3})
        Check(ValidateDownloadUrl("https://example.com/file.zip") = "", "HTTPS URL")
        Check(ValidateDownloadUrl("HTTP://example.com/file.zip") = "", "uppercase HTTP URL")
        Check(ValidateDownloadUrl(New Uri(Path.Combine(Fixture, "file.zip")).AbsoluteUri) <> "", "reject file protocol")
        Check(ValidateDownloadUrl("javascript:alert(1)") <> "", "reject javascript protocol")
        Check(ValidateDownloadUrl("https://") <> "", "reject missing host")
        Check(ValidateDownloadName("file.zip") = "", "ordinary filename")
        Check(ValidateDownloadName("..\escape.exe") <> "", "reject path traversal filename")
        Check(ValidateDownloadName("CON.txt") <> "", "reject device name")
        Check(ValidateDownloadName("bad.") <> "", "reject trailing dot")
        Check(ValidateDownloadName("bad ") <> "", "reject trailing space")
        Check(ValidateDownloadName("bad:name") <> "", "reject alternate stream syntax")
        Check(ValidateDownloadFolder(Path.GetPathRoot(Fixture).Substring(0, 2) & "relative") <> "", "reject drive-relative folder")
        Check(ValidateDownloadFolder("\\server\share") <> "", "reject unsupported UNC")
        Check(ValidateDownloadFolder(Cache) = "", "absolute local folder")
        PathTemp = Root
        Dim Rejected As Boolean = False
        Try
            GetCleanupRoot()
        Catch ex As InvalidOperationException
            Rejected = True
        End Try
        Check(Rejected, "reject arbitrary configured cache root")
        Dim Count As Integer, Skipped As Integer, Bytes As Long
        Rejected = False
        Try
            DeleteCacheContents(Root, Outside, Count, Skipped, Bytes)
        Catch ex As IOException
            Rejected = True
        End Try
        Check(Rejected AndAlso File.Exists(Path.Combine(Outside, "sentinel.txt")), "outside root preserved")
        Dim Sibling = Root & "-sibling"
        Directory.CreateDirectory(Sibling)
        File.WriteAllText(Path.Combine(Sibling, "sentinel.txt"), "keep")
        Rejected = False
        Try
            DeleteCacheContents(Root, Sibling, Count, Skipped, Bytes)
        Catch ex As IOException
            Rejected = True
        End Try
        Check(Rejected AndAlso File.Exists(Path.Combine(Sibling, "sentinel.txt")), "prefix sibling preserved")
        Using Locked As New FileStream(Path.Combine(Cache, "locked.bin"), FileMode.Create, FileAccess.ReadWrite, FileShare.None)
            Locked.WriteByte(7)
            DeleteCacheContents(Root, Cache, Count, Skipped, Bytes)
            Check(File.Exists(Path.Combine(Cache, "locked.bin")), "locked file preserved")
        End Using
        Check(Not File.Exists(Path.Combine(Cache, "normal.bin")) AndAlso Count = 1 AndAlso Bytes = 3, "only ordinary cache file removed")
        Check(Skipped >= 1, "locked file counted as skipped")
        Check(File.Exists(Path.Combine(Outside, "sentinel.txt")), "junction target preserved")
        If Args.Length > 1 AndAlso Args(1) = "junction" Then
            Check(Directory.Exists(Path.Combine(Cache, "outside-link")), "junction skipped")
            Check(Skipped >= 2, "junction counted as skipped")
        End If
        Console.WriteLine("PASS: " & Checks & " assertions; source-derived validation and cleanup helpers.")
    End Sub
'@
$nl = [Environment]::NewLine
$harness = (@('Imports System','Imports System.IO','Imports System.Linq','Public Class ToolboxTests') -join $nl) + $nl + $methods + $testMain + $nl + 'End Class' + $nl
$harnessPath = Join-Path $OutputRoot 'ToolboxTests.vb'
[System.IO.File]::WriteAllText($harnessPath, $harness, [System.Text.UTF8Encoding]::new($false))
$executable = Join-Path $OutputRoot 'ToolboxTests.exe'
Compile-TestProgram -SourcePath $harnessPath -OutputPath $executable
if ($LASTEXITCODE -ne 0) { throw 'Toolbox helper compilation failed' }
$fixture = Join-Path $OutputRoot ('fixture-' + [Guid]::NewGuid().ToString('N'))
$cache = Join-Path $fixture 'PCL\Cache'
$outside = Join-Path $fixture 'outside'
New-Item -ItemType Directory -Path $cache,$outside -Force | Out-Null
$junction = Join-Path $cache 'outside-link'
$junctionCreated = $false
try {
    New-Item -ItemType Junction -Path $junction -Target $outside -ErrorAction Stop | Out-Null
    $junctionCreated = $true
} catch {
    Write-Output ('Junction case unavailable: ' + $_.Exception.Message)
}
$testArgs = @($fixture)
if ($junctionCreated) { $testArgs += 'junction' }
& $executable @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Toolbox helper tests failed' }
if ($junctionCreated) {
    $expectedJunction = [IO.Path]::GetFullPath((Join-Path $cache 'outside-link'))
    $actualJunction = [IO.Path]::GetFullPath($junction)
    $junctionItem = Get-Item -LiteralPath $actualJunction -Force
    if ($actualJunction -cne $expectedJunction -or -not ($junctionItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Refusing to remove an unexpected fixture junction.'
    }
    [IO.Directory]::Delete($actualJunction, $false)
}

