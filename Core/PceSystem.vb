''' <summary>Système PC Engine - orchestration scanline par scanline</summary>
Public Class PceSystem

    Private cpu As Cpu6280
    Private mpu As MemoryMap
    Private vce As Vce
    Private vdc As Vdc
    Private psg As Psg
    Private timer As CpuTimer
    Private joypad As Joypad

    Private cartridge As Cartridge
    Private framebuffer(PceConstants.SCREEN_WIDTH * PceConstants.SCREEN_HEIGHT - 1) As Integer

    Private _frameCount As Integer = 0
    Private cycleDebt As Integer = 0

    Public Sub New(romPath As String, enableSuperGrafx As Boolean)
        cartridge = CartridgeLoader.LoadCartridge(romPath)

        mpu = New MemoryMap(cartridge)
        vce = New Vce()
        vdc = New Vdc(vce)
        psg = New Psg()
        timer = New CpuTimer()
        joypad = New Joypad()

        mpu.ConnectPeripherals(vce, vdc, psg, timer, joypad)

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

            ' VDC : rendu + IRQ (RCR, VBlank, SATB)
            vdc.DoScanline(scanline, framebuffer)
        Next

        ' Génération audio de la frame
        psg.AddSamples(psg.GenerateSamples(cpu.CyclesThisFrame))

        _frameCount += 1
    End Sub

    Public Function GetFramebuffer() As Integer()
        Return framebuffer
    End Function

    ''' <summary>Largeur d'affichage active du VDC</summary>
    Public ReadOnly Property DisplayWidth As Integer
        Get
            Return vdc.DisplayWidth
        End Get
    End Property

    ''' <summary>Hauteur d'affichage active du VDC</summary>
    Public ReadOnly Property DisplayHeight As Integer
        Get
            Return vdc.DisplayHeight
        End Get
    End Property

    ''' <summary>Retourne les échantillons audio de la frame</summary>
    Public Function GetAudioSamples() As Short()
        Return psg.GetAudioBuffer()
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

    Public Function DbgPsgState() As String
        Return psg.DbgState()
    End Function

    Public Sub Reset()
        cpu.Reset()
        _frameCount = 0
    End Sub

End Class
