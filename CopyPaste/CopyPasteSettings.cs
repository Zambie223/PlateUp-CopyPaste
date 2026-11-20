using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CopyPaste
{
    /// <summary>
    /// The mod's settings, kept in CopyPaste.cfg next to the game's Player.log. The file can be
    /// edited by hand (read at startup) or changed in-game through Mod List's options page,
    /// which saves it immediately. Mod List isn't required.
    ///
    /// Only the host's file matters: the host carries out everyone's copy/paste and draws the
    /// feedback on its own screen.
    /// </summary>
    internal static class CopyPasteSettings
    {
        private const string FileName = "CopyPaste.cfg";
        private const string KeyEnabled = "enabled";
        private const string KeyHoldMs = "hold_to_copy_ms";
        private const string KeyPopups = "show_popups";
        private const string KeyPreview = "show_preview";
        private const int KeyCount = 4;

        public const int MinHoldMs = 200;
        public const int MaxHoldMs = 2000;
        public const int HoldMsStep = 50;

        public static bool Enabled = true;

        /// <summary>
        /// How long the button must be held to copy; shorter presses paste. 500 ms is about the
        /// hold vanilla uses to switch Advanced Build Mode on and off with ping.
        /// </summary>
        public static int HoldToCopyMs = 500;

        /// <summary>"Copied" / "Pasted" / "Nothing to copy" labels over the appliance.</summary>
        public static bool ShowPopups = true;

        /// <summary>Ghost of the copied setting over a matching appliance under the crane.</summary>
        public static bool ShowPreview = true;

        public static float HoldToCopySeconds => HoldToCopyMs / 1000f;

        private static string ConfigPath => Path.Combine(Application.persistentDataPath, FileName);

        public static void Load()
        {
            string path = ConfigPath;
            try
            {
                if (!File.Exists(path))
                {
                    Save();
                    return;
                }
                HashSet<string> seen = new HashSet<string>();
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();
                    if (Apply(key, value))
                    {
                        seen.Add(key);
                    }
                    else
                    {
                        ModMain.LogWarning($"{FileName}: ignoring '{key}={value}'");
                    }
                }
                if (seen.Count < KeyCount)
                {
                    // A key was missing, invalid or out of range (or the file is from an older
                    // build): rewrite it with every key, keeping the values that were read.
                    Save();
                }
            }
            catch (Exception e)
            {
                ModMain.LogWarning($"Couldn't read {FileName}, using defaults: {e.Message}");
            }
        }

        private static bool Apply(string key, string value)
        {
            switch (key)
            {
                case KeyEnabled:
                    return TryBool(value, ref Enabled);
                case KeyPopups:
                    return TryBool(value, ref ShowPopups);
                case KeyPreview:
                    return TryBool(value, ref ShowPreview);
                case KeyHoldMs:
                    if (int.TryParse(value, out int ms))
                    {
                        HoldToCopyMs = ClampHold(ms);
                        return HoldToCopyMs == ms;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static bool TryBool(string value, ref bool target)
        {
            if (bool.TryParse(value, out bool b))
            {
                target = b;
                return true;
            }
            return false;
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(ConfigPath,
                    "# Copy Paste settings. Edit while the game is closed, or change them in-game with the Mod List mod.\n" +
                    "# In Advanced Build Mode, hold the ready-up button (K / Y) on an appliance to copy its settings,\n" +
                    "# and tap it on another appliance of the same type to paste them. Only the host's settings count.\n" +
                    "\n" +
                    "# On or off (true/false).\n" +
                    $"{KeyEnabled}={B(Enabled)}\n" +
                    "\n" +
                    $"# How long the button must be held to copy, in milliseconds ({MinHoldMs} to {MaxHoldMs}). Shorter presses paste.\n" +
                    $"{KeyHoldMs}={HoldToCopyMs}\n" +
                    "\n" +
                    "# Feedback on the host's screen (true/false): labels when copying or pasting, and a preview of\n" +
                    "# the copied setting over a matching appliance under the crane.\n" +
                    $"{KeyPopups}={B(ShowPopups)}\n" +
                    $"{KeyPreview}={B(ShowPreview)}\n");
            }
            catch (Exception e)
            {
                ModMain.LogWarning($"Couldn't save {FileName}: {e.Message}");
            }
        }

        private static string B(bool b) => b ? "true" : "false";

        public static int ClampHold(int ms)
        {
            ms = Math.Max(MinHoldMs, Math.Min(MaxHoldMs, ms));
            // Snap to the step so the file and Mod List's picker agree.
            return MinHoldMs + (int)Math.Round((ms - MinHoldMs) / (double)HoldMsStep) * HoldMsStep;
        }

        public static string Describe()
        {
            return $"{(Enabled ? "enabled" : "disabled")}, hold {HoldToCopyMs} ms to copy, " +
                   $"popups {(ShowPopups ? "on" : "off")}, preview {(ShowPreview ? "on" : "off")}";
        }
    }
}
