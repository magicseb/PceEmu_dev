''' <summary>
''' Banc d'essai du LFO du PSG.
'''
''' Principe : le canal 0 reçoit une onde carrée — sa période s'entend donc — tandis que
''' le canal 1 (le modulateur) reçoit une forme d'onde CONSTANTE.
''' Sa sortie vaut alors toujours (valeur - 16) décalée selon la profondeur, donc
''' la période du canal 0 est décalée d'une constante connue. Le résultat doit être
''' rigoureusement identique à un PSG où l'on aurait écrit cette période directement,
''' LFO éteint et canal 1 coupé. La comparaison se fait échantillon par échantillon.
''' </summary>
Public Module LfoPsgTest

    Private Const CH0_FREQ As Integer = 400
    Private Const CH1_FREQ As Integer = 100
    Private Const LFO_FREQ_REG As Integer = 8
    Private Const SAMPLES_CYCLES As Long = 119000   ' Cycles CPU d'une frame, valeur arbitraire mais fixe

    Private passed As Integer = 0
    Private failed As Integer = 0

    Public Function Main() As Integer
        ' Garde-fou : sans lui, une onde porteuse constante rendrait tous les tests
        ' insensibles à la période et ils passeraient sans rien prouver
        CheckDiffers("garde-fou : la période s'entend",
                     Reference(CH0_FREQ), Reference(CH0_FREQ + 15))

        ' Profondeur 1 (×1) : la sortie 31 du modulateur vaut +15
        Check("profondeur ×1, décalage positif",
              Modulated(depth:=1, modValue:=31),
              Reference(CH0_FREQ + 15))

        ' Profondeur 1, sortie 0 du modulateur : -16
        Check("profondeur ×1, décalage négatif",
              Modulated(depth:=1, modValue:=0),
              Reference(CH0_FREQ - 16))

        ' Profondeur 2 (×16) : 15 × 16 = 240
        Check("profondeur ×16",
              Modulated(depth:=2, modValue:=31),
              Reference(CH0_FREQ + 240))

        ' Profondeur 3 (×256) : 1 × 256 = 256
        Check("profondeur ×256",
              Modulated(depth:=3, modValue:=17),
              Reference(CH0_FREQ + 256))

        ' Le canal 1 devient muet dès que le LFO est actif : sa composante continue
        ' est absente du mixage, contrairement à une référence où il resterait audible
        CheckDiffers("canal modulateur rendu muet",
                     Modulated(depth:=1, modValue:=31),
                     TwoChannelReference(CH0_FREQ + 15, 31))

        ' Bit 7 : le modulateur est figé, plus aucune modulation, mais le canal 1 reste muet
        Check("bit 7 (maintien) : aucune modulation",
              Modulated(depth:=1, modValue:=31, held:=True),
              Reference(CH0_FREQ))

        ' Profondeur 0 : le LFO est éteint, le canal 1 redevient un canal audible normal
        Check("profondeur 0 : canal 1 audible",
              Modulated(depth:=0, modValue:=31),
              TwoChannelReference(CH0_FREQ, 31))

        ' Une forme d'onde variable doit produire une modulation réellement variable
        CheckDiffers("modulateur variable : le son change",
                     ModulatedRamp(),
                     Reference(CH0_FREQ))

        Console.WriteLine()
        Console.WriteLine(passed & " réussis, " & failed & " échoués")
        Return If(failed = 0, 0, 1)
    End Function

    Private Sub Check(label As String, actual() As Short, expected() As Short)
        Dim ok = SameSamples(actual, expected)
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label &
                          If(ok, "", "  (premier écart : " & FirstDiff(actual, expected) & ")"))
    End Sub

    Private Sub CheckDiffers(label As String, actual() As Short, other() As Short)
        Dim ok = Not SameSamples(actual, other)
        If ok Then passed += 1 Else failed += 1
        Console.WriteLine("  [" & If(ok, "OK  ", "ÉCHEC") & "] " & label)
    End Sub

    Private Function SameSamples(a() As Short, b() As Short) As Boolean
        If a.Length <> b.Length Then Return False
        For i = 0 To a.Length - 1
            If a(i) <> b(i) Then Return False
        Next
        Return True
    End Function

    Private Function FirstDiff(a() As Short, b() As Short) As String
        For i = 0 To Math.Min(a.Length, b.Length) - 1
            If a(i) <> b(i) Then Return "échantillon " & i & " : " & a(i) & " vs " & b(i)
        Next
        Return "longueurs différentes"
    End Function

    ''' <summary>PSG avec LFO actif et forme d'onde constante sur le modulateur.</summary>
    Private Function Modulated(depth As Integer, modValue As Integer, Optional held As Boolean = False) As Short()
        Dim psg = New Psg()
        SetupToneChannel(psg, 0, CH0_FREQ, SquareWave())     ' Canal audible
        SetupToneChannel(psg, 1, CH1_FREQ, ConstantWave(modValue))
        psg.Write(8, LFO_FREQ_REG)
        psg.Write(9, depth Or If(held, &H80, 0))
        Return psg.GenerateSamples(SAMPLES_CYCLES)
    End Function

    ''' <summary>PSG avec LFO actif et modulateur en dents de scie.</summary>
    Private Function ModulatedRamp() As Short()
        Dim psg = New Psg()
        Dim ramp(31) As Integer
        For i = 0 To 31
            ramp(i) = i
        Next
        SetupToneChannel(psg, 0, CH0_FREQ, SquareWave())
        SetupToneChannel(psg, 1, CH1_FREQ, ramp)
        psg.Write(8, LFO_FREQ_REG)
        psg.Write(9, 2)
        Return psg.GenerateSamples(SAMPLES_CYCLES)
    End Function

    ''' <summary>PSG sans LFO, canal 0 à la période attendue, canal 1 coupé.</summary>
    Private Function Reference(freq As Integer) As Short()
        Dim psg = New Psg()
        SetupToneChannel(psg, 0, freq And &HFFF, SquareWave())
        Return psg.GenerateSamples(SAMPLES_CYCLES)
    End Function

    ''' <summary>PSG sans LFO, les deux canaux audibles.</summary>
    Private Function TwoChannelReference(freq As Integer, modValue As Integer) As Short()
        Dim psg = New Psg()
        SetupToneChannel(psg, 0, freq And &HFFF, SquareWave())
        SetupToneChannel(psg, 1, CH1_FREQ, ConstantWave(modValue))
        psg.Write(8, LFO_FREQ_REG)
        psg.Write(9, 0)
        Return psg.GenerateSamples(SAMPLES_CYCLES)
    End Function

    ''' <summary>Onde carrée : c'est elle qui rend la période audible.</summary>
    Private Function SquareWave() As Integer()
        Dim w(31) As Integer
        For i = 0 To 31
            w(i) = If(i < 16, 0, 31)
        Next
        Return w
    End Function

    Private Function ConstantWave(value As Integer) As Integer()
        Dim w(31) As Integer
        For i = 0 To 31
            w(i) = value
        Next
        Return w
    End Function

    ''' <summary>Programme un canal en mode table d'onde, volume et balance au maximum.</summary>
    Private Sub SetupToneChannel(psg As Psg, index As Integer, freq As Integer, waveform() As Integer)
        psg.Write(1, &HFF)              ' Balance générale
        psg.Write(0, index)             ' Sélection du canal
        psg.Write(2, freq And &HFF)     ' Période, poids faible
        psg.Write(3, (freq >> 8) And &HF)
        psg.Write(5, &HFF)              ' Balance du canal
        psg.Write(4, 0)                 ' Canal coupé : remet à zéro le pointeur d'écriture
        For i = 0 To 31
            psg.Write(6, waveform(i))
        Next
        psg.Write(4, &H80 Or &H1F)      ' Canal actif, volume maximal
    End Sub

End Module
