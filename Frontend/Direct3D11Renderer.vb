''' <summary>Rendu GDI+ : bitmap persistant + Paint event + double-buffering</summary>
Public Class Direct3D11Renderer
    Implements IDisposable

    Private bitmap As System.Drawing.Bitmap
    Private bitmapLock As New Object()
    Private panel As System.Windows.Forms.Panel
    Private curWidth As Integer = 0
    Private curHeight As Integer = 0

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

    ''' <summary>Dessine le bitmap étiré sur le panel</summary>
    Private Sub OnPanelPaint(sender As Object, e As System.Windows.Forms.PaintEventArgs)
        SyncLock bitmapLock
            If bitmap Is Nothing Then Return

            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half

            ' Conserver le ratio d'aspect
            Dim pw = panel.ClientSize.Width
            Dim ph = panel.ClientSize.Height
            If pw <= 0 OrElse ph <= 0 Then Return

            Dim scale = Math.Min(pw / CDbl(curWidth), ph / CDbl(curHeight))
            Dim dw = CInt(curWidth * scale)
            Dim dh = CInt(curHeight * scale)
            Dim dx = (pw - dw) \ 2
            Dim dy = (ph - dh) \ 2

            e.Graphics.DrawImage(bitmap, dx, dy, dw, dh)
        End SyncLock
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock bitmapLock
            If bitmap IsNot Nothing Then bitmap.Dispose()
            bitmap = Nothing
        End SyncLock
        If panel IsNot Nothing Then
            RemoveHandler panel.Paint, AddressOf OnPanelPaint
        End If
    End Sub

End Class
