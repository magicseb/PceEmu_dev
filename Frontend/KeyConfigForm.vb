''' <summary>
''' Fenêtre de réglage des touches : on choisit une action dans la liste, on appuie
''' sur la touche voulue, c'est fait.
''' </summary>
Public Class KeyConfigForm
    Inherits System.Windows.Forms.Form

    Private ReadOnly grid As System.Windows.Forms.ListView
    Private ReadOnly hint As System.Windows.Forms.Label
    Private ReadOnly config As Settings

    ''' <summary>Assignations en cours d'édition, validées seulement à la fermeture.</summary>
    Private ReadOnly pending As New System.Collections.Generic.Dictionary(Of String, System.Windows.Forms.Keys)

    Private capturing As Boolean = False

    Public Sub New(input As InputManager, settings As Settings)
        config = settings

        Text = "Configuration des touches"
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        ClientSize = New System.Drawing.Size(380, 400)
        KeyPreview = True

        For Each action In InputManager.AllActions
            pending(action) = input.BindingOf(action)
        Next

        grid = New System.Windows.Forms.ListView() With {
            .View = System.Windows.Forms.View.Details,
            .FullRowSelect = True,
            .MultiSelect = False,
            .HideSelection = False,
            .Dock = System.Windows.Forms.DockStyle.Fill
        }
        grid.Columns.Add("Action", 180)
        grid.Columns.Add("Touche", 160)
        AddHandler grid.DoubleClick, AddressOf BeginCapture

        hint = New System.Windows.Forms.Label() With {
            .Dock = System.Windows.Forms.DockStyle.Top,
            .Height = 46,
            .Padding = New System.Windows.Forms.Padding(8, 6, 8, 6),
            .Text = "Double-cliquez une action puis appuyez sur la touche voulue." & Environment.NewLine &
                    "Disposition détectée : " & KeyboardLayout.Describe()
        }

        Dim buttons = New System.Windows.Forms.FlowLayoutPanel() With {
            .Dock = System.Windows.Forms.DockStyle.Bottom,
            .Height = 44,
            .FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
            .Padding = New System.Windows.Forms.Padding(6)
        }

        Dim okButton = New System.Windows.Forms.Button() With {.Text = "Valider", .Width = 90}
        AddHandler okButton.Click, AddressOf ConfirmAndClose

        Dim cancelButton = New System.Windows.Forms.Button() With {.Text = "Annuler", .Width = 90}
        AddHandler cancelButton.Click, Sub() Close()

        Dim resetButton = New System.Windows.Forms.Button() With {.Text = "Par défaut", .Width = 110}
        AddHandler resetButton.Click, AddressOf RestoreDefaults

        buttons.Controls.AddRange({okButton, cancelButton, resetButton})

        Controls.Add(grid)
        Controls.Add(buttons)
        Controls.Add(hint)

        Refill()
    End Sub

    Private Sub Refill()
        grid.Items.Clear()
        For Each action In InputManager.AllActions
            Dim row = New System.Windows.Forms.ListViewItem(action)
            row.SubItems.Add(Describe(pending(action)))
            row.Tag = action
            grid.Items.Add(row)
        Next
    End Sub

    Private Shared Function Describe(key As System.Windows.Forms.Keys) As String
        If key = System.Windows.Forms.Keys.None Then Return "(aucune)"
        Return key.ToString()
    End Function

    Private Sub BeginCapture(sender As Object, e As EventArgs)
        If grid.SelectedItems.Count = 0 Then Return
        capturing = True
        hint.Text = "Appuyez sur la touche à assigner à « " & grid.SelectedItems(0).Tag.ToString() & " »." &
                    Environment.NewLine & "Échap pour renoncer."
    End Sub

    Protected Overrides Sub OnKeyDown(e As System.Windows.Forms.KeyEventArgs)
        If Not capturing Then
            MyBase.OnKeyDown(e)
            Return
        End If

        e.SuppressKeyPress = True
        capturing = False

        If e.KeyCode <> System.Windows.Forms.Keys.Escape AndAlso grid.SelectedItems.Count > 0 Then
            Dim action = grid.SelectedItems(0).Tag.ToString()

            ' Une touche ne sert qu'à une action : on libère l'ancienne
            For Each other In InputManager.AllActions
                If other <> action AndAlso pending(other) = e.KeyCode Then
                    pending(other) = System.Windows.Forms.Keys.None
                End If
            Next

            pending(action) = e.KeyCode
            Refill()
        End If

        hint.Text = "Double-cliquez une action puis appuyez sur la touche voulue." & Environment.NewLine &
                    "Disposition détectée : " & KeyboardLayout.Describe()
    End Sub

    Private Sub RestoreDefaults(sender As Object, e As EventArgs)
        For Each action In InputManager.AllActions
            pending(action) = InputManager.DefaultKey(action)
        Next
        Refill()
    End Sub

    Private Sub ConfirmAndClose(sender As Object, e As EventArgs)
        config.ClearBindings()
        For Each action In InputManager.AllActions
            config.SetBinding(action, pending(action))
        Next
        config.Save()

        DialogResult = System.Windows.Forms.DialogResult.OK
        Close()
    End Sub

End Class
