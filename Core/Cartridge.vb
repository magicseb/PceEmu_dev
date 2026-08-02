''' <summary>
''' Classe de base des cartouches HuCard.
''' C'est la cartouche qui traduit une page logique (MPR) en adresse ROM :
''' un mapper n'a ainsi rien à changer dans la MMU.
''' </summary>
Public MustInherit Class Cartridge

    Public RomData() As Byte

    ''' <summary>Masque de miroir, arrondi à la puissance de 2 supérieure à la taille de la ROM.</summary>
    Protected RomMask As Integer

    ''' <summary>Nom affichable de la cartouche, sans chemin ni extension.</summary>
    Public ReadOnly Property Title As String

    Public Sub New(romPath As String)
        Me.New(System.IO.Path.GetFileNameWithoutExtension(romPath), System.IO.File.ReadAllBytes(romPath))
    End Sub

    ''' <summary>
    ''' Charge une ROM déjà en mémoire : c'est ce qui permet de lire une archive
    ''' sans jamais écrire son contenu sur le disque.
    ''' </summary>
    Public Sub New(name As String, data() As Byte)
        _Title = name
        ApplyRomData(data)

        RomMask = 1
        While RomMask < RomData.Length
            RomMask <<= 1
        End While
        RomMask -= 1

        ' Mapping « coupé » propre aux cartouches de 384 Ko (3 Mbit)
        Is384 = (RomData.Length = &H60000)
    End Sub

    ''' <summary>Retient le contenu utile de la ROM, en-tête éventuel retiré.</summary>
    Protected Sub ApplyRomData(data() As Byte)
        ' En-tête de 512 octets à ignorer si la taille n'est pas un multiple de 8 Ko
        If (data.Length Mod 8192) = 512 Then
            RomData = New Byte(data.Length - 513) {}
            System.Array.Copy(data, 512, RomData, 0, data.Length - 512)
        Else
            RomData = data
        End If
    End Sub

    ''' <summary>Vrai pour une HuCard de 384 Ko (3 Mbit), au mapping « coupé » particulier.</summary>
    Protected Is384 As Boolean

    ''' <summary>Lit un octet dans la zone ROM (pages $00-$7F).</summary>
    Public Overridable Function ReadRom(page As Integer, offset As Integer) As Integer
        Dim addr As Integer

        If Is384 Then
            ' HuCard 3 Mbit : les sources se coupent en deux blocs distincts.
            '  · pages $00-$3F → les 256 premiers Ko (2 Mbit), en miroir tous les 32 banques
            '  · pages $40-$7F → les 128 derniers Ko (1 Mbit), en miroir tous les 16 banques
            ' C'est par les banques $40+ que le jeu atteint son second bloc de code —
            ' un simple miroir « puissance de deux » l'y renverrait au début de la ROM.
            If (page And &H40) = 0 Then
                addr = ((page And &H1F) << 13) Or offset
            Else
                addr = &H40000 + (((page And &HF) << 13) Or offset)
            End If
            Return RomData(addr)
        End If

        addr = ((page << 13) Or offset) And RomMask
        If addr < RomData.Length Then Return RomData(addr)
        Return &HFF
    End Function

    ''' <summary>Écriture dans la zone ROM : sans effet, sauf pour les cartouches à mapper.</summary>
    Public Overridable Sub WriteRom(page As Integer, offset As Integer, value As Integer)
    End Sub

    Public MustOverride Function GetMapper() As String

    ''' <summary>Écrit l'état du mapper : rien à retenir pour une cartouche ordinaire.</summary>
    Public Overridable Sub SaveState(w As System.IO.BinaryWriter)
    End Sub

    ''' <summary>Restaure l'état du mapper.</summary>
    Public Overridable Sub LoadState(r As System.IO.BinaryReader)
    End Sub

    ''' <summary>Empreinte de la ROM, pour refuser une sauvegarde faite avec un autre jeu.</summary>
    Public Function Signature() As Integer
        ' Accumulateur en 64 bits : le masquage seul ne suffirait pas à éviter
        ' un débordement quand les vérifications arithmétiques sont actives
        Dim sig As Long = RomData.Length
        Dim stride = Math.Max(1, RomData.Length \ 4096)
        Dim i = 0
        While i < RomData.Length
            sig = ((sig * 31) + RomData(i)) And &H7FFFFFFFL
            i += stride
        End While
        Return CInt(sig)
    End Function

