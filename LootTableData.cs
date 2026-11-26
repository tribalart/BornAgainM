using System.Collections.Generic;

namespace BornAgainM
{
    /// <summary>
    /// Boss loot table: boss type -> bag tiers 1..5 (index 0..4: Mythic Chest, Rare Chest, Pet Basket, Hat Bag,
    /// Crystal Box), plus an optional index 5 (Exalted Chest) -> item types. Names and sprites are
    /// resolved in game (LibraryExtensions.GetDefinition); the names in comments are for reading only.
    /// </summary>
    internal static class LootTableData
    {
        public const int TIERS = 6;   // 5 = Exalted Chest (dungeon lords), optional

        public static readonly Dictionary<ushort, ushort[][]> Drops = new()
        {
            // Dungeon lords (bag from the item rarity: Exalted -> Exalted Chest, Mythic -> Mythic Chest; helms -> Hat Bag).
            [21760] = new ushort[][]   // Lord Hammurabi
            {
                new ushort[] { 2578, 1090 },   // Hammurabi's Safeguard, Jade Bull Armor
                new ushort[] {  },
                new ushort[] {  },
                new ushort[] { 45121 },   // Lord Hammurabi's Helm
                new ushort[] {  },
                new ushort[] { 341, 277 },   // Hammurabi's Judgement, Hammurabi's Verdict
            },
            [22016] = new ushort[][]   // Lord Cicero
            {
                new ushort[] { 283, 3386 },   // Executioner's Blade, Cicero's Agony
                new ushort[] {  },
                new ushort[] {  },
                new ushort[] { 45155 },   // Lord Cicero's Helm
                new ushort[] {  },
                new ushort[] { 376, 2582 },   // Cicero's Torment, Cicero's Punishment
            },
            [22080] = new ushort[][]   // Lady Valeria
            {
                new ushort[] { 348 },   // Paladin's Piercing Staff
                new ushort[] {  },
                new ushort[] {  },
                new ushort[] { 45176 },   // Lady Valeria's Helm
                new ushort[] {  },
                new ushort[] { 314, 531 },   // Valeria's Vindication, Valeria's Severance
            },
            [21817] = new ushort[][]   // Mannah the Malevolent
            {
                new ushort[] { 284 },   // Mannah's Soul Crux
                new ushort[] {  },
                new ushort[] {  },
                new ushort[] { 45110 },   // Mannah's Helm
                new ushort[] {  },
            },
            [20950] = new ushort[][]   // Boggo the Brave
            {
                new ushort[] {  },
                new ushort[] { 3395 },   // Boggo's Medal
                new ushort[] {  },
                new ushort[] {  },
                new ushort[] {  },
            },
            [21636] = new ushort[][]   // Grug-Mant
            {
                new ushort[] { 2577, 1092, 275 },   // Golden Hammer, Golden Plate Mail, Attuned Blade
                new ushort[] { 1148 },   // Golden Greaves
                new ushort[] { 47116 },   // Pet Yak
                new ushort[] { 45096 },   // Grug Mant's Helm
                new ushort[] { 1288 },   // Onyx Crystal
            },
            [21637] = new ushort[][]   // Vika-Minci
            {
                new ushort[] { 3377, 371, 1058 },   // Amulet of the Fallen Queen, Bloodletter Dagger, Vika Minci's Robe
                new ushort[] {  },
                new ushort[] { 47116 },   // Pet Yak
                new ushort[] { 45098 },   // Vika Minci's Helm
                new ushort[] { 1288 },   // Onyx Crystal
            },
            [21640] = new ushort[][]   // Umbra
            {
                new ushort[] { 490, 2387, 1062 },   // Umbra's Bow, Hunter's Trap, Hunter's Cloak
                new ushort[] { 1829, 377 },   // Hunter's Horn, Hunter's Knife
                new ushort[] { 47139 },   // Pet Yak Umbra
                new ushort[] { 45138 },   // Umbra's Wig
                new ushort[] { 1285 },   // Topaz Crystal
            },
            [21642] = new ushort[][]   // Mountain Dragon
            {
                new ushort[] { 307, 404 },   // Dragonsbane, Dragonclaw Tekko
                new ushort[] {  },
                new ushort[] { 47122 },   // Pet Dragon
                new ushort[] {  },
                new ushort[] { 1283 },   // Ruby Crystal
            },
            [21394] = new ushort[][]   // Pirate Captain
            {
                new ushort[] { 3379 },   // Amulet of the Pirate Captain
                new ushort[] { 1621, 525 },   // Pirate Cannon, Pirate's Throwing Shiv
                new ushort[] { 47117 },   // Pet Puffin
                new ushort[] {  },
                new ushort[] { 1283 },   // Ruby Crystal
            },
            [21385] = new ushort[][]   // Wapo Helmsman
            {
                new ushort[] { 493, 1059 },   // Puffinspike Bow, Helmsman's Vesture
                new ushort[] { 2130 },   // Tamed Puffin
                new ushort[] { 47117 },   // Pet Puffin
                new ushort[] { 45139, 45087 },   // Puffin Cap, Wapo Hat
                new ushort[] { 1288 },   // Onyx Crystal
            },
            [21380] = new ushort[][]   // Kitsune Momoko
            {
                new ushort[] { 2132, 1824 },   // Kitsune Charm, Kitsune Scroll
                new ushort[] { 1057, 1149 },   // Demonfox Kimono, Kitsune Tabi
                new ushort[] { 47113 },   // Pet Kitsune
                new ushort[] { 45128 },   // Kitsune Mask
                new ushort[] { 1285 },   // Topaz Crystal
            },
            [21130] = new ushort[][]   // Yoku-Ba
            {
                new ushort[] { 1061 },   // Yoku Garb
                new ushort[] { 1620 },   // Yoku Shield
                new ushort[] { 47118 },   // Pet Fire Sprite
                new ushort[] { 45083 },   // Yoku Hair Ties
                new ushort[] { 1288 },   // Onyx Crystal
            },
            [21133] = new ushort[][]   // Hahn
            {
                new ushort[] { 343, 2131 },   // Hahn's Glaive, Magma Grenades
                new ushort[] { 1627, 3376 },   // Gorgon Lava Bucket, Amulet of Tempered Obsidian
                new ushort[] { 47119 },   // Pet Salamander
                new ushort[] { 45082 },   // Gorgon Mustache
                new ushort[] { 1285 },   // Topaz Crystal
            },
            [21126] = new ushort[][]   // Phoenix
            {
                new ushort[] { 523, 1093 },   // Feathertip Kunai, Flamefeather Armor
                new ushort[] { 1136, 285 },   // Phoenix Feather Boots, Flametalon Blade
                new ushort[] { 47121 },   // Pet Phoenix
                new ushort[] { 45142 },   // Phoenix Crown
                new ushort[] { 1283 },   // Ruby Crystal
            },
            [21281] = new ushort[][]   // Forest Guardian
            {
                new ushort[] {  },
                new ushort[] { 3381, 1139, 1056 },   // Amulet of the Bamboo Spirit, Forest Guardian Tabi, Forest Guardian Cloak
                new ushort[] { 47110 },   // Pet Forest Sprite
                new ushort[] { 45075 },   // Forest Guardian's Mask
                new ushort[] { 1287 },   // Sapphire Crystal
            },
            [21252] = new ushort[][]   // Akuji Saikami
            {
                new ushort[] {  },
                new ushort[] { 1631, 1070, 337, 2128, 304, 1625 },   // Akuji Throwing-Bombs, Akuji Grand Robes, Akuji Grand Staff, Smoke Bomb, Akuji Katana, Poison Blowgun
                new ushort[] { 47125 },   // Pet Boar
                new ushort[] { 45073 },   // Saikami's Hat
                new ushort[] { 1287 },   // Sapphire Crystal
            },
            [21274] = new ushort[][]   // Hill Giant Shaman
            {
                new ushort[] {  },
                new ushort[] { 1826, 338 },   // Shaman Skull Bombs, Chieftain Mauler
                new ushort[] { 47125 },   // Pet Boar
                new ushort[] { 45076 },   // Hill Giant Braids
                new ushort[] { 1286 },   // Emerald Crystal
            },
            [21266] = new ushort[][]   // Bhognin
            {
                new ushort[] {  },
                new ushort[] { 2391, 530, 1102 },   // Ball and Chain, Chain Kunai, Chainlink Raiment
                new ushort[] { 47115 },   // Pet Wolf
                new ushort[] { 45175 },   // Bald Cap
                new ushort[] { 1284 },   // Amber Crystal
            },
            [21015] = new ushort[][]   // Bubra Elephant
            {
                new ushort[] {  },
                new ushort[] { 2584, 2385, 1138 },   // Elephant Tusk, Bubra Hookshot, Elephant Boots
                new ushort[] { 47106 },   // Pet Snake
                new ushort[] { 45080 },   // Bubra Earrings
                new ushort[] { 1287 },   // Sapphire Crystal
            },
            [21019] = new ushort[][]   // Queoti Queen
            {
                new ushort[] {  },
                new ushort[] { 3378, 1137, 280 },   // Amulet of the Queoti's Boon, Sandshroud Tabi, Queoti Toxinblade
                new ushort[] { 47111 },   // Pet Hula
                new ushort[] { 45081 },   // Queoti Braids
                new ushort[] { 1286 },   // Emerald Crystal
            },
            [21011] = new ushort[][]   // Sand Eater
            {
                new ushort[] {  },
                new ushort[] { 3383, 1088, 373 },   // Amulet of the Leviathan's Tooth, Dune Hide Armor, Razordrill Dagger
                new ushort[] { 47108 },   // Pet Dune Worm
                new ushort[] {  },
                new ushort[] { 1284 },   // Amber Crystal
            },
            [21510] = new ushort[][]   // Tiko Tikatu
            {
                new ushort[] {  },
                new ushort[] { 1825, 2386 },   // Tiko Totem, Tiko Bongos
                new ushort[] { 47124 },   // Pet Monkey
                new ushort[] { 45092 },   // Tiko Mask
                new ushort[] { 1287 },   // Sapphire Crystal
            },
            [21533] = new ushort[][]   // Gorilla King
            {
                new ushort[] {  },
                new ushort[] { 492, 1628, 526 },   // Jungle Bow, Thorn-tipped Blossoms, Banana Kunai
                new ushort[] { 47120 },   // Pet Toucan
                new ushort[] { 45056 },   // King's Crown
                new ushort[] { 1286 },   // Emerald Crystal
            },
            [21513] = new ushort[][]   // Gushna
            {
                new ushort[] {  },
                new ushort[] { 3382, 1068, 401 },   // Amulet of Elemental Fury, Elemental Weave, Rakasha Tekko
                new ushort[] { 47120 },   // Pet Toucan
                new ushort[] { 45088 },   // Gushna's Crown
                new ushort[] { 1284 },   // Amber Crystal
            },
            [21294] = new ushort[][]   // Giant Boar
            {
                new ushort[] { 313 },   // Kingfall Katana
                new ushort[] { 529 },   // Boar Tusk Kunai
                new ushort[] { 47110 },   // Pet Forest Sprite
                new ushort[] { 45158 },   // Boar Fur Hat
                new ushort[] { 1284, 1287, 1286 },   // Amber Crystal, Sapphire Crystal, Emerald Crystal
            },
            [21025] = new ushort[][]   // Djinn
            {
                new ushort[] { 1629 },   // Cloud in a Bottle
                new ushort[] { 3389 },   // Heart of the Dunes
                new ushort[] { 47111 },   // Pet Hula
                new ushort[] { 45162 },   // Djinn's Crown
                new ushort[] { 1284, 1287, 1286 },   // Amber Crystal, Sapphire Crystal, Emerald Crystal
            },
            [21539] = new ushort[][]   // Mysterious Head
            {
                new ushort[] { 3388 },   // Mysterious Eye
                new ushort[] { 2388 },   // Mysterious Idol
                new ushort[] { 47141 },   // Pet Mysterious Idol
                new ushort[] { 45160 },   // Stone Dome
                new ushort[] { 1284, 1287, 1286 },   // Amber Crystal, Sapphire Crystal, Emerald Crystal
            },
            [20888] = new ushort[][]   // Giant Gloop
            {
                new ushort[] {  },
                new ushort[] { 306, 340, 274, 522, 2135, 400, 370 },   // Gloop-Covered Katana, Gloop-Covered Staff, Gloop-Covered Sword, Gloop-Covered Kunai, Gloop Blob, Gloop-Covered Tekko, Gloop-Covered Dagger
                new ushort[] { 47105, 47104 },   // Pet Green Gloop, Pet Black Gloop
                new ushort[] { 45141 },   // Gloop Goop
                new ushort[] {  },
            },
            [20867] = new ushort[][]   // Kobukin King
            {
                new ushort[] {  },
                new ushort[] { 1101, 272 },   // Kobukin Studded Armor, Kobukin Club
                new ushort[] { 47140 },   // Pet Kobukin Servant
                new ushort[] { 45058 },   // Kobukin Crown
                new ushort[] {  },
            },
            [20626] = new ushort[][]   // Mummunt
            {
                new ushort[] {  },
                new ushort[] { 1089, 2390 },   // Spiked Shoulderguards, Boulder Bracers
                new ushort[] { 47137 },   // Pet Jumping Spider
                new ushort[] { 45063 },   // Mummunt's Hat
                new ushort[] {  },
            },
            [20628] = new ushort[][]   // Stone Giant
            {
                new ushort[] {  },
                new ushort[] { 403 },   // Golem Gauntlets
                new ushort[] { 47114 },   // Pet Stone Crab
                new ushort[] { 45131 },   // Stone Giant's Head
                new ushort[] {  },
            },
            [20622] = new ushort[][]   // Dokai Chancellor
            {
                new ushort[] {  },
                new ushort[] { 336, 1069 },   // Chancellor's Staff, Chancellor's Cloak
                new ushort[] { 47137 },   // Pet Jumping Spider
                new ushort[] { 45061 },   // Chancellor's Mask
                new ushort[] {  },
            },
            [20751] = new ushort[][]   // Shroom Conglomerate
            {
                new ushort[] {  },
                new ushort[] { 2585, 1626, 1150 },   // Shroom Bulb, Spore Stem, Spore Tabi
                new ushort[] { 47107 },   // Pet Toad
                new ushort[] { 45065 },   // Shroom Cap
                new ushort[] {  },
            },
            [20745] = new ushort[][]   // Pogger Beast Tamer
            {
                new ushort[] {  },
                new ushort[] { 2384, 368 },   // Pogger Beast Barrel, Pogger Beast Blade
                new ushort[] { 47107 },   // Pet Toad
                new ushort[] { 45066 },   // Pogger Disguise
                new ushort[] {  },
            },
            [20752] = new ushort[][]   // Hydra Head
            {
                new ushort[] {  },
                new ushort[] { 1616, 1100 },   // Head of the Hydra, Hydra Scale Armor
                new ushort[] { 47112 },   // Pet Kage
                new ushort[] { 45150 },   // Hydra Head
                new ushort[] {  },
            },
            [20893] = new ushort[][]   // Mounted Knight
            {
                new ushort[] {  },
                new ushort[] { 496, 346, 1097 },   // Knight's Bow, Knight's Spear, Knight's Chainmail
                new ushort[] { 47105, 47104 },   // Pet Green Gloop, Pet Black Gloop
                new ushort[] { 45157 },   // Mounted Knight's Helm
                new ushort[] {  },
            },
            [20641] = new ushort[][]   // Warring Ox
            {
                new ushort[] {  },
                new ushort[] { 2583, 1066 },   // Stone Mallet, Barbaric Straps
                new ushort[] { 47114 },   // Pet Stone Crab
                new ushort[] { 45161 },   // Ox Horns
                new ushort[] {  },
            },
            [20773] = new ushort[][]   // Bullfrog Shaman
            {
                new ushort[] {  },
                new ushort[] { 3387, 1827 },   // Fungal Charm, Odd Mushrooms
                new ushort[] { 47107 },   // Pet Toad
                new ushort[] { 45159 },   // Bullfrog Cap
                new ushort[] {  },
            },
        };
    }
}
