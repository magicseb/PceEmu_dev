Imports System.IO

''' <summary>
''' Arcade Card (carte d'extension CD-ROM²) : 2 Mo de RAM accessibles par 4 « ports »
''' à adressage auto-incrémenté (registres $1A00-$1AFF), plus une fenêtre directe dans
''' les banques CPU $40-$43. Portage fidèle de l'émulation de Mednafen
''' (hw_misc/arcade_card/arcade_card.cpp — d'après les infos de Ki et David Shadoff).
'''
''' Chaque port a une base (24 bits), un offset (16 bits), un incrément (16 bits) et un
''' registre de contrôle (7 bits). Lire/écrire le registre de données ($1Ax0/$1Ax1)
''' accède à RAM[base (+offset)] et auto-incrémente base ou offset selon le contrôle.
''' Un bloc partagé en $1AE0+ fournit un registre à décalage/rotation 32 bits et
''' l'identifiant de la carte ($1AFE = version $10, $1AFF = ID $51) que les jeux lisent
''' pour détecter la présence de l'Arcade Card.
''' </summary>
Public Class ArcadeCard

    Private Class AcPort
        Public Base As Integer          ' 24 bits
        Public Offset As Integer        ' 16 bits
        Public Increment As Integer     ' 16 bits
        Public Control As Integer       ' 7 bits
    End Class

    Private ReadOnly ports As AcPort() = {New AcPort(), New AcPort(), New AcPort(), New AcPort()}
    Private shiftLatch As UInteger      ' 32 bits
    Private shiftBits As Integer        ' 4 bits
    Private rotateBits As Integer       ' 4 bits

    Private ReadOnly ram(&H1FFFFF) As Byte   ' 2 Mo
    Private ramUsed As Boolean

    ''' <summary>Auto-incrément après un accès aux données, si le contrôle le demande.</summary>
    Private Shared Sub AutoIncrement(p As AcPort)
        If (p.Control And &H1) <> 0 Then
            If (p.Control And &H10) <> 0 Then
                p.Base = (p.Base + p.Increment) And &HFFFFFF
            Else
                p.Offset = (p.Offset + p.Increment) And &HFFFF
            End If
        End If
    End Sub

    ''' <summary>Adresse RAM effective d'un port (base, éventuellement + offset).</summary>
    Private Shared Function EffectiveAddr(p As AcPort) As Integer
        Dim aci = p.Base
        If (p.Control And &H2) <> 0 Then
            aci += p.Offset
            If (p.Control And &H8) <> 0 Then aci += &HFF0000
        End If
        Return aci And &H1FFFFF
    End Function

    ''' <summary>Ajoute l'offset à la base (déclenché par certaines écritures selon le contrôle).</summary>
    Private Shared Sub AddOffsetToBase(p As AcPort)
        If (p.Control And &H8) <> 0 Then p.Base += &HFF0000
        p.Base = (p.Base + p.Offset) And &HFFFFFF
    End Sub

    ''' <summary>Lecture d'un registre Arcade Card. A = adresse dans la page d'E/S ($1A00-$1AFF).</summary>
    Public Function Read(a As Integer, Optional peek As Boolean = False) As Integer
        If (a And &H1F00) <> &H1A00 Then Return &HFF

        If a < &H1A80 Then
            Dim p = ports((a >> 4) And &H3)
            Select Case a And &HF
                Case &H0, &H1
                    Dim ret = CInt(ram(EffectiveAddr(p)))
                    If Not peek Then AutoIncrement(p)
                    Return ret
                Case &H2 : Return (p.Base >> 0) And &HFF
                Case &H3 : Return (p.Base >> 8) And &HFF
                Case &H4 : Return (p.Base >> 16) And &HFF
                Case &H5 : Return (p.Offset >> 0) And &HFF
                Case &H6 : Return (p.Offset >> 8) And &HFF
                Case &H7 : Return (p.Increment >> 0) And &HFF
                Case &H8 : Return (p.Increment >> 8) And &HFF
                Case &H9 : Return p.Control
                Case Else : Return &HFF
            End Select
        ElseIf a >= &H1AE0 Then
            Select Case a And &H1F
                Case &H0, &H1, &H2, &H3 : Return CInt((shiftLatch >> ((a And 3) * 8)) And &HFFUI)
                Case &H4 : Return shiftBits
                Case &H5 : Return rotateBits
                Case &H1C : Return &H0
                Case &H1D : Return &H0
                Case &H1E : Return &H10          ' numéro de version
                Case &H1F : Return &H51          ' identifiant Arcade Card
                Case Else : Return &HFF
            End Select
        End If
        Return &HFF
    End Function

    ''' <summary>Écriture d'un registre Arcade Card.</summary>
    Public Sub Write(a As Integer, v As Integer)
        If (a And &H1F00) <> &H1A00 Then Return
        v = v And &HFF

        If a < &H1A80 Then
            Dim p = ports((a >> 4) And &H3)
            Select Case a And &HF
                Case &H0, &H1
                    ramUsed = True
                    ram(EffectiveAddr(p)) = CByte(v)
                    AutoIncrement(p)
                Case &H2 : p.Base = (p.Base And Not &HFF) Or (v << 0)
                Case &H3 : p.Base = (p.Base And Not &HFF00) Or (v << 8)
                Case &H4 : p.Base = (p.Base And Not &HFF0000) Or (v << 16)
                Case &H5
                    p.Offset = (p.Offset And Not &HFF) Or (v << 0)
                    If (p.Control And &H60) = &H20 Then AddOffsetToBase(p)
                Case &H6
                    p.Offset = (p.Offset And Not &HFF00) Or (v << 8)
                    If (p.Control And &H60) = &H40 Then AddOffsetToBase(p)
                Case &H7 : p.Increment = (p.Increment And Not &HFF) Or (v << 0)
                Case &H8 : p.Increment = (p.Increment And Not &HFF00) Or (v << 8)
                Case &H9 : p.Control = v And &H7F
                Case &HA
                    If (p.Control And &H60) = &H60 Then AddOffsetToBase(p)
            End Select
        ElseIf a >= &H1AE0 Then
            Select Case a And &H1F
                Case &H0, &H1, &H2, &H3
                    Dim sh = (a And 3) * 8
                    shiftLatch = (shiftLatch And Not (CUInt(&HFF) << sh)) Or (CUInt(v) << sh)
                Case &H4
                    shiftBits = v And &HF
                    If shiftBits <> 0 Then
                        If (shiftBits And &H8) <> 0 Then
                            shiftLatch >>= (16 - shiftBits)
                        Else
                            shiftLatch <<= shiftBits
                        End If
                    End If
                Case &H5
                    rotateBits = v And &HF
                    If rotateBits <> 0 Then
                        If (rotateBits And &H8) <> 0 Then
                            Dim sa = 16 - rotateBits
                            Dim orv = shiftLatch << (32 - sa)
                            shiftLatch = (shiftLatch >> sa) Or orv
                        Else
                            Dim sa = rotateBits
                            Dim orv = (shiftLatch >> (32 - sa)) And ((1UI << sa) - 1UI)
                            shiftLatch = (shiftLatch << sa) Or orv
                        End If
                    End If
            End Select
        End If
    End Sub

    ''' <summary>Accès direct via les banques CPU $40-$43 : chaque banque vise le registre de
    ''' données ($1Ax0) d'un des quatre ports. A = adresse physique 21 bits.</summary>
    Public Function PhysRead(a As Integer, Optional peek As Boolean = False) As Integer
        Return Read(&H1A00 Or ((a >> 9) And &H30), peek)
    End Function
    Public Sub PhysWrite(a As Integer, v As Integer)
        Write(&H1A00 Or ((a >> 9) And &H30), v)
    End Sub

    Public Sub Reset()
        For Each p In ports
            p.Base = 0 : p.Offset = 0 : p.Increment = 0 : p.Control = 0
        Next
        shiftLatch = 0 : shiftBits = 0 : rotateBits = 0
        Array.Clear(ram, 0, ram.Length)
        ramUsed = False
    End Sub

    ''' <summary>Sérialise l'état. La RAM 2 Mo n'est écrite que si elle a été utilisée
    ''' (drapeau ramUsed) — un jeu CD sans Arcade Card ne gonfle donc pas la sauvegarde.</summary>
    Public Sub SaveState(w As BinaryWriter)
        For Each p In ports
            w.Write(p.Base) : w.Write(p.Offset) : w.Write(p.Increment) : w.Write(p.Control)
        Next
        w.Write(shiftLatch) : w.Write(shiftBits) : w.Write(rotateBits)
        w.Write(ramUsed)
        If ramUsed Then w.Write(ram, 0, ram.Length)
    End Sub

    Public Sub LoadState(r As BinaryReader)
        For Each p In ports
            p.Base = r.ReadInt32() : p.Offset = r.ReadInt32()
            p.Increment = r.ReadInt32() : p.Control = r.ReadInt32()
        Next
        shiftLatch = r.ReadUInt32() : shiftBits = r.ReadInt32() : rotateBits = r.ReadInt32()
        ramUsed = r.ReadBoolean()
        If ramUsed Then
            Dim data = r.ReadBytes(ram.Length)
            Array.Copy(data, ram, Math.Min(data.Length, ram.Length))
        Else
            Array.Clear(ram, 0, ram.Length)
        End If
    End Sub

End Class
