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
            Case 8
                lfoFreq = value
            Case 9
                lfoControl = value
        End Select
    End Sub

    ''' <summary>Génère les échantillons audio pour une frame (~735 à 60fps)</summary>
    Public Function GenerateSamples(cyclesThisFrame As Long) As Short()
        Dim numSamples = CInt(PceConstants.AUDIO_SAMPLE_RATE / PceConstants.FRAME_RATE)
        Dim result(numSamples - 1) As Short

        ' Gains par canal (volume log × balance canal × balance générale, mono)
        Dim mainL = BalTable((mainBalance >> 4) And &HF)
        Dim mainR = BalTable(mainBalance And &HF)
        Dim chanGain(5) As Double
        For chIdx = 0 To 5
            Dim ch = channels(chIdx)
            Dim chL = BalTable((ch.Balance >> 4) And &HF)
            Dim chR = BalTable(ch.Balance And &HF)
            chanGain(chIdx) = VolTable(ch.Volume) * (chL * mainL + chR * mainR) * 0.5 * 350.0
        Next

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
            Dim mixed As Integer = 0
            Dim frameCycle As Long = CLng(cyclesThisFrame) * s \ numSamples

            For chIdx = 0 To 5
                Dim ch = channels(chIdx)
                If Not ch.Enabled Then Continue For
                If chanGain(chIdx) < 0.01 Then Continue For

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
                    ' Période 0 = 4096 sur le hardware
                    Dim period = If(ch.Freq = 0, 4096, ch.Freq)
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

                ' Gain logarithmique : ±16 × 700 = ±11200/canal (clamp si cumul)
                mixed += CInt(sample * chanGain(chIdx))
            Next

            ' Clamp
            If mixed > 32767 Then mixed = 32767
            If mixed < -32768 Then mixed = -32768
            result(s) = CShort(mixed)
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

End Class
