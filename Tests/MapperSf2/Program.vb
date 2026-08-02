''' <summary>
''' Banc d'essai du mapper Street Fighter II'.
'''
''' On fabrique une fausse ROM de 2,5 Mo dont chaque page de 8 Ko commence par son
''' propre numéro. Lire une page revient donc à demander à la cartouche « quelle
''' portion de ROM as-tu placée ici ? », et la réponse se vérifie exactement.
'''
''' Rappel du câblage : les 512 premiers kilooctets (pages $00-$3F) sont fixes ;
''' les 2 Mo restants forment quatre banques de 512 Ko, dont une seule apparaît à la
''' fois sur les pages $40-$7F. C'est l'adresse écrite ($1FF0 à $1FF3) qui choisit la
''' banque, pas la valeur.
''' </summary>
Public Module MapperSf2Test

    Private Const PAGE_SIZE As Integer = &H2000
    Private Const TOTAL_PAGES As Integer = 320          ' 2,5 Mo
    Private Const FIRST_BANKED_PAGE As Integer = &H40
    Private Const PAGES_PER_BANK As Integer = &H40

    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        Dim romPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_fake_sf2.pce")
        WriteFakeRom(romPath, TOTAL_PAGES)

        Dim cart = CartridgeLoader.LoadCartridge(romPath)
        Check("cartouche de 2,5 Mo reconnue comme SF2", cart.GetMapper(), "SF2")

        Dim mpu = New MemoryMap(cart)

        ' Banque 0 par défaut : les pages commutables suivent immédiatement la zone fixe
        Check("banque par défaut, première page commutable", PageAt(mpu, &H40), 64)
        Check("banque par défaut, dernière page commutable", PageAt(mpu, &H7F), 127)

        ' Chaque banque décale la fenêtre de 512 Ko
        For bank = 0 To 3
            SelectBank(mpu, bank)
            Dim expectedFirst = 64 + bank * PAGES_PER_BANK
            Check("banque " & bank & " : page $40", PageAt(mpu, &H40), expectedFirst)
            Check("banque " & bank & " : page $7F", PageAt(mpu, &H7F), expectedFirst + 63)
        Next

        ' La zone basse ne bouge jamais, quelle que soit la banque
        SelectBank(mpu, 3)
        Check("zone fixe : page $00 inchangée", PageAt(mpu, &H0), 0)
        Check("zone fixe : page $3F inchangée", PageAt(mpu, &H3F), 63)

        ' Seule l'adresse compte : la valeur écrite est ignorée
        SelectBank(mpu, 0)
        WriteAt(mpu, &H1FF2, &H00)
        Check("valeur écrite ignorée, seule l'adresse compte", PageAt(mpu, &H40), 64 + 2 * PAGES_PER_BANK)

        ' Une écriture hors de la fenêtre du mapper ne change rien
        SelectBank(mpu, 1)
        WriteAt(mpu, &H1FF4, &HFF)
        WriteAt(mpu, &H1FE0, &HFF)
        WriteAt(mpu, &H0000, &HFF)
        Check("écriture hors mapper sans effet", PageAt(mpu, &H40), 64 + PAGES_PER_BANK)

        ' Une cartouche ordinaire n'a pas de mapper : elle ignore ces écritures
        Dim plainPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_fake_plain.pce")
        WriteFakeRom(plainPath, 128)                    ' 1 Mo
        Dim plain = CartridgeLoader.LoadCartridge(plainPath)
        Check("cartouche de 1 Mo reconnue comme standard", plain.GetMapper(), "Standard")

        Dim plainMpu = New MemoryMap(plain)
        WriteAt(plainMpu, &H1FF1, &HFF)
        Check("cartouche standard insensible au mapper", PageAt(plainMpu, &H40), 64)

        ' Une ROM plus petite que la zone cartouche s'y répète : 256 Ko = 32 pages,
        ' la page $25 retombe donc sur la page 5
        Dim smallPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceemu_fake_small.pce")
        WriteFakeRom(smallPath, 32)
        Dim smallMpu = New MemoryMap(CartridgeLoader.LoadCartridge(smallPath))
        Check("ROM de 256 Ko : page $05 directe", PageAt(smallMpu, &H5), 5)
        Check("ROM de 256 Ko : page $25 en miroir", PageAt(smallMpu, &H25), 5)
        Check("ROM de 256 Ko : page $65 en miroir", PageAt(smallMpu, &H65), 5)

        System.IO.File.Delete(romPath)
        System.IO.File.Delete(plainPath)
        System.IO.File.Delete(smallPath)

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    ''' <summary>Écrit une ROM factice : chaque page de 8 Ko s'ouvre sur son numéro.</summary>
    Private Sub WriteFakeRom(path As String, pages As Integer)
        Dim data(pages * PAGE_SIZE - 1) As Byte
        For p = 0 To pages - 1
            data(p * PAGE_SIZE) = CByte(p And &HFF)
            data(p * PAGE_SIZE + 1) = CByte((p >> 8) And &HFF)
        Next
        System.IO.File.WriteAllBytes(path, data)
    End Sub

    ''' <summary>Numéro de page ROM effectivement visible derrière une page logique.</summary>
    Private Function PageAt(mpu As MemoryMap, page As Integer) As Integer
        mpu.SetMPR(0, page)
        Return mpu.ReadByte(&H0) Or (mpu.ReadByte(&H1) << 8)
    End Function

    ''' <summary>Écrit à une adresse donnée d'une page ROM (page $40 choisie arbitrairement).</summary>
    Private Sub WriteAt(mpu As MemoryMap, offset As Integer, value As Integer)
        mpu.SetMPR(0, &H40)
        mpu.WriteByte(offset, value)
    End Sub

    Private Sub SelectBank(mpu As MemoryMap, bank As Integer)
        WriteAt(mpu, &H1FF0 + bank, &HA5)
    End Sub

    Private Sub Check(label As String, actual As Object, expected As Object)
        Dim ok = actual.ToString() = expected.ToString()
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label &
                          If(ok, "", "  (obtenu " & actual.ToString() & ", attendu " & expected.ToString() & ")"))
    End Sub

End Module
