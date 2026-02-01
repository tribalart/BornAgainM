using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using System.Threading.Tasks;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2Cpp;
using Il2CppZero.Game.Shared;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppRonin.Model.Simulation;
using Il2CppRonin.Model.Data;
using Il2CppRonin.Model.Simulation.Components;
using Il2CppRonin.Model.Structs;
using Il2CppRonin.Model.Enums;
using System.Runtime.InteropServices;

[assembly: MelonInfo(typeof(BornAgainM.Core), "BornAgainM", "2.0.8", "Toi")]
[assembly: MelonGame("Unnamed Studios", "Born Again")]

namespace BornAgainM
{
    public class Core : MelonMod
    {
        private bool sortingInProgress = false;
        private bool stopRequested = false;

        private bool isRecording = false;
        private float startTime;
        private const float DPS_DURATION = 20f;
        private readonly HashSet<uint> processedAttacks = new();
        private readonly Dictionary<uint, PlayerDPS> players = new();
        private PlayersListUI playersListUI;

        // Dictionnaire pour stocker les dégâts de chaque joueur (public static pour le patch)
        public static Dictionary<uint, int> playerDamage = new Dictionary<uint, int>();


        // NOUVEAU : Patch pour tracker les dégâts
        [HarmonyPatch(typeof(WorldObject))]
        [HarmonyPatch(nameof(WorldObject.WorldMessageDamage))]
        public class WorldMessageDamagePatch
        {
            static void Postfix(
                WorldObject __instance,
            int damage,
            bool fromPlayer,
            bool isPlayer,
            bool trueDamage,
            bool criticalStrike
            )
            {
                if (__instance == null || __instance.Pointer == IntPtr.Zero)
                    return;

                if (damage <= 0)
                    return;

                
                Character character = __instance.TryCast<Character>();
                if (character == null)
                    return;

                uint entityId = character.EntityId;

                if (!playerDamage.ContainsKey(entityId))
                    playerDamage[entityId] = 0;

                playerDamage[entityId] += damage;
            }

        }







    private class AttackInfo
        {
            public int Damage;
            public bool IsTrueDamage;
            public uint TargetsHit;
            public float Time;

            public string AttackType;
            public int MinDamage;
            public int MaxDamage;
            public int ArmorDamage;
            public bool Pierces;
            public float Radius;
            public float Speed;
            public string WeaponName;
        }

        private class PlayerDPS
        {
            public string Name;
            public uint OwnerId;
            public List<AttackInfo> Attacks = new();

            public int TotalDamage => Attacks.Sum(a => a.Damage);
            public int HitCount => Attacks.Count;
            public int MaxHit => Attacks.Count > 0 ? Attacks.Max(a => a.Damage) : 0;
            public int MinHit => Attacks.Count > 0 ? Attacks.Min(a => a.Damage) : 0;
            public double AvgHit => Attacks.Count > 0 ? Attacks.Average(a => a.Damage) : 0;
            public int TrueDamageCount => Attacks.Count(a => a.IsTrueDamage);
            public int TrueDamageTotal => Attacks.Where(a => a.IsTrueDamage).Sum(a => a.Damage);
        }

        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("Bank Sorter Loaded (toggle with NumKey6)");
            MelonLogger.Msg("DPS Meter Loaded (toggle with NumKey+)");
            MelonLogger.Msg("Live Player UI (toggle with NumKeyDivide)");

            playersListUI = new PlayersListUI();

