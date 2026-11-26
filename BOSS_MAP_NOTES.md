# Boss Map — notes de maintenance

Fichiers : `LootMapNative.cs` (carte + bandeau de loot + infobulle), `LootTableData.cs` (table des drops).

## Fonctionnement en jeu (aucun fichier externe)
- **Carte du monde** : lue une fois au lancement dans `Data\overworld.map` (`Data.OpenRead` + `MapFile.Read`),
  dès que `Library.Loaded`. Indépendante de la scène courante (le jeu, lui, ne charge que la carte de la scène).
  Couleurs calculées comme `MinimapTexture.GetColor` : MeanColor de l'objet (sauf `HideMinimap`) mélangée au
  sol par `TransparentRatio`, sinon MeanColor du sol. Texel (x+1, y+1) = tuile (x, y) relative à
  `Header.ContentCoords`. Texture en `HideFlags.HideAndDontSave` (sinon détruite par `UnloadUnusedAssets`).
- **Boss de la carte** : SpawnZones du fichier carte, même règle que `BossSpawnTable` (zone à un seul type,
  `MinionRequirement != 0`, sans verrou). Le log affiche la liste : `[LootMapNative] world map loaded: ...`.
- **Sprites, noms, infobulle** : toujours relus dans le jeu (`GetDefinition`, `GetDisplaySprite`,
  `Tooltip.Builder.Build` + `TooltipExtensions.BuildItem(ItemValue(type, 1))`, fermeture `builder.Return`).
- Le client ne contient **aucune table de drop** (`LootData` vide, rien dans `packed.json`) : la liste
  boss → objets est saisie à la main dans `LootTableData.cs`.

## Données codées en dur (à revoir après une mise à jour du jeu)
| Où | Quoi |
|---|---|
| `LootTableData.Drops` | boss type → sacs → item types. Index 0 Mythic Chest, 1 Rare Chest, 2 Pet Basket, 3 Hat Bag, 4 Crystal Box, 5 (optionnel) Exalted Chest. |
| `LootMapNative.TIER_BAGS` | ContainerDefinition de chaque sac : 9741, 9737, 9739, 9738, 9735, 9742. |
| `LootMapNative.TIER_ORDER` | ordre d'affichage : Exalted puis 1..5. |
| `LootMapNative.SharedSpawns` | spawner de la carte → boss affichés (par **nom** de définition) : `Peaks Spawner` → Grug-Mant + Vika-Minci ; `Hydra` → Hydra Head. |
| `LootMapNative.ExtraBosses` | boss absents des SpawnZones (« event »), placés à la main en tuiles : Djinn (290,322), Mysterious Head (563,315), Giant Boar (398,303). |
| `LootMapNative.DungeonBosses` | colonne en haut à gauche : Lord Hammurabi, Lord Cicero, Lady Valeria, Mannah, Boggo. |

## Règle des sacs (vérifiée sur toute la table)
Le sac se déduit de la rareté (`ItemDefinition.Rarity`) : Exalted → Exalted Chest, Mythic → Mythic Chest,
Rare → Rare Chest ; sinon par type : pet → Pet Basket, chapeau → Hat Bag, cristal → Crystal Box.
Un objet qui ne respecte pas cette règle dans la table est probablement une erreur de saisie.

## Ajouter / corriger un drop
1. Retrouver l'ID (type) de l'objet à partir de son nom en jeu.
2. Choisir le sac selon la règle ci-dessus, éditer `LootTableData.cs` (garder le nom en commentaire).
3. Fermer le jeu (la DLL est verrouillée) puis `dotnet build -c Release`.

## Placer un boss à la main (ExtraBosses)
Prendre une capture de la Boss Map, repérer deux pastilles de coordonnées connues (ex. Djinn et Mysterious
Head), en déduire l'échelle s (px/tuile) et l'origine : `x_écran = a + (x + 1,5)·s`,
`y_écran = b − (y + 1,5)·s` ; inverser pour le point voulu. Précision ≈ 1 tuile.

## Origine de la table actuelle
Établie hors jeu à partir de l'image « loottable » de Wys (boss du monde) par comparaison automatique des
icônes avec les sprites du jeu, puis corrigée à la main en jeu (Sand Eater,
Bhognin, Djinn, Giant Boar, Grug/Vika…). Boss de donjon ajoutés d'après la rareté des objets.
Non couverts volontairement : boss d'événement saisonniers, objets hors jeu / test.
