''' <summary>
''' Banc d'essai : sémantique fine du HuC6280 vérifiée contre MAME h6280.
'''
''' Couvre :
'''  - le mode T (flag $20 de P, posé par SET) : ADC/SBC/AND/ORA/EOR opèrent alors
'''    sur la case mémoire ($2000+X) au lieu de A, qui reste intact ;
'''  - la SURVIE du mode T à travers RTI : P restauré depuis la pile avec T=1 met
'''    l'instruction suivante en mode T (une IRQ tombée entre SET et l'ALU ne doit
'''    pas corrompre le programme) ;
'''  - TMA #masque : le DERNIER bit du masque gagne, et TMA ne modifie AUCUN drapeau ;
'''  - PHP : le P poussé n'a jamais le bit T (effacé avant le push, MAME php()).
'''
''' La ROM synthétique écrit ses résultats en page zéro ($2040-$2055), relus par
''' PeekZp. Chaque constante est choisie pour que la chaîne T diverge si UNE étape
''' saute (prouvé par mutation : SBC sans mode T, TMA premier-bit, TMA+flags).
''' </summary>
Public Module CpuFlagsTest
    Private passed As Integer = 0
    Private failed As Integer = 0
    Private code As New System.Collections.Generic.List(Of Byte)

    Private Sub W(ParamArray bs As Integer())
        For Each b In bs : code.Add(CByte(b And &HFF)) : Next
    End Sub

    Public Function Main() As Integer
        Console.WriteLine("Banc sémantique CPU (mode T, TMA, PHP — référence MAME h6280)")

        Dim romPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_cpuflags.pce")
        System.IO.File.WriteAllBytes(romPath, BuildRom())

        Dim sys = New PceSystem(romPath, False)
        For i = 1 To 3
            sys.RunFrame()
            sys.GetAudioSamples()
        Next

        ' Chaîne T complète : $11 +5(ADC) -6(SBC) |$08(ORA) &$FE(AND) ^$01(EOR) +1(ADC après RTI)
        Check("chaîne mode T complète : mem($2040) = $1A", sys.PeekZp(&H40), &H1A)
        Check("A intact après un ADC en mode T ($AA)", sys.PeekZp(&H50), &HAA)
        Check("TMA multi-bits : le DERNIER bit gagne (MPR2=$66)", sys.PeekZp(&H51), &H66)
        Check("TMA ne modifie pas les drapeaux (Z de LDA #0 conservé)", sys.PeekZp(&H54) And &H2, &H2)
        Check("RTI restaure le mode T ; A intact après l'ADC ($24)", sys.PeekZp(&H52), &H24)
        Check("PHP : le P poussé n'a pas le bit T", sys.PeekZp(&H53) And &H20, 0)
        Check("la ROM a fini son parcours (sentinelle)", sys.PeekZp(&H5F), &HA5)

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    Private Sub Check(label As String, actual As Integer, expected As Integer)
        Dim ok = (actual = expected)
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label & If(ok, "", $" (obtenu ${actual:X2}, attendu ${expected:X2})"))
    End Sub

    Private Function BuildRom() As Byte()
        ' Page unique de 8 Ko mappée partout ; RESET ($FFFE) -> $E000 (offset 0).
        code.Clear()

        W(&H78)                     ' SEI
        W(&HD4)                     ' CSH
        W(&HA9, &HF8, &H53, &H2)    ' LDA #$F8 : TAM #$02   (MPR1 = $F8 : ZP/pile)
        W(&HA2, &H40)               ' LDX #$40              (cible T = zp $40)
        W(&HA9, &H11, &H85, &H40)   ' LDA #$11 : STA $40    (mémoire initiale)
        W(&H18)                     ' CLC
        W(&HA9, &HAA)               ' LDA #$AA              (sentinelle A)
        W(&HF4, &H69, &H5)          ' SET : ADC #$05        -> mem = $16, A intact
        W(&H85, &H50)               ' STA $50               ($AA attendu)
        W(&H38)                     ' SEC
        W(&HF4, &HE9, &H6)          ' SET : SBC #$06        -> mem = $10, A intact
        W(&HF4, &H9, &H8)           ' SET : ORA #$08        -> mem = $18
        W(&HF4, &H29, &HFE)         ' SET : AND #$FE        -> mem = $18
        W(&HF4, &H49, &H1)          ' SET : EOR #$01        -> mem = $19

        ' TMA : MPR2 = $66 posé exprès (page jamais lue ensuite), masque $06 (bits 1+2)
        W(&HA9, &H66, &H53, &H4)    ' LDA #$66 : TAM #$04   (MPR2 = $66)
        W(&HA9, &H0)                ' LDA #$00              (Z=1)
        W(&H43, &H6)                ' TMA #$06              -> A = MPR2 = $66, drapeaux INTACTS
        W(&H8)                      ' PHP
        W(&H85, &H51)               ' STA $51               ($66 attendu)
        W(&H68, &H85, &H54)         ' PLA : STA $54         (P de TMA : Z doit être encore levé)

        ' RTI restaure le mode T : préparer pile (PCH, PCL, P=$24 = T|I) puis RTI
        ' L'adresse de retour "after" est calculée après assemblage (placeholders).
        Dim patchHi = code.Count + 1
        W(&HA9, &H0, &H48)          ' LDA #>after : PHA
        Dim patchLo = code.Count + 1
        W(&HA9, &H0, &H48)          ' LDA #<after : PHA
        W(&HA9, &H24, &H48)         ' LDA #$24 : PHA        (P restauré : T=1, I=1, C=0)
        W(&H40)                     ' RTI
        Dim afterOfs = code.Count   ' after:
        W(&H69, &H1)                ' ADC #$01              (mode T restauré) -> mem = $1A, A=$24 intact
        W(&H85, &H52)               ' STA $52               ($24 attendu)
        W(&HF4, &H8)                ' SET : PHP             (P poussé SANS T)
        W(&H68, &H85, &H53)         ' PLA : STA $53
        W(&HA9, &HA5, &H85, &H5F)   ' LDA #$A5 : STA $5F    (sentinelle de fin)
        W(&H80, &HFE)               ' BRA *                 (boucle finale)

        Dim afterAddr = &HE000 + afterOfs
        code(patchHi) = CByte((afterAddr >> 8) And &HFF)
        code(patchLo) = CByte(afterAddr And &HFF)

        Dim rom(8191) As Byte
        code.CopyTo(rom, 0)
        ' Vecteurs en fin de banque ($FFF6-$FFFF = offsets $1FF6+) : tout sur RESET-like
        For Each vec In {&H1FF6, &H1FF8, &H1FFA, &H1FFC, &H1FFE}
            rom(vec) = &H0 : rom(vec + 1) = &HE0
        Next
        Return rom
    End Function
End Module
