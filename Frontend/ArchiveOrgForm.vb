Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Text.Json
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Une source archive.org : un libellé lisible et l'identifiant d'un item
''' (la partie qui suit archive.org/details/… ou archive.org/download/…).
''' </summary>
Public Class ArchiveSource
    Public Property Name As String
    Public Property Item As String

    Public Sub New()
    End Sub

    Public Sub New(displayName As String, itemId As String)
        Name = displayName
        Item = itemId
    End Sub
End Class

''' <summary>
''' Parcourt un item archive.org : liste ses fichiers via l'API « metadata »,
''' filtre par nom, et télécharge le fichier choisi dans le dossier de la
''' bibliothèque. Les archives (.zip/.7z) sont déposées telles quelles — la
''' bibliothèque sait déjà les ouvrir en mémoire.
'''
''' Aucune source n'est fournie d'origine : c'est l'utilisateur qui ajoute les
''' identifiants d'items qu'il souhaite. Télécharger des jeux commerciaux peut
''' relever d'une zone légale grise selon les pays ; à l'utilisateur de s'assurer
''' qu'il en a le droit.
''' </summary>
Public NotInheritable Class ArchiveOrgForm
    Inherits Form

    Private Shared ReadOnly _http As New HttpClient()

    Private ReadOnly _config As Settings
    Private ReadOnly _sources As List(Of ArchiveSource)

    Private ReadOnly _source As New ComboBox()
    Private ReadOnly _addSrcBtn As New Button()
    Private ReadOnly _delSrcBtn As New Button()
    Private ReadOnly _filter As New TextBox()
    Private ReadOnly _list As New ListBox()
    Private ReadOnly _downloadBtn As New Button()
    Private ReadOnly _cancelBtn As New Button()
    Private ReadOnly _statusLbl As New Label()

    Private ReadOnly _cache As New Dictionary(Of String, List(Of String))()
    Private _allFiles As New List(Of String)()  ' fichiers du set courant
    Private _shown As New List(Of String)()     ' fichiers affichés (après filtre)
    Private _curItem As String

    Private _cancel As Boolean
    Private _busy As Boolean

    ''' <summary>Chemin du fichier installé, ou Nothing si la fenêtre s'est fermée sans installation.</summary>
    Public Property LastDownloaded As String

    Shared Sub New()
        _http.Timeout = TimeSpan.FromMinutes(5)
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PceEmu/1.0")
    End Sub

    Public Sub New(config As Settings)
        _config = config
        _sources = config.GetArchiveSources()

        Text = "Télécharger des jeux (archive.org)"
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(720, 520)
        MinimumSize = New Size(560, 440)
        BackColor = Color.FromArgb(24, 24, 30)
        ForeColor = Color.White

        Dim lSrc As New Label() With {.Text = "Source :", .ForeColor = Color.Gainsboro}
        lSrc.SetBounds(12, 15, 62, 22)
        _source.SetBounds(78, 12, 300, 26)
        _source.DropDownStyle = ComboBoxStyle.DropDownList
        AddHandler _source.SelectedIndexChanged, Sub(s, e) OnSourceChanged()

        _addSrcBtn.SetBounds(384, 11, 92, 28)
        _addSrcBtn.Text = "Ajouter…"
        AddHandler _addSrcBtn.Click, Sub(s, e) OnAddSource()

        _delSrcBtn.SetBounds(480, 11, 92, 28)
        _delSrcBtn.Text = "Retirer"
        AddHandler _delSrcBtn.Click, Sub(s, e) OnRemoveSource()

        Dim lFil As New Label() With {.Text = "Filtrer :", .ForeColor = Color.Gainsboro}
        lFil.SetBounds(12, 51, 62, 22)
        _filter.SetBounds(78, 48, 494, 26)
        _filter.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        AddHandler _filter.TextChanged, Sub(s, e) ApplyFilter()

        _list.SetBounds(12, 84, 696, 384)
        _list.BackColor = Color.FromArgb(34, 34, 44)
        _list.ForeColor = Color.White
        _list.IntegralHeight = False
        _list.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        AddHandler _list.DoubleClick, Sub(s, e) DoDownload()

        _downloadBtn.SetBounds(12, 476, 200, 32)
        _downloadBtn.Text = "Télécharger et installer"
        _downloadBtn.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        AddHandler _downloadBtn.Click, Sub(s, e) DoDownload()

        _cancelBtn.SetBounds(220, 476, 92, 32)
        _cancelBtn.Text = "Annuler"
        _cancelBtn.Enabled = False
        _cancelBtn.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        AddHandler _cancelBtn.Click, Sub(s, e) _cancel = True

        _statusLbl.SetBounds(324, 482, 384, 22)
        _statusLbl.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        _statusLbl.ForeColor = Color.Gainsboro

        Controls.AddRange(New Control() {lSrc, _source, _addSrcBtn, _delSrcBtn,
                                         lFil, _filter, _list, _downloadBtn, _cancelBtn, _statusLbl})

        RefillSourceCombo()
    End Sub

    Private Sub SetStatus(msg As String)
        If InvokeRequired Then BeginInvoke(New Action(Sub() _statusLbl.Text = msg)) Else _statusLbl.Text = msg
    End Sub

    ''' <summary>Recharge le menu déroulant des sources depuis la liste en mémoire.</summary>
    Private Sub RefillSourceCombo()
        _source.Items.Clear()
        For Each s In _sources
            _source.Items.Add(s.Name)
        Next
        _delSrcBtn.Enabled = _sources.Count > 0

        If _sources.Count = 0 Then
            _list.Items.Clear()
            _allFiles = New List(Of String)()
            _shown = New List(Of String)()
            _curItem = Nothing
            SetStatus("Aucune source. Cliquez « Ajouter… » pour indiquer un item archive.org.")
        Else
            _source.SelectedIndex = 0   ' déclenche OnSourceChanged
        End If
    End Sub

    Private Sub OnAddSource()
        If _busy Then Return
        Using dlg = New ArchiveSourceEditForm()
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                _sources.Add(New ArchiveSource(dlg.SourceName, dlg.SourceItem))
                _config.SetArchiveSources(_sources)
                _config.Save()
                RefillSourceCombo()
                _source.SelectedIndex = _sources.Count - 1
            End If
        End Using
    End Sub

    Private Sub OnRemoveSource()
        If _busy Then Return
        Dim idx = _source.SelectedIndex
        If idx < 0 OrElse idx >= _sources.Count Then Return
        _sources.RemoveAt(idx)
        _config.SetArchiveSources(_sources)
        _config.Save()
        RefillSourceCombo()
    End Sub

    Private Sub OnSourceChanged()
        Dim idx = _source.SelectedIndex
        If idx < 0 OrElse idx >= _sources.Count Then Return
        _curItem = _sources(idx).Item
        _list.Items.Clear()
        _allFiles = New List(Of String)()

        If _cache.ContainsKey(_curItem) Then
            _allFiles = _cache(_curItem)
            ApplyFilter()
            SetStatus($"{_allFiles.Count} fichiers. Filtrez puis double-cliquez pour installer.")
            Return
        End If

        SetStatus("Chargement de la liste des fichiers…")
        Dim item = _curItem
        ThreadPool.QueueUserWorkItem(
            Sub()
                Try
                    Dim url = "https://archive.org/metadata/" & Uri.EscapeDataString(item)
                    Dim json = _http.GetStringAsync(url).GetAwaiter().GetResult()
                    Dim names As New List(Of String)()
                    Using doc = JsonDocument.Parse(json)
                        Dim files As JsonElement
                        If doc.RootElement.TryGetProperty("files", files) Then
                            For Each f In files.EnumerateArray()
                                Dim ne As JsonElement
                                If f.TryGetProperty("name", ne) Then
                                    Dim nm = ne.GetString()
                                    ' On ne retient que ce que la bibliothèque sait ouvrir.
                                    If nm IsNot Nothing AndAlso RomArchive.IsSupported(nm) Then names.Add(nm)
                                End If
                            Next
                        End If
                    End Using
                    names.Sort(StringComparer.OrdinalIgnoreCase)
                    BeginInvoke(New Action(Sub()
                                               If item <> _curItem Then Return
                                               _cache(item) = names
                                               _allFiles = names
                                               ApplyFilter()
                                               If names.Count = 0 Then
                                                   SetStatus("Aucun fichier compatible (.pce/.sgx/.bin/.zip/.7z) dans cet item.")
                                               Else
                                                   SetStatus($"{names.Count} fichiers. Filtrez puis double-cliquez pour installer.")
                                               End If
                                           End Sub))
                Catch ex As Exception
                    SetStatus("Échec du chargement : " & ex.Message)
                End Try
            End Sub)
    End Sub

    Private Sub ApplyFilter()
        Dim q = _filter.Text.Trim().ToLowerInvariant()
        _shown = If(q = "",
                    _allFiles,
                    _allFiles.Where(Function(n) n.ToLowerInvariant().Contains(q)).ToList())
        _list.BeginUpdate()
        _list.Items.Clear()
        For Each n In _shown
            _list.Items.Add(Path.GetFileNameWithoutExtension(n))
        Next
        _list.EndUpdate()
        If _shown.Count > 0 Then _list.SelectedIndex = 0
    End Sub

    Private Sub DoDownload()
        If _busy Then Return
        Dim i = _list.SelectedIndex
        If i < 0 OrElse i >= _shown.Count Then Return
        If String.IsNullOrEmpty(_config.GamesFolder) Then
            SetStatus("Aucun dossier de jeux configuré.")
            Return
        End If
        Try
            Directory.CreateDirectory(_config.GamesFolder)
        Catch
        End Try

        Dim item = _curItem
        Dim name = _shown(i)                     ' nom complet dans l'item (peut contenir des « / »)
        Dim localName = SafeLocalName(name)      ' nom de fichier seul, assaini
        SetBusy(True)
        SetStatus("Téléchargement de " & localName & "…")

        ThreadPool.QueueUserWorkItem(
            Sub()
                Dim destPath = Path.Combine(_config.GamesFolder, localName)
                Dim partPath = destPath & ".part"
                Try
                    Dim url = "https://archive.org/download/" & Uri.EscapeDataString(item) & "/" & EscapePath(name)
                    DownloadToFile(url, partPath)

                    If _cancel Then
                        TryDelete(partPath)
                        SetStatus("Annulé.")
                        BeginInvoke(New Action(Sub() SetBusy(False)))
                        Return
                    End If

                    If File.Exists(destPath) Then File.Delete(destPath)
                    File.Move(partPath, destPath)
                    LastDownloaded = destPath

                    BeginInvoke(New Action(Sub()
                                               SetBusy(False)
                                               SetStatus("Installé : " & Path.GetFileName(destPath))
                                               DialogResult = DialogResult.OK
                                           End Sub))
                Catch ex As Exception
                    TryDelete(partPath)
                    SetStatus("Échec : " & ex.Message)
                    BeginInvoke(New Action(Sub() SetBusy(False)))
                End Try
            End Sub)
    End Sub

    Private Sub DownloadToFile(url As String, destPath As String)
        Using resp = _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult()
            resp.EnsureSuccessStatusCode()
            Dim total = If(resp.Content.Headers.ContentLength.HasValue, resp.Content.Headers.ContentLength.Value, -1L)
            Using src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                Using fs As New FileStream(destPath, FileMode.Create, FileAccess.Write)
                    Dim buf(1048575) As Byte
                    Dim got As Long = 0, n As Integer
                    Do
                        If _cancel Then Exit Do
                        n = src.Read(buf, 0, buf.Length)
                        If n <= 0 Then Exit Do
                        fs.Write(buf, 0, n)
                        got += n
                        If total > 0 Then
                            SetStatus($"Téléchargement… {CInt(got * 100L \ total)} % ({got \ (1024 * 1024)}/{total \ (1024 * 1024)} Mo)")
                        Else
                            SetStatus($"Téléchargement… {got \ 1024} Ko")
                        End If
                    Loop
                End Using
            End Using
        End Using
    End Sub

    Private Sub SetBusy(busy As Boolean)
        _busy = busy
        If Not busy Then _cancel = False
        _downloadBtn.Enabled = Not busy
        _cancelBtn.Enabled = busy
        _source.Enabled = Not busy
        _addSrcBtn.Enabled = Not busy
        _delSrcBtn.Enabled = (Not busy) AndAlso _sources.Count > 0
        _list.Enabled = Not busy
        _filter.Enabled = Not busy
    End Sub

    ''' <summary>Nom de fichier local sûr : dernier segment, sans caractères interdits.</summary>
    Private Shared Function SafeLocalName(entryName As String) As String
        Dim leaf = Path.GetFileName(entryName.Replace("\"c, "/"c))
        If String.IsNullOrWhiteSpace(leaf) Then leaf = "jeu.bin"
        For Each bad In Path.GetInvalidFileNameChars()
            leaf = leaf.Replace(bad, "_"c)
        Next
        Return leaf
    End Function

    ''' <summary>Échappe chaque segment d'un chemin d'item, en gardant les « / ».</summary>
    Private Shared Function EscapePath(entryName As String) As String
        Return String.Join("/", entryName.Replace("\"c, "/"c).Split("/"c).
                           Select(Function(seg) Uri.EscapeDataString(seg)))
    End Function

    Private Shared Sub TryDelete(path As String)
        Try
            If File.Exists(path) Then File.Delete(path)
        Catch
        End Try
    End Sub

    ''' <summary>Empêche la fermeture pendant un téléchargement (annule d'abord).</summary>
    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If _busy Then
            _cancel = True
            e.Cancel = True
            SetStatus("Annulation en cours…")
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

End Class

''' <summary>Petit dialogue pour saisir une source archive.org : libellé + identifiant d'item.</summary>
Public NotInheritable Class ArchiveSourceEditForm
    Inherits Form

    Private ReadOnly _nameBox As New TextBox()
    Private ReadOnly _itemBox As New TextBox()

    Public ReadOnly Property SourceName As String
        Get
            Return _nameBox.Text.Trim()
        End Get
    End Property

    Public ReadOnly Property SourceItem As String
        Get
            Return _itemBox.Text.Trim()
        End Get
    End Property

    Public Sub New()
        Text = "Ajouter une source archive.org"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ClientSize = New Size(480, 190)

        Dim lName As New Label() With {.Text = "Libellé :", .Left = 12, .Top = 16, .Width = 90}
        _nameBox.SetBounds(108, 13, 358, 24)

        Dim lItem As New Label() With {.Text = "Identifiant d'item :", .Left = 12, .Top = 52, .Width = 90}
        _itemBox.SetBounds(108, 49, 358, 24)

        Dim hint As New Label() With {
            .Left = 108, .Top = 78, .Width = 358, .Height = 60,
            .ForeColor = Color.DimGray,
            .Text = "L'identifiant est la partie qui suit archive.org/details/ ou " &
                    "archive.org/download/ dans l'adresse de l'item (ex. pour " &
                    "archive.org/details/mon-item → « mon-item »)."}

        Dim okBtn As New Button() With {
            .Text = "OK", .Left = 284, .Top = 150, .Width = 84, .DialogResult = DialogResult.OK}
        Dim cancelBtn As New Button() With {
            .Text = "Annuler", .Left = 376, .Top = 150, .Width = 90, .DialogResult = DialogResult.Cancel}
        AddHandler okBtn.Click, AddressOf OnOk

        Controls.AddRange(New Control() {lName, _nameBox, lItem, _itemBox, hint, okBtn, cancelBtn})
        AcceptButton = okBtn
        MyBase.CancelButton = cancelBtn
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If SourceItem.Length = 0 Then
            MessageBox.Show(Me, "Indiquez l'identifiant d'un item archive.org.", "Source incomplète")
            DialogResult = DialogResult.None
            Return
        End If
        If SourceName.Length = 0 Then _nameBox.Text = SourceItem
    End Sub

End Class
