''' <summary>
''' Suit l'état du clavier et traduit les touches en actions de la console.
'''
''' Les assignations viennent de la configuration ; à défaut, les touches par défaut
''' sont déduites de la disposition active, si bien qu'un clavier AZERTY reçoit
''' naturellement W là où un QWERTY reçoit Z — la même position physique.
''' </summary>
Public Class InputManager

    ' Noms des actions, utilisés dans la configuration et dans la fenêtre de réglage
    Public Const ACTION_UP As String = "Haut"
    Public Const ACTION_DOWN As String = "Bas"
    Public Const ACTION_LEFT As String = "Gauche"
    Public Const ACTION_RIGHT As String = "Droite"
    Public Const ACTION_BUTTON_I As String = "BoutonI"
    Public Const ACTION_BUTTON_II As String = "BoutonII"
    Public Const ACTION_SELECT As String = "Select"
    Public Const ACTION_RUN As String = "Run"
    Public Const ACTION_PAUSE As String = "Pause"
    Public Const ACTION_RESET As String = "Reset"
    Public Const ACTION_SAVE_STATE As String = "Sauvegarder"
    Public Const ACTION_LOAD_STATE As String = "Charger"

    ''' <summary>Toutes les actions assignables, dans l'ordre d'affichage.</summary>
    Public Shared ReadOnly AllActions As String() = {
        ACTION_UP, ACTION_DOWN, ACTION_LEFT, ACTION_RIGHT,
        ACTION_BUTTON_I, ACTION_BUTTON_II, ACTION_SELECT, ACTION_RUN,
        ACTION_PAUSE, ACTION_RESET, ACTION_SAVE_STATE, ACTION_LOAD_STATE
    }

    ''' <summary>Actions transmises à la manette de la console.</summary>
    Private Shared ReadOnly PadActions As String() = {
        ACTION_UP, ACTION_DOWN, ACTION_LEFT, ACTION_RIGHT,
        ACTION_BUTTON_I, ACTION_BUTTON_II, ACTION_SELECT, ACTION_RUN
    }

    Private ReadOnly keyState As New System.Collections.Generic.Dictionary(Of String, Boolean)
    Private ReadOnly lastState As New System.Collections.Generic.Dictionary(Of String, Boolean)
    Private ReadOnly padState As New System.Collections.Generic.Dictionary(Of String, Boolean)

    ''' <summary>Touche assignée à chaque action.</summary>
    Private ReadOnly bindings As New System.Collections.Generic.Dictionary(Of String, System.Windows.Forms.Keys)

    Public Sub New(config As Settings)
        For Each action In AllActions
            keyState(action) = False
            lastState(action) = False
            padState(action) = False
        Next
        ApplyBindings(config)
    End Sub

    ''' <summary>Relit les assignations depuis la configuration.</summary>
    Public Sub ApplyBindings(config As Settings)
        bindings.Clear()

        For Each action In AllActions
            Dim configured As System.Windows.Forms.Keys? = Nothing
            If config IsNot Nothing Then configured = config.GetBinding(action)
            bindings(action) = If(configured.HasValue, configured.Value, DefaultKey(action))
        Next
    End Sub

    ''' <summary>
    ''' Touche par défaut d'une action. Les deux boutons de jeu sont désignés par leur
    ''' emplacement physique, les autres par une touche identique sur toutes les
    ''' dispositions.
    ''' </summary>
    Public Shared Function DefaultKey(action As String) As System.Windows.Forms.Keys
        Select Case action
            Case ACTION_UP : Return System.Windows.Forms.Keys.Up
            Case ACTION_DOWN : Return System.Windows.Forms.Keys.Down
            Case ACTION_LEFT : Return System.Windows.Forms.Keys.Left
            Case ACTION_RIGHT : Return System.Windows.Forms.Keys.Right
            Case ACTION_BUTTON_I : Return KeyboardLayout.KeyAt(KeyboardLayout.SCAN_X, System.Windows.Forms.Keys.X)
            Case ACTION_BUTTON_II : Return KeyboardLayout.KeyAt(KeyboardLayout.SCAN_Z, System.Windows.Forms.Keys.Z)
            Case ACTION_SELECT : Return System.Windows.Forms.Keys.ShiftKey
            Case ACTION_RUN : Return System.Windows.Forms.Keys.Return
            Case ACTION_PAUSE : Return System.Windows.Forms.Keys.P
            Case ACTION_RESET : Return System.Windows.Forms.Keys.R
            Case ACTION_SAVE_STATE : Return System.Windows.Forms.Keys.F5
            Case ACTION_LOAD_STATE : Return System.Windows.Forms.Keys.F8
            Case Else : Return System.Windows.Forms.Keys.None
        End Select
    End Function

    ''' <summary>Touche actuellement assignée à une action.</summary>
    Public Function BindingOf(action As String) As System.Windows.Forms.Keys
        Dim key As System.Windows.Forms.Keys
        If bindings.TryGetValue(action, key) Then Return key
        Return System.Windows.Forms.Keys.None
    End Function

    Public Sub HandleKeyDown(e As System.Windows.Forms.KeyEventArgs)
        SetKey(e.KeyCode, True)
    End Sub

    Public Sub HandleKeyUp(e As System.Windows.Forms.KeyEventArgs)
        SetKey(e.KeyCode, False)
    End Sub

    Private Sub SetKey(code As System.Windows.Forms.Keys, pressed As Boolean)
        ' Les deux touches Majuscule remontent parfois sous leur forme générique
        Dim normalized = code
        If code = System.Windows.Forms.Keys.LShiftKey OrElse code = System.Windows.Forms.Keys.RShiftKey Then
            normalized = System.Windows.Forms.Keys.ShiftKey
        End If

        For Each action In AllActions
            If bindings(action) = normalized OrElse bindings(action) = code Then
                keyState(action) = pressed
            End If
        Next
    End Sub

    ''' <summary>Applique l'état d'une manette, fusionné avec celui du clavier.</summary>
    Public Sub ApplyGamepad(state As System.Collections.Generic.Dictionary(Of String, Boolean))
        For Each action In AllActions
            padState(action) = state IsNot Nothing AndAlso
                               state.ContainsKey(action) AndAlso state(action)
        Next
    End Sub

    ''' <summary>État des boutons de la console, clavier et manette confondus.</summary>
    Public Function GetPadState() As System.Collections.Generic.Dictionary(Of String, Boolean)
        Dim result As New System.Collections.Generic.Dictionary(Of String, Boolean)
        For Each action In PadActions
            result(action) = keyState(action) OrElse padState(action)
        Next
        Return result
    End Function

    ''' <summary>Détecte l'instant où une action vient d'être déclenchée.</summary>
    Public Function IsActionPressed(action As String) As Boolean
        If Not keyState.ContainsKey(action) Then Return False

        Dim current = keyState(action) OrElse padState(action)
        Dim pressed = current AndAlso Not lastState(action)
        lastState(action) = current
        Return pressed
    End Function

    ''' <summary>Vrai si l'action est maintenue.</summary>
    Public Function IsActionHeld(action As String) As Boolean
        If Not keyState.ContainsKey(action) Then Return False
        Return keyState(action) OrElse padState(action)
    End Function

End Class
