''' <summary>
''' Ouvre une ROM, qu'elle soit nue ou enfermée dans une archive.
'''
''' Rien n'est jamais écrit sur le disque : l'archive est lue en mémoire et seule
''' l'entrée retenue est décompressée. C'est aussi ce qui met le programme hors de
''' portée des failles de traversée de répertoire — l'API d'extraction vers un
''' dossier n'est tout simplement pas utilisée.
''' </summary>
Public Class RomArchive

    ''' <summary>Extensions reconnues comme des ROMs PC Engine.</summary>
    Private Shared ReadOnly RomExtensions As String() = {".pce", ".sgx", ".bin", ".cue", ".ccd", ".chd"}

    ''' <summary>
    ''' Garde-fou contre les archives piégées : aucune HuCard ne dépasse 2,5 Mo,
    ''' on refuse donc de décompresser au-delà de 8 Mo.
    ''' </summary>
    Private Const MAX_ROM_BYTES As Integer = 8 * 1024 * 1024

    ''' <summary>Nom du jeu, sans chemin ni extension.</summary>
    Public ReadOnly Property Title As String

    ''' <summary>Contenu brut de la ROM.</summary>
    Public ReadOnly Property Data As Byte()

    Private Sub New(name As String, content As Byte())
        _Title = name
        _Data = content
    End Sub

    ''' <summary>Vrai si l'extension laisse espérer une ROM ou une archive.</summary>
    Public Shared Function IsSupported(path As String) As Boolean
        Dim ext = System.IO.Path.GetExtension(path).ToLowerInvariant()
        If ext = ".zip" OrElse ext = ".7z" Then Return True
        Return Array.IndexOf(RomExtensions, ext) >= 0
    End Function

    ''' <summary>Charge une ROM depuis un fichier nu, un ZIP ou un 7z.</summary>
    Public Shared Function Load(path As String) As RomArchive
        Select Case System.IO.Path.GetExtension(path).ToLowerInvariant()
            Case ".zip"
                Return LoadZip(path)
            Case ".7z"
                Return LoadSevenZip(path)
            Case Else
                Return New RomArchive(System.IO.Path.GetFileNameWithoutExtension(path),
                                      System.IO.File.ReadAllBytes(path))
        End Select
    End Function

    ''' <summary>ZIP : géré par la bibliothèque standard, sans dépendance externe.</summary>
    Private Shared Function LoadZip(path As String) As RomArchive
        Using archive = System.IO.Compression.ZipFile.OpenRead(path)
            Dim best As System.IO.Compression.ZipArchiveEntry = Nothing

            For Each entry In archive.Entries
                If Not LooksLikeRom(entry.FullName) Then Continue For
                If best Is Nothing OrElse entry.Length > best.Length Then best = entry
            Next

            If best Is Nothing Then
                Throw New InvalidOperationException("Aucune ROM PC Engine dans cette archive ZIP.")
            End If

            CheckSize(best.Length, best.FullName)

            Using stream = best.Open()
                Return New RomArchive(System.IO.Path.GetFileNameWithoutExtension(best.Name),
                                      ReadFully(stream, CInt(best.Length)))
            End Using
        End Using
    End Function

    ''' <summary>7z : décodage LZMA confié à SharpCompress.</summary>
    Private Shared Function LoadSevenZip(path As String) As RomArchive
        Using archive = SharpCompress.Archives.SevenZip.SevenZipArchive.Open(path)
            Dim best As SharpCompress.Archives.SevenZip.SevenZipArchiveEntry = Nothing

            For Each entry In archive.Entries
                If entry.IsDirectory Then Continue For
                If Not LooksLikeRom(entry.Key) Then Continue For
                If best Is Nothing OrElse entry.Size > best.Size Then best = entry
            Next

            If best Is Nothing Then
                Throw New InvalidOperationException("Aucune ROM PC Engine dans cette archive 7z.")
            End If

            CheckSize(best.Size, best.Key)

            Using stream = best.OpenEntryStream()
                Return New RomArchive(System.IO.Path.GetFileNameWithoutExtension(best.Key),
                                      ReadFully(stream, CInt(best.Size)))
            End Using
        End Using
    End Function

    Private Shared Function LooksLikeRom(entryName As String) As Boolean
        If String.IsNullOrEmpty(entryName) Then Return False
        Dim ext = System.IO.Path.GetExtension(entryName).ToLowerInvariant()
        Return Array.IndexOf(RomExtensions, ext) >= 0
    End Function

    Private Shared Sub CheckSize(size As Long, entryName As String)
        If size > MAX_ROM_BYTES Then
            Throw New InvalidOperationException(
                "L'entrée « " & entryName & " » fait " & (size \ 1024) &
                " Ko : c'est trop pour une HuCard, l'archive est refusée.")
        End If
    End Sub

    ''' <summary>Lit un flux jusqu'au bout, sans dépasser la taille annoncée.</summary>
    Private Shared Function ReadFully(stream As System.IO.Stream, expected As Integer) As Byte()
        Using buffer = New System.IO.MemoryStream(Math.Max(expected, 1024))
            Dim chunk(65535) As Byte
            Dim total = 0

            Do
                Dim read = stream.Read(chunk, 0, chunk.Length)
                If read <= 0 Then Exit Do

                total += read
                If total > MAX_ROM_BYTES Then
                    Throw New InvalidOperationException("Décompression interrompue : contenu trop volumineux.")
                End If

                buffer.Write(chunk, 0, read)
            Loop

            Return buffer.ToArray()
        End Using
    End Function

End Class
