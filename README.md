# PceEmu - Émulateur PC Engine / TurboGrafx-16

Émulateur PC Engine (TurboGrafx-16) en VB.NET (.NET 8) : CPU HuC6280 complet, VDC/VCE, PSG 6 canaux avec DDA, rendu GDI+ double-bufferisé.

**Validé sur ROMs réelles** : Reflectron (Aetherbyte), Bonk 3 - Bonk's Big Adventure, Andre Panza Kick Boxing — boot, affichage, musique et samples vocaux fonctionnels.

## Fonctionnalités

- **CPU HuC6280** : table d'opcodes complète et conforme au hardware (TAM=$53, TMA=$43, CSL=$54, CSH=$D4, ST0/1/2, transferts de blocs TII/TDD/TIN/TIA/TAI, BBRi/BBSi, RMBi/SMBi, flag T, mode BCD) ; vecteurs corrects (RESET=$FFFE, IRQ1=$FFF8, TIMER=$FFFA, IRQ2/BRK=$FFF6) ; IRQ level-triggered avec masque $1402/$1403
- **VDC (HuC6270)** : VRAM 32K words, rendu tilemap + sprites par scanline avec priorités et flips, limite 16 sprites/ligne, collision du sprite 0 avec IRQ, DMA VRAM-VRAM et SATB, IRQ RCR/VBlank, compteur de scroll vertical latché (splits raster/parallaxe corrects), résolution d'affichage dynamique (256/320/512 de large)
- **VCE (HuC6260)** : palette 512 couleurs 9 bits (G3R3B3) avec cache ARGB
- **PSG 6 canaux** : waveform 32×5 bits, **volumes logarithmiques conformes au hardware** (1,5 dB/pas volume, 3 dB/pas balance), **DDA timestampé au cycle CPU** (voix et effets de coups restitués), bruit LFSR, **LFO** (le canal 1 module la période du canal 0 en ×1/×16/×256), période 0 = 4096, anti-aliasing au-delà de Nyquist
- **Timer** : prescaler /1024 cycles, IRQ TIMER avec acquittement $1403
- **Joypad** : nibbles actifs bas via SEL/CLR
- **Mapper SF2** : Street Fighter II' Champion Edition (2,5 Mo) — 512 Ko fixes plus quatre banques de 512 Ko commutées par l'adresse écrite en $1FF0-$1FF3
- **Rendu GDI+** : bitmap persistant + événement Paint + double-buffering, recadrage sur la résolution active avec conservation du ratio, mise à l'échelle nearest-neighbor
- **Audio NAudio** : sortie 44,1 kHz mono, buffer 500 ms avec rejet propre en cas de dépassement, pré-roll anti-famine

## Limitations actuelles

- ❌ SuperGrafx (VDC2/VPC) : retiré temporairement, à réintégrer
- ❌ CD-ROM² / Arcade Card
- ❌ Sortie audio mono (pas de stéréo balancée)
- ❌ Timing VDC par scanline (pas mid-scanline)

## Prérequis

- **Windows 10/11**
- **.NET 8 SDK** ou ultérieur
- **Visual Studio 2022** (ou compilation en ligne de commande)

