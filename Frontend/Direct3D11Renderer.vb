''' <summary>Rendu GDI+ : bitmap persistant + Paint event + double-buffering</summary>
Public Class Direct3D11Renderer
    Implements IDisposable

    Private bitmap As System.Drawing.Bitmap
    Private snapshot As System.Drawing.Bitmap        ' copie 1:1 de la frame pour dessiner hors verrou
    Private scaledBitmap As System.Drawing.Bitmap   ' intermédiaire préscalé (facteur entier)
    Private curPs As Integer = 0
    Private bitmapLock As New Object()
    Private panel As System.Windows.Forms.Panel
    Private curWidth As Integer = 0
    Private curHeight As Integer = 0
    ''' <summary>Vrai = affichage en 4:3 (aspect TV d'origine, pixels non carrés) ;
    ''' Faux = conserve l'aspect des pixels internes.</summary>
    Public ForceAspect43 As Boolean = True

    Public Sub New(width As UInteger, height As UInteger, panelRef As System.Windows.Forms.Panel)
        panel = panelRef

        ' Activer le double-buffering du panel (propriété protégée → réflexion)
        If panel IsNot Nothing Then
            Dim prop = GetType(System.Windows.Forms.Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance Or System.Reflection.BindingFlags.NonPublic)
            If prop IsNot Nothing Then prop.SetValue(panel, True, Nothing)
            AddHandler panel.Paint, AddressOf OnPanelPaint
            AddHandler panel.Resize, Sub() panel.Invalidate()
        End If
    End Sub

    ''' <summary>Met à jour l'image depuis le framebuffer (appelable depuis le thread d'émulation)</summary>
    Public Sub UpdateFrame(framebuffer() As Integer, displayWidth As Integer, displayHeight As Integer)
        If framebuffer Is Nothing OrElse panel Is Nothing Then Return
        If displayWidth < 8 Then displayWidth = 256
        If displayHeight < 8 Then displayHeight = 224

        SyncLock bitmapLock
            ' (Re)créer le bitmap si les dimensions d'affichage changent
            If bitmap Is Nothing OrElse curWidth <> displayWidth OrElse curHeight <> displayHeight Then
                If bitmap IsNot Nothing Then bitmap.Dispose()
                bitmap = New System.Drawing.Bitmap(displayWidth, displayHeight,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                curWidth = displayWidth
                curHeight = displayHeight
            End If

            Dim data = bitmap.LockBits(
                New System.Drawing.Rectangle(0, 0, displayWidth, displayHeight),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb)

            ' Copier ligne par ligne (le framebuffer a un stride de SCREEN_WIDTH)
            Dim srcStride = CInt(PceConstants.SCREEN_WIDTH)
            For y = 0 To displayHeight - 1
                Dim srcOffset = y * srcStride
                Dim dstPtr = data.Scan0 + y * data.Stride
                System.Runtime.InteropServices.Marshal.Copy(framebuffer, srcOffset, dstPtr, displayWidth)
            Next

            bitmap.UnlockBits(data)
        End SyncLock

        ' Demander un repaint (thread-safe)
        Try
            panel.Invalidate()
        Catch
        End Try
    End Sub

    ''' <summary>Compat : ancienne API</summary>
    Public Sub Present(framebuffer() As Integer)
        UpdateFrame(framebuffer, CInt(PceConstants.SCREEN_WIDTH), CInt(PceConstants.SCREEN_HEIGHT))
    End Sub

    ''' <summary>Dessine le bitmap étiré sur le panel. Le verrou n'est tenu que pour
    ''' une copie rapide (snapshot) : la mise à l'échelle coûteuse se fait HORS verrou,
    ''' sinon un gros repaint bloquerait le thread d'émulation (→ famine audio/saccade).</summary>
    Private Sub OnPanelPaint(sender As Object, e As System.Windows.Forms.PaintEventArgs)
        Dim w As Integer, h As Integer
        SyncLock bitmapLock
            If bitmap Is Nothing OrElse curWidth <= 0 OrElse curHeight <= 0 Then Return
            w = curWidth : h = curHeight
            ' Snapshot de la frame courante (copie 1:1 rapide, sous verrou)
            If snapshot Is Nothing OrElse snapshot.Width <> w OrElse snapshot.Height <> h Then
                If snapshot IsNot Nothing Then snapshot.Dispose()
                snapshot = New System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            End If
            Using g = System.Drawing.Graphics.FromImage(snapshot)
                g.DrawImageUnscaled(bitmap, 0, 0)
            End Using
        End SyncLock

        ' --- HORS verrou : le thread d'émulation n'attend plus le paint ---
        Dim pw = panel.ClientSize.Width
        Dim ph = panel.ClientSize.Height
        If pw <= 0 OrElse ph <= 0 Then Return

        ' Aspect d'affichage : 4:3 (comme la TV d'origine), indépendant de la
        ' résolution interne (256/320/344/352… = pixels non carrés). Option
        ' ForceAspect43=False : conserve l'aspect des pixels internes.
        Dim dw, dh As Integer
        If ForceAspect43 Then
            Const targetAspect As Double = 4.0 / 3.0
            If pw / CDbl(ph) > targetAspect Then
                dh = ph : dw = Math.Max(1, CInt(ph * targetAspect))   ' bandes latérales
            Else
                dw = pw : dh = Math.Max(1, CInt(pw / targetAspect))   ' bandes haut/bas
            End If
        Else
            Dim scale = Math.Min(pw / CDbl(w), ph / CDbl(h))
            If scale <= 0 Then Return
            dw = Math.Max(1, CInt(w * scale))
            dh = Math.Max(1, CInt(h * scale))
        End If
        Dim dx = (pw - dw) \ 2
        Dim dy = (ph - dh) \ 2

        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half

        ' Une mise à l'échelle NearestNeighbor à facteur FRACTIONNAIRE laisse tomber
        ' des lignes/colonnes source de façon irrégulière (le texte fin du haut perd
        ' des scanlines). Solution « sharp bilinear » : agrandir d'abord d'un facteur
        ' ENTIER en NearestNeighbor (aucune ligne perdue, pixels nets), puis réduire
        ' vers la taille finale en bilinéaire (doux, sans lignes perdues).
        ' snapshot/scaledBitmap ne sont touchés que par le thread UI → sûrs hors verrou.
        Dim ps = Math.Max(1, Math.Min(4, CInt(Math.Ceiling(Math.Max(dw / CDbl(w), dh / CDbl(h))))))
        If ps > 1 Then
            Dim bw = w * ps, bh = h * ps
            If scaledBitmap Is Nothing OrElse curPs <> ps _
               OrElse scaledBitmap.Width <> bw OrElse scaledBitmap.Height <> bh Then
                If scaledBitmap IsNot Nothing Then scaledBitmap.Dispose()
                scaledBitmap = New System.Drawing.Bitmap(bw, bh,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                curPs = ps
            End If
            Using g = System.Drawing.Graphics.FromImage(scaledBitmap)
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half
                g.DrawImage(snapshot, 0, 0, bw, bh)
            End Using
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear
            e.Graphics.DrawImage(scaledBitmap, dx, dy, dw, dh)
        Else
            ' facteur < 2 : bilinéaire direct (évite les lignes perdues du NN fractionnaire)
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear
            e.Graphics.DrawImage(snapshot, dx, dy, dw, dh)
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock bitmapLock
            If bitmap IsNot Nothing Then bitmap.Dispose()
            bitmap = Nothing
            If scaledBitmap IsNot Nothing Then scaledBitmap.Dispose()
            scaledBitmap = Nothing
            If snapshot IsNot Nothing Then snapshot.Dispose()
            snapshot = Nothing
        End SyncLock
        If panel IsNot Nothing Then
            RemoveHandler panel.Paint, AddressOf OnPanelPaint
        End If
    End Sub

End Class
