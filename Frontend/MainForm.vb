''' <summary>Fenêtre principale WinForms pour l'émulateur</summary>
Public Class MainForm
    Inherits System.Windows.Forms.Form

    Private WithEvents menuStripMain As System.Windows.Forms.MenuStrip
    Private WithEvents renderPanel As System.Windows.Forms.Panel
    Private statusLabel As System.Windows.Forms.Label
    
    Private pceSystem As PceSystem
    Private renderer As Direct3D11Renderer
    Private audioOut As AudioOut
    Private ReadOnly config As Settings = Settings.Load()
    Private inputManager As InputManager
    Private ReadOnly gamepad As New GamepadInput()
    
    Private emulationTask As System.Threading.Tasks.Task
    Private shouldStopEmulation As Boolean = False
    Private romLoaded As Boolean = False
    Private isPaused As Boolean = False

    ' Protège le cœur d'émulation : les sauvegardes viennent du fil de l'interface,
    ' l'exécution des frames du fil de fond
    Private ReadOnly emulationLock As New Object()

    Private currentRomPath As String = Nothing
    Private currentSlot As Integer = 1
    Private superGrafxMode As Boolean = False
    Private superGrafxMenuItem As System.Windows.Forms.ToolStripMenuItem
    Private gamepadMenuItem As System.Windows.Forms.ToolStripMenuItem
    
    Public Sub New()
        MyBase.New()
        ' InitializeComponent() supprimé car pas généré par Designer
        SetupUI()
    End Sub

    ''' <summary>Initialise les composants UI</summary>
    Private Sub SetupUI()
        Me.Text = "PceEmu - PC Engine Emulator"
        Me.Size = New System.Drawing.Size(1024, 600)
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        
        ' Menu
        menuStripMain = New System.Windows.Forms.MenuStrip()
        
        ' File menu
        Dim fileMenu = New System.Windows.Forms.ToolStripMenuItem("&File")
        fileMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem("&Bibliothèque de jeux…", Nothing, AddressOf MenuLibrary))
        fileMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem("&Ouvrir une ROM…", Nothing, AddressOf MenuOpenROM))
        fileMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem("&Télécharger des jeux…", Nothing, AddressOf MenuDownload))
        fileMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripSeparator())
        fileMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem("E&xit", Nothing, AddressOf MenuExit))
        
        ' Emulation menu
        Dim emuMenu = New System.Windows.Forms.ToolStripMenuItem("&Emulation")
        emuMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem("&Pause", Nothing, AddressOf MenuPause))
        emuMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem("&Reset", Nothing, AddressOf MenuReset))
        emuMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripSeparator())
        emuMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem("&Sauvegarder l'état" & vbTab & "F5", Nothing, AddressOf MenuSaveState))
        emuMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem("&Charger l'état" & vbTab & "F8", Nothing, AddressOf MenuLoadState))

        Dim slotMenu = New System.Windows.Forms.ToolStripMenuItem("&Emplacement")
        For slot = 1 To 5
            Dim item = New System.Windows.Forms.ToolStripMenuItem("Emplacement " & slot, Nothing, AddressOf MenuSelectSlot)
            item.Tag = slot
            item.Checked = (slot = currentSlot)
            slotMenu.DropDownItems.Add(item)
        Next
        emuMenu.DropDownItems.Add(slotMenu)
        emuMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripSeparator())
        superGrafxMenuItem = New System.Windows.Forms.ToolStripMenuItem("Mode Super&Grafx", Nothing, AddressOf MenuToggleSuperGrafx)
        superGrafxMenuItem.CheckOnClick = True
        emuMenu.DropDownItems.Add(superGrafxMenuItem)
        
        ' View menu
        Dim viewMenu = New System.Windows.Forms.ToolStripMenuItem("&View")
        viewMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem("Scale &1x", Nothing, AddressOf MenuScale1x))
        viewMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem("Scale &2x", Nothing, AddressOf MenuScale2x))
        viewMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem("Scale &3x", Nothing, AddressOf MenuScale3x))
        
        ' Menu Options
        Dim optionsMenu = New System.Windows.Forms.ToolStripMenuItem("&Options")
        optionsMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem(
            "Configurer les &touches…", Nothing, AddressOf MenuConfigureKeys))
        optionsMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripMenuItem(
            "&Dossier des jeux…", Nothing, AddressOf MenuChooseGamesFolder))
        gamepadMenuItem = New System.Windows.Forms.ToolStripMenuItem(
            "Manette &Xbox", Nothing, AddressOf MenuToggleGamepad)
        gamepadMenuItem.CheckOnClick = True
        gamepadMenuItem.Checked = config.GamepadEnabled
        optionsMenu.DropDownItems.Add(gamepadMenuItem)

        menuStripMain.Items.AddRange({fileMenu, emuMenu, viewMenu, optionsMenu})
        Me.Controls.Add(menuStripMain)
        
        ' Render panel
        renderPanel = New System.Windows.Forms.Panel()
        renderPanel.Dock = System.Windows.Forms.DockStyle.Fill
        renderPanel.BackColor = System.Drawing.Color.Black
        renderPanel.Top = menuStripMain.Height
        Me.Controls.Add(renderPanel)
        
        ' Status label
        statusLabel = New System.Windows.Forms.Label()
        statusLabel.AutoSize = True
        statusLabel.ForeColor = System.Drawing.Color.White
        statusLabel.Text = "Prêt. Ouvrir une ROM pour commencer."
        statusLabel.Dock = System.Windows.Forms.DockStyle.Bottom
        statusLabel.BackColor = System.Drawing.Color.DarkGray
        statusLabel.Padding = New System.Windows.Forms.Padding(5)
        Me.Controls.Add(statusLabel)
        
        ' Entrées : les touches par défaut suivent la disposition clavier détectée
        inputManager = New InputManager(config)
        
        ' Événements clavier
        Me.KeyPreview = True
    End Sub

    ''' <summary>Ouvre une ROM</summary>
    Private Sub MenuOpenROM(sender As Object, e As EventArgs)
        Dim openFileDialog = New System.Windows.Forms.OpenFileDialog()
        openFileDialog.Filter = "Jeux PC Engine (*.pce;*.sgx;*.zip;*.7z;*.cue;*.ccd)|*.pce;*.sgx;*.zip;*.7z;*.cue;*.ccd|" &
                                "ROMs HuCard (*.pce;*.sgx)|*.pce;*.sgx|" &
                                "Jeux CD-ROM² (*.cue;*.ccd)|*.cue;*.ccd|" &
                                "Archives (*.zip;*.7z)|*.zip;*.7z|" &
                                "Tous les fichiers (*.*)|*.*"
        openFileDialog.Title = "Ouvrir une ROM PC Engine"
        
        If openFileDialog.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
            LoadROM(openFileDialog.FileName)
        End If
    End Sub

    ''' <summary>Vrai si le chemin désigne une image CD-ROM² (.cue/.ccd/.img).</summary>
    Private Shared Function IsCdImage(path As String) As Boolean
        Dim ext = System.IO.Path.GetExtension(path).ToLowerInvariant()
        Return ext = ".cue" OrElse ext = ".ccd" OrElse ext = ".img"
    End Function

    ''' <summary>
    ''' Retourne le chemin de la System Card (BIOS CD-ROM²) : celui mémorisé s'il est
    ''' valide, sinon demande à l'utilisateur de le localiser et le mémorise.
    ''' Retourne Nothing si l'utilisateur annule.
    ''' </summary>
    Private Function ResolveSystemCard() As String
        Dim saved = config.SystemCardPath
        If Not String.IsNullOrEmpty(saved) AndAlso System.IO.File.Exists(saved) Then Return saved

        System.Windows.Forms.MessageBox.Show(
            "Les jeux CD-ROM² nécessitent le fichier de la System Card (BIOS), par ex. syscard3.pce." & Environment.NewLine &
            "Sélectionnez-le : il sera mémorisé pour les prochains lancements.",
            "System Card requise")
        Dim dlg = New System.Windows.Forms.OpenFileDialog()
        dlg.Filter = "System Card (*.pce)|*.pce|Tous les fichiers (*.*)|*.*"
        dlg.Title = "Localiser la System Card (BIOS CD-ROM²)"
        If dlg.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
            config.SystemCardPath = dlg.FileName
            config.Save()
            Return dlg.FileName
        End If
        Return Nothing
    End Function

    ''' <summary>Charge et démarre une ROM</summary>
    Private Sub LoadROM(romPath As String, Optional forceMode As Boolean = False)
        Try
            ' Arrêter l'émulation courante
            StopEmulationTask()
            
            ' Charger le système
            ' La BRAM du jeu précédent est enregistrée avant tout changement
            FlushBram()

            ' Sans consigne explicite, on se fie au nom du fichier
            If Not forceMode Then
                superGrafxMode = LooksLikeSuperGrafx(romPath)
                superGrafxMenuItem.Checked = superGrafxMode
            End If


            ' L'archive éventuelle est décompressée en mémoire, rien n'atterrit sur le disque
            If IsCdImage(romPath) Then
                ' Jeu CD-ROM² : il faut la System Card (BIOS) + l'image CD
                Dim scPath = ResolveSystemCard()
                If scPath Is Nothing Then
                    statusLabel.Text = "Chargement CD annulé (System Card requise)."
                    Return
                End If
                superGrafxMode = False
                superGrafxMenuItem.Checked = False
                Dim sc = RomArchive.Load(scPath)
                pceSystem = New PceSystem(sc.Title, sc.Data, False)
                pceSystem.InsertCd(New CdImage(romPath))
            Else
                Dim rom = RomArchive.Load(romPath)
                pceSystem = New PceSystem(rom.Title, rom.Data, superGrafxMode)
            End If
            currentRomPath = romPath
            pceSystem.LoadBram(BramPath())
            
            ' Initialiser le rendu Direct3D 11
            If renderer IsNot Nothing Then renderer.Dispose()
            renderer = New Direct3D11Renderer(PceConstants.SCREEN_WIDTH, PceConstants.SCREEN_HEIGHT, renderPanel)
            
            ' Initialiser l'audio
            If audioOut IsNot Nothing Then audioOut.Dispose()
            audioOut = New AudioOut(CInt(PceConstants.AUDIO_SAMPLE_RATE), 2)
            
            romLoaded = True
            isPaused = False
            shouldStopEmulation = False
            
            ' Démarrer la boucle d'émulation
            emulationTask = System.Threading.Tasks.Task.Run(AddressOf EmulationLoop)
            
            statusLabel.Text = "ROM chargée en mode " & ModeName() & "."
            Me.Text = "PceEmu - " & System.IO.Path.GetFileName(romPath)
        Catch ex As Exception
            System.Windows.Forms.MessageBox.Show("Erreur chargement ROM : " & ex.Message)
            statusLabel.Text = "Erreur chargement ROM"
        End Try
    End Sub

    ''' <summary>Boucle principale d'émulation (limiteur de framerate précis)</summary>
    Private Sub EmulationLoop()
        Dim ticksPerFrame = CLng(System.Diagnostics.Stopwatch.Frequency / PceConstants.FRAME_RATE)
        Dim clock = System.Diagnostics.Stopwatch.StartNew()
        Dim nextFrameTick As Long = clock.ElapsedTicks
        Dim fps = 0
        Dim fpsTimer = System.Diagnostics.Stopwatch.StartNew()

        While Not shouldStopEmulation
            If Not isPaused And pceSystem IsNot Nothing Then
                ' Entrées
                pceSystem.UpdateInput(inputManager.GetPadState())

                ' Une frame d'émulation
                SyncLock emulationLock
                    pceSystem.RunFrame()
                End SyncLock

                ' Audio
                Dim audioSamples = pceSystem.GetAudioSamples()
                If audioOut IsNot Nothing AndAlso audioSamples IsNot Nothing AndAlso audioSamples.Length > 0 Then
                    audioOut.SendAudio(audioSamples)
                End If

                ' Rendu (thread-safe)
                If renderer IsNot Nothing Then
                    renderer.UpdateFrame(pceSystem.GetFramebuffer(), pceSystem.DisplayWidth, pceSystem.DisplayHeight)
                End If

                fps += 1
            End If

            ' Manette : lue à chaque frame, fusionnée avec le clavier
            If config.GamepadEnabled Then
                inputManager.ApplyGamepad(gamepad.Poll())
            End If

            ' Commandes de l'émulateur
            If inputManager.IsActionPressed(InputManager.ACTION_PAUSE) Then isPaused = Not isPaused
            If inputManager.IsActionPressed(InputManager.ACTION_RESET) AndAlso pceSystem IsNot Nothing Then pceSystem.Reset()
            If inputManager.IsActionPressed(InputManager.ACTION_SAVE_STATE) Then DoSaveState()
            If inputManager.IsActionPressed(InputManager.ACTION_LOAD_STATE) Then DoLoadState()

            ' Limiteur : accumulateur en ticks (pas de dérive), Sleep grossier + spin fin
            nextFrameTick += ticksPerFrame
            Dim remaining = nextFrameTick - clock.ElapsedTicks
            If remaining > 0 Then
                Dim remainingMs = remaining * 1000 \ System.Diagnostics.Stopwatch.Frequency
                If remainingMs > 2 Then
                    System.Threading.Thread.Sleep(CInt(remainingMs - 1))
                End If
                While clock.ElapsedTicks < nextFrameTick
                    System.Threading.Thread.SpinWait(50)
                End While
            Else
                ' En retard : resynchroniser pour éviter l'effet catch-up
                nextFrameTick = clock.ElapsedTicks
            End If

            ' Affichage FPS (1×/s)
            If fpsTimer.ElapsedMilliseconds >= 1000 Then
                Dim fpsNow = fps
                Try
                    Me.BeginInvoke(Sub() statusLabel.Text = fpsNow & " FPS")
                Catch
                End Try
                fps = 0
                fpsTimer.Restart()
            End If
        End While
    End Sub

    ''' <summary>Arrête la boucle d'émulation</summary>
    Private Sub StopEmulationTask()
        shouldStopEmulation = True
        If emulationTask IsNot Nothing Then
            emulationTask.Wait(5000)  ' Attendre max 5s
        End If
        shouldStopEmulation = False
    End Sub

    ' Menus
    Private Sub MenuPause(sender As Object, e As EventArgs)
        If romLoaded Then
            isPaused = Not isPaused
            statusLabel.Text = If(isPaused, "Pausé", "Reprise")
        End If
    End Sub

    Private Sub MenuReset(sender As Object, e As EventArgs)
        If romLoaded And pceSystem IsNot Nothing Then
            pceSystem.Reset()
            statusLabel.Text = "Réinitialisé"
        End If
    End Sub

    ''' <summary>
    ''' Bascule entre PC Engine et SuperGrafx. Les cinq jeux SuperGrafx sont reconnus
    ''' par leur nom de fichier au chargement ; cette case permet de forcer le mode
    ''' pour une ROM que la reconnaissance aurait manquée.
    ''' </summary>
    Private Sub MenuToggleSuperGrafx(sender As Object, e As EventArgs)
        superGrafxMode = superGrafxMenuItem.Checked

        If String.IsNullOrEmpty(currentRomPath) Then
            ShowStatus("Mode " & ModeName() & " : actif au prochain chargement")
            Return
        End If

        ' Le mode change le câblage de la console : il faut la reconstruire
        LoadRom(currentRomPath, forceMode:=True)
    End Sub

    Private Function ModeName() As String
        Return If(superGrafxMode, "SuperGrafx", "PC Engine")
    End Function

    ''' <summary>
    ''' Reconnaît les cinq HuCards SuperGrafx d'après leur nom de fichier. C'est le
    ''' seul moyen simple : rien dans l'en-tête d'une ROM n'indique le mode, et les
    ''' jeux concernés se comptent sur les doigts d'une main.
    ''' </summary>
    Private Shared Function LooksLikeSuperGrafx(romPath As String) As Boolean
        Dim name = System.IO.Path.GetFileNameWithoutExtension(romPath).ToLowerInvariant()
        Dim titles = {"daimakaimura", "ghouls", "ghosts", "aldynes",
                      "battle ace", "battle_ace", "1941", "granzort", "grandzort"}
        For Each title In titles
            If name.Contains(title) Then Return True
        Next
        Return False
    End Function

    Private Sub MenuScale1x(sender As Object, e As EventArgs)
        Me.ClientSize = New System.Drawing.Size(256, 224 + menuStripMain.Height)
    End Sub

    Private Sub MenuScale2x(sender As Object, e As EventArgs)
        Me.ClientSize = New System.Drawing.Size(512, 448 + menuStripMain.Height)
    End Sub

    Private Sub MenuScale3x(sender As Object, e As EventArgs)
        Me.ClientSize = New System.Drawing.Size(768, 672 + menuStripMain.Height)
    End Sub

    Private Sub MenuExit(sender As Object, e As EventArgs)
        Me.Close()
    End Sub

    ' ===== Sauvegardes =====

    ''' <summary>Dossier des sauvegardes, à côté de l'exécutable.</summary>
    Private Function SaveFolder() As String
        Return System.IO.Path.Combine(AppContext.BaseDirectory, "Sauvegardes")
    End Function

    ''' <summary>
    ''' Fichier de la BRAM. Elle est unique, comme la pile de la console :
    ''' tous les jeux se partagent les mêmes 2 Ko.
    ''' </summary>
    Private Function BramPath() As String
        Return System.IO.Path.Combine(SaveFolder(), "bram.sav")
    End Function

    ''' <summary>Fichier d'un emplacement de sauvegarde, propre à la ROM chargée.</summary>
    Private Function StatePath(slot As Integer) As String
        Dim name = System.IO.Path.GetFileNameWithoutExtension(currentRomPath)
        Return System.IO.Path.Combine(SaveFolder(), name & ".st" & slot)
    End Function

    ''' <summary>Écrit la BRAM sur disque si un jeu y a touché.</summary>
    Private Sub FlushBram()
        If pceSystem Is Nothing OrElse Not pceSystem.BramModified Then Return
        Try
            pceSystem.SaveBram(BramPath())
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("BRAM non enregistrée : " & ex.Message)
        End Try
    End Sub

    Private Sub DoSaveState()
        If pceSystem Is Nothing Then Return
        Try
            SyncLock emulationLock
                pceSystem.SaveState(StatePath(currentSlot))
            End SyncLock
            ShowStatus("État sauvegardé dans l'emplacement " & currentSlot)
        Catch ex As Exception
            ShowStatus("Échec de la sauvegarde : " & ex.Message)
        End Try
    End Sub

    Private Sub DoLoadState()
        If pceSystem Is Nothing Then Return
        Dim path = StatePath(currentSlot)
        If Not System.IO.File.Exists(path) Then
            ShowStatus("Emplacement " & currentSlot & " vide")
            Return
        End If
        Try
            SyncLock emulationLock
                pceSystem.LoadState(path)
            End SyncLock
            ShowStatus("État rechargé depuis l'emplacement " & currentSlot)
        Catch ex As Exception
            ShowStatus("Échec du chargement : " & ex.Message)
        End Try
    End Sub

    ''' <summary>Affiche un message dans la barre d'état, depuis n'importe quel fil.</summary>
    Private Sub ShowStatus(message As String)
        Try
            If statusLabel.InvokeRequired Then
                statusLabel.BeginInvoke(Sub() statusLabel.Text = message)
            Else
                statusLabel.Text = message
            End If
        Catch
        End Try
    End Sub

    Private Sub MenuSaveState(sender As Object, e As EventArgs)
        DoSaveState()
    End Sub

    Private Sub MenuLoadState(sender As Object, e As EventArgs)
        DoLoadState()
    End Sub

    Private Sub MenuSelectSlot(sender As Object, e As EventArgs)
        Dim item = CType(sender, System.Windows.Forms.ToolStripMenuItem)
        currentSlot = CInt(item.Tag)
        For Each sibling As System.Windows.Forms.ToolStripItem In item.Owner.Items
            If TypeOf sibling Is System.Windows.Forms.ToolStripMenuItem Then
                CType(sibling, System.Windows.Forms.ToolStripMenuItem).Checked = (sibling Is item)
            End If
        Next
        ShowStatus("Emplacement " & currentSlot & " sélectionné")
    End Sub

    ''' <summary>Ouvre la bibliothèque et lance le jeu retenu.</summary>
    Private Sub MenuLibrary(sender As Object, e As EventArgs)
        Using dialog = New RomLibraryForm(config)
            If dialog.ShowDialog(Me) = System.Windows.Forms.DialogResult.OK AndAlso
               Not String.IsNullOrEmpty(dialog.SelectedRom) Then
                LoadROM(dialog.SelectedRom)
            End If
        End Using
    End Sub

    Private Sub MenuDownload(sender As Object, e As EventArgs)
        Using dialog = New ArchiveOrgForm(config)
            If dialog.ShowDialog(Me) = System.Windows.Forms.DialogResult.OK AndAlso
               Not String.IsNullOrEmpty(dialog.LastDownloaded) Then
                LoadROM(dialog.LastDownloaded)
            End If
        End Using
    End Sub

    Private Sub MenuConfigureKeys(sender As Object, e As EventArgs)
        Using dialog = New KeyConfigForm(inputManager, config)
            If dialog.ShowDialog(Me) = System.Windows.Forms.DialogResult.OK Then
                inputManager.ApplyBindings(config)
                ShowStatus("Touches enregistrées")
            End If
        End Using
    End Sub

    Private Sub MenuChooseGamesFolder(sender As Object, e As EventArgs)
        Using dialog = New System.Windows.Forms.FolderBrowserDialog()
            dialog.Description = "Choisir le dossier des jeux"
            dialog.SelectedPath = config.GamesFolder

            If dialog.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
                config.GamesFolder = dialog.SelectedPath
                config.Save()
                ShowStatus("Dossier des jeux : " & dialog.SelectedPath)
            End If
        End Using
    End Sub

    Private Sub MenuToggleGamepad(sender As Object, e As EventArgs)
        config.GamepadEnabled = gamepadMenuItem.Checked
        config.Save()

        If config.GamepadEnabled Then
            gamepad.Rescan()
            ShowStatus("Manette activée")
        Else
            inputManager.ApplyGamepad(Nothing)
            ShowStatus("Manette désactivée")
        End If
    End Sub

    ''' <summary>Gestion des touches</summary>
    Protected Overrides Sub OnKeyDown(e As System.Windows.Forms.KeyEventArgs)
        inputManager.HandleKeyDown(e)
        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Sub OnKeyUp(e As System.Windows.Forms.KeyEventArgs)
        inputManager.HandleKeyUp(e)
        MyBase.OnKeyUp(e)
    End Sub

    ''' <summary>Fermeture de l'app</summary>
    Protected Overrides Sub OnFormClosing(e As System.Windows.Forms.FormClosingEventArgs)
        StopEmulationTask()
        FlushBram()
        If renderer IsNot Nothing Then renderer.Dispose()
        If audioOut IsNot Nothing Then audioOut.Dispose()
        MyBase.OnFormClosing(e)
    End Sub

End Class
