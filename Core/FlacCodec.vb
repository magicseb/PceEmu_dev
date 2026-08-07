''' <summary>
''' Décodeur FLAC minimal pour les hunks CHD audio (codec cdfl, pistes CD-DA).
''' CHD stocke l'audio d'un hunk comme des frames FLAC brutes (l'en-tête STREAMINFO
''' est synthétique côté décodeur : 2 canaux, 16 bits, 44100 Hz). On décode donc
''' directement les frames FLAC jusqu'à produire destLen octets de PCM 16 bits stéréo
''' entrelacé en LITTLE-ENDIAN (ordre des .bin CD). Gère les sous-trames CONSTANT,
''' VERBATIM, FIXED (ordre 0-4) et LPC (ordre 1-32), le codage de Rice (partitionné,
''' avec échappement), et la décorrélation stéréo (indépendant / gauche-côté /
''' droite-côté / milieu-côté).
''' </summary>
Public NotInheritable Class FlacCodec

    Private src() As Byte
    Private bitPos As Integer      ' position en bits depuis srcStart
    Private srcStart As Integer
    Private srcBits As Integer     ' nombre total de bits disponibles

    ''' <summary>Décode le flux FLAC en PCM 16 bits stéréo LE dans dest (destLen octets).</summary>
    Public Shared Sub Decode(source() As Byte, start As Integer, len As Integer, dest() As Byte, destLen As Integer)
        Dim d As New FlacCodec()
        d.Run(source, start, len, dest, destLen)
    End Sub

    Private Sub Run(source() As Byte, start As Integer, len As Integer, dest() As Byte, destLen As Integer)
        src = source : srcStart = start : bitPos = 0 : srcBits = len * 8
        Dim outPos = 0                       ' octet courant dans dest
        Dim totalSamples = destLen \ 4       ' échantillons stéréo attendus

        Dim ch0(65535) As Integer, ch1(65535) As Integer
        Dim produced = 0
        While produced < totalSamples
            Dim blockSize = DecodeFrame(ch0, ch1)
            If blockSize <= 0 Then Exit While
            For i = 0 To blockSize - 1
                If produced >= totalSamples Then Exit For
                Dim l = ch0(i), r = ch1(i)
                ' big-endian (convention de stockage CD audio dans CHD) — CdImage swappe en LE pour l'audio
                dest(outPos) = CByte((l >> 8) And &HFF) : dest(outPos + 1) = CByte(l And &HFF)
                dest(outPos + 2) = CByte((r >> 8) And &HFF) : dest(outPos + 3) = CByte(r And &HFF)
                outPos += 4 : produced += 1
            Next
        End While
    End Sub

    ''' <summary>Décode une frame FLAC dans ch0/ch1 ; renvoie le nombre d'échantillons (block size).</summary>
    Private Function DecodeFrame(ch0() As Integer, ch1() As Integer) As Integer
        ' --- en-tête de frame ---
        Dim sync = ReadBits(14)
        If sync <> &H3FFE Then Return -1
        ReadBits(1)                          ' réservé
        Dim blockingStrategy = ReadBits(1)
        Dim bsCode = ReadBits(4)
        Dim srCode = ReadBits(4)
        Dim chAssign = ReadBits(4)
        Dim ssCode = ReadBits(3)
        ReadBits(1)                          ' réservé

        ' numéro de frame/échantillon codé « UTF-8 » (1 à 7 octets) — ignoré
        SkipCodedNumber()

        Dim blockSize As Integer
        Select Case bsCode
            Case 1 : blockSize = 192
            Case 2, 3, 4, 5 : blockSize = 576 << (bsCode - 2)
            Case 6 : blockSize = CInt(ReadBits(8)) + 1
            Case 7 : blockSize = CInt(ReadBits(16)) + 1
            Case Else : blockSize = 256 << (bsCode - 8)   ' 8..15
        End Select

        If srCode = 12 Then
            ReadBits(8)
        ElseIf srCode = 13 OrElse srCode = 14 Then
            ReadBits(16)
        End If

        Dim bps = 16
        Select Case ssCode
            Case 1 : bps = 8
            Case 2 : bps = 12
            Case 5 : bps = 20
            Case 6 : bps = 24
            Case Else : bps = 16                          ' 0 (streaminfo) ou 4
        End Select

        ReadBits(8)                          ' CRC-8 de l'en-tête (ignoré)

        ' --- canaux ---
        Dim nch = If(chAssign < 8, chAssign + 1, 2)
        Dim chans(nch - 1)() As Integer
        For c = 0 To nch - 1
            chans(c) = New Integer(blockSize - 1) {}
            ' bits supplémentaires pour le canal « côté »
            Dim extra = 0
            If (chAssign = 8 AndAlso c = 1) OrElse (chAssign = 9 AndAlso c = 0) OrElse (chAssign = 10 AndAlso c = 1) Then extra = 1
            DecodeSubframe(chans(c), blockSize, bps + extra)
        Next

        ' --- décorrélation stéréo → ch0 (L), ch1 (R) ---
        If nch = 1 Then
            Array.Copy(chans(0), ch0, blockSize)
            Array.Copy(chans(0), ch1, blockSize)
        ElseIf chAssign = 8 Then          ' gauche / côté
            For i = 0 To blockSize - 1
                Dim l = chans(0)(i) : Dim s = chans(1)(i)
                ch0(i) = l : ch1(i) = l - s
            Next
        ElseIf chAssign = 9 Then          ' côté / droite
            For i = 0 To blockSize - 1
                Dim s = chans(0)(i) : Dim r = chans(1)(i)
                ch0(i) = r + s : ch1(i) = r
            Next
        ElseIf chAssign = 10 Then         ' milieu / côté
            For i = 0 To blockSize - 1
                Dim m = chans(0)(i) : Dim s = chans(1)(i)
                Dim mm = (m << 1) Or (s And 1)
                ch0(i) = (mm + s) >> 1 : ch1(i) = (mm - s) >> 1
            Next
        Else                               ' indépendant
            Array.Copy(chans(0), ch0, blockSize)
            If nch > 1 Then Array.Copy(chans(1), ch1, blockSize) Else Array.Copy(chans(0), ch1, blockSize)
        End If

        ' aligner sur l'octet + sauter le CRC-16 de fin de frame
        AlignToByte()
        ReadBits(16)
        Return blockSize
    End Function

    Private Sub DecodeSubframe(out() As Integer, blockSize As Integer, bps As Integer)
        ReadBits(1)                          ' bit de bourrage
        Dim typ = CInt(ReadBits(6))
        ' « wasted bits » (unaire)
        Dim wasted = 0
        If ReadBits(1) = 1 Then
            wasted = 1
            While ReadBits(1) = 0
                wasted += 1
            End While
        End If
        Dim effBps = bps - wasted

        If typ = 0 Then
            ' CONSTANT
            Dim v = ReadSigned(effBps)
            For i = 0 To blockSize - 1 : out(i) = v : Next
        ElseIf typ = 1 Then
            ' VERBATIM
            For i = 0 To blockSize - 1 : out(i) = ReadSigned(effBps) : Next
        ElseIf typ >= 8 AndAlso typ <= 12 Then
            ' FIXED, ordre = typ - 8
            Dim order = typ - 8
            For i = 0 To order - 1 : out(i) = ReadSigned(effBps) : Next
            DecodeResidual(out, blockSize, order)
            RestoreFixed(out, blockSize, order)
        ElseIf typ >= 32 Then
            ' LPC, ordre = typ - 31
            Dim order = typ - 31
            For i = 0 To order - 1 : out(i) = ReadSigned(effBps) : Next
            Dim precision = CInt(ReadBits(4)) + 1
            Dim shift = ReadSigned(5)
            Dim coefs(order - 1) As Integer
            For j = 0 To order - 1 : coefs(j) = ReadSigned(precision) : Next
            DecodeResidual(out, blockSize, order)
            RestoreLpc(out, blockSize, order, coefs, shift)
        Else
            ' type réservé : on ne sait pas décoder — laisser à zéro
        End If

        If wasted > 0 Then
            For i = 0 To blockSize - 1 : out(i) = out(i) << wasted : Next
        End If
    End Sub

    Private Sub DecodeResidual(out() As Integer, blockSize As Integer, order As Integer)
        Dim method = CInt(ReadBits(2))
        Dim paramBits = If(method = 0, 4, 5)
        Dim escape = If(method = 0, &HF, &H1F)
        Dim partOrder = CInt(ReadBits(4))
        Dim partitions = 1 << partOrder
        Dim idx = order
        For p = 0 To partitions - 1
            Dim count = (blockSize >> partOrder) - If(p = 0, order, 0)
            Dim param = CInt(ReadBits(paramBits))
            If param = escape Then
                Dim rawBits = CInt(ReadBits(5))
                For k = 0 To count - 1
                    out(idx) = If(rawBits = 0, 0, ReadSigned(rawBits)) : idx += 1
                Next
            Else
                For k = 0 To count - 1
                    ' quotient unaire
                    Dim q = 0
                    While ReadBits(1) = 0
                        q += 1
                    End While
                    Dim r = CInt(ReadBits(param))
                    Dim val = (q << param) Or r
                    out(idx) = (val >> 1) Xor (-(val And 1))     ' dézigzag
                    idx += 1
                Next
            End If
        Next
    End Sub

    Private Shared Sub RestoreFixed(x() As Integer, n As Integer, order As Integer)
        Select Case order
            Case 1
                For i = 1 To n - 1 : x(i) += x(i - 1) : Next
            Case 2
                For i = 2 To n - 1 : x(i) += 2 * x(i - 1) - x(i - 2) : Next
            Case 3
                For i = 3 To n - 1 : x(i) += 3 * x(i - 1) - 3 * x(i - 2) + x(i - 3) : Next
            Case 4
                For i = 4 To n - 1 : x(i) += 4 * x(i - 1) - 6 * x(i - 2) + 4 * x(i - 3) - x(i - 4) : Next
        End Select
    End Sub

    Private Shared Sub RestoreLpc(x() As Integer, n As Integer, order As Integer, coefs() As Integer, shift As Integer)
        For i = order To n - 1
            Dim acc As Long = 0
            For j = 0 To order - 1
                acc += CLng(coefs(j)) * x(i - 1 - j)
            Next
            x(i) += CInt(acc >> shift)
        Next
    End Sub

    ' ---------- lecture de bits (MSB d'abord) ----------

    Private Function ReadBits(n As Integer) As UInteger
        Dim result As UInteger = 0
        For k = 0 To n - 1
            Dim bytePos = srcStart + (bitPos >> 3)
            Dim bit = (src(bytePos) >> (7 - (bitPos And 7))) And 1
            result = (result << 1) Or CUInt(bit)
            bitPos += 1
        Next
        Return result
    End Function

    Private Function ReadSigned(n As Integer) As Integer
        If n = 0 Then Return 0
        Dim v = CInt(ReadBits(n))
        If (v And (1 << (n - 1))) <> 0 Then v -= (1 << n)
        Return v
    End Function

    Private Sub AlignToByte()
        If (bitPos And 7) <> 0 Then bitPos = (bitPos + 7) And Not 7
    End Sub

    Private Sub SkipCodedNumber()
        ' premier octet « UTF-8 » : le nombre de bits de poids fort à 1 donne la longueur
        Dim b = CInt(ReadBits(8))
        Dim extra = 0
        If (b And &H80) = 0 Then
            extra = 0
        ElseIf (b And &HE0) = &HC0 Then
            extra = 1
        ElseIf (b And &HF0) = &HE0 Then
            extra = 2
        ElseIf (b And &HF8) = &HF0 Then
            extra = 3
        ElseIf (b And &HFC) = &HF8 Then
            extra = 4
        ElseIf (b And &HFE) = &HFC Then
            extra = 5
        ElseIf (b And &HFF) = &HFE Then
            extra = 6
        End If
        For i = 1 To extra : ReadBits(8) : Next
    End Sub

End Class
