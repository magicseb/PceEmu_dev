''' <summary>
''' Interface CD-ROM² (SCSI) — registres $1800-$180F. Modélise le bus SCSI vers le
''' lecteur comme une machine à phases (Commande → Données entrantes → Status → Message).
'''
''' Handshake : le lecteur pose REQ quand un octet est prêt ; l'initiateur (System Card)
''' asserte ACK via $1802 bit7 (ce qui fait retomber REQ), puis relâche ACK (REQ remonte
''' pour l'octet suivant ou change de phase). Les données en masse peuvent aussi être
''' lues via $1808 (auto-ACK).
'''
''' Commandes gérées pour le boot : TEST UNIT READY ($00), READ ($08), GET DIR INFO/TOC
''' ($DE). Les commandes audio/ADPCM sont acquittées sans effet (ADPCM/CD-DA à faire).
''' </summary>
Public Class CdRom

    Private ReadOnly disc As CdImage

    ' Signaux du bus SCSI
    Private sBsy As Boolean
    Private sReq As Boolean
    Private sMsg As Boolean
    Private sCd As Boolean       ' Control/Data : 1 = phase commande/status/message
    Private sIo As Boolean       ' Input/Output : 1 = lecteur -> initiateur
    Private dataBusIn As Integer ' octet présenté par le lecteur
    Private dataBusOut As Integer ' octet écrit par l'initiateur (commande)
    Private ackAsserted As Boolean

    Private Enum Phase
        BusFree
        Command
        DataIn
        Status
        MessageIn
    End Enum
    Private ph As Phase = Phase.BusFree

    Private cmd(15) As Integer
    Private cmdIdx As Integer
    Private cmdLen As Integer
    Private dataBuf() As Byte = New Byte(-1) {}
    Private dataPos As Integer
    Private statusByte As Integer

    ' IRQ CD (IRQ2)
    Private irqEnable As Integer
    Private irqStatus As Integer
    Public Const IRQ_TRANSFER_DONE As Integer = &H20
    Public Const IRQ_TRANSFER_READY As Integer = &H40

    Public BramEnabled As Boolean = False

    Public Sub New(discImage As CdImage)
        disc = discImage
    End Sub

    Public ReadOnly Property IrqLine As Boolean
        Get
            Return (irqStatus And irqEnable) <> 0
        End Get
    End Property

    ' ===================== Accès registres $1800-$180F =====================

    Public Function Read(reg As Integer) As Integer
        Select Case reg And &HF
            Case &H0        ' status du bus SCSI
                Dim r = 0
                If sBsy Then r = r Or &H80
                If sReq Then r = r Or &H40
                If sMsg Then r = r Or &H20
                If sCd Then r = r Or &H10
                If sIo Then r = r Or &H8
                Return r
            Case &H1        ' bus de données SCSI (sans ACK)
                Return dataBusIn And &HFF
            Case &H2        ' relecture enable IRQ
                Return irqEnable
            Case &H3        ' status IRQ
                Return irqStatus
            Case &H8        ' lecture de données avec auto-ACK (transfert en masse)
                Dim v = dataBusIn And &HFF
                AutoAck()
                Return v
            Case Else
                Return 0
        End Select
    End Function

    Public Sub Write(reg As Integer, value As Integer)
        value = value And &HFF
        Select Case reg And &HF
            Case &H0        ' sélection du lecteur (SEL) -> phase commande
                StartSelection()
            Case &H1        ' octet de commande vers le bus (latché, consommé à l'ACK)
                dataBusOut = value
            Case &H2        ' bit7 = ACK ; bits 0-6 = enable IRQ
                irqEnable = value And &H7F
                Dim ackNow = (value And &H80) <> 0
                If ackNow AndAlso Not ackAsserted Then OnAckAssert()
                If (Not ackNow) AndAlso ackAsserted Then OnAckRelease()
                ackAsserted = ackNow
            Case &H4        ' reset CD (bit1)
                If (value And &H2) <> 0 Then ResetBus()
            Case &H7        ' active la BRAM
                BramEnabled = (value And &H80) <> 0
            Case Else
                ' ADPCM / audio : ignorés pour l'instant
        End Select
    End Sub

    ' ===================== Machine à états SCSI =====================

    Private Sub ResetBus()
        ph = Phase.BusFree
        sBsy = False : sReq = False : sMsg = False : sCd = False : sIo = False
        cmdIdx = 0 : dataPos = 0 : irqStatus = 0 : ackAsserted = False
    End Sub

    Private Sub StartSelection()
        ph = Phase.Command
        sBsy = True : sMsg = False : sCd = True : sIo = False : sReq = True
        cmdIdx = 0 : cmdLen = 6
    End Sub

    ''' <summary>ACK asserté : le lecteur retire REQ (et consomme l'octet de commande courant).</summary>
    Private Sub OnAckAssert()
        If ph = Phase.Command Then
            cmd(cmdIdx) = dataBusOut
            If cmdIdx = 0 Then cmdLen = CommandLength(dataBusOut)
        End If
        sReq = False
    End Sub

    ''' <summary>ACK relâché : le lecteur avance (octet/phase suivants) et repose REQ.</summary>
    Private Sub OnAckRelease()
        Select Case ph
            Case Phase.Command
                cmdIdx += 1
                If cmdIdx >= cmdLen Then
                    ExecuteCommand()
                Else
                    sReq = True
                End If
            Case Phase.DataIn
                dataPos += 1
                If dataPos < dataBuf.Length Then
                    dataBusIn = dataBuf(dataPos)
                    sReq = True
                Else
                    EnterStatus(0)
                End If
            Case Phase.Status
                EnterMessage()
            Case Phase.MessageIn
                EnterBusFree()
        End Select
    End Sub

    ''' <summary>Lecture $1808 : équivaut à un cycle ACK complet (assert + release).</summary>
    Private Sub AutoAck()
        OnAckAssert()
        OnAckRelease()
    End Sub

    Private Function CommandLength(opcode As Integer) As Integer
        If opcode < &H20 Then Return 6
        Return 10
    End Function

    Private Sub ExecuteCommand()
        Dim opcode = cmd(0)
        Select Case opcode
            Case &H0        ' TEST UNIT READY
                EnterStatus(0)
            Case &H8        ' READ(6)
                Dim lba = ((cmd(1) And &H1F) << 16) Or (cmd(2) << 8) Or cmd(3)
                Dim count = cmd(4)
                If count = 0 Then count = 256
                Dim buf(count * 2048 - 1) As Byte
                For i = 0 To count - 1
                    System.Array.Copy(disc.ReadUserData(lba + i), 0, buf, i * 2048, 2048)
                Next
                EnterDataIn(buf)
            Case &HDE       ' GET DIR INFO (TOC)
                EnterDataIn(BuildTocResponse())
            Case Else       ' audio / sous-code / inconnu : acquittés OK
                EnterStatus(0)
        End Select
    End Sub

    Private Function BuildTocResponse() As Byte()
        Select Case cmd(1)
            Case &H0        ' première / dernière piste (BCD)
                Return New Byte() {ToBcd(disc.FirstTrack), ToBcd(disc.LastTrack)}
            Case &H1        ' lead-out (MSF, BCD)
                Return MsfBcd(disc.LeadOutLba)
            Case &H2        ' début de piste + type
                Dim trackNo = FromBcd(cmd(2))
                Dim startLba = 0, isAudio = False
                For i = 0 To disc.TrackCount - 1
                    If disc.Track(i).Number = trackNo Then
                        startLba = disc.Track(i).StartLba : isAudio = disc.Track(i).IsAudio
                    End If
                Next
                Dim msf = MsfBcd(startLba)
                Return New Byte() {msf(0), msf(1), msf(2), If(isAudio, CByte(0), CByte(&H4))}
            Case Else
                Return New Byte(-1) {}
        End Select
    End Function

    Private Sub EnterDataIn(buf As Byte())
        dataBuf = buf : dataPos = 0
        If dataBuf.Length = 0 Then EnterStatus(0) : Return
        ph = Phase.DataIn
        sBsy = True : sMsg = False : sCd = False : sIo = True : sReq = True
        dataBusIn = dataBuf(0)
        irqStatus = irqStatus Or IRQ_TRANSFER_READY
    End Sub

    Private Sub EnterStatus(code As Integer)
        statusByte = code : ph = Phase.Status
        sBsy = True : sMsg = False : sCd = True : sIo = True : sReq = True
        dataBusIn = statusByte
    End Sub

    Private Sub EnterMessage()
        ph = Phase.MessageIn
        sBsy = True : sMsg = True : sCd = True : sIo = True : sReq = True
        dataBusIn = 0
    End Sub

    Private Sub EnterBusFree()
        ph = Phase.BusFree
        sBsy = False : sReq = False : sMsg = False : sCd = False : sIo = False
        irqStatus = irqStatus Or IRQ_TRANSFER_DONE
    End Sub

    ' ===================== Utilitaires =====================

    Private Shared Function ToBcd(v As Integer) As Byte
        Return CByte(((v \ 10) << 4) Or (v Mod 10))
    End Function
    Private Shared Function FromBcd(v As Integer) As Integer
        Return ((v >> 4) And &HF) * 10 + (v And &HF)
    End Function
    Private Shared Function MsfBcd(lba As Integer) As Byte()
        Dim f = lba + 150
        Return New Byte() {ToBcd(f \ (75 * 60)), ToBcd((f \ 75) Mod 60), ToBcd(f Mod 75)}
    End Function

End Class
