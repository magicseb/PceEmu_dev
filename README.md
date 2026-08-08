# PceEmu - Émulateur PC Engine / TurboGrafx-16

Émulateur PC Engine (TurboGrafx-16) en VB.NET (.NET 8) : CPU HuC6280 complet, VDC/VCE, PSG 6 canaux avec DDA, support CD-ROM² / Super CD-ROM² et SuperGrafx, affichage Direct3D 11 avec shaders sélectionnables (repli GDI+).

**Validé sur ROMs réelles** : Reflectron (Aetherbyte), Bonk 3 - Bonk's Big Adventure, Andre Panza Kick Boxing, Batman (HuCard 3 Mbit) ; jeux CD-ROM² comme Addams Family et Baby Jo (musique CD-DA et bruitages ADPCM) — boot, affichage, musique et samples fonctionnels.

## Fonctionnalités

- **CPU HuC6280** : table d'opcodes complète et conforme au hardware (TAM=$53, TMA=$43, CSL=$54, CSH=$D4, ST0/1/2, transferts de blocs TII/TDD/TIN/TIA/TAI, BBRi/BBSi, RMBi/SMBi, flag T, mode BCD) ; vecteurs corrects (RESET=$FFFE, IRQ1=$FFF8, TIMER=$FFFA, IRQ2/BRK=$FFF6) ; IRQ level-triggered avec masque $1402/$1403
- **VDC (HuC6270)** : VRAM 32K words, rendu tilemap + sprites par scanline avec priorités et flips, limite 16 sprites/ligne, collision du sprite 0 avec IRQ, DMA VRAM-VRAM et SATB, IRQ RCR/VBlank, compteur de scroll vertical latché (splits raster/parallaxe corrects), résolution d'affichage dynamique (256/320/512 de large)
- **VCE (HuC6260)** : palette 512 couleurs 9 bits (G3R3B3) avec cache ARGB
- **PSG 6 canaux** : waveform 32×5 bits, **volumes logarithmiques conformes au hardware** (1,5 dB/pas volume, 3 dB/pas balance), **DDA timestampé au cycle CPU** (voix et effets de coups restitués), bruit LFSR, **LFO** (le canal 1 module la période du canal 0 en ×1/×16/×256), période 0 = 4096, anti-aliasing au-delà de Nyquist, **sortie stéréo** (balance de canal $0805 et balance générale $0801 appliquées voie par voie)
- **Timer** : prescaler /1024 cycles, IRQ TIMER avec acquittement $1403
- **Joypad** : nibbles actifs bas via SEL/CLR
- **Mapper SF2** : Street Fighter II' Champion Edition (2,5 Mo) — 512 Ko fixes plus quatre banques de 512 Ko commutées par l'adresse écrite en $1FF0-$1FF3
- **CD-ROM² / Super CD-ROM²** : boot et exécution des jeux CD (System Card / Super System Card), interface SCSI $1800-$180F (handshake REQ/ACK), images `.cue/.ccd/.img` (un fichier multi-pistes ou un `.bin` par piste) **et `.chd`** (format compressé de MAME, lecteur CHD géré — sans dépendance native, avec musique CD-DA décodée : zlib, LZMA et FLAC), 256 Ko de RAM CD, IRQ2 ; **musique CD-DA** (adressage par LBA / MSF / numéro de piste, boucle) et **bruitages ADPCM** (décodage OKI, adresses de lecture par sample) restitués
- **SuperGrafx** : deux VDC + VPC (Vpc.vb), fenêtres de priorité et mélange des deux plans ; les cinq jeux SuperGrafx démarrent et animent
- **Affichage Direct3D 11** : vrai pipeline GPU (Vortice.Windows) avec **shaders sélectionnables** (pixels nets, pixels lisses, scanlines, CRT), affichage en **4:3** de la console d'origine (letterbox fait dans le shader), **mode plein écran** (F11) ; **repli automatique sur GDI+** si Direct3D est indisponible
- **Sauvegarde d'état** (F5/F8), y compris pour les jeux CD (RAM CD étendue + état complet du lecteur sérialisés) ; l'empreinte de la ROM est stockée pour éviter de charger l'état d'un autre jeu
- **Manette Xbox** (XInput, sans dépendance) en plus du clavier ; touches reconfigurables et **bibliothèque de jeux** (dossier `games`)
- **Menu rapide à la manette** : en jeu, **LB + RT** ouvre un overlay de configuration **entièrement pilotable à la manette** (croix pour naviguer, ←→ pour modifier, A valider, B retour, LB+RT pour fermer) donnant accès à charger un jeu, sauvegarder/charger l'état, réinitialiser, filtre d'affichage, aspect 4:3, plein écran, taille de la fenêtre et quitter
- **Audio NAudio** : sortie 44,1 kHz **stéréo**, buffer 500 ms avec rejet propre en cas de dépassement, pré-roll anti-famine

## Limitations actuelles

- ⚠️ CD-ROM² : les jeux bootent (images mono-fichier multi-pistes ou un `.bin` par piste), chargent leur programme, jouent leur **musique CD-DA** et leurs **bruitages ADPCM**. Les images **CHD** (`.chd`) bootent, s'exécutent et jouent leur **musique CD-DA** (pistes data en LZMA/zlib, audio en FLAC/LZMA/zlib). Reste à faire : l'auto-boot sans appuyer sur RUN
- ❌ Arcade Card
- ❌ Timing VDC par scanline (pas mid-scanline)

## Prérequis

- **Windows 10/11**
- **.NET 8 SDK** ou ultérieur
- **Visual Studio 2022** (ou compilation en ligne de commande)

