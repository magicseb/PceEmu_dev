''' <summary>
''' Banc d'essai de l'interface CD-ROM² (SCSI). Pilote le lecteur exactement comme la
''' System Card : sélection ($1800), envoi des octets de commande ($1801 + handshake
''' ACK via $1802 bit7), puis lecture des données ($1808 auto-ACK).
'''
''' Le cœur testé est le HANDSHAKE REQ/ACK : REQ (bit6 de $1800) doit retomber quand
''' l'initiateur asserte ACK, et remonter à la relâche — sans quoi la boucle « attendre
''' REQ bas » de la System Card tourne à l'infini (le vrai symptôme rencontré).
'''
''' Garde-fou : si le handshake ne fait pas retomber REQ, la lecture se bloque et les
''' vérifications de données échouent (vérifié par mutation).
''' </summary>
Public Module CdRomTest
    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        Console.WriteLine("Banc interface CD-ROM² (SCSI)")

        Dim cuePath = BuildTestImage(12)   ' 12 secteurs de test
        Dim disc = New CdImage(cuePath)
        Dim cd = New CdRom(disc)

        ' 1) Handshake REQ pendant la phase commande
        cd.Write(0, 0)                                  ' SEL -> phase commande
        Check("après sélection : REQ haut", (cd.Read(0) And &H40) <> 0)
        cd.Write(1, &H0)                                ' latch octet
        cd.Write(2, &H80)                               ' ACK assert
        Check("ACK asserté : REQ retombe", (cd.Read(0) And &H40) = 0)
        cd.Write(2, &H0)                                ' ACK relâché
        Check("ACK relâché : REQ remonte", (cd.Read(0) And &H40) <> 0)

        ' 2) TEST UNIT READY -> status 0 (on termine la commande commencée ci-dessus : op=$00)
        ' On a déjà envoyé l'octet 0 = $00 ; envoyons les 5 restants.
        For i = 1 To 5 : SendByte(cd, 0) : Next
        ' phase status : CD=1, IO=1
        Dim st = cd.Read(0)
        Check("TEST UNIT READY : phase status atteinte", (st And &H10) <> 0 AndAlso (st And &H8) <> 0)
        Check("TEST UNIT READY : status = 0 (OK)", cd.Read(1) = 0)

        ' 3) READ(6) d'un secteur, données correctes
        Dim d5 = DoRead(cd, disc, lba:=5, count:=1)
        Check("READ LBA5 : 2048 octets", d5.Length = 2048)
        Check("READ LBA5 : données du bon secteur", CheckSector(d5, 0, 5))

        ' 4) READ multi-secteurs (frontière)
        Dim d = DoRead(cd, disc, lba:=3, count:=2)
        Check("READ 2 secteurs : 4096 octets", d.Length = 4096)
        Check("READ 2 secteurs : secteur 3 correct", CheckSector(d, 0, 3))
        Check("READ 2 secteurs : secteur 4 correct", CheckSector(d, 2048, 4))

        ' 5) GET DIR INFO type 0 : première/dernière piste
        SendCommand(cd, New Integer() {&HDE, &H0, 0, 0, 0, 0, 0, 0, 0, 0})
        Dim ft = cd.Read(8) : Dim lt = cd.Read(8)
        Check("GET DIR INFO : piste 1 à 1 (BCD 01/01)", ft = &H1 AndAlso lt = &H1)

        ' 6) Acquittement du status d'IRQ : après qu'une commande se termine (bus libre),
        ' le bit « transfert terminé » ($20) est posé dans $1803, et sa LECTURE l'efface
        ' (sans quoi l'IRQ2 CD tempête : le handler du BIOS re-déclenche sans fin).
        SendCommand(cd, New Integer() {&H0, 0, 0, 0, 0, 0})   ' TEST UNIT READY
        cd.Read(1)                                            ' lit le status -> phase status
        cd.Write(2, &H80) : cd.Write(2, &H0)                  ' ACK -> phase message
        cd.Read(1)                                            ' lit le message
        cd.Write(2, &H80) : cd.Write(2, &H0)                  ' ACK -> bus libre (pose $20)
        Dim irq1 = cd.Read(3)
        Dim irq2 = cd.Read(3)
        Check("IRQ status : transfert terminé signalé ($20)", (irq1 And &H20) <> 0)
        Check("IRQ status : la lecture de $1803 l'acquitte", (irq2 And &H20) = 0)

        ' 7) Relecture de la RAM ADPCM par le CPU ($180A) — protocole ad_read du BIOS.
        ' Le matériel a un tampon à UNE lecture de latence : lire $180A renvoie la
        ' valeur précédemment tamponnée puis charge mem[adresse] et incrémente.
        ' Le BIOS arme l'adresse de lecture à latch-1 ($180D bit3, bit2=0), fait DEUX
        ' lectures « à jeter », puis stocke : la 3e lecture doit rendre mem[latch].
        ' Sans le tampon, tout le flux relu est décalé d'un octet — c'était la cause
        ' des sprites en fragments de Forgotten Worlds (démo, étoile près du joueur).
        ' Écrire un motif connu en RAM ADPCM à $4000 : adresse d'écriture = latch (bit1, bit0=1)
        cd.Write(8, &H0) : cd.Write(9, &H40)     ' latch = $4000
        cd.Write(&HD, &H3)                       ' front bit1, bit0=1 -> écriture = $4000
        cd.Write(&HD, &H0)
        For i = 0 To 7 : cd.Write(&HA, &H10 + i) : Next   ' mem[$4000..$4007] = $10..$17
        ' Armer la lecture comme le BIOS : latch = $4001, bit3 avec bit2=0 -> adresse = $4000
        cd.Write(8, &H1) : cd.Write(9, &H40)     ' latch = $4001
        cd.Write(&HD, &H8)                       ' front bit3, bit2=0 -> lecture = latch-1 = $4000
        cd.Write(&HD, &H0)
        Dim dummy1 = cd.Read(&HA)                ' jetée (contenu périmé du tampon)
        Dim dummy2 = cd.Read(&HA)                ' rend mem[latch-1] = mem[$4000]
        Check("ADPCM relecture : 2e lecture = mem[latch-1] ($10)", dummy2 = &H10)
        Check("ADPCM relecture : 3e lecture = mem[latch] ($11)", cd.Read(&HA) = &H11)
        Check("ADPCM relecture : 4e lecture = mem[latch+1] ($12)", cd.Read(&HA) = &H12)
        Check("ADPCM relecture : 5e lecture = mem[latch+2] ($13)", cd.Read(&HA) = &H13)
        ' Ré-armement : le pipeline repart du nouveau latch (mêmes règles)
        cd.Write(8, &H4) : cd.Write(9, &H40)     ' latch = $4004
        cd.Write(&HD, &H8) : cd.Write(&HD, &H0)  ' lecture = $4003
        cd.Read(&HA)                             ' jetée
        cd.Read(&HA)                             ' mem[$4003]
        Check("ADPCM relecture après ré-armement : mem[latch] ($14)", cd.Read(&HA) = &H14)

        ' 8) IRQ de fin de lecture ADPCM ($08 de $1803) — Down Load 2.
        ' Quand la lecture ADPCM épuise sa longueur, le bit $08 doit apparaître dans le
        ' status d'IRQ ($1803) et l'IRQ2 s'asserter si l'enable $08 ($1802) est actif :
        ' c'est ce qui déclenche le handler de la System Card ($E845) qui efface les bits
        ' play de $180D (TRB #$60). Sans cette IRQ, ad_stat renvoie « en lecture » pour
        ' toujours et Down Load 2 reste figé sur l'écran du cerveau de son intro.
        ' Préparer un petit sample : 32 octets à partir de $2000.
        cd.Write(8, &H0) : cd.Write(9, &H20)     ' latch = $2000
        cd.Write(&HD, &H3) : cd.Write(&HD, &H0)  ' adresse d'écriture = $2000
        For i = 0 To 31 : cd.Write(&HA, &H88) : Next
        ' Adresse de lecture + longueur = 32, puis lecture (bit5) avec arrêt en fin (bit6).
        cd.Write(&HD, &H8) : cd.Write(&HD, &H0)  ' adresse de lecture = $1FFF (latch-1)
        cd.Write(8, &H20) : cd.Write(9, &H0)     ' latch = $0020 (longueur)
        cd.Write(&HE, &HE)                       ' cadence rapide
        cd.Write(2, &H8)                         ' enable IRQ2 : fin ADPCM ($08)
        cd.Write(&HD, &H70)                      ' bit4 longueur=latch + bit5 lecture + bit6 stop-en-fin
        cd.Write(&HD, &H60)                      ' bit4 relâché (impulsion, comme AD_PLAY du BIOS)
        Check("ADPCM lecture lancée : pas encore d'IRQ fin", (cd.Read(3) And &H8) = 0)
        Check("ADPCM lecture lancée : IRQ2 non assertée", Not cd.IrqLine)
        Dim buf(2047) As Short
        For i = 1 To 200
            Array.Clear(buf, 0, buf.Length)
            cd.RenderAudio(buf, 1024)
            If (cd.Read(3) And &H8) <> 0 Then Exit For
        Next
        Check("fin de lecture : bit $08 posé dans $1803", (cd.Read(3) And &H8) <> 0)
        Check("fin de lecture : IRQ2 assertée (enable $08)", cd.IrqLine)
        Dim s3 = cd.Read(3)
        Check("le bit $08 n'est PAS acquitté par la lecture de $1803", (cd.Read(3) And &H8) <> 0)
        cd.Write(2, &H0)                         ' le handler BIOS désactive l'enable
        Check("enable retiré : IRQ2 retombe (pas de tempête)", Not cd.IrqLine)
        cd.Write(&HD, &H10)                      ' re-latch de longueur (nouvelle lecture)
        Check("re-latch de longueur : bit $08 effacé", (cd.Read(3) And &H8) = 0)

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    ''' <summary>Envoie un octet de commande avec le handshake ACK complet.</summary>
    Private Sub SendByte(cd As CdRom, value As Integer)
        cd.Write(1, value)
        cd.Write(2, &H80)   ' ACK assert
        cd.Write(2, &H0)    ' ACK release
    End Sub

    Private Sub SendCommand(cd As CdRom, cdb As Integer())
        cd.Write(0, 0)      ' SEL -> commande
        For Each b In cdb : SendByte(cd, b) : Next
    End Sub

    ''' <summary>Exécute un READ(6) et récupère count*2048 octets via $1808.</summary>
    Private Function DoRead(cd As CdRom, disc As CdImage, lba As Integer, count As Integer) As Byte()
        SendCommand(cd, New Integer() {&H8, (lba >> 16) And &H1F, (lba >> 8) And &HFF, lba And &HFF, count, 0})
        Dim n = count * 2048
        Dim res(n - 1) As Byte
        For i = 0 To n - 1
            res(i) = CByte(cd.Read(8))   ' $1808 auto-ACK
        Next
        Return res
    End Function

    ''' <summary>Vérifie que buf[off..off+2048] correspond au motif du secteur lba.</summary>
    Private Function CheckSector(buf As Byte(), off As Integer, lba As Integer) As Boolean
        For i = 0 To 2047
            If buf(off + i) <> CByte((lba + i) And &HFF) Then Return False
        Next
        Return True
    End Function

    ''' <summary>Écrit une image .img/.cue de test : secteur L = motif (L+i) mod 256.</summary>
    Private Function BuildTestImage(sectors As Integer) As String
        Dim dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_cdtest")
        System.IO.Directory.CreateDirectory(dir)
        Dim imgPath = System.IO.Path.Combine(dir, "test.img")
        Dim img(sectors * 2352 - 1) As Byte
        For lba = 0 To sectors - 1
            Dim base = lba * 2352 + 16   ' données utilisateur MODE1
            For i = 0 To 2047
                img(base + i) = CByte((lba + i) And &HFF)
            Next
        Next
        System.IO.File.WriteAllBytes(imgPath, img)
        Dim cuePath = System.IO.Path.Combine(dir, "test.cue")
        System.IO.File.WriteAllText(cuePath, "FILE ""test.img"" BINARY" & Environment.NewLine &
                                             "  TRACK 01 MODE1/2352" & Environment.NewLine &
                                             "    INDEX 01 00:00:00" & Environment.NewLine)
        Return cuePath
    End Function

    Private Sub Check(label As String, ok As Boolean)
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label)
    End Sub
End Module
