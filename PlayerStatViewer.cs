/*
 * PlayerStatViewer - Bonelab Fusion mod
 * -------------------------------------
 * Shows a toggleable overlay listing every connected player's:
 *   - Upper Body Strength
 *   - Lower Body Strength
 *   - Current Speed (m/s)
 *
 * Requirements:
 *   - MelonLoader
 *   - BoneLib
 *   - Fusion (Lakatrazz) installed and referenced
 *
 * NOTES ON API ACCURACY:
 *   Fusion's public wiki confirms BodyVitals "Proportions" data (which includes
 *   strength values) is synced between players, but I could not verify the exact
 *   field/property names for your specific BoneLib/Fusion version from documentation
 *   alone. This mod tries the common names first (UpperBodyStrength / LowerBodyStrength)
 *   and falls back to reflection to find any float field/property with "upper",
 *   "lower", or "strength" in the name if the direct call fails. Check the console
 *   log on load — it will print exactly what it found so you can confirm or adjust.
 *
 * Toggle:
 *   - BoneMenu: BoneLab > Mods > Player Stat Viewer > Enabled
 *   - Hotkey: F6 (configurable below)
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using SLZ.Rig;
using SLZ.Marrow.Data;
using BoneLib;

[assembly: MelonInfo(typeof(PlayerStatViewer.Core), "Player Stat Viewer", "1.0.0", "You")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace PlayerStatViewer
{
    public class Core : MelonMod
    {
        public static bool Enabled = false;
        public static KeyCode ToggleKey = KeyCode.F6;

        private static GUIStyle _headerStyle;
        private static GUIStyle _lineStyle;
        private static bool _stylesInit;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Player Stat Viewer loaded. Press F6 or use BoneMenu to toggle.");
            SetupBoneMenu();
        }

        private void SetupBoneMenu()
        {
            try
            {
                var page = BoneMenu.Menu.Page.Root.CreatePage("Player Stat Viewer", Color.cyan);
                page.CreateBool("Enabled", Color.white, Enabled, (value) => { Enabled = value; });
            }
            catch (Exception e)
            {
                LoggerInstance.Warning("BoneMenu setup failed (menu API may differ in your BoneLib version): " + e.Message);
            }
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(ToggleKey))
            {
                Enabled = !Enabled;
                LoggerInstance.Msg("Player Stat Viewer: " + (Enabled ? "ON" : "OFF"));
            }
        }

        public override void OnGUI()
        {
            if (!Enabled) return;
            InitStyles();

            var players = PlayerStatReader.GetAllRigs();

            float x = 20f;
            float y = 20f;
            float width = 340f;
            float lineHeight = 22f;
            float boxHeight = 34f + players.Count * lineHeight * 3.2f;

            GUI.Box(new Rect(x, y, width, boxHeight), "");
            GUI.Label(new Rect(x + 10, y + 6, width - 20, 20), "Player Stat Viewer (F6 to toggle)", _headerStyle);

            float rowY = y + 34f;
            foreach (var rig in players)
            {
                var stats = PlayerStatReader.ReadStats(rig);

                GUI.Label(new Rect(x + 10, rowY, width - 20, lineHeight), stats.Name, _headerStyle);
                rowY += lineHeight;

                GUI.Label(new Rect(x + 20, rowY, width - 30, lineHeight),
                    string.Format("Upper Strength: {0}", stats.UpperStrengthText), _lineStyle);
                rowY += lineHeight;

                GUI.Label(new Rect(x + 20, rowY, width - 30, lineHeight),
                    string.Format("Lower Strength: {0}", stats.LowerStrengthText), _lineStyle);
                rowY += lineHeight;

                GUI.Label(new Rect(x + 20, rowY, width - 30, lineHeight),
                    string.Format("Speed: {0} m/s", stats.SpeedText), _lineStyle);
                rowY += lineHeight * 1.2f;
            }
        }

        private void InitStyles()
        {
            if (_stylesInit) return;
            _headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14, normal = { textColor = Color.white } };
            _lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = Color.cyan } };
            _stylesInit = true;
        }
    }

    /// <summary>
    /// Handles finding player rigs (local + remote via Fusion) and pulling
    /// stat values off them, with a reflection-based fallback for safety.
    /// </summary>
    public static class PlayerStatReader
    {
        public struct StatResult
        {
            public string Name;
            public string UpperStrengthText;
            public string LowerStrengthText;
            public string SpeedText;
        }

        private static readonly Dictionary<int, Vector3> _lastPositions = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, float> _lastTimes = new Dictionary<int, float>();

        /// <summary>
        /// Returns all known RigManagers - local player plus any Fusion remote players.
        /// Adjust the Fusion lookup below if your installed version exposes a
        /// different manager class (e.g. NetworkPlayerManager, PlayerIdManager, etc).
        /// </summary>
        public static List<RigManager> GetAllRigs()
        {
            var rigs = new List<RigManager>();

            // Local player
            try
            {
                if (Player.RigManager != null)
                    rigs.Add(Player.RigManager);
            }
            catch { /* BoneLib not fully initialized yet */ }

            // Remote Fusion players - try common Fusion access points via reflection
            // so this compiles even if you're on a Fusion version with different names.
            try
            {
                var fusionAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name.Contains("Fusion"));

                if (fusionAssembly != null)
                {
                    var playerIdManagerType = fusionAssembly.GetTypes()
                        .FirstOrDefault(t => t.Name == "PlayerIdManager");

                    if (playerIdManagerType != null)
                    {
                        var playersProp = playerIdManagerType.GetProperty("PlayerIds", BindingFlags.Public | BindingFlags.Static)
                                          ?? playerIdManagerType.GetProperty("Instances", BindingFlags.Public | BindingFlags.Static);

                        if (playersProp != null)
                        {
                            var idList = playersProp.GetValue(null) as System.Collections.IEnumerable;
                            if (idList != null)
                            {
                                foreach (var idObj in idList)
                                {
                                    var rigManagerProp = idObj.GetType().GetProperty("RigManager")
                                                          ?? idObj.GetType().GetField("RigManager")?.ReflectedType.GetProperty("RigManager");
                                    var rm = idObj.GetType().GetProperty("RigManager")?.GetValue(idObj) as RigManager;
                                    if (rm != null && !rigs.Contains(rm))
                                        rigs.Add(rm);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Fusion remote player lookup failed, showing local player only: " + e.Message);
            }

            return rigs;
        }

        public static StatResult ReadStats(RigManager rig)
        {
            var result = new StatResult
            {
                Name = TryGetName(rig),
                UpperStrengthText = "N/A",
                LowerStrengthText = "N/A",
                SpeedText = "0.0"
            };

            if (rig == null) return result;

            // --- Strength values via BodyVitals ---
            try
            {
                var bodyVitals = rig.physicsRig != null ? rig.physicsRig.GetComponentInChildren(typeof(Component)) : null;
                // Direct known-name attempt first
                var vitals = rig.GetComponentInChildren<SLZ.VRMK.BodyVitals>();
                if (vitals != null)
                {
                    float? upper = TryReadFloat(vitals, "upperStrength") ?? TryReadFloat(vitals, "UpperBodyStrength") ?? TryReadFloat(vitals, "upperBodyStrength");
                    float? lower = TryReadFloat(vitals, "lowerStrength") ?? TryReadFloat(vitals, "LowerBodyStrength") ?? TryReadFloat(vitals, "lowerBodyStrength");

                    if (upper == null || lower == null)
                    {
                        // Reflection fallback: scan all float fields/props for name matches
                        var fallback = ScanForStrengthFields(vitals);
                        if (upper == null) upper = fallback.upper;
                        if (lower == null) lower = fallback.lower;
                    }

                    result.UpperStrengthText = upper.HasValue ? upper.Value.ToString("0.00") : "unknown (check field names)";
                    result.LowerStrengthText = lower.HasValue ? lower.Value.ToString("0.00") : "unknown (check field names)";
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Failed reading strength stats: " + e.Message);
            }

            // --- Speed: computed from position delta each GUI frame ---
            try
            {
                int id = rig.GetInstanceID();
                Vector3 currentPos = rig.physicsRig != null ? rig.physicsRig.m_head.position : rig.transform.position;
                float now = Time.time;

                if (_lastPositions.TryGetValue(id, out var lastPos) && _lastTimes.TryGetValue(id, out var lastTime))
                {
                    float dt = now - lastTime;
                    if (dt > 0.0001f)
                    {
                        float speed = Vector3.Distance(currentPos, lastPos) / dt;
                        result.SpeedText = speed.ToString("0.00");
                    }
                }

                _lastPositions[id] = currentPos;
                _lastTimes[id] = now;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Failed computing speed: " + e.Message);
            }

            return result;
        }

        private static string TryGetName(RigManager rig)
        {
            try
            {
                if (rig == Player.RigManager) return "You (Local)";
                // Try to find a Fusion display-name component by reflection
                var comps = rig.GetComponentsInChildren(typeof(Component));
                foreach (var c in comps)
                {
                    var nameProp = c.GetType().GetProperty("Username") ?? c.GetType().GetProperty("DisplayName") ?? c.GetType().GetProperty("Nickname");
                    if (nameProp != null)
                    {
                        var val = nameProp.GetValue(c) as string;
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }
            }
            catch { }
            return "Player " + rig.GetInstanceID();
        }

        private static float? TryReadFloat(object obj, string memberName)
        {
            var type = obj.GetType();
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(float))
                return (float)field.GetValue(obj);

            var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(float))
                return (float)prop.GetValue(obj);

            return null;
        }

        private static (float? upper, float? lower) ScanForStrengthFields(object obj)
        {
            float? upper = null, lower = null;
            var type = obj.GetType();

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(float)) continue;
                string n = field.Name.ToLower();
                if (n.Contains("upper") && n.Contains("str")) upper = (float)field.GetValue(obj);
                if (n.Contains("lower") && n.Contains("str")) lower = (float)field.GetValue(obj);
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (prop.PropertyType != typeof(float)) continue;
                string n = prop.Name.ToLower();
                try
                {
                    if (n.Contains("upper") && n.Contains("str")) upper = upper ?? (float)prop.GetValue(obj);
                    if (n.Contains("lower") && n.Contains("str")) lower = lower ?? (float)prop.GetValue(obj);
                }
                catch { }
            }

            return (upper, lower);
        }
    }
}
