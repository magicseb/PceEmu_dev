''' <summary>
''' Bibliothèque de jeux : liste le contenu du dossier configuré et permet d'en
''' lancer un. Les ROMs nues comme les archives ZIP et 7z y figurent.
''' </summary>
Public Class RomLibraryForm
    Inherits System.Windows.Forms.Form

    Private ReadOnly list As System.Windows.Forms.ListView
    Private ReadOnly filterBox As System.Windows.Forms.TextBox
    Private ReadOnly folderLabel As System.Windows.Forms.Label
    Private ReadOnly config As Settings

    Private entries As New System.Collections.Generic.List(Of String)

    ''' <summary>Chemin du jeu retenu, ou Nothing si la fenêtre a été fermée sans choix.</summary>
    Public ReadOnly Property SelectedRom As String

    Public Sub New(settings As Settings)
        config = settings

        Text = "Bibliothèque de jeux"
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        ClientSize = New System.Drawing.Size(560, 440)
        MinimumSize = New System.Drawing.Size(420, 300)

        list = New System.Windows.Forms.ListView() With {
            .View = System.Windows.Forms.View.Details,
            .FullRowSelect = True,
            .MultiSelect = False,
            .HideSelection = False,
            .Dock = System.Windows.Forms.DockStyle.Fill
        }
        list.Columns.Add("Jeu", 340)
        list.Columns.Add("Format", 80)
        list.Columns.Add("Taille", 90)
        AddHandler list.DoubleClick, AddressOf ChooseSelected

        filterBox = New System.Windows.Forms.TextBox() With {
            .Dock = System.Windows.Forms.DockStyle.Top,
            .PlaceholderText = "Filtrer par nom…"
        }
        AddHandler filterBox.TextChanged, Sub() Refill()

        folderLabel = New System.Windows.Forms.Label() With {
            .Dock = System.Windows.Forms.DockStyle.Top,
            .Height = 24,
            .Padding = New System.Windows.Forms.Padding(2, 4, 2, 0),
            .AutoEllipsis = True
        }

        Dim buttons = New System.Windows.Forms.FlowLayoutPanel() With {
            .Dock = System.Windows.Forms.DockStyle.Bottom,
            .Height = 44,
            .FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
            .Padding = New System.Windows.Forms.Padding(6)
        }

        Dim playButton = New System.Windows.Forms.Button() With {.Text = "Jouer", .Width = 90}
        AddHandler playButton.Click, AddressOf ChooseSelected

        Dim closeButton = New System.Windows.Forms.Button() With {.Text = "Fermer", .Width = 90}
        AddHandler closeButton.Click, Sub() Close()

        Dim folderButton = New System.Windows.Forms.Button() With {.Text = "Changer de dossier…", .Width = 150}
        AddHandler folderButton.Click, AddressOf ChangeFolder

        Dim refreshButton = New System.Windows.Forms.Button() With {.Text = "Actualiser", .Width = 100}
        AddHandler refreshButton.Click, Sub() Scan()

        buttons.Controls.AddRange({playButton, closeButton, folderButton, refreshButton})

        Controls.Add(list)
        Controls.Add(filterBox)
        Controls.Add(folderLabel)
        Controls.Add(buttons)

        Scan()
    End Sub

    ''' <summary>Relit le dossier des jeux, sous-dossiers compris.</summary>
    Private Sub Scan()
        Dim folder = config.GamesFolder
        folderLabel.Text = "Dossier : " & folder

        entries.Clear()

        Try
            If Not System.IO.Directory.Exists(folder) Then
                System.IO.Directory.CreateDirectory(folder)
            End If

            For Each file In System.IO.Directory.EnumerateFiles(folder, "*.*", System.IO.SearchOption.AllDirectories)
                If RomArchive.IsSupported(file) Then entries.Add(file)
            Next

            entries.Sort(StringComparer.CurrentCultureIgnoreCase)
        Catch ex As Exception
            folderLabel.Text = "Dossier illisible : " & ex.Message
        End Try

        Refill()
    End Sub

    Private Sub Refill()
        Dim needle = filterBox.Text.Trim()

        list.BeginUpdate()
        list.Items.Clear()

        For Each path In entries
            Dim name = System.IO.Path.GetFileNameWithoutExtension(path)
            If needle.Length > 0 AndAlso name.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) < 0 Then
                Continue For
            End If

            Dim row = New System.Windows.Forms.ListViewItem(name)
            row.SubItems.Add(System.IO.Path.GetExtension(path).TrimStart("."c).ToUpperInvariant())
            row.SubItems.Add(FormatSize(path))
            row.Tag = path
            list.Items.Add(row)
        Next

        list.EndUpdate()

        If list.Items.Count = 0 AndAlso entries.Count = 0 Then
            folderLabel.Text = "Dossier : " & config.GamesFolder & "  (vide — déposez-y vos jeux)"
        End If
    End Sub

    Private Shared Function FormatSize(path As String) As String
        Try
            Return (New System.IO.FileInfo(path).Length \ 1024) & " Ko"
        Catch ex As Exception
            Return "?"
        End Try
    End Function

    Private Sub ChangeFolder(sender As Object, e As EventArgs)
        Using dialog = New System.Windows.Forms.FolderBrowserDialog()
            dialog.Description = "Choisir le dossier des jeux"
            dialog.SelectedPath = config.GamesFolder

            If dialog.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
                config.GamesFolder = dialog.SelectedPath
                config.Save()
                Scan()
            End If
        End Using
    End Sub

    Private Sub ChooseSelected(sender As Object, e As EventArgs)
        If list.SelectedItems.Count = 0 Then Return
        _SelectedRom = list.SelectedItems(0).Tag.ToString()
        DialogResult = System.Windows.Forms.DialogResult.OK
        Close()
    End Sub

End Class
