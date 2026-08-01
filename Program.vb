''' <summary>Point d'entrée de l'application</summary>
Public Module Program

    <System.STAThread()>
    Public Sub Main()
        Dim args = Environment.GetCommandLineArgs()
        
        ' Mode test console
        If args.Length > 1 AndAlso args(1) = "--test-console" Then
            If args.Length < 3 Then
                Console.WriteLine("Usage: dotnet run -- --test-console <rompath>")
                Return
            End If
            TestConsole(args(2))
            Return
        End If
        
        ' Mode WinForms normal
        System.Windows.Forms.Application.EnableVisualStyles()
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(False)
        System.Windows.Forms.Application.Run(New MainForm())
    End Sub
    
    ''' <summary>Mode test console minimal</summary>
    Private Sub TestConsole(romPath As String)
        Console.WriteLine("=== PceEmu Console Test ===")
        Console.WriteLine("ROM: " & romPath)
        
        Try
            Dim pceSystem = New PceSystem(romPath, False)
            Console.WriteLine("✓ ROM chargée")
            Console.WriteLine("✓ FrameCount initial: " & pceSystem.FrameCount)
            
            ' Exécuter 10 frames
            Dim frameTime = System.Diagnostics.Stopwatch.StartNew()
            For frame = 0 To 9
                pceSystem.RunFrame()
                Dim fb = pceSystem.GetFramebuffer()
                Dim pixelCount = 0
                If fb IsNot Nothing Then
                    ' Compter pixels différents du noir
                    For i = 0 To Math.Min(fb.Length - 1, 1000)
                        If fb(i) <> &HFF000000 Then pixelCount += 1
                    Next
                End If
                Console.WriteLine("  Frame " & frame & ": FC=" & pceSystem.FrameCount & " pixels=" & pixelCount)
            Next
            frameTime.Stop()
            
            Console.WriteLine("✓ Test OK en " & frameTime.ElapsedMilliseconds & "ms")
            
        Catch ex As Exception
            Console.WriteLine("✗ ERROR: " & ex.Message)
            Console.WriteLine(ex.StackTrace)
        End Try
    End Sub

End Module
