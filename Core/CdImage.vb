''' <summary>
''' Lecteur d'image CD pour le CD-ROM² : parse un .cue (ou .ccd/.img CloneCD) et
''' expose les secteurs et la table des matières (TOC).
'''
''' Ne gère pour l'instant que les pistes MODE1/2352 (données) — suffisant pour les
''' jeux CD-ROM² sans piste audio ; les pistes AUDIO sont reconnues dans la TOC.
''' </summary>
Public Class CdImage

    Private ReadOnly img As Byte()
    Private ReadOnly sectorSize As Integer
    Private ReadOnly userOffset As Integer          ' décalage des données utilisateur dans le secteur brut

    Public ReadOnly Property TotalSectors As Integer
    Public ReadOnly Property FirstTrack As Integer
    Public ReadOnly Property LastTrack As Integer

    ''' <summary>Piste : LBA de départ, drapeau audio.</summary>
    Public Structure TrackInfo
        Public Number As Integer
        Public StartLba As Integer
        Public IsAudio As Boolean
    End Structure
    Private ReadOnly tracks As New System.Collections.Generic.List(Of TrackInfo)

    ''' <summary>LBA du lead-out (= nombre total de secteurs de la zone programme).</summary>
    Public ReadOnly Property LeadOutLba As Integer
        Get
            Return TotalSectors
        End Get
    End Property

    Public ReadOnly Property TrackCount As Integer
        Get
            Return tracks.Count
        End Get
    End Property

    Public Function Track(index As Integer) As TrackInfo
        Return tracks(index)
    End Function

    ''' <summary>Ouvre une image à partir d'un .cue (ou d'un .ccd — l'.img de même nom est utilisé).</summary>
    Public Sub New(cuePath As String)
        Dim dir = System.IO.Path.GetDirectoryName(cuePath)
        Dim imgName As String = Nothing
        Dim ss As Integer = 2352
        Dim uo As Integer = 16
        Dim firstTr As Integer = 99
        Dim lastTr As Integer = 0

        Dim ext = System.IO.Path.GetExtension(cuePath).ToLowerInvariant()
        If ext = ".cue" Then
            Dim curMode As String = "MODE1/2352"
            For Each raw In System.IO.File.ReadAllLines(cuePath)
                Dim line = raw.Trim()
                If line.StartsWith("FILE", StringComparison.OrdinalIgnoreCase) Then
                    Dim q1 = line.IndexOf(""""c)
                    Dim q2 = line.LastIndexOf(""""c)
                    If q1 >= 0 AndAlso q2 > q1 Then imgName = line.Substring(q1 + 1, q2 - q1 - 1)
                ElseIf line.StartsWith("TRACK", StringComparison.OrdinalIgnoreCase) Then
                    Dim parts = line.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
                    Dim num = CInt(parts(1))
                    curMode = parts(2).ToUpperInvariant()
                    Dim audio = curMode.StartsWith("AUDIO")
                    tracks.Add(New TrackInfo With {.Number = num, .StartLba = 0, .IsAudio = audio})
                    firstTr = Math.Min(firstTr, num)
                    lastTr = Math.Max(lastTr, num)
                End If
            Next
            ' Taille de secteur selon le mode de la 1re piste data
            If curMode.EndsWith("2048") Then
                ss = 2048 : uo = 0
            Else
                ss = 2352
                uo = If(curMode.StartsWith("MODE1"), 16, If(curMode.StartsWith("MODE2"), 24, 0))
            End If
        Else
            ' .ccd/.img : image brute 2352, piste data unique
            imgName = System.IO.Path.GetFileNameWithoutExtension(cuePath) & ".img"
            tracks.Add(New TrackInfo With {.Number = 1, .StartLba = 0, .IsAudio = False})
            firstTr = 1 : lastTr = 1
        End If

        Dim imgPath = System.IO.Path.Combine(dir, imgName)
        If Not System.IO.File.Exists(imgPath) Then
            ' repli : .bin de même base
            imgPath = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(cuePath) & ".bin")
        End If
        img = System.IO.File.ReadAllBytes(imgPath)
        sectorSize = ss
        userOffset = uo
        TotalSectors = img.Length \ sectorSize
        FirstTrack = If(lastTr = 0, 1, firstTr)
        LastTrack = Math.Max(lastTr, 1)
        If tracks.Count = 0 Then tracks.Add(New TrackInfo With {.Number = 1, .StartLba = 0, .IsAudio = False})
    End Sub

    ''' <summary>2048 octets de données utilisateur du secteur LBA (MODE1).</summary>
    Public Function ReadUserData(lba As Integer) As Byte()
        Dim result(2047) As Byte
        If lba < 0 OrElse lba >= TotalSectors Then Return result
        System.Array.Copy(img, lba * sectorSize + userOffset, result, 0, 2048)
        Return result
    End Function

    ''' <summary>Secteur brut complet (2352 octets) — utile pour le CD-DA.</summary>
    Public Function ReadRaw(lba As Integer) As Byte()
        Dim result(sectorSize - 1) As Byte
        If lba < 0 OrElse lba >= TotalSectors Then Return result
        System.Array.Copy(img, lba * sectorSize, result, 0, sectorSize)
        Return result
    End Function

End Class
