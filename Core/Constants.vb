''' <summary>Constantes centralisées pour timing et adressage du PC Engine</summary>
Public Module PceConstants

    ' ===== Timing =====
    ''' <summary>Fréquence CPU principale (Hz)</summary>
    Public Const CPU_CLOCK_FAST As UInteger = 7160000  ' Mode rapide 7.16 MHz
    Public Const CPU_CLOCK_SLOW As UInteger = 1790000  ' Mode lent 1.79 MHz

    ''' <summary>Fréquence pixel clock VCE (Hz)</summary>
    Public Const PIXEL_CLOCK As UInteger = 5369317  ' 5.37 MHz

    ''' <summary>Cycles CPU par ligne de balayage (scanline)</summary>
    Public Const CYCLES_PER_SCANLINE As Integer = 455  ' cycles CPU (7.16 MHz) par scanline

    ''' <summary>Cycles maîtres par scanline VDC (sans CPU)</summary>
    Public Const VDC_CYCLES_PER_SCANLINE As UInteger = 455

    ''' <summary>Nombre total de scanlines par frame</summary>
    Public Const SCANLINES_PER_FRAME As Integer = 263

    ''' <summary>Nombre de scanlines actives (zone d'affichage)</summary>
    Public Const ACTIVE_SCANLINES As Integer = 242

    ''' <summary>Fréquence frame (Hz)</summary>
    Public Const FRAME_RATE As Double = 59.82

    ' ===== Adressage mémoire =====
    ''' <summary>Taille d'une page de mémoire (8 Ko)</summary>
    Public Const PAGE_SIZE As UInteger = &H2000

    ''' <summary>Nombre de pages mappables (MPR0-7)</summary>
    Public Const NUM_PAGES As UInteger = 8

    ''' <summary>Taille de l'espace adressable (128 Ko)</summary>
    Public Const ADDRESS_SPACE As UInteger = &H20000

    ''' <summary>Adresse page zéro relocalisée</summary>
    Public Const ZERO_PAGE_BASE As UInteger = &H2000

    ''' <summary>Adresse pile relocalisée</summary>
    Public Const STACK_BASE As UInteger = &H2100

    ''' <summary>Adresse page I/O matérielle</summary>
    Public Const IO_PAGE_BASE As UInteger = &H1F0000

    ' ===== ROM et RAM =====
    ''' <summary>Taille maximale ROM HuCard (1 Mo)</summary>
    Public Const MAX_ROM_SIZE As UInteger = &H100000

    ''' <summary>Taille RAM de travail (8 Ko)</summary>
    Public Const WORK_RAM_SIZE As UInteger = &H2000

    ''' <summary>Taille BRAM sauvegarde (2 Ko)</summary>
    Public Const BRAM_SIZE As UInteger = &H800

    ''' <summary>Taille VRAM VDC (32 Ko = 16K words)</summary>
    Public Const VRAM_SIZE As UInteger = &H8000

    ' ===== Registres VDC (HuC6270) =====
    Public Const REG_VDC_STATUS As UInteger = 0       ' Statut/sélection registre
    Public Const REG_VDC_DATA_LO As UInteger = 2      ' Données LSB
    Public Const REG_VDC_DATA_HI As UInteger = 3      ' Données MSB

    ' ===== Décalages pour décodage page I/O =====
    ''' <summary>Masque pour extraire adresse I/O (offset dans page $FF)</summary>
    Public Const IO_MASK As UInteger = &H1FFF

    ' ===== Dimensions écran =====
    ''' <summary>Largeur écran max en pixels</summary>
    Public Const SCREEN_WIDTH As Integer = 512

    ''' <summary>Hauteur écran max en pixels</summary>
    Public Const SCREEN_HEIGHT As Integer = 242

    ' ===== Audio =====
    ''' <summary>Fréquence d'échantillonnage (Hz)</summary>
    Public Const AUDIO_SAMPLE_RATE As UInteger = 44100

    ''' <summary>Taille du ring buffer audio (échantillons)</summary>
    Public Const AUDIO_BUFFER_SAMPLES As UInteger = 4410  ' ~100 ms à 44100 Hz

    ' ===== PSG =====
    ''' <summary>Nombre de canaux PSG</summary>
    Public Const PSG_CHANNELS As UInteger = 6

    ''' <summary>Taille waveform RAM par canal</summary>
    Public Const PSG_WAVEFORM_SIZE As UInteger = 32

    ' ===== Sprites VDC =====
    ''' <summary>Nombre d'entrées SAT (Sprite Attribute Table)</summary>
    Public Const SPRITE_COUNT As UInteger = 64

    ''' <summary>Limite sprites par scanline</summary>
    Public Const SPRITES_PER_LINE As UInteger = 16

End Module
