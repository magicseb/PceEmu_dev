Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Text.Json

''' <summary>
''' Logique de téléchargement archive.org indépendante de toute interface :
''' lister les fichiers d'un item, télécharger un fichier avec suivi de
''' progression et annulation. Utilisée par ArchiveOrgForm (bureau) et par
''' le menu manette (GamepadMenuForm/MainForm), pour que les deux chemins
''' se comportent exactement pareil.
''' </summary>
Public NotInheritable Class ArchiveOrgClient

    Private Shared ReadOnly _http As New HttpClient()

    Shared Sub New()
        _http.Timeout = TimeSpan.FromMinutes(5)
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PceEmu/1.0")
    End Sub

    ''' <summary>Liste triée des fichiers d'un item que la bibliothèque sait ouvrir.</summary>
    Public Shared Function FetchFileNames(item As String) As List(Of String)
        Dim url = "https://archive.org/metadata/" & Uri.EscapeDataString(item)
        Dim json = _http.GetStringAsync(url).GetAwaiter().GetResult()
        Dim names As New List(Of String)()
        Using doc = JsonDocument.Parse(json)
            Dim files As JsonElement
            If doc.RootElement.TryGetProperty("files", files) Then
                For Each f In files.EnumerateArray()
                    Dim ne As JsonElement
                    If f.TryGetProperty("name", ne) Then
                        Dim nm = ne.GetString()
                        If nm IsNot Nothing AndAlso RomArchive.IsSupported(nm) Then names.Add(nm)
                    End If
                Next
            End If
        End Using
        names.Sort(StringComparer.OrdinalIgnoreCase)
        Return names
    End Function

    ''' <summary>Noms de base (sans extension) déjà présents dans un dossier, insensible à la casse.</summary>
    Public Shared Function OwnedBaseNames(folder As String) As HashSet(Of String)
        Dim owned As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If String.IsNullOrEmpty(folder) OrElse Not Directory.Exists(folder) Then Return owned
        Try
            For Each f In Directory.EnumerateFiles(folder)
                If RomArchive.IsSupported(f) Then owned.Add(Path.GetFileNameWithoutExtension(f))
            Next
        Catch
            ' dossier illisible : on n'exclut rien plutôt que d'échouer
        End Try
        Return owned
    End Function

    ''' <summary>
    ''' Télécharge un fichier d'un item vers destPath (via un .part renommé à la fin).
    ''' progress(msg) est appelé régulièrement ; cancelCheck() est consulté entre
    ''' chaque bloc lu. Lève OperationCanceledException si annulé, sinon toute
    ''' exception réseau/E-S rencontrée.
    ''' </summary>
    Public Shared Sub DownloadItemFile(item As String, name As String, destPath As String,
                                        progress As Action(Of String), cancelCheck As Func(Of Boolean))
        Dim partPath = destPath & ".part"
        Try
            Dim url = "https://archive.org/download/" & Uri.EscapeDataString(item) & "/" & EscapePath(name)
            Using resp = _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult()
                resp.EnsureSuccessStatusCode()
                Dim total = If(resp.Content.Headers.ContentLength.HasValue, resp.Content.Headers.ContentLength.Value, -1L)
                Using src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                    Using fs As New FileStream(partPath, FileMode.Create, FileAccess.Write)
                        Dim buf(1048575) As Byte
                        Dim got As Long = 0, n As Integer
                        Do
                            If cancelCheck IsNot Nothing AndAlso cancelCheck() Then Exit Do
                            n = src.Read(buf, 0, buf.Length)
                            If n <= 0 Then Exit Do
                            fs.Write(buf, 0, n)
                            got += n
                            If progress IsNot Nothing Then
                                If total > 0 Then
                                    progress($"Téléchargement… {CInt(got * 100L \ total)} % ({got \ (1024 * 1024)}/{total \ (1024 * 1024)} Mo)")
                                Else
                                    progress($"Téléchargement… {got \ 1024} Ko")
                                End If
                            End If
                        Loop
                    End Using
                End Using
            End Using
        Catch
            TryDelete(partPath)
            Throw
        End Try

        If cancelCheck IsNot Nothing AndAlso cancelCheck() Then
            TryDelete(partPath)
            Throw New OperationCanceledException()
        End If

        If File.Exists(destPath) Then File.Delete(destPath)
        File.Move(partPath, destPath)
    End Sub

    ''' <summary>Nom de fichier local sûr : dernier segment, sans caractères interdits.</summary>
    Public Shared Function SafeLocalName(entryName As String) As String
        Dim leaf = Path.GetFileName(entryName.Replace("\"c, "/"c))
        If String.IsNullOrWhiteSpace(leaf) Then leaf = "jeu.bin"
        For Each bad In Path.GetInvalidFileNameChars()
            leaf = leaf.Replace(bad, "_"c)
        Next
        Return leaf
    End Function

    ''' <summary>Échappe chaque segment d'un chemin d'item, en gardant les « / ».</summary>
    Public Shared Function EscapePath(entryName As String) As String
        Return String.Join("/", entryName.Replace("\"c, "/"c).Split("/"c).
                           Select(Function(seg) Uri.EscapeDataString(seg)))
    End Function

    Private Shared Sub TryDelete(path As String)
        Try
            If File.Exists(path) Then File.Delete(path)
        Catch
        End Try
    End Sub

End Class
