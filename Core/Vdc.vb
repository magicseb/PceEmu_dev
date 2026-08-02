''' <summary>VDC HuC6270 - VRAM en words, rendu tilemap + sprites, IRQ, DMA</summary>
Public Class Vdc

    ' VRAM : 32K words (64 Ko)
    Private vram(&H7FFF) As Integer

    ' SATB interne : 64 sprites × 4 words
    Private satb(255) As Integer

    ' Registres
    Private regSelect As Integer = 0
    Private regs(31) As Integer          ' Valeurs 16 bits des registres
    Private writeLatch As Integer = 0    ' LSB latché pour écriture VWR
    Private readBuffer As Integer = 0    ' Buffer de lecture VRAM

    ' Status
    Private statusReg As Integer = 0
    Public IrqLine As Boolean = False
    ' VBlank différée d'une scanline lorsqu'un RCR coïncide avec la ligne de VBlank :
    ' le CPU doit pouvoir acquitter le RCR avant que la VBlank ne soit assertée, sinon
    ' les deux bits de status fusionnent et un handler « RCR ou VBlank » rate la VBlank.
    Private vblankPending As Boolean = False

    ' Compteurs de diagnostic (sans effet sur l'émulation)
    Public Shared DbgCollisionCount As Long = 0
    Public Shared DbgCollisionIrqCount As Long = 0

    ' SATB DMA
    Private satbPending As Boolean = False
    Private satbAuto As Boolean = False

    ' Compteur de scroll vertical interne (relatché quand BYR est écrit)
    Private bgYCounter As Integer = 0
    Private byrWritten As Boolean = False

    Private vce As Vce
    Private cpu As Cpu6280

    ' Buffers de ligne réutilisés (évite les allocations par scanline)
    ''' <summary>
    ''' Sortie de la dernière scanline rendue, un code par pixel : -1 pour un pixel
    ''' transparent, sinon l'index 0-511 dans la palette du VCE. C'est exactement ce
    ''' que le vrai VDC envoie sur son bus 9 bits, et ce que le VPC mélange sur
    ''' SuperGrafx.
    ''' </summary>
    Public ReadOnly LineOutput(PceConstants.SCREEN_WIDTH - 1) As Integer

    Private lineBgSolid(PceConstants.SCREEN_WIDTH - 1) As Boolean
    Private lineSprCovered(PceConstants.SCREEN_WIDTH - 1) As Boolean
    Private lineSpr0Mask(PceConstants.SCREEN_WIDTH - 1) As Boolean

    ' Indices registres
    Private Const R_MAWR = 0
    Private Const R_MARR = 1
    Private Const R_VRW = 2
    Private Const R_CR = 5
    Private Const R_RCR = 6
    Private Const R_BXR = 7
    Private Const R_BYR = 8
    Private Const R_MWR = 9
    Private Const R_HSR = 10
    Private Const R_HDR = 11
    Private Const R_VPR = 12
    Private Const R_VDW = 13
    Private Const R_VCR = 14
    Private Const R_DCR = 15
    Private Const R_SOUR = 16
    Private Const R_DESR = 17
    Private Const R_LENR = 18
    Private Const R_DVSSR = 19

    ' Bits status
    Private Const ST_CR = &H1    ' Sprite collision
    Private Const ST_OVR = &H2   ' Sprite overflow
    Private Const ST_RR = &H4    ' Raster compare
    Private Const ST_DS = &H8    ' SATB DMA terminé
    Private Const ST_DV = &H10   ' VRAM DMA terminé
    Private Const ST_VD = &H20   ' VBlank

    Public Sub New(vceRef As Vce)
        vce = vceRef
    End Sub

    Public Sub ConnectCPU(cpuRef As Cpu6280)
        cpu = cpuRef
    End Sub

    ''' <summary>Incrément d'adresse selon CR bits 11-12</summary>
    Private Function AddrInc() As Integer
        Select Case (regs(R_CR) >> 11) And 3
            Case 0 : Return 1
            Case 1 : Return &H20
            Case 2 : Return &H40
            Case Else : Return &H80
        End Select
    End Function

    ''' <summary>Lecture registre VDC ($0000-$0003)</summary>
    Public Function Read(offset As Integer) As Integer
        Select Case offset And 3
            Case 0
                Dim st = statusReg
                statusReg = 0
                IrqLine = False
                Return st
            Case 2
                Return readBuffer And &HFF
            Case 3
                Dim hi = (readBuffer >> 8) And &HFF
                If regSelect = R_VRW Then
                    ' Auto-increment + prefetch
                    regs(R_MARR) = (regs(R_MARR) + AddrInc()) And &HFFFF
                    readBuffer = vram(regs(R_MARR) And &H7FFF)
                End If
                Return hi
            Case Else
                Return 0
        End Select
    End Function

    ''' <summary>Écriture registre VDC</summary>
    Public Sub Write(offset As Integer, value As Integer)
        value = value And &HFF
        Select Case offset And 3
            Case 0
                regSelect = value And &H1F
            Case 2
                HandleLSB(value)
            Case 3
                HandleMSB(value)
        End Select
    End Sub

    Private Sub HandleLSB(value As Integer)
        Select Case regSelect
            Case R_VRW
                writeLatch = value
            Case Else
                regs(regSelect) = (regs(regSelect) And &HFF00) Or value
                If regSelect = R_MARR Then
                    readBuffer = vram(regs(R_MARR) And &H7FFF)
                ElseIf regSelect = R_BYR Then
                    byrWritten = True
                End If
        End Select
    End Sub

    Private Sub HandleMSB(value As Integer)
        Select Case regSelect
            Case R_VRW
                ' Écriture word en VRAM à MAWR
                Dim addr = regs(R_MAWR) And &H7FFF
                vram(addr) = writeLatch Or (value << 8)
                regs(R_MAWR) = (regs(R_MAWR) + AddrInc()) And &HFFFF
            Case R_LENR
                regs(R_LENR) = (regs(R_LENR) And &HFF) Or (value << 8)
                ExecuteVramDMA()
            Case R_DVSSR
                regs(R_DVSSR) = (regs(R_DVSSR) And &HFF) Or (value << 8)
                satbPending = True
            Case Else
                regs(regSelect) = (regs(regSelect) And &HFF) Or (value << 8)
                If regSelect = R_MARR Then
                    readBuffer = vram(regs(R_MARR) And &H7FFF)
                ElseIf regSelect = R_BYR Then
                    byrWritten = True
                End If
        End Select
    End Sub

    ''' <summary>DMA VRAM → VRAM</summary>
    Private Sub ExecuteVramDMA()
        Dim srcInc = If((regs(R_DCR) And &H4) <> 0, -1, 1)
        Dim dstInc = If((regs(R_DCR) And &H8) <> 0, -1, 1)
        Dim src = regs(R_SOUR)
        Dim dst = regs(R_DESR)
        Dim len = regs(R_LENR) + 1

        For i = 1 To len
            vram(dst And &H7FFF) = vram(src And &H7FFF)
            src = (src + srcInc) And &HFFFF
            dst = (dst + dstInc) And &HFFFF
        Next
        regs(R_SOUR) = src
        regs(R_DESR) = dst

        ' IRQ fin DMA VRAM si activée
        If (regs(R_DCR) And &H2) <> 0 Then
            statusReg = statusReg Or ST_DV
            AssertIrq()
        End If
    End Sub

    ''' <summary>Transfert SATB (appelé au VBlank)</summary>
    Private Sub DoSatbTransfer()
        Dim src = regs(R_DVSSR) And &H7FFF
        For i = 0 To 255
            satb(i) = vram((src + i) And &H7FFF)
        Next
        satbPending = False
        satbAuto = (regs(R_DCR) And &H10) <> 0
        If (regs(R_DCR) And &H1) <> 0 Then
            statusReg = statusReg Or ST_DS
            AssertIrq()
        End If
    End Sub

    Private Sub AssertIrq()
        IrqLine = True
    End Sub

    ''' <summary>Hauteur d'affichage active (lignes)</summary>
    Public ReadOnly Property DisplayHeight As Integer
        Get
            Dim h = (regs(R_VDW) And &H1FF) + 1
            If h < 1 Then h = 240
            If h > PceConstants.ACTIVE_SCANLINES Then h = PceConstants.ACTIVE_SCANLINES
            Return h
        End Get
    End Property

    ''' <summary>Largeur d'affichage active (pixels)</summary>
    Public ReadOnly Property DisplayWidth As Integer
        Get
            Dim w = ((regs(R_HDR) And &H7F) + 1) * 8
            If w < 8 Then w = 256
            If w > PceConstants.SCREEN_WIDTH Then w = PceConstants.SCREEN_WIDTH
            Return w
        End Get
    End Property

    ''' <summary>Traite une scanline : IRQ RCR, rendu, VBlank</summary>
    Public Sub DoScanline(scanline As Integer)
        ' VBlank différée depuis la scanline précédente (RCR coïncident) : on l'asserte
        ' maintenant, le CPU ayant eu le temps d'acquitter le RCR entre-temps.
        If vblankPending Then
            vblankPending = False
            statusReg = statusReg Or ST_VD
            If (regs(R_CR) And &H8) <> 0 Then
                AssertIrq()
            End If
            If satbPending Or satbAuto Then
                DoSatbTransfer()
            End If
        End If

        ' Compteur de scroll vertical : latché à BYR en début de frame,
        ' incrémenté par ligne, relatché si le jeu écrit BYR (split raster)
        If scanline = 0 Then
            bgYCounter = regs(R_BYR)
            byrWritten = False
        ElseIf byrWritten Then
            bgYCounter = regs(R_BYR) + 1
            byrWritten = False
        Else
            bgYCounter += 1
        End If

        ' IRQ Raster Compare : RCR compare (scanline affichée + 64)
        Dim rcrFired = False
        If (regs(R_CR) And &H4) <> 0 Then
            If scanline + 64 = (regs(R_RCR) And &H3FF) Then
                statusReg = statusReg Or ST_RR
                AssertIrq()
                rcrFired = True
            End If
        End If

        If scanline < DisplayHeight Then
            RenderLine(scanline)
        ElseIf scanline = DisplayHeight Then
            If rcrFired Then
                ' RCR coïncide avec la ligne de VBlank : différer la VBlank d'une
                ' scanline pour que le CPU serve/acquitte d'abord le RCR (le matériel
                ' génère deux interruptions distinctes séparées par des cycles CPU).
                vblankPending = True
            Else
                ' Début VBlank
                statusReg = statusReg Or ST_VD
                If (regs(R_CR) And &H8) <> 0 Then
                    AssertIrq()
                End If
                ' SATB DMA
                If satbPending Or satbAuto Then
                    DoSatbTransfer()
                End If
            End If
        End If
    End Sub

    ''' <summary>Dimensions tilemap depuis MWR</summary>
    Private Sub GetMapSize(ByRef w As Integer, ByRef h As Integer)
        Select Case (regs(R_MWR) >> 4) And 3
            Case 0 : w = 32
            Case 1 : w = 64
            Case Else : w = 128
        End Select
        h = If(((regs(R_MWR) >> 6) And 1) = 0, 32, 64)
    End Sub

    ''' <summary>Rend une scanline complète (BG + sprites)</summary>
    Private Sub RenderLine(scanline As Integer)
        Dim width = DisplayWidth
        Dim bgEnabled = (regs(R_CR) And &H80) <> 0
        Dim sprEnabled = (regs(R_CR) And &H40) <> 0

        ' Buffer de solidité BG (pour priorités sprites)
        Dim bgSolid = lineBgSolid
        Array.Clear(bgSolid, 0, width)

        If bgEnabled Then
            Dim mapW = 32, mapH = 32
            GetMapSize(mapW, mapH)

            Dim bgY = bgYCounter And (mapH * 8 - 1)
            Dim tileRow = bgY >> 3
            Dim fineY = bgY And 7
            Dim bxr = regs(R_BXR)

            For x = 0 To width - 1
                Dim bgX = (bxr + x) And (mapW * 8 - 1)
                Dim tileCol = bgX >> 3
                Dim fineX = bgX And 7

                Dim batEntry = vram((tileRow * mapW + tileCol) And &H7FFF)
                Dim tileIndex = batEntry And &HFFF
                Dim palette = (batEntry >> 12) And &HF

                Dim patternAddr = (tileIndex << 4) + fineY
                Dim w01 = vram(patternAddr And &H7FFF)
                Dim w23 = vram((patternAddr + 8) And &H7FFF)

                Dim bit = 7 - fineX
                Dim pix = ((w01 >> bit) And 1) Or
                          (((w01 >> (8 + bit)) And 1) << 1) Or
                          (((w23 >> bit) And 1) << 2) Or
                          (((w23 >> (8 + bit)) And 1) << 3)

                If pix = 0 Then
                    LineOutput(x) = -1
                Else
                    LineOutput(x) = (palette << 4) Or pix
                    bgSolid(x) = True
                End If
            Next
        Else
            ' Fond éteint : le VDC n'émet plus rien
            For x = 0 To width - 1
                LineOutput(x) = -1
            Next
        End If

        ' ===== Sprites =====
        If sprEnabled Then
            RenderSpritesLine(scanline, width, bgSolid)
        End If
    End Sub

    ''' <summary>Rend les sprites d'une scanline (limite 16/ligne)</summary>
    Private Sub RenderSpritesLine(scanline As Integer, width As Integer, bgSolid() As Boolean)
        Dim sprCovered = lineSprCovered
        Array.Clear(sprCovered, 0, width)

        ' Pixels opaques du sprite 0 sur cette ligne (détection de collision)
        Dim spr0Mask = lineSpr0Mask
        Array.Clear(spr0Mask, 0, width)
        Dim spr0OnLine = False

        Dim count = 0

        For sprIdx = 0 To 63
            Dim base = sprIdx * 4
            Dim sy = (satb(base) And &H3FF) - 64
            Dim sx = (satb(base + 1) And &H3FF) - 32
            Dim code = satb(base + 2)
            Dim attr = satb(base + 3)

            Dim cgx = (attr >> 8) And 1          ' 0=16 large, 1=32
            Dim cgy = (attr >> 12) And 3         ' 0=16, 1=32, 2=32, 3=64 haut
            Dim sprW = (cgx + 1) * 16
            Dim sprH As Integer
            Select Case cgy
                Case 0 : sprH = 16
                Case 1, 2 : sprH = 32
                Case Else : sprH = 64
            End Select

            If scanline < sy Or scanline >= sy + sprH Then Continue For

            Dim isSprite0 = (sprIdx = 0)
            If isSprite0 Then spr0OnLine = True

            count += 1
            If count > 16 Then
                statusReg = statusReg Or ST_OVR
                If (regs(R_CR) And &H2) <> 0 Then AssertIrq()
                Exit For
            End If

            Dim yFlip = (attr And &H8000) <> 0
            Dim xFlip = (attr And &H800) <> 0
            Dim prio = (attr And &H80) <> 0     ' 1 = devant BG
            Dim pal = attr And &HF

            Dim lineInSpr = scanline - sy
            If yFlip Then lineInSpr = sprH - 1 - lineInSpr

            ' Adresse pattern : code bits 10-1, cellule 16×16 = 64 words
            Dim patBase = (code And &H7FE) << 5   ' bit 0 ignoré par le HW

            ' Masquer bits selon taille
            If cgx = 1 Then patBase = patBase And Not &H40
            If cgy >= 1 Then patBase = patBase And Not &H80
            If cgy = 3 Then patBase = patBase And Not &H180

            Dim cellY = lineInSpr >> 4
            Dim fineY = lineInSpr And &HF

            For px = 0 To sprW - 1
                Dim screenX = sx + px
                If screenX < 0 Or screenX >= width Then Continue For

                Dim colInSpr = If(xFlip, sprW - 1 - px, px)
                Dim cellX = colInSpr >> 4
                Dim fineX = colInSpr And &HF

                Dim cellAddr = patBase + cellY * &H80 + cellX * &H40
                Dim bit = 15 - fineX
                Dim w0 = vram((cellAddr + fineY) And &H7FFF)
                Dim w1 = vram((cellAddr + fineY + 16) And &H7FFF)
                Dim w2 = vram((cellAddr + fineY + 32) And &H7FFF)
                Dim w3 = vram((cellAddr + fineY + 48) And &H7FFF)

                Dim pix = ((w0 >> bit) And 1) Or
                          (((w1 >> bit) And 1) << 1) Or
                          (((w2 >> bit) And 1) << 2) Or
                          (((w3 >> bit) And 1) << 3)

                If pix = 0 Then Continue For

                ' Collision sprite 0 : deux pixels opaques superposés, que le sprite
                ' soit visible ou non (occlusion et priorité n'entrent pas en jeu)
                If isSprite0 Then
                    spr0Mask(screenX) = True
                ElseIf spr0OnLine AndAlso spr0Mask(screenX) Then
                    statusReg = statusReg Or ST_CR
                    DbgCollisionCount += 1
                    If (regs(R_CR) And &H1) <> 0 Then
                        AssertIrq()
                        DbgCollisionIrqCount += 1
                    End If
                End If

                ' Le premier sprite rencontré occupe le pixel, les suivants sont masqués
                If sprCovered(screenX) Then Continue For
                sprCovered(screenX) = True

                ' Priorité : devant BG, ou derrière (visible seulement si BG transparent)
                If prio OrElse Not bgSolid(screenX) Then
                    LineOutput(screenX) = 256 + (pal << 4) + pix
                End If
            Next
        Next
    End Sub


    ''' <summary>Écrit l'état du VDC dans une sauvegarde.</summary>
    Public Sub SaveState(w As System.IO.BinaryWriter)
        For i = 0 To vram.Length - 1
            w.Write(vram(i))
        Next
        For i = 0 To satb.Length - 1
            w.Write(satb(i))
        Next
        For i = 0 To regs.Length - 1
            w.Write(regs(i))
        Next
        w.Write(regSelect) : w.Write(writeLatch) : w.Write(readBuffer) : w.Write(statusReg)
        w.Write(IrqLine) : w.Write(satbPending) : w.Write(satbAuto)
        w.Write(bgYCounter) : w.Write(byrWritten)
    End Sub

    ''' <summary>Restaure l'état du VDC depuis une sauvegarde.</summary>
    Public Sub LoadState(r As System.IO.BinaryReader)
        For i = 0 To vram.Length - 1
            vram(i) = r.ReadInt32()
        Next
        For i = 0 To satb.Length - 1
            satb(i) = r.ReadInt32()
        Next
        For i = 0 To regs.Length - 1
            regs(i) = r.ReadInt32()
        Next
        regSelect = r.ReadInt32() : writeLatch = r.ReadInt32()
        readBuffer = r.ReadInt32() : statusReg = r.ReadInt32()
        IrqLine = r.ReadBoolean() : satbPending = r.ReadBoolean() : satbAuto = r.ReadBoolean()
        bgYCounter = r.ReadInt32() : byrWritten = r.ReadBoolean()
    End Sub

End Class