End Class

''' <summary>Cartouche standard : ROM linéaire avec miroirs.</summary>
Public Class CartridgeStandard
    Inherits Cartridge

    Public Sub New(romPath As String)
        MyBase.New(romPath)
    End Sub

    Public Sub New(name As String, data() As Byte)
        MyBase.New(name, data)
    End Sub

    Public Overrides Function GetMapper() As String
        Return "Standard"
    End Function

End Class

''' <summary>
''' Cartouche Street Fighter II' Champion Edition (2,5 Mo).
'''
''' Les 512 premiers kilooctets sont câblés sur les pages $00-$3F. Les 2 Mo restants
''' forment quatre banques de 512 Ko dont une seule est visible à la fois, sur les
''' pages $40-$7F. C'est l'ADRESSE écrite ($1FF0 à $1FF3) qui choisit la banque :
''' la valeur écrite n'a aucune importance.
''' </summary>
Public Class CartridgeSF2
    Inherits Cartridge

    Private Const FIXED_SIZE As Integer = &H80000   ' 512 Ko toujours visibles
    Private Const BANK_SIZE As Integer = &H80000    ' 512 Ko par banque commutable
    Private Const FIRST_BANKED_PAGE As Integer = &H40

    Private bank As Integer = 0

    ''' <summary>Compteur de diagnostic (sans effet sur l'émulation).</summary>
    Public Shared DbgBankSwitches As Long = 0

    Public Sub New(romPath As String)
        MyBase.New(romPath)
    End Sub

    Public Sub New(name As String, data() As Byte)
        MyBase.New(name, data)
    End Sub

    Public Overrides Function GetMapper() As String
        Return "SF2"
    End Function

    ''' <summary>Banque haute actuellement sélectionnée (0 à 3).</summary>
    Public ReadOnly Property CurrentBank As Integer
        Get
            Return bank
        End Get
    End Property

    Public Overrides Function ReadRom(page As Integer, offset As Integer) As Integer
        Dim addr As Integer
        If page < FIRST_BANKED_PAGE Then
            addr = (page << 13) Or offset
        Else
            addr = FIXED_SIZE + bank * BANK_SIZE + (((page - FIRST_BANKED_PAGE) << 13) Or offset)
        End If

        If addr < RomData.Length Then Return RomData(addr)
        Return &HFF
    End Function

    Public Overrides Sub SaveState(w As System.IO.BinaryWriter)
        w.Write(bank)
    End Sub

    Public Overrides Sub LoadState(r As System.IO.BinaryReader)
        bank = r.ReadInt32() And 3
    End Sub

    Public Overrides Sub WriteRom(page As Integer, offset As Integer, value As Integer)
        ' Seules les adresses $1FF0-$1FF3 pilotent le mapper
        If (offset And &H1FFC) = &H1FF0 Then
            Dim newBank = offset And 3
            If newBank <> bank Then DbgBankSwitches += 1
            bank = newBank
        End If
    End Sub

End Class

''' <summary>Fabrique de cartouches : choisit le mapper d'après la taille utile de la ROM.</summary>
Public Class CartridgeLoader

    Public Shared Function LoadCartridge(romPath As String) As Cartridge
        Return LoadCartridge(System.IO.Path.GetFileNameWithoutExtension(romPath),
                             System.IO.File.ReadAllBytes(romPath))
    End Function

    ''' <summary>Choisit le mapper d'après la taille utile d'une ROM en mémoire.</summary>
    Public Shared Function LoadCartridge(name As String, data() As Byte) As Cartridge
        Dim length = data.Length

        ' Taille utile : l'éventuel en-tête de 512 octets ne compte pas
        If (length Mod 8192) = 512 Then length -= 512

        ' Street Fighter II' Champion Edition est la seule HuCard de 2,5 Mo
        If length = &H280000 Then Return New CartridgeSF2(name, data)
        Return New CartridgeStandard(name, data)
    End Function

End Class
