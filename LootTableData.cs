using System.Collections.Generic;

namespace BornAgainM
{
    /// <summary>
    /// Boss loot table: boss type -> item types. The bag of each item is not stored: it is computed in game from
    /// the item definition (LootMapNative.BagOf: rarity, pet, hat). Names and sprites are also resolved in game;
    /// the names in comments are for reading only.
    /// </summary>
    internal static class LootTableData
    {
        public static readonly Dictionary<ushort, ushort[]> Drops = new()
        {
            // Lord Hammurabi: Hammurabi's Safeguard, Jade Bull Armor, Lord Hammurabi's Helm, Hammurabi's Judgement, Hammurabi's Verdict
            [21760] = new ushort[] { 2578, 1090, 45121, 341, 277 },
            // Lord Cicero: Executioner's Blade, Cicero's Agony, Lord Cicero's Helm, Cicero's Torment, Cicero's Punishment
            [22016] = new ushort[] { 283, 3386, 45155, 376, 2582 },
            // Lady Valeria: Paladin's Piercing Staff, Lady Valeria's Helm, Valeria's Vindication, Valeria's Severance
            [22080] = new ushort[] { 348, 45176, 314, 531 },
            // Mannah the Malevolent: Mannah's Soul Crux, Mannah's Helm
            [21817] = new ushort[] { 284, 45110 },
            // Boggo the Brave: Boggo's Medal
            [20950] = new ushort[] { 3395 },
            // Grug-Mant: Golden Hammer, Golden Plate Mail, Attuned Blade, Golden Greaves, Pet Yak, Grug Mant's Helm, Onyx Crystal
            [21636] = new ushort[] { 2577, 1092, 275, 1148, 47116, 45096, 1288 },
            // Vika-Minci: Amulet of the Fallen Queen, Bloodletter Dagger, Vika Minci's Robe, Pet Yak, Vika Minci's Helm, Onyx Crystal
            [21637] = new ushort[] { 3377, 371, 1058, 47116, 45098, 1288 },
            // Umbra: Umbra's Bow, Hunter's Trap, Hunter's Cloak, Hunter's Horn, Hunter's Knife, Pet Yak Umbra, Umbra's Wig, Topaz Crystal
            [21640] = new ushort[] { 490, 2387, 1062, 1829, 377, 47139, 45138, 1285 },
            // Mountain Dragon: Dragonsbane, Dragonclaw Tekko, Pet Dragon, Ruby Crystal
            [21642] = new ushort[] { 307, 404, 47122, 1283 },
            // Pirate Captain: Amulet of the Pirate Captain, Pirate Cannon, Pirate's Throwing Shiv, Pet Puffin, Ruby Crystal
            [21394] = new ushort[] { 3379, 1621, 525, 47117, 1283 },
            // Wapo Helmsman: Puffinspike Bow, Helmsman's Vesture, Tamed Puffin, Pet Puffin, Puffin Cap, Wapo Hat, Onyx Crystal
            [21385] = new ushort[] { 493, 1059, 2130, 47117, 45139, 45087, 1288 },
            // Kitsune Momoko: Kitsune Charm, Kitsune Scroll, Demonfox Kimono, Kitsune Tabi, Pet Kitsune, Kitsune Mask, Topaz Crystal
            [21380] = new ushort[] { 2132, 1824, 1057, 1149, 47113, 45128, 1285 },
            // Yoku-Ba: Yoku Garb, Yoku Shield, Pet Fire Sprite, Yoku Hair Ties, Onyx Crystal
            [21130] = new ushort[] { 1061, 1620, 47118, 45083, 1288 },
            // Hahn: Hahn's Glaive, Magma Grenades, Gorgon Lava Bucket, Amulet of Tempered Obsidian, Pet Salamander, Gorgon Mustache, Topaz Crystal
            [21133] = new ushort[] { 343, 2131, 1627, 3376, 47119, 45082, 1285 },
            // Phoenix: Feathertip Kunai, Flamefeather Armor, Phoenix Feather Boots, Flametalon Blade, Pet Phoenix, Phoenix Crown, Ruby Crystal
            [21126] = new ushort[] { 523, 1093, 1136, 285, 47121, 45142, 1283 },
            // Forest Guardian: Amulet of the Bamboo Spirit, Forest Guardian Tabi, Forest Guardian Cloak, Pet Forest Sprite, Forest Guardian's Mask, Sapphire Crystal
            [21281] = new ushort[] { 3381, 1139, 1056, 47110, 45075, 1287 },
            // Akuji Saikami: Akuji Throwing-Bombs, Akuji Grand Robes, Akuji Grand Staff, Smoke Bomb, Akuji Katana, Poison Blowgun, Pet Boar, Saikami's Hat, Sapphire Crystal
            [21252] = new ushort[] { 1631, 1070, 337, 2128, 304, 1625, 47125, 45073, 1287 },
            // Hill Giant Shaman: Shaman Skull Bombs, Chieftain Mauler, Pet Boar, Hill Giant Braids, Emerald Crystal
            [21274] = new ushort[] { 1826, 338, 47125, 45076, 1286 },
            // Bhognin: Ball and Chain, Chain Kunai, Chainlink Raiment, Pet Wolf, Bald Cap, Amber Crystal
            [21266] = new ushort[] { 2391, 530, 1102, 47115, 45175, 1284 },
            // Bubra Elephant: Elephant Tusk, Bubra Hookshot, Elephant Boots, Pet Snake, Bubra Earrings, Sapphire Crystal
            [21015] = new ushort[] { 2584, 2385, 1138, 47106, 45080, 1287 },
            // Queoti Queen: Amulet of the Queoti's Boon, Sandshroud Tabi, Queoti Toxinblade, Pet Hula, Queoti Braids, Emerald Crystal
            [21019] = new ushort[] { 3378, 1137, 280, 47111, 45081, 1286 },
            // Sand Eater: Amulet of the Leviathan's Tooth, Dune Hide Armor, Razordrill Dagger, Pet Dune Worm, Amber Crystal
            [21011] = new ushort[] { 3383, 1088, 373, 47108, 1284 },
            // Tiko Tikatu: Tiko Totem, Tiko Bongos, Pet Monkey, Tiko Mask, Sapphire Crystal
            [21510] = new ushort[] { 1825, 2386, 47124, 45092, 1287 },
            // Gorilla King: Jungle Bow, Thorn-tipped Blossoms, Banana Kunai, Pet Toucan, King's Crown, Emerald Crystal
            [21533] = new ushort[] { 492, 1628, 526, 47120, 45056, 1286 },
            // Gushna: Amulet of Elemental Fury, Elemental Weave, Rakasha Tekko, Pet Toucan, Gushna's Crown, Amber Crystal
            [21513] = new ushort[] { 3382, 1068, 401, 47120, 45088, 1284 },
            // Giant Boar: Kingfall Katana, Boar Tusk Kunai, Pet Forest Sprite, Boar Fur Hat, Amber Crystal, Sapphire Crystal, Emerald Crystal
            [21294] = new ushort[] { 313, 529, 47110, 45158, 1284, 1287, 1286 },
            // Djinn: Cloud in a Bottle, Heart of the Dunes, Pet Hula, Djinn's Crown, Amber Crystal, Sapphire Crystal, Emerald Crystal
            [21025] = new ushort[] { 1629, 3389, 47111, 45162, 1284, 1287, 1286 },
            // Mysterious Head: Mysterious Eye, Mysterious Idol, Pet Mysterious Idol, Stone Dome, Amber Crystal, Sapphire Crystal, Emerald Crystal
            [21539] = new ushort[] { 3388, 2388, 47141, 45160, 1284, 1287, 1286 },
            // Giant Gloop: Gloop-Covered Katana, Gloop-Covered Staff, Gloop-Covered Sword, Gloop-Covered Kunai, Gloop Blob, Gloop-Covered Tekko, Gloop-Covered Dagger, Pet Green Gloop, Pet Black Gloop, Gloop Goop
            [20888] = new ushort[] { 306, 340, 274, 522, 2135, 400, 370, 47105, 47104, 45141 },
            // Kobukin King: Kobukin Studded Armor, Kobukin Club, Pet Kobukin Servant, Kobukin Crown
            [20867] = new ushort[] { 1101, 272, 47140, 45058 },
            // Mummunt: Spiked Shoulderguards, Boulder Bracers, Pet Jumping Spider, Mummunt's Hat
            [20626] = new ushort[] { 1089, 2390, 47137, 45063 },
            // Stone Giant: Golem Gauntlets, Pet Stone Crab, Stone Giant's Head
            [20628] = new ushort[] { 403, 47114, 45131 },
            // Dokai Chancellor: Chancellor's Staff, Chancellor's Cloak, Pet Jumping Spider, Chancellor's Mask
            [20622] = new ushort[] { 336, 1069, 47137, 45061 },
            // Shroom Conglomerate: Shroom Bulb, Spore Stem, Spore Tabi, Pet Toad, Shroom Cap
            [20751] = new ushort[] { 2585, 1626, 1150, 47107, 45065 },
            // Pogger Beast Tamer: Pogger Beast Barrel, Pogger Beast Blade, Pet Toad, Pogger Disguise
            [20745] = new ushort[] { 2384, 368, 47107, 45066 },
            // Hydra Head: Head of the Hydra, Hydra Scale Armor, Pet Kage, Hydra Head
            [20752] = new ushort[] { 1616, 1100, 47112, 45150 },
            // Mounted Knight: Knight's Bow, Knight's Spear, Knight's Chainmail, Pet Green Gloop, Pet Black Gloop, Mounted Knight's Helm
            [20893] = new ushort[] { 496, 346, 1097, 47105, 47104, 45157 },
            // Warring Ox: Stone Mallet, Barbaric Straps, Pet Stone Crab, Ox Horns
            [20641] = new ushort[] { 2583, 1066, 47114, 45161 },
            // Bullfrog Shaman: Fungal Charm, Odd Mushrooms, Pet Toad, Bullfrog Cap
            [20773] = new ushort[] { 3387, 1827, 47107, 45159 },
        };
    }
}
