''' <summary>Fenêtre principale WinForms pour l'émulateur</summary>
Public Class MainForm
    Inherits System.Windows.Forms.Form

    Private WithEvents menuStripMain As System.Windows.Forms.MenuStrip
    Private WithEvents renderPanel As System.Windows.Forms.Panel
    Private statusLabel As System.Windows.Forms.Label
    
    Private pceSystem As PceSystem
    Private renderer As IEmuRenderer
    Private usingD3D As Boolean = False
    Private currentShader As PceShader = PceShader.SharpPixels
    Private currentScale As Integer = 2
    Private gamepadMenu As GamepadMenuForm
    Private menuOpen As Boolean = False
    Private prevPausedBeforeMenu As Boolean = False
    Private lastMenu As GamepadInput.MenuState
    Private ReadOnly menuHeld As New System.Collections.Generic.Dictionary(Of String, Integer)
    Private shaderSharpItem, shaderSmoothItem, shaderScanItem, shaderCrtItem As System.Windows.Forms.ToolStripMenuItem
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
    Private aspect43MenuItem As System.Windows.Forms.ToolStripMenuItem
    ''' <summary>La fenêtre se verrouille en 4:3 au redimensionnement quand vrai.</summary>
    Private lockAspect43 As Boolean = True
    Private fullscreenMenuItem As System.Windows.Forms.ToolStripMenuItem
    Private isFullscreen As Boolean = False
    Private savedBounds As System.Drawing.Rectangle
    Private savedBorder As System.Windows.Forms.FormBorderStyle
    Private savedState As System.Windows.Forms.FormWindowState
    
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

        ' Icône de la fenêtre / barre des tâches (ressource embarquée « PceEmu.ico »)
        Try
            Using st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PceEmu.ico")
                If st IsNot Nothing Then Me.Icon = New System.Drawing.Icon(st)
            End Using
        Catch
            ' pas d'icône : on garde celle par défaut
        End Try
        
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
        viewMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripSeparator())
        aspect43MenuItem = New System.Windows.Forms.ToolStripMenuItem("Aspect &4:3", Nothing, AddressOf MenuToggleAspect43)
        aspect43MenuItem.CheckOnClick = True
        aspect43MenuItem.Checked = True
        viewMenu.DropDownItems.Add(aspect43MenuItem)
        fullscreenMenuItem = New System.Windows.Forms.ToolStripMenuItem("Plein &écran" & vbTab & "F11", Nothing, AddressOf MenuToggleFullscreen)
        viewMenu.DropDownItems.Add(fullscreenMenuItem)
        viewMenu.DropDownItems.Add(New System.Windows.Forms.ToolStripSeparator())
        Dim shaderMenu = New System.Windows.Forms.ToolStripMenuItem("&Filtre d'affichage")
        shaderSharpItem = New System.Windows.Forms.ToolStripMenuItem("Pixels &nets", Nothing, AddressOf MenuShaderSelect)
        shaderSharpItem.Tag = PceShader.SharpPixels : shaderSharpItem.Checked = True
        shaderSmoothItem = New System.Windows.Forms.ToolStripMenuItem("Pixels &lisses", Nothing, AddressOf MenuShaderSelect)
        shaderSmoothItem.Tag = PceShader.SmoothPixels
        shaderScanItem = New System.Windows.Forms.ToolStripMenuItem("&Scanlines", Nothing, AddressOf MenuShaderSelect)
        shaderScanItem.Tag = PceShader.Scanlines
        shaderCrtItem = New System.Windows.Forms.ToolStripMenuItem("&CRT", Nothing, AddressOf MenuShaderSelect)
        shaderCrtItem.Tag = PceShader.Crt
        shaderMenu.DropDownItems.AddRange({shaderSharpItem, shaderSmoothItem, shaderScanItem, shaderCrtItem})
        viewMenu.DropDownItems.Add(shaderMenu)
        
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

        ' Fenêtre en 4:3 par défaut (panneau de rendu 640×480)
        SetPanel43(640)
    End Sub

    ''' <summary>Ouvre une ROM</summary>
    Private Sub MenuOpenROM(sender As Object, e As EventArgs)
        Dim openFileDialog = New System.Windows.Forms.OpenFileDialog()
        openFileDialog.Filter = "Jeux PC Engine (*.pce;*.sgx;*.zip;*.7z;*.cue;*.ccd;*.chd)|*.pce;*.sgx;*.zip;*.7z;*.cue;*.ccd;*.chd|" &
                                "ROMs HuCard (*.pce;*.sgx)|*.pce;*.sgx|" &
                                "Jeux CD-ROM² (*.cue;*.ccd;*.chd)|*.cue;*.ccd;*.chd|" &
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
        Return ext = ".cue" OrElse ext = ".ccd" OrElse ext = ".img" OrElse ext = ".chd"
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
            
            ' Initialiser le rendu : Direct3D 11 (shaders) avec repli GDI+
            If renderer IsNot Nothing Then renderer.Dispose()
            Try
                renderer = New D3DRenderer(renderPanel)
                usingD3D = True
                ShowStatus("Affichage Direct3D 11")
            Catch ex As Exception
                renderer = New Direct3D11Renderer(PceConstants.SCREEN_WIDTH, PceConstants.SCREEN_HEIGHT, renderPanel)
                usingD3D = False
                ShowStatus("Affichage GDI+ (Direct3D indisponible)")
            End Try
            renderer.ForceAspect43 = lockAspect43
            renderer.Shader = currentShader
            
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

            ' Manette : lecture + menu rapide en surimpression (ouvert par LB+RT)
            If config.GamepadEnabled Then
                Dim pad = gamepad.Poll()          ' rafraîchit aussi l'état brut du menu
                Dim mb = gamepad.ReadMenu()
                If mb.Toggle AndAlso Not lastMenu.Toggle Then Me.BeginInvoke(New Action(AddressOf ToggleGamepadMenu))
                If menuOpen Then
                    DispatchMenuNav(mb)
                Else
                    inputManager.ApplyGamepad(pad)
                End If
                lastMenu = mb
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

    ''' <summary>Dimensionne la fenêtre pour un panneau de rendu de largeur donnée en 4:3.</summary>
    Private Sub SetPanel43(width As Integer)
        currentScale = Math.Max(1, Math.Min(3, width \ 320))
        Dim chromeH = menuStripMain.Height + statusLabel.Height
        Me.ClientSize = New System.Drawing.Size(width, CInt(width * 3 / 4) + chromeH)
    End Sub

    Private Sub MenuScale1x(sender As Object, e As EventArgs)
        SetPanel43(320)   ' 320×240 = 4:3
    End Sub

    Private Sub MenuScale2x(sender As Object, e As EventArgs)
        SetPanel43(640)   ' 640×480 = 4:3
    End Sub

    Private Sub MenuScale3x(sender As Object, e As EventArgs)
        SetPanel43(960)   ' 960×720 = 4:3
    End Sub

    Private Sub MenuToggleAspect43(sender As Object, e As EventArgs)
        lockAspect43 = aspect43MenuItem.Checked
        If renderer IsNot Nothing Then renderer.ForceAspect43 = lockAspect43
        If lockAspect43 Then SetPanel43(renderPanel.ClientSize.Width)   ' resnappe la fenêtre en 4:3
        renderPanel.Invalidate()
    End Sub

    Private Sub MenuToggleFullscreen(sender As Object, e As EventArgs)
        ToggleFullscreen()
    End Sub

    Private Sub MenuShaderSelect(sender As Object, e As EventArgs)
        SetShader(CType(CType(sender, System.Windows.Forms.ToolStripMenuItem).Tag, PceShader))
        If Not usingD3D Then ShowStatus("Note : les filtres nécessitent Direct3D (repli GDI+ actif)")
    End Sub

    ''' <summary>Applique un shader et synchronise les coches du menu (appelé par le menu et l'overlay manette).</summary>
    Private Sub SetShader(s As PceShader)
        currentShader = s
        If renderer IsNot Nothing Then renderer.Shader = s
        shaderSharpItem.Checked = (s = PceShader.SharpPixels)
        shaderSmoothItem.Checked = (s = PceShader.SmoothPixels)
        shaderScanItem.Checked = (s = PceShader.Scanlines)
        shaderCrtItem.Checked = (s = PceShader.Crt)
    End Sub

    ' ===== Menu rapide manette (overlay ouvert par LB+RT) =====

    Private Sub ToggleGamepadMenu()
        If menuOpen Then CloseGamepadMenu() Else OpenGamepadMenu()
    End Sub

    Private Sub OpenGamepadMenu()
        If pceSystem Is Nothing Then Return   ' rien à configurer sans jeu chargé
        If gamepadMenu Is Nothing OrElse gamepadMenu.IsDisposed Then gamepadMenu = New GamepadMenuForm(Me)
        prevPausedBeforeMenu = isPaused
        isPaused = True
        menuOpen = True
        menuHeld.Clear()
        gamepadMenu.ResetToRoot()
        PositionGamepadMenu()
        gamepadMenu.Show(Me)
        gamepadMenu.Invalidate()
    End Sub

    Friend Sub CloseGamepadMenu()
        menuOpen = False
        isPaused = prevPausedBeforeMenu
        menuHeld.Clear()
        If gamepadMenu IsNot Nothing AndAlso Not gamepadMenu.IsDisposed Then gamepadMenu.Hide()
        Me.Activate()
    End Sub

    ''' <summary>Cale l'overlay sur la zone de rendu (à l'ouverture et après un changement de taille/plein écran).</summary>
    Friend Sub PositionGamepadMenu()
        If gamepadMenu Is Nothing OrElse gamepadMenu.IsDisposed Then Return
        gamepadMenu.Bounds = renderPanel.RectangleToScreen(renderPanel.ClientRectangle)
    End Sub

    ' --- actions déclenchées par l'overlay (thread UI) ---
    Friend Sub RequestSaveState()
        DoSaveState() : CloseGamepadMenu()
    End Sub
    Friend Sub RequestLoadState()
        DoLoadState() : CloseGamepadMenu()
    End Sub
    Friend Sub RequestReset()
        If pceSystem IsNot Nothing Then
            SyncLock emulationLock : pceSystem.Reset() : End SyncLock
        End If
        CloseGamepadMenu()
    End Sub
    Friend Sub RequestQuit()
        CloseGamepadMenu() : Me.Close()
    End Sub
    Friend Sub MenuLoadRom(path As String)
        CloseGamepadMenu()
        LoadROM(path)
    End Sub
    Friend Function MenuRomList() As System.Collections.Generic.List(Of String)
        Dim list As New System.Collections.Generic.List(Of String)
        Try
            Dim folder = config.GamesFolder
            If System.IO.Directory.Exists(folder) Then
                For Each f In System.IO.Directory.EnumerateFiles(folder, "*.*", System.IO.SearchOption.AllDirectories)
                    If RomArchive.IsSupported(f) Then list.Add(f)
                Next
                list.Sort(StringComparer.CurrentCultureIgnoreCase)
            End If
        Catch
        End Try
        Return list
    End Function

    ' --- téléchargement archive.org, accessible aussi depuis le menu manette ---
    Private archiveDownloadCancel As Boolean

    Friend Function MenuArchiveSources() As System.Collections.Generic.List(Of ArchiveSource)
        Return config.GetArchiveSources()
    End Function

    ''' <summary>Liste en tâche de fond les fichiers d'une source, déjà filtrés des jeux
    ''' déjà présents dans le dossier games. callback est rappelé sur le thread UI.</summary>
    Friend Sub MenuFetchArchiveFiles(item As String,
                                      callback As Action(Of System.Collections.Generic.List(Of String), String))
        Dim folder = config.GamesFolder
        System.Threading.ThreadPool.QueueUserWorkItem(
            Sub()
                Try
                    Dim names = ArchiveOrgClient.FetchFileNames(item)
                    Dim owned = ArchiveOrgClient.OwnedBaseNames(folder)
                    Dim shown As New System.Collections.Generic.List(Of String)
                    For Each n In names
                        If Not owned.Contains(System.IO.Path.GetFileNameWithoutExtension(n)) Then shown.Add(n)
                    Next
                    Me.BeginInvoke(New Action(Sub() callback(shown, Nothing)))
                Catch ex As Exception
                    Me.BeginInvoke(New Action(Sub() callback(Nothing, ex.Message)))
                End Try
            End Sub)
    End Sub

    ''' <summary>Télécharge un fichier d'une source vers le dossier games en tâche de fond.
    ''' progress/done sont rappelés sur le thread UI.</summary>
    Friend Sub MenuDownloadArchiveFile(item As String, name As String,
                                        progress As Action(Of String),
                                        done As Action(Of String, String))
        If String.IsNullOrEmpty(config.GamesFolder) Then
            done(Nothing, "Aucun dossier de jeux configuré.")
            Return
        End If
        Try
            System.IO.Directory.CreateDirectory(config.GamesFolder)
        Catch
        End Try
        archiveDownloadCancel = False
        Dim localName = ArchiveOrgClient.SafeLocalName(name)
        Dim destPath = System.IO.Path.Combine(config.GamesFolder, localName)
        System.Threading.ThreadPool.QueueUserWorkItem(
            Sub()
                Try
                    ArchiveOrgClient.DownloadItemFile(item, name, destPath,
                        Sub(msg) Me.BeginInvoke(New Action(Sub() progress(msg))),
                        Function() archiveDownloadCancel)
                    Me.BeginInvoke(New Action(Sub() done(destPath, Nothing)))
                Catch ex As OperationCanceledException
                    Me.BeginInvoke(New Action(Sub() done(Nothing, "Annulé.")))
                Catch ex As Exception
                    Me.BeginInvoke(New Action(Sub() done(Nothing, ex.Message)))
                End Try
            End Sub)
    End Sub

    Friend Sub MenuCancelArchiveDownload()
        archiveDownloadCancel = True
    End Sub

    ' --- réglages lus/écrits par l'overlay ---
    Friend ReadOnly Property MenuShaderLabel As String
        Get
            Select Case currentShader
                Case PceShader.SmoothPixels : Return "Pixels lisses"
                Case PceShader.Scanlines : Return "Scanlines"
                Case PceShader.Crt : Return "CRT"
                Case Else : Return "Pixels nets"
            End Select
        End Get
    End Property
    Friend Sub MenuCycleShader(dir As Integer)
        SetShader(CType((CInt(currentShader) + dir + 4) Mod 4, PceShader))
    End Sub
    Friend ReadOnly Property MenuAspectOn As Boolean
        Get
            Return lockAspect43
        End Get
    End Property
    Friend Sub MenuToggleAspect()
        aspect43MenuItem.Checked = Not aspect43MenuItem.Checked
        MenuToggleAspect43(Nothing, Nothing)
        PositionGamepadMenu()
    End Sub
    Friend ReadOnly Property MenuFullscreenOn As Boolean
        Get
            Return isFullscreen
        End Get
    End Property
    Friend Sub MenuToggleFullscreenFromPad()
        ToggleFullscreen()
        PositionGamepadMenu()
    End Sub
    Friend ReadOnly Property MenuScaleValue As Integer
        Get
            Return currentScale
        End Get
    End Property
    Friend Sub MenuCycleScale(dir As Integer)
        If isFullscreen Then Return
        SetPanel43(Math.Max(1, Math.Min(3, currentScale + dir)) * 320)
        PositionGamepadMenu()
    End Sub

    ' --- routage de la navigation (thread d'émulation → thread UI) ---
    Private Sub DispatchMenuNav(mb As GamepadInput.MenuState)
        If gamepadMenu Is Nothing OrElse gamepadMenu.IsDisposed Then Return
        If MenuRepeat("up", mb.Up) Then Me.BeginInvoke(New Action(Sub() gamepadMenu.NavUp()))
        If MenuRepeat("down", mb.Down) Then Me.BeginInvoke(New Action(Sub() gamepadMenu.NavDown()))
        If mb.Left AndAlso Not lastMenu.Left Then Me.BeginInvoke(New Action(Sub() gamepadMenu.NavLeft()))
        If mb.Right AndAlso Not lastMenu.Right Then Me.BeginInvoke(New Action(Sub() gamepadMenu.NavRight()))
        If mb.Accept AndAlso Not lastMenu.Accept Then Me.BeginInvoke(New Action(Sub() gamepadMenu.Accept()))
        If mb.Back AndAlso Not lastMenu.Back Then Me.BeginInvoke(New Action(Sub() gamepadMenu.Back()))
        If mb.Check AndAlso Not lastMenu.Check Then Me.BeginInvoke(New Action(Sub() gamepadMenu.ToggleCheck()))
        If mb.Batch AndAlso Not lastMenu.Batch Then Me.BeginInvoke(New Action(Sub() gamepadMenu.StartBatch()))
    End Sub

    ''' <summary>Front à la première pression, puis répétition automatique (maintien).</summary>
    Private Function MenuRepeat(key As String, pressed As Boolean) As Boolean
        Dim held = If(menuHeld.ContainsKey(key), menuHeld(key), 0)
        Dim fire = False
        If pressed Then
            If held = 0 Then
                fire = True
            ElseIf held >= 18 AndAlso ((held - 18) Mod 4) = 0 Then
                fire = True
            End If
            held += 1
        Else
            held = 0
        End If
        menuHeld(key) = held
        Return fire
    End Function

    ''' <summary>Bascule plein écran : cache menu/barre d'état et bordure, couvre l'écran.
    ''' L'image reste en 4:3 (letterbox) via le rendu. F11 bascule, Échap sort.</summary>
    Private Sub ToggleFullscreen()
        If Not isFullscreen Then
            savedBounds = Me.Bounds
            savedBorder = Me.FormBorderStyle
            savedState = Me.WindowState
            menuStripMain.Visible = False
            statusLabel.Visible = False
            Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
            Me.WindowState = System.Windows.Forms.FormWindowState.Normal
            Me.Bounds = System.Windows.Forms.Screen.FromControl(Me).Bounds
            isFullscreen = True
        Else
            menuStripMain.Visible = True
            statusLabel.Visible = True
            Me.FormBorderStyle = savedBorder
            Me.Bounds = savedBounds
            Me.WindowState = savedState
            isFullscreen = False
        End If
        If fullscreenMenuItem IsNot Nothing Then fullscreenMenuItem.Checked = isFullscreen
        renderPanel.Invalidate()
    End Sub

    ' ---- Verrouillage de l'aspect 4:3 de la fenêtre au redimensionnement ----
    Private Const WM_SIZING As Integer = &H214
    Private Const WMSZ_LEFT As Integer = 1, WMSZ_RIGHT As Integer = 2
    Private Const WMSZ_TOP As Integer = 3, WMSZ_TOPLEFT As Integer = 4
    Private Const WMSZ_TOPRIGHT As Integer = 5, WMSZ_BOTTOM As Integer = 6
    Private Const WMSZ_BOTTOMLEFT As Integer = 7, WMSZ_BOTTOMRIGHT As Integer = 8

    <System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)>
    Private Structure NativeRect
        Public Left As Integer, Top As Integer, Right As Integer, Bottom As Integer
    End Structure

    ''' <summary>Force la fenêtre à garder un panneau de rendu en 4:3 pendant le drag.</summary>
    Protected Overrides Sub WndProc(ByRef m As System.Windows.Forms.Message)
        If m.Msg = WM_SIZING AndAlso lockAspect43 Then
            Dim r = CType(System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, GetType(NativeRect)), NativeRect)
            Dim edge = m.WParam.ToInt32()
            ' chrome hors panneau : bordures/titre + barre de menu + barre d'état
            Dim ncW = Me.Width - Me.ClientSize.Width
            Dim ncH = Me.Height - Me.ClientSize.Height
            Dim chromeH = ncH + menuStripMain.Height + statusLabel.Height
            Select Case edge
                Case WMSZ_LEFT, WMSZ_RIGHT
                    Dim panelW = (r.Right - r.Left) - ncW
                    r.Bottom = r.Top + CInt(panelW * 3 / 4) + chromeH
                Case WMSZ_TOP, WMSZ_BOTTOM
                    Dim panelH = (r.Bottom - r.Top) - chromeH
                    r.Right = r.Left + CInt(panelH * 4 / 3) + ncW
                Case Else   ' coins : la largeur pilote la hauteur
                    Dim panelW = (r.Right - r.Left) - ncW
                    Dim newH = CInt(panelW * 3 / 4) + chromeH
                    If edge = WMSZ_TOPLEFT OrElse edge = WMSZ_TOPRIGHT Then
                        r.Top = r.Bottom - newH
                    Else
                        r.Bottom = r.Top + newH
                    End If
            End Select
            System.Runtime.InteropServices.Marshal.StructureToPtr(r, m.LParam, False)
            m.Result = New IntPtr(1)
            Return
        End If
        MyBase.WndProc(m)
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
        If e.KeyCode = System.Windows.Forms.Keys.Escape AndAlso menuOpen Then
            CloseGamepadMenu() : e.Handled = True : Return
        End If
        If e.KeyCode = System.Windows.Forms.Keys.F11 Then
            ToggleFullscreen() : e.Handled = True : Return
        ElseIf e.KeyCode = System.Windows.Forms.Keys.Escape AndAlso isFullscreen Then
            ToggleFullscreen() : e.Handled = True : Return
        End If
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
        If gamepadMenu IsNot Nothing AndAlso Not gamepadMenu.IsDisposed Then gamepadMenu.Dispose()
        If renderer IsNot Nothing Then renderer.Dispose()
        If audioOut IsNot Nothing Then audioOut.Dispose()
        MyBase.OnFormClosing(e)
    End Sub

End Class
