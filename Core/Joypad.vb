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

    ''' <summary>
    ''' Applique un état de boutons fourni par le frontend. Les clés désignent les
    ''' boutons de la console, pas des touches : le clavier et la manette peuvent
    ''' ainsi alimenter la même entrée.
    ''' </summary>
    Public Sub UpdateFromKeys(buttons As System.Collections.Generic.Dictionary(Of String, Boolean))
        If buttons Is Nothing Then Return
        BtnUp = Pressed(buttons, "Haut")
        BtnDown = Pressed(buttons, "Bas")
        BtnLeft = Pressed(buttons, "Gauche")
        BtnRight = Pressed(buttons, "Droite")
        BtnI = Pressed(buttons, "BoutonI")
        BtnII = Pressed(buttons, "BoutonII")
        BtnSelect = Pressed(buttons, "Select")
        BtnRun = Pressed(buttons, "Run")
    End Sub

    Private Shared Function Pressed(buttons As System.Collections.Generic.Dictionary(Of String, Boolean),
                                    name As String) As Boolean
        Dim value As Boolean
        If buttons.TryGetValue(name, value) Then Return value
        Return False
    End Function


    ''' <summary>Écrit l'état de la manette dans une sauvegarde.</summary>
    Public Sub SaveState(w As System.IO.BinaryWriter)
        w.Write(sel) : w.Write(clr)
    End Sub

    ''' <summary>Restaure l'état de la manette ; les boutons sont réactualisés à la frame suivante.</summary>
    Public Sub LoadState(r As System.IO.BinaryReader)
        sel = r.ReadBoolean() : clr = r.ReadBoolean()
    End Sub

End Class
