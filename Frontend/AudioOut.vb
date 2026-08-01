''' <summary>Sortie audio NAudio : buffer 500 ms, drop propre, pré-roll anti-famine</summary>
Public Class AudioOut
    Implements IDisposable

    Private waveOutEvent As NAudio.Wave.IWavePlayer
    Private bufferedWaveProvider As NAudio.Wave.BufferedWaveProvider
    Private waveFormat As NAudio.Wave.WaveFormat
    Private preRolled As Boolean = False

    Public Sub New(sampleRate As Integer, channels As Integer)
        Try
            waveFormat = New NAudio.Wave.WaveFormat(sampleRate, 16, channels)
            waveOutEvent = New NAudio.Wave.WaveOutEvent() With {
                .DesiredLatency = 100
            }
            bufferedWaveProvider = New NAudio.Wave.BufferedWaveProvider(waveFormat) With {
                .BufferDuration = TimeSpan.FromMilliseconds(500),
                .DiscardOnBufferOverflow = True
            }
            waveOutEvent.Init(bufferedWaveProvider)
            waveOutEvent.Play()
        Catch ex As Exception
            Throw New Exception("Erreur initialisation audio NAudio", ex)
        End Try
    End Sub

    ''' <summary>Envoie des échantillons au buffer</summary>
    Public Sub SendAudio(samples() As Short)
        If samples Is Nothing OrElse samples.Length = 0 Then Return

        ' Pré-roll : ~60 ms de silence au premier envoi pour éviter la famine initiale
        If Not preRolled Then
            preRolled = True
            Dim silence(CInt(waveFormat.AverageBytesPerSecond * 0.06) - 1) As Byte
            bufferedWaveProvider.AddSamples(silence, 0, silence.Length)
        End If

        Dim audioBytes(samples.Length * 2 - 1) As Byte
        System.Buffer.BlockCopy(samples, 0, audioBytes, 0, audioBytes.Length)

        Try
            bufferedWaveProvider.AddSamples(audioBytes, 0, audioBytes.Length)
        Catch
        End Try
    End Sub

    ''' <summary>Niveau de remplissage du buffer</summary>
    Public Function GetBufferedBytes() As Integer
        If bufferedWaveProvider IsNot Nothing Then
            Return bufferedWaveProvider.BufferedBytes
        End If
        Return 0
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If waveOutEvent IsNot Nothing Then
            waveOutEvent.Stop()
            waveOutEvent.Dispose()
        End If
        If bufferedWaveProvider IsNot Nothing Then
            bufferedWaveProvider.ClearBuffer()
        End If
    End Sub

End Class