Affichage **Direct3D 11** (n'importe quel GPU compatible D3D 11) ; **repli automatique sur GDI+** si Direct3D est indisponible, donc aucun matériel particulier n'est indispensable.

## Compilation

> ⚠️ **Compilez en Release pour jouer.** Le mode Debug est nettement plus lent
> (vérifications d'overflow, débogueur attaché) et peut saccader.

### Visual Studio

1. Ouvrir `PceEmu.sln`
2. Passer la configuration de **Debug** à **Release** (liste déroulante en haut)
3. Build → Compiler la solution (Ctrl+Shift+B)
4. Lancer avec **Ctrl+F5** (sans débogueur)
5. Exécutable : `bin\Release\net8.0-windows\PceEmu.exe`

### Ligne de commande

```bash
cd PceEmu
dotnet build -c Release
dotnet run -c Release
```

### Mode test console (sans interface)

Pour diagnostiquer une ROM sans fenêtre :

```bash
PceEmu.exe --test-console chemin/vers/rom.pce
```

Affiche le comptage de frames et de pixels non-noirs sur 10 frames.

## Utilisation

**Menu**
- **File → Open ROM** : charger une ROM (`.pce`, HuCard, image CD `.cue/.ccd/.img/.chd`, ou archive `.zip`/`.7z`) ; **Bibliothèque de jeux** liste le dossier `games`
- **Emulation → Pause / Reset** (touches P / R)
- **View → Scale 1x/2x/3x** : taille de fenêtre en 4:3 (320×240, 640×480, 960×720) ; le rendu s'adapte aussi au redimensionnement libre (verrouillé en 4:3)
- **View → Aspect 4:3** : basculer entre le 4:3 d'origine et l'aspect des pixels internes
- **View → Plein écran** (F11 ; Échap pour sortir)
- **View → Filtre d'affichage** : Pixels nets, Pixels lisses, Scanlines, CRT (nécessite Direct3D)
- **Options** : configuration des touches, activation de la manette, dossier des jeux

**Clavier**

| Touche | Bouton PC Engine |
|--------|------------------|
| Flèches | Directions |
| X | Bouton I |
| Z | Bouton II |
| Entrée | RUN |
| Shift | SELECT |
| P | Pause |
| R | Reset |
| F5 / F8 | Sauvegarder / charger l'état |
| F11 / Échap | Plein écran (entrer / sortir) |

Les boutons I et II suivent la **position physique** des touches X et Z (indépendamment de la disposition AZERTY/QWERTY) ; toutes les touches sont reconfigurables via **Options → Configuration des touches**, et une manette Xbox est utilisable en parallèle.

**Menu rapide à la manette** — en cours de jeu, appuyer sur **LB + RT** (bumper gauche + gâchette droite) ouvre un overlay de configuration qui met le jeu en pause. Il se pilote uniquement à la manette : croix haut/bas pour choisir une entrée, gauche/droite pour changer une valeur (filtre, aspect, taille…), **A** pour valider, **B** pour revenir (ou fermer depuis le menu principal), **LB + RT** pour refermer. On peut ainsi charger un jeu, gérer les états, changer le filtre d'affichage, passer en plein écran, etc., sans clavier ni souris.

## Architecture

```
PceEmu/
├── Core/                     # Émulation pure (aucune dépendance UI)
│   ├── Cpu6280.vb            # CPU HuC6280 (table complète, IRQ, flag T, blocs)
│   ├── Vdc.vb                # VDC HuC6270 (VRAM words, tilemap, sprites, DMA, IRQ)
│   ├── Vce.vb                # VCE HuC6260 (palette 512, cache ARGB)
│   ├── Vpc.vb                # SuperGrafx : 2e VDC + VPC (fenêtres de priorité, mélange)
│   ├── Psg.vb                # PSG 6 canaux (volumes log, DDA timestampé, bruit)
│   ├── Timer.vb              # Timer /1024 (classe CpuTimer)
│   ├── Joypad.vb             # Manette (nibbles SEL, actifs bas)
│   ├── MemoryMap.vb          # MMU MPR0-7, miroirs ROM, décodage I/O, $1402/$1403
│   ├── Cartridge.vb          # Chargement ROM (+ en-tête 512 octets) + mapper SF2
│   ├── CdRom.vb              # CD-ROM² : interface SCSI, CD-DA, ADPCM, IRQ2
│   ├── CdImage.vb            # Lecture des images CD (.cue/.ccd/.img/.chd, TOC, LBA/pregaps)
│   ├── Chd.vb                # Lecteur CHD géré (en-tête v5, map, Huffman, framing CD, zlib)
│   ├── LzmaCodec.vb          # Décodeur LZMA brut pour les hunks CHD (cdlz)
│   ├── FlacCodec.vb          # Décodeur FLAC pour les hunks CHD audio (cdfl, CD-DA)
│   ├── PceSystem.vb          # Orchestration : 263 scanlines × 455 cycles CPU
│   └── Constants.vb          # Constantes timing/adressage
├── Frontend/                 # Interface Windows
│   ├── MainForm.vb           # Fenêtre, menus, boucle d'émulation (limiteur précis)
│   ├── D3DRenderer.vb        # Affichage Direct3D 11 (Vortice) : texture + shaders HLSL
│   ├── Direct3D11Renderer.vb # Repli GDI+ (nom historique) : bitmap + Paint + Invalidate
│   ├── AudioOut.vb           # NAudio stéréo, buffer 500 ms, pré-roll
│   ├── Input.vb              # Actions clavier configurables
│   ├── KeyboardLayout.vb     # Position physique d'une touche selon la disposition
│   ├── GamepadInput.vb       # Manette Xbox (XInput, sans dépendance)
│   ├── KeyConfigForm.vb      # Fenêtre de configuration des touches
│   ├── RomLibraryForm.vb     # Bibliothèque du dossier des jeux
│   ├── RomArchive.vb         # Ouverture ROM / ZIP / 7z en mémoire
│   ├── ArchiveOrgForm.vb     # Téléchargement depuis archive.org (sources personnalisables)
│   └── Settings.vb           # Réglages persistants (PceEmu.cfg)
├── Tests/                    # Bancs d'essai (projets séparés, hors PceEmu.sln)
│   ├── CollisionSprite0/     # Vérifie la collision du sprite 0 via les registres VDC
│   ├── LfoPsg/               # Vérifie le LFO du PSG contre des références calculées
│   ├── MapperSf2/            # Vérifie le mapper SF2 avec une ROM factice à motif connu
│   ├── SaveState/            # Vérifie le déterminisme des sauvegardes et la BRAM
│   ├── SuperGrafx/           # Vérifie le décodage, la RAM étendue et le mélange VPC
│   ├── RomArchive/           # Vérifie l'ouverture des ROMs nues, ZIP et 7z
│   ├── StereoPsg/            # Vérifie le panoramique stéréo (balance canal + générale)
│   ├── TimerIrqAck/          # Vérifie l'idiome ré-activer→acquitter de l'IRQ timer
│   ├── VblankRcrSplit/       # Vérifie la coïncidence RCR / VBlank sur une scanline
│   └── CdRom/                # Vérifie l'interface SCSI CD-ROM² (handshake, lecture secteurs)
├── Program.vb                # Point d'entrée (+ mode --test-console)
├── PceEmu.vbproj             # Projet (.NET 8, RemoveIntegerChecks, NAudio, SharpCompress, Vortice)
└── PceEmu.sln
```

Le Core est indépendant de WinForms : il compile aussi en `net8.0` pur (utilisé pour les tests automatisés sous Linux pendant le développement).

## Dépendances NuGet

- **NAudio 2.2.1** — sortie audio (restauration automatique via `dotnet restore`)
- **SharpCompress 1.0.0** — décompression des archives 7z en mémoire (le ZIP passe par la bibliothèque standard)
- **Vortice.Direct3D11 / Vortice.D3DCompiler 3.6.2** — pipeline Direct3D 11 et compilation des shaders HLSL de l'affichage GPU

Toutes se restaurent automatiquement au build. L'affichage utilise Direct3D 11 (via Vortice) et retombe sur GDI+ si le GPU/pilote ne le permet pas ; la compilation HLSL s'appuie sur `d3dcompiler_47.dll`, fourni d'origine avec Windows.

## Jalons de test

1. ✅ **CPU + Mémoire** — boot vérifié contre le code réel des ROMs (vecteurs, TAM, TII…)
2. ✅ **VDC/VCE** — écrans titres complets (Reflectron, Bonk 3)
3. ✅ **Sprites + IRQ RCR/VBlank + DMA SATB** — scènes de jeu correctes
4. ✅ **Joypad** — navigation des menus validée
5. ✅ **PSG** — musique juste (fréquences vérifiées par analyse spectrale), voix DDA restituées
6. ✅ **SuperGrafx** — les cinq jeux démarrent et animent ; Daimakaimura pilote les fenêtres du VPC (9 526 écritures en 1800 frames)
7. ✅ **Mapper SF2** — Street Fighter II' démarre et anime, 1662 commutations de banque en 3600 frames
8. ✅ **HuCard 3 Mbit (384 Ko)** — Batman (Japan) (En) : écran-titre affiché grâce au mapping « coupé » $00-$3F / $40-$7F (auparavant écran noir)
9. ✅ **Stéréo PSG** — balance de canal et balance générale appliquées voie par voie (banc StereoPsg : 10/10, dont un cas garde-fou)
10. ✅ **Acquittement IRQ timer** — After Burner II (Japan) (En) ne gèle plus : délai d'un cran après démasquage d'IRQ ($1402) pour que l'ack ($1403) s'exécute avant la reprise (banc TimerIrqAck : 3/3)
11. ✅ **Coïncidence RCR / VBlank** — Air Zonk (USA) ne gèle plus en niveau 1 : quand un split raster tombe sur la ligne de VBlank, la VBlank est différée d'une scanline pour être servie séparément (banc VblankRcrSplit : 5/5)
12. ✅ **CD-ROM² (Super System Card)** — les jeux CD bootent et s'exécutent : interface SCSI $1800-$180F (handshake REQ/ACK), lecture de l'image CD (.cue/.ccd/.img), 256 Ko de RAM CD, IRQ2. La System Card charge le programme depuis le disque et le lance
13. ✅ **Images CD multi-pistes + acquittement d'IRQ CD** — prise en charge des .cue référençant un .bin par piste (piste data + pistes audio), avec calcul des LBA cumulés et pregaps ; la lecture de $1803 acquitte l'IRQ CD (évite une tempête d'IRQ2). Banc CdRom : 13/13
14. ✅ **Audio ADPCM du CD-ROM²** — DMA automatique des données du CD vers la RAM ADPCM ($180B), décodage OKI ADPCM 4 bits, lecture à la fréquence réglée par $180E et mixage avec le PSG. Addams Family (qui plantait en jouant ses samples) boote, tourne et sort son audio
15. ✅ **Images CD mono-fichier multi-pistes** — un .cue pointant un unique .img contenant plusieurs pistes (data + audio, aux INDEX absolus) est correctement découpé en pistes, avec une plage LBA par piste. Corrige Implode, qui tombait dans le lecteur CD de la System Card faute de TOC exploitable

## Notes techniques

### Points d'exactitude hardware notables
- Vecteurs d'interruption HuC6280 (différents du 6502 : RESET en $FFFE)
- Démasquage d'IRQ ($1402) différé d'une instruction : l'idiome « ré-activer puis acquitter » ($1402 puis $1403) des handlers timer ne se ré-entre pas en boucle
- RCR (comparaison raster) et VBlank sur la même scanline : la VBlank est différée d'une ligne pour rester une interruption distincte (comme le matériel, où le split milieu-de-ligne et la VBlank fin-de-ligne sont espacés)
- CD-ROM² : interface SCSI ($1800-$180F). Le lecteur pose REQ (bit6 de $1800) quand un octet est prêt ; l'initiateur asserte ACK ($1802 bit7), ce qui fait retomber REQ, puis le relâche (REQ remonte). Machine à phases commande→données→status→message. RAM CD de 256 Ko en banques $68-$87. Interruption du lecteur sur IRQ2 (vecteur $FFF6) ; la lecture de $1803 acquitte le status d'IRQ (sinon tempête). La System Card (256 Ko + en-tête de 512 o) se charge comme une HuCard. Images CD : un .cue peut pointer un unique .img **multi-pistes** (INDEX absolus dans le fichier) OU un .bin par piste (LBA cumulés sur les fichiers) ; une entrée par piste avec une plage LBA propre, pregaps (INDEX 00/01) compris ; secteurs lus à la demande (les pistes audio pèsent des centaines de Mo). **Audio ADPCM** : le jeu écrit un flux vers la RAM ADPCM par DMA depuis le CD ($180B), puis le lecteur décode l'ADPCM OKI 4 bits à la fréquence de $180E et le mixe au PSG. **Images CHD** : le format compressé de MAME (v5) est lu par un portage géré de libchdr (pas de DLL native) — en-tête, map v5 (bitstream + Huffman RLE), framing des codecs CD (8 frames de 2448 o/hunk), et décodage des hunks en **zlib** (`DeflateStream`) ou **LZMA** (décodeur brut porté). L'émulateur ne lit que les 2048 o de données utilisateur (offset 16) des secteurs data et l'audio brut du CD-DA : on n'a donc besoin ni de régénérer le sync/ECC ni de décoder le subcode. Le mapping LBA↔frame physique suit le padding par piste (4 frames) des métadonnées CHT2. Validé octet à octet contre les images d'origine (Addams, Baby Jo). Les pistes **CD-DA** sont décodées via un portage de décodeur **FLAC** (cdfl : frames FLAC brutes, sous-trames CONSTANT/VERBATIM/FIXED/LPC, codage de Rice, décorrélation stéréo). L'audio CD est stocké **big-endian** dans le CHD : `CdImage` repasse les secteurs audio en little-endian (comme un .bin) avant de les fournir au lecteur
- MPR7 = 0 au reset ; zéro page logique en $2000, pile en $2100
- VRAM adressée en words 16 bits, écriture VWR = latch LSB puis MSB
- Auto-incrément d'adresse VRAM selon CR bits 11-12 (1/32/64/128)
- Bit 0 du code pattern sprite ignoré (cellules de 64 words, stride $40/$80)
- Collision du sprite 0 évaluée sur les pixels opaques, quel que soit l'ordre d'affichage
- Volumes PSG logarithmiques (1,5 dB par pas) — indispensable pour l'équilibre musical
- DDA : chaque écriture est horodatée au cycle CPU et rejouée sur la timeline de la frame (sans cela, voix et coups sont inaudibles)
- SuperGrafx : chaque VDC tranche lui-même entre son fond et ses sprites, et n'émet qu'un pixel accompagné d'un drapeau « sprite ou fond » ; le VPC ne peut donc pas distinguer un sprite de priorité haute d'un sprite de priorité basse
- BRAM : une console neuve présente l'en-tête de formatage « HUBM » ; sans lui les jeux considèrent la mémoire vierge et refusent d'y écrire
- Sauvegarde d'état : l'empreinte de la ROM est stockée dans le fichier, ce qui interdit de charger l'état d'un autre jeu ; le verrou d'écriture de la BRAM ($1803) n'est pas émulé
- Mapper SF2 : c'est l'adresse écrite qui sélectionne la banque ($1FF0 à $1FF3), la valeur écrite est ignorée ; le mapping est porté par la cartouche, pas par la MMU
- HuCard de 384 Ko (3 Mbit) : mapping « coupé » — pages $00-$3F sur les 256 premiers Ko, pages $40-$7F sur les 128 derniers Ko (à partir de 0x40000). Un simple miroir puissance-de-deux renverrait les banques hautes au début de la ROM et donnerait un écran noir (cas Batman)
- LFO : le canal 1 cesse d'être audible et sa sortie signée s'ajoute à la période du canal 0 ; sa propre période vaut celle du canal 1 multipliée par $0808 ; le bit 7 de $0809 fige le modulateur sans rendre le canal 1 audible
- Affichage Direct3D 11 : la frame est chargée dans une texture BGRA et affichée par un triangle plein écran ; le letterbox 4:3 est calculé dans le pixel shader (barres noires), ce qui évite tout effacement du fond. Le framebuffer interne a un **pas (stride) fixe de 512 pixels** quelle que soit la largeur affichée (256/320/344/352…) : chaque ligne se lit à `y × 512` et seuls `largeur affichée` pixels sont copiés (une lecture au pas de la largeur affichée provoque un cisaillement de l'image). Rendu et présentation sur le thread d'émulation (le limiteur en ticks absolus évite le double throttling avec le vsync)

### Performance
- `RemoveIntegerChecks=true` dans le vbproj (les vérifications d'overflow VB coûtent très cher dans la boucle CPU)
- Limiteur de framerate par accumulateur de ticks Stopwatch (Sleep grossier + SpinWait fin) — `Thread.Sleep` seul a une précision de ~15,6 ms sous Windows
- Aucune allocation par scanline (buffers réutilisés)
- Mesuré : ~4× le temps réel en Release sur machine modeste

## FAQ

**Q : L'image reste noire**
R : Vérifier que la ROM est un `.pce` valide ; essayer `--test-console` pour voir si le Core produit des pixels. Les cartouches de 384 Ko (3 Mbit) sont désormais prises en charge.

**Q : Ça saccade**
R : Compiler et lancer en **Release**, sans débogueur (Ctrl+F5). Vérifier le compteur FPS dans la barre de statut : ~59-60 attendu.

**Q : Le son crépite au démarrage**
R : Un pré-roll de 60 ms est déjà appliqué ; si cela persiste, augmenter `DesiredLatency` dans `AudioOut.vb`.

**Q : Comment vérifier une fonction du VDC sans ROM ?**
R : Voir `Tests/README.md`. Le banc d'essai de la collision sprite 0 se lance avec `dotnet run -c Release` et sert de modèle.

**Q : Une ROM ne boote pas**
R : Les ROMs avec en-tête (taille Mod 8192 = 512) sont gérées. Signaler la ROM concernée pour diagnostic.

## Dossier des jeux et configuration

Les jeux sont cherchés dans un dossier `games` créé à côté de l'exécutable, sous-dossiers compris. **Fichier → Bibliothèque de jeux** en donne la liste avec un filtre par nom ; le dossier se change depuis cette fenêtre ou depuis **Options → Dossier des jeux**.

Les réglages (touches, dossier des jeux, manette) sont conservés dans `PceEmu.cfg`, à côté de l'exécutable. C'est un fichier texte en « clé = valeur », modifiable à la main si besoin.

## Historique des versions

- **1.36** — correctif du **blocage de Down Load 2 sur l'écran du cerveau** de l'intro (le jeu n'enchaînait jamais sur la suite — logo NEC Avenue, écran-titre). L'intro joue une narration ADPCM et attend sa fin en interrogeant le BIOS (`ad_stat`), qui lit l'état de lecture dans le registre de contrôle `$180D`. Sur le vrai matériel, quand la lecture ADPCM épuise sa longueur, le contrôleur CD lève une **IRQ2 « fin ADPCM »** (bit `$08` du status `$1803`, masqué par l'enable `$1802`) ; le handler de la System Card (`$E845`) désactive alors les enables et **efface les bits de lecture du contrôle** (`TRB #$60 $180D`) — c'est ce qui fait dire à `ad_stat` « terminé ». Mon émulation ne levait jamais cette IRQ (seuls les bits `$20/$40/$10` existaient) : le ménage du BIOS ne tournait jamais, `ad_stat` répondait « en lecture » pour toujours, et le jeu attendait indéfiniment. Correctif (`Core/CdRom.vb`) : le bit `$08` est posé dans le status à la fin de lecture, s'efface sur re-latch de longueur et sur reset ADPCM, et n'est pas acquitté par la lecture de `$1803` (conforme au matériel : c'est la désactivation de l'enable par le handler qui fait retomber l'IRQ). Le bit « moitié ADPCM » (`$04`, utilisé pour le streaming) reste volontairement non émis tant que sa condition exacte n'est pas validée sur référence. Vérifié : l'intro enchaîne cerveau → NEC Avenue → titre « PUSH RUN BUTTON » → démo. Non-régression : suite complète **154 tests, 0 échec**, dont 7 nouveaux cas dans `Tests/CdRom` (cycle complet lecture→fin→IRQ→ménage, prouvés sensibles par mutation) ; Forgotten Worlds (1.35, gros utilisateur d'ADPCM), Turrican (1.33) et D&D (1.34) revérifiés.
- **1.35** — correctif du **glitch graphique de Forgotten Worlds** (sprite d'effet « en fragments » à côté du personnage pendant la démonstration, au lieu d'une étoile nette). Longue enquête : un validateur sémantique indépendant a d'abord innocenté le décompresseur du jeu (71 748 instructions rejouées, zéro divergence), puis la comparaison maillon par maillon avec l'émulateur de référence Geargrafx (piloté en débogueur : points d'arrêt, dumps mémoire) a montré que les motifs du sprite arrivaient en VRAM **décalés d'un octet**. Cause : le jeu charge cette animation en RAM ADPCM (DMA CD), puis la **relit par le CPU via le port `$180A`** pour la recopier en RAM banque `$87` avant de la streamer en VRAM. Sur le vrai matériel, `$180A` a un **tampon à une lecture de latence** : lire le port renvoie la valeur *précédemment* tamponnée puis charge l'octet courant ; le BIOS (routine `ad_read`) le sait, arme l'adresse à `latch-1` et jette les **deux** premières lectures. Mon émulation lisait la RAM ADPCM directement, sans tampon → la deuxième lecture « à jeter » consommait le premier octet utile et tout le flux glissait d'une position, faussant l'appariement des octets en mots VDC. Correctif (`Core/CdRom.vb`) : modélisation du tampon (renvoyer l'ancien contenu, recharger, incrémenter), sérialisé dans les sauvegardes d'état (**format 3** ; les sauvegardes aux formats 1 et 2 restent lisibles). Étoile vérifiée visuellement dans la démo, contenu RAM/VRAM identique octet pour octet à Geargrafx et au disque. Non-régression : suite complète **147 tests, 0 échec**, dont 5 nouveaux cas dans `Tests/CdRom` reproduisant le protocole `ad_read` (prouvés sensibles : ils échouent sans le tampon) ; Turrican (1.33) et D&D Order of the Griffon (1.34) revérifiés.
- **1.34** — correctif d'un **gel au démarrage de Dungeons & Dragons: Order of the Griffon** (écran noir, le jeu ne démarrait pas). Le processeur restait bloqué dans une **tempête d'interruptions timer** : son gestionnaire d'IRQ timer réactive les interruptions (`CLI`) *avant* d'acquitter l'interruption (`STA $1403`), en s'appuyant sur une subtilité du 6502/HuC6280 — l'effet de `CLI` sur la prise en compte des interruptions est **différé d'une instruction**, ce qui laisse l'acquittement s'exécuter avant qu'une nouvelle IRQ ne soit reprise. Mon émulation appliquait déjà ce délai au démasquage d'IRQ via le registre `$1402`, mais **pas à l'instruction `CLI` elle-même** ; l'IRQ était donc reprise juste après le `CLI`, avant l'acquittement, et le gestionnaire se ré-entrait à l'infini sans jamais atteindre son travail utile — écran figé. Correctif (`Core/Cpu6280.vb`) : `CLI` arme désormais le même délai d'un cran que le démasquage `$1402`. Titre et menu du jeu vérifiés à l'écran. Non-régression : suite complète **142 tests, 0 échec**, dont un nouveau banc dédié (`Tests/TimerIrqAckCli`) reproduisant l'idiome `CLI`+ack et prouvé sensible (il échoue si l'on retire le délai) ; le banc `TimerIrqAck` (idiome `$1402`) et tous les autres restent au vert, et Turrican (correctif 1.33) reste fonctionnel.
- **1.33** — correctif de l'**écran noir de ~25 secondes au démarrage de Turrican**. Le jeu affiche normalement, pendant son intro, un logo puis un écran-titre et un menu, tous bâtis comme une image bitmap dessinée à l'écran ; l'émulateur ne montrait qu'un écran noir avant de basculer sur la démonstration. Cause : le HuC6270 n'adresse que 32 K mots de mémoire vidéo ($0000-$7FFF), et une écriture dont l'adresse dépasse cette limite doit être **ignorée** (mémoire inexistante). Turrican efface volontairement un bloc plus grand que nécessaire en partant d'une adresse haute, en comptant sur ce rejet ; mon émulation **repliait** au contraire l'adresse au début de la mémoire vidéo, ce qui écrasait la table d'affichage (BAT) tout juste écrite → l'écran restait vide malgré l'arrière-plan activé. Correctif (`Core/Vdc.vb`) : une écriture n'est effectuée que si l'adresse d'écriture est dans les limites (< $8000), sans repli — comportement conforme au matériel et aux émulateurs de référence. Rendu vérifié de bout en bout (logo Ballistic, titre Turrican, menu, démo). Non-régression : suite complète **139 tests, 0 échec**, dont un nouveau banc dédié (`Tests/VramWriteWrap`) prouvé sensible (il échoue si l'on rétablit le repli).
- **1.32** — correctif d'un **blocage définitif de Chiki Chiki Boys** après la mort du personnage (le jeu restait figé indéfiniment, sans planter au sens propre — le processeur tournait en boucle infinie en attendant une interruption qui ne survenait jamais). Cause : le VDC (HuC6270) expose un mode « auto-répétition » pour le transfert de la table de sprites (SATB) — une fois activé, un nouveau transfert doit se déclencher automatiquement à chaque vblank sans que le jeu ait besoin de le redemander explicitement. Mon émulation n'armait ce mode qu'*après* un premier transfert manuel ; un jeu qui active l'auto-répétition sans redéclencher explicitement un transfert (parce que l'adresse source n'a pas changé depuis la dernière fois) restait donc bloqué à vie, le mode ne s'étant jamais réellement activé. C'est exactement ce qui se produit dans Chiki Chiki Boys après la séquence de mort. Correctif (`Core/Vdc.vb`) : le drapeau interne d'auto-répétition se met à jour dès l'écriture du registre de contrôle DMA, et non plus seulement après un transfert. Diagnostic mené à partir d'une sauvegarde d'état fournie par l'utilisateur juste avant la mort du personnage ; confirmé par traçage du processeur (boucle sur seulement 2 adresses pendant des dizaines de milliers d'instructions avant le correctif, disparue après). Non-régression : **empreinte de rendu de Batman identique au bit près avant/après** (20 captures sur 1200 frames), Addams Family boote et tourne toujours, bancs CdRom 13/13, SaveState 13/13, SuperGrafx 33/33, RomArchive 16/16
- **1.31** — **téléchargements multiples en cochant des jeux**, sur les deux interfaces. Menu rapide manette : sur la page « Télécharger des jeux… », **Y** coche/décoche le jeu en surbrillance et **X** lance le téléchargement de tous les jeux cochés à la suite (progression « [i/n] nom — … », annulation par B qui arrête après le fichier en cours, résumé final « x/n installés »). Fenêtre de bureau : la liste devient une liste à cases à cocher (clic pour cocher) ; « Télécharger la sélection » télécharge tous les jeux cochés à la suite, ou le jeu survolé si rien n'est coché (comportement à un seul jeu inchangé) ; la liste se rafraîchit automatiquement en fin de lot pour masquer les jeux désormais installés. Aucun changement dans `ArchiveOrgClient.vb` (le téléchargement d'un fichier reste la même brique, simplement rejouée en séquence). Non-régression : Core inchangé, bancs CdRom 13/13, SaveState 13/13, SuperGrafx 33/33, RomArchive 16/16
- **1.30** — le **menu rapide manette** (LB+RT en jeu) permet désormais de **télécharger des jeux depuis archive.org sans quitter le jeu en cours** : une nouvelle entrée « Télécharger des jeux… » ouvre la liste des sources déjà configurées (ajoutées depuis le menu Fichier sur ordinateur), puis la liste des fichiers compatibles de la source choisie (déjà présents dans le dossier `games` masqués automatiquement), avec suivi de progression et annulation (B) entièrement à la manette. Une fois le téléchargement terminé, un nouvel appui sur A lance le jeu directement. La logique de téléchargement (liste des fichiers, transfert avec progression/annulation) a été isolée dans `Frontend/ArchiveOrgClient.vb`, partagée avec le formulaire de bureau existant sans le modifier. Non-régression : Core inchangé, bancs CdRom 13/13, SaveState 13/13, SuperGrafx 33/33, RomArchive 16/16
- **1.29** — correctif du **chargement CD de certains jeux Arcade CD** (ex. **Forgotten Worlds**), qui restaient bloqués sur un écran noir après l'écran de sélection. La routine de lecture de secteurs du BIOS System Card lit en masse via le port de données $1808 par blocs fixes de 2048 octets, puis reteste la phase SCSI en $1800 pour décider s'il reste des données (DATA IN, $C8) ou si la commande est terminée (STATUS, $D8). Quand la fin des données d'une commande tombe **au milieu** d'un bloc de 2048, le BIOS **sur-lit** $1808 au-delà du dernier octet ; mon émulation faisait alors avancer le handshake auto-ACK dans les phases Status puis Message, entraînant la séquence jusqu'à BusFree **avant** que le BIOS ne puisse observer la phase Status — il attendait alors indéfiniment $C8/$D8 sur un bus libre ($00). Correctif : la lecture de $1808 ne fait avancer le handshake auto-ACK **que pendant la phase DATA IN** ; en Status/Message, elle renvoie l'octet courant sans avancer la phase (le statut et le message se lisent, comme sur matériel, via l'ACK manuel $1801/$1802). Forgotten Worlds passe désormais la sélection joueur et charge son niveau (intro affichée). Non-régression vérifiée : Baby Jo, Addams Family et Chiki Chiki Boys bootent à l'identique (mêmes empreintes d'image, avec et sans le correctif) ; bancs CdRom 13/13, SaveState 13/13, SuperGrafx 33/33
- **1.28** — support de l'**Arcade Card** (extension CD-ROM² à 2 Mo de RAM). Portage fidèle de l'émulation Mednafen (`hw_misc/arcade_card`) : quatre « ports » à adressage **auto-incrémenté** via les registres $1A00-$1AFF (base 24 bits, offset 16 bits, incrément 16 bits, contrôle), un registre à **décalage/rotation 32 bits** en $1AE0+, et une **fenêtre d'accès direct** dans les banques CPU $40-$43 (chaque banque vise le port de données correspondant). Les jeux détectent la carte via son identifiant ($1AFE = version $10, $1AFF = ID $51). La carte est présente d'office pour tout jeu CD (comme une Arcade Card branchée) ; les jeux CD non-Arcade ne la touchent pas et restent inchangés (banques $40-$43 routées vers la carte seulement quand elle est allouée). État inclus dans les sauvegardes (**format d'état monté à 2, rétrocompatible** : les sauvegardes format 1 restent chargeables ; la RAM de 2 Mo n'est écrite que si le jeu l'a réellement utilisée). Validé : logique des registres conforme à Mednafen (14 cas — streaming offset auto-incrémenté, mode base, add-offset-to-base sur écriture, décalage 32 bits, ID, hors-plage) ; non-régression : Baby Jo et Addams bootent toujours, bancs CdRom 13/13, SaveState 13/13, SuperGrafx 33/33. (Aucun jeu Arcade Card n'était disponible pour un test de boot complet.)
- **1.27** — dans la fenêtre de téléchargement (archive.org), les jeux **déjà présents dans le dossier `games` sont masqués de la liste**, pour ne pas les re-télécharger. La comparaison se fait par nom de base (sans extension, insensible à la casse) : un `Jeu.zip` proposé par le serveur est masqué même si on n'a localement que `Jeu.pce`. Seuls les vrais fichiers de jeu comptent (save-states, configs, etc. ignorés). Le statut indique combien de jeux sont masqués car déjà présents, et signale le cas où tout l'item est déjà installé. La liste des fichiers possédés est relue à chaque changement de source (donc après un téléchargement)

Le numéro de version monte de 0,1 à chaque correction complète appliquée.

- **1.26** — **support des images CHD** (`.chd`, format compressé de MAME) pour les jeux CD-ROM². Portage géré (VB, sans dépendance native) de la partie lecture de libchdr : en-tête v5, map v5 compressée (bitstream + arbre Huffman importé en RLE), framing des codecs CD (hunk = 8 frames de 2448 o), et décodage des hunks en **zlib** (`DeflateStream` brut) et **LZMA** (décodeur d'intervalle porté du SDK LZMA). Comme l'émulateur ne lit que les données utilisateur des secteurs data (offset 16) et l'audio brut du CD-DA, on évite la régénération du sync/ECC et le décodage du subcode. Le mapping LBA→frame physique respecte le padding par piste (métadonnées CHT2). Résultat validé **octet à octet** contre les .bin d'origine (Addams en cdlz et cdzl ; Baby Jo multi-pistes) : `CdImage(.chd)` est identique à `CdImage(.cue)` sur toute la piste data. Les jeux CHD bootent, s'exécutent et jouent leur **musique CD-DA** : décodeur **FLAC** porté (codec `cdfl` : frames FLAC brutes, sous-trames CONSTANT/VERBATIM/FIXED/LPC, codage de Rice partitionné, décorrélation gauche-côté / droite-côté / milieu-côté), plus audio compressé en LZMA/zlib. L'audio CD étant stocké **big-endian** dans le CHD, `CdImage` repasse les secteurs audio en little-endian ; pour `cdfl` le flux FLAC démarre à l'offset 0 du hunk (pas d'en-tête ecc/complen, contrairement à cdlz/cdzl). Validé **octet à octet** contre les .bin d'origine — data (Addams cdlz/cdzl, Baby Jo) ET audio (Baby Jo piste 1 FLAC : 3440 secteurs ; piste 5 mixte FLAC/LZMA : 19690 secteurs) — 0 écart. `.chd` reconnu à l'ouverture, dans la bibliothèque et le menu manette. Bancs : CdRom 13/13, SaveState 13/13, SuperGrafx 33/33
- **1.25** — **menu de configuration à la manette**. Pendant le jeu, **LB + RT** (bumper gauche + gâchette droite) ouvre un overlay en surimpression, navigable entièrement à la manette : croix haut/bas pour choisir, gauche/droite pour modifier une valeur, A pour valider, B pour revenir/fermer. Il donne accès à toutes les fonctions de configuration : reprendre, charger un jeu (liste du dossier `games`), sauvegarder/charger l'état, réinitialiser, filtre d'affichage, aspect 4:3, plein écran, taille de la fenêtre, et quitter. Le jeu se met en pause tant que le menu est affiché, et l'overlay ne vole pas le focus de la fenêtre
- **1.24** — correctif de l'affichage Direct3D : cisaillement horizontal de l'image (chaque ligne décalée). Le framebuffer a un pas (stride) fixe de 512 pixels quelle que soit la largeur affichée ; le renderer D3D lisait au pas de la largeur affichée, d'où une dérive ligne à ligne. Il lit désormais au bon pas (comme le renderer GDI+)
- **1.23** — **affichage Direct3D 11 avec shaders sélectionnables**. Le rendu passait par GDI+ (mise à l'échelle CPU) ; il utilise désormais un vrai pipeline GPU Direct3D 11 (via Vortice.Windows) : la frame est chargée dans une texture et affichée par un shader HLSL au choix, dans le menu **View → Filtre d'affichage** : **Pixels nets** (échantillonnage au plus proche), **Pixels lisses** (bilinéaire), **Scanlines** (lignes de balayage type CRT) et **CRT** (scanlines + masque d'ouverture RGB). Le letterbox 4:3 est fait dans le shader (barres noires). Repli automatique sur GDI+ si Direct3D est indisponible. Le compilateur HLSL s'appuie sur d3dcompiler_47.dll (fourni avec Windows)
- **1.22** — mode **plein écran** : F11 pour entrer/sortir, Échap pour sortir, ou menu View → « Plein écran ». Cache la barre de menu, la barre d'état et la bordure de fenêtre, et couvre tout l'écran ; l'image reste en 4:3 (bandes latérales sur un écran 16:9). Retour à la fenêtre précédente en re-basculant
- **1.21** — la fenêtre respecte désormais le **4:3** de la console d'origine. Sur une vraie PC Engine, l'image s'affiche en 4:3 sur la TV quelle que soit la résolution interne (256, 320, 344, 352… de large = pixels non carrés) ; le rendu conservait l'aspect des pixels internes, donc le rapport variait selon le jeu. Désormais : image toujours mise à l'échelle en 4:3 (bandes latérales ou haut/bas si besoin), fenêtre en 4:3 par défaut (640×480) et **verrouillée en 4:3 au redimensionnement**, préréglages de taille 1x/2x/3x en 4:3 (320×240, 640×480, 960×720). Option « Aspect 4:3 » dans le menu View pour revenir à l'aspect des pixels internes
- **1.20** — bruitages échantillonnés (ADPCM) : le mauvais sample était joué (ex. Baby Jo — les pleurs du bébé). Le registre de contrôle ADPCM `$180D` définit l'adresse de LECTURE du sample via son bit 3 (D3, depuis le latch d'adresse $1808/$1809) ; mon code l'ignorait et lisait TOUJOURS à l'offset 0 → tous les samples d'un même banc DMA jouaient le premier. Réécriture fidèle au matériel (source Mednafen pcecd.cpp) : adresse de lecture (D3) et d'écriture (D1) sur front, longueur en compteur décroissant (D4), lecture/arrêt sur D5, demi-octet HAUT en premier, arrêt en fin si D6. Vérifié : Baby Jo joue enfin des samples à des adresses distinctes (6597, 40258…) au lieu de 0 ; Addams Family (qui n'utilise que l'offset 0) intact, audio ADPCM lisse
- **1.19** — Baby Jo jouait la mauvaise piste audio sur le menu/intro (piste 3 au lieu de la piste 5). Les commandes CD-DA D8/D9 portent un **type d'adresse** dans leur 10e octet (`cmd(9) & $C0`) : `$00` = LBA binaire, `$40` = MSF (BCD), `$80` = **numéro de piste** (BCD). `ParsePlayPos` ignorait ce type et lisait toujours les octets comme du MSF ; Baby Jo demande « jouer la piste 5 » (`cmd(9)=$80`, `cmd(2)=$05`), que j'interprétais comme « MSF 05:00:00 » → lecture de la piste 3. Corrigé (gestion des 3 types + recherche du LBA de début de piste). Vérifié conforme à RetroArch par analyse audio (corrélation de classes de hauteur 0,997 sur la piste 5)
- **1.18** — icône de l'application (console PC Engine, pixel-art). Intégrée pour l'exécutable (Explorateur, raccourci et barre des tâches) et pour la fenêtre en cours d'exécution
- **1.17** — musique de menu de Baby Jo (et jeux CD à musique en boucle). La commande CD-DA SAPEP (0xD9) porte un **mode** dans son 1er octet : `01` = **boucle** (rejouer le segment en continu), `02` = arrêt + IRQ, `00`/`03` = arrêt. Le mapping était inversé (le mode 1 était traité comme « arrêt + IRQ » au lieu de « boucle ») : la musique de menu se coupait ~11 s à chaque fin de segment avant que le jeu ne la relance. Corrigé d'après la sémantique du matériel (source Mednafen) → la boucle est désormais transparente
- **1.16** — plus de saccade audio à l'agrandissement de la fenêtre (ROM et CD-DA). Le rendu à l'échelle (préscale + bilinéaire) était fait en tenant le verrou du framebuffer partagé avec le thread d'émulation ; sur une grande fenêtre, un repaint long bloquait l'émulation, qui n'alimentait plus l'audio (sous-alimentation = saccade). Le verrou n'est plus tenu que pour une copie 1:1 rapide (snapshot) ; toute la mise à l'échelle se fait hors verrou
- **1.15** — sauvegarde d'état des jeux CD. La **RAM CD étendue** ($68-$87, qui contient le code exécuté du jeu) et l'**état complet du lecteur** (phase SCSI, registres, ADPCM avec ses 64 Ko de RAM, position CD-DA, IRQ) sont désormais sérialisés. Auparavant, recharger un état de jeu CD **plantait** (RAM CD non restaurée → le CPU exécutait du vide). Le format des sauvegardes **cartouche reste inchangé** : le bloc CD n'est écrit que lorsqu'un CD est inséré, donc les anciennes sauvegardes cartouche se rechargent toujours
- **1.14** — rendu : la mise à l'échelle de la fenêtre ne perd plus de lignes. Un agrandissement NearestNeighbor à facteur fractionnaire laissait tomber des scanlines de façon irrégulière (le texte fin, comme la ligne de titre du menu du PCE Loader, apparaissait strié). Désormais l'image est d'abord agrandie d'un facteur entier (pixels nets, aucune ligne perdue) puis réduite à la taille finale en bilinéaire (« sharp bilinear »)
- **1.13** — détection de la Super System Card via les registres d'identification de la RAM étendue ($18C0-$18C7). Le BIOS `ex_memopen` lit une signature $AA/$55 en $18C1/$18C2 puis un octet de configuration en $18C3 (nombre d'unités de 64 Ko de RAM étendue). Sans ces registres, `ex_memopen` échouait et les jeux Super CD-ROM² affichaient « This disc only works on the SUPER CD-ROM² SYSTEM ». Corrige la détection pour tous les jeux Super CD qui reposent sur `ex_memopen` (p. ex. Fantasy Star Soldier, qui passe désormais le contrôle et charge son programme)
- **1.12** — images CD mono-fichier multi-pistes : un .cue pointant un seul .img à plusieurs pistes (data + audio) est découpé correctement, une plage LBA par piste. Corrige Implode, qui tombait dans le lecteur CD de la System Card
- **1.11** — audio ADPCM du CD-ROM² : DMA des données du CD vers la RAM ADPCM, décodage OKI 4 bits et mixage avec le PSG. Addams Family, qui plantait au moment de jouer ses samples audio, boote, tourne et sort son son
- **1.10** — images CD multi-pistes : un .cue peut référencer un .bin distinct par piste (piste data + pistes audio CD-DA), avec LBA cumulés et pregaps — corrige l'erreur de chargement sur ces images. Correction d'une tempête d'IRQ2 : la lecture de $1803 acquitte désormais le status d'IRQ CD. Les jeux qui reposent sur le CD-DA (musique) démarrent mais restent en attente de l'audio (à faire)
- **1.9** — support CD-ROM² / Super System Card : boot et exécution des jeux CD depuis une image .cue/.ccd/.img. Interface SCSI ($1800-$180F) avec handshake REQ/ACK, RAM CD de 256 Ko (banques $68-$87), IRQ2. La System Card lit la TOC, charge le programme du jeu et l'exécute (banc garde-fou CdRom). Ouvrir un .cue/.ccd demande la System Card (mémorisée ensuite). RESTE : audio ADPCM et CD-DA, auto-boot sans RUN, Arcade Card
- **1.8** — correction du gel d'Air Zonk : lorsqu'un split raster (RCR) coïncide avec la ligne de VBlank, la VBlank est différée d'une scanline afin d'être délivrée comme une interruption distincte (les handlers « RCR ou VBlank » ne ratent plus la VBlank ; banc garde-fou VblankRcrSplit)
- **1.7** — correction du gel d'After Burner II : délai d'un cran de reconnaissance d'IRQ après un démasquage via $1402, pour que l'idiome « ré-activer puis acquitter » du handler timer ne provoque plus de ré-entrance en boucle (banc garde-fou TimerIrqAck)
- **1.6** — sortie audio stéréo : la balance de canal ($0805) et la balance générale ($0801) sont appliquées séparément à chaque voie (banc garde-fou StereoPsg)
- **1.5** — mapping des HuCard de 384 Ko (3 Mbit) : Batman (Japan) (En) affiche son écran-titre (auparavant écran noir)
- **1.4** — Core validé sur ROMs commerciales

## Licence

Projet d'apprentissage. Libre de modification pour usage personnel.

---

**Version** : 1.36 (août 2026) — correctif du blocage de Down Load 2 (IRQ de fin de lecture ADPCM)  
**Langage** : VB.NET (.NET 8)  
**Plateforme** : Windows (WinForms + Direct3D 11, repli GDI+)