            // Appliquer les patches Harmony
            var harmony = new HarmonyLib.Harmony("com.bornagainm.mod");
            harmony.PatchAll();
            MelonLogger.Msg("Harmony patches applied");
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.Keypad6))
            {
                if (sortingInProgress)
                {
                    stopRequested = true;
                    MelonLogger.Msg("Stop requested, finishing current swaps...");
                }
                else
                {
                    MelonLogger.Msg("Starting bank sort...");
                    sortingInProgress = true;
                    stopRequested = false;
                    MelonCoroutines.Start(SortBankCoroutine());
                }
            }
            if (Input.GetKeyDown(KeyCode.Keypad3))
            {
                new BankManager();
            }




            if (Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                if (!isRecording)
                    StartDPS();
                else
                    StopDPS();
            }

            if (Input.GetKeyDown(KeyCode.KeypadDivide))
            {
                playersListUI.ToggleLivePlayerUI();
            }

            if (PlayersListUIState.liveCanvas != null && PlayersListUIState.liveCanvas.activeSelf)
            {
                playersListUI.UpdateLivePlayerUI();
            }

            if (!isRecording) return;

            if (Time.time - startTime >= DPS_DURATION)
            {
                StopDPS();
                return;
            }
            CaptureAttacks();
        }

        public override void OnLateUpdate()
        {
        }

        // ------------------- BANK SORT COROUTINE -------------------
        private IEnumerator SortBankCoroutine()
        {
            const int startIndex = 0;
            var controller = UnityEngine.Object.FindObjectOfType<Il2Cpp.SlotController>();
            if (controller == null)
            {
                MelonLogger.Msg("SlotController not found");
                sortingInProgress = false;
                yield break;
            }

            int currentIndex = startIndex;

            while (!stopRequested)
            {
                var slots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.Slot>()
                    .Where(s => s.Index >= currentIndex)
                    .OrderBy(s => s.Index)
                    .ToArray();

                if (slots.Length == 0)
                    break;

                var itemTypes = slots
                    .Select(s => s.GetItemValue())
                    .Where(i => i.Count > 0)
                    .Select(i => i.Type)
                    .Distinct()
                    .OrderBy(t => t)
                    .ToArray();

                bool swappedAnything = false;

                foreach (var type in itemTypes)
                {
                    if (stopRequested) break;

                    slots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.Slot>()
                        .Where(s => s.Index >= currentIndex)
                        .OrderBy(s => s.Index)
                        .ToArray();

                    var itemsOfType = slots
                        .Select(s => new { Slot = s, Item = s.GetItemValue() })
                        .Where(x => x.Item.Type == type)
                        .OrderBy(x => x.Slot.Index)
                        .ToList();

                    foreach (var pair in itemsOfType)
                    {
                        if (stopRequested) break;

                        var targetSlot = slots.FirstOrDefault(s => s.Index == currentIndex);
                        if (targetSlot == null) continue;

                        if (pair.Slot.Index != targetSlot.Index)
                        {
                            controller.Swap(pair.Slot, targetSlot);
                            MelonLogger.Msg($"Swapped Slot {pair.Slot.Index} with Slot {targetSlot.Index} (ItemType {type})");
                            swappedAnything = true;
                            yield return new WaitForSeconds(UnityEngine.Random.Range(0.25f, 0.30f));
                        }

                        currentIndex++;
                    }
                }

                if (!swappedAnything) break;
            }

            sortingInProgress = false;
            stopRequested = false;
            MelonLogger.Msg("Bank sort completed!");
        }

        private void StartDPS()
        {
            isRecording = true;
            startTime = Time.time;
            processedAttacks.Clear();

            MelonLogger.Msg("=== DPS / COMBAT METER STARTED ===");
            PostChat("=== DPS STARTED for 20 Sec ===");
        }

        private void CaptureAttacks()
        {
            try
            {
                var sim = World.Instance?.Simulation;
                if (sim == null) return;

                var localPlayer = GameObject.FindObjectOfType<Character>();
                if (localPlayer == null) return;
                long localPlayerId = localPlayer.PlayerId;

                var type = Il2CppSystem.Type.GetTypeFromHandle(Il2CppType.Of<Simulation>().TypeHandle);
                var field = type.GetField("_attacks",
                    (Il2CppSystem.Reflection.BindingFlags)((int)Il2CppSystem.Reflection.BindingFlags.NonPublic | (int)Il2CppSystem.Reflection.BindingFlags.Instance)
                );

                if (field == null) return;

                var fieldValue = field.GetValue(sim);
                if (fieldValue == null) return;

                var array = fieldValue.Cast<Il2CppSystem.Array>();
                if (array == null) return;

                for (int i = 0; i < array.Length; i++)
                {
                    var atkObj = array.GetValue(i);
                    if (atkObj == null) continue;

                    var atk = atkObj.Cast<LiveAttack>();
                    if (atk == null) continue;
                    if (atk.Id == 0 || processedAttacks.Contains(atk.Id)) continue;
                    if (atk.Damage <= 0) continue;

                    var character = GameObject.FindObjectsOfType<Character>()
                        .FirstOrDefault(c => c.EntityId == atk.OwnerId);

                    if (character != null && character.PlayerId == localPlayerId)
                    {
                        var descriptor = atk.AttackDescriptor;

                        var attackInfo = new AttackInfo
                        {
                            Damage = atk.Damage,
                            IsTrueDamage = atk.TrueDamage,
                            TargetsHit = (uint)(atk.Hits?.Count ?? 0),
                            Time = Time.time,

                            AttackType = descriptor?.Type.ToString() ?? "Unknown",
                            MinDamage = descriptor?.MinDamage ?? 0,
                            MaxDamage = descriptor?.MaxDamage ?? 0,
                            ArmorDamage = descriptor?.ArmorDamage ?? 0,
                            Pierces = descriptor?.Pierces ?? false,
                            Radius = descriptor?.Radius ?? 0f,
                            Speed = descriptor?.Speed ?? 0f,
                            WeaponName = descriptor?.Origin.ToString() ?? "Unknown",
                        };

                        RegisterAttack(atk.OwnerId, attackInfo);
                        processedAttacks.Add(atk.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in CaptureAttacks: {ex.Message}");
            }
        }

        private void RegisterAttack(uint ownerId, AttackInfo attackInfo)
        {
            if (!players.TryGetValue(ownerId, out var p))
            {
                var character = GameObject.FindObjectsOfType<Character>()
                    .FirstOrDefault(c => c.EntityId == ownerId);
                string name = character != null
                    ? (!string.IsNullOrEmpty(character.EntityName) ? character.EntityName : $"Player_{character.PlayerId}")
                    : $"Entity_{ownerId}";

                p = new PlayerDPS { Name = name, OwnerId = ownerId };
                players[ownerId] = p;
            }

            p.Attacks.Add(attackInfo);
        }

        private void StopDPS()
        {
            if (!isRecording) return;
            isRecording = false;

            if (players.Count == 0)
            {
                PostChat("DPS Meter stopped: No attacks recorded.");
                MelonLogger.Msg("DPS Meter stopped: No attacks recorded.");
                return;
            }

            PostChat("=== DPS Meter Results ===");

            foreach (var kvp in players)
            {
                var p = kvp.Value;
                string msg = $"{p.Name} - Total: {p.TotalDamage} dmg | Hits: {p.HitCount} | Max: {p.MaxHit} | Min: {p.MinHit} | Avg: {p.AvgHit:F1}";
                PostChat(msg);
            }
            foreach (var kvp in players)
            {
                kvp.Value.Attacks.Clear();
            }

            MelonLogger.Msg("DPS Meter stopped - Results posted in game chat");
        }

        private void PostChat(string message)
        {
            try
            {
                var chatContainer = GameObject.FindObjectOfType<ChatContainer>();
                if (chatContainer == null) return;

                var chat = new Chat(
                    ChatType.PartyChat,
                    message,
                    BadgeType.None,
                    new Il2CppSystem.Nullable<int>(),
                    new Il2CppSystem.Nullable<ChatOwnerData>(),
                    new Il2CppSystem.Nullable<ChatIdData>(),
                    RankType.Admin,
                    (ushort)0
                );

                chatContainer.AddChat(chat);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in PostChat: {ex.Message}");
            }
        }

        private async void OnPlayerNameClicked(Character character)
        {
            if (character == null) return;

            try
            {
                MelonLogger.Msg($"=== STATS [{character.EntityName}] ===");

                var entityState = character.GetComponent<EntityState>();

                Stats stats = entityState.StatIncreases;
                int tries = 0;
                while (stats.Strength == 0 && stats.MaxHealth == 0 && tries < 10)
                {
                    await Task.Delay(200);
                    stats = entityState.StatIncreases;
                    tries++;
                }

                if (stats.Strength == 0 && stats.MaxHealth == 0)
                {
                    MelonLogger.Msg("Stats still zero after waiting.");
                }
                else
                {
                    LogStats(stats);
                }

                MelonLogger.Msg("=== COMPONENTS ===");
                var components = character.gameObject.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp != null)
                        MelonLogger.Msg($"  - {comp.GetIl2CppType().FullName}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"OnPlayerNameClicked error: {ex}");
            }
        }

        private void LogStats(Stats stats)
        {
            MelonLogger.Msg($"Strength : {stats.Strength}");
            MelonLogger.Msg($"Dexterity: {stats.Dexterity}");
            MelonLogger.Msg($"Defense  : {stats.Defense}");
            MelonLogger.Msg($"Vigor    : {stats.Vigor}");
            MelonLogger.Msg($"Speed    : {stats.Speed}");
            MelonLogger.Msg($"MaxHP    : {stats.MaxHealth}");
        }
    }

    static class TaskExtensions
    {
        public static void Forget(this Task t) { }
    }
}