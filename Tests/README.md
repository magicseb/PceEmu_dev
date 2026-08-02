# Tests

## CollisionSprite0

Banc d'essai de la détection de collision du sprite 0. Il pilote le VDC uniquement par ses registres — comme le ferait un jeu — puis relit le registre d'état :

```bash
cd Tests\CollisionSprite0
dotnet run -c Release
```

Le test monte une scène de deux sprites 16×16 opaques, transfère la table d'attributs par DMA depuis la VRAM, exécute deux frames (le transfert SATB a lieu au VBlank, les sprites n'apparaissent donc qu'à la frame suivante), puis vérifie huit cas :

- sprites superposés → collision détectée
- sprites disjoints horizontalement → pas de collision
- sprites sur des lignes différentes → pas de collision
- collision avec IRQ activée (CR bit 0) → ligne d'interruption levée
- collision avec IRQ désactivée → pas d'interruption
- sprite 0 absent de la ligne → pas de collision
- sprite masqué par un autre sprite → collision détectée quand même
- lecture du registre d'état → drapeau effacé

Le projet ne fait pas partie de `PceEmu.sln` et n'entre pas dans la compilation de l'émulateur (`<Compile Remove="Tests/**" />` dans `PceEmu.vbproj`). Il sert aussi de modèle pour tester d'autres comportements du VDC sans passer par une ROM.

## LfoPsg

Banc d'essai du LFO du PSG :

```bash
cd Tests\LfoPsg
dotnet run -c Release
```

Le principe évite toute mesure approximative : on donne au canal 1 — le modulateur — une forme d'onde **constante**, si bien que la période du canal 0 se trouve décalée d'une valeur connue à l'avance. La sortie doit alors être rigoureusement identique, échantillon par échantillon, à celle d'un PSG où cette période aurait été écrite directement avec le LFO éteint.

Huit cas sont vérifiés : les trois profondeurs (×1, ×16, ×256), un décalage négatif, la mise en sourdine du canal modulateur, le bit 7 de maintien, la profondeur 0 qui rend le canal 1 audible, et une forme d'onde variable qui doit produire une modulation variable.

## MapperSf2

Banc d'essai du mapper de Street Fighter II' :

```bash
cd Tests\MapperSf2
dotnet run -c Release
```

Le test fabrique une ROM factice de 2,5 Mo dont chaque page de 8 Ko commence par son propre numéro. Lire une page revient donc à demander à la cartouche quelle portion de ROM elle a placée là, et la réponse se vérifie exactement — sans dépendre d'un jeu réel.

Vingt cas sont couverts : détection de la cartouche par sa taille, les quatre banques sur les pages $40-$7F, l'immobilité de la zone fixe $00-$3F, le fait que seule l'adresse écrite compte (et non la valeur), l'absence d'effet des écritures hors fenêtre du mapper, et le comportement d'une cartouche ordinaire, miroirs compris.

## SaveState

Banc d'essai des sauvegardes d'état et de la BRAM :

```bash
cd Tests\SaveState
dotnet run -c Release
```

Le test vérifie la seule propriété qui compte vraiment : **une console vierge, rechargée depuis une sauvegarde, doit produire exactement le même avenir que la console d'origine**. Si un champ manque à l'appel — un registre d'adresse VRAM, la palette, un compteur interne — les deux futurs divergent et le test échoue.

Pour cela il utilise une ROM de 8 Ko assemblée à la main, dont le programme incrémente un compteur en page zéro et le déverse en VRAM et dans la palette : chaque frame modifie donc la RAM, la VRAM, la palette et les registres d'adresse du VDC et du VCE.

Treize cas sont couverts, dont un garde-fou vérifiant que l'image évolue bien entre les deux points de comparaison, le rejet d'une sauvegarde faite avec un autre jeu, le rejet d'un fichier étranger, l'en-tête de formatage d'une BRAM neuve et la détection des écritures en BRAM.
