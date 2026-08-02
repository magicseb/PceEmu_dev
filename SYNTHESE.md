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

## 🗺️ Feuille de route

1. **SuperGrafx** : réintégrer Vpc.vb (VDC2 + mixage fenêtres/priorités)
2. **Stéréo** : exploiter les balances L/R déjà décodées (sortie actuellement mono)
5. Sauvegarde d'état, BRAM persistante

## 🔧 Outils de diagnostic intégrés

- `Tests/CollisionSprite0` : banc d'essai pilotant le VDC par ses registres, sans ROM (8 cas sur la collision sprite 0)
- `Tests/LfoPsg` : banc d'essai du LFO, comparaison échantillon par échantillon avec des références calculées (9 cas, dont un garde-fou contre les tests insensibles)
- `Tests/MapperSf2` : banc d'essai du mapper, ROM factice dont chaque page porte son numéro (20 cas : banques, zone fixe, miroirs, écritures neutres)
- Mode `--test-console <rom>` dans `Program.vb` (compte les pixels sans UI)
- Compteurs de debug dans `Psg`/`CpuTimer` (`DbgWriteCount`, `DbgDdaWrites`…) et `PceSystem.DbgPsgState()` — inoffensifs en production, précieux pour diagnostiquer une ROM récalcitrante
