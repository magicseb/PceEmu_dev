# 🚀 Démarrage Rapide - PceEmu

## En 5 minutes

### 1️⃣ Contenu du dossier
```
PceEmu/
├── Core/                  # Logique d'émulation (CPU, VDC, PSG…)
├── Frontend/              # Interface WinForms + rendu + audio
├── Program.vb             # Point d'entrée (+ mode --test-console)
├── PceEmu.sln             # Solution Visual Studio
├── PceEmu.vbproj          # Projet .NET 8
├── README.md              # Documentation complète
├── SYNTHESE.md            # Détails techniques et historique des corrections
└── DEMARRAGE_RAPIDE.md    # Ce fichier
```

### 2️⃣ Compiler — EN RELEASE !

> ⚠️ Le mode Debug saccade (2 à 4× plus lent). Toujours jouer en **Release**.

**Option A : Visual Studio 2022 (recommandé)**
```
1. Ouvrir PceEmu.sln
2. En haut, passer la liste déroulante Debug → Release
3. Build → Compiler la solution (Ctrl+Shift+B)
4. Lancer SANS débogueur : Ctrl+F5
```

**Option B : Ligne de commande**
```bash
cd PceEmu
dotnet build -c Release
dotnet run -c Release
```

### 3️⃣ Charger une ROM
```
1. File → Open ROM
2. Sélectionner un fichier .pce
3. Le jeu démarre automatiquement
```

### 4️⃣ Jouer
```
Flèches      → Directions
X            → Bouton I
Z            → Bouton II
Entrée       → Run
Shift        → Select
P            → Pause
R            → Reset
```

La barre de statut affiche les FPS : **~59-60 attendu** en Release.

---

## ⚠️ Prérequis

- ✅ **Windows 10/11**
- ✅ **.NET 8 SDK** (ou plus récent)
- ✅ **Visual Studio 2022** ou `dotnet` CLI
- ✅ Aucun GPU particulier (rendu GDI+)

## 🆘 Problèmes courants

### « Ça saccade »
→ Compiler en **Release** et lancer avec **Ctrl+F5** (pas F5). Vérifier le compteur FPS.

### « Erreur NAudio non trouvé »
```bash
dotnet restore PceEmu.vbproj
```
(NAudio est la seule dépendance.)

### « Écran noir sur une ROM »
→ Tester le Core seul, sans interface :
```bash
PceEmu.exe --test-console chemin/vers/rom.pce
```
Si des pixels non-noirs sont comptés, le Core fonctionne — signaler le souci d'affichage.
Sinon, la ROM utilise peut-être une fonctionnalité non supportée (SuperGrafx, CD-ROM²).

### « Le son est déséquilibré »
→ Les volumes suivent les tables logarithmiques du hardware. Ajuster le gain global :
`Core/Psg.vb`, chercher `* 350.0` (une seule occurrence, dans le calcul de `chanGain`).

## 🧪 ROMs validées

| ROM | Résolution | État |
|-----|-----------|------|
| Reflectron (Aetherbyte) | 256×224 | ✅ écran titre + jeu |
| Bonk 3 - Bonk's Big Adventure | 256×240 | ✅ jeu + musique |
| Andre Panza Kick Boxing | 320×240 | ✅ jeu + voix DDA |

Bon jeu ! 🎮
