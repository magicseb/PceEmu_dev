''' <summary>
''' Banc d'essai de la collision sprite 0 : pilote le VDC uniquement par ses
''' registres, comme le ferait un jeu, puis relit le registre d'état.
''' </summary>
Public Module CollisionSprite0Test

    Private Const PATTERN_BASE As Integer = &H100   ' Adresse VRAM du motif (en words)
    Private Const SATB_BASE As Integer = &H200      ' Adresse VRAM de la table d'attributs
    Private Const SPRITE_CODE As Integer = 8        ' (8 And &H7FE) << 5 = &H100

    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        Dim collided As Boolean, irq As Boolean

        RunScene(58, 20, 20, False, collided, irq)
        Check("sprites superposés -> collision", collided, True)

        RunScene(200, 20, 20, False, collided, irq)
        Check("sprites disjoints -> pas de collision", collided, False)

        RunScene(200, 20, 24, False, collided, irq)
        Check("superposés verticalement seulement -> pas de collision", collided, False)

        RunScene(58, 20, 20, True, collided, irq)
        Check("collision + IRQ activée -> IRQ levée", irq, True)

        RunScene(58, 20, 20, False, collided, irq)
        Check("collision + IRQ désactivée -> pas d'IRQ", irq, False)

        RunScene(58, 150, 20, False, collided, irq)
        Check("sprite 0 absent de la ligne -> pas de collision", collided, False)

        RunScene(50, 20, 20, False, collided, irq)
        Check("collision derrière un autre sprite -> détectée quand même", collided, True)

        Check("lecture de l'état -> drapeau effacé", ReadTwice(), False)

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    Private Sub Check(label As String, actual As Boolean, expected As Boolean)
        Dim ok = (actual = expected)
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label)
    End Sub

    ''' <summary>Monte une scène à deux sprites et relève l'état du VDC après rendu.</summary>
    Private Sub RunScene(spr1X As Integer, spr0Y As Integer, spr1Y As Integer,
                         collisionIrqEnabled As Boolean,
                         ByRef collided As Boolean, ByRef irq As Boolean)
        Dim vdc = BuildScene(spr0Y, spr1X, spr1Y, collisionIrqEnabled)
        ' L'ordre compte : lire le registre d'état relâche la ligne d'interruption
        irq = vdc.IrqLine
        collided = (vdc.Read(0) And &H1) <> 0
    End Sub

    ''' <summary>Vérifie que la lecture du registre d'état efface bien le drapeau.</summary>
    Private Function ReadTwice() As Boolean
        Dim vdc = BuildScene(20, 58, 20, False)
        vdc.Read(0)                              ' Première lecture : consomme le drapeau
        Return (vdc.Read(0) And &H1) <> 0
    End Function

    Private Function BuildScene(spr0Y As Integer, spr1X As Integer, spr1Y As Integer,
                                collisionIrqEnabled As Boolean) As Vdc
        Dim vce = New Vce()
        Dim vdc = New Vdc(vce)
        Dim framebuffer(PceConstants.SCREEN_WIDTH * PceConstants.SCREEN_HEIGHT - 1) As Integer

        ' Affichage 256×240, sprites activés, IRQ de collision optionnelle
        SetReg(vdc, 11, 31)     ' HDR : (31 + 1) × 8 = 256 pixels
        SetReg(vdc, 13, 239)    ' VDW : 239 + 1 = 240 lignes
        SetReg(vdc, 5, &H40 Or If(collisionIrqEnabled, &H1, &H0))

        ' Motif 16×16 entièrement opaque : le plan 0 suffit à rendre pix non nul
        SetReg(vdc, 0, PATTERN_BASE)   ' MAWR
        SelectReg(vdc, 2)              ' Port de données VRAM
        For row = 0 To 15
            WriteWord(vdc, &HFFFF)
        Next

        ' Table d'attributs : sprite 0, sprite 1, puis 62 entrées hors écran
        SetReg(vdc, 0, SATB_BASE)
        SelectReg(vdc, 2)
        WriteSprite(vdc, spr0Y, 50)
        WriteSprite(vdc, spr1Y, spr1X)
        For i = 2 To 63
            WriteSprite(vdc, -64, -32)
        Next

        SetReg(vdc, 19, SATB_BASE)     ' DVSSR : arme le transfert vers la SATB

        ' Le transfert SATB a lieu au VBlank, donc après le rendu de la frame :
        ' les sprites n'apparaissent qu'à la frame suivante
        RunFrame(vdc, framebuffer)
        RunFrame(vdc, framebuffer)

        Return vdc
    End Function

    Private Sub RunFrame(vdc As Vdc, framebuffer() As Integer)
        ' Le framebuffer n'est plus utilisé : le VDC écrit dans sa propre ligne de sortie
        For line = 0 To PceConstants.SCANLINES_PER_FRAME - 1
            vdc.DoScanline(line)
        Next
    End Sub

    Private Sub SelectReg(vdc As Vdc, index As Integer)
        vdc.Write(0, index)
    End Sub

    Private Sub SetReg(vdc As Vdc, index As Integer, value As Integer)
        SelectReg(vdc, index)
        vdc.Write(2, value And &HFF)
        vdc.Write(3, (value >> 8) And &HFF)
    End Sub

    Private Sub WriteWord(vdc As Vdc, word As Integer)
        vdc.Write(2, word And &HFF)
        vdc.Write(3, (word >> 8) And &HFF)
    End Sub

    ''' <summary>Une entrée SATB : Y décalé de 64, X décalé de 32, code du motif, attributs.</summary>
    Private Sub WriteSprite(vdc As Vdc, y As Integer, x As Integer)
        WriteWord(vdc, (y + 64) And &H3FF)
        WriteWord(vdc, (x + 32) And &H3FF)
        WriteWord(vdc, SPRITE_CODE)
        WriteWord(vdc, 0)
    End Sub

End Module
