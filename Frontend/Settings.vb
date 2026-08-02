''' <summary>
''' Réglages de l'émulateur, conservés dans un fichier texte à côté de l'exécutable.
''' Le format est volontairement lisible : une ligne « clé = valeur », que l'on peut
''' corriger à la main si une touche a été mal assignée.
''' </summary>
Public Class Settings

    Private Const FILE_NAME As String = "PceEmu.cfg"

    Private values As New System.Collections.Generic.Dictionary(Of String, String)(
        StringComparer.OrdinalIgnoreCase)

    ''' <summary>Dossier où sont rangés les jeux.</summary>
    Public Property GamesFolder As String
        Get
            Dim stored = GetValue("DossierJeux", "")
            If String.IsNullOrWhiteSpace(stored) Then
                Return System.IO.Path.Combine(AppContext.BaseDirectory, "games")
            End If
            Return stored
        End Get
        Set(value As String)
            values("DossierJeux") = value
        End Set
    End Property

    ''' <summary>Assignation d'une action à une touche, ou Nothing si jamais réglée.</summary>
    Public Function GetBinding(action As String) As System.Windows.Forms.Keys?
        Dim raw = GetValue("Touche." & action, "")
        If String.IsNullOrWhiteSpace(raw) Then Return Nothing

        Dim parsed As System.Windows.Forms.Keys
        If [Enum].TryParse(raw, True, parsed) Then Return parsed
        Return Nothing
    End Function

    Public Sub SetBinding(action As String, key As System.Windows.Forms.Keys)
        values("Touche." & action) = key.ToString()
    End Sub

    ''' <summary>Efface toutes les assignations pour revenir aux touches par défaut.</summary>
    Public Sub ClearBindings()
        For Each name In values.Keys.Where(Function(k) k.StartsWith("Touche.", StringComparison.OrdinalIgnoreCase)).ToList()
            values.Remove(name)
        Next
    End Sub

    ''' <summary>
    ''' Sources archive.org enregistrées, dans l'ordre. Chacune associe un libellé
    ''' lisible à un identifiant d'item archive.org (ex. « nom | identifiant-item »),
    ''' stockée sous des clés « Source.1 », « Source.2 »… La liste part vide : c'est
    ''' l'utilisateur qui décide vers quels items pointer.
    ''' </summary>
    Public Function GetArchiveSources() As System.Collections.Generic.List(Of ArchiveSource)
        Dim result As New System.Collections.Generic.List(Of ArchiveSource)

        Dim numbered = values _
            .Where(Function(p) p.Key.StartsWith("Source.", StringComparison.OrdinalIgnoreCase)) _
            .Select(Function(p) New With {.Order = OrderOf(p.Key), .Raw = p.Value}) _
            .OrderBy(Function(x) x.Order)

        For Each entry In numbered
            Dim raw = entry.Raw
            Dim cut = raw.IndexOf(" | ", StringComparison.Ordinal)
            If cut >= 0 Then
                result.Add(New ArchiveSource(raw.Substring(0, cut).Trim(), raw.Substring(cut + 3).Trim()))
            ElseIf raw.Trim().Length > 0 Then
                ' Valeur sans séparateur : on la prend comme identifiant, libellé = l'identifiant.
                result.Add(New ArchiveSource(raw.Trim(), raw.Trim()))
            End If
        Next

        Return result
    End Function

    ''' <summary>Remplace la liste des sources et renumérote proprement.</summary>
    Public Sub SetArchiveSources(sources As System.Collections.Generic.IEnumerable(Of ArchiveSource))
        For Each name In values.Keys.Where(
                Function(k) k.StartsWith("Source.", StringComparison.OrdinalIgnoreCase)).ToList()
            values.Remove(name)
        Next

        Dim index = 1
        For Each s In sources
            If s Is Nothing OrElse String.IsNullOrWhiteSpace(s.Item) Then Continue For
            Dim label = If(String.IsNullOrWhiteSpace(s.Name), s.Item, s.Name)
            values("Source." & index) = label.Trim() & " | " & s.Item.Trim()
            index += 1
        Next
    End Sub

    Private Shared Function OrderOf(key As String) As Integer
        Dim dot = key.IndexOf("."c)
        Dim n As Integer
        If dot >= 0 AndAlso Integer.TryParse(key.Substring(dot + 1), n) Then Return n
        Return Integer.MaxValue
    End Function

    ''' <summary>Vrai si la manette Xbox doit être lue.</summary>
    Public Property GamepadEnabled As Boolean
        Get
            Return GetValue("Manette", "1") <> "0"
        End Get
        Set(value As Boolean)
            values("Manette") = If(value, "1", "0")
        End Set
    End Property

    ''' <summary>Chemin de la System Card (BIOS CD-ROM²), mémorisé pour lancer les jeux CD.</summary>
    Public Property SystemCardPath As String
        Get
            Return GetValue("SystemCard", "")
        End Get
        Set(value As String)
            values("SystemCard") = If(value, "")
        End Set
    End Property

    Private Function GetValue(key As String, fallback As String) As String
        Dim result As String = Nothing
        If values.TryGetValue(key, result) Then Return result
        Return fallback
    End Function

    Private Shared Function ConfigPath() As String
        Return System.IO.Path.Combine(AppContext.BaseDirectory, FILE_NAME)
    End Function

    ''' <summary>Relit le fichier de configuration, s'il existe.</summary>
    Public Shared Function Load() As Settings
        Dim result = New Settings()
        Dim path = ConfigPath()
        If Not System.IO.File.Exists(path) Then Return result

        Try
            For Each line In System.IO.File.ReadAllLines(path)
                Dim trimmed = line.Trim()
                If trimmed.Length = 0 OrElse trimmed.StartsWith("#") Then Continue For

                Dim separator = trimmed.IndexOf("="c)
                If separator <= 0 Then Continue For

                result.values(trimmed.Substring(0, separator).Trim()) =
                    trimmed.Substring(separator + 1).Trim()
            Next
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("Configuration illisible : " & ex.Message)
        End Try

        Return result
    End Function

    ''' <summary>Écrit la configuration ; un échec n'est jamais bloquant.</summary>
    Public Sub Save()
        Try
            Dim lines As New System.Collections.Generic.List(Of String) From {
                "# Configuration de PceEmu — modifiable à la main",
                "# Les noms de touches suivent l'énumération Keys de .NET (Z, W, Up, Enter, ShiftKey…)"
            }

            For Each pair In values.OrderBy(Function(p) p.Key)
                lines.Add(pair.Key & " = " & pair.Value)
            Next

            System.IO.File.WriteAllLines(ConfigPath(), lines)
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("Configuration non enregistrée : " & ex.Message)
        End Try
    End Sub

End Class
