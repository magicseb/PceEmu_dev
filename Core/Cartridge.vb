''' <summary>Classe de base pour cartridges</summary>
Public MustInherit Class Cartridge
    Public RomData() As Byte
    
    Public Sub New(romPath As String)
        LoadROM(romPath)
    End Sub
    
    Protected Sub LoadROM(romPath As String)
        Dim data = System.IO.File.ReadAllBytes(romPath)
        
        ' Détection en-tête (512 octets à ignorer si taille Mod 8192 = 512)
        If (data.Length Mod 8192) = 512 Then
            RomData = New Byte(data.Length - 513) {}
            System.Array.Copy(data, 512, RomData, 0, data.Length - 512)
        Else
            RomData = data
        End If
    End Sub
    
    Public MustOverride Function GetMapper() As String
End Class

''' <summary>Cartridge standard (ROM linéaire)</summary>
Public Class CartridgeStandard
    Inherits Cartridge
    
    Public Sub New(romPath As String)
        MyBase.New(romPath)
    End Sub
    
    Public Overrides Function GetMapper() As String
        Return "Standard"
    End Function
End Class

''' <summary>Cartridge avec mapper SF2 (Street Fighter II') - 2.5 Mo ROM</summary>
Public Class CartridgeSF2
    Inherits Cartridge
    
    Private bankHigh As Byte = 0  ' Banque courante pour zone haute
    Private Const BANK_SIZE = &H20000  ' 128 Ko par banque
    Private Const BANKS_COUNT = &H14   ' 20 banques
    
    Public Sub New(romPath As String)
        MyBase.New(romPath)
    End Sub
    
    Public Overrides Function GetMapper() As String
        Return "SF2"
    End Function
    
    ''' <summary>Définit la banque ROM haute via registres $1FF0-$1FF3</summary>
    Public Sub SetBankRegister(register As Integer, value As Integer)
        Select Case register
            Case &H1FF0, &H1FF1, &H1FF2, &H1FF3
                ' Ces registres sélectionnent la banque haute
                ' Implémentation simplifiée : on stocke juste la valeur
                bankHigh = value And &H1F  ' 5 bits → 32 banques max
        End Select
    End Sub
    
    ''' <summary>Mappe la ROM avec prise en compte de la banque</summary>
    Public Function GetMappedByte(logicalAddr As UShort) As Byte
        ' Implémentation simplifiée : accès linéaire pour maintenant
        If logicalAddr < RomData.Length Then
            Return RomData(logicalAddr)
        End If
        Return 0
    End Function
End Class

''' <summary>Utilitaire de création de cartridge depuis un fichier ROM</summary>
Public Class CartridgeLoader
    
    ''' <summary>Charge une cartridge et retourne l'instance appropriée</summary>
    Public Shared Function LoadCartridge(romPath As String) As Cartridge
        Dim fileInfo = New System.IO.FileInfo(romPath)
        
        ' Détection mapper par taille
        Select Case fileInfo.Length
            ' SF2' : 2.5 Mo
            Case &H280000
                Return New CartridgeSF2(romPath)
            ' Par défaut : cartridge standard
            Case Else
                Return New CartridgeStandard(romPath)
        End Select
    End Function
    
End Class
