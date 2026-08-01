''' <summary>Joypad PC Engine - 2 nibbles via SEL, boutons actifs bas</summary>
Public Class Joypad

    Private sel As Boolean = False
    Private clr As Boolean = False

    ' État des boutons (True = pressé)
    Public BtnUp As Boolean = False
    Public BtnDown As Boolean = False
    Public BtnLeft As Boolean = False
    Public BtnRight As Boolean = False
    Public BtnI As Boolean = False
    Public BtnII As Boolean = False
    Public BtnSelect As Boolean = False
    Public BtnRun As Boolean = False

    Public Sub Write(value As Integer)
        sel = (value And 1) <> 0
        clr = (value And 2) <> 0
    End Sub

    Public Function Read() As Integer
        If clr Then Return &H30

        Dim nibble As Integer = &HF
        If sel Then
            ' Directions : bit0=Up, bit1=Right, bit2=Down, bit3=Left (actifs bas)
            If BtnUp Then nibble = nibble And Not 1
            If BtnRight Then nibble = nibble And Not 2
            If BtnDown Then nibble = nibble And Not 4
            If BtnLeft Then nibble = nibble And Not 8
        Else
            ' Boutons : bit0=I, bit1=II, bit2=Select, bit3=Run
            If BtnI Then nibble = nibble And Not 1
            If BtnII Then nibble = nibble And Not 2
            If BtnSelect Then nibble = nibble And Not 4
            If BtnRun Then nibble = nibble And Not 8
        End If
        Return &H30 Or nibble
    End Function

    ''' <summary>Met à jour depuis un dictionnaire d'état clavier</summary>
    Public Sub UpdateFromKeys(keys As System.Collections.Generic.Dictionary(Of String, Boolean))
        If keys Is Nothing Then Return
        If keys.ContainsKey("Up") Then BtnUp = keys("Up")
        If keys.ContainsKey("Down") Then BtnDown = keys("Down")
        If keys.ContainsKey("Left") Then BtnLeft = keys("Left")
        If keys.ContainsKey("Right") Then BtnRight = keys("Right")
        If keys.ContainsKey("X") Then BtnI = keys("X")
        If keys.ContainsKey("Z") Then BtnII = keys("Z")
        If keys.ContainsKey("LShift") Then BtnSelect = keys("LShift")
        If keys.ContainsKey("Enter") Then BtnRun = keys("Enter")
    End Sub

End Class