Aucun GPU particulier requis (rendu GDI+).

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
- **File → Open ROM** : charger une ROM `.pce`
- **Emulation → Pause / Reset** (touches P / R)
- **View → Scale 1x/2x/3x** : taille de fenêtre (le rendu s'adapte aussi au redimensionnement libre)

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

## Architecture

```
PceEmu/
├── Core/                     # Émulation pure (aucune dépendance UI)
│   ├── Cpu6280.vb            # CPU HuC6280 (table complète, IRQ, flag T, blocs)
│   ├── Vdc.vb                # VDC HuC6270 (VRAM words, tilemap, sprites, DMA, IRQ)
│   ├── Vce.vb                # VCE HuC6260 (palette 512, cache ARGB)
│   ├── Psg.vb                # PSG 6 canaux (volumes log, DDA timestampé, bruit)
│   ├── Timer.vb              # Timer /1024 (classe CpuTimer)
│   ├── Joypad.vb             # Manette (nibbles SEL, actifs bas)
│   ├── MemoryMap.vb          # MMU MPR0-7, miroirs ROM, décodage I/O, $1402/$1403
│   ├── Cartridge.vb          # Chargement ROM (+ en-tête 512 octets) + mapper SF2
│   ├── PceSystem.vb          # Orchestration : 263 scanlines × 455 cycles CPU
│   └── Constants.vb          # Constantes timing/adressage
├── Frontend/                 # Interface Windows
│   ├── MainForm.vb           # Fenêtre, menus, boucle d'émulation (limiteur précis)
│   ├── Direct3D11Renderer.vb # Rendu GDI+ (nom historique) : bitmap + Paint + Invalidate
│   ├── AudioOut.vb           # NAudio mono, buffer 500 ms, pré-roll
│   └── Input.vb              # État clavier
├── Tests/                    # Bancs d'essai (projets séparés, hors PceEmu.sln)
│   ├── CollisionSprite0/     # Vérifie la collision du sprite 0 via les registres VDC
│   ├── LfoPsg/               # Vérifie le LFO du PSG contre des références calculées
│   ├── MapperSf2/            # Vérifie le mapper SF2 avec une ROM factice à motif connu
│   └── SaveState/            # Vérifie le déterminisme des sauvegardes et la BRAM
├── Program.vb                # Point d'entrée (+ mode --test-console)
├── PceEmu.vbproj             # Projet (.NET 8, RemoveIntegerChecks, NAudio)
└── PceEmu.sln
```

Le Core est indépendant de WinForms : il compile aussi en `net8.0` pur (utilisé pour les tests automatisés sous Linux pendant le développement).

## Dépendances NuGet

- **NAudio 2.2.1** — sortie audio (restauration automatique via `dotnet restore`)

C'est la seule dépendance : SharpDX a été abandonné au profit de GDI+.

## Jalons de test

1. ✅ **CPU + Mémoire** — boot vérifié contre le code réel des ROMs (vecteurs, TAM, TII…)
2. ✅ **VDC/VCE** — écrans titres complets (Reflectron, Bonk 3)
3. ✅ **Sprites + IRQ RCR/VBlank + DMA SATB** — scènes de jeu correctes
4. ✅ **Joypad** — navigation des menus validée
5. ✅ **PSG** — musique juste (fréquences vérifiées par analyse spectrale), voix DDA restituées
6. ❌ **SuperGrafx** — à réintégrer
7. ✅ **Mapper SF2** — Street Fighter II' démarre et anime, 1662 commutations de banque en 3600 frames

## Notes techniques

### Points d'exactitude hardware notables
- Vecteurs d'interruption HuC6280 (différents du 6502 : RESET en $FFFE)
- MPR7 = 0 au reset ; zéro page logique en $2000, pile en $2100
- VRAM adressée en words 16 bits, écriture VWR = latch LSB puis MSB
- Auto-incrément d'adresse VRAM selon CR bits 11-12 (1/32/64/128)
- Bit 0 du code pattern sprite ignoré (cellules de 64 words, stride $40/$80)
- Collision du sprite 0 évaluée sur les pixels opaques, quel que soit l'ordre d'affichage
- Volumes PSG logarithmiques (1,5 dB par pas) — indispensable pour l'équilibre musical
- DDA : chaque écriture est horodatée au cycle CPU et rejouée sur la timeline de la frame (sans cela, voix et coups sont inaudibles)
- BRAM : une console neuve présente l'en-tête de formatage « HUBM » ; sans lui les jeux considèrent la mémoire vierge et refusent d'y écrire
- Sauvegarde d'état : l'empreinte de la ROM est stockée dans le fichier, ce qui interdit de charger l'état d'un autre jeu ; le verrou d'écriture de la BRAM ($1803) n'est pas émulé
- Mapper SF2 : c'est l'adresse écrite qui sélectionne la banque ($1FF0 à $1FF3), la valeur écrite est ignorée ; le mapping est porté par la cartouche, pas par la MMU
- LFO : le canal 1 cesse d'être audible et sa sortie signée s'ajoute à la période du canal 0 ; sa propre période vaut celle du canal 1 multipliée par $0808 ; le bit 7 de $0809 fige le modulateur sans rendre le canal 1 audible

### Performance
- `RemoveIntegerChecks=true` dans le vbproj (les vérifications d'overflow VB coûtent très cher dans la boucle CPU)
- Limiteur de framerate par accumulateur de ticks Stopwatch (Sleep grossier + SpinWait fin) — `Thread.Sleep` seul a une précision de ~15,6 ms sous Windows
- Aucune allocation par scanline (buffers réutilisés)
- Mesuré : ~4× le temps réel en Release sur machine modeste

## FAQ

**Q : L'image reste noire**
R : Vérifier que la ROM est un `.pce` valide ; essayer `--test-console` pour voir si le Core produit des pixels.

**Q : Ça saccade**
R : Compiler et lancer en **Release**, sans débogueur (Ctrl+F5). Vérifier le compteur FPS dans la barre de statut : ~59-60 attendu.

**Q : Le son crépite au démarrage**
R : Un pré-roll de 60 ms est déjà appliqué ; si cela persiste, augmenter `DesiredLatency` dans `AudioOut.vb`.

**Q : Comment vérifier une fonction du VDC sans ROM ?**
R : Voir `Tests/README.md`. Le banc d'essai de la collision sprite 0 se lance avec `dotnet run -c Release` et sert de modèle.

**Q : Une ROM ne boote pas**
R : Les ROMs avec en-tête (taille Mod 8192 = 512) sont gérées. Signaler la ROM concernée pour diagnostic.

## Licence

Projet d'apprentissage. Libre de modification pour usage personnel.

---

**Version** : 1.2 (août 2026) — Core validé sur ROMs commerciales  
**Langage** : VB.NET (.NET 8)  
**Plateforme** : Windows (WinForms + GDI+)
