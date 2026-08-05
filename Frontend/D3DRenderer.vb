Imports System.Runtime.InteropServices
Imports Vortice.Direct3D
Imports Vortice.Direct3D11
Imports Vortice.DXGI
Imports Vortice.D3DCompiler
Imports Vortice.Mathematics

''' <summary>Shaders sélectionnables pour l'affichage.</summary>
Public Enum PceShader
    SharpPixels = 0   ' pixels nets (échantillonnage point)
    SmoothPixels = 1  ' pixels lisses (bilinéaire)
    Scanlines = 2     ' lignes de balayage
    Crt = 3           ' CRT (scanlines + masque d'ouverture)
End Enum

''' <summary>Surface commune aux deux renderers (D3D11 et repli GDI+).</summary>
Public Interface IEmuRenderer
    Inherits IDisposable
    Sub UpdateFrame(framebuffer() As Integer, displayWidth As Integer, displayHeight As Integer)
    Property ForceAspect43 As Boolean
    Property Shader As PceShader
End Interface

''' <summary>Rendu réel Direct3D 11 avec shaders HLSL sélectionnables.
''' Le letterbox 4:3 est fait dans le pixel shader (barres noires) : pas de clear.
''' Tout l'accès au contexte D3D est sérialisé (thread d'émulation + resize UI).</summary>
Public Class D3DRenderer
    Implements IEmuRenderer

    Private ReadOnly panel As System.Windows.Forms.Panel
    Private device As ID3D11Device
    Private context As ID3D11DeviceContext
    Private swapChain As IDXGISwapChain1
    Private rtv As ID3D11RenderTargetView
    Private frameTex As ID3D11Texture2D
    Private frameSrv As ID3D11ShaderResourceView
    Private samplerPoint As ID3D11SamplerState
    Private samplerLinear As ID3D11SamplerState
    Private vs As ID3D11VertexShader
    Private psPass As ID3D11PixelShader
    Private psScan As ID3D11PixelShader
    Private psCrt As ID3D11PixelShader
    Private cbuf As ID3D11Buffer

    Private texW As Integer = 0, texH As Integer = 0
    Private bbW As Integer = 0, bbH As Integer = 0
    Private needResize As Boolean = False
    Private pendW As Integer, pendH As Integer
    Private disposed As Boolean = False
    Private ReadOnly lockObj As New Object()

    Public Property ForceAspect43 As Boolean = True Implements IEmuRenderer.ForceAspect43
    Public Property Shader As PceShader = PceShader.SharpPixels Implements IEmuRenderer.Shader

    <StructLayout(LayoutKind.Sequential)>
    Private Structure CBData
        Public rectMinX, rectMinY, rectMaxX, rectMaxY As Single
        Public srcW, srcH As Single
        Public scanIntensity, pad As Single
    End Structure

    Public Sub New(panelRef As System.Windows.Forms.Panel)
        panel = panelRef
        Dim w = Math.Max(16, panel.ClientSize.Width)
        Dim h = Math.Max(16, panel.ClientSize.Height)

        Dim fl() As FeatureLevel = {FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0}
        D3D11.D3D11CreateDevice(CType(Nothing, IDXGIAdapter), DriverType.Hardware,
            DeviceCreationFlags.BgraSupport, fl, device, context)

        Dim dxgiDev = device.QueryInterface(Of IDXGIDevice)()
        Dim adapter = dxgiDev.GetAdapter()
        Dim factory = adapter.GetParent(Of IDXGIFactory2)()
        Dim scd As New SwapChainDescription1 With {
            .Width = w, .Height = h, .Format = Format.B8G8R8A8_UNorm,
            .BufferCount = 2, .BufferUsage = Usage.RenderTargetOutput,
            .SwapEffect = SwapEffect.FlipDiscard, .SampleDescription = New SampleDescription(1, 0),
            .Scaling = Scaling.Stretch, .AlphaMode = AlphaMode.Ignore}
        swapChain = factory.CreateSwapChainForHwnd(device, panel.Handle, scd)
        dxgiDev.Dispose() : adapter.Dispose() : factory.Dispose()
        bbW = w : bbH = h

        CreateRtv()
        CompileShaders()
        CreateSamplers()
        CreateCb()
        AddHandler panel.Resize, AddressOf OnResize
    End Sub

    Private Sub CreateRtv()
        Dim backbuf = swapChain.GetBuffer(Of ID3D11Texture2D)(0)
        rtv = device.CreateRenderTargetView(backbuf)
        backbuf.Dispose()
    End Sub

    Private Shared ReadOnly HlslSource As String = <s><![CDATA[
Texture2D tex : register(t0);
SamplerState smp : register(s0);
cbuffer P : register(b0) {
    float2 rectMin;
    float2 rectMax;
    float2 srcSize;
    float  scanIntensity;
    float  pad;
};
struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
VSOut VSMain(uint id : SV_VertexID) {
    VSOut o;
    o.uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}
float2 Remap(float2 uv, out bool inside) {
    float2 t = (uv - rectMin) / (rectMax - rectMin);
    inside = t.x >= 0.0 && t.x <= 1.0 && t.y >= 0.0 && t.y <= 1.0;
    return t;
}
float4 PSPass(VSOut i) : SV_Target {
    bool ins; float2 t = Remap(i.uv, ins);
    if (!ins) return float4(0, 0, 0, 1);
    return float4(tex.Sample(smp, t).rgb, 1);
}
float4 PSScan(VSOut i) : SV_Target {
    bool ins; float2 t = Remap(i.uv, ins);
    if (!ins) return float4(0, 0, 0, 1);
    float3 c = tex.Sample(smp, t).rgb;
    float ph = frac(t.y * srcSize.y);
    float s = sin(ph * 3.14159265);
    float mult = lerp(1.0 - scanIntensity, 1.0, s * s);
    return float4(c * mult, 1);
}
float4 PSCrt(VSOut i) : SV_Target {
    bool ins; float2 t = Remap(i.uv, ins);
    if (!ins) return float4(0, 0, 0, 1);
    float3 c = tex.Sample(smp, t).rgb;
    float ph = frac(t.y * srcSize.y);
    float s = sin(ph * 3.14159265);
    float mult = lerp(1.0 - scanIntensity, 1.0, s * s);
    float m = frac(i.pos.x / 3.0);
    float3 mask = float3(0.8, 0.8, 0.8);
    if (m < 0.3333) mask.r = 1.2;
    else if (m < 0.6666) mask.g = 1.2;
    else mask.b = 1.2;
    return float4(c * mult * mask, 1);
}
]]></s>.Value

    Private Sub CompileShaders()
        Dim vsBlob = Compiler.Compile(HlslSource, "VSMain", "pce.hlsl", "vs_4_0")
        vs = device.CreateVertexShader(vsBlob.Span)
        Dim pBlob = Compiler.Compile(HlslSource, "PSPass", "pce.hlsl", "ps_4_0")
        psPass = device.CreatePixelShader(pBlob.Span)
        Dim sBlob = Compiler.Compile(HlslSource, "PSScan", "pce.hlsl", "ps_4_0")
        psScan = device.CreatePixelShader(sBlob.Span)
        Dim cBlob = Compiler.Compile(HlslSource, "PSCrt", "pce.hlsl", "ps_4_0")
        psCrt = device.CreatePixelShader(cBlob.Span)
    End Sub

    Private Sub CreateSamplers()
        Dim sp As New SamplerDescription With {
            .Filter = Filter.MinMagMipPoint, .AddressU = TextureAddressMode.Clamp,
            .AddressV = TextureAddressMode.Clamp, .AddressW = TextureAddressMode.Clamp}
        samplerPoint = device.CreateSamplerState(sp)
        Dim sl As New SamplerDescription With {
            .Filter = Filter.MinMagMipLinear, .AddressU = TextureAddressMode.Clamp,
            .AddressV = TextureAddressMode.Clamp, .AddressW = TextureAddressMode.Clamp}
        samplerLinear = device.CreateSamplerState(sl)
    End Sub

    Private Sub CreateCb()
        Dim cd As New BufferDescription With {
            .ByteWidth = Marshal.SizeOf(Of CBData)(), .Usage = ResourceUsage.Dynamic,
            .BindFlags = BindFlags.ConstantBuffer, .CPUAccessFlags = CpuAccessFlags.Write}
        cbuf = device.CreateBuffer(cd)
    End Sub

    Private Sub OnResize(sender As Object, e As EventArgs)
        SyncLock lockObj
            pendW = Math.Max(16, panel.ClientSize.Width)
            pendH = Math.Max(16, panel.ClientSize.Height)
            needResize = True
        End SyncLock
    End Sub

    Private Sub DoResize()
        If rtv IsNot Nothing Then rtv.Dispose() : rtv = Nothing
        swapChain.ResizeBuffers(2, pendW, pendH, Format.B8G8R8A8_UNorm, SwapChainFlags.None)
        bbW = pendW : bbH = pendH
        CreateRtv()
        needResize = False
    End Sub

    Public Sub UpdateFrame(framebuffer() As Integer, displayWidth As Integer, displayHeight As Integer) Implements IEmuRenderer.UpdateFrame
        If framebuffer Is Nothing Then Return
        Dim dw = If(displayWidth < 8, 256, displayWidth)
        Dim dh = If(displayHeight < 8, 224, displayHeight)
        SyncLock lockObj
            If disposed Then Return
            If needResize Then DoResize()
            If frameTex Is Nothing OrElse texW <> dw OrElse texH <> dh Then
                If frameSrv IsNot Nothing Then frameSrv.Dispose() : frameSrv = Nothing
                If frameTex IsNot Nothing Then frameTex.Dispose() : frameTex = Nothing
                Dim td As New Texture2DDescription With {
                    .Width = dw, .Height = dh, .MipLevels = 1, .ArraySize = 1,
                    .Format = Format.B8G8R8A8_UNorm, .SampleDescription = New SampleDescription(1, 0),
                    .Usage = ResourceUsage.Dynamic, .BindFlags = BindFlags.ShaderResource,
                    .CPUAccessFlags = CpuAccessFlags.Write}
                frameTex = device.CreateTexture2D(td)
                frameSrv = device.CreateShaderResourceView(frameTex)
                texW = dw : texH = dh
            End If
            ' upload de la frame : le framebuffer a un stride FIXE de SCREEN_WIDTH,
            ' on n'en copie que displayWidth pixels par ligne (rowpitch destination aligné)
            Dim srcStride = CInt(PceConstants.SCREEN_WIDTH)
            Dim ms = context.Map(frameTex, 0, MapMode.WriteDiscard)
            For y = 0 To dh - 1
                Marshal.Copy(framebuffer, y * srcStride, IntPtr.Add(ms.DataPointer, y * ms.RowPitch), dw)
            Next
            context.Unmap(frameTex, 0)
            RenderInternal(dw, dh)
        End SyncLock
    End Sub

    Private Sub RenderInternal(dw As Integer, dh As Integer)
        ' rectangle image (en UV du backbuffer) : 4:3 letterbox/pillarbox
        Dim rminx = 0.0F, rminy = 0.0F, rmaxx = 1.0F, rmaxy = 1.0F
        Dim imgAspect As Double = If(ForceAspect43, 4.0 / 3.0, dw / CDbl(dh))
        Dim bbAspect As Double = bbW / CDbl(Math.Max(1, bbH))
        If bbAspect > imgAspect Then
            Dim fr = CSng(imgAspect / bbAspect)
            rminx = (1.0F - fr) / 2.0F : rmaxx = 1.0F - (1.0F - fr) / 2.0F
        Else
            Dim fr = CSng(bbAspect / imgAspect)
            rminy = (1.0F - fr) / 2.0F : rmaxy = 1.0F - (1.0F - fr) / 2.0F
        End If

        Dim cd As New CBData With {
            .rectMinX = rminx, .rectMinY = rminy, .rectMaxX = rmaxx, .rectMaxY = rmaxy,
            .srcW = dw, .srcH = dh, .scanIntensity = 0.35F}
        Dim cms = context.Map(cbuf, MapMode.WriteDiscard)
        Marshal.StructureToPtr(cd, cms.DataPointer, False)
        context.Unmap(cbuf)

        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList)
        context.VSSetShader(vs)
        Dim ps = psPass, smp = samplerPoint
        Select Case Shader
            Case PceShader.SmoothPixels : ps = psPass : smp = samplerLinear
            Case PceShader.Scanlines : ps = psScan : smp = samplerLinear
            Case PceShader.Crt : ps = psCrt : smp = samplerLinear
            Case Else : ps = psPass : smp = samplerPoint
        End Select
        context.PSSetShader(ps)
        context.PSSetShaderResource(0, frameSrv)
        context.PSSetSampler(0, smp)
        context.PSSetConstantBuffer(0, cbuf)
        context.RSSetViewports(1, New Viewport() {New Viewport(0.0F, 0.0F, CSng(bbW), CSng(bbH))})
        context.OMSetRenderTargets(New ID3D11RenderTargetView() {rtv})
        context.Draw(3, 0)
        swapChain.Present(1, PresentFlags.None)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock lockObj
            If disposed Then Return
            disposed = True
            Try : RemoveHandler panel.Resize, AddressOf OnResize : Catch : End Try
            For Each d As IDisposable In New IDisposable() {frameSrv, frameTex, samplerPoint, samplerLinear,
                    vs, psPass, psScan, psCrt, cbuf, rtv, swapChain, context, device}
                Try : If d IsNot Nothing Then d.Dispose()
                Catch : End Try
            Next
        End SyncLock
    End Sub
End Class
