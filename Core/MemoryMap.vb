''' <summary>MMU PC Engine - mapping 21 bits via MPR0-7, décodage I/O</summary>
Public Class MemoryMap

    Private rom() As Byte
    Private romMask As Integer
    Private workRam(&H1FFF) As Byte     ' 8 Ko
    Private bram(&H7FF) As Byte         ' 2 Ko
    Private mpr(7) As Integer

    ' Contrôle IRQ ($1402/$1403)
    Public IrqDisable As Integer = 0

    Private cartridge As Cartridge
    Private vce As Vce
    Private vdc As Vdc
    Private psg As Psg
    Public TimerRef As CpuTimer
    Private joypad As Joypad

    Public Sub New(cart As Cartridge)
        cartridge = cart
        rom = cart.RomData
        ' Masque pour miroirs ROM (puissance de 2)
        romMask = 1
        While romMask < rom.Length
            romMask <<= 1
        End While
        romMask -= 1
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

    ''' <summary>Lit un octet (adresse logique 16 bits)</summary>
    Public Function ReadByte(logicalAddr As Integer) As Integer
        Dim page = mpr((logicalAddr >> 13) And 7)
        Dim offset = logicalAddr And &H1FFF

        If page = &HFF Then
            Return ReadIO(offset)
        ElseIf page < &H80 Then
            ' ROM avec miroirs
            Dim addr = ((page << 13) Or offset) And romMask
            If addr < rom.Length Then Return rom(addr)
            Return &HFF
        ElseIf page >= &HF8 AndAlso page <= &HFB Then
            ' RAM travail (miroir 8 Ko sur PCE standard)
            Return workRam(offset)
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
        ElseIf page >= &HF8 AndAlso page <= &HFB Then
            workRam(offset) = CByte(value)
        ElseIf page = &HF7 Then
            bram(offset And &H7FF) = CByte(value)
        ElseIf page < &H80 Then
            ' Écriture en zone ROM : mapper SF2
            If TypeOf cartridge Is CartridgeSF2 AndAlso (offset And &H1FFC) = &H1FF0 Then
                CType(cartridge, CartridgeSF2).SetBankRegister(offset And 3, value)
            End If
        End If
    End Sub

    ''' <summary>Décodage page I/O ($FF)</summary>
    Private Function ReadIO(offset As Integer) As Integer
        Select Case (offset >> 10) And 7
            Case 0  ' $0000-$03FF : VDC
                Return vdc.Read(offset And 3)
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
                        If vdc IsNot Nothing AndAlso vdc.IrqLine Then st = st Or &H2
                        Return st
                    Case Else
                        Return 0
                End Select
            Case Else
                Return &HFF
        End Select
    End Function

    Private Sub WriteIO(offset As Integer, value As Integer)
        Select Case (offset >> 10) And 7
            Case 0
                vdc.Write(offset And 3, value)
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
                        IrqDisable = value And 7
                    Case 3
                        ' Acquittement TIMER
                        If TimerRef IsNot Nothing Then TimerRef.AckIrq()
                End Select
        End Select
    End Sub

    Public Sub SetMPR(index As Integer, value As Integer)
        If index >= 0 AndAlso index <= 7 Then mpr(index) = value And &HFF
    End Sub

    Public Function GetMPR(index As Integer) As Integer
        If index >= 0 AndAlso index <= 7 Then Return mpr(index)
        Return 0
    End Function

End Class
