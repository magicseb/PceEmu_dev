''' <summary>
''' Lecture d'une manette Xbox par XInput.
'''
''' L'appel se fait directement dans la bibliothèque système, sans dépendance
''' supplémentaire. Deux versions de la DLL cohabitent selon l'âge de Windows : on
''' essaie la plus récente puis on retombe sur l'ancienne. Si aucune n'est présente
''' — machine sans XInput, ou exécution hors Windows — la manette est simplement
''' considérée comme absente et le clavier continue de fonctionner.
''' </summary>
Public Class GamepadInput

    <System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)>
    Private Structure XInputGamepad
        Public Buttons As UShort
        Public LeftTrigger As Byte
        Public RightTrigger As Byte
        Public ThumbLX As Short
        Public ThumbLY As Short
        Public ThumbRX As Short
        Public ThumbRY As Short
    End Structure

    <System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)>
    Private Structure XInputState
        Public PacketNumber As UInteger
        Public Gamepad As XInputGamepad
    End Structure

    <System.Runtime.InteropServices.DllImport("xinput1_4.dll", EntryPoint:="XInputGetState")>
    Private Shared Function XInputGetStateModern(userIndex As Integer, ByRef state As XInputState) As Integer
    End Function

    <System.Runtime.InteropServices.DllImport("xinput9_1_0.dll", EntryPoint:="XInputGetState")>
    Private Shared Function XInputGetStateLegacy(userIndex As Integer, ByRef state As XInputState) As Integer
    End Function

    ' Masques des boutons, tels que définis par XInput
    Private Const DPAD_UP As UShort = &H1
    Private Const DPAD_DOWN As UShort = &H2
    Private Const DPAD_LEFT As UShort = &H4
    Private Const DPAD_RIGHT As UShort = &H8
    Private Const BTN_START As UShort = &H10
    Private Const BTN_BACK As UShort = &H20
    Private Const BTN_A As UShort = &H1000
    Private Const BTN_B As UShort = &H2000
    Private Const BTN_X As UShort = &H4000
    Private Const BTN_Y As UShort = &H8000US
    Private Const BTN_LB As UShort = &H100
    Private Const BTN_RB As UShort = &H200

    ''' <summary>Gâchette analogique considérée pressée au-delà de ce seuil (0-255).</summary>
    Private Const TRIGGER_TH As Byte = 60

    ''' <summary>État brut des commandes du menu rapide manette (LB+RT, croix, A, B, X, Y).</summary>
    Public Structure MenuState
        Public Toggle As Boolean   ' LB + RT ensemble : ouvre/ferme le menu
        Public Up As Boolean
        Public Down As Boolean
        Public Left As Boolean
        Public Right As Boolean
        Public Accept As Boolean   ' A
        Public Back As Boolean     ' B
        Public Check As Boolean    ' Y : cocher/décocher (téléchargements multiples)
        Public Batch As Boolean    ' X : lancer le téléchargement des jeux cochés
    End Structure

    ''' <summary>Dernier état brut lu par Poll() (sert au menu rapide).</summary>
    Private lastRaw As XInputGamepad

    Private Const ERROR_SUCCESS As Integer = 0

    ''' <summary>Au-delà de ce seuil, le stick est considéré comme poussé.</summary>
    Private Const THUMB_DEADZONE As Short = 12000

    ''' <summary>Quelle DLL utiliser : déterminé au premier appel réussi.</summary>
    Private Enum Backend
        Unknown
        Modern
        Legacy
        Unavailable
    End Enum

    Private backendInUse As Backend = Backend.Unknown

    ''' <summary>Vrai si une manette a répondu lors de la dernière lecture.</summary>
    Public ReadOnly Property Connected As Boolean

    ''' <summary>
    ''' Lit la première manette branchée et retourne l'état des actions.
    ''' Retourne Nothing si aucune manette n'est disponible.
    ''' </summary>
    Public Function Poll() As System.Collections.Generic.Dictionary(Of String, Boolean)
        Dim state As XInputState = Nothing

        If Not TryGetState(state) Then
            _Connected = False
            lastRaw = New XInputGamepad()
            Return Nothing
        End If

        _Connected = True
        lastRaw = state.Gamepad
        Dim buttons = state.Gamepad.Buttons
        Dim result As New System.Collections.Generic.Dictionary(Of String, Boolean)

        ' La croix et le stick gauche commandent tous deux les directions
        result(InputManager.ACTION_UP) = Has(buttons, DPAD_UP) OrElse state.Gamepad.ThumbLY > THUMB_DEADZONE
        result(InputManager.ACTION_DOWN) = Has(buttons, DPAD_DOWN) OrElse state.Gamepad.ThumbLY < -THUMB_DEADZONE
        result(InputManager.ACTION_LEFT) = Has(buttons, DPAD_LEFT) OrElse state.Gamepad.ThumbLX < -THUMB_DEADZONE
        result(InputManager.ACTION_RIGHT) = Has(buttons, DPAD_RIGHT) OrElse state.Gamepad.ThumbLX > THUMB_DEADZONE

        ' La PC Engine n'a que deux boutons : A et B les portent, X et Y les doublent
        result(InputManager.ACTION_BUTTON_I) = Has(buttons, BTN_A) OrElse Has(buttons, BTN_X)
        result(InputManager.ACTION_BUTTON_II) = Has(buttons, BTN_B) OrElse Has(buttons, BTN_Y)
        result(InputManager.ACTION_RUN) = Has(buttons, BTN_START)
        result(InputManager.ACTION_SELECT) = Has(buttons, BTN_BACK)

        Return result
    End Function

    Private Shared Function Has(buttons As UShort, mask As UShort) As Boolean
        Return (buttons And mask) <> 0
    End Function

    ''' <summary>État des commandes du menu rapide, dérivé de la dernière lecture de Poll().
    ''' Appeler Poll() d'abord à chaque frame pour rafraîchir l'état brut.</summary>
    Public Function ReadMenu() As MenuState
        Dim g = lastRaw
        Dim m As MenuState
        m.Toggle = Has(g.Buttons, BTN_LB) AndAlso g.RightTrigger > TRIGGER_TH
        m.Up = Has(g.Buttons, DPAD_UP) OrElse g.ThumbLY > THUMB_DEADZONE
        m.Down = Has(g.Buttons, DPAD_DOWN) OrElse g.ThumbLY < -THUMB_DEADZONE
        m.Left = Has(g.Buttons, DPAD_LEFT) OrElse g.ThumbLX < -THUMB_DEADZONE
        m.Right = Has(g.Buttons, DPAD_RIGHT) OrElse g.ThumbLX > THUMB_DEADZONE
        m.Accept = Has(g.Buttons, BTN_A)
        m.Back = Has(g.Buttons, BTN_B)
        m.Check = Has(g.Buttons, BTN_Y)
        m.Batch = Has(g.Buttons, BTN_X)
        Return m
    End Function

    ''' <summary>Interroge la manette 0, en retenant la DLL qui a répondu.</summary>
    Private Function TryGetState(ByRef state As XInputState) As Boolean
        Select Case backendInUse
            Case Backend.Unavailable
                Return False

            Case Backend.Modern
                Return CallSafely(AddressOf XInputGetStateModern, state)

            Case Backend.Legacy
                Return CallSafely(AddressOf XInputGetStateLegacy, state)

            Case Else
                If CallSafely(AddressOf XInputGetStateModern, state) Then
                    backendInUse = Backend.Modern
                    Return True
                End If
                If CallSafely(AddressOf XInputGetStateLegacy, state) Then
                    backendInUse = Backend.Legacy
                    Return True
                End If
                ' Aucune manette ou aucune DLL : inutile de réessayer à chaque frame
                backendInUse = Backend.Unavailable
                Return False
        End Select
    End Function

    Private Delegate Function GetStateCall(userIndex As Integer, ByRef state As XInputState) As Integer

    Private Shared Function CallSafely(call_ As GetStateCall, ByRef state As XInputState) As Boolean
        Try
            Return call_(0, state) = ERROR_SUCCESS
        Catch ex As Exception
            ' DllNotFoundException, EntryPointNotFoundException… : pas de XInput ici
            Return False
        End Try
    End Function

    ''' <summary>Permet de retenter la détection après avoir branché une manette.</summary>
    Public Sub Rescan()
        backendInUse = Backend.Unknown
    End Sub

End Class
