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
| `LootTableData.Drops` | boss type → liste d'item types (le sac n'est **pas** stocké, il est calculé en jeu). |
| `LootMapNative.TIER_BAGS` | ContainerDefinition de chaque sac : 0 Mythic 9741, 1 Rare 9737, 2 Pet Basket 9739, 3 Hat Bag 9738, 4 Crystal Box 9735, 5 Exalted 9742, 6 Epic 9736. |
| `LootMapNative.TIER_ORDER` | ordre d'affichage : Exalted, Mythic, Epic, Rare, Pet, Hat, Crystal. |
| `LootMapNative.SharedSpawns` | spawner de la carte → boss affichés (par **nom** de définition) : `Peaks Spawner` → Grug-Mant + Vika-Minci ; `Hydra` → Hydra Head. |
| `LootMapNative.ExtraBosses` | boss absents des SpawnZones (« event »), placés à la main en tuiles : Djinn (290,322), Mysterious Head (563,315), Giant Boar (398,303). |
| `LootMapNative.DungeonBosses` | colonne en haut à gauche : Lord Hammurabi, Lord Cicero, Lady Valeria, Mannah, Boggo. |

## Règle des sacs (calculée en jeu : `LootMapNative.BagOf`)
Rareté (`ItemDefinition.Rarity`) : Exalted → Exalted Chest, Mythic → Mythic Chest, Epic → Epic Chest,
Rare → Rare Chest ; sinon par type : `PetDefinition` → Pet Basket, `HatDefinition` → Hat Bag, le reste
(cristaux) → Crystal Box. Calcul fait une seule fois pour toute la table (cache `lootRows`, ainsi que
sprites et noms) ; le log signale les objets non-cristaux tombés dans la Crystal Box
(`[LootMapNative] loot table: ...`).

## Ajouter / corriger un drop
1. Retrouver l'ID (type) de l'objet à partir de son nom en jeu.
2. L'ajouter à la liste du boss dans `LootTableData.cs` (garder le nom en commentaire) ; le sac est automatique.
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
