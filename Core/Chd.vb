Imports System.IO

''' <summary>
''' Lecteur d'images CHD (Compressed Hunks of Data, format compressé de MAME) pour
''' les CD-ROM². Portage géré (VB) de la partie lecture de libchdr : en-tête v5,
''' map v5 compressée (Huffman + bitstream), framing des codecs CD, et décodage des
''' hunks (zlib / LZMA / FLAC). L'émulateur ne lit que les données utilisateur des
''' secteurs data et l'audio brut du CD-DA : on n'a donc PAS besoin de régénérer le
''' sync/ECC ni de décoder le subcode. Un hunk CD = 8 frames de 2448 o (2352 données
''' secteur + 96 subcode) ; on ne reconstruit que les 2352 o de données par frame.
''' </summary>
Public Class ChdReader
    Implements IDisposable

    ' --- codecs (FourCC big-endian) ---
    Private Const CODEC_NONE As UInteger = 0
    Private Const CODEC_ZLIB As UInteger = &H7A6C6962UI    ' 'zlib'
    Private Const CODEC_LZMA As UInteger = &H6C7A6D61UI    ' 'lzma'
    Private Const CODEC_FLAC As UInteger = &H666C6163UI    ' 'flac'
    Private Const CODEC_CD_ZLIB As UInteger = &H63647A6CUI ' 'cdzl'
    Private Const CODEC_CD_LZMA As UInteger = &H63646C7AUI ' 'cdlz'
    Private Const CODEC_CD_FLAC As UInteger = &H6364666CUI ' 'cdfl'

    ' --- types de compression de la map v5 ---
    Private Const COMP_TYPE_0 As Byte = 0
    Private Const COMP_TYPE_3 As Byte = 3
    Private Const COMP_NONE As Byte = 4
    Private Const COMP_SELF As Byte = 5
    Private Const COMP_PARENT As Byte = 6
    Private Const COMP_RLE_SMALL As Byte = 7
    Private Const COMP_RLE_LARGE As Byte = 8
    Private Const COMP_SELF_0 As Byte = 9
    Private Const COMP_SELF_1 As Byte = 10
    Private Const COMP_PARENT_SELF As Byte = 11
    Private Const COMP_PARENT_0 As Byte = 12
    Private Const COMP_PARENT_1 As Byte = 13

    Private Const CD_MAX_SECTOR_DATA As Integer = 2352
    Private Const CD_MAX_SUBCODE_DATA As Integer = 96
    Private Const CD_FRAME_SIZE As Integer = CD_MAX_SECTOR_DATA + CD_MAX_SUBCODE_DATA  ' 2448

    Private ReadOnly fs As FileStream
    Private ReadOnly compression(3) As UInteger
    Private ReadOnly hunkBytes As Integer
    Private ReadOnly unitBytes As Integer
    Private ReadOnly hunkCount As Integer
    Private ReadOnly logicalBytes As Long
    Private ReadOnly metaOffset As Long
    Private ReadOnly framesPerHunk As Integer

    ' map décodée : un tableau par champ, indexé par numéro de hunk
    Private ReadOnly mapType() As Byte
    Private ReadOnly mapLength() As Integer
    Private ReadOnly mapOffset() As Long

    ' cache d'un hunk décodé
    Private cachedHunk As Integer = -1
    Private ReadOnly cachedData() As Byte

    Public ReadOnly Property TotalFrames As Integer
        Get
            Return CInt(logicalBytes \ CD_FRAME_SIZE)
        End Get
    End Property

    ''' <summary>Une piste telle que décrite par les métadonnées CD du CHD.</summary>
    Public Structure ChdTrack
        Public Number As Integer
        Public Type As String        ' MODE1, MODE1_RAW, MODE2, AUDIO…
        Public Frames As Integer     ' secteurs de données de la piste
        Public Pregap As Integer
        Public IsAudio As Boolean
        Public PhysFrame As Integer  ' 1er frame de la piste dans le CHD (physique, padding compris)
        Public StartLba As Integer   ' LBA logique du début de piste (vu par l'émulateur)
    End Structure

    Private ReadOnly _tracks As New System.Collections.Generic.List(Of ChdTrack)
    Public ReadOnly Property Tracks As System.Collections.Generic.List(Of ChdTrack)
        Get
            Return _tracks
        End Get
    End Property

    Public Sub New(path As String)
        fs = New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)

        Dim hdr(123) As Byte
        ReadAt(0, hdr, 124)
        ' tag 'MComprHD'
        Dim tag = System.Text.Encoding.ASCII.GetString(hdr, 0, 8)
        If tag <> "MComprHD" Then Throw New InvalidDataException("CHD : signature absente")
        Dim version = BE32(hdr, 12)
        If version <> 5 Then Throw New InvalidDataException("CHD : seule la version 5 est prise en charge (trouvé v" & version & ")")

        compression(0) = BE32(hdr, 16)
        compression(1) = BE32(hdr, 20)
        compression(2) = BE32(hdr, 24)
        compression(3) = BE32(hdr, 28)
        logicalBytes = BE64(hdr, 32)
        Dim mapOff = BE64(hdr, 40)
        metaOffset = BE64(hdr, 48)
        hunkBytes = CInt(BE32(hdr, 56))
        unitBytes = CInt(BE32(hdr, 60))
        hunkCount = CInt((logicalBytes + hunkBytes - 1) \ hunkBytes)
        framesPerHunk = hunkBytes \ CD_FRAME_SIZE

        ReDim mapType(hunkCount - 1)
        ReDim mapLength(hunkCount - 1)
        ReDim mapOffset(hunkCount - 1)
        ReDim cachedData(hunkBytes - 1)

        DecodeMapV5(mapOff)
        ParseMetadata()
    End Sub

    ' ===================== Map v5 =====================

    Private Sub DecodeMapV5(mapFileOffset As Long)
        Dim mh(15) As Byte
        ReadAt(mapFileOffset, mh, 16)
        Dim mapBytes = CInt(BE32(mh, 0))
        Dim firstOffs = BE48(mh, 4)
        Dim lengthBits = mh(12)
        Dim selfBits = mh(13)
        Dim parentBits = mh(14)

        Dim comp(mapBytes - 1) As Byte
        ReadAt(mapFileOffset + 16, comp, mapBytes)
        Dim bs As New BitReader(comp)

        ' arbre Huffman (16 codes, 8 bits) importé en RLE
        Dim huff As New HuffmanDecoder(16, 8)
        huff.ImportTreeRle(bs)

        ' 1) types de compression, avec runs RLE
        Dim lastComp As Byte = 0
        Dim repcount As Integer = 0
        For hunk = 0 To hunkCount - 1
            If repcount > 0 Then
                mapType(hunk) = lastComp : repcount -= 1
            Else
                Dim val As Byte = CByte(huff.DecodeOne(bs))
                If val = COMP_RLE_SMALL Then
                    mapType(hunk) = lastComp : repcount = 2 + CInt(huff.DecodeOne(bs))
                ElseIf val = COMP_RLE_LARGE Then
                    mapType(hunk) = lastComp
                    repcount = 2 + 16 + (CInt(huff.DecodeOne(bs)) << 4)
                    repcount += CInt(huff.DecodeOne(bs))
                Else
                    mapType(hunk) = val : lastComp = val
                End If
            End If
        Next

        ' 2) longueurs / offsets
        Dim curOffset As Long = firstOffs
        Dim lastSelf As Long = 0
        Dim lastParent As Long = 0
        For hunk = 0 To hunkCount - 1
            Dim offset As Long = curOffset
            Dim length As Integer = 0
            Select Case mapType(hunk)
                Case COMP_TYPE_0, 1, 2, COMP_TYPE_3
                    length = CInt(bs.Read(lengthBits))
                    curOffset += length
                    bs.Read(16)   ' crc, ignoré
                Case COMP_NONE
                    length = hunkBytes
                    curOffset += length
                    bs.Read(16)
                Case COMP_SELF
                    offset = bs.Read(selfBits) : lastSelf = offset
                Case COMP_PARENT
                    offset = bs.Read(parentBits) : lastParent = offset
                Case COMP_SELF_1
                    lastSelf += 1
                    mapType(hunk) = COMP_SELF : offset = lastSelf
                Case COMP_SELF_0
                    mapType(hunk) = COMP_SELF : offset = lastSelf
                Case COMP_PARENT_SELF
                    mapType(hunk) = COMP_PARENT
                    lastParent = (CLng(hunk) * hunkBytes) \ unitBytes : offset = lastParent
                Case COMP_PARENT_1
                    lastParent += hunkBytes \ unitBytes
                    mapType(hunk) = COMP_PARENT : offset = lastParent
                Case COMP_PARENT_0
                    mapType(hunk) = COMP_PARENT : offset = lastParent
            End Select
            mapLength(hunk) = length
            mapOffset(hunk) = offset
        Next
    End Sub

    ' ===================== Décodage d'un hunk =====================

    ''' <summary>Décode le hunk demandé (avec cache du dernier) et renvoie son contenu
    ''' réassemblé (2448 o/frame ; données secteur remplies, subcode laissé à zéro).</summary>
    Private Function DecodeHunk(hunk As Integer) As Byte()
        If hunk = cachedHunk Then Return cachedData
        DecodeHunkInto(hunk, cachedData)
        cachedHunk = hunk
        Return cachedData
    End Function

    Private Sub DecodeHunkInto(hunk As Integer, dest() As Byte)
        Dim t = mapType(hunk)
        Select Case t
            Case COMP_TYPE_0, 1, 2, COMP_TYPE_3
                Dim src(mapLength(hunk) - 1) As Byte
                ReadAt(mapOffset(hunk), src, mapLength(hunk))
                CdDecompress(compression(t), src, dest)
            Case COMP_NONE
                ReadAt(mapOffset(hunk), dest, hunkBytes)
            Case COMP_SELF
                DecodeHunkInto(CInt(mapOffset(hunk)), dest)
            Case Else
                Throw New InvalidDataException("CHD : type de hunk non pris en charge (" & t & ")")
        End Select
    End Sub

    ''' <summary>Framing des codecs CD : en-tête (ecc_bytes + complen_bytes), flux « base »
    ''' (données secteur), subcode ignoré, réassemblage 2352 o/frame dans dest.</summary>
    Private Sub CdDecompress(codec As UInteger, src() As Byte, dest() As Byte)
        Dim frames = hunkBytes \ CD_FRAME_SIZE
        Dim sectorBuf(frames * CD_MAX_SECTOR_DATA - 1) As Byte

        If codec = CODEC_CD_FLAC OrElse codec = CODEC_FLAC Then
            ' cdfl : le flux FLAC démarre à l'offset 0 (pas d'en-tête ecc/complen) ;
            ' le décodeur s'arrête après frames×2352 octets, le subcode qui suit est ignoré.
            FlacCodec.Decode(src, 0, src.Length, sectorBuf, frames * CD_MAX_SECTOR_DATA)
        Else
            Dim complenBytes = If(hunkBytes < 65536, 2, 3)
            Dim eccBytes = (frames + 7) \ 8
            Dim headerBytes = eccBytes + complenBytes
            Dim complenBase As Integer = (CInt(src(eccBytes)) << 8) Or src(eccBytes + 1)
            If complenBytes > 2 Then complenBase = (complenBase << 8) Or src(eccBytes + 2)
            BaseDecompress(codec, src, headerBytes, complenBase, sectorBuf, frames * CD_MAX_SECTOR_DATA)
        End If

        ' réassemblage : données secteur → dest[frame*2448 .. +2352] (subcode laissé à 0)
        For f = 0 To frames - 1
            Array.Copy(sectorBuf, f * CD_MAX_SECTOR_DATA, dest, f * CD_FRAME_SIZE, CD_MAX_SECTOR_DATA)
        Next
    End Sub

    ''' <summary>Décode le flux « base » d'un hunk CD avec le codec voulu.</summary>
    Private Sub BaseDecompress(codec As UInteger, src() As Byte, srcStart As Integer, srcLen As Integer,
                               dest() As Byte, destLen As Integer)
        Select Case codec
            Case CODEC_CD_ZLIB, CODEC_ZLIB
                InflateRaw(src, srcStart, srcLen, dest, destLen)
            Case CODEC_CD_LZMA, CODEC_LZMA
                LzmaCodec.Decode(src, srcStart, srcLen, dest, destLen, hunkBytes)
            Case CODEC_CD_FLAC, CODEC_FLAC
                FlacCodec.Decode(src, srcStart, srcLen, dest, destLen)
            Case Else
                Throw New InvalidDataException("CHD : codec base non pris en charge")
        End Select
    End Sub

    ''' <summary>Deflate brut (sans en-tête zlib), comme le codec zlib de CHD.</summary>
    Private Shared Sub InflateRaw(src() As Byte, start As Integer, len As Integer, dest() As Byte, destLen As Integer)
        Using ms As New MemoryStream(src, start, len),
              ds As New System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress)
            Dim off = 0
            While off < destLen
                Dim n = ds.Read(dest, off, destLen - off)
                If n <= 0 Then Exit While
                off += n
            End While
        End Using
    End Sub

    ' ===================== Lecture d'un secteur =====================

    ''' <summary>2352 octets de données secteur du frame CHD PHYSIQUE demandé.</summary>
    Public Function ReadPhysFrame(physFrame As Integer) As Byte()
        Dim result(CD_MAX_SECTOR_DATA - 1) As Byte
        If physFrame < 0 OrElse physFrame >= TotalFrames Then Return result
        Dim hunk = physFrame \ framesPerHunk
        Dim frameInHunk = physFrame Mod framesPerHunk
        Dim data = DecodeHunk(hunk)
        Array.Copy(data, frameInHunk * CD_FRAME_SIZE, result, 0, CD_MAX_SECTOR_DATA)
        Return result
    End Function

    ' ===================== Métadonnées CD =====================

    Private Sub ParseMetadata()
        Dim off = metaOffset
        Dim physFrame = 0
        Dim startLba = 0
        Const PADDING = 4
        While off <> 0
            Dim mh(15) As Byte
            ReadAt(off, mh, 16)
            Dim metaTag = BE32(mh, 0)
            Dim metaFlagsLen = BE32(mh, 4)
            Dim metaLen = CInt(metaFlagsLen And &HFFFFFFUI)
            Dim metaNext = BE64(mh, 8)

            If metaTag = &H43485432UI OrElse metaTag = &H43485452UI Then  ' 'CHT2' / 'CHTR'
                Dim body(metaLen - 1) As Byte
                ReadAt(off + 16, body, metaLen)
                Dim text = System.Text.Encoding.ASCII.GetString(body).TrimEnd(ChrW(0))
                Dim tr = ParseTrackLine(text)
                tr.PhysFrame = physFrame
                tr.StartLba = startLba
                _tracks.Add(tr)
                ' avancer : physique padé à 4 ; logique = frames (+ pregap éventuel)
                Dim padded = ((tr.Frames + PADDING - 1) \ PADDING) * PADDING
                physFrame += padded
                startLba += tr.Frames
            End If
            off = metaNext
        End While
    End Sub

    Private Shared Function ParseTrackLine(text As String) As ChdTrack
        Dim tr As New ChdTrack With {.Number = 1, .Type = "MODE1", .Frames = 0, .Pregap = 0}
        For Each tok In text.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            Dim kv = tok.Split(":"c)
            If kv.Length <> 2 Then Continue For
            Select Case kv(0)
                Case "TRACK" : Integer.TryParse(kv(1), tr.Number)
                Case "TYPE" : tr.Type = kv(1)
                Case "FRAMES" : Integer.TryParse(kv(1), tr.Frames)
                Case "PREGAP" : Integer.TryParse(kv(1), tr.Pregap)
            End Select
        Next
        tr.IsAudio = tr.Type.StartsWith("AUDIO")
        Return tr
    End Function

    ' ===================== E/S et helpers =====================

    Private Sub ReadAt(pos As Long, buf() As Byte, count As Integer)
        fs.Seek(pos, SeekOrigin.Begin)
        Dim off = 0
        While off < count
            Dim n = fs.Read(buf, off, count - off)
            If n <= 0 Then Exit While
            off += n
        End While
    End Sub

    Private Shared Function BE32(b() As Byte, o As Integer) As UInteger
        Return (CUInt(b(o)) << 24) Or (CUInt(b(o + 1)) << 16) Or (CUInt(b(o + 2)) << 8) Or b(o + 3)
    End Function
    Private Shared Function BE48(b() As Byte, o As Integer) As Long
        Dim v As Long = 0
        For i = 0 To 5 : v = (v << 8) Or b(o + i) : Next
        Return v
    End Function
    Private Shared Function BE64(b() As Byte, o As Integer) As Long
        Dim v As Long = 0
        For i = 0 To 7 : v = (v << 8) Or b(o + i) : Next
        Return v
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        fs?.Dispose()
    End Sub

    ' ===================== Bitstream (comme libchdr) =====================

    Private NotInheritable Class BitReader
        Private ReadOnly data() As Byte
        Private buffer As UInteger = 0
        Private bits As Integer = 0
        Private doffset As Integer = 0
        Private ReadOnly dlength As Integer

        Public Sub New(src() As Byte)
            data = src : dlength = src.Length
        End Sub

        Public Function Peek(numbits As Integer) As UInteger
            If numbits = 0 Then Return 0
            If numbits > bits Then
                While bits <= 24
                    If doffset < dlength Then buffer = buffer Or (CUInt(data(doffset)) << (24 - bits))
                    doffset += 1
                    bits += 8
                End While
            End If
            Return buffer >> (32 - numbits)
        End Function

        Public Sub Remove(numbits As Integer)
            buffer <<= numbits
            bits -= numbits
        End Sub

        Public Function Read(numbits As Integer) As UInteger
            Dim r = Peek(numbits)
            Remove(numbits)
            Return r
        End Function
    End Class

    ' ===================== Huffman (comme libchdr) =====================

    Private NotInheritable Class HuffmanDecoder
        Private ReadOnly numCodes As Integer
        Private ReadOnly maxBits As Integer
        Private ReadOnly lookup() As UShort
        Private ReadOnly nodeBits() As Byte
        Private ReadOnly nodeCode() As UInteger

        Public Sub New(codes As Integer, mbits As Integer)
            numCodes = codes : maxBits = mbits
            ReDim lookup((1 << maxBits) - 1)
            ReDim nodeBits(numCodes - 1)
            ReDim nodeCode(numCodes - 1)
        End Sub

        Public Function DecodeOne(bs As BitReader) As UInteger
            Dim b = bs.Peek(maxBits)
            Dim lv = lookup(CInt(b))
            bs.Remove(lv And &H1F)
            Return CUInt(lv) >> 5
        End Function

        Public Sub ImportTreeRle(bs As BitReader)
            Dim nb As Integer = If(maxBits >= 16, 5, If(maxBits >= 8, 4, 3))
            Dim curnode = 0
            While curnode < numCodes
                Dim v = CInt(bs.Read(nb))
                If v <> 1 Then
                    nodeBits(curnode) = CByte(v) : curnode += 1
                Else
                    v = CInt(bs.Read(nb))
                    If v = 1 Then
                        nodeBits(curnode) = 1 : curnode += 1
                    Else
                        Dim rep = CInt(bs.Read(nb)) + 3
                        While rep > 0 AndAlso curnode < numCodes
                            nodeBits(curnode) = CByte(v) : curnode += 1 : rep -= 1
                        End While
                    End If
                End If
            End While
            AssignCanonical()
            BuildLookup()
        End Sub

        Private Sub AssignCanonical()
            Dim histo(32) As UInteger
            For c = 0 To numCodes - 1
                If nodeBits(c) <= 32 Then histo(nodeBits(c)) += 1UI
            Next
            Dim curstart As UInteger = 0
            For codelen = 32 To 1 Step -1
                Dim nextstart = (curstart + histo(codelen)) >> 1
                histo(codelen) = curstart
                curstart = nextstart
            Next
            For c = 0 To numCodes - 1
                If nodeBits(c) > 0 Then
                    nodeCode(c) = histo(nodeBits(c))
                    histo(nodeBits(c)) += 1UI
                End If
            Next
        End Sub

        Private Sub BuildLookup()
            For c = 0 To numCodes - 1
                If nodeBits(c) > 0 Then
                    Dim value As UShort = CUShort((CUInt(c) << 5) Or CUInt(nodeBits(c)))
                    Dim shift = maxBits - nodeBits(c)
                    Dim first = CInt(nodeCode(c) << shift)
                    Dim last = CInt(((nodeCode(c) + 1UI) << shift) - 1UI)
                    For i = first To last
                        lookup(i) = value
                    Next
                End If
            Next
        End Sub
    End Class

End Class
