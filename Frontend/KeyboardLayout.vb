''' <summary>
''' Traduit une position physique de touche en touche logique, selon la disposition
''' clavier active.
'''
''' Plutôt que de tester si l'utilisateur est en français, on demande à Windows quelle
''' touche se trouve à un emplacement donné. La touche sous l'annulaire gauche est
''' « Z » en QWERTY, « W » en AZERTY et « Y » en QWERTZ : les trois dispositions sont
''' donc gérées sans les énumérer, et un clavier exotique le sera aussi.
''' </summary>
Public Class KeyboardLayout

    ''' <summary>Convertit un code de balayage en touche virtuelle de la disposition active.</summary>
    Private Const MAPVK_VSC_TO_VK As UInteger = 1

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function MapVirtualKey(uCode As UInteger, uMapType As UInteger) As UInteger
    End Function

    ' Codes de balayage : ils désignent un emplacement sur le clavier, jamais une lettre
    Public Const SCAN_Z As UInteger = &H2C     ' Z en QWERTY, W en AZERTY, Y en QWERTZ
    Public Const SCAN_X As UInteger = &H2D     ' X sur la plupart des dispositions

    ''' <summary>
    ''' Touche présente à un emplacement physique donné. Retourne la valeur de repli
    ''' si Windows ne sait pas répondre — cas d'une disposition sans cette position.
    ''' </summary>
    Public Shared Function KeyAt(scanCode As UInteger, fallback As System.Windows.Forms.Keys) As System.Windows.Forms.Keys
        Try
            Dim virtualKey = MapVirtualKey(scanCode, MAPVK_VSC_TO_VK)
            If virtualKey = 0 Then Return fallback
            Return CType(virtualKey, System.Windows.Forms.Keys)
        Catch ex As Exception
            ' Pas de user32 (exécution hors Windows) : on garde la disposition QWERTY
            Return fallback
        End Try
    End Function

    ''' <summary>Nom lisible de la disposition active, pour l'afficher à l'utilisateur.</summary>
    Public Shared Function Describe() As String
        Try
            Return System.Windows.Forms.InputLanguage.CurrentInputLanguage.LayoutName
        Catch ex As Exception
            Return "inconnue"
        End Try
    End Function

End Class
