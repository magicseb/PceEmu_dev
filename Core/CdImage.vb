''' <summary>
''' Lecteur d'image CD pour le CD-ROM². Gère :
'''  - une image mono-fichier (.cue/.ccd + un seul .img/.bin), et
'''  - un .cue MULTI-FICHIERS (un .bin par piste, data + pistes audio CD-DA).
'''
''' Les secteurs sont lus À LA DEMANDE via des flux (les pistes audio peuvent peser
''' des centaines de Mo — on ne charge rien en mémoire d'un coup). Les LBA sont
''' cumulés sur l'ensemble des fichiers, pregaps (INDEX 00/01) compris.
''' </summary>
Public Class CdImage

    Private Structure Entry
        Public FilePath As String
        Public SectorSize As Integer
        Public UserOffset As Integer     ' décalage des données utilisateur dans le secteur brut
        Public IsAudio As Boolean
        Public Number As Integer
        Public FileStartLba As Integer   ' LBA absolu du secteur 0 du FICHIER (base pour l'offset)
        Public FileSectors As Integer
        Public TrackStartLba As Integer  ' LBA absolu de l'INDEX 01 (début « officiel » de la piste)
        Public RangeStart As Integer     ' plage LBA couverte par cette piste (pour la recherche)
        Public RangeEnd As Integer
        Public ChdPhysBase As Integer    ' frame physique CHD du début de fichier (images .chd)
    End Structure

    Private ReadOnly entries As New System.Collections.Generic.List(Of Entry)
    Private ReadOnly streams As New System.Collections.Generic.Dictionary(Of String, System.IO.FileStream)
    Private chd As ChdReader     ' non-Nothing pour une image .chd

    Public ReadOnly Property TotalSectors As Integer
    Public ReadOnly Property FirstTrack As Integer
    Public ReadOnly Property LastTrack As Integer

    Public Structure TrackInfo
        Public Number As Integer
        Public StartLba As Integer
        Public IsAudio As Boolean
    End Structure

    Public ReadOnly Property LeadOutLba As Integer
        Get
            Return TotalSectors
        End Get
    End Property

    Public ReadOnly Property TrackCount As Integer
        Get
            Return entries.Count
        End Get
    End Property

    Public Function Track(index As Integer) As TrackInfo
        Dim e = entries(index)
        Return New TrackInfo With {.Number = e.Number, .StartLba = e.TrackStartLba, .IsAudio = e.IsAudio}
    End Function

    Public Sub New(path As String)
        Dim dir = System.IO.Path.GetDirectoryName(path)
        Dim ext = System.IO.Path.GetExtension(path).ToLowerInvariant()
        Dim curLba = 0

        If ext = ".chd" Then
            ' --- image CHD compressée (MAME) : pistes issues des métadonnées CD ---
            curLba = LoadChd(path)
        ElseIf ext = ".cue" Then
            ' --- Parse d'un .cue : mono-fichier multi-pistes (un .img, INDEX absolus)
            '     OU multi-fichiers (un .bin par piste, LBA cumulés). Une entrée PAR PISTE. ---
            Dim curFile As String = Nothing
            Dim curFileBase = 0
            Dim curNum = 0, curMode = "MODE1/2352", curIsAudio = False, curIndex01 = 0
            Dim pending = False

            For Each raw In System.IO.File.ReadAllLines(path)
                Dim line = raw.Trim()
                If line.StartsWith("FILE", StringComparison.OrdinalIgnoreCase) Then
                    If pending Then AddTrack(curFile, dir, curNum, curMode, curIsAudio, curIndex01, curFileBase) : pending = False
                    Dim q1 = line.IndexOf(""""c), q2 = line.LastIndexOf(""""c)
                    curFile = If(q1 >= 0 AndAlso q2 > q1, line.Substring(q1 + 1, q2 - q1 - 1), Nothing)
                    ' base LBA de ce fichier = LBA cumulé courant ; on avance ensuite de sa taille
                    curFileBase = curLba
                    curLba = curFileBase + FileSectorsOf(curFile, dir)
                    curIndex01 = 0
                ElseIf line.StartsWith("TRACK", StringComparison.OrdinalIgnoreCase) Then
                    If pending Then AddTrack(curFile, dir, curNum, curMode, curIsAudio, curIndex01, curFileBase) : pending = False
                    Dim parts = line.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
                    curNum = CInt(parts(1))
                    curMode = parts(2).ToUpperInvariant()
                    curIsAudio = curMode.StartsWith("AUDIO")
                    curIndex01 = 0
                    pending = True
                ElseIf line.StartsWith("INDEX 01", StringComparison.OrdinalIgnoreCase) Then
                    curIndex01 = MsfToSectors(line)
                End If
            Next
            If pending Then AddTrack(curFile, dir, curNum, curMode, curIsAudio, curIndex01, curFileBase)
        Else
            ' --- .ccd/.img : image brute mono-fichier, piste data unique ---
            Dim imgName = System.IO.Path.GetFileNameWithoutExtension(path) & ".img"
            AddTrack(imgName, dir, 1, "MODE1/2352", False, 0, 0)
            If entries.Count > 0 Then curLba = entries(0).FileSectors
        End If

        TotalSectors = curLba
        If TotalSectors = 0 AndAlso entries.Count > 0 Then
            TotalSectors = entries(entries.Count - 1).FileStartLba + entries(entries.Count - 1).FileSectors
        End If

        ' Trier les pistes par LBA et calculer les plages contiguës (la 1re couvre depuis 0).
        entries.Sort(Function(a, b) a.TrackStartLba.CompareTo(b.TrackStartLba))
        For i = 0 To entries.Count - 1
            Dim e = entries(i)
            e.RangeStart = If(i = 0, 0, e.TrackStartLba)
            e.RangeEnd = If(i < entries.Count - 1, entries(i + 1).TrackStartLba, TotalSectors)
            entries(i) = e
        Next

        Dim first = Integer.MaxValue, last = 0
        For Each e In entries
            first = Math.Min(first, e.Number) : last = Math.Max(last, e.Number)
        Next
        FirstTrack = If(entries.Count = 0, 1, first)
        LastTrack = Math.Max(last, 1)
    End Sub

    ''' <summary>Nombre de secteurs bruts (2352 o) d'un fichier image, 0 s'il est absent.</summary>
    Private Shared Function FileSectorsOf(fileName As String, dir As String) As Integer
        Dim p = ResolveFile(fileName, dir)
        If p IsNot Nothing AndAlso System.IO.File.Exists(p) Then Return CInt(New System.IO.FileInfo(p).Length \ 2352)
        Return 0
    End Function

    ''' <summary>Ajoute une piste (entrée). N'avance PAS le LBA (géré à la ligne FILE).</summary>
    Private Sub AddTrack(fileName As String, dir As String, num As Integer, mode As String,
                         isAudio As Boolean, index01 As Integer, fileBaseLba As Integer)
        Dim fullPath = ResolveFile(fileName, dir)
        Dim ss = 2352, uo = 0
        If mode.EndsWith("2048") Then
            ss = 2048 : uo = 0
        ElseIf mode.StartsWith("MODE1") Then
            ss = 2352 : uo = 16
        ElseIf mode.StartsWith("MODE2") Then
            ss = 2352 : uo = 24
        Else
            ss = 2352 : uo = 0        ' AUDIO
        End If
        Dim sectors = 0
        If fullPath IsNot Nothing AndAlso System.IO.File.Exists(fullPath) Then
            sectors = CInt(New System.IO.FileInfo(fullPath).Length \ ss)
        End If
        entries.Add(New Entry With {
            .FilePath = fullPath, .SectorSize = ss, .UserOffset = uo, .IsAudio = isAudio,
            .Number = num, .FileStartLba = fileBaseLba, .FileSectors = sectors,
            .TrackStartLba = fileBaseLba + index01})
    End Sub

    ''' <summary>Construit les entrées de pistes depuis une image CHD. Renvoie le lead-out (LBA total).</summary>
    Private Function LoadChd(path As String) As Integer
        chd = New ChdReader(path)
        For Each t In chd.Tracks
            Dim uo = 0
            If Not t.IsAudio Then
                If t.Type.StartsWith("MODE2") Then uo = 24 Else uo = 16   ' MODE1/MODE1_RAW → 16
            End If
            entries.Add(New Entry With {
                .FilePath = Nothing, .SectorSize = 2352, .UserOffset = uo, .IsAudio = t.IsAudio,
                .Number = t.Number, .FileStartLba = t.StartLba, .FileSectors = t.Frames,
                .TrackStartLba = t.StartLba + t.Pregap, .ChdPhysBase = t.PhysFrame})
        Next
        If chd.Tracks.Count = 0 Then Return 0
        Dim last = chd.Tracks(chd.Tracks.Count - 1)
        Return last.StartLba + last.Frames
    End Function

    ''' <summary>Piste dont les frames stockées contiennent le LBA (base fichier), pour une image .chd.</summary>
    Private Function ChdEntryForLba(absLba As Integer) As Integer
        For i = 0 To entries.Count - 1
            Dim e = entries(i)
            If absLba >= e.FileStartLba AndAlso absLba < e.FileStartLba + e.FileSectors Then Return i
        Next
        Return -1
    End Function

    Private Shared Function ResolveFile(fileName As String, dir As String) As String
        If fileName Is Nothing Then Return Nothing
        Dim p = System.IO.Path.Combine(dir, fileName)
        If System.IO.File.Exists(p) Then Return p
        ' repli : même nom de base avec extension .bin/.img
        Dim baseName = System.IO.Path.GetFileNameWithoutExtension(fileName)
        For Each altExt In New String() {".bin", ".img"}
            Dim alt = System.IO.Path.Combine(dir, baseName & altExt)
            If System.IO.File.Exists(alt) Then Return alt
        Next
        Return p
    End Function

    Private Shared Function MsfToSectors(indexLine As String) As Integer
        ' "INDEX 01 mm:ss:ff"
        Dim parts = indexLine.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
        Dim msf = parts(parts.Length - 1).Split(":"c)
        If msf.Length <> 3 Then Return 0
        Return (CInt(msf(0)) * 60 + CInt(msf(1))) * 75 + CInt(msf(2))
    End Function

    ''' <summary>Trouve la piste/fichier contenant le LBA absolu.</summary>
    Private Function EntryForLba(absLba As Integer) As Integer
        For i = 0 To entries.Count - 1
            Dim e = entries(i)
            If absLba >= e.RangeStart AndAlso absLba < e.RangeEnd Then Return i
        Next
        Return -1
    End Function

    Private Function GetStream(path As String) As System.IO.FileStream
        Dim fs As System.IO.FileStream = Nothing
        If Not streams.TryGetValue(path, fs) Then
            fs = New System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read)
            streams(path) = fs
        End If
        Return fs
    End Function

    ''' <summary>2048 octets de données utilisateur du secteur LBA absolu.</summary>
    Public Function ReadUserData(absLba As Integer) As Byte()
        Dim result(2047) As Byte
        If chd IsNot Nothing Then
            Dim ci = ChdEntryForLba(absLba)
            If ci < 0 Then Return result
            Dim ce = entries(ci)
            Dim sec = chd.ReadPhysFrame(ce.ChdPhysBase + (absLba - ce.FileStartLba))
            Dim nUser = Math.Min(2048, 2352 - ce.UserOffset)
            Array.Copy(sec, ce.UserOffset, result, 0, nUser)
            Return result
        End If
        Dim i = EntryForLba(absLba)
        If i < 0 Then Return result
        Dim e = entries(i)
        If e.FilePath Is Nothing OrElse Not System.IO.File.Exists(e.FilePath) Then Return result
        Dim pos As Long = CLng(absLba - e.FileStartLba) * e.SectorSize + e.UserOffset
        Dim fs = GetStream(e.FilePath)
        fs.Seek(pos, System.IO.SeekOrigin.Begin)
        Dim toRead = Math.Min(2048, e.SectorSize - e.UserOffset)
        Dim off = 0
        While off < toRead
            Dim n = fs.Read(result, off, toRead - off)
            If n <= 0 Then Exit While
            off += n
        End While
        Return result
    End Function

    ''' <summary>Secteur audio brut (2352 octets) — pour le CD-DA (à venir).</summary>
    Public Function ReadRaw(absLba As Integer) As Byte()
        If chd IsNot Nothing Then
            Dim ci = ChdEntryForLba(absLba)
            If ci < 0 Then Return New Byte(2351) {}
            Dim ce = entries(ci)
            Dim sec = chd.ReadPhysFrame(ce.ChdPhysBase + (absLba - ce.FileStartLba))
            ' l'audio CD est stocké en big-endian dans le CHD → repasser en little-endian (comme un .bin)
            If ce.IsAudio Then
                For j = 0 To sec.Length - 2 Step 2
                    Dim t = sec(j) : sec(j) = sec(j + 1) : sec(j + 1) = t
                Next
            End If
            Return sec
        End If
        Dim i = EntryForLba(absLba)
        If i < 0 Then Return New Byte(2351) {}
        Dim e = entries(i)
        Dim result(e.SectorSize - 1) As Byte
        If e.FilePath Is Nothing OrElse Not System.IO.File.Exists(e.FilePath) Then Return result
        Dim fs = GetStream(e.FilePath)
        fs.Seek(CLng(absLba - e.FileStartLba) * e.SectorSize, System.IO.SeekOrigin.Begin)
        Dim off = 0
        While off < e.SectorSize
            Dim n = fs.Read(result, off, e.SectorSize - off)
            If n <= 0 Then Exit While
            off += n
        End While
        Return result
    End Function

End Class
