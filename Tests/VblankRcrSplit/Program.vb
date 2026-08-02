''' <summary>
''' Banc d'essai : coïncidence RCR (comparaison raster) / VBlank sur la même scanline.
'''
''' Beaucoup de jeux (Air Zonk, etc.) écrivent un handler d'IRQ VDC de la forme
''' « lire status ; si bit RCR -> traiter le split raster ET RETOURNER ; sinon si bit
''' VBlank -> traiter la VBlank ». Ce handler suppose que RCR et VBlank arrivent comme
''' DEUX interruptions distinctes. Sur le matériel, la comparaison raster (milieu de
''' ligne) et la VBlank (fin de ligne) sont séparées par des cycles CPU ; le CPU sert
''' et acquitte le RCR avant que la VBlank ne soit assertée.
'''
''' Si l'émulateur pose les deux bits de status d'un coup (RCR programmé pile sur la
''' ligne de VBlank), une seule lecture voit les deux bits, le handler traite le RCR et
''' RATE la VBlank -> le jeu, qui attend un flag posé par la VBlank, se fige.
'''
''' Le test vérifie qu'un RCR sur la ligne de VBlank produit malgré tout une VBlank
''' LISIBLE SEULE (status = VBlank sans RCR), donc servable séparément.
'''
''' Garde-fou : sans le correctif (VBlank différée d'une scanline), les deux bits
''' fusionnent et aucune lecture ne montre la VBlank seule -> ce test échoue (vérifié
''' par mutation).
''' </summary>
Public Module VblankRcrSplitTest
    Private Const ST_RR As Integer = &H4
    Private Const ST_VD As Integer = &H20
    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        Console.WriteLine("Banc coïncidence RCR / VBlank")
        Const H As Integer = 240        ' hauteur d'affichage

        ' --- Cas coïncident : RCR programmé sur la ligne de VBlank (scanline H) ---
        Dim reads = SimFrame(H, rcrLine:=H)
        Dim vblankSeul = HasStandalone(reads, ST_VD, ST_RR)
        Dim rcrVu = AnySet(reads, ST_RR)
        Check("RCR sur la ligne de VBlank : la VBlank reste lisible seule", vblankSeul)
        Check("le RCR coïncident se déclenche bien", rcrVu)
        Check("la VBlank n'est pas perdue (au moins une VBlank vue)", AnySet(reads, ST_VD))

        ' --- Cas témoin : RCR loin de la VBlank (ligne 100) — cas normal, inchangé ---
        Dim reads2 = SimFrame(H, rcrLine:=100)
        Check("cas normal : RCR seul à sa ligne", HasStandalone(reads2, ST_RR, ST_VD))
        Check("cas normal : VBlank seule à sa ligne", HasStandalone(reads2, ST_VD, ST_RR))

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    ''' <summary>
    ''' Simule une frame : configure le VDC (hauteur H, IRQ RCR+VBlank activées, RCR sur
    ''' rcrLine) puis, pour chaque scanline, appelle DoScanline et LIT le status (ce qui
    ''' l'acquitte, comme le ferait le CPU en servant l'IRQ). Retourne la liste des status lus.
    ''' </summary>
    Private Function SimFrame(dispHeight As Integer, rcrLine As Integer) As System.Collections.Generic.List(Of Integer)
        Dim vce = New Vce()
        Dim vdc = New Vdc(vce)
        SetReg(vdc, 13, dispHeight - 1)         ' VDW -> DisplayHeight = dispHeight
        SetReg(vdc, 5, &HC)                      ' CR : bit2 (RCR IRQ) + bit3 (VBlank IRQ)
        SetReg(vdc, 6, rcrLine + 64)             ' RCR : se déclenche à la scanline rcrLine
        Dim res = New System.Collections.Generic.List(Of Integer)
        For scan = 0 To dispHeight + 3
            vdc.DoScanline(scan)
            res.Add(vdc.Read(0))                 ' le CPU lit/acquitte le status
        Next
        Return res
    End Function

    Private Sub SetReg(vdc As Vdc, reg As Integer, value As Integer)
        vdc.Write(0, reg)
        vdc.Write(2, value And &HFF)
        vdc.Write(3, (value >> 8) And &HFF)
    End Sub

    ''' <summary>Vrai si une lecture a le bit 'want' posé ET le bit 'without' absent.</summary>
    Private Function HasStandalone(reads As System.Collections.Generic.List(Of Integer), want As Integer, without As Integer) As Boolean
        For Each st In reads
            If (st And want) <> 0 AndAlso (st And without) = 0 Then Return True
        Next
        Return False
    End Function

    Private Function AnySet(reads As System.Collections.Generic.List(Of Integer), bit As Integer) As Boolean
        For Each st In reads
            If (st And bit) <> 0 Then Return True
        Next
        Return False
    End Function

    Private Sub Check(label As String, ok As Boolean)
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label)
    End Sub
End Module
