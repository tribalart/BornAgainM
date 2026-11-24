using System;
using System.Linq;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppZero.Game.Shared;
using Il2CppRonin.Model.Simulation;
using Il2CppRonin.Model.Simulation.Components;
using Il2CppRonin.Model.Enums;
using Il2CppInterop.Runtime;
using Il2CppRonin.Model.Data;
using Il2CppRonin.Model.GameActions;
using Il2CppSystem.Linq;
using Il2CppRonin.Model.Definitions;
using Il2CppZero.Game.Client;

namespace BornAgainM
{
    /// <summary>
    /// Personal DPS meter. Toggled with NumPad +, it records the local player's attacks for a fixed
    /// window by scanning the simulation's attack buffer, then posts a per-player summary to the game
    /// chat. Self-contained: own its hotkey, recording state and chat output.
    /// </summary>
    internal class DpsMeter
    {
        private bool isRecording;
        private float startTime;
        private const float DPS_DURATION = 20f;
        private readonly HashSet<uint> processedAttacks = new();
        private readonly Dictionary<uint, PlayerDPS> players = new();
        private static Il2CppSystem.Reflection.FieldInfo _attacksField;

        // Resolved once when a recording starts: the meter only tracks the local player, so there is
        // no need to re-find it or scan every Character each frame.
        private uint localEntityId;
        private string localName = "";

        /// <summary>One recorded attack and its descriptor details.</summary>
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

        /// <summary>Accumulated attacks for one player, with derived DPS stats.</summary>
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

        /// <summary>Per-frame: handle the NumPad + toggle, stop automatically after the window, and
        /// capture new attacks while recording.</summary>
        public void Update()
        {
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

        /// <summary>Start a DPS recording window, resolving the local player once up front.</summary>
        private void StartDPS()
        {
            var local = ResolveLocalPlayer();
            if (local == null)
            {
                MelonLogger.Msg("DPS Meter: local player not found");
                PostChat("DPS Meter: local player not found.");
                return;
            }

            try { localEntityId = local.EntityId; } catch { localEntityId = 0; }
            string nm = null;
            try { nm = local.EntityName; } catch { }
            if (string.IsNullOrEmpty(nm)) { try { nm = $"Player_{local.PlayerId}"; } catch { nm = "Player"; } }
            localName = nm;

            isRecording = true;
            startTime = Time.time;
            processedAttacks.Clear();
            players.Clear();

            MelonLogger.Msg("=== DPS / COMBAT METER STARTED ===");
            PostChat("=== DPS STARTED for 20 Sec ===");
        }

        /// <summary>Resolve the local player: the authoritative controlled entity, falling back to
        /// the first Character in the scene if that is unavailable.</summary>
        private static Character ResolveLocalPlayer()
        {
            var world = World.Instance;
            Character c = null;
            if (world != null)
            {
                try { c = world._controlledEntity?.TryCast<Character>(); } catch { }
            }
            if (c == null)
            {
                try { c = GameObject.FindObjectOfType<Character>(); } catch { }
            }
            return c;
        }

        /// <summary>Scan the simulation's attack buffer and record the local player's new attacks.
        /// Only the local player is tracked, so an attack is ours exactly when its OwnerId matches
        /// the entity id resolved at <see cref="StartDPS"/> — no per-frame or per-attack scene scan.</summary>
        private void CaptureAttacks()
        {
            try
            {
                var sim = World.Instance?.Simulation;
                if (sim == null) return;

                if (_attacksField == null)
                {
                    var type = Il2CppSystem.Type.GetTypeFromHandle(Il2CppType.Of<Simulation>().TypeHandle);
                    _attacksField = type.GetField("_attacks",
                        (Il2CppSystem.Reflection.BindingFlags)((int)Il2CppSystem.Reflection.BindingFlags.NonPublic | (int)Il2CppSystem.Reflection.BindingFlags.Instance)
                    );
                }

                if (_attacksField == null) return;

                var fieldValue = _attacksField.GetValue(sim);
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
                    if (atk.OwnerId != localEntityId) continue;   // only the local player's attacks

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

                    RegisterAttack(localEntityId, attackInfo);
                    processedAttacks.Add(atk.Id);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in CaptureAttacks: {ex.Message}");
            }
        }

        /// <summary>Add an attack to the (single, local) player's record.</summary>
        private void RegisterAttack(uint ownerId, AttackInfo attackInfo)
        {
            if (!players.TryGetValue(ownerId, out var p))
            {
                p = new PlayerDPS { Name = localName, OwnerId = ownerId };
                players[ownerId] = p;
            }

            p.Attacks.Add(attackInfo);
        }

        /// <summary>Stop recording and post the per-player results to the game chat.</summary>
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

        /// <summary>Post a message to the party chat.</summary>
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
}
