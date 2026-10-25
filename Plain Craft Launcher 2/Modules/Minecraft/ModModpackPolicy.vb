Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

' Check package declarations before creating installation tasks, copying instance files,
' downloading dependencies, or handing control to an included launcher.
Friend Module ModModpackPolicy
    Friend Const QuiltModpackUnsupportedMessage As String = "此版本不支持安装 Quilt 整合包。"
    Private Const MaximumNestedDepth As Integer = 16

    Friend Sub EnsureSupportedModpack(Archive As ZipArchive)
        If Archive Is Nothing Then Throw New ArgumentNullException("Archive")
        CheckArchive(Archive, 0)
    End Sub

    Friend Function IsNestedModpackFile(FileNameOrPath As String) As Boolean
        If String.IsNullOrEmpty(FileNameOrPath) Then Return False
        Dim NormalizedPath = FileNameOrPath.Replace("\", "/")
        Dim Name = NormalizedPath.Substring(NormalizedPath.LastIndexOf("/"c) + 1)
        Return Name.Equals("modpack.zip", StringComparison.OrdinalIgnoreCase) OrElse
               Name.Equals("modpack.mrpack", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Sub CheckArchive(Archive As ZipArchive, Depth As Integer)
        If Depth > MaximumNestedDepth Then Throw New InvalidDataException("整合包嵌套层数过多，无法检查其加载器。")
        For Each Entry In Archive.Entries
            Dim EntryPath = Entry.FullName.Replace("\", "/")
            Dim Name = EntryPath.Substring(EntryPath.LastIndexOf("/"c) + 1).ToLowerInvariant()
            If IsNestedModpackFile(EntryPath) Then
                ' A nested ZIP entry is not seekable. Spool it outside the instance folder
                ' so large bundled packs do not have to be held entirely in memory.
                Dim TemporaryPath = Path.GetTempFileName()
                Try
                    Using Source = Entry.Open(), Target = File.Open(TemporaryPath, FileMode.Create, FileAccess.Write, FileShare.None)
                        Source.CopyTo(Target)
                    End Using
                    Using Nested = ZipFile.OpenRead(TemporaryPath)
                        CheckArchive(Nested, Depth + 1)
                    End Using
                Finally
                    File.Delete(TemporaryPath)
                End Try
            ElseIf Name.EndsWith(".json", StringComparison.Ordinal) OrElse Name = "mcbbs.packmeta" Then
                Dim Json As JObject = Nothing
                Try
                    Using Reader As New StreamReader(Entry.Open(), Encoding.UTF8, True)
                        Json = TryCast(JToken.Parse(Reader.ReadToEnd()), JObject)
                    End Using
                Catch ex As JsonException
                    ' Existing format-specific parsers report malformed manifests. Other
                    ' JSON files in a pack need not be launcher/version metadata at all.
                    Continue For
                End Try
                If Json Is Nothing Then Continue For
                If IsQuiltVersion(Json) OrElse
                   ((Name = "manifest.json" OrElse Name = "modrinth.index.json" OrElse
                     Name = "mmc-pack.json" OrElse Name = "mcbbs.packmeta" OrElse Name = "modpack.json") AndAlso
                    DeclaresQuilt(Json)) Then
                    Throw New NotSupportedException(QuiltModpackUnsupportedMessage)
                End If
            End If
        Next
    End Sub

    Private Function DeclaresQuilt(Json As JObject) As Boolean
        ' CurseForge: minecraft.modLoaders[].id.
        Dim Minecraft = TryCast(ValueOf(Json, "minecraft"), JObject)
        If HasQuiltEntry(ValueOf(Minecraft, "modLoaders"), "id") Then Return True
        ' Modrinth: dependencies["quilt-loader"].
        Dim Dependencies = TryCast(ValueOf(Json, "dependencies"), JObject)
        If Dependencies IsNot Nothing AndAlso Dependencies.Properties().Any(Function(p) IsQuiltLoaderId(p.Name)) Then Return True
        ' MultiMC: components[].uid. MCBBS/HMCL: addons[].id.
        If HasQuiltEntry(ValueOf(Json, "components"), "uid") OrElse HasQuiltEntry(ValueOf(Json, "addons"), "id") Then Return True
        ' Some HMCL metadata keeps add-ons as a version dictionary.
        Dim Addons = TryCast(ValueOf(Json, "addons"), JObject)
        If Addons IsNot Nothing AndAlso Addons.Properties().Any(Function(p) IsQuiltLoaderId(p.Name)) Then Return True
        For Each Key In New String() {"quiltLoaderVersion", "quiltVersion"}
            Dim Version = ValueOf(Json, Key)
            If Version IsNot Nothing AndAlso Version.Type <> JTokenType.Null AndAlso Not String.IsNullOrWhiteSpace(Version.ToString()) Then Return True
        Next
        Return False
    End Function

    Private Function IsQuiltVersion(Json As JObject) As Boolean
        ' Existing instance recognition remains unchanged. This check applies only to
        ' version/patch JSON embedded in a package being newly installed.
        Dim MainClass = ValueOf(Json, "mainClass")
        If MainClass IsNot Nothing AndAlso MainClass.ToString().StartsWith("org.quiltmc.loader.", StringComparison.OrdinalIgnoreCase) Then Return True
        If IsQuiltLoaderId(Convert.ToString(ValueOf(Json, "uid"))) Then Return True
        Dim Patches = TryCast(ValueOf(Json, "patches"), JArray)
        If Patches IsNot Nothing Then
            For Each Patch As JObject In Patches.OfType(Of JObject)()
                If IsQuiltLoaderId(Convert.ToString(ValueOf(Patch, "id"))) OrElse IsQuiltVersion(Patch) Then Return True
            Next
        End If
        Dim Libraries = TryCast(ValueOf(Json, "libraries"), JArray)
        If Libraries Is Nothing Then Return False
        For Each Library As JObject In Libraries.OfType(Of JObject)()
            Dim Name = ValueOf(Library, "name")
            If Name IsNot Nothing AndAlso Name.ToString().StartsWith("org.quiltmc:quilt-loader:", StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    Private Function HasQuiltEntry(Entries As JToken, IdProperty As String) As Boolean
        Dim Array = TryCast(Entries, JArray)
        If Array Is Nothing Then Return False
        Return Array.OfType(Of JObject)().Any(Function(e) IsQuiltLoaderId(Convert.ToString(ValueOf(e, IdProperty))))
    End Function

    Private Function IsQuiltLoaderId(Id As String) As Boolean
        If String.IsNullOrWhiteSpace(Id) Then Return False
        Id = Id.Trim().ToLowerInvariant()
        Return Id = "quilt" OrElse Id = "quilt-loader" OrElse Id = "quilt_loader" OrElse
               Id = "org.quiltmc.quilt-loader" OrElse Id = "org.quiltmc:quilt-loader" OrElse
               Id.StartsWith("quilt-", StringComparison.Ordinal) OrElse
               Id.StartsWith("org.quiltmc:quilt-loader:", StringComparison.Ordinal)
    End Function

    Private Function ValueOf(Json As JObject, Name As String) As JToken
        If Json Is Nothing Then Return Nothing
        Dim PropertyValue = Json.Properties().FirstOrDefault(Function(p) String.Equals(p.Name, Name, StringComparison.OrdinalIgnoreCase))
        Return If(PropertyValue Is Nothing, Nothing, PropertyValue.Value)
    End Function
End Module
