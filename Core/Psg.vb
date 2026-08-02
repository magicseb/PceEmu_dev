''' <summary>PSG PC Engine - 6 canaux waveform, bruit, DDA</summary>
Public Class Psg

    Private Structure DdaEvent
        Public Cycle As Long
        Public Value As Integer
        Public Sub New(c As Long, v As Integer)
            Cycle = c : Value = v
        End Sub
    End Structure

    ''' <summary>Fournit le cycle CPU courant (pour timestamper le DDA)</summary>
    Public CycleProvider As Func(Of Long) = Nothing

    Private Class Channel
        Public Freq As Integer = 0           ' 12 bits
        Public Enabled As Boolean = False
        Public DdaMode As Boolean = False
        Public Volume As Integer = 0         ' 5 bits
        Public Balance As Integer = &HFF
        Public Waveform(31) As Integer       ' 32 × 5 bits
        Public WaveWritePos As Integer = 0
        Public Phase As Double = 0
        Public DdaSample As Integer = 16
        Public DdaPreFrame As Integer = 16
        Public DdaEvents As New System.Collections.Generic.List(Of DdaEvent)
        Public NoiseEnabled As Boolean = False
        Public NoiseFreq As Integer = 0
        Public NoiseLfsr As Integer = 1
    End Class

    Private channels(5) As Channel
    Private selectedChannel As Integer = 0
    Private mainBalance As Integer = &HFF
    Private lfoFreq As Integer = 0
    Private lfoControl As Integer = 0

    Private audioBuffer As System.Collections.Generic.List(Of Short)
    Public Shared DbgWriteCount As Long = 0
    Public Shared DbgFreqWrites As Long = 0
    Public Shared DbgDdaWrites As Long = 0
    Public Shared DbgNoiseWrites As Long = 0
    Public Shared DbgLfoEnableWrites As Long = 0

    ' Tables d'atténuation logarithmiques du hardware :
    ' volume canal 5 bits = 1.5 dB/pas, balance 4 bits = 3 dB/pas
    Private Shared ReadOnly VolTable(31) As Double
    Private Shared ReadOnly BalTable(15) As Double

    Shared Sub New()
        For v = 0 To 31
            VolTable(v) = Math.Pow(10.0, -(31 - v) * 1.5 / 20.0)
        Next
        VolTable(0) = 0
        For b = 0 To 15
            BalTable(b) = Math.Pow(10.0, -(15 - b) * 3.0 / 20.0)
        Next
        BalTable(0) = 0
    End Sub

    Public Sub New()
        For i = 0 To 5
            channels(i) = New Channel()
        Next
        audioBuffer = New System.Collections.Generic.List(Of Short)
    End Sub

    Public Function Read(offset As Integer) As Integer
        Return 0
    End Function

    ''' <summary>Debug : état des canaux</summary>
    Public Function DbgState() As String
        Dim sb As New Text.StringBuilder()
        For i = 0 To 5
            Dim ch = channels(i)
            Dim wmin = 31, wmax = 0
            For w = 0 To 31
                If ch.Waveform(w) < wmin Then wmin = ch.Waveform(w)
                If ch.Waveform(w) > wmax Then wmax = ch.Waveform(w)
            Next
            sb.Append("ch" & i & "[en=" & If(ch.Enabled, 1, 0) & " f=" & ch.Freq & " v=" & ch.Volume &
                      " wave=" & wmin & ".." & wmax & " wpos=" & ch.WaveWritePos & "] ")
        Next
        Return sb.ToString()
    End Function

    Public Sub Write(offset As Integer, value As Integer)
        DbgWriteCount += 1
        If (offset And &HF) = 2 OrElse (offset And &HF) = 3 Then DbgFreqWrites += 1
        value = value And &HFF
        Select Case offset And &HF
            Case 0  ' Channel select
                selectedChannel = value And 7
            Case 1  ' Main balance
                mainBalance = value
            Case 2  ' Freq low
                If selectedChannel < 6 Then
                    Dim ch = channels(selectedChannel)
                    ch.Freq = (ch.Freq And &HF00) Or value
                End If
            Case 3  ' Freq high
                If selectedChannel < 6 Then
                    Dim ch = channels(selectedChannel)
                    ch.Freq = (ch.Freq And &HFF) Or ((value And &HF) << 8)
                End If
            Case 4  ' Control
                If selectedChannel < 6 Then
                    Dim ch = channels(selectedChannel)
                    Dim wasEnabled = ch.Enabled
                    ch.Enabled = (value And &H80) <> 0
                    ch.DdaMode = (value And &H40) <> 0
                    ch.Volume = value And &H1F
                    ' Reset position d'écriture quand canal désactivé et pas DDA
                    If Not ch.Enabled AndAlso Not ch.DdaMode Then
                        ch.WaveWritePos = 0
                    End If
                End If
            Case 5  ' Channel balance
                If selectedChannel < 6 Then
                    channels(selectedChannel).Balance = value
                End If
            Case 6  ' Waveform data / DDA
                If selectedChannel < 6 Then
                    Dim ch = channels(selectedChannel)
                    If ch.DdaMode Then
                        DbgDdaWrites += 1
                        Dim cyc As Long = If(CycleProvider IsNot Nothing, CycleProvider(), 0)
                        If ch.DdaEvents.Count = 0 Then ch.DdaPreFrame = ch.DdaSample
                        ch.DdaEvents.Add(New DdaEvent(cyc, value And &H1F))
                        ch.DdaSample = value And &H1F
                    Else
                        ch.Waveform(ch.WaveWritePos) = value And &H1F
                        ch.WaveWritePos = (ch.WaveWritePos + 1) And &H1F
                    End If
                End If
            Case 7  ' Noise (canaux 4-5)
                If selectedChannel >= 4 AndAlso selectedChannel < 6 Then
                    If (value And &H80) <> 0 Then DbgNoiseWrites += 1
                    channels(selectedChannel).NoiseEnabled = (value And &H80) <> 0
                    channels(selectedChannel).NoiseFreq = value And &H1F
                End If
            Case 8  ' $0808 : multiplicateur de période du canal modulateur
                lfoFreq = value
            Case 9  ' $0809 : bits 0-1 = profondeur, bit 7 = maintien du LFO à zéro
                lfoControl = value
                If (value And &H3) <> 0 Then DbgLfoEnableWrites += 1
                ' Le bit 7 remet le modulateur au début de sa forme d'onde
                If (value And &H80) <> 0 Then channels(1).Phase = 0
        End Select
    End Sub

    ''' <summary>Génère les échantillons audio pour une frame (~735 à 60fps)</summary>
    Public Function GenerateSamples(cyclesThisFrame As Long) As Short()
        Dim numSamples = CInt(PceConstants.AUDIO_SAMPLE_RATE / PceConstants.FRAME_RATE)
        ' Sortie stéréo entrelacée : deux Short (gauche, droite) par échantillon.
        Dim result(numSamples * 2 - 1) As Short

        ' Gains par canal, séparés gauche/droite (volume log × balance canal × balance générale).
        ' L'ancien rendu mono valait exactement (gaucheL + droiteR) / 2 de ces deux gains.
        Dim mainL = BalTable((mainBalance >> 4) And &HF)
        Dim mainR = BalTable(mainBalance And &HF)
        Dim chanGainL(5) As Double
        Dim chanGainR(5) As Double
        For chIdx = 0 To 5
            Dim ch = channels(chIdx)
            Dim chL = BalTable((ch.Balance >> 4) And &HF)
            Dim chR = BalTable(ch.Balance And &HF)
            chanGainL(chIdx) = VolTable(ch.Volume) * chL * mainL * 350.0
            chanGainR(chIdx) = VolTable(ch.Volume) * chR * mainR * 350.0
        Next

        ' LFO : le canal 1 cesse d'être audible et module la période du canal 0.
        ' $0809 bits 0-1 : 0 = désactivé, 1 = ×1, 2 = ×16, 3 = ×256 ; bit 7 = LFO maintenu à zéro.
        Dim lfoDepth = lfoControl And &H3
        Dim lfoEnabled = lfoDepth <> 0              ' Le canal 1 devient modulateur, donc muet
        Dim lfoHeld = (lfoControl And &H80) <> 0    ' Modulateur figé : plus aucune modulation
        Dim lfoShift = (lfoDepth - 1) * 4
        Dim lfoStep As Double = 0
        If lfoEnabled AndAlso Not lfoHeld Then
            ' Période du modulateur = période du canal 1 × registre $0808
            Dim lfoPeriod = Math.Max(1, channels(1).Freq * lfoFreq)
            lfoStep = 3579545.0 / lfoPeriod / PceConstants.AUDIO_SAMPLE_RATE
        End If

        ' Pointeurs de relecture des événements DDA (timeline de la frame)
        Dim ddaPtr(5) As Integer
        Dim ddaVal(5) As Integer
        For chIdx = 0 To 5
            ' Valeur au début de frame = état AVANT les événements de cette frame
            If channels(chIdx).DdaEvents.Count > 0 Then
                ddaVal(chIdx) = channels(chIdx).DdaPreFrame
            Else
                ddaVal(chIdx) = channels(chIdx).DdaSample
            End If
        Next

        For s = 0 To numSamples - 1
            Dim mixedL As Integer = 0
            Dim mixedR As Integer = 0
            Dim frameCycle As Long = CLng(cyclesThisFrame) * s \ numSamples

            ' Sortie courante du modulateur, centrée puis décalée selon la profondeur
            Dim lfoDelta As Integer = 0
            If lfoEnabled AndAlso Not lfoHeld Then
                Dim chLfo = channels(1)
                chLfo.Phase += lfoStep
                While chLfo.Phase >= 32.0
                    chLfo.Phase -= 32.0
                End While
                lfoDelta = (chLfo.Waveform(CInt(Math.Floor(chLfo.Phase)) And &H1F) - 16) << lfoShift
            End If

            For chIdx = 0 To 5
                Dim ch = channels(chIdx)
                ' Le canal 1 sert de modulateur : il ne produit aucun son
                If lfoEnabled AndAlso chIdx = 1 Then Continue For
                If Not ch.Enabled Then Continue For
                If chanGainL(chIdx) < 0.01 AndAlso chanGainR(chIdx) < 0.01 Then Continue For

                Dim sample As Integer
                If ch.DdaMode Then
                    ' Avancer dans les événements DDA jusqu'au cycle courant
                    While ddaPtr(chIdx) < ch.DdaEvents.Count AndAlso ch.DdaEvents(ddaPtr(chIdx)).Cycle <= frameCycle
                        ddaVal(chIdx) = ch.DdaEvents(ddaPtr(chIdx)).Value
                        ddaPtr(chIdx) += 1
                    End While
                    sample = ddaVal(chIdx) - 16
                ElseIf ch.NoiseEnabled Then
                    ' Bruit LFSR
                    Dim nf = 64 * (32 - ch.NoiseFreq)
                    ch.Phase += 3579545.0 / Math.Max(nf, 1) / PceConstants.AUDIO_SAMPLE_RATE
                    Dim steps = CInt(Math.Floor(ch.Phase))
                    ch.Phase -= steps
                    ' Limiter les itérations (freq élevées)
                    If steps > 64 Then steps = 64
                    For n = 1 To steps
                        Dim fb = ((ch.NoiseLfsr >> 0) Xor (ch.NoiseLfsr >> 1)) And 1
                        ch.NoiseLfsr = (ch.NoiseLfsr >> 1) Or (fb << 17)
                    Next
                    sample = If((ch.NoiseLfsr And 1) <> 0, 15, -16)
                Else
                    ' Le LFO ajoute sa sortie signée à la période du canal 0 (12 bits)
                    Dim basePeriod = ch.Freq
                    If lfoEnabled AndAlso chIdx = 0 Then
                        basePeriod = (basePeriod + lfoDelta) And &HFFF
                    End If
                    ' Période 0 = 4096 sur le hardware
                    Dim period = If(basePeriod = 0, 4096, basePeriod)
                    ' Fréquence du ton : 3.58 MHz / (32 × période)
                    ' Au-dessus de Nyquist (période < ~6) : sortir la moyenne (évite l'aliasing → bruit)
                    If period < 6 Then
                        Dim sum = 0
                        For w = 0 To 31
                            sum += ch.Waveform(w)
                        Next
                        sample = (sum >> 5) - 16
                    Else
                        ch.Phase += 3579545.0 / period / PceConstants.AUDIO_SAMPLE_RATE
                        While ch.Phase >= 32.0
                            ch.Phase -= 32.0
                        End While
                        sample = ch.Waveform(CInt(Math.Floor(ch.Phase)) And &H1F) - 16
                    End If
                End If

                ' Gain logarithmique appliqué séparément à chaque voie stéréo
                mixedL += CInt(sample * chanGainL(chIdx))
                mixedR += CInt(sample * chanGainR(chIdx))
            Next

            ' Clamp par voie
            If mixedL > 32767 Then mixedL = 32767
            If mixedL < -32768 Then mixedL = -32768
            If mixedR > 32767 Then mixedR = 32767
            If mixedR < -32768 Then mixedR = -32768
            result(s * 2) = CShort(mixedL)
            result(s * 2 + 1) = CShort(mixedR)
        Next

        ' Purger les événements DDA consommés
        For chIdx = 0 To 5
            channels(chIdx).DdaEvents.Clear()
        Next

        Return result
    End Function

    ''' <summary>Retourne et vide le buffer audio</summary>
    Public Function GetAudioBuffer() As Short()
        Dim r = audioBuffer.ToArray()
        audioBuffer.Clear()
        Return r
    End Function

    Public Sub AddSamples(samples() As Short)
        If samples IsNot Nothing Then audioBuffer.AddRange(samples)
    End Sub


    ''' <summary>Écrit l'état du PSG dans une sauvegarde.</summary>
    Public Sub SaveState(w As System.IO.BinaryWriter)
        w.Write(selectedChannel) : w.Write(mainBalance)
        w.Write(lfoFreq) : w.Write(lfoControl)
        For Each ch In channels
            w.Write(ch.Freq) : w.Write(ch.Enabled) : w.Write(ch.DdaMode)
            w.Write(ch.Volume) : w.Write(ch.Balance)
            For i = 0 To ch.Waveform.Length - 1
                w.Write(ch.Waveform(i))
            Next
            w.Write(ch.WaveWritePos) : w.Write(ch.Phase)
            w.Write(ch.DdaSample) : w.Write(ch.DdaPreFrame)
            w.Write(ch.NoiseEnabled) : w.Write(ch.NoiseFreq) : w.Write(ch.NoiseLfsr)
        Next
    End Sub

    ''' <summary>
    ''' Restaure l'état du PSG. Les événements DDA en attente ne sont pas sauvegardés :
    ''' ils ne vivent que le temps d'une frame et sont reconstruits par le jeu.
    ''' </summary>
    Public Sub LoadState(r As System.IO.BinaryReader)
        selectedChannel = r.ReadInt32() : mainBalance = r.ReadInt32()
        lfoFreq = r.ReadInt32() : lfoControl = r.ReadInt32()
        For Each ch In channels
            ch.Freq = r.ReadInt32() : ch.Enabled = r.ReadBoolean() : ch.DdaMode = r.ReadBoolean()
            ch.Volume = r.ReadInt32() : ch.Balance = r.ReadInt32()
            For i = 0 To ch.Waveform.Length - 1
                ch.Waveform(i) = r.ReadInt32()
            Next
            ch.WaveWritePos = r.ReadInt32() : ch.Phase = r.ReadDouble()
            ch.DdaSample = r.ReadInt32() : ch.DdaPreFrame = r.ReadInt32()
            ch.NoiseEnabled = r.ReadBoolean() : ch.NoiseFreq = r.ReadInt32() : ch.NoiseLfsr = r.ReadInt32()
            ch.DdaEvents.Clear()
        Next
        audioBuffer.Clear()
    End Sub

End Class
