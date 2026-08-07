''' <summary>
''' Banc d'essai : handler timer à l'idiome « CLI puis acquitter » (sans passer
''' par le masquage $1402).
'''
''' Certains jeux (Dungeons &amp; Dragons: Order of the Griffon, etc.) écrivent leur
''' handler timer ainsi :
'''   PHA/PHX/PHY ; CLI ; ACK ($1403) ; travail ; RTI
''' Le CLI ré-autorise les interruptions AVANT l'acquittement. Cela ne fonctionne
''' que grâce au délai d'un cran de reconnaissance d'IRQ du 6502/HuC6280 : après
''' un CLI, l'instruction suivante (ici l'ACK) s'exécute avant que l'IRQ ne soit
''' reprise. Sans ce délai sur CLI, l'IRQ est reprise juste après le CLI, avant
''' l'ACK : le handler se ré-entre sans fin (storm), l'ack n'est jamais atteint et
''' l'écran se fige — c'est le symptôme observé sur Order of the Griffon.
'''
''' La ROM synthétique ci-dessous reproduit cet idiome : le handler n'avance la
''' couleur de fond QU'APRÈS l'ack. En cas de storm, l'ack n'est jamais atteint →
''' fond figé. Le test vérifie donc que l'image PROGRESSE.
'''
''' Garde-fou : sans le correctif (délai sur CLI), l'image reste figée et ce test
''' échoue (vérifié par mutation).
''' </summary>
Public Module TimerIrqAckCliTest
    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        Console.WriteLine("Banc acquittement IRQ timer (idiome CLI puis ACK)")

        Dim romPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_timerack_cli.pce")
        System.IO.File.WriteAllBytes(romPath, BuildRom())

        Dim sys = New PceSystem(romPath, False)
        Dim s0 = Snap(sys, 5)
        Dim s1 = Snap(sys, 20)
        Dim s2 = Snap(sys, 20)
        Dim s3 = Snap(sys, 20)

        Check("l'image progresse (pas de storm d'IRQ)", s1 <> s2 OrElse s2 <> s3)
        Check("la couleur de fond a évolué depuis l'amorçage", s3 <> s0)
        Dim a = Snap(sys, 40)
        Dim b = Snap(sys, 40)
        Check("progression maintenue dans la durée", a <> b)

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    Private Sub Check(label As String, ok As Boolean)
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label)
    End Sub

    Private Function Snap(sys As PceSystem, n As Integer) As String
        For i = 1 To n
            sys.RunFrame()
            sys.GetAudioSamples()
        Next
        Dim fb = sys.GetFramebuffer()
        Dim bytes(fb.Length * 4 - 1) As Byte
        Buffer.BlockCopy(fb, 0, bytes, 0, bytes.Length)
        Using md5 = System.Security.Cryptography.MD5.Create()
            Return Convert.ToHexString(md5.ComputeHash(bytes))
        End Using
    End Function

    Private Function BuildRom() As Byte()
        Dim main As Byte() = {
            &H78,                       ' SEI
            &HD4,                       ' CSH
            &HA9, &HFF, &H53, &H1,      ' LDA #$FF : TAM #$01  -> MPR0 = matériel
            &HA9, &HF8, &H53, &H2,      ' LDA #$F8 : TAM #$02  -> MPR1 = RAM
            &HA2, &HFF, &H9A,           ' LDX #$FF : TXS
            &HA9, &HB, &H8D, &H0, &H0,  ' LDA #$0B : STA $0000  (HDR)
            &HA9, &H1F, &H8D, &H2, &H0, ' LDA #$1F : STA $0002  (largeur 256)
            &H9C, &H3, &H0,             ' STZ $0003
            &HA9, &HD, &H8D, &H0, &H0,  ' LDA #$0D : STA $0000  (VDW)
            &HA9, &HEF, &H8D, &H2, &H0, ' LDA #$EF : STA $0002  (240 lignes)
            &H9C, &H3, &H0,             ' STZ $0003
            &H9C, &H0, &H0,             ' STZ $0000  (MAWR)
            &H9C, &H2, &H0, &H9C, &H3, &H0, ' STZ $0002 : STZ $0003  (addr VRAM=0)
            &HA9, &H2, &H8D, &H0, &H0,  ' LDA #$02 : STA $0000  (port données VRAM)
            &HA9, &H1, &H8D, &H0, &HC,  ' LDA #$01 : STA $0C00  (timer reload=1)
            &HA9, &H1, &H8D, &H1, &HC,  ' LDA #$01 : STA $0C01  (timer enable)
            &HA9, &H2, &H8D, &H2, &H14, ' LDA #$02 : STA $1402  (IRQ1 masquée, timer autorisé)
            &HA9, &H0, &H85, &H10,      ' LDA #$00 : STA $10    (compteur handler = 0)
            &H58,                       ' CLI
            &H80, &HFE                  ' BRA *  (boucle principale)
        }
        ' Handler à l'idiome D&D : CLI TÔT, puis ACK. Repose sur le délai du CLI.
        Dim handler As Byte() = {
            &H48,                       ' PHA
            &H58,                       ' CLI        (ré-autorise AVANT l'ack)   <-- point critique
            &H9C, &H3, &H14,            ' STZ $1403  (ACQUITTE)  <-- doit passer grâce au délai CLI
            &HE6, &H10,                 ' INC $10    (handler terminé)
            &H9C, &H2, &H4, &H9C, &H3, &H4, ' STZ $0402 : STZ $0403  (addr palette=0)
            &HA5, &H10,                 ' LDA $10
            &H8D, &H4, &H4,            ' STA $0404  (palette poids faible = compteur)
            &H8D, &H5, &H4,            ' STA $0405  (écrit le mot)
            &H68,                       ' PLA
            &H40                        ' RTI
        }
        Dim halt As Byte() = {&H80, &HFE}   ' BRA *  (piège sûr)

        Dim rom(8191) As Byte
        Array.Copy(main, 0, rom, 0, main.Length)
        Array.Copy(handler, 0, rom, &H100, handler.Length)  ' $E100
        Array.Copy(halt, 0, rom, &H1F0, halt.Length)        ' $E1F0
        rom(&H1FF6) = &HF0 : rom(&H1FF7) = &HE1   ' IRQ2 -> $E1F0
        rom(&H1FF8) = &HF0 : rom(&H1FF9) = &HE1   ' IRQ1 -> $E1F0
        rom(&H1FFA) = &H0  : rom(&H1FFB) = &HE1   ' TIMER -> $E100
        rom(&H1FFE) = &H0  : rom(&H1FFF) = &HE0   ' RESET -> $E000
        Return rom
    End Function
End Module
