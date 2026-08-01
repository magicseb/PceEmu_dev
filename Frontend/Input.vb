''' <summary>Gestion clavier pour l'émulateur</summary>
Public Class InputManager

    Private keyState As System.Collections.Generic.Dictionary(Of String, Boolean)
    Private lastKeyState As System.Collections.Generic.Dictionary(Of String, Boolean)
    
    Public Sub New()
        keyState = New System.Collections.Generic.Dictionary(Of String, Boolean)
        lastKeyState = New System.Collections.Generic.Dictionary(Of String, Boolean)
        
        InitializeKeyMap()
    End Sub

    ''' <summary>Initialise le mappage des touches</summary>
    Private Sub InitializeKeyMap()
        keyState.Add("Up", False)
        keyState.Add("Down", False)
        keyState.Add("Left", False)
        keyState.Add("Right", False)
        keyState.Add("Z", False)       ' Bouton II
        keyState.Add("X", False)       ' Bouton I
        keyState.Add("Enter", False)   ' Run
        keyState.Add("LShift", False)  ' Select
        keyState.Add("P", False)       ' Pause
        keyState.Add("R", False)       ' Reset
        keyState.Add("S", False)       ' Save state
        keyState.Add("L", False)       ' Load state
        
        For Each key In keyState.Keys.ToList()
            lastKeyState.Add(key, False)
        Next
    End Sub

    ''' <summary>Met à jour l'état d'une touche (KeyDown)</summary>
    Public Sub HandleKeyDown(e As System.Windows.Forms.KeyEventArgs)
        Select Case e.KeyCode
            Case System.Windows.Forms.Keys.Up
                keyState("Up") = True
            Case System.Windows.Forms.Keys.Down
                keyState("Down") = True
            Case System.Windows.Forms.Keys.Left
                keyState("Left") = True
            Case System.Windows.Forms.Keys.Right
                keyState("Right") = True
            Case System.Windows.Forms.Keys.Z
                keyState("Z") = True
            Case System.Windows.Forms.Keys.X
                keyState("X") = True
            Case System.Windows.Forms.Keys.Return
                keyState("Enter") = True
            Case System.Windows.Forms.Keys.LShiftKey, System.Windows.Forms.Keys.ShiftKey
                keyState("LShift") = True
            Case System.Windows.Forms.Keys.P
                keyState("P") = True
            Case System.Windows.Forms.Keys.R
                keyState("R") = True
            Case System.Windows.Forms.Keys.S
                keyState("S") = True
            Case System.Windows.Forms.Keys.L
                keyState("L") = True
        End Select
    End Sub

    ''' <summary>Met à jour l'état d'une touche (KeyUp)</summary>
    Public Sub HandleKeyUp(e As System.Windows.Forms.KeyEventArgs)
        Select Case e.KeyCode
            Case System.Windows.Forms.Keys.Up
                keyState("Up") = False
            Case System.Windows.Forms.Keys.Down
                keyState("Down") = False
            Case System.Windows.Forms.Keys.Left
                keyState("Left") = False
            Case System.Windows.Forms.Keys.Right
                keyState("Right") = False
            Case System.Windows.Forms.Keys.Z
                keyState("Z") = False
            Case System.Windows.Forms.Keys.X
                keyState("X") = False
            Case System.Windows.Forms.Keys.Return
                keyState("Enter") = False
            Case System.Windows.Forms.Keys.LShiftKey, System.Windows.Forms.Keys.ShiftKey
                keyState("LShift") = False
            Case System.Windows.Forms.Keys.P
                keyState("P") = False
            Case System.Windows.Forms.Keys.R
                keyState("R") = False
            Case System.Windows.Forms.Keys.S
                keyState("S") = False
            Case System.Windows.Forms.Keys.L
                keyState("L") = False
        End Select
    End Sub

    ''' <summary>Retourne l'état actuel des touches</summary>
    Public Function GetKeyState() As System.Collections.Generic.Dictionary(Of String, Boolean)
        Return New System.Collections.Generic.Dictionary(Of String, Boolean)(keyState)
    End Function

    ''' <summary>Détecte une pression de touche (transition 0→1)</summary>
    Public Function IsKeyPressed(keyName As String) As Boolean
        If Not keyState.ContainsKey(keyName) Then Return False
        If Not lastKeyState.ContainsKey(keyName) Then Return False
        
        Dim result = keyState(keyName) And Not lastKeyState(keyName)
        lastKeyState(keyName) = keyState(keyName)
        Return result
    End Function

    ''' <summary>Détecte si une touche est maintenue</summary>
    Public Function IsKeyHeld(keyName As String) As Boolean
        If keyState.ContainsKey(keyName) Then
            Return keyState(keyName)
        End If
        Return False
    End Function

End Class
