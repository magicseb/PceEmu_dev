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
    Public Const IRQ_CDDA_DONE As Integer = &H10        ' fin de lecture CD-DA (sous-canal)

    ' ---- CD-DA (lecture des pistes audio) ----
    Private cddaPlaying As Boolean
    Private cddaPaused As Boolean
    Private cddaStartLba As Integer
    Private cddaEndLba As Integer
    Private cddaCurLba As Integer
    Private cddaMode As Integer          ' 0/1 = jouer une fois puis IRQ ; 2 = boucler ; 3 = jusqu'au bout
    Private cddaSector(2351) As Byte     ' secteur audio courant (2352 o = 588 trames stéréo)
    Private cddaSampleInSector As Integer
    Private cddaSectorValid As Boolean

    ' ---- ADPCM (samples PCM différentiels du CD-ROM²) ----
    Private adpcmRam(&HFFFF) As Byte
    Private adpcmWriteAddr As Integer
    Private adpcmReadAddr As Integer
    Private adpcmLength As Integer
    Private adpcmDmaCtrl As Integer
    Private adpcmControl As Integer
    Private adpcmRate As Integer
    Private adpcmPlaying As Boolean
    Private adpcmEnded As Boolean
    Private adpcmPlayEnd As Integer          ' dernière adresse à jouer
    Private adpcmPredictor As Integer        ' état OKI ADPCM
    Private adpcmStepIndex As Integer
    Private adpcmHighNibble As Boolean       ' quel demi-octet lire ensuite
    Private adpcmFrac As Double              ' accumulateur de rééchantillonnage
    Private adpcmCurByte As Integer

    Private Shared ReadOnly AdpcmStep() As Integer = {
        16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66,
        73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230, 253,
        279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876,
        963, 1060, 1166, 1282, 1411, 1552}
    Private Shared ReadOnly AdpcmIndex() As Integer = {-1, -1, -1, -1, 2, 4, 6, 8}

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
            Case &H3        ' status IRQ : la lecture acquitte (efface les drapeaux de transfert)
                Dim v = irqStatus
                irqStatus = irqStatus And Not (IRQ_TRANSFER_DONE Or IRQ_TRANSFER_READY)
                Return v
            Case &HA        ' port de données ADPCM (lecture)
                Dim v = CInt(adpcmRam(adpcmReadAddr And &HFFFF))
                adpcmReadAddr = (adpcmReadAddr + 1) And &HFFFF
                Return v
            Case &HB        ' relecture contrôle DMA ADPCM
                Return adpcmDmaCtrl
            Case &HC        ' status ADPCM
                Dim st = 0
                If adpcmPlaying Then st = st Or &H8
                If adpcmEnded Then st = st Or &H1
                Return st
            Case &HD
                Return adpcmControl
            Case &H8        ' lecture de données avec auto-ACK (transfert en masse)
                Dim v = dataBusIn And &HFF
                ' L'auto-ACK ne fait avancer le transfert qu'en phase DataIn (IO=1, CD=0).
                ' En Status/Message (CD=1), lire $1808 renvoie l'octet courant sans avancer :
                ' évite que la boucle de lecture en masse du BIOS (secteur fixe de 2048 o)
                ' ne consomme les octets de status/message quand la fin des données tombe
                ' au milieu du secteur — ce qui empêchait le BIOS de voir la phase Status
                ' et bloquait certains jeux Arcade CD (ex. Forgotten Worlds) au chargement.
                If ph = Phase.DataIn Then AutoAck()
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
            Case &HA        ' port de données ADPCM (écriture)
                adpcmRam(adpcmWriteAddr And &HFFFF) = CByte(value And &HFF)
                adpcmWriteAddr = (adpcmWriteAddr + 1) And &HFFFF
            Case &HB        ' contrôle DMA ADPCM : bit0/1 = DMA auto depuis le CD
                adpcmDmaCtrl = value
                If (value And &H3) <> 0 Then AdpcmDmaFromCd()
            Case &H8
                adpcmAddrLatch = (adpcmAddrLatch And &HFF00) Or (value And &HFF)
                If (adpcmControl And &H10) <> 0 Then adpcmLength = adpcmLatchAddr()
            Case &H9
                adpcmAddrLatch = (adpcmAddrLatch And &HFF) Or ((value And &HFF) << 8)
                If (adpcmControl And &H10) <> 0 Then adpcmLength = adpcmLatchAddr()
            Case &HD        ' contrôle ADPCM ($180D) — sémantique Mednafen pcecd.cpp
                If (value And &H80) <> 0 Then       ' D7 : reset complet
                    adpcmAddrLatch = 0 : adpcmReadAddr = 0 : adpcmWriteAddr = 0
                    adpcmLength = 0 : adpcmPlaying = False : adpcmEnded = False
                    adpcmHighNibble = False : adpcmPredictor = 0 : adpcmStepIndex = 0
                    adpcmControl = 0
                Else
                    ' D5 ($20) : lecture marche/arrêt (sur front)
                    If adpcmPlaying AndAlso (value And &H20) = 0 Then adpcmPlaying = False
                    If (Not adpcmPlaying) AndAlso (value And &H20) <> 0 Then
                        adpcmPlaying = True : adpcmEnded = False
                        adpcmHighNibble = False : adpcmPredictor = 0 : adpcmStepIndex = 0 : adpcmFrac = 0.0
                    End If
                    ' D4 ($10) : longueur = latch (compteur décroissant)
                    If (value And &H10) <> 0 Then adpcmLength = adpcmLatchAddr() : adpcmEnded = False
                    ' D3 ($08) front : adresse de LECTURE = latch (ou latch-1 si D2=0)
                    If (adpcmControl And &H8) = 0 AndAlso (value And &H8) <> 0 Then
                        If (value And &H4) <> 0 Then adpcmReadAddr = adpcmLatchAddr() _
                        Else adpcmReadAddr = (adpcmLatchAddr() - 1) And &HFFFF
                    End If
                    ' D1 ($02) front : adresse d'ÉCRITURE = latch (ou latch-1 si D0=0)
                    If (adpcmControl And &H2) = 0 AndAlso (value And &H2) <> 0 Then
                        adpcmWriteAddr = adpcmLatchAddr()
                        If (value And &H1) = 0 Then adpcmWriteAddr = (adpcmWriteAddr - 1) And &HFFFF
                    End If
                    adpcmControl = value
                End If
            Case &HE        ' fréquence de lecture ADPCM
                adpcmRate = value
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
            Case &HD8       ' SAPSP : régler le début de lecture audio (et démarrer)
                cddaStartLba = ParsePlayPos(cmd(9), cmd(2), cmd(3), cmd(4), cmd(5))
                cddaCurLba = cddaStartLba
                cddaEndLba = disc.LeadOutLba
                cddaMode = 3
                cddaSectorValid = False
                cddaSampleInSector = 0
                cddaPlaying = True
                cddaPaused = (cmd(1) = 0)     ' mode 0 : armé mais en pause jusqu'au SAPEP
                EnterStatus(0)
            Case &HD9       ' SAPEP : régler la fin + le mode, lancer la lecture
                cddaEndLba = ParsePlayPos(cmd(9), cmd(2), cmd(3), cmd(4), cmd(5))
                cddaMode = cmd(1) And &H3
                cddaCurLba = cddaStartLba
                cddaSectorValid = False
                cddaSampleInSector = 0
                cddaPlaying = (cmd(1) <> 0)   ' mode 0 = SILENT : arrêter, sinon jouer
                cddaPaused = False
                EnterStatus(0)
            Case &HDA       ' PAUSE audio
                cddaPaused = True
                EnterStatus(0)
            Case &HDD       ' READ SUB-Q : status + position de lecture
                EnterDataIn(BuildSubQResponse())
            Case Else       ' inconnu : acquitté OK
                EnterStatus(0)
        End Select
    End Sub

    ''' <summary>Convertit une position de lecture MSF (BCD) en LBA absolu, borné au disque.</summary>
    ''' <summary>Convertit la position d'une commande D8/D9 en LBA absolu.
    ''' Le TYPE d'adresse est dans cmd(9) &amp; &amp;HC0 (sémantique Mednafen) :
    '''   &amp;H00 = LBA binaire (b3,b4,b5) ; &amp;H40 = MSF BCD (b2,b3,b4) ;
    '''   &amp;H80 = NUMÉRO DE PISTE (b2, BCD) → début de cette piste.</summary>
    Private Function ParsePlayPos(atype As Integer, b2 As Integer, b3 As Integer, b4 As Integer, b5 As Integer) As Integer
        Dim lba As Integer
        Select Case atype And &HC0
            Case &H0        ' LBA binaire
                lba = (b3 << 16) Or (b4 << 8) Or b5
            Case &H80       ' numéro de piste → LBA de début de piste (piste > dernière = lead-out)
                Dim trackNo = FromBcd(b2)
                If trackNo < 1 Then trackNo = 1
                If trackNo > disc.LastTrack Then Return disc.LeadOutLba
                lba = TrackStartLbaOf(trackNo)
            Case Else       ' &H40 : MSF BCD
                lba = (FromBcd(b2) * 60 + FromBcd(b3)) * 75 + FromBcd(b4) - 150
        End Select
        If lba < 0 Then lba = 0
        If lba >= disc.LeadOutLba Then lba = disc.LeadOutLba - 1
        Return lba
    End Function

    Private Function TrackStartLbaOf(trackNo As Integer) As Integer
        For i = 0 To disc.TrackCount - 1
            If disc.Track(i).Number = trackNo Then Return disc.Track(i).StartLba
        Next
        Return disc.LeadOutLba
    End Function

    ''' <summary>Réponse READ SUB-Q : 10 octets (status, contrôle, piste, index, MSF rel, MSF abs).</summary>
    Private Function BuildSubQResponse() As Byte()
        Dim status = If(cddaPlaying AndAlso Not cddaPaused, CByte(0), If(cddaPaused, CByte(2), CByte(3)))
        Dim trackNo = 1, trackStart = 0
        For i = 0 To disc.TrackCount - 1
            If disc.Track(i).StartLba <= cddaCurLba Then
                trackNo = disc.Track(i).Number : trackStart = disc.Track(i).StartLba
            End If
        Next
        Dim absMsf = MsfBcd(cddaCurLba)
        Dim relMsf = MsfBcd(If(cddaCurLba >= trackStart, cddaCurLba - trackStart, 0))
        Return New Byte() {status, ToBcd(trackNo), &H1,
                           relMsf(0), relMsf(1), relMsf(2),
                           absMsf(0), absMsf(1), absMsf(2), 0}
    End Function

    ''' <summary>
    ''' Produit numSamples échantillons stéréo entrelacés de CD-DA (44100 Hz, natif du CD)
    ''' et fait avancer la lecture. Retourne des zéros si rien ne joue.
    ''' </summary>
    Public Sub RenderAudio(buffer As Short(), numSamples As Integer)
        For i = 0 To numSamples - 1 Step 2
            If Not cddaPlaying OrElse cddaPaused Then
                buffer(i) = 0 : buffer(i + 1) = 0
                Continue For
            End If
            If Not cddaSectorValid Then
                If cddaCurLba >= cddaEndLba Then
                    EndOfPlayback() : buffer(i) = 0 : buffer(i + 1) = 0 : Continue For
                End If
                cddaSector = disc.ReadRaw(cddaCurLba)
                If cddaSector Is Nothing OrElse cddaSector.Length < 2352 Then cddaSector = New Byte(2351) {}
                cddaSectorValid = True
                cddaSampleInSector = 0
            End If
            Dim o = cddaSampleInSector * 4
            buffer(i) = DecodeSample(cddaSector, o)
            buffer(i + 1) = DecodeSample(cddaSector, o + 2)
            cddaSampleInSector += 1
            If cddaSampleInSector >= 588 Then
                cddaCurLba += 1
                cddaSectorValid = False
            End If
        Next
        RenderAdpcm(buffer, numSamples)
    End Sub

    ''' <summary>
    ''' Décode l'ADPCM OKI (4 bits) à sa fréquence propre (réglée par $180E), rééchantillonne
    ''' vers 44100 Hz, et l'ajoute (mono) aux deux canaux du tampon.
    ''' </summary>
    Private Sub RenderAdpcm(buffer As Short(), numSamples As Integer)
        If Not adpcmPlaying Then Return
        ' fréquence ADPCM ≈ 32000 / (16 - débit) Hz
        Dim div = 16 - (adpcmRate And &HF)
        If div < 1 Then div = 1
        Dim freq = 32087.5 / div
        Dim ratio = freq / 44100.0
        For i = 0 To numSamples - 1 Step 2
            If Not adpcmPlaying Then Exit For
            adpcmFrac += ratio
            While adpcmFrac >= 1.0
                AdpcmDecodeNext()
                adpcmFrac -= 1.0
                If Not adpcmPlaying Then Exit While
            End While
            Dim s16 = adpcmPredictor << 4
            If s16 > 32767 Then s16 = 32767
            If s16 < -32768 Then s16 = -32768
            Dim l = CInt(buffer(i)) + s16 : If l > 32767 Then l = 32767 Else If l < -32768 Then l = -32768
            Dim r = CInt(buffer(i + 1)) + s16 : If r > 32767 Then r = 32767 Else If r < -32768 Then r = -32768
            buffer(i) = CShort(l) : buffer(i + 1) = CShort(r)
        Next
    End Sub

    ''' <summary>Décode le prochain demi-octet ADPCM et met à jour le prédicteur.</summary>
    Private Sub AdpcmDecodeNext()
        ' adpcmHighNibble=False : début d'octet → charger l'octet + décoder le demi-octet HAUT
        ' adpcmHighNibble=True  : décoder le demi-octet BAS du même octet
        If Not adpcmHighNibble Then
            ' fin de sample : longueur épuisée (et D4 non tenu)
            If adpcmLength = 0 AndAlso (adpcmControl And &H10) = 0 Then
                adpcmEnded = True
                If (adpcmControl And &H40) <> 0 Then adpcmPlaying = False   ' D6 : stop en fin
            End If
            adpcmCurByte = CInt(adpcmRam(adpcmReadAddr And &HFFFF))
            adpcmReadAddr = (adpcmReadAddr + 1) And &HFFFF
            If adpcmLength <> 0 AndAlso (adpcmControl And &H10) = 0 Then adpcmLength -= 1
        End If
        If Not adpcmPlaying Then Return
        Dim nib = If(Not adpcmHighNibble, (adpcmCurByte >> 4) And &HF, adpcmCurByte And &HF)
        Dim stp = AdpcmStep(adpcmStepIndex)
        Dim diff = stp >> 3
        If (nib And 1) <> 0 Then diff += stp >> 2
        If (nib And 2) <> 0 Then diff += stp >> 1
        If (nib And 4) <> 0 Then diff += stp
        If (nib And 8) <> 0 Then adpcmPredictor -= diff Else adpcmPredictor += diff
        If adpcmPredictor > 2047 Then adpcmPredictor = 2047
        If adpcmPredictor < -2048 Then adpcmPredictor = -2048
        adpcmStepIndex += AdpcmIndex(nib And 7)
        If adpcmStepIndex < 0 Then adpcmStepIndex = 0
        If adpcmStepIndex > 48 Then adpcmStepIndex = 48
        adpcmHighNibble = Not adpcmHighNibble
    End Sub

    ''' <summary>Décode un échantillon 16 bits signé little-endian.</summary>
    ''' <summary>Adresse ADPCM latchée (via $1808/$1809 écrits par la System Card).</summary>
    Private Function adpcmLatchAddr() As Integer
        Return adpcmAddrLatch And &HFFFF
    End Function
    Private adpcmAddrLatch As Integer

    ''' <summary>
    ''' DMA auto depuis le CD : draine les données de la phase DataIn dans la RAM ADPCM
    ''' et termine le transfert (pose « transfert terminé » sur $1803), comme le fait le
    ''' matériel quand $180B a le bit de DMA.
    ''' </summary>
    Private Sub AdpcmDmaFromCd()
        If ph = Phase.DataIn Then
            While dataPos < dataBuf.Length
                adpcmRam(adpcmWriteAddr And &HFFFF) = dataBuf(dataPos)
                adpcmWriteAddr = (adpcmWriteAddr + 1) And &HFFFF
                dataPos += 1
            End While
            ' transfert de données terminé : signaler + laisser le lecteur en phase Status
            ' (le jeu lit ensuite l'octet de status, comme après une lecture normale)
            irqStatus = irqStatus Or IRQ_TRANSFER_DONE
            EnterStatus(0)
        End If
    End Sub

    Private Shared Function DecodeSample(data As Byte(), offset As Integer) As Short
        Dim v = CInt(data(offset)) Or (CInt(data(offset + 1)) << 8)
        If v >= &H8000 Then v -= &H10000
        Return CShort(v)
    End Function

    Private Sub EndOfPlayback()
        ' Modes SAPEP (cmd(1)) — sémantique du matériel (source Mednafen pcecd_drive) :
        '   1 = LOOP      : reboucler au début de segment, continuer (PAS d'IRQ)
        '   2 = INTERRUPT : arrêter + IRQ de fin
        '   0 = SILENT / 3 = NORMAL : arrêter, PAS d'IRQ
        Select Case cddaMode
            Case 1      ' LOOP : reboucler et continuer à jouer (musiques de menu)
                cddaCurLba = cddaStartLba : cddaSectorValid = False
            Case 2      ' INTERRUPT : arrêter + signaler la fin
                cddaPlaying = False
                irqStatus = irqStatus Or IRQ_CDDA_DONE
            Case Else   ' 0 (SILENT) / 3 (NORMAL) : arrêter sans IRQ
                cddaPlaying = False
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

    ' ===================== Sauvegarde d'état =====================
    ' `disc` (image CD) n'est PAS sérialisé : il est ré-inséré au chargement.

    Public Sub SaveState(w As System.IO.BinaryWriter)
        w.Write(sBsy) : w.Write(sReq) : w.Write(sMsg) : w.Write(sCd) : w.Write(sIo)
        w.Write(dataBusIn) : w.Write(dataBusOut) : w.Write(ackAsserted)
        w.Write(CInt(ph))
        For i = 0 To 15 : w.Write(cmd(i)) : Next
        w.Write(cmdIdx) : w.Write(cmdLen)
        w.Write(dataBuf.Length)
        If dataBuf.Length > 0 Then w.Write(dataBuf, 0, dataBuf.Length)
        w.Write(dataPos) : w.Write(statusByte)
        w.Write(irqEnable) : w.Write(irqStatus)
        w.Write(cddaPlaying) : w.Write(cddaPaused)
        w.Write(cddaStartLba) : w.Write(cddaEndLba) : w.Write(cddaCurLba) : w.Write(cddaMode)
        w.Write(cddaSector, 0, cddaSector.Length)
        w.Write(cddaSampleInSector) : w.Write(cddaSectorValid)
        w.Write(adpcmRam, 0, adpcmRam.Length)
        w.Write(adpcmWriteAddr) : w.Write(adpcmReadAddr) : w.Write(adpcmLength)
        w.Write(adpcmDmaCtrl) : w.Write(adpcmControl) : w.Write(adpcmRate)
        w.Write(adpcmPlaying) : w.Write(adpcmEnded) : w.Write(adpcmPlayEnd)
        w.Write(adpcmPredictor) : w.Write(adpcmStepIndex) : w.Write(adpcmHighNibble)
        w.Write(adpcmFrac) : w.Write(adpcmCurByte) : w.Write(adpcmAddrLatch)
        w.Write(BramEnabled)
    End Sub

    Public Sub LoadState(r As System.IO.BinaryReader)
        sBsy = r.ReadBoolean() : sReq = r.ReadBoolean() : sMsg = r.ReadBoolean() : sCd = r.ReadBoolean() : sIo = r.ReadBoolean()
        dataBusIn = r.ReadInt32() : dataBusOut = r.ReadInt32() : ackAsserted = r.ReadBoolean()
        ph = CType(r.ReadInt32(), Phase)
        For i = 0 To 15 : cmd(i) = r.ReadInt32() : Next
        cmdIdx = r.ReadInt32() : cmdLen = r.ReadInt32()
        Dim dbLen = r.ReadInt32()
        dataBuf = If(dbLen > 0, r.ReadBytes(dbLen), New Byte(-1) {})
        dataPos = r.ReadInt32() : statusByte = r.ReadInt32()
        irqEnable = r.ReadInt32() : irqStatus = r.ReadInt32()
        cddaPlaying = r.ReadBoolean() : cddaPaused = r.ReadBoolean()
        cddaStartLba = r.ReadInt32() : cddaEndLba = r.ReadInt32() : cddaCurLba = r.ReadInt32() : cddaMode = r.ReadInt32()
        Array.Copy(r.ReadBytes(cddaSector.Length), cddaSector, cddaSector.Length)
        cddaSampleInSector = r.ReadInt32() : cddaSectorValid = r.ReadBoolean()
        Array.Copy(r.ReadBytes(adpcmRam.Length), adpcmRam, adpcmRam.Length)
        adpcmWriteAddr = r.ReadInt32() : adpcmReadAddr = r.ReadInt32() : adpcmLength = r.ReadInt32()
        adpcmDmaCtrl = r.ReadInt32() : adpcmControl = r.ReadInt32() : adpcmRate = r.ReadInt32()
        adpcmPlaying = r.ReadBoolean() : adpcmEnded = r.ReadBoolean() : adpcmPlayEnd = r.ReadInt32()
        adpcmPredictor = r.ReadInt32() : adpcmStepIndex = r.ReadInt32() : adpcmHighNibble = r.ReadBoolean()
        adpcmFrac = r.ReadDouble() : adpcmCurByte = r.ReadInt32() : adpcmAddrLatch = r.ReadInt32()
        BramEnabled = r.ReadBoolean()
    End Sub

End Class
