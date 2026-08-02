''' <summary>
''' Banc d'essai de la sortie stéréo du PSG.
'''
''' La sortie est entrelacée : indices pairs = voie gauche, impairs = voie droite.
''' On pilote la balance d'un canal (registre $0805) et la balance générale ($0801)
''' pour vérifier que le panoramique agit réellement voie par voie.
'''
''' Test garde-fou (leçon « pas de test vacux ») : une régression qui repasserait
''' la sortie en mono (voies identiques) ferait échouer « panoramique gauche : droite
''' muette », « … droite : gauche muette » et « gauche ≠ droite en panoramique ».
''' </summary>
Public Module StereoPsgTest

    Private Const CH_FREQ As Integer = 400
    Private Const SAMPLES_CYCLES As Long = 119000   ' cycles CPU d'une frame

    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        Console.WriteLine("Banc stéréo PSG")

        Dim numSamples = CInt(PceConstants.AUDIO_SAMPLE_RATE / PceConstants.FRAME_RATE)

        ' 1) Format entrelacé : deux Short par échantillon
        Dim centered = Render(&HFF, &HFF)
        Check("sortie entrelacée : longueur = 2 × échantillons", centered.Length = numSamples * 2)

        ' 2) Balance centrée : les deux voies sont identiques
        Check("balance centrée : gauche = droite", ChannelsEqual(centered))

        ' 3) Panoramique canal à gauche (F0) : voie droite muette, gauche audible
        Dim panL = Render(&HF0, &HFF)
        Check("panoramique gauche : droite muette", MaxAbs(panL, 1) = 0)
        Check("panoramique gauche : gauche audible", MaxAbs(panL, 0) > 0)

        ' 4) Panoramique canal à droite (0F) : voie gauche muette, droite audible
        Dim panR = Render(&HF, &HFF)
        Check("panoramique droite : gauche muette", MaxAbs(panR, 0) = 0)
        Check("panoramique droite : droite audible", MaxAbs(panR, 1) > 0)

        ' 5) Balance GÉNÉRALE à gauche (main F0), canal centré : droite muette quand même
        Dim mainL = Render(&HFF, &HF0)
        Check("balance générale gauche : droite muette", MaxAbs(mainL, 1) = 0)
        Check("balance générale gauche : gauche audible", MaxAbs(mainL, 0) > 0)

        ' 6) GARDE-FOU : en panoramique, les deux voies diffèrent réellement
        Check("garde-fou : gauche ≠ droite en panoramique", Not ChannelsEqual(panL))

        ' 7) Balance graduée (F4) : droite atténuée mais NON nulle, et < gauche
        Dim graded = Render(&HF4, &HFF)
        Dim gl = MaxAbs(graded, 0)
        Dim gr = MaxAbs(graded, 1)
        Check("balance graduée : droite audible mais atténuée", gr > 0 AndAlso gr < gl)

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    Private Sub Check(label As String, ok As Boolean)
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label)
    End Sub

    ''' <summary>Rend une frame avec la balance de canal et la balance générale données.</summary>
    Private Function Render(channelBalance As Integer, mainBalance As Integer) As Short()
        Dim psg = New Psg()
        psg.Write(1, mainBalance)              ' Balance générale
        psg.Write(0, 0)                        ' Sélection du canal 0
        psg.Write(2, CH_FREQ And &HFF)         ' Période, poids faible
        psg.Write(3, (CH_FREQ >> 8) And &HF)   ' Période, poids fort
        psg.Write(5, channelBalance)           ' Balance du canal
        psg.Write(4, 0)                        ' Remet à zéro le pointeur d'écriture d'onde
        Dim wave = SquareWave()
        For i = 0 To 31
            psg.Write(6, wave(i))
        Next
        psg.Write(4, &H80 Or &H1F)             ' Canal actif, volume maximal
        Return psg.GenerateSamples(SAMPLES_CYCLES)
    End Function

    ''' <summary>Amplitude maximale d'une voie (0 = gauche, 1 = droite).</summary>
    Private Function MaxAbs(samples() As Short, channel As Integer) As Integer
        Dim m = 0
        Dim i = channel
        While i < samples.Length
            Dim a = Math.Abs(CInt(samples(i)))
            If a > m Then m = a
            i += 2
        End While
        Return m
    End Function

    ''' <summary>Vrai si les deux voies sont rigoureusement identiques.</summary>
    Private Function ChannelsEqual(samples() As Short) As Boolean
        Dim i = 0
        While i < samples.Length
            If samples(i) <> samples(i + 1) Then Return False
            i += 2
        End While
        Return True
    End Function

    Private Function SquareWave() As Integer()
        Dim w(31) As Integer
        For i = 0 To 31
            w(i) = If(i < 16, 0, 31)
        Next
        Return w
    End Function

End Module
