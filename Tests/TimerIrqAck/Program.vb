''' <summary>
''' Banc d'essai : acquittement d'IRQ timer et idiome « ré-activer puis acquitter ».
'''
''' Beaucoup de jeux (After Burner II, etc.) écrivent leur handler timer ainsi :
'''   ... masquer l'IRQ ($1402|=4) ; CLI ; travail ; DÉMASQUER ($1402&=~4) ; ACK ($1403) ; RTI
''' Cela suppose que l'instruction d'acquittement s'exécute AVANT que l'IRQ ré-autorisée
''' ne soit reprise — c'est le délai d'un cran de reconnaissance d'interruption du 6502.
''' Sans ce délai, l'IRQ est reprise juste avant l'ack : le handler se ré-entre sans fin,
''' la pile déborde, le CPU part en vrille (BRK) et l'écran se fige.
'''
''' La ROM synthétique ci-dessous reproduit exactement cet idiome. Le handler n'avance
''' la couleur de fond (palette 0) QU'APRÈS l'ack. En cas de storm, l'ack n'est jamais
''' atteint → fond figé. Le test vérifie donc que l'image PROGRESSE.
'''
''' Garde-fou : sans le correctif (délai $1402), l'image reste figée et ce test échoue
''' (vérifié par mutation).
''' </summary>
Public Module TimerIrqAckTest
    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        Console.WriteLine("Banc acquittement IRQ timer (idiome ré-activer→acquitter)")

        Dim romPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_timerack.pce")
        System.IO.File.WriteAllBytes(romPath, BuildRom())

        Dim sys = New PceSystem(romPath, False)
        Dim s0 = Snap(sys, 5)      ' après amorçage
        Dim s1 = Snap(sys, 20)
        Dim s2 = Snap(sys, 20)
        Dim s3 = Snap(sys, 20)

        ' 1) L'image progresse (le handler atteint son ack et se termine à répétition)
        Check("l'image progresse (pas de storm d'IRQ)", s1 <> s2 OrElse s2 <> s3)
        ' 2) Elle a bien quitté l'état d'amorçage (le handler tourne réellement)
        Check("la couleur de fond a évolué depuis l'amorçage", s3 <> s0)
        ' 3) Stabilité : sur une longue durée, l'image continue de changer
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

    ''' <summary>Avance de n frames puis retourne une empreinte de l'image.</summary>
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
            &H80, &HFE                  ' BRA *  (boucle principale : ne fait rien)
        }
        Dim handler As Byte() = {
            &H48,                       ' PHA
            &HAD, &H2, &H14,            ' LDA $1402
            &H9, &H4,                   ' ORA #$04   (masque le timer)
            &H8D, &H2, &H14,            ' STA $1402
            &H58,                       ' CLI        (ré-autorise les interruptions)
            &HAD, &H2, &H14,            ' LDA $1402
            &H29, &HFB,                 ' AND #$FB   (DÉMASQUE le timer)  <-- point critique
            &H8D, &H2, &H14,            ' STA $1402
            &H9C, &H3, &H14,            ' STZ $1403  (ACQUITTE)          <-- doit passer avant re-IRQ
            &HE6, &H10,                 ' INC $10    (handler terminé : avance le compteur)
            &H9C, &H2, &H4, &H9C, &H3, &H4, ' STZ $0402 : STZ $0403  (addr palette=0)
            &HA5, &H10,                 ' LDA $10
            &H8D, &H4, &H4,            ' STA $0404  (palette poids faible = compteur)
            &H8D, &H5, &H4,            ' STA $0405  (écrit le mot, avance)
            &H68,                       ' PLA
            &H40                        ' RTI
        }
        Dim halt As Byte() = {&H80, &HFE}   ' BRA *  (piège sûr : ne touche pas la palette)

        Dim rom(8191) As Byte
        Array.Copy(main, 0, rom, 0, main.Length)          ' $E000
        Array.Copy(handler, 0, rom, &H100, handler.Length) ' $E100
        Array.Copy(halt, 0, rom, &H1F0, halt.Length)       ' $E1F0
        rom(&H1FF6) = &HF0 : rom(&H1FF7) = &HE1   ' BRK/IRQ2 -> $E1F0 (piège sûr)
        rom(&H1FF8) = &HF0 : rom(&H1FF9) = &HE1   ' IRQ1     -> $E1F0
        rom(&H1FFA) = &H0  : rom(&H1FFB) = &HE1   ' TIMER    -> $E100
        rom(&H1FFE) = &H0  : rom(&H1FFF) = &HE0   ' RESET    -> $E000
        Return rom
    End Function
End Module
