''' <summary>MMU PC Engine - mapping 21 bits via MPR0-7, décodage I/O</summary>
Public Class MemoryMap

    Private rom() As Byte
    Private workRam() As Byte           ' 8 Ko sur PC Engine, 32 Ko sur SuperGrafx
    Private workRamMask As Integer
    Private bram(&H7FF) As Byte         ' 2 Ko
    Private mpr(7) As Integer

    ' Contrôle IRQ ($1402/$1403)
    Public IrqDisable As Integer = 0
    ' Démasquage d'IRQ ($1402) : la reconnaissance est différée d'une instruction
    ' (comportement 6502/HuC6280). Sans cela, l'idiome « ré-activer l'IRQ puis
    ' l'acquitter » des handlers timer re-déclenche l'IRQ avant l'ack → ré-entrance.
    Public IrqEnableDelay As Boolean = False

    ''' <summary>Vrai dès qu'un jeu a écrit en BRAM : évite de réécrire le fichier pour rien.</summary>
    Public BramModified As Boolean = False

    Private cartridge As Cartridge
    Private vce As Vce
    Private vdc As Vdc
    Private psg As Psg
    Public TimerRef As CpuTimer
    Private joypad As Joypad

    ' Présents uniquement en mode SuperGrafx
    Private vdc2 As Vdc = Nothing
    Private vpc As Vpc = Nothing

    ' CD-ROM² : lecteur SCSI + RAM CD (Super System Card : 256 Ko, banques $68-$87)
    Private cd As CdRom = Nothing
    Private cdRam() As Byte = Nothing

    ''' <summary>Compteur de diagnostic (sans effet sur l'émulation).</summary>

    ''' <summary>Vrai quand le second VDC et le VPC sont câblés.</summary>
    Public ReadOnly Property SuperGrafx As Boolean
        Get
            Return vpc IsNot Nothing
        End Get
    End Property

    Public Sub New(cart As Cartridge)
        Me.New(cart, False)
    End Sub

    ''' <summary>
    ''' Le SuperGrafx dispose de 32 Ko de RAM de travail au lieu de 8 ;
    ''' la PC Engine ne fait que répéter ses 8 Ko sur les quatre pages $F8-$FB.
    ''' </summary>
    Public Sub New(cart As Cartridge, superGrafxMode As Boolean)
        cartridge = cart
        rom = cart.RomData
        workRamMask = If(superGrafxMode, &H7FFF, &H1FFF)
        workRam = New Byte(workRamMask) {}
        InitializeMPR()
    End Sub

    ''' <summary>Au reset : MPR7 = 0 (bank 0 ROM pour les vecteurs)</summary>
    Private Sub InitializeMPR()
        For i = 0 To 6
            mpr(i) = 0
        Next
        mpr(7) = 0
    End Sub

    Public Sub ConnectPeripherals(vceRef As Vce, vdcRef As Vdc, psgRef As Psg, tmrRef As CpuTimer, joypadRef As Joypad)
        vce = vceRef
        vdc = vdcRef
        psg = psgRef
        TimerRef = tmrRef
        joypad = joypadRef
    End Sub

    ''' <summary>Câble le second VDC et le VPC : le décodage de la zone vidéo change.</summary>
    Public Sub ConnectSuperGrafx(vdc2Ref As Vdc, vpcRef As Vpc)
        vdc2 = vdc2Ref
        vpc = vpcRef
    End Sub

    ''' <summary>Câble le lecteur CD-ROM² et alloue les 256 Ko de RAM CD (Super System Card).</summary>
    Public Sub ConnectCd(cdRef As CdRom)
        cd = cdRef
        cdRam = New Byte(&H3FFFF) {}   ' 256 Ko : banques $68-$87
    End Sub

    ''' <summary>Ligne IRQ2 (CD-ROM², vecteur $FFF6).</summary>
    Public ReadOnly Property Irq2Line As Boolean
        Get
            Return cd IsNot Nothing AndAlso cd.IrqLine
        End Get
    End Property

    ''' <summary>
    ''' Ligne IRQ1 : les deux VDC la partagent sur SuperGrafx, d'où l'obligation
    ''' pour le jeu de lire les deux registres d'état pour savoir qui a interrompu.
    ''' </summary>
    Public ReadOnly Property Irq1Line As Boolean
        Get
            If vdc.IrqLine Then Return True
            Return vdc2 IsNot Nothing AndAlso vdc2.IrqLine
        End Get
    End Property

    ''' <summary>
    ''' Destination des instructions ST0/ST1/ST2 : le VDC #1, ou le VDC #2 quand le
    ''' VPC l'a demandé via son registre $000E.
    ''' </summary>
    Public Sub WriteStoreImmediate(port As Integer, value As Integer)
        If vpc IsNot Nothing AndAlso vpc.StoreImmediateTargetsVdc2 Then
            vdc2.Write(port, value)
        Else
            vdc.Write(port, value)
        End If
    End Sub

    ''' <summary>Lit un octet (adresse logique 16 bits)</summary>
    Public Function ReadByte(logicalAddr As Integer) As Integer
        Dim page = mpr((logicalAddr >> 13) And 7)
        Dim offset = logicalAddr And &H1FFF

        If page = &HFF Then
            Return ReadIO(offset)
        ElseIf cdRam IsNot Nothing AndAlso page >= &H68 AndAlso page <= &H87 Then
            Return cdRam(((page - &H68) << 13) Or offset)
        ElseIf page < &H80 Then
            ' La cartouche traduit elle-même la page en adresse ROM (miroirs, mapper)
            Return cartridge.ReadRom(page, offset)
        ElseIf page >= &HF8 AndAlso page <= &HFB Then
            Return workRam(WorkRamIndex(page, offset))
        ElseIf page = &HF7 Then
            Return bram(offset And &H7FF)
        End If
        Return &HFF
    End Function

    ''' <summary>Écrit un octet</summary>
    Public Sub WriteByte(logicalAddr As Integer, value As Integer)
        Dim page = mpr((logicalAddr >> 13) And 7)
        Dim offset = logicalAddr And &H1FFF
        value = value And &HFF

        If page = &HFF Then
            WriteIO(offset, value)
        ElseIf cdRam IsNot Nothing AndAlso page >= &H68 AndAlso page <= &H87 Then
            cdRam(((page - &H68) << 13) Or offset) = CByte(value)
        ElseIf page >= &HF8 AndAlso page <= &HFB Then
            workRam(WorkRamIndex(page, offset)) = CByte(value)
        ElseIf page = &HF7 Then
            Dim slot = offset And &H7FF
            If bram(slot) <> CByte(value) Then
                bram(slot) = CByte(value)
                BramModified = True
            End If
        ElseIf page < &H80 Then
            ' Sans effet, sauf sur une cartouche à mapper
            cartridge.WriteRom(page, offset, value)
        End If
    End Sub

    ''' <summary>Adresse dans la RAM de travail : linéaire sur 32 Ko, répétée sur 8 Ko.</summary>
    Private Function WorkRamIndex(page As Integer, offset As Integer) As Integer
        Return (((page - &HF8) << 13) Or offset) And workRamMask
    End Function

    ''' <summary>Décodage page I/O ($FF)</summary>
    Private Function ReadIO(offset As Integer) As Integer
        Select Case (offset >> 10) And 7
            Case 0  ' $0000-$03FF : zone vidéo
                Return ReadVideoArea(offset)
            Case 1  ' $0400-$07FF : VCE
                Return vce.Read(offset And 7)
            Case 2  ' $0800-$0BFF : PSG
                Return psg.Read(offset And &HF)
            Case 3  ' $0C00-$0FFF : Timer
                Return TimerRef.Read(offset And 1)
            Case 4  ' $1000-$13FF : Joypad
                Return joypad.Read()
            Case 5  ' $1400-$17FF : IRQ control
                Select Case offset And 3
                    Case 2
                        Return IrqDisable And 7
                    Case 3
                        Dim st = 0
                        If TimerRef IsNot Nothing AndAlso TimerRef.IrqPending Then st = st Or &H4
                        If Irq1Line Then st = st Or &H2
                        Return st
                    Case Else
                        Return 0
                End Select
            Case 6  ' $1800-$1BFF : interface CD-ROM² ($1800-$18FF)
                If cd IsNot Nothing AndAlso (offset And &HF00) = &H800 Then
                    ' Registres d'identification de la RAM étendue de la Super System Card,
                    ' lus par le BIOS ex_memopen ($FE92) : signature $AA/$55 en $18C1/$18C2 puis
                    ' un octet de config en $18C3 dont (val And $7F) doit valoir >= 3 (nombre
                    ' d'unités de 64 Ko de RAM étendue : $68-$7F = 192 Ko = 3). Sans ces
                    ' registres, ex_memopen échoue et les jeux Super CD affichent
                    ' « This disc only works on the SUPER CD-ROM² SYSTEM ».
                    If cdRam IsNot Nothing AndAlso offset >= &H18C0 AndAlso offset <= &H18C7 Then
                        Select Case offset
                            Case &H18C1 : Return &HAA
                            Case &H18C2 : Return &H55
                            Case &H18C3 : Return &H3
                            Case Else : Return 0
                        End Select
                    End If
                    Return cd.Read(offset And &HF)
                End If
                Return &HFF
            Case Else
                Return &HFF
        End Select
    End Function

    Private Sub WriteIO(offset As Integer, value As Integer)
        Select Case (offset >> 10) And 7
            Case 0
                WriteVideoArea(offset, value)
            Case 1
                vce.Write(offset And 7, value)
            Case 2
                psg.Write(offset And &HF, value)
            Case 3
                TimerRef.Write(offset And 1, value)
            Case 4
                joypad.Write(value)
            Case 5
                Select Case offset And 3
                    Case 2
                        Dim oldDisable = IrqDisable
                        IrqDisable = value And 7
                        ' Un bit de masquage passant de 1 à 0 = démasquage : diffère
                        ' la reconnaissance de l'IRQ d'une instruction.
                        If ((oldDisable And (Not IrqDisable)) And 7) <> 0 Then IrqEnableDelay = True
                    Case 3
                        ' Acquittement TIMER
                        If TimerRef IsNot Nothing Then TimerRef.AckIrq()
                End Select
            Case 6  ' $1800-$1BFF : interface CD-ROM² ($1800-$18FF)
                If cd IsNot Nothing AndAlso (offset And &HF00) = &H800 Then
                    cd.Write(offset And &HF, value)
                End If
        End Select
    End Sub

    ''' <summary>
    ''' Décodage de la zone vidéo en lecture. Sur PC Engine le VDC s'y répète tous les
    ''' quatre octets ; sur SuperGrafx c'est un bloc de 32 octets qui se répète, avec
    ''' le VPC et le second VDC logés dans les adresses ainsi libérées.
    ''' </summary>
    Private Function ReadVideoArea(offset As Integer) As Integer
        If vpc Is Nothing Then Return vdc.Read(offset And 3)

        Select Case (offset And &H1F) >> 3
            Case 0 : Return vdc.Read(offset And 3)      ' $00-$07 : VDC #1 et son miroir
            Case 1 : Return vpc.Read(offset And 7)      ' $08-$0F : VPC
            Case 2 : Return vdc2.Read(offset And 3)     ' $10-$17 : VDC #2 et son miroir
            Case Else : Return &HFF                     ' $18-$1F : inutilisé
        End Select
    End Function

    Private Sub WriteVideoArea(offset As Integer, value As Integer)
        If vpc Is Nothing Then
            vdc.Write(offset And 3, value)
            Return
        End If

        Select Case (offset And &H1F) >> 3
            Case 0 : vdc.Write(offset And 3, value)
            Case 1 : vpc.Write(offset And 7, value)
            Case 2 : vdc2.Write(offset And 3, value)
        End Select
    End Sub

    Public Sub SetMPR(index As Integer, value As Integer)
        If index >= 0 AndAlso index <= 7 Then mpr(index) = value And &HFF
    End Sub

    Public Function GetMPR(index As Integer) As Integer
        If index >= 0 AndAlso index <= 7 Then Return mpr(index)
        Return 0
    End Function


    ''' <summary>Copie de la BRAM, pour l'écrire sur disque.</summary>
    Public Function GetBram() As Byte()
        Return CType(bram.Clone(), Byte())
    End Function

    ''' <summary>Charge une BRAM lue sur disque (le surplus est ignoré).</summary>
    Public Sub SetBram(data() As Byte)
        If data Is Nothing Then Return
        Dim n = Math.Min(data.Length, bram.Length)
        Array.Copy(data, bram, n)
        BramModified = False
    End Sub

    ''' <summary>Écrit l'état de la mémoire dans une sauvegarde.</summary>
    Public Sub SaveState(w As System.IO.BinaryWriter)
        w.Write(workRam.Length)
        w.Write(workRam, 0, workRam.Length)
        w.Write(bram, 0, bram.Length)
        For i = 0 To 7
            w.Write(mpr(i))
        Next
        w.Write(IrqDisable)
    End Sub

    ''' <summary>Restaure l'état de la mémoire depuis une sauvegarde.</summary>
    Public Sub LoadState(r As System.IO.BinaryReader)
        Dim ramSize = r.ReadInt32()
        Dim ramData = r.ReadBytes(ramSize)
        Array.Copy(ramData, workRam, Math.Min(ramSize, workRam.Length))
        Array.Copy(r.ReadBytes(bram.Length), bram, bram.Length)
        For i = 0 To 7
            mpr(i) = r.ReadInt32()
        Next
        IrqDisable = r.ReadInt32()
        BramModified = True
    End Sub

    ''' <summary>Sauve la RAM CD étendue ($68-$87). Bloc séparé, écrit seulement
    ''' pour les jeux CD (le format des sauvegardes cartouche reste inchangé).</summary>
    Public Sub SaveCdRam(w As System.IO.BinaryWriter)
        Dim n = If(cdRam IsNot Nothing, cdRam.Length, 0)
        w.Write(n)
        If n > 0 Then w.Write(cdRam, 0, n)
    End Sub

    Public Sub LoadCdRam(r As System.IO.BinaryReader)
        Dim n = r.ReadInt32()
        Dim data = r.ReadBytes(n)
        If cdRam IsNot Nothing AndAlso n > 0 Then Array.Copy(data, cdRam, Math.Min(n, cdRam.Length))
    End Sub

End Class
