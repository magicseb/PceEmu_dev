''' <summary>
''' VPC HuC6202 — contrôleur de priorité vidéo du SuperGrafx.
'''
''' Il mélange les sorties des deux VDC pixel par pixel et transmet le résultat au
''' VCE. Chaque VDC a déjà tranché en interne entre son fond et ses sprites : le VPC
''' ne reçoit qu'un seul pixel par chip, accompagné d'un simple drapeau « sprite ou
''' fond ». Il ne sait donc pas distinguer un sprite de priorité haute d'un sprite de
''' priorité basse — cette subtilité appartient au VDC.
'''
''' Deux fenêtres verticales, définies par leur largeur, découpent l'écran en quatre
''' régions ; chacune a son propre réglage de couches actives et de priorité.
'''
''' Source : « NEC SuperGrafx hardware notes », Charles MacDonald.
''' </summary>
Public Class Vpc

    ' Registres $0008-$000F
    Private priority(1) As Integer      ' $0008 et $0009, deux champs de 4 bits chacun
    Private windowWidth(1) As Integer   ' $000A-$000B et $000C-$000D, 10 bits
    Private stToVdc2 As Boolean = False ' $000E bit 0

    ' Réglage décodé de chacune des quatre régions
    Private regionEnableVdc1(3) As Boolean
    Private regionEnableVdc2(3) As Boolean
    Private regionMode(3) As Integer

    ''' <summary>
    ''' Les fenêtres partent du bord gauche de l'écran physique, pas de la zone
    ''' affichée : celle-ci commence à la largeur $0040. Une fenêtre plus étroite
    ''' que cette valeur est donc invisible.
    ''' </summary>
    Private Const DISPLAY_START As Integer = &H40

    ' Index des quatre régions découpées par les fenêtres
    Private Const REGION_NONE As Integer = 0        ' Aucune fenêtre
    Private Const REGION_WINDOW1 As Integer = 1     ' Fenêtre 1 seule
    Private Const REGION_WINDOW2 As Integer = 2     ' Fenêtre 2 seule
    Private Const REGION_BOTH As Integer = 3        ' Recouvrement des deux

    ''' <summary>Compteurs de diagnostic (sans effet sur l'émulation).</summary>
    Public Shared DbgRegisterWrites As Long = 0

    Private vdc1 As Vdc
    Private vdc2 As Vdc
    Private vce As Vce

    Public Sub New(vdc1Ref As Vdc, vdc2Ref As Vdc, vceRef As Vce)
        vdc1 = vdc1Ref
        vdc2 = vdc2Ref
        vce = vceRef

        ' État de mise sous tension : fenêtres désactivées, seul le VDC #1 visible
        WriteRegister(0, &H11)
        WriteRegister(1, &H11)
    End Sub

    ''' <summary>Vrai si ST0/ST1/ST2 doivent viser le VDC #2 ($000E bit 0).</summary>
    Public ReadOnly Property StoreImmediateTargetsVdc2 As Boolean
        Get
            Return stToVdc2
        End Get
    End Property

    ''' <summary>Lecture des registres du VPC ($0008-$000F).</summary>
    Public Function Read(offset As Integer) As Integer
        Select Case offset And 7
            Case 0 : Return priority(0)
            Case 1 : Return priority(1)
            Case 2 : Return windowWidth(0) And &HFF
            Case 3 : Return (windowWidth(0) >> 8) And &H3
            Case 4 : Return windowWidth(1) And &HFF
            Case 5 : Return (windowWidth(1) >> 8) And &H3
            Case Else : Return 0        ' $000E et $000F renvoient toujours zéro
        End Select
    End Function

    ''' <summary>Écriture des registres du VPC.</summary>
    Public Sub Write(offset As Integer, value As Integer)
        DbgRegisterWrites += 1
        WriteRegister(offset And 7, value And &HFF)
    End Sub

    Private Sub WriteRegister(reg As Integer, value As Integer)
        Select Case reg
            Case 0, 1
                priority(reg) = value
                DecodePriority()
            Case 2
                windowWidth(0) = (windowWidth(0) And &H300) Or value
            Case 3
                windowWidth(0) = (windowWidth(0) And &HFF) Or ((value And &H3) << 8)
            Case 4
                windowWidth(1) = (windowWidth(1) And &H300) Or value
            Case 5
                windowWidth(1) = (windowWidth(1) And &HFF) Or ((value And &H3) << 8)
            Case 6
                stToVdc2 = (value And 1) <> 0
            Case 7
                ' Inutilisé
        End Select
    End Sub

    ''' <summary>
    ''' Répartit les deux octets de priorité sur les quatre régions.
    ''' Chaque champ de 4 bits : bit 0 = VDC #1 actif, bit 1 = VDC #2 actif,
    ''' bits 3-2 = mode de priorité.
    ''' </summary>
    Private Sub DecodePriority()
        Store(REGION_WINDOW2, (priority(0) >> 4) And &HF)
        Store(REGION_BOTH, priority(0) And &HF)
        Store(REGION_NONE, (priority(1) >> 4) And &HF)
        Store(REGION_WINDOW1, priority(1) And &HF)
    End Sub

    Private Sub Store(region As Integer, field As Integer)
        regionEnableVdc1(region) = (field And &H1) <> 0
        regionEnableVdc2(region) = (field And &H2) <> 0
        regionMode(region) = (field >> 2) And &H3
    End Sub

    ''' <summary>Fait avancer les deux VDC d'une scanline puis compose le résultat.</summary>
    Public Sub DoScanline(scanline As Integer, framebuffer() As Integer)
        vdc1.DoScanline(scanline)
        vdc2.DoScanline(scanline)

        If scanline >= Math.Max(vdc1.DisplayHeight, vdc2.DisplayHeight) Then Return
        MixLine(scanline, framebuffer)
    End Sub

    ''' <summary>Détermine la région à laquelle appartient un pixel de la zone affichée.</summary>
    Private Function RegionAt(x As Integer) As Integer
        ' Le pixel x de la zone affichée occupe la position physique x + $40
        Dim physical = x + DISPLAY_START
        Dim inWindow1 = windowWidth(0) >= DISPLAY_START AndAlso physical < windowWidth(0)
        Dim inWindow2 = windowWidth(1) >= DISPLAY_START AndAlso physical < windowWidth(1)

        If inWindow1 AndAlso inWindow2 Then Return REGION_BOTH
        If inWindow1 Then Return REGION_WINDOW1
        If inWindow2 Then Return REGION_WINDOW2
        Return REGION_NONE
    End Function

    ''' <summary>Compose une scanline à partir des sorties des deux VDC.</summary>
    Private Sub MixLine(scanline As Integer, framebuffer() As Integer)
        Dim startIdx = scanline * PceConstants.SCREEN_WIDTH
        Dim width = Math.Max(vdc1.DisplayWidth, vdc2.DisplayWidth)
        If width > PceConstants.SCREEN_WIDTH Then width = PceConstants.SCREEN_WIDTH

        Dim out1 = vdc1.LineOutput
        Dim out2 = vdc2.LineOutput

        For x = 0 To width - 1
            Dim region = RegionAt(x)

            Dim code1 = If(regionEnableVdc1(region), out1(x), -1)
            Dim code2 = If(regionEnableVdc2(region), out2(x), -1)

            framebuffer(startIdx + x) = vce.GetColorArgb(Choose(code1, code2, regionMode(region)))
        Next
    End Sub

    ''' <summary>
    ''' Choisit le pixel gagnant entre les deux chips.
    '''
    ''' Un code négatif signifie « rien à afficher » ; un code au-delà de 255 désigne
    ''' un pixel de sprite. Quand plus rien ne subsiste, c'est la couleur 0 du VCE qui
    ''' s'affiche — y compris dans l'overscan, contrairement à une PC Engine ordinaire
    ''' qui y met la couleur 256.
    ''' </summary>
    Private Shared Function Choose(code1 As Integer, code2 As Integer, mode As Integer) As Integer
        Select Case mode
            Case 1, 2
                ' Les sprites des deux chips passent devant les deux fonds.
                ' Les sources documentaires se contredisent sur ces deux modes ;
                ' c'est le comportement relevé sur console avec Daimakaimura.
                If IsSprite(code1) Then Return code1
                If IsSprite(code2) Then Return code2
                If code1 >= 0 Then Return code1
                If code2 >= 0 Then Return code2

            Case Else
                ' Modes 0 et 3 : le VDC #1 passe intégralement devant le VDC #2
                If code1 >= 0 Then Return code1
                If code2 >= 0 Then Return code2
        End Select

        Return 0
    End Function

    Private Shared Function IsSprite(code As Integer) As Boolean
        Return code >= 256
    End Function

    ''' <summary>Écrit l'état du VPC dans une sauvegarde.</summary>
    Public Sub SaveState(w As System.IO.BinaryWriter)
        w.Write(priority(0)) : w.Write(priority(1))
        w.Write(windowWidth(0)) : w.Write(windowWidth(1))
        w.Write(stToVdc2)
    End Sub

    ''' <summary>Restaure l'état du VPC depuis une sauvegarde.</summary>
    Public Sub LoadState(r As System.IO.BinaryReader)
        priority(0) = r.ReadInt32() : priority(1) = r.ReadInt32()
        windowWidth(0) = r.ReadInt32() : windowWidth(1) = r.ReadInt32()
        stToVdc2 = r.ReadBoolean()
        DecodePriority()
    End Sub

End Class
