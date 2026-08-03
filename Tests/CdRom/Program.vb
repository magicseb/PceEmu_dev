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
