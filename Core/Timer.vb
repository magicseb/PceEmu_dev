''' <summary>Timer HuC6280 - décompte /1024 cycles, IRQ TIMER</summary>
Public Class CpuTimer

    Private reload As Integer = 0
    Private counter As Integer = 0
    Private enabled As Boolean = False
    Private prescaler As Integer = 0
    Public IrqPending As Boolean = False
    Public Shared DbgIrqCount As Long = 0
    Public Shared DbgEnableWrites As Long = 0

    ''' <summary>$0C00 = compteur/reload, $0C01 = enable</summary>
    Public Function Read(offset As Integer) As Integer
        Select Case offset And 1
            Case 0
                Return counter And &H7F
            Case Else
                Return 0
        End Select
    End Function

    Public Sub Write(offset As Integer, value As Integer)
        Select Case offset And 1
            Case 0
                reload = value And &H7F
            Case 1
                DbgEnableWrites += 1
                Dim newEnabled = (value And 1) <> 0
                If newEnabled And Not enabled Then
                    counter = reload
                    prescaler = 0
                End If
                enabled = newEnabled
        End Select
    End Sub

    ''' <summary>Avance le timer de N cycles CPU</summary>
    Public Sub Tick(cycles As Integer)
        If Not enabled Then Return
        prescaler += cycles
        While prescaler >= 1024
            prescaler -= 1024
            counter -= 1
            If counter < 0 Then
                counter = reload
                IrqPending = True
                DbgIrqCount += 1
            End If
        End While
    End Sub

    ''' <summary>Acquittement IRQ (write $1403)</summary>
    Public Sub AckIrq()
        IrqPending = False
    End Sub


    ''' <summary>Écrit l'état du timer dans une sauvegarde.</summary>
    Public Sub SaveState(w As System.IO.BinaryWriter)
        w.Write(reload) : w.Write(counter) : w.Write(prescaler)
        w.Write(enabled) : w.Write(IrqPending)
    End Sub

    ''' <summary>Restaure l'état du timer depuis une sauvegarde.</summary>
    Public Sub LoadState(r As System.IO.BinaryReader)
        reload = r.ReadInt32() : counter = r.ReadInt32() : prescaler = r.ReadInt32()
        enabled = r.ReadBoolean() : IrqPending = r.ReadBoolean()
    End Sub

End Class
