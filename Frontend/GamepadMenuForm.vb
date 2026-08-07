Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Menu de configuration en surimpression, entièrement pilotable à la manette.
''' Ouvert/fermé par LB+RT pendant le jeu (géré par MainForm). Navigation : croix
''' haut/bas pour choisir, gauche/droite pour modifier une valeur, A pour valider,
''' B pour revenir/fermer. Deux pages : le menu principal et la liste des jeux.
''' Le formulaire ne prend pas le focus (WS_EX_NOACTIVATE) et se dessine en GDI+.
''' </summary>
Public Class GamepadMenuForm
    Inherits Form

    Private ReadOnly host As MainForm
    ' page : 0 = menu principal, 1 = liste des jeux, 2 = sources archive.org, 3 = fichiers d'une source
    Private page As Integer = 0
    Private sel As Integer = 0           ' index sélectionné (menu principal)
    Private romSel As Integer = 0        ' index sélectionné (liste des jeux)
    Private roms As System.Collections.Generic.List(Of String)
    Private ReadOnly items As System.Collections.Generic.List(Of Item)

    ' --- téléchargement archive.org (pages 2 et 3) ---
    Private archiveSources As System.Collections.Generic.List(Of ArchiveSource)
    Private archiveSrcSel As Integer = 0
    Private archiveItem As String
    Private archiveSourceName As String
    Private archiveFiles As System.Collections.Generic.List(Of String)   ' Nothing = en cours de chargement
    Private archiveSel As Integer = 0
    Private archiveStatus As String = ""
    Private archiveBusy As Boolean = False
    Private archiveInstalledPath As String
    Private archiveLoadToken As Integer = 0   ' ignore les résultats d'une requête périmée
    Private ReadOnly archiveChecked As New System.Collections.Generic.HashSet(Of String)(StringComparer.Ordinal)
    Private archiveBatchQueue As System.Collections.Generic.Queue(Of String)
    Private archiveBatchTotal As Integer
    Private archiveBatchOk As Integer
    Private archiveBatchFail As Integer
    Private archiveBatchStop As Boolean

    Private Const WS_EX_NOACTIVATE As Integer = &H8000000

    ''' <summary>Un élément du menu principal : libellé, valeur affichée, et actions.</summary>
    Private Class Item
        Public Label As String
        Public Value As Func(Of String)     ' texte de droite (Nothing = simple action)
        Public OnAccept As Action
        Public OnLeft As Action
        Public OnRight As Action
    End Class

    Public Sub New(hostForm As MainForm)
        host = hostForm
        Me.FormBorderStyle = FormBorderStyle.None
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.Manual
        Me.BackColor = Color.FromArgb(12, 12, 18)
        Me.Opacity = 0.94
        Me.DoubleBuffered = True
        items = BuildItems()
    End Sub

    ''' <summary>Ne pas voler le focus à la fenêtre principale à l'affichage.</summary>
    Protected Overrides ReadOnly Property CreateParams As CreateParams
        Get
            Dim cp = MyBase.CreateParams
            cp.ExStyle = cp.ExStyle Or WS_EX_NOACTIVATE
            Return cp
        End Get
    End Property

    Private Function BuildItems() As System.Collections.Generic.List(Of Item)
        Dim l As New System.Collections.Generic.List(Of Item)
        l.Add(New Item With {.Label = "Reprendre", .OnAccept = Sub() host.CloseGamepadMenu()})
        l.Add(New Item With {.Label = "Charger un jeu…", .OnAccept = AddressOf OpenRomPage})
        l.Add(New Item With {.Label = "Télécharger des jeux…", .OnAccept = AddressOf OpenArchiveSourcesPage})
        l.Add(New Item With {.Label = "Sauvegarder l'état", .OnAccept = Sub() host.RequestSaveState()})
        l.Add(New Item With {.Label = "Charger l'état", .OnAccept = Sub() host.RequestLoadState()})
        l.Add(New Item With {.Label = "Réinitialiser", .OnAccept = Sub() host.RequestReset()})
        l.Add(New Item With {.Label = "Filtre d'affichage", .Value = Function() host.MenuShaderLabel,
                             .OnAccept = Sub() host.MenuCycleShader(1),
                             .OnLeft = Sub() host.MenuCycleShader(-1),
                             .OnRight = Sub() host.MenuCycleShader(1)})
        l.Add(New Item With {.Label = "Aspect 4:3", .Value = Function() OnOff(host.MenuAspectOn),
                             .OnAccept = Sub() host.MenuToggleAspect(),
                             .OnLeft = Sub() host.MenuToggleAspect(),
                             .OnRight = Sub() host.MenuToggleAspect()})
        l.Add(New Item With {.Label = "Plein écran", .Value = Function() OnOff(host.MenuFullscreenOn),
                             .OnAccept = Sub() host.MenuToggleFullscreenFromPad(),
                             .OnLeft = Sub() host.MenuToggleFullscreenFromPad(),
                             .OnRight = Sub() host.MenuToggleFullscreenFromPad()})
        l.Add(New Item With {.Label = "Taille de la fenêtre", .Value = Function() host.MenuScaleValue & "x",
                             .OnAccept = Sub() host.MenuCycleScale(1),
                             .OnLeft = Sub() host.MenuCycleScale(-1),
                             .OnRight = Sub() host.MenuCycleScale(1)})
        l.Add(New Item With {.Label = "Quitter l'émulateur", .OnAccept = Sub() host.RequestQuit()})
        Return l
    End Function

    Private Shared Function OnOff(b As Boolean) As String
        Return If(b, "Oui", "Non")
    End Function

    ''' <summary>Réinitialise l'overlay sur la page principale (à chaque ouverture).</summary>
    Public Sub ResetToRoot()
        page = 0
        sel = 0
        Invalidate()
    End Sub

    Private Sub OpenRomPage()
        roms = host.MenuRomList()
        romSel = 0
        page = 1
        Invalidate()
    End Sub

    Private Sub OpenArchiveSourcesPage()
        archiveSources = host.MenuArchiveSources()
        archiveSrcSel = 0
        page = 2
        Invalidate()
    End Sub

    ''' <summary>Charge la liste des fichiers d'une source en tâche de fond (page 3).</summary>
    Private Sub OpenArchiveFilesPage(src As ArchiveSource)
        archiveItem = src.Item
        archiveSourceName = src.Name
        archiveFiles = Nothing
        archiveSel = 0
        archiveBusy = False
        archiveInstalledPath = Nothing
        archiveStatus = "Chargement de la liste…"
        archiveChecked.Clear()
        archiveBatchQueue = Nothing
        page = 3
        archiveLoadToken += 1
        Dim myToken = archiveLoadToken
        host.MenuFetchArchiveFiles(archiveItem,
            Sub(files, err)
                If myToken <> archiveLoadToken Then Return   ' la page a changé entre-temps
                If err IsNot Nothing Then
                    archiveFiles = New System.Collections.Generic.List(Of String)()
                    archiveStatus = "Échec : " & err
                Else
                    archiveFiles = files
                    archiveSel = 0
                    archiveStatus = If(files.Count = 0,
                        "Aucun jeu compatible, ou déjà tous installés.",
                        $"{files.Count} jeux disponibles.")
                End If
                Invalidate()
            End Sub)
        Invalidate()
    End Sub

    ''' <summary>Lance le téléchargement du fichier choisi en tâche de fond (page 3).</summary>
    Private Sub StartArchiveDownload(name As String)
        archiveBusy = True
        archiveBatchStop = False
        archiveStatus = "Préparation…"
        host.MenuDownloadArchiveFile(archiveItem, name,
            Sub(msg)
                archiveStatus = msg
                Invalidate()
            End Sub,
            Sub(path, err)
                archiveBusy = False
                If err IsNot Nothing Then
                    archiveStatus = "Échec : " & err
                Else
                    archiveInstalledPath = path
                    archiveStatus = "Installé : " & System.IO.Path.GetFileName(path) & "  —  A pour lancer"
                End If
                Invalidate()
            End Sub)
        Invalidate()
    End Sub

    ''' <summary>Coche/décoche le jeu en surbrillance, pour un téléchargement groupé (bouton Y).</summary>
    Public Sub ToggleCheck()
        If page <> 3 OrElse archiveBusy OrElse archiveFiles Is Nothing OrElse archiveFiles.Count = 0 Then Return
        Dim f = archiveFiles(archiveSel)
        If archiveChecked.Contains(f) Then archiveChecked.Remove(f) Else archiveChecked.Add(f)
        Invalidate()
    End Sub

    ''' <summary>Lance le téléchargement de tous les jeux cochés, un par un (bouton X).</summary>
    Public Sub StartBatch()
        If page <> 3 OrElse archiveBusy Then Return
        If archiveChecked.Count = 0 Then
            archiveStatus = "Aucun jeu coché (Y pour cocher, X pour lancer)."
            Invalidate()
            Return
        End If
        archiveBatchQueue = New System.Collections.Generic.Queue(Of String)()
        For Each f In archiveFiles
            If archiveChecked.Contains(f) Then archiveBatchQueue.Enqueue(f)
        Next
        archiveBatchTotal = archiveBatchQueue.Count
        archiveBatchOk = 0
        archiveBatchFail = 0
        archiveBatchStop = False
        archiveBusy = True
        archiveInstalledPath = Nothing
        ProcessNextBatchItem()
    End Sub

    Private Sub ProcessNextBatchItem()
        If archiveBatchStop OrElse archiveBatchQueue Is Nothing OrElse archiveBatchQueue.Count = 0 Then
            FinishBatch()
            Return
        End If
        Dim name = archiveBatchQueue.Dequeue()
        Dim idx1 = archiveBatchTotal - archiveBatchQueue.Count
        Dim label = System.IO.Path.GetFileNameWithoutExtension(name)
        archiveStatus = $"[{idx1}/{archiveBatchTotal}] {label} — préparation…"
        Invalidate()
        host.MenuDownloadArchiveFile(archiveItem, name,
            Sub(msg)
                archiveStatus = $"[{idx1}/{archiveBatchTotal}] {label} — {msg}"
                Invalidate()
            End Sub,
            Sub(path, err)
                If err Is Nothing Then
                    archiveBatchOk += 1
                    archiveChecked.Remove(name)
                Else
                    archiveBatchFail += 1
                End If
                ProcessNextBatchItem()
            End Sub)
    End Sub

    Private Sub FinishBatch()
        archiveBusy = False
        Dim summary As String
        If archiveBatchStop Then
            summary = $"Annulé — {archiveBatchOk}/{archiveBatchTotal} installés."
        ElseIf archiveBatchFail = 0 Then
            summary = $"{archiveBatchOk}/{archiveBatchTotal} jeux installés."
        Else
            summary = $"{archiveBatchOk}/{archiveBatchTotal} installés, {archiveBatchFail} échec(s)."
        End If
        archiveBatchQueue = Nothing
        ' rafraîchir la liste (masque les jeux désormais possédés), en gardant le résumé affiché
        archiveLoadToken += 1
        Dim myToken = archiveLoadToken
        host.MenuFetchArchiveFiles(archiveItem,
            Sub(files, err)
                If myToken <> archiveLoadToken Then Return
                If err Is Nothing Then
                    archiveFiles = files
                    archiveSel = Math.Min(archiveSel, Math.Max(0, files.Count - 1))
                End If
                archiveStatus = summary
                Invalidate()
            End Sub)
        archiveStatus = summary
        Invalidate()
    End Sub

    ' ===== Navigation (appelée depuis le thread UI par MainForm) =====

    Public Sub NavUp()
        If page = 0 Then
            sel = (sel - 1 + items.Count) Mod items.Count
        ElseIf page = 1 AndAlso roms IsNot Nothing AndAlso roms.Count > 0 Then
            romSel = (romSel - 1 + roms.Count) Mod roms.Count
        ElseIf page = 2 AndAlso archiveSources IsNot Nothing AndAlso archiveSources.Count > 0 Then
            archiveSrcSel = (archiveSrcSel - 1 + archiveSources.Count) Mod archiveSources.Count
        ElseIf page = 3 AndAlso Not archiveBusy AndAlso archiveFiles IsNot Nothing AndAlso archiveFiles.Count > 0 Then
            archiveSel = (archiveSel - 1 + archiveFiles.Count) Mod archiveFiles.Count
        End If
        Invalidate()
    End Sub

    Public Sub NavDown()
        If page = 0 Then
            sel = (sel + 1) Mod items.Count
        ElseIf page = 1 AndAlso roms IsNot Nothing AndAlso roms.Count > 0 Then
            romSel = (romSel + 1) Mod roms.Count
        ElseIf page = 2 AndAlso archiveSources IsNot Nothing AndAlso archiveSources.Count > 0 Then
            archiveSrcSel = (archiveSrcSel + 1) Mod archiveSources.Count
        ElseIf page = 3 AndAlso Not archiveBusy AndAlso archiveFiles IsNot Nothing AndAlso archiveFiles.Count > 0 Then
            archiveSel = (archiveSel + 1) Mod archiveFiles.Count
        End If
        Invalidate()
    End Sub

    Public Sub NavLeft()
        If page = 0 Then
            Dim a = items(sel).OnLeft
            If a IsNot Nothing Then a.Invoke()
        End If
        Invalidate()
    End Sub

    Public Sub NavRight()
        If page = 0 Then
            Dim a = items(sel).OnRight
            If a IsNot Nothing Then a.Invoke()
        End If
        Invalidate()
    End Sub

    Public Sub Accept()
        If page = 0 Then
            Dim a = items(sel).OnAccept
            If a IsNot Nothing Then a.Invoke()
        ElseIf page = 1 Then
            If roms IsNot Nothing AndAlso romSel < roms.Count Then host.MenuLoadRom(roms(romSel))
        ElseIf page = 2 Then
            If archiveSources IsNot Nothing AndAlso archiveSrcSel < archiveSources.Count Then
                OpenArchiveFilesPage(archiveSources(archiveSrcSel))
            End If
        ElseIf page = 3 Then
            If archiveBusy Then Return
            If archiveInstalledPath IsNot Nothing Then
                host.MenuLoadRom(archiveInstalledPath)
            ElseIf archiveFiles IsNot Nothing AndAlso archiveSel < archiveFiles.Count Then
                StartArchiveDownload(archiveFiles(archiveSel))
            End If
        End If
        Invalidate()
    End Sub

    Public Sub Back()
        If page = 3 Then
            If archiveBusy Then
                archiveBatchStop = True
                host.MenuCancelArchiveDownload()
                archiveStatus = "Annulation…"
            Else
                page = 2
            End If
            Invalidate()
        ElseIf page = 1 OrElse page = 2 Then
            page = 0
            Invalidate()
        Else
            host.CloseGamepadMenu()
        End If
    End Sub

    ' ===== Rendu =====

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit
        Select Case page
            Case 0 : PaintMain(g)
            Case 1 : PaintRoms(g)
            Case 2 : PaintArchiveSources(g)
            Case Else : PaintArchiveFiles(g)
        End Select
    End Sub

    Private Sub PaintMain(g As Graphics)
        Dim W = ClientSize.Width, H = ClientSize.Height
        Dim titleFont As New Font("Segoe UI", 20, FontStyle.Bold)
        Dim itemFont As New Font("Segoe UI", 14, FontStyle.Regular)
        Dim hintFont As New Font("Segoe UI", 9, FontStyle.Regular)
        Using accent As New SolidBrush(Color.FromArgb(60, 120, 220)),
              white As New SolidBrush(Color.White),
              grey As New SolidBrush(Color.FromArgb(180, 180, 190)),
              dim_ As New SolidBrush(Color.FromArgb(120, 120, 130))
            g.DrawString("CONFIGURATION", titleFont, white, 40, 28)
            Dim y As Single = 90
            Dim rowH As Single = Math.Min(40, (H - 150) / items.Count)
            For i = 0 To items.Count - 1
                Dim it = items(i)
                If i = sel Then
                    Using sb As New SolidBrush(Color.FromArgb(40, 60, 120))
                        g.FillRectangle(sb, 28, y - 4, W - 56, rowH)
                    End Using
                    g.FillRectangle(accent, 28, y - 4, 5, rowH)
                End If
                g.DrawString(it.Label, itemFont, If(i = sel, white, grey), 48, y)
                If it.Value IsNot Nothing Then
                    Dim v = it.Value.Invoke()
                    Dim sz = g.MeasureString(v, itemFont)
                    g.DrawString(v, itemFont, If(i = sel, white, grey), W - 48 - sz.Width, y)
                End If
                y += rowH
            Next
            g.DrawString("Croix : naviguer    ←→ : modifier    A : valider    B : retour    LB+RT : fermer",
                         hintFont, dim_, 40, H - 34)
        End Using
        titleFont.Dispose() : itemFont.Dispose() : hintFont.Dispose()
    End Sub

    Private Sub PaintRoms(g As Graphics)
        Dim W = ClientSize.Width, H = ClientSize.Height
        Dim titleFont As New Font("Segoe UI", 20, FontStyle.Bold)
        Dim itemFont As New Font("Segoe UI", 13, FontStyle.Regular)
        Dim hintFont As New Font("Segoe UI", 9, FontStyle.Regular)
        Using white As New SolidBrush(Color.White),
              grey As New SolidBrush(Color.FromArgb(180, 180, 190)),
              dim_ As New SolidBrush(Color.FromArgb(120, 120, 130)),
              accent As New SolidBrush(Color.FromArgb(60, 120, 220))
            g.DrawString("CHARGER UN JEU", titleFont, white, 40, 28)
            If roms Is Nothing OrElse roms.Count = 0 Then
                g.DrawString("Aucun jeu dans le dossier « games ».", itemFont, grey, 48, 100)
            Else
                Dim rowH As Single = 30
                Dim top As Single = 90
                Dim visible = CInt(Math.Floor((H - top - 40) / rowH))
                Dim first = Math.Max(0, Math.Min(romSel - visible \ 2, Math.Max(0, roms.Count - visible)))
                Dim y As Single = top
                For i = first To Math.Min(roms.Count - 1, first + visible - 1)
                    If i = romSel Then
                        Using sb As New SolidBrush(Color.FromArgb(40, 60, 120))
                            g.FillRectangle(sb, 28, y - 3, W - 56, rowH)
                        End Using
                        g.FillRectangle(accent, 28, y - 3, 5, rowH)
                    End If
                    Dim name = System.IO.Path.GetFileName(roms(i))
                    g.DrawString(name, itemFont, If(i = romSel, white, grey), 48, y)
                    y += rowH
                Next
                g.DrawString((romSel + 1) & " / " & roms.Count, hintFont, dim_, W - 120, top - 26)
            End If
            g.DrawString("Croix : naviguer    A : charger    B : retour", hintFont, dim_, 40, H - 34)
        End Using
        titleFont.Dispose() : itemFont.Dispose() : hintFont.Dispose()
    End Sub

    Private Sub PaintArchiveSources(g As Graphics)
        Dim W = ClientSize.Width, H = ClientSize.Height
        Dim titleFont As New Font("Segoe UI", 20, FontStyle.Bold)
        Dim itemFont As New Font("Segoe UI", 14, FontStyle.Regular)
        Dim hintFont As New Font("Segoe UI", 9, FontStyle.Regular)
        Using white As New SolidBrush(Color.White),
              grey As New SolidBrush(Color.FromArgb(180, 180, 190)),
              dim_ As New SolidBrush(Color.FromArgb(120, 120, 130)),
              accent As New SolidBrush(Color.FromArgb(60, 120, 220))
            g.DrawString("TÉLÉCHARGER DES JEUX", titleFont, white, 40, 28)
            If archiveSources Is Nothing OrElse archiveSources.Count = 0 Then
                g.DrawString("Aucune source configurée. Ajoutez-en une depuis",
                             itemFont, grey, 48, 100)
                g.DrawString("« Fichier › Télécharger des jeux… » sur l'ordinateur.",
                             itemFont, grey, 48, 128)
            Else
                Dim y As Single = 90
                Dim rowH As Single = 36
                For i = 0 To archiveSources.Count - 1
                    If i = archiveSrcSel Then
                        Using sb As New SolidBrush(Color.FromArgb(40, 60, 120))
                            g.FillRectangle(sb, 28, y - 4, W - 56, rowH)
                        End Using
                        g.FillRectangle(accent, 28, y - 4, 5, rowH)
                    End If
                    g.DrawString(archiveSources(i).Name, itemFont, If(i = archiveSrcSel, white, grey), 48, y)
                    y += rowH
                Next
            End If
            g.DrawString("Croix : naviguer    A : ouvrir    B : retour", hintFont, dim_, 40, H - 34)
        End Using
        titleFont.Dispose() : itemFont.Dispose() : hintFont.Dispose()
    End Sub

    Private Sub PaintArchiveFiles(g As Graphics)
        Dim W = ClientSize.Width, H = ClientSize.Height
        Dim titleFont As New Font("Segoe UI", 18, FontStyle.Bold)
        Dim itemFont As New Font("Segoe UI", 13, FontStyle.Regular)
        Dim hintFont As New Font("Segoe UI", 9, FontStyle.Regular)
        Using white As New SolidBrush(Color.White),
              grey As New SolidBrush(Color.FromArgb(180, 180, 190)),
              dim_ As New SolidBrush(Color.FromArgb(120, 120, 130)),
              accent As New SolidBrush(Color.FromArgb(60, 120, 220)),
              statusBrush As New SolidBrush(Color.FromArgb(255, 210, 120))
            g.DrawString("TÉLÉCHARGER — " & archiveSourceName, titleFont, white, 40, 24)
            g.DrawString(archiveStatus, itemFont, statusBrush, 48, 58)
            If archiveFiles IsNot Nothing AndAlso archiveFiles.Count > 0 Then
                Dim rowH As Single = 28
                Dim top As Single = 92
                Dim visible = CInt(Math.Floor((H - top - 40) / rowH))
                Dim first = Math.Max(0, Math.Min(archiveSel - visible \ 2, Math.Max(0, archiveFiles.Count - visible)))
                Dim y As Single = top
                For i = first To Math.Min(archiveFiles.Count - 1, first + visible - 1)
                    If i = archiveSel Then
                        Using sb As New SolidBrush(Color.FromArgb(40, 60, 120))
                            g.FillRectangle(sb, 28, y - 3, W - 56, rowH)
                        End Using
                        g.FillRectangle(accent, 28, y - 3, 5, rowH)
                    End If
                    Dim name = System.IO.Path.GetFileNameWithoutExtension(archiveFiles(i))
                    Dim box = If(archiveChecked.Contains(archiveFiles(i)), "[x] ", "[ ] ")
                    g.DrawString(box & name, itemFont, If(i = archiveSel, white, grey), 48, y)
                    y += rowH
                Next
            End If
            Dim hint = If(archiveBusy, "B : annuler",
                        If(archiveInstalledPath IsNot Nothing, "A : lancer le jeu    B : retour",
                           "↑↓ naviguer   A télécharger   Y cocher   X lancer (" & archiveChecked.Count & ")   B retour"))
            g.DrawString(hint, hintFont, dim_, 40, H - 34)
        End Using
        titleFont.Dispose() : itemFont.Dispose() : hintFont.Dispose()
    End Sub

End Class
