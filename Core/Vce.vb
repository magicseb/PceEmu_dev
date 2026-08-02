''' <summary>VCE HuC6260 - palette 512 couleurs 9 bits (G3R3B3)</summary>
Public Class Vce

    Private palette(511) As Integer      ' Valeurs brutes 9 bits
    Private argbCache(511) As Integer    ' Cache ARGB
    Private ctaAddress As Integer = 0    ' Adresse palette courante
    Private controlReg As Integer = 0

    Public Sub New()
        For i = 0 To 511
            palette(i) = 0
            argbCache(i) = &HFF000000
        Next
    End Sub

    ''' <summary>Lecture VCE</summary>
    Public Function Read(offset As Integer) As Integer
        Select Case offset And 7
            Case 4  ' Data LSB
                Return palette(ctaAddress And &H1FF) And &HFF
            Case 5  ' Data MSB
                Dim v = (palette(ctaAddress And &H1FF) >> 8) And 1
                ctaAddress = (ctaAddress + 1) And &H1FF
                Return v Or &HFE   ' Bits hauts à 1
            Case Else
                Return &HFF
        End Select
    End Function

    ''' <summary>Écriture VCE</summary>
    Public Sub Write(offset As Integer, value As Integer)
        value = value And &HFF
        Select Case offset And 7
            Case 0  ' Contrôle (pixel clock)
                controlReg = value
            Case 2  ' Adresse LSB
                ctaAddress = (ctaAddress And &H100) Or value
            Case 3  ' Adresse MSB
                ctaAddress = (ctaAddress And &HFF) Or ((value And 1) << 8)
            Case 4  ' Data LSB
                Dim idx = ctaAddress And &H1FF
                palette(idx) = (palette(idx) And &H100) Or value
                UpdateCache(idx)
            Case 5  ' Data MSB
                Dim idx2 = ctaAddress And &H1FF
                palette(idx2) = (palette(idx2) And &HFF) Or ((value And 1) << 8)
                UpdateCache(idx2)
                ctaAddress = (ctaAddress + 1) And &H1FF
        End Select
    End Sub

    ''' <summary>Convertit 9 bits G3R3B3 → ARGB</summary>
    Private Sub UpdateCache(idx As Integer)
        Dim raw = palette(idx)
        Dim g = (raw >> 6) And 7
        Dim r = (raw >> 3) And 7
        Dim b = raw And 7
        ' Expansion 3 bits → 8 bits
        Dim r8 = (r << 5) Or (r << 2) Or (r >> 1)
        Dim g8 = (g << 5) Or (g << 2) Or (g >> 1)
        Dim b8 = (b << 5) Or (b << 2) Or (b >> 1)
        argbCache(idx) = &HFF000000 Or (r8 << 16) Or (g8 << 8) Or b8
    End Sub

    ''' <summary>Retourne l'ARGB d'une entrée palette (0-511)</summary>
    Public Function GetColorArgb(index As Integer) As Integer
        Return argbCache(index And &H1FF)
    End Function


    ''' <summary>Écrit l'état du VCE dans une sauvegarde.</summary>
    Public Sub SaveState(w As System.IO.BinaryWriter)
        For i = 0 To palette.Length - 1
            w.Write(palette(i))
        Next
        w.Write(ctaAddress) : w.Write(controlReg)
    End Sub

    ''' <summary>Restaure l'état du VCE ; le cache ARGB est recalculé.</summary>
    Public Sub LoadState(r As System.IO.BinaryReader)
        For i = 0 To palette.Length - 1
            palette(i) = r.ReadInt32()
            UpdateCache(i)
        Next
        ctaAddress = r.ReadInt32() : controlReg = r.ReadInt32()
    End Sub

End Class
