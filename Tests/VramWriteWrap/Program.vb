''' <summary>
''' Banc d'essai du bord haut de la VRAM. Le HuC6270 n'adresse que 32 K mots
''' ($0000-$7FFF) : quand l'adresse d'écriture MAWR déborde au-delà de $7FFF,
''' l'écriture doit être IGNORÉE (VRAM inexistante), et surtout PAS repliée sur
''' $0000. Certains jeux effacent un bloc volontairement sur-dimensionné en
''' partant d'une adresse haute (Turrican : clear de 16384 mots depuis $4400) en
''' comptant sur ce rejet ; un repli sur $0000 écraserait la BAT et donnerait un
''' écran noir. Ce banc pilote le VDC uniquement par ses registres.
''' </summary>
Public Module VramWriteWrapTest

    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        Dim vce = New Vce()
        Dim vdc = New Vdc(vce)

        ' Repère connu en tête de VRAM (la « BAT ») pour détecter un écrasement.
        WriteAt(vdc, &H0, &HBEEF)
        WriteAt(vdc, &H1, &HCAFE)

        ' Écriture séquentielle chevauchant le bord : $7FFE, $7FFF, puis $8000,
        ' $8001 (hors limites). MAWR s'incrémente de 1 word.
        SetReg(vdc, 0, &H7FFE)          ' MAWR
        SelectReg(vdc, 2)               ' port données VRAM (VWR)
        WriteWord(vdc, &H1111)          ' -> $7FFE
        WriteWord(vdc, &H2222)          ' -> $7FFF
        WriteWord(vdc, &H3333)          ' -> $8000 : doit être jeté
        WriteWord(vdc, &H4444)          ' -> $8001 : doit être jeté

        Check("mot en $7FFE écrit", ReadAt(vdc, &H7FFE), &H1111)
        Check("mot en $7FFF écrit", ReadAt(vdc, &H7FFF), &H2222)
        Check("débordement n'écrase pas $0000 (BAT)", ReadAt(vdc, &H0), &HBEEF)
        Check("débordement n'écrase pas $0001 (BAT)", ReadAt(vdc, &H1), &HCAFE)

        ' Reproduction fidèle du clear Turrican : 16384 mots à zéro depuis $4400.
        ' 0x3C00 mots atteignent $7FFF, les 0x400 suivants débordent : ils NE
        ' doivent PAS effacer $0000-$03FF. On repose d'abord un repère en $0000.
        WriteAt(vdc, &H0, &H0440)
        WriteAt(vdc, &H3FF, &H07FF)
        SetReg(vdc, 0, &H4400)
        SelectReg(vdc, 2)
        For i = 0 To 16383
            WriteWord(vdc, &H0)
        Next
        Check("clear sur-dimensionné : $0000 préservé", ReadAt(vdc, &H0), &H0440)
        Check("clear sur-dimensionné : $03FF préservé", ReadAt(vdc, &H3FF), &H07FF)
        Check("clear sur-dimensionné : $7FFF bien effacé", ReadAt(vdc, &H7FFF), &H0)
        Check("clear sur-dimensionné : $4400 bien effacé", ReadAt(vdc, &H4400), &H0)

        ' Garde-fou : dans les limites, l'écriture fonctionne normalement.
        WriteAt(vdc, &H1234, &HABCD)
        Check("écriture normale en limites", ReadAt(vdc, &H1234), &HABCD)

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    Private Sub Check(label As String, actual As Integer, expected As Integer)
        Dim ok = (actual = expected)
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label &
                          If(ok, "", "  (attendu $" & Hex(expected) & ", obtenu $" & Hex(actual) & ")"))
    End Sub

    Private Sub SelectReg(vdc As Vdc, index As Integer)
        vdc.Write(0, index)
    End Sub

    Private Sub SetReg(vdc As Vdc, index As Integer, value As Integer)
        SelectReg(vdc, index)
        vdc.Write(2, value And &HFF)
        vdc.Write(3, (value >> 8) And &HFF)
    End Sub

    Private Sub WriteWord(vdc As Vdc, value As Integer)
        vdc.Write(2, value And &HFF)
        vdc.Write(3, (value >> 8) And &HFF)
    End Sub

    ''' <summary>Écrit un mot à une adresse VRAM précise (positionne MAWR puis VWR).</summary>
    Private Sub WriteAt(vdc As Vdc, addr As Integer, value As Integer)
        SetReg(vdc, 0, addr)
        SelectReg(vdc, 2)
        WriteWord(vdc, value)
    End Sub

    ''' <summary>
    ''' Lit un mot VRAM. MARR est écrit MSB d'abord puis LSB : l'écriture du LSB
    ''' déclenche le préchargement de readBuffer avec MARR complet.
    ''' </summary>
    Private Function ReadAt(vdc As Vdc, addr As Integer) As Integer
        SelectReg(vdc, 1)                       ' MARR
        vdc.Write(3, (addr >> 8) And &HFF)      ' MSB (pas de préchargement)
        vdc.Write(2, addr And &HFF)             ' LSB -> readBuffer = vram(addr)
        Return vdc.Read(2) Or (vdc.Read(3) << 8)
    End Function

End Module
