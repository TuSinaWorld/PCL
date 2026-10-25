Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Text

Public Class LocalModpackChecks
    Private Shared Count As Integer

    Private Shared Sub Check(Condition As Boolean, Message As String)
        If Not Condition Then Throw New Exception("Failed: " & Message)
        Count += 1
    End Sub

    Private Shared Function TextEntries(Name As String, Json As String) As Dictionary(Of String, Byte())
        Return New Dictionary(Of String, Byte()) From {{Name, Encoding.UTF8.GetBytes(Json)}}
    End Function

    Private Shared Function CreateArchive(Entries As Dictionary(Of String, Byte())) As Byte()
        Using Buffer As New MemoryStream()
            Using Archive As New ZipArchive(Buffer, ZipArchiveMode.Create, True)
                For Each Pair In Entries
                    Using Target = Archive.CreateEntry(Pair.Key).Open()
                        Target.Write(Pair.Value, 0, Pair.Value.Length)
                    End Using
                Next
            End Using
            Return Buffer.ToArray()
        End Using
    End Function

    Private Shared Sub Verify(Root As String, Name As String, Entries As Dictionary(Of String, Byte()), Reject As Boolean)
        Dim FixturePath = Path.Combine(Root, Name & ".zip")
        File.WriteAllBytes(FixturePath, CreateArchive(Entries))
        Dim WasRejected As Boolean = False
        Dim InstallationReached As Boolean = False
        Try
            Using Archive = ZipFile.OpenRead(FixturePath)
                ModModpackPolicy.EnsureSupportedModpack(Archive)
                InstallationReached = True
            End Using
        Catch ex As NotSupportedException
            WasRejected = True
            Check(ex.Message = ModModpackPolicy.QuiltModpackUnsupportedMessage, Name & ": clear unsupported-loader message")
        End Try
        Check(WasRejected = Reject, Name & ": expected package support result")
        Check(InstallationReached <> Reject, Name & ": rejected package does not reach installation continuation")
    End Sub

    Public Shared Function Run(Root As String) As String
        Directory.CreateDirectory(Root)
        Verify(Root, "curseforge-vanilla", TextEntries("manifest.json", "{""minecraft"":{""version"":""1.21.1"",""modLoaders"":[]}}"), False)
        Verify(Root, "curseforge-forge", TextEntries("manifest.json", "{""minecraft"":{""modLoaders"":[{""id"":""forge-52.0.1""}]}}"), False)
        Verify(Root, "curseforge-fabric", TextEntries("manifest.json", "{""minecraft"":{""modLoaders"":[{""id"":""fabric-0.16.0""}]}}"), False)
        Verify(Root, "curseforge-neoforge", TextEntries("manifest.json", "{""minecraft"":{""modLoaders"":[{""id"":""neoforge-21.1.0""}]}}"), False)
        Verify(Root, "curseforge-quilt", TextEntries("manifest.json", "{""minecraft"":{""modLoaders"":[{""id"":""quilt-0.26.0""}]}}"), True)
        Verify(Root, "wrapped-curseforge-quilt", TextEntries("wrapper/manifest.json", "{""MINECRAFT"":{""MODLOADERS"":[{""ID"":""QUILT-0.26.0""}]}}"), True)
        Verify(Root, "modrinth-fabric", TextEntries("modrinth.index.json", "{""dependencies"":{""minecraft"":""1.21.1"",""fabric-loader"":""0.16.0""}}"), False)
        Verify(Root, "modrinth-quilt", TextEntries("modrinth.index.json", "{""dependencies"":{""minecraft"":""1.21.1"",""quilt-loader"":""0.26.0""}}"), True)
        Verify(Root, "wrapped-modrinth-quilt", TextEntries("wrapper/MODRINTH.INDEX.JSON", "{""dependencies"":{""QUILT-LOADER"":""0.26.0""}}"), True)
        Verify(Root, "mmc-forge", TextEntries("mmc-pack.json", "{""components"":[{""uid"":""net.minecraft""},{""uid"":""net.minecraftforge""}]}"), False)
        Verify(Root, "mmc-quilt", TextEntries("mmc-pack.json", "{""components"":[{""uid"":""org.quiltmc.quilt-loader"",""version"":""0.26.0""}]}"), True)
        Verify(Root, "mcbbs-fabric", TextEntries("mcbbs.packmeta", "{""addons"":[{""id"":""game"",""version"":""1.21.1""},{""id"":""fabric"",""version"":""0.16.0""}]}"), False)
        Verify(Root, "mcbbs-quilt", TextEntries("mcbbs.packmeta", "{""addons"":[{""id"":""quilt"",""version"":""0.26.0""}]}"), True)
        Verify(Root, "mcbbs-manifest-quilt", TextEntries("manifest.json", "{""addons"":[{""id"":""quilt"",""version"":""0.26.0""}]}"), True)
        Verify(Root, "hmcl-vanilla", TextEntries("modpack.json", "{""gameVersion"":""1.21.1"",""name"":""My quilt inspired build""}"), False)
        Verify(Root, "hmcl-quilt-version", TextEntries("modpack.json", "{""gameVersion"":""1.21.1"",""quiltLoaderVersion"":""0.26.0""}"), True)
        Verify(Root, "hmcl-quilt-addons", TextEntries("modpack.json", "{""addons"":{""quilt-loader"":""0.26.0""}}"), True)
        Verify(Root, "hmcl-embedded-quilt", New Dictionary(Of String, Byte()) From {
            {"modpack.json", Encoding.UTF8.GetBytes("{""gameVersion"":""1.21.1""}")},
            {"minecraft/custom.json", Encoding.UTF8.GetBytes("{""libraries"":[{""name"":""org.quiltmc:quilt-loader:0.26.0""}]}")}}, True)
        Verify(Root, "hmcl-inline-quilt-patch", TextEntries("minecraft/demo.json", "{""patches"":[{""id"":""quilt"",""version"":""0.26.0""}]}"), True)
        Verify(Root, "hmcl-inline-fabric-patch", TextEntries("minecraft/demo.json", "{""patches"":[{""id"":""fabric"",""version"":""0.16.0""}]}"), False)
        Verify(Root, "raw-instance-quilt-library", TextEntries(".minecraft/versions/demo/demo.json", "{""libraries"":[{""name"":""org.quiltmc:quilt-loader:0.26.0""}]}"), True)
        Verify(Root, "raw-instance-quilt-mainclass", TextEntries(".minecraft/versions/demo/demo.json", "{""mainClass"":""org.quiltmc.loader.impl.launch.knot.KnotClient""}"), True)
        Verify(Root, "mmc-quilt-patch", TextEntries("patches/quilt.json", "{""uid"":""org.quiltmc.quilt-loader""}"), True)
        Verify(Root, "quilt-api-is-not-loader", TextEntries(".minecraft/versions/demo/demo.json", "{""libraries"":[{""name"":""org.quiltmc:quilted-fabric-api:1.0.0""}]}"), False)
        Verify(Root, "quilt-description-is-not-loader", TextEntries("config/example.json", "{""description"":""quilt-loader"",""addons"":[{""id"":""quilt""}]}"), False)
        Verify(Root, "unrelated-malformed-json", TextEntries("config/example.json", "// configuration is parsed by its owner"), False)
        Dim SupportedInner = CreateArchive(TextEntries("modrinth.index.json", "{""dependencies"":{""minecraft"":""1.21.1""}}"))
        Dim QuiltInner = CreateArchive(TextEntries("modrinth.index.json", "{""dependencies"":{""quilt-loader"":""0.26.0""}}"))
        Verify(Root, "launcher-pack-supported", New Dictionary(Of String, Byte()) From {{"modpack.mrpack", SupportedInner}, {"Launcher.exe", New Byte() {0, 1, 2}}}, False)
        Verify(Root, "launcher-pack-quilt", New Dictionary(Of String, Byte()) From {{"modpack.mrpack", QuiltInner}, {"Launcher.exe", New Byte() {0, 1, 2}}}, True)
        Verify(Root, "wrapped-launcher-pack-quilt", New Dictionary(Of String, Byte()) From {{"wrapper/modpack.zip", QuiltInner}}, True)
        Verify(Root, "two-inner-packs-one-quilt", New Dictionary(Of String, Byte()) From {{"modpack.zip", SupportedInner}, {"other/modpack.mrpack", QuiltInner}}, True)
        Verify(Root, "two-inner-packs-reverse-order", New Dictionary(Of String, Byte()) From {{"other/modpack.mrpack", QuiltInner}, {"modpack.zip", SupportedInner}}, True)
        Verify(Root, "rar-is-not-installable-inner-pack", New Dictionary(Of String, Byte()) From {{"modpack.rar", QuiltInner}, {"modpack.zip", SupportedInner}}, False)
        Dim SelectionRoot = Path.Combine(Root, "nested-selection")
        Directory.CreateDirectory(SelectionRoot)
        File.WriteAllBytes(Path.Combine(SelectionRoot, "modpack.rar"), QuiltInner)
        File.WriteAllBytes(Path.Combine(SelectionRoot, "modpack.json"), Encoding.UTF8.GetBytes("{}"))
        Dim ExpectedSelection = Path.Combine(SelectionRoot, "MODPACK.MRPACK")
        File.WriteAllBytes(ExpectedSelection, SupportedInner)
        Dim SelectedPath = System.Linq.Enumerable.FirstOrDefault(Directory.EnumerateFiles(SelectionRoot), Function(p) ModModpackPolicy.IsNestedModpackFile(p))
        Check(SelectedPath = ExpectedSelection, "installation selector ignores earlier unscanned rar and metadata files")
        Using SelectedArchive = ZipFile.OpenRead(SelectedPath)
            ModModpackPolicy.EnsureSupportedModpack(SelectedArchive)
        End Using
        Check(ModModpackPolicy.IsNestedModpackFile("wrapper\MODPACK.ZIP"), "archive and Windows paths share case-insensitive nested selection")
        Check(Not ModModpackPolicy.IsNestedModpackFile("wrapper/modpack.zip.backup"), "similarly named backups are not installable nested packs")
        Dim TwiceNested = CreateArchive(New Dictionary(Of String, Byte()) From {{"modpack.mrpack", QuiltInner}})
        Verify(Root, "two-nested-quilt", New Dictionary(Of String, Byte()) From {{"modpack.zip", TwiceNested}}, True)
        Dim Deep = SupportedInner
        For Index = 0 To 16
            Deep = CreateArchive(New Dictionary(Of String, Byte()) From {{"modpack.zip", Deep}})
        Next
        Dim DepthRejected As Boolean
        Using Buffer As New MemoryStream(Deep), Archive As New ZipArchive(Buffer, ZipArchiveMode.Read)
            Try
                ModModpackPolicy.EnsureSupportedModpack(Archive)
            Catch ex As InvalidDataException
                DepthRejected = ex.Message.Contains("嵌套层数")
            End Try
        End Using
        Check(DepthRejected, "unbounded nested archives fail before installation")
        Return Count.ToString() & " checks passed; synthetic ZIP/JSON fixtures only; no network, instance install, or target executable ran."
    End Function
End Class
