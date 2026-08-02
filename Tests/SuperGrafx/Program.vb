''' <summary>
''' Banc d'essai du SuperGrafx : décodage de la zone vidéo, RAM étendue, et surtout
''' le mélange des deux VDC par le VPC.
'''
''' Chaque VDC reçoit un fond d'une couleur connue — opaque ou transparent selon le
''' cas — et l'on vérifie quel chip gagne à l'écran. Les réglages du VPC (couches
''' actives, mode de priorité, fenêtres) sont écrits dans ses registres comme le
''' ferait un jeu.
''' </summary>
Public Module SuperGrafxTest

    Private Const BAT_ENTRIES As Integer = 32       ' Une tilemap de 32 tuiles de large
    Private Const TILE_INDEX As Integer = &H100     ' Motif placé au-delà de la tilemap
    Private Const PATTERN_ADDR As Integer = TILE_INDEX << 4

    Private Const SPRITE_PATTERN As Integer = &H2000
    Private Const SPRITE_CODE As Integer = &H100    ' (&H100 And &H7FE) << 5 = &H2000
    Private Const SATB_ADDR As Integer = &H3000

    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        TestMemoryDecoding()
        TestWorkRam()
        TestMixing()
        TestWindows()
        TestStoreImmediate()
        TestSharedInterrupt()

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    ' ===== Décodage de la zone vidéo =====

    Private Sub TestMemoryDecoding()
        Dim romPath = MakeRom()
        Dim sys = New PceSystem(romPath, True)
        Check("mode SuperGrafx actif", sys.IsSuperGrafx, True)

        Dim pce = New PceSystem(romPath, False)
        Check("mode PC Engine par défaut", pce.IsSuperGrafx, False)

        ' Les deux VDC doivent être réellement distincts : on donne à chacun une
        ' adresse d'écriture VRAM différente, puis on relit par leur port respectif
        Dim mpu = BuildBus()
        SetVdcReg(mpu, &H0, 0, &H0010)      ' VDC #1 : MAWR = $0010
        SetVdcReg(mpu, &H10, 0, &H0020)     ' VDC #2 : MAWR = $0020
        WriteVdcData(mpu, &H0, &H1111)      ' Écrit dans la VRAM du VDC #1
        WriteVdcData(mpu, &H10, &H2222)     ' Écrit dans la VRAM du VDC #2

        Check("VDC #1 relit sa propre VRAM", ReadVram(mpu, &H0, &H0010), &H1111)
        Check("VDC #2 relit sa propre VRAM", ReadVram(mpu, &H10, &H0020), &H2222)
        Check("les VRAM sont bien séparées", ReadVram(mpu, &H1, &H0020), 0)

        ' Le miroir du VDC #1 ($04-$07) doit viser le même chip
        SetVdcReg(mpu, &H4, 0, &H0030)
        WriteVdcData(mpu, &H4, &H3333)
        Check("miroir $0004 : même chip que $0000", ReadVram(mpu, &H0, &H0030), &H3333)

        ' Les registres du VPC répondent à $0008-$000F
        mpu.WriteByte(&HA, &H80)
        mpu.WriteByte(&HB, &H1)
        Check("registre de fenêtre du VPC relu", mpu.ReadByte(&HA) Or (mpu.ReadByte(&HB) << 8), &H180)

        System.IO.File.Delete(romPath)
    End Sub

    ' ===== RAM de travail =====

    Private Sub TestWorkRam()
        Dim romPath = MakeRom()
        Dim cart = CartridgeLoader.LoadCartridge(romPath)

        ' Sur PC Engine, les 8 Ko se répètent sur les quatre pages
        Dim pce = New MemoryMap(cart, False)
        pce.SetMPR(0, &HF8)
        pce.WriteByte(&H0, &HAA)
        pce.SetMPR(0, &HF9)
        Check("PC Engine : la RAM se répète", pce.ReadByte(&H0), &HAA)

        ' Sur SuperGrafx, les 32 Ko sont linéaires
        Dim sgx = New MemoryMap(cart, True)
        sgx.SetMPR(0, &HF8)
        sgx.WriteByte(&H0, &HAA)
        sgx.SetMPR(0, &HF9)
        Check("SuperGrafx : la page $F9 est distincte", sgx.ReadByte(&H0), 0)
        sgx.WriteByte(&H0, &HBB)
        sgx.SetMPR(0, &HF8)
        Check("SuperGrafx : la page $F8 est intacte", sgx.ReadByte(&H0), &HAA)
        sgx.SetMPR(0, &HFB)
        sgx.WriteByte(&H1FFF, &HCC)
        Check("SuperGrafx : dernier octet des 32 Ko", sgx.ReadByte(&H1FFF), &HCC)

        System.IO.File.Delete(romPath)
    End Sub

    ' ===== Mélange des deux VDC =====

    Private Sub TestMixing()
        ' Garde-fou : si deux codes donnaient la même couleur, tous les tests
        ' de mélange ci-dessous seraient vrais sans rien prouver
        Dim probe = New Vce()
        SetupPalette(probe)
        CheckDiffers("garde-fou : deux codes, deux couleurs",
                     probe.GetColorArgb(Code(1)), probe.GetColorArgb(Code(2)))

        ' Réglage d'usine : seul le VDC #1 est visible
        Check("à la mise sous tension, seul le VDC #1 s'affiche",
              MixedPixel(bg1Opaque:=True, bg2Opaque:=True, noWindowField:=Nothing), Code(1))

        Check("VDC #1 devant VDC #2",
              MixedPixel(True, True, &H3), Code(1))

        Check("VDC #2 visible à travers un VDC #1 transparent",
              MixedPixel(False, True, &H3), Code(2))

        Check("aucune couche opaque : couleur 0",
              MixedPixel(False, False, &H3), 0)

        Check("VDC #1 éteint : le VDC #2 passe devant",
              MixedPixel(True, True, &H2), Code(2))

        Check("VDC #2 éteint : seul le VDC #1 subsiste",
              MixedPixel(True, True, &H1), Code(1))

        Check("les deux couches éteintes : couleur 0",
              MixedPixel(True, True, &H0), 0)

        ' Mode 1 : les sprites des deux chips passent devant les deux fonds
        Check("mode 0 : le fond du VDC #1 masque le sprite du VDC #2",
              MixedPixel(True, False, &H3, spr2:=True), Code(1))

        Check("mode 1 : le sprite du VDC #2 passe devant le fond du VDC #1",
              MixedPixel(True, False, &H7, spr2:=True), SpriteCode(2))
    End Sub

    ' ===== Fenêtres =====

    Private Sub TestWindows()
        Dim vce = New Vce()
        Dim vdc1 = New Vdc(vce)
        Dim vdc2 = New Vdc(vce)
        Dim vpc = New Vpc(vdc1, vdc2, vce)
        Dim fb(PceConstants.SCREEN_WIDTH * PceConstants.SCREEN_HEIGHT - 1) As Integer

        SetupPalette(vce)
        SetupBackground(vdc1, 1, True)
        SetupBackground(vdc2, 2, True)

        ' Fenêtre 1 large de 32 pixels de zone affichée ($40 + 32)
        vpc.Write(2, (&H40 + 32) And &HFF)
        vpc.Write(3, ((&H40 + 32) >> 8) And &H3)
        vpc.Write(4, 0)          ' Fenêtre 2 désactivée
        vpc.Write(5, 0)

        ' Hors fenêtre : VDC #1 seul ; dans la fenêtre 1 : VDC #2 seul
        vpc.Write(1, (&H1 << 4) Or &H2)
        vpc.Write(0, 0)

        RunFrames(vpc, fb, 2)

        Check("dans la fenêtre 1 : le VDC #2", fb(10), vce.GetColorArgb(Code(2)))
        Check("hors fenêtre : le VDC #1", fb(100), vce.GetColorArgb(Code(1)))
        Check("bord droit de la fenêtre inclus", fb(31), vce.GetColorArgb(Code(2)))
        Check("premier pixel hors fenêtre", fb(32), vce.GetColorArgb(Code(1)))

        ' Une fenêtre plus étroite que $40 est invisible
        vpc.Write(2, &H10)
        vpc.Write(3, 0)
        RunFrames(vpc, fb, 1)
        Check("fenêtre trop étroite : sans effet", fb(0), vce.GetColorArgb(Code(1)))
    End Sub

    ' ===== ST0/ST1/ST2 =====

    Private Sub TestStoreImmediate()
        Dim mpu = BuildBus()

        ' Par défaut les écritures immédiates visent le VDC #1
        mpu.WriteStoreImmediate(0, 0)            ' Sélectionne MAWR
        mpu.WriteStoreImmediate(2, &H40)
        mpu.WriteStoreImmediate(3, 0)
        WriteVdcData(mpu, &H0, &HABCD)
        Check("ST0-ST2 visent le VDC #1 par défaut", ReadVram(mpu, &H0, &H40), &HABCD)

        ' $000E bit 0 les redirige vers le VDC #2
        mpu.WriteByte(&HE, 1)
        mpu.WriteStoreImmediate(0, 0)
        mpu.WriteStoreImmediate(2, &H50)
        mpu.WriteStoreImmediate(3, 0)
        WriteVdcData(mpu, &H10, &H1234)
        Check("ST0-ST2 redirigés vers le VDC #2", ReadVram(mpu, &H10, &H50), &H1234)
        Check("le VDC #1 n'a rien reçu", ReadVram(mpu, &H0, &H50), 0)

        Check("le registre $000E se lit toujours à zéro", mpu.ReadByte(&HE), 0)
    End Sub

    ' ===== Interruption partagée =====

    Private Sub TestSharedInterrupt()
        Dim vce = New Vce()
        Dim vdc1 = New Vdc(vce)
        Dim vdc2 = New Vdc(vce)
        Dim vpc = New Vpc(vdc1, vdc2, vce)
        Dim cart = CartridgeLoader.LoadCartridge(MakeRom())
        Dim mpu = New MemoryMap(cart, True)
        mpu.ConnectPeripherals(vce, vdc1, New Psg(), New CpuTimer(), New Joypad())
        mpu.ConnectSuperGrafx(vdc2, vpc)

        Check("aucune interruption au repos", mpu.Irq1Line, False)

        ' Seul le VDC #2 demande une interruption de fin d'image
        SetupBackground(vdc2, 2, True)
        SetVdcRegDirect(vdc2, 5, &H8)            ' CR : IRQ de VBlank
        Dim fb(PceConstants.SCREEN_WIDTH * PceConstants.SCREEN_HEIGHT - 1) As Integer
        RunFrames(vpc, fb, 1)

        Check("le VDC #2 lève la ligne partagée", mpu.Irq1Line, True)
        vdc2.Read(0)                              ' La lecture de l'état relâche la ligne
        Check("la ligne retombe après lecture de l'état", mpu.Irq1Line, False)
    End Sub

    ' ===== Utilitaires =====

    ''' <summary>
    ''' Donne à chaque entrée de palette une teinte qui lui est propre. Sans cela
    ''' toutes les couleurs valent le même noir et les comparaisons de pixels
    ''' seraient vraies sans rien démontrer.
    ''' </summary>
    Private Sub SetupPalette(vce As Vce)
        vce.Write(2, 0)
        vce.Write(3, 0)
        For entry = 0 To 511
            vce.Write(4, entry And &HFF)
            vce.Write(5, (entry >> 8) And &H1)
        Next
    End Sub

    ''' <summary>Code VCE d'un fond opaque de palette donnée.</summary>
    Private Function Code(palette As Integer) As Integer
        Return (palette << 4) Or 1
    End Function

    ''' <summary>Code VCE d'un sprite opaque de palette donnée.</summary>
    Private Function SpriteCode(palette As Integer) As Integer
        Return 256 + (palette << 4) + 1
    End Function

    ''' <summary>
    ''' Monte deux VDC, applique un réglage de priorité et retourne le code du pixel
    ''' retenu au milieu de la première ligne.
    ''' </summary>
    Private Function MixedPixel(bg1Opaque As Boolean, bg2Opaque As Boolean,
                                noWindowField As Object,
                                Optional spr2 As Boolean = False) As Integer
        Dim vce = New Vce()
        Dim vdc1 = New Vdc(vce)
        Dim vdc2 = New Vdc(vce)
        Dim vpc = New Vpc(vdc1, vdc2, vce)
        Dim fb(PceConstants.SCREEN_WIDTH * PceConstants.SCREEN_HEIGHT - 1) As Integer

        SetupPalette(vce)
        SetupBackground(vdc1, 1, bg1Opaque)
        SetupBackground(vdc2, 2, bg2Opaque)
        If spr2 Then SetupSprite(vdc2, 2)

        ' Le champ « hors fenêtre » occupe les bits 7-4 de $0009
        If noWindowField IsNot Nothing Then
            vpc.Write(1, (CInt(noWindowField) << 4) Or &H1)
        End If

        RunFrames(vpc, fb, 2)

        ' On retrouve le code à partir de la couleur : chaque code a sa teinte
        Dim pixel = fb(64)
        For candidate = 0 To 511
            If vce.GetColorArgb(candidate) = pixel Then Return candidate
        Next
        Return -1
    End Function

    Private Sub RunFrames(vpc As Vpc, fb() As Integer, count As Integer)
        For f = 1 To count
            For line = 0 To PceConstants.SCANLINES_PER_FRAME - 1
                vpc.DoScanline(line, fb)
            Next
        Next
    End Sub

    ''' <summary>Programme un VDC avec un fond uni, opaque ou transparent.</summary>
    Private Sub SetupBackground(vdc As Vdc, palette As Integer, opaque As Boolean)
        SetVdcRegDirect(vdc, 11, 31)             ' HDR : 256 pixels
        SetVdcRegDirect(vdc, 13, 239)            ' VDW : 240 lignes
        SetVdcRegDirect(vdc, 9, 0)               ' MWR : tilemap 32x32
        SetVdcRegDirect(vdc, 5, &HC0)            ' CR : fond et sprites actifs

        ' Tilemap : chaque case pointe le même motif, avec la palette voulue
        SetVdcRegDirect(vdc, 0, 0)
        SelectReg(vdc, 2)
        For i = 0 To BAT_ENTRIES - 1
            WriteWord(vdc, (palette << 12) Or TILE_INDEX)
        Next

        ' Motif : plan 0 rempli donne une couleur d'index 1, vide donne du transparent
        SetVdcRegDirect(vdc, 0, PATTERN_ADDR)
        SelectReg(vdc, 2)
        For row = 0 To 15
            WriteWord(vdc, If(opaque AndAlso row < 8, &HFF, 0))
        Next
    End Sub

    ''' <summary>Ajoute au VDC un sprite plein écran sur la première ligne.</summary>
    Private Sub SetupSprite(vdc As Vdc, palette As Integer)
        ' Motif de sprite entièrement opaque
        SetVdcRegDirect(vdc, 0, SPRITE_PATTERN)
        SelectReg(vdc, 2)
        For row = 0 To 15
            WriteWord(vdc, &HFFFF)
        Next

        ' Table d'attributs : un sprite en haut à gauche, priorité haute
        SetVdcRegDirect(vdc, 0, SATB_ADDR)
        SelectReg(vdc, 2)
        WriteWord(vdc, 0 + 64)                   ' Y
        WriteWord(vdc, 64 + 32)                  ' X, centré sur le pixel testé
        WriteWord(vdc, SPRITE_CODE)
        WriteWord(vdc, &H80 Or palette)          ' Devant le fond, palette voulue
        For i = 1 To 63
            WriteWord(vdc, &H0) : WriteWord(vdc, &H0) : WriteWord(vdc, 0) : WriteWord(vdc, 0)
        Next

        SetVdcRegDirect(vdc, 19, SATB_ADDR)      ' DVSSR : arme le transfert
    End Sub

    ' --- Accès aux VDC, directement ou à travers le bus ---

    Private Sub SelectReg(vdc As Vdc, index As Integer)
        vdc.Write(0, index)
    End Sub

    Private Sub WriteWord(vdc As Vdc, value As Integer)
        vdc.Write(2, value And &HFF)
        vdc.Write(3, (value >> 8) And &HFF)
    End Sub

    Private Sub SetVdcRegDirect(vdc As Vdc, index As Integer, value As Integer)
        SelectReg(vdc, index)
        vdc.Write(2, value And &HFF)
        vdc.Write(3, (value >> 8) And &HFF)
    End Sub

    Private Sub SetVdcReg(mpu As MemoryMap, port As Integer, index As Integer, value As Integer)
        mpu.WriteByte(port, index)
        mpu.WriteByte(port + 2, value And &HFF)
        mpu.WriteByte(port + 3, (value >> 8) And &HFF)
    End Sub

    Private Sub WriteVdcData(mpu As MemoryMap, port As Integer, value As Integer)
        mpu.WriteByte(port, 2)                   ' Sélectionne le port de données VRAM
        mpu.WriteByte(port + 2, value And &HFF)
        mpu.WriteByte(port + 3, (value >> 8) And &HFF)
    End Sub

    Private Function ReadVram(mpu As MemoryMap, port As Integer, address As Integer) As Integer
        mpu.WriteByte(port, 1)                   ' MARR : adresse de lecture
        mpu.WriteByte(port + 2, address And &HFF)
        mpu.WriteByte(port + 3, (address >> 8) And &HFF)
        mpu.WriteByte(port, 2)                   ' Port de données
        Return mpu.ReadByte(port + 2) Or (mpu.ReadByte(port + 3) << 8)
    End Function

    ''' <summary>Bus complet en mode SuperGrafx, page matérielle mappée en $0000.</summary>
    Private Function BuildBus() As MemoryMap
        Dim romPath = MakeRom()
        Dim cart = CartridgeLoader.LoadCartridge(romPath)
        Dim vce = New Vce()
        Dim vdc1 = New Vdc(vce)
        Dim vdc2 = New Vdc(vce)
        Dim vpc = New Vpc(vdc1, vdc2, vce)
        Dim mpu = New MemoryMap(cart, True)
        mpu.ConnectPeripherals(vce, vdc1, New Psg(), New CpuTimer(), New Joypad())
        mpu.ConnectSuperGrafx(vdc2, vpc)
        mpu.SetMPR(0, &HFF)                      ' Page matérielle visible en $0000
        System.IO.File.Delete(romPath)
        Return mpu
    End Function

    Private Function MakeRom() As String
        Dim path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "pceemu_sgx_" & Guid.NewGuid().ToString("N") & ".pce")
        Dim rom(8191) As Byte
        rom(&H1FFE) = &H0
        rom(&H1FFF) = &HE0
        System.IO.File.WriteAllBytes(path, rom)
        Return path
    End Function

    Private Sub CheckDiffers(label As String, actual As Object, other As Object)
        Dim ok = actual.ToString() <> other.ToString()
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label)
    End Sub

    Private Sub Check(label As String, actual As Object, expected As Object)
        Dim ok = actual.ToString() = expected.ToString()
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label &
                          If(ok, "", "  (obtenu " & actual.ToString() & ", attendu " & expected.ToString() & ")"))
    End Sub

End Module
