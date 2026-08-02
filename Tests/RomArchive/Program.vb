''' <summary>
''' Banc d'essai de l'ouverture des jeux : fichier nu, ZIP, 7z.
'''
''' Les archives sont fabriquées ici même, à partir d'une ROM factice au contenu
''' reconnaissable, de sorte que le test vérifie l'octet extrait et pas seulement
''' l'absence d'exception.
''' </summary>
Public Module RomArchiveTest

    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        Dim work = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "pceemu_arc_" & Guid.NewGuid().ToString("N"))
        System.IO.Directory.CreateDirectory(work)

        Try
            Dim rom = MakeRom(&H2000)
            Dim romPath = System.IO.Path.Combine(work, "Jeu Test.pce")
            System.IO.File.WriteAllBytes(romPath, rom)

            ' --- Fichier nu ---
            Dim plain = RomArchive.Load(romPath)
            Check("ROM nue : titre sans extension", plain.Title, "Jeu Test")
            Check("ROM nue : taille", plain.Data.Length, rom.Length)
            Check("ROM nue : contenu", Fingerprint(plain.Data), Fingerprint(rom))

            ' --- ZIP contenant la ROM et un fichier parasite ---
            Dim zipPath = System.IO.Path.Combine(work, "archive.zip")
            BuildZip(zipPath, New (String, Byte())() {("lisezmoi.txt", Text("notes")), ("Jeu Test.pce", rom)})
            Dim fromZip = RomArchive.Load(zipPath)
            Check("ZIP : la ROM est retenue, pas le texte", fromZip.Title, "Jeu Test")
            Check("ZIP : contenu identique à l'original", Fingerprint(fromZip.Data), Fingerprint(rom))

            ' --- ZIP avec deux ROMs : la plus grosse l'emporte ---
            Dim big = MakeRom(&H4000)
            Dim twoPath = System.IO.Path.Combine(work, "deux.zip")
            BuildZip(twoPath, New (String, Byte())() {("petit.pce", rom), ("grand.pce", big)})
            Dim fromTwo = RomArchive.Load(twoPath)
            Check("ZIP : la plus grosse ROM l'emporte", fromTwo.Title, "grand")
            Check("ZIP : taille de la plus grosse", fromTwo.Data.Length, big.Length)

            ' --- ZIP sans ROM ---
            Dim emptyPath = System.IO.Path.Combine(work, "vide.zip")
            BuildZip(emptyPath, New (String, Byte())() {("lisezmoi.txt", Text("rien ici"))})
            CheckThrows("ZIP sans ROM : refusé", Sub() RomArchive.Load(emptyPath))

            ' --- Extensions reconnues ---
            Check("extension .pce reconnue", RomArchive.IsSupported("a.pce"), True)
            Check("extension .sgx reconnue", RomArchive.IsSupported("a.sgx"), True)
            Check("extension .zip reconnue", RomArchive.IsSupported("a.zip"), True)
            Check("extension .7z reconnue", RomArchive.IsSupported("a.7z"), True)
            Check("extension .txt ignorée", RomArchive.IsSupported("a.txt"), False)

            ' --- La ROM extraite est réellement exploitable par le cœur ---
            Dim cart = CartridgeLoader.LoadCartridge(fromZip.Title, fromZip.Data)
            Check("la ROM extraite donne une cartouche", cart.GetMapper(), "Standard")
            Check("titre transmis à la cartouche", cart.Title, "Jeu Test")

            ' --- Garde-fou : deux contenus différents ont deux empreintes différentes ---
            CheckDiffers("garde-fou : l'empreinte distingue les contenus",
                         Fingerprint(rom), Fingerprint(big))

        Finally
            Try
                System.IO.Directory.Delete(work, True)
            Catch
            End Try
        End Try

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    ''' <summary>ROM factice au contenu variant d'un octet à l'autre.</summary>
    Private Function MakeRom(size As Integer) As Byte()
        Dim data(size - 1) As Byte
        For i = 0 To size - 1
            data(i) = CByte((i * 7 + size) And &HFF)
        Next
        Return data
    End Function

    Private Function Text(value As String) As Byte()
        Return System.Text.Encoding.UTF8.GetBytes(value)
    End Function

    Private Function Fingerprint(data As Byte()) As String
        Using md5 = System.Security.Cryptography.MD5.Create()
            Return Convert.ToHexString(md5.ComputeHash(data))
        End Using
    End Function

    Private Sub BuildZip(path As String, files As (Name As String, Content As Byte())())
        Using stream = New System.IO.FileStream(path, System.IO.FileMode.Create)
            Using archive = New System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create)
                For Each file In files
                    Dim entry = archive.CreateEntry(file.Name)
                    Using target = entry.Open()
                        target.Write(file.Content, 0, file.Content.Length)
                    End Using
                Next
            End Using
        End Using
    End Sub

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

End Module
