''' <summary>
''' Décodeur LZMA « brut » (sans en-tête) pour les hunks CHD compressés en cdlz/lzma.
''' Portage en VB du décodeur de référence LZMA (LzmaSpec du SDK LZMA, domaine public).
''' CHD utilise des propriétés fixes lc=3, lp=0, pb=2 ; le flux ne porte ni en-tête ni
''' taille : la taille de sortie est connue (destLen) et la sortie sert elle-même de
''' dictionnaire (les distances restent dans le hunk). Repose sur l'arithmétique 32 bits
''' non signée qui « wrap » (RemoveIntegerChecks du projet).
''' </summary>
Public NotInheritable Class LzmaCodec

    Private Const kNumBitModelTotalBits As Integer = 11
    Private Const kBitModelTotal As UInteger = 1UI << kNumBitModelTotalBits
    Private Const kNumMoveBits As Integer = 5
    Private Const PROB_INIT As UShort = CUShort(kBitModelTotal \ 2UI)
    Private Const kTopValue As UInteger = 1UI << 24

    Private Const kNumPosBitsMax As Integer = 4
    Private Const kNumStates As Integer = 12
    Private Const kNumLenToPosStates As Integer = 4
    Private Const kNumAlignBits As Integer = 4
    Private Const kEndPosModelIndex As Integer = 14
    Private Const kNumFullDistances As Integer = 1 << (kEndPosModelIndex \ 2)  ' 128
    Private Const kMatchMinLen As Integer = 2

    Private src() As Byte
    Private srcPos As Integer
    Private srcEnd As Integer
    Private Range As UInteger
    Private Code As UInteger

    Private outBuf() As Byte
    Private outPos As Integer

    Private lc As Integer, lp As Integer, pb As Integer

    Private litProbs() As UShort
    Private ReadOnly IsMatch((kNumStates << kNumPosBitsMax) - 1) As UShort
    Private ReadOnly IsRep(kNumStates - 1) As UShort
    Private ReadOnly IsRepG0(kNumStates - 1) As UShort
    Private ReadOnly IsRepG1(kNumStates - 1) As UShort
    Private ReadOnly IsRepG2(kNumStates - 1) As UShort
    Private ReadOnly IsRep0Long((kNumStates << kNumPosBitsMax) - 1) As UShort
    Private ReadOnly PosSlot(kNumLenToPosStates * (1 << 6) - 1) As UShort
    Private ReadOnly SpecPos(kNumFullDistances - kEndPosModelIndex) As UShort   ' 115 entrées
    Private ReadOnly Align_((1 << kNumAlignBits) - 1) As UShort
    Private ReadOnly LenChoice(1) As UShort
    Private ReadOnly LenLow((1 << kNumPosBitsMax) * (1 << 3) - 1) As UShort
    Private ReadOnly LenMid((1 << kNumPosBitsMax) * (1 << 3) - 1) As UShort
    Private ReadOnly LenHigh((1 << 8) - 1) As UShort
    Private ReadOnly RepChoice(1) As UShort
    Private ReadOnly RepLow((1 << kNumPosBitsMax) * (1 << 3) - 1) As UShort
    Private ReadOnly RepMid((1 << kNumPosBitsMax) * (1 << 3) - 1) As UShort
    Private ReadOnly RepHigh((1 << 8) - 1) As UShort

    Public Shared Sub Decode(source() As Byte, srcStart As Integer, srcLen As Integer,
                             dest() As Byte, destLen As Integer, hunkBytes As Integer)
        Dim d As New LzmaCodec()
        d.Run(source, srcStart, srcLen, dest, destLen)
    End Sub

    Private Sub Run(source() As Byte, srcStart As Integer, srcLen As Integer, dest() As Byte, destLen As Integer)
        src = source : srcPos = srcStart : srcEnd = srcStart + srcLen
        outBuf = dest : outPos = 0
        lc = 3 : lp = 0 : pb = 2

        InitProbs()
        RangeInit()

        Dim state = 0
        Dim rep0 As UInteger = 0, rep1 As UInteger = 0, rep2 As UInteger = 0, rep3 As UInteger = 0
        Dim pbMask = (1 << pb) - 1
        Dim lpMask = (1 << lp) - 1

        While outPos < destLen
            Dim posState = outPos And pbMask
            If DecodeBit(IsMatch, (state << kNumPosBitsMax) + posState) = 0 Then
                Dim prevByte As Integer = If(outPos = 0, 0, outBuf(outPos - 1))
                Dim litState = ((outPos And lpMask) << lc) + (prevByte >> (8 - lc))
                Dim probBase = &H300 * litState
                Dim symbol As Integer = 1
                If state >= 7 Then
                    Dim matchByte As Integer = outBuf(outPos - CInt(rep0) - 1)
                    Do
                        Dim matchBit = (matchByte >> 7) And 1
                        matchByte <<= 1
                        Dim bit = DecodeBit(litProbs, probBase + ((1 + matchBit) << 8) + symbol)
                        symbol = (symbol << 1) Or bit
                        If matchBit <> bit Then Exit Do
                    Loop While symbol < &H100
                End If
                While symbol < &H100
                    symbol = (symbol << 1) Or DecodeBit(litProbs, probBase + symbol)
                End While
                outBuf(outPos) = CByte(symbol And &HFF)
                outPos += 1
                state = If(state < 4, 0, If(state < 10, state - 3, state - 6))
                Continue While
            End If

            Dim len As Integer
            If DecodeBit(IsRep, state) <> 0 Then
                If DecodeBit(IsRepG0, state) = 0 Then
                    If DecodeBit(IsRep0Long, (state << kNumPosBitsMax) + posState) = 0 Then
                        state = If(state < 7, 9, 11)
                        outBuf(outPos) = outBuf(outPos - CInt(rep0) - 1)
                        outPos += 1
                        Continue While
                    End If
                Else
                    Dim dist As UInteger
                    If DecodeBit(IsRepG1, state) = 0 Then
                        dist = rep1
                    Else
                        If DecodeBit(IsRepG2, state) = 0 Then
                            dist = rep2
                        Else
                            dist = rep3 : rep3 = rep2
                        End If
                        rep2 = rep1
                    End If
                    rep1 = rep0 : rep0 = dist
                End If
                len = DecodeLen(RepChoice, RepLow, RepMid, RepHigh, posState)
                state = If(state < 7, 8, 11)
            Else
                rep3 = rep2 : rep2 = rep1 : rep1 = rep0
                len = DecodeLen(LenChoice, LenLow, LenMid, LenHigh, posState)
                state = If(state < 7, 7, 10)
                rep0 = DecodeDistance(len)
                If rep0 = &HFFFFFFFFUI Then Exit While
            End If

            len += kMatchMinLen
            Dim srcIdx = outPos - CInt(rep0) - 1
            For i = 0 To len - 1
                outBuf(outPos) = outBuf(srcIdx)
                outPos += 1 : srcIdx += 1
                If outPos >= destLen Then Exit For
            Next
        End While
    End Sub

    Private Function NextByte() As UInteger
        Dim b As UInteger = If(srcPos < srcEnd, src(srcPos), 0)
        srcPos += 1
        Return b
    End Function

    Private Sub RangeInit()
        NextByte()
        Code = 0 : Range = &HFFFFFFFFUI
        For i = 0 To 3
            Code = (Code << 8) Or NextByte()
        Next
    End Sub

    Private Sub Normalize()
        If Range < kTopValue Then
            Range <<= 8
            Code = (Code << 8) Or NextByte()
        End If
    End Sub

    Private Function DecodeBit(probs() As UShort, idx As Integer) As Integer
        Dim v As UInteger = probs(idx)
        Dim bound = (Range >> kNumBitModelTotalBits) * v
        Dim symbol As Integer
        If Code < bound Then
            v += (kBitModelTotal - v) >> kNumMoveBits
            Range = bound
            symbol = 0
        Else
            v -= v >> kNumMoveBits
            Code -= bound
            Range -= bound
            symbol = 1
        End If
        probs(idx) = CUShort(v)
        Normalize()
        Return symbol
    End Function

    Private Function DecodeDirectBits(numBits As Integer) As UInteger
        Dim res As UInteger = 0
        Do
            Range >>= 1
            Code -= Range
            Dim t As UInteger = 0UI - (Code >> 31)
            Code += Range And t
            Normalize()
            res = (res << 1) + t + 1UI
            numBits -= 1
        Loop While numBits > 0
        Return res
    End Function

    Private Function BitTree(probs() As UShort, baseIdx As Integer, numBits As Integer) As Integer
        Dim m As Integer = 1
        For i = 0 To numBits - 1
            m = (m << 1) Or DecodeBit(probs, baseIdx + m)
        Next
        Return m - (1 << numBits)
    End Function

    Private Function BitTreeReverse(probs() As UShort, baseIdx As Integer, numBits As Integer) As UInteger
        Dim m As Integer = 1
        Dim res As UInteger = 0
        For i = 0 To numBits - 1
            Dim b = DecodeBit(probs, baseIdx + m)
            m = (m << 1) Or b
            res = res Or (CUInt(b) << i)
        Next
        Return res
    End Function

    Private Function DecodeLen(choice() As UShort, low() As UShort, mid() As UShort, high() As UShort,
                               posState As Integer) As Integer
        If DecodeBit(choice, 0) = 0 Then Return BitTree(low, posState << 3, 3)
        If DecodeBit(choice, 1) = 0 Then Return 8 + BitTree(mid, posState << 3, 3)
        Return 16 + BitTree(high, 0, 8)
    End Function

    Private Function DecodeDistance(len As Integer) As UInteger
        Dim lenState = If(len < kNumLenToPosStates, len, kNumLenToPosStates - 1)
        Dim slot = BitTree(PosSlot, lenState << 6, 6)
        If slot < 4 Then Return CUInt(slot)
        Dim numDirect = (slot >> 1) - 1
        Dim dist As UInteger = CUInt(2 Or (slot And 1)) << numDirect
        If slot < kEndPosModelIndex Then
            dist += BitTreeReverse(SpecPos, CInt(dist) - slot, numDirect)
        Else
            dist += DecodeDirectBits(numDirect - kNumAlignBits) << kNumAlignBits
            dist += BitTreeReverse(Align_, 0, kNumAlignBits)
        End If
        Return dist
    End Function

    Private Sub InitProbs()
        ReDim litProbs(&H300 * (1 << (lc + lp)) - 1)
        FillProbs(litProbs)
        FillProbs(IsMatch) : FillProbs(IsRep) : FillProbs(IsRepG0) : FillProbs(IsRepG1)
        FillProbs(IsRepG2) : FillProbs(IsRep0Long) : FillProbs(PosSlot) : FillProbs(SpecPos)
        FillProbs(Align_) : FillProbs(LenChoice) : FillProbs(LenLow) : FillProbs(LenMid)
        FillProbs(LenHigh) : FillProbs(RepChoice) : FillProbs(RepLow) : FillProbs(RepMid) : FillProbs(RepHigh)
    End Sub

    Private Shared Sub FillProbs(a() As UShort)
        For i = 0 To a.Length - 1
            a(i) = PROB_INIT
        Next
    End Sub

End Class
