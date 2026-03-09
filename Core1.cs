using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppZero.Game.Shared;
using HarmonyLib;
using Il2CppRonin.Model.Simulation;
using Il2CppRonin.Model.Simulation.Components;
using Il2CppRonin.Model.Enums;
using Il2CppInterop.Runtime;
using Il2CppRonin.Model.Data;

[assembly: MelonInfo(typeof(BornAgainM.Core), "BornAgainM", "2.0.9", "Toi")]
[assembly: MelonGame("Unnamed Studios", "Born Again")]

namespace BornAgainM
{

    public class Core : MelonMod
    {
        private SortBank sortBank;

        private bool isRecording = false;
        private float startTime;
        private const float DPS_DURATION = 20f;
        private readonly HashSet<uint> processedAttacks = new();
        private readonly Dictionary<uint, PlayerDPS> players = new();
        private DamageMeterUI damageMeterUI;
        private int initDamageMeterUI = 0;

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
            MelonLogger.Msg("BornAgainM Mod Loaded");
            MelonLogger.Msg("DPS Meter (toggle with NumKey+)");
            MelonLogger.Msg("Damage Meter UI auto-enabled");

            damageMeterUI = new DamageMeterUI();
            damageMeterUI.CreateUI();

            sortBank = new SortBank();

            // Appliquer les patches Harmony
            var harmony = new HarmonyLib.Harmony("com.bornagainm.mod");
            harmony.PatchAll();

            try
            {
                BlessingsPatchRegistrar.Register(harmony);
                MelonLogger.Msg("Harmony patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying Harmony patches: {ex.Message}");
            }
        }

        public override void OnUpdate()
        {
            // Initialiser l'UI au premier update
            if (initDamageMeterUI == 0)
                damageMeterUI.Toggle();
            initDamageMeterUI = 1;

            // Mettre à jour l'UI du Damage Meter
            damageMeterUI.UpdateUI();

            // Gestion du DPS Meter (NumPad+)
            if (Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                if (!isRecording)
                    StartDPS();
                else
                    StopDPS();
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
    }

    static class TaskExtensions
    {
        public static void Forget(this Task t) { }
    }


}