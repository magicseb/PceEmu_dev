# Synthèse Technique - PceEmu

État du projet après la campagne de débogage et de validation sur ROMs réelles (août 2026).

## 📋 Vue d'ensemble

| | |
|---|---|
| Langage | VB.NET (.NET 8), WinForms |
| Taille | ~2 550 lignes (Core ~1 950, Frontend ~530) |
| Dépendance | NAudio 2.2.1 uniquement |
| ROMs validées | Reflectron, Bonk 3, Andre Panza Kick Boxing |
| Perf mesurée | ~4× le temps réel en Release |

### Fichiers

**Core (émulation pure, aucune dépendance UI)**
- `Cpu6280.vb` (667 l.) — CPU HuC6280 complet
- `Vdc.vb` (407 l.) — Affichage HuC6270
- `Psg.vb` (256 l.) — Audio 6 canaux
- `MemoryMap.vb` (151 l.) — MMU + décodage I/O
- `PceSystem.vb` (107 l.) — Orchestration frame
- `Constants.vb` (96 l.), `Cartridge.vb` (91 l.), `Vce.vb` (70 l.), `Timer.vb` (57 l.), `Joypad.vb` (55 l.)

**Frontend**
- `MainForm.vb` (249 l.) — Fenêtre + boucle d'émulation
- `Input.vb` (117 l.) — Clavier
- `Direct3D11Renderer.vb` (101 l.) — Rendu GDI+ (nom historique conservé)
- `AudioOut.vb` (65 l.) — NAudio

`Program.vb` (58 l.) — Point d'entrée + mode `--test-console`.

## 🧠 CPU HuC6280 — points d'exactitude

Le HuC6280 n'est **pas** un 6502 standard. Détails critiques implémentés (tous vérifiés contre le code de boot des ROMs) :

- **Vecteurs** : IRQ2/BRK=$FFF6, IRQ1(VDC)=$FFF8, TIMER=$FFFA, NMI=$FFFC, **RESET=$FFFE** (le 6502 utilise $FFFC pour RESET — piège classique)
- **Opcodes spécifiques aux vrais codes** : ST0=$03, ST1=$13, ST2=$23 (écritures directes VDC), TMA=$43, BSR=$44, TAM=$53, CSL=$54, CSH=$D4, SXY=$02, SAX=$22, SAY=$42, CLA/CLX/CLY=$62/$82/$C2, SET=$F4
- **Transferts de blocs** : TII=$73, TDD=$C3, TIN=$D3, TIA=$E3 (destination alternant dst/dst+1 — pour alimenter les ports VDC), TAI=$F3 (source alternante) ; cycles = 17 + 6×longueur
- **BBRi/BBSi** (branches sur bit de zéro page), **RMBi/SMBi**, **TSB/TRB/TST**
- **Flag T** (bit 5 de P) : posé par SET, la prochaine opération ADC/AND/ORA/EOR s'applique à la mémoire $2000+X au lieu de A
- **Mode BCD** pour ADC/SBC
- **Zéro page logique en $2000**, **pile en $2100+S** (mappées par MPR1)
- **MPR7 = 0 au reset** (les vecteurs se lisent dans la bank 0 de la ROM)
- **IRQ level-triggered** : le CPU interroge les lignes VDC/Timer avant chaque instruction, masquées par $1402 et le flag I ; statut lisible en $1403, write $1403 = acquittement Timer
- Registres internes en `Integer` masqués — zéro risque d'OverflowException VB

## 🖥️ VDC HuC6270

