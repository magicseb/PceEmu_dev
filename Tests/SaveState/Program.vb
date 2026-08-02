''' <summary>
''' Banc d'essai de la sauvegarde d'état et de la BRAM.
'''
''' Le test central vérifie la seule propriété qui compte vraiment : une console
''' vierge, rechargée depuis une sauvegarde, doit produire exactement le même avenir
''' que la console d'origine. Si un champ manque à l'appel — un registre d'adresse
''' VRAM, la palette, un compteur interne — les deux futurs divergent.
'''
''' La ROM utilisée est assemblée à la main ci-dessous : quelques instructions qui
''' incrémentent un compteur en RAM et le déversent en VRAM et dans la palette, si
''' bien que l'état de la machine évolue à chaque frame.
''' </summary>
Public Module SaveStateTest

    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        Dim romPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_state_test.pce")
        Dim otherRomPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_state_other.pce")
        Dim statePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_state_test.st1")
        Dim bramPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_state_test.brm")

        WriteTestRom(romPath, &H10)
        WriteTestRom(otherRomPath, &H11)   ' Même programme, un octet de données différent

        ' --- Référence : on avance, on sauvegarde, on continue ---
        Dim origin = New PceSystem(romPath, False)
        RunFrames(origin, 60)
        origin.SaveState(statePath)
        RunFrames(origin, 30)
        Dim expected = Snapshot(origin)

        ' Garde-fou : sans lui, une image figée rendrait toutes les comparaisons
        ' ci-dessous vraies sans rien prouver
        Dim atSave = New PceSystem(romPath, False)
        atSave.LoadState(statePath)
        CheckDiffers("garde-fou : l'image évolue entre les deux points",
                     Snapshot(atSave), expected)

        ' --- La même console rechargée doit refaire le même chemin ---
        origin.LoadState(statePath)
        RunFrames(origin, 30)
        Check("rechargement sur la même console", Snapshot(origin), expected)

        ' --- Une console vierge aussi : l'état se suffit à lui-même ---
        Dim fresh = New PceSystem(romPath, False)
        fresh.LoadState(statePath)
        RunFrames(fresh, 30)
        Check("rechargement sur une console vierge", Snapshot(fresh), expected)

        ' --- Le compteur de frames fait partie de l'état ---
        Dim counted = New PceSystem(romPath, False)
        counted.LoadState(statePath)
        Check("compteur de frames restauré", counted.FrameCount, 60)

        ' --- Une sauvegarde faite avec un autre jeu doit être refusée ---
        Dim other = New PceSystem(otherRomPath, False)
        CheckThrows("sauvegarde d'un autre jeu refusée", Sub() other.LoadState(statePath))

        ' --- Un fichier qui n'est pas une sauvegarde doit être refusé ---
        Dim junkPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_junk.st1")
        System.IO.File.WriteAllBytes(junkPath, New Byte() {1, 2, 3, 4, 5, 6, 7, 8})
        CheckThrows("fichier étranger refusé", Sub() origin.LoadState(junkPath))

        ' --- BRAM : une console neuve présente une mémoire formatée ---
        If System.IO.File.Exists(bramPath) Then System.IO.File.Delete(bramPath)
        Dim bramSystem = New PceSystem(romPath, False)
        bramSystem.LoadBram(bramPath)
        bramSystem.SaveBram(bramPath)
        Dim written = System.IO.File.ReadAllBytes(bramPath)
        Check("BRAM neuve : taille de 2 Ko", written.Length, 2048)
        Check("BRAM neuve : en-tête de formatage", System.Text.Encoding.ASCII.GetString(written, 0, 4), "HUBM")

        ' --- BRAM : ce qui a été écrit se relit ---
        written(&H100) = &H5A
        System.IO.File.WriteAllBytes(bramPath, written)
        Dim reloaded = New PceSystem(romPath, False)
        reloaded.LoadBram(bramPath)
        reloaded.SaveBram(bramPath)
        Check("BRAM : contenu conservé au rechargement",
              System.IO.File.ReadAllBytes(bramPath)(&H100), CByte(&H5A))

        ' --- BRAM : les écritures du jeu sont détectées et sauvegardées ---
        Dim cart = CartridgeLoader.LoadCartridge(romPath)
        Dim mpu = New MemoryMap(cart)
        Check("BRAM intacte au démarrage", mpu.BramModified, False)
        mpu.SetMPR(0, &HF7)
        mpu.WriteByte(&H20, &H99)
        Check("écriture en BRAM détectée", mpu.BramModified, True)
        Check("écriture en BRAM relue", mpu.GetBram()(&H20), CByte(&H99))
        mpu.WriteByte(&H21, &H0)
        Check("écriture sans changement ignorée", mpu.ReadByte(&H21), 0)

        For Each p In {romPath, otherRomPath, statePath, bramPath, junkPath}
            If System.IO.File.Exists(p) Then System.IO.File.Delete(p)
        Next

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    Private Sub RunFrames(sys As PceSystem, count As Integer)
        For i = 1 To count
            sys.RunFrame()
            sys.GetAudioSamples()      ' Vide le tampon, comme le fait le frontend
        Next
    End Sub

    ''' <summary>Empreinte de l'image produite : c'est le reflet observable de l'état interne.</summary>
    Private Function Snapshot(sys As PceSystem) As String
        Dim fb = sys.GetFramebuffer()
        Dim bytes(fb.Length * 4 - 1) As Byte
        Buffer.BlockCopy(fb, 0, bytes, 0, bytes.Length)
        Using md5 = System.Security.Cryptography.MD5.Create()
            Return Convert.ToHexString(md5.ComputeHash(bytes))
        End Using
    End Function

    Private Sub Check(label As String, actual As Object, expected As Object)
        Dim ok = actual.ToString() = expected.ToString()
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label &
                          If(ok, "", "  (obtenu " & actual.ToString() & ", attendu " & expected.ToString() & ")"))
    End Sub

    Private Sub CheckDiffers(label As String, actual As Object, other As Object)
        Dim ok = actual.ToString() <> other.ToString()
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label)
    End Sub

    Private Sub CheckThrows(label As String, action As Action)
        Dim threw = False
        Try
            action()
        Catch ex As Exception
            threw = True
        End Try
        If threw Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(threw, "OK  ", "ÉCHEC") & "] " & label)
    End Sub

    ''' <summary>
    ''' Écrit une ROM de 8 Ko contenant un programme HuC6280 assemblé à la main.
    ''' Il initialise le mapping et le VDC, puis boucle en incrémentant un compteur
    ''' de page zéro qu'il recopie en VRAM et dans la palette — chaque frame modifie
    ''' donc la VRAM, la palette, la RAM et les registres d'adresse du VDC et du VCE.
    ''' </summary>
    Private Sub WriteTestRom(path As String, counterSlot As Byte)
        Dim code As Byte() = {
            &H78,                        ' SEI
            &HD4,                        ' CSH
            &HA9, &HFF,                  ' LDA #$FF
            &H53, &H1,                   ' TAM #$01   -> MPR0 = page matérielle
            &HA9, &HF8,                  ' LDA #$F8
            &H53, &H2,                   ' TAM #$02   -> MPR1 = RAM (page zéro et pile)
            &HA2, &HFF,                  ' LDX #$FF
            &H9A,                        ' TXS
            &HA9, &HB,                   ' LDA #$0B
            &H8D, &H0, &H0,              ' STA $0000  -> sélectionne HDR
            &HA9, &H1F,                  ' LDA #$1F
            &H8D, &H2, &H0,              ' STA $0002  -> largeur 256
            &H9C, &H3, &H0,              ' STZ $0003
            &HA9, &HD,                   ' LDA #$0D
            &H8D, &H0, &H0,              ' STA $0000  -> sélectionne VDW
            &HA9, &HEF,                  ' LDA #$EF
            &H8D, &H2, &H0,              ' STA $0002  -> 240 lignes
            &H9C, &H3, &H0,              ' STZ $0003
            &H9C, &H0, &H0,              ' STZ $0000  -> sélectionne MAWR
            &H9C, &H2, &H0,              ' STZ $0002
            &H9C, &H3, &H0,              ' STZ $0003  -> adresse d'écriture VRAM = 0
            &HA9, &H2,                   ' LDA #$02
            &H8D, &H0, &H0,              ' STA $0000  -> sélectionne le port de données VRAM
            &H9C, &H2, &H4,              ' STZ $0402
            &H9C, &H3, &H4,              ' STZ $0403  -> adresse palette = 0
            &HE6, counterSlot,           ' INC <compteur>
            &HA5, counterSlot,           ' LDA <compteur>
            &H8D, &H2, &H0,              ' STA $0002  -> VRAM, octet de poids faible
            &H8D, &H3, &H0,              ' STA $0003  -> VRAM, écrit le mot et avance
            &H8D, &H4, &H4,              ' STA $0404  -> palette, poids faible
            &H8D, &H5, &H4,              ' STA $0405  -> palette, écrit et avance
            &H80, &HEE                   ' BRA -18    -> retour au INC
        }

        Dim rom(8191) As Byte
        Array.Copy(code, rom, code.Length)
        rom(&H1FFE) = &H0        ' Vecteur RESET = $E000
        rom(&H1FFF) = &HE0
        System.IO.File.WriteAllBytes(path, rom)
    End Sub

End Module
