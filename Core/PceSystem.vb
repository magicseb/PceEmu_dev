''' <summary>Système PC Engine - orchestration scanline par scanline</summary>
Public Class PceSystem

    Private cpu As Cpu6280
    Private mpu As MemoryMap
    Private vce As Vce
    Private vdc As Vdc
    Private psg As Psg
    Private timer As CpuTimer
    Private joypad As Joypad

    ' Uniquement en mode SuperGrafx
    Private vdc2 As Vdc = Nothing
    Private vpc As Vpc = Nothing

    Private cartridge As Cartridge
    Private cd As CdRom = Nothing
    Private framebuffer(PceConstants.SCREEN_WIDTH * PceConstants.SCREEN_HEIGHT - 1) As Integer

    Private _frameCount As Integer = 0
    Private cycleDebt As Integer = 0

    ' Signature d'une sauvegarde d'état : "PCEST" suivi du numéro de format
    Private Shared ReadOnly STATE_MAGIC As Byte() = {&H50, &H43, &H45, &H53, &H54}
    Private Const STATE_VERSION As Integer = 1

    ''' <summary>
    ''' BRAM d'une console neuve. Les jeux reconnaissent une mémoire formatée à cet
    ''' en-tête ; sans lui, ils la considèrent vierge et refusent d'y écrire.
    ''' </summary>
    Private Shared ReadOnly EMPTY_BRAM_HEADER As Byte() = {&H48, &H55, &H42, &H4D, &H0, &H88, &H10, &H80}

    ''' <summary>Insère une image CD-ROM² : crée le lecteur et le câble à la MMU (RAM CD comprise).</summary>
    Public Sub InsertCd(cdImage As CdImage)
        cd = New CdRom(cdImage)
        mpu.ConnectCd(cd)
    End Sub

    Public Sub New(romPath As String, enableSuperGrafx As Boolean)
        Me.New(CartridgeLoader.LoadCartridge(romPath), enableSuperGrafx)
    End Sub

    ''' <summary>Démarre sur une ROM déjà en mémoire (extraite d'une archive, par exemple).</summary>
    Public Sub New(romName As String, romData() As Byte, enableSuperGrafx As Boolean)
        Me.New(CartridgeLoader.LoadCartridge(romName, romData), enableSuperGrafx)
    End Sub

    Private Sub New(cart As Cartridge, enableSuperGrafx As Boolean)
        cartridge = cart

        mpu = New MemoryMap(cartridge, enableSuperGrafx)
        vce = New Vce()
        vdc = New Vdc(vce)
        psg = New Psg()
        timer = New CpuTimer()
        joypad = New Joypad()

        mpu.ConnectPeripherals(vce, vdc, psg, timer, joypad)

        If enableSuperGrafx Then
            vdc2 = New Vdc(vce)
            vpc = New Vpc(vdc, vdc2, vce)
            mpu.ConnectSuperGrafx(vdc2, vpc)
        End If

        cpu = New Cpu6280(mpu, vdc)
        vdc.ConnectCPU(cpu)
        psg.CycleProvider = Function() cpu.CyclesThisFrame

        For i = 0 To framebuffer.Length - 1
            framebuffer(i) = &HFF000000
        Next
    End Sub

    ''' <summary>Exécute une frame complète (263 scanlines)</summary>
    Public Sub RunFrame()
        cpu.CyclesThisFrame = 0

        For scanline = 0 To PceConstants.SCANLINES_PER_FRAME - 1
            ' CPU pour cette scanline
            Dim target = PceConstants.CYCLES_PER_SCANLINE - cycleDebt
            Dim executed = 0
            While executed < target
                Dim c = cpu.ExecuteInstruction()
                executed += c
                timer.Tick(c)
            End While
            cycleDebt = executed - target

            ' Affichage : le VPC pilote les deux VDC en mode SuperGrafx
            If vpc IsNot Nothing Then
                vpc.DoScanline(scanline, framebuffer)
            Else
                vdc.DoScanline(scanline)
                ComposeLine(scanline)
            End If
        Next

        ' Génération audio de la frame
        psg.AddSamples(psg.GenerateSamples(cpu.CyclesThisFrame))

        _frameCount += 1
    End Sub

    ''' <summary>
    ''' Convertit la ligne émise par le VDC en pixels. Un code négatif signifie que le
    ''' VDC n'émet rien : c'est alors la couleur 0 du VCE qui s'affiche.
    ''' </summary>
    Private Sub ComposeLine(scanline As Integer)
        If scanline >= vdc.DisplayHeight Then Return

        Dim startIdx = scanline * PceConstants.SCREEN_WIDTH
        Dim width = vdc.DisplayWidth
        Dim line = vdc.LineOutput

        For x = 0 To width - 1
            Dim code = line(x)
            framebuffer(startIdx + x) = vce.GetColorArgb(If(code < 0, 0, code))
        Next
    End Sub

    ''' <summary>Nom de la cartouche chargée.</summary>
    Public ReadOnly Property Title As String
        Get
            Return cartridge.Title
        End Get
    End Property

    ''' <summary>Vrai si la console émule un SuperGrafx.</summary>
    Public ReadOnly Property IsSuperGrafx As Boolean
        Get
            Return vpc IsNot Nothing
        End Get
    End Property

    Public Function GetFramebuffer() As Integer()
        Return framebuffer
    End Function

    ''' <summary>
    ''' Largeur affichée. En mode SuperGrafx c'est la plus large des deux zones :
    ''' chaque VDC définit la sienne, et le VPC mélange sur toute l'étendue commune.
    ''' </summary>
    Public ReadOnly Property DisplayWidth As Integer
        Get
            If vdc2 Is Nothing Then Return vdc.DisplayWidth
            Return Math.Max(vdc.DisplayWidth, vdc2.DisplayWidth)
        End Get
    End Property

    ''' <summary>Hauteur affichée, la plus grande des deux VDC en mode SuperGrafx.</summary>
    Public ReadOnly Property DisplayHeight As Integer
        Get
            If vdc2 Is Nothing Then Return vdc.DisplayHeight
            Return Math.Max(vdc.DisplayHeight, vdc2.DisplayHeight)
        End Get
    End Property

    ''' <summary>Retourne les échantillons audio de la frame</summary>
    Public Function GetAudioSamples() As Short()
        Dim buf = psg.GetAudioBuffer()
        If cd IsNot Nothing Then
            Dim cdbuf(buf.Length - 1) As Short
            cd.RenderAudio(cdbuf, buf.Length)
            For i = 0 To buf.Length - 1
                Dim m = CInt(buf(i)) + CInt(cdbuf(i))
                If m > 32767 Then m = 32767
                If m < -32768 Then m = -32768
                buf(i) = CShort(m)
            Next
        End If
        Return buf
    End Function

    ''' <summary>Met à jour l'état des touches</summary>
    Public Sub UpdateInput(keys As System.Collections.Generic.Dictionary(Of String, Boolean))
        joypad.UpdateFromKeys(keys)
    End Sub

    Public ReadOnly Property FrameCount As Integer
        Get
            Return _frameCount
        End Get
    End Property

    Public Function DbgMapper() As String
        Return cartridge.GetMapper()
    End Function

    Public Function DbgPsgState() As String
        Return psg.DbgState()
    End Function

    Public Sub Reset()
        cpu.Reset()
        _frameCount = 0
    End Sub


    ' ===== BRAM persistante =====

    ''' <summary>Vrai si un jeu a écrit en BRAM depuis le dernier chargement.</summary>
    Public ReadOnly Property BramModified As Boolean
        Get
            Return mpu.BramModified
        End Get
    End Property

    ''' <summary>
    ''' Charge la BRAM depuis un fichier. Si le fichier n'existe pas, la mémoire est
    ''' initialisée comme celle d'une console neuve, en-tête de formatage compris.
    ''' </summary>
    Public Sub LoadBram(path As String)
        Dim data(PceConstants.BRAM_SIZE - 1) As Byte

        If Not String.IsNullOrEmpty(path) AndAlso System.IO.File.Exists(path) Then
            Dim raw = System.IO.File.ReadAllBytes(path)
            Array.Copy(raw, data, Math.Min(raw.Length, data.Length))
        Else
            Array.Copy(EMPTY_BRAM_HEADER, data, EMPTY_BRAM_HEADER.Length)
        End If

        mpu.SetBram(data)
    End Sub

    ''' <summary>Écrit la BRAM sur disque (le dossier est créé au besoin).</summary>
    Public Sub SaveBram(path As String)
        If String.IsNullOrEmpty(path) Then Return
        Dim folder = System.IO.Path.GetDirectoryName(path)
        If Not String.IsNullOrEmpty(folder) Then System.IO.Directory.CreateDirectory(folder)
        System.IO.File.WriteAllBytes(path, mpu.GetBram())
    End Sub

    ' ===== Sauvegarde d'état =====

    ''' <summary>Enregistre l'état complet de la console dans un fichier compressé.</summary>
    Public Sub SaveState(path As String)
        Dim folder = System.IO.Path.GetDirectoryName(path)
        If Not String.IsNullOrEmpty(folder) Then System.IO.Directory.CreateDirectory(folder)

        Using fs = New System.IO.FileStream(path, System.IO.FileMode.Create)
            fs.Write(STATE_MAGIC, 0, STATE_MAGIC.Length)

            Using gz = New System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest)
                Using w = New System.IO.BinaryWriter(gz)
                    w.Write(STATE_VERSION)
                    w.Write(cartridge.Signature() Xor If(vpc IsNot Nothing, &H5347, 0))

                    cpu.SaveState(w)
                    mpu.SaveState(w)
                    vdc.SaveState(w)
                    vce.SaveState(w)
                    psg.SaveState(w)
                    timer.SaveState(w)
                    joypad.SaveState(w)
                    cartridge.SaveState(w)

                    If vpc IsNot Nothing Then
                        vdc2.SaveState(w)
                        vpc.SaveState(w)
                    End If

                    w.Write(_frameCount)
                    w.Write(cycleDebt)
                End Using
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' Recharge un état complet. Lève une exception si le fichier n'est pas une
    ''' sauvegarde, s'il vient d'un autre format, ou s'il a été fait avec un autre jeu.
    ''' </summary>
    Public Sub LoadState(path As String)
        Using fs = New System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read)
            Dim magic(STATE_MAGIC.Length - 1) As Byte
            fs.Read(magic, 0, magic.Length)
            For i = 0 To magic.Length - 1
                If magic(i) <> STATE_MAGIC(i) Then
                    Throw New InvalidOperationException("Ce fichier n'est pas une sauvegarde d'état PceEmu.")
                End If
            Next

            Using gz = New System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress)
                Using r = New System.IO.BinaryReader(gz)
                    Dim version = r.ReadInt32()
                    If version <> STATE_VERSION Then
                        Throw New InvalidOperationException("Sauvegarde au format " & version &
                                                            ", incompatible avec le format " & STATE_VERSION & ".")
                    End If

                    If r.ReadInt32() <> (cartridge.Signature() Xor If(vpc IsNot Nothing, &H5347, 0)) Then
                        Throw New InvalidOperationException("Cette sauvegarde a été faite avec une autre ROM, ou dans l'autre mode console.")
                    End If

                    cpu.LoadState(r)
                    mpu.LoadState(r)
                    vdc.LoadState(r)
                    vce.LoadState(r)
                    psg.LoadState(r)
                    timer.LoadState(r)
                    joypad.LoadState(r)
                    cartridge.LoadState(r)

                    If vpc IsNot Nothing Then
                        vdc2.LoadState(r)
                        vpc.LoadState(r)
                    End If

                    _frameCount = r.ReadInt32()
                    cycleDebt = r.ReadInt32()
                End Using
            End Using
        End Using
    End Sub

End Class