- **VRAM : 32 768 words de 16 bits** (l'adressage est en words, pas en octets)
- Écriture VWR : LSB latché, l'écriture du MSB écrit le word et incrémente MAWR selon CR bits 11-12 (1/32/64/128)
- Lecture VRR : buffer pré-chargé, la lecture du MSB incrémente MARR et re-précharge
- **Status** ($0000) : flags CR/OVR/RR/DS/DV/VD, la lecture efface et relâche la ligne IRQ1
- **RCR** : IRQ quand (ligne affichée + 64) == RCR, si CR bit 2
- **VBlank** : flag + IRQ (CR bit 3) à la fin de la zone active (VDW+1 lignes) ; le transfert SATB (DVSSR, ou auto si DCR bit 4) a lieu à ce moment
- **DMA VRAM-VRAM** déclenchée par l'écriture du MSB de LENR
- **Fond** : BAT (12 bits index + 4 bits palette), tiles 8×8 en 4 bitplanes (words y et y+8) ; taille de carte via MWR (32/64/128 × 32/64)
- **Scroll vertical** : compteur interne latché à BYR en début de frame, +1 par ligne, **relatché à la ligne suivant toute écriture de BYR** — indispensable pour les splits raster (HUD, parallaxe)
- **Sortie du VDC** : chaque scanline est produite sous forme de codes VCE (-1 = rien émis), ce qui permet au VPC de mélanger deux VDC sans dupliquer le moteur de rendu
- **Sprites** : SATB interne 64×4 words ; Y-64, X-32 ; tailles 16/32 × 16/32/64 (CGX/CGY) ; cellules 16×16 de 64 words, stride cellule = $40 (X) / $80 (Y) ; **le bit 0 du code pattern est ignoré par le hardware** ; flips X/Y, priorité devant/derrière le fond, limite 16 sprites/ligne avec flag overflow
- **Collision sprite 0** : drapeau d'état bit 0 levé dès qu'un pixel opaque d'un autre sprite recouvre un pixel opaque du sprite 0, IRQ si CR bit 0 ; la détection ignore l'occlusion entre sprites et la priorité vis-à-vis du fond (un sprite invisible collisionne quand même), et le drapeau est effacé à la lecture de l'état
- **Résolution dynamique** : largeur = (HDW+1)×8 (256/320/512 observées), hauteur = VDW+1 ; exposées via `DisplayWidth`/`DisplayHeight`

## 🎨 VCE HuC6260

512 entrées de 9 bits **G3R3B3** (vert en poids fort), adresse auto-incrémentée à l'accès MSB, cache ARGB avec expansion 3→8 bits. Entrées 0-255 : fonds ; 256-511 : sprites.

## 🔊 PSG — les deux clés de l'authenticité

1. **Volumes logarithmiques** : le hardware atténue de **1,5 dB par pas de volume** (5 bits) et **3 dB par pas de balance** (4 bits par côté, canal et général). En linéaire, un accompagnement à 16/31 joue à 52% au lieu de ~7,5% → bouillie sonore. Tables `VolTable`/`BalTable`, gain effectif par canal recalculé une fois par frame.

2. **DDA horodaté** : les jeux jouent voix et percussions en écrivant les échantillons un à un (~7 kHz) via le CPU. Générer l'audio une fois par frame en ne gardant que la dernière valeur réduit le sample à un continu inaudible. Chaque écriture DDA est donc **timestampée au cycle CPU** (`Psg.CycleProvider`) puis la séquence est rejouée sur la timeline de la frame. Mesuré sur Andre Panza : 21 058 écritures DDA sur les 600 premières frames.

3. **LFO** : le canal 1 quitte le mixage et devient modulateur. Sa sortie, centrée sur zéro, est décalée de 0, 4 ou 8 bits selon les bits 0-1 de $0809 (×1, ×16, ×256) puis ajoutée à la période 12 bits du canal 0. La période du modulateur vaut celle du canal 1 multipliée par le registre $0808 — c'est ce produit qui donne des vibratos de quelques hertz. Le bit 7 de $0809 fige le modulateur : plus aucune modulation, mais le canal 1 reste muet.

Également : période 0 = 4096 (hardware), sortie de la moyenne de la waveform au-delà de Nyquist (anti-aliasing), bruit LFSR borné, mixage mono clampé sans clipping.

**Chaîne audio** : `PceSystem.RunFrame()` → `GenerateSamples` (~737 échantillons/frame) → NAudio `BufferedWaveProvider` mono, buffer 500 ms, `DiscardOnBufferOverflow`, pré-roll 60 ms.

## 🖼️ Rendu (Frontend)

L'écran noir initial venait de `CreateGraphics()` : le dessin est effacé au premier repaint. Architecture correcte :
- bitmap persistant protégé par verrou
- `UpdateFrame()` appelable depuis le thread d'émulation : copie du framebuffer (recadré sur `DisplayWidth`×`DisplayHeight`, stride 512), puis `Invalidate()`
- gestionnaire `Paint` : `DrawImage` en nearest-neighbor, ratio conservé, centré
- double-buffering du panel activé par réflexion

## ⏱️ Boucle & timing

- 263 scanlines/frame × **455 cycles CPU** (7,16 MHz) par scanline, ~59,82 Hz
- Dette de cycles reportée d'une scanline à l'autre (instructions à cheval)
- Limiteur de framerate : accumulateur de ticks `Stopwatch` + `Sleep` grossier + `SpinWait` fin, resynchronisation en cas de retard (`Thread.Sleep` seul = précision ~15,6 ms → saccades)
- `RemoveIntegerChecks=true` : gain majeur en VB pour la boucle CPU

## 🐛 Journal des corrections majeures

| Bug | Symptôme | Correction |
|-----|----------|------------|
| Opcodes HuC6280 aux mauvais codes, vecteurs 6502 | Aucune ROM ne boote | Réécriture CPU conforme |
| MPR7=$FF au reset | Vecteurs lus dans l'I/O | MPR7=0 |
| VRAM en octets | Graphismes détruits | VRAM en 32K words |
| Shadowing VB `timerRef`/`TimerRef` | NullReference sur Timer | Paramètre renommé |
| `CreateGraphics()` éphémère | Écran noir | Bitmap + Paint + Invalidate |
| `Thread.Sleep` imprécis + logs en boucle | Saccades | Limiteur ticks + zéro WriteLine |
| NAudio initialisé stéréo (PSG mono) | Buffer vidé 2× trop vite | Mono |
| Bit 0 du code sprite conservé | Sprites impairs corrompus | `And &H7FE` |
| BYR + scanline | Scroll cassé après split raster | Compteur latché |
| Mixage ×8 | Clipping massif (« bruits bizarres ») | Gain sûr + volumes log |
| Volume linéaire | Bouillie sans dynamique | Tables 1,5 dB / 3 dB |
| DDA = dernière valeur de la frame | Voix et coups inaudibles | Événements timestampés au cycle |
| Collision sprite 0 absente | Jeux la testant en boucle | Masque du sprite 0 par scanline |
| LFO PSG ignoré | Vibratos et sirènes absents | Canal 1 en modulateur de période |
| Mapper SF2 factice | SF2' illisible au-delà de 1 Mo | Banques portées par la cartouche |
| Signature ROM débordait | Sauvegarde impossible hors Release | Accumulateur en 64 bits |

## 🖥️🖥️ SuperGrafx

Deux VDC, chacun avec sa VRAM, mélangés par le **VPC HuC6202**.

Le point à comprendre : chaque VDC résout lui-même la priorité entre son fond et ses sprites, puis n'émet qu'un seul pixel sur un bus de 9 bits — 4 bits de couleur, 4 bits de palette, 1 bit « sprite ou fond ». Le VPC ne voit donc jamais la différence entre un sprite de priorité haute et un sprite de priorité basse ; il ne fait qu'ordonner deux pixels déjà résolus.

**Décodage.** En mode SuperGrafx, la zone vidéo n'est plus le VDC répété tous les quatre octets, mais un bloc de 32 octets : `$00-$07` VDC #1 et son miroir, `$08-$0F` VPC, `$10-$17` VDC #2 et son miroir. C'est ce qui permet à un jeu PC Engine bien écrit de fonctionner tel quel.

**Fenêtres.** Deux registres de 10 bits donnent la largeur de deux fenêtres, comptée depuis le bord gauche de l'écran *physique* : la zone affichée ne commence qu'à la valeur `$40`, si bien qu'une fenêtre plus étroite est invisible. Les quatre régions ainsi découpées — aucune fenêtre, fenêtre 1, fenêtre 2, recouvrement — ont chacune leur réglage de 4 bits : VDC #1 actif, VDC #2 actif, et un mode de priorité sur 2 bits.

**Priorités.** Modes 0 et 3 : le VDC #1 passe intégralement devant le VDC #2. Modes 1 et 2 : les sprites des deux chips passent devant les deux fonds. Les sources se contredisent sur la distinction exacte entre les modes 1 et 2 — les notes de Charles MacDonald et les relevés sur console (fil nesdev, Daimakaimura) ne concordent pas — et le choix retenu est celui vérifié sur machine réelle. Les cinq jeux SuperGrafx utilisent de toute façon le mode par défaut la plupart du temps.

**Divers.** 32 Ko de RAM de travail linéaires sur les pages `$F8-$FB` au lieu de 8 Ko répétés ; la ligne IRQ1 est partagée, un jeu doit lire les deux registres d'état pour savoir qui a interrompu ; le bit 0 de `$000E` redirige ST0/ST1/ST2 vers le second VDC.

**Résultat.** Les cinq HuCards SuperGrafx démarrent et animent. Daimakaimura écrit 9 526 fois dans les registres du VPC sur 1800 frames, contre une dizaine pour les autres jeux : il est bien le seul à exploiter les fenêtres, en les redécoupant à chaque ligne — ce que la documentation laissait attendre. Lancés en mode PC Engine, ces jeux ne produisent qu'un écran uni : ils dépendent réellement du matériel supplémentaire.

Source : « NEC SuperGrafx hardware notes », Charles MacDonald.

## 💾 Sauvegarde d'état et BRAM

**Sauvegarde d'état.** Chaque composant sérialise ses propres champs privés (`SaveState`/`LoadState` sur CPU, VDC, VCE, PSG, Timer, manette, MMU et cartouche), `PceSystem` orchestre. Le fichier est compressé en gzip et s'ouvre sur la signature `PCEST`, un numéro de format et une empreinte de la ROM : charger l'état d'un autre jeu est refusé plutôt que de produire un plantage inexplicable. Les événements DDA en attente ne sont pas sauvegardés — ils ne vivent que le temps d'une frame et le jeu les reconstruit.

**BRAM.** Les 2 Ko sont relus au chargement d'une ROM et réécrits quand un jeu y a touché (`BramModified` évite les écritures inutiles). Le fichier est **unique et partagé par tous les jeux**, comme la pile de la vraie console. Une BRAM neuve est initialisée avec l'en-tête de formatage `HUBM` : sans lui, les jeux la voient comme vierge et refusent d'y écrire.

## ⌨️ Entrées et ouverture des jeux

**Disposition clavier.** Plutôt que de tester si l'utilisateur est français, on demande à Windows quelle touche occupe une position physique donnée : `MapVirtualKey(scanCode, MAPVK_VSC_TO_VK)`. Le code de balayage `$2C` désigne la touche sous l'annulaire gauche — « Z » en QWERTY, « W » en AZERTY, « Y » en QWERTZ. Les trois dispositions sont donc gérées sans jamais les énumérer, et une disposition exotique le sera aussi. Hors Windows, l'appel échoue proprement et les valeurs QWERTY servent de repli.

**Configuration.** Les actions portent un nom (`BoutonI`, `Run`, `Sauvegarder`…) et non une touche. `Joypad.UpdateFromKeys` reçoit désormais des noms de boutons de console, ce qui permet au clavier et à la manette d'alimenter la même entrée sans se marcher dessus.

**Manette Xbox.** XInput appelé directement dans `xinput1_4.dll`, avec repli sur `xinput9_1_0.dll` pour les Windows plus anciens. Aucune dépendance ajoutée. Si aucune des deux n'est présente, la manette est déclarée absente une fois pour toutes plutôt que de retenter à chaque frame.

**Archives.** Le ZIP passe par la bibliothèque standard, le 7z par SharpCompress (**1.0.0** ; l'avis CVE-2026-44788 / GHSA-6c8g-7p36-r338 sur `WriteToDirectory()` est corrigé depuis la 0.48.0, et de toute façon cette API n'est jamais appelée ici). Dans les deux cas l'entrée retenue est la plus grosse portant une extension de ROM, et elle est lue **en mémoire** : rien n'est écrit sur le disque, ce qui met du même coup le programme hors de portée des failles de traversée de répertoire. La taille décompressée est plafonnée à 8 Mo — aucune HuCard ne dépasse 2,5 Mo.

## 🗺️ Feuille de route

2. **Stéréo** : exploiter les balances L/R déjà décodées (sortie actuellement mono)
5. Verrou d'écriture de la BRAM ($1803), multitap, CD-ROM²

## 🔧 Outils de diagnostic intégrés

- `Tests/CollisionSprite0` : banc d'essai pilotant le VDC par ses registres, sans ROM (8 cas sur la collision sprite 0)
- `Tests/LfoPsg` : banc d'essai du LFO, comparaison échantillon par échantillon avec des références calculées (9 cas, dont un garde-fou contre les tests insensibles)
- `Tests/MapperSf2` : banc d'essai du mapper, ROM factice dont chaque page porte son numéro (20 cas : banques, zone fixe, miroirs, écritures neutres)
- `Tests/RomArchive` : banc d'essai de l'ouverture des jeux (16 cas : ROM nue, ZIP, 7z, choix de l'entrée, refus d'une archive sans ROM)
- `Tests/SuperGrafx` : banc d'essai du second VDC et du VPC (33 cas : décodage, VRAM séparées, RAM 32 Ko, priorités, fenêtres, ST0-ST2, IRQ partagée)
- `Tests/SaveState` : banc d'essai des sauvegardes, ROM assemblée à la main dont l'état évolue à chaque frame (13 cas, dont un garde-fou et le rejet des fichiers étrangers)
- Mode `--test-console <rom>` dans `Program.vb` (compte les pixels sans UI)
- Compteurs de debug dans `Psg`/`CpuTimer` (`DbgWriteCount`, `DbgDdaWrites`…) et `PceSystem.DbgPsgState()` — inoffensifs en production, précieux pour diagnostiquer une ROM récalcitrante
