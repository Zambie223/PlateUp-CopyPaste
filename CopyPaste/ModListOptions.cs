using System;
using System.Collections;
using System.Collections.Generic;

namespace CopyPaste
{
    /// <summary>
    /// In-game options through the Mod List mod (options API v1, Mod List 0.2.0+): Pause → Mods →
    /// the gear on Copy Paste's row. Mod List finds this class by name; there's no reference to it
    /// in either direction, and without Mod List the settings still come from CopyPaste.cfg.
    /// Only the host's values count (see CopyPasteSettings).
    /// </summary>
    public static class ModListOptions
    {
        public static IEnumerable GetOptions()
        {
            return new List<Dictionary<string, object>>
            {
                Bool("enabled", "Enabled",
                    "In Advanced Build Mode, hold the ready-up button on an appliance to copy its settings and tap it on another of the same type to paste. Only the host's setting counts.",
                    () => CopyPasteSettings.Enabled, v => CopyPasteSettings.Enabled = v),
                new Dictionary<string, object>
                {
                    ["id"] = "hold_to_copy_ms",
                    ["type"] = "int",
                    ["label"] = "Hold to copy",
                    ["description"] = "How long the ready-up button must be held to copy. Shorter presses paste.",
                    ["min"] = CopyPasteSettings.MinHoldMs,
                    ["max"] = CopyPasteSettings.MaxHoldMs,
                    ["step"] = CopyPasteSettings.HoldMsStep,
                    ["format"] = "{0} ms",
                    ["get"] = (Func<int>)(() => CopyPasteSettings.HoldToCopyMs),
                    ["set"] = (Action<int>)(v =>
                    {
                        CopyPasteSettings.HoldToCopyMs = CopyPasteSettings.ClampHold(v);
                        CopyPasteSettings.Save();
                    }),
                },
                new Dictionary<string, object>
                {
                    ["id"] = "heading_feedback",
                    ["type"] = "heading",
                    ["label"] = "Feedback (host's screen)",
                },
                Bool("show_popups", "Popups", "Show \"Copied\", \"Pasted\" and similar labels over the appliance.",
                    () => CopyPasteSettings.ShowPopups, v => CopyPasteSettings.ShowPopups = v),
                Bool("show_preview", "Preview", "Show the copied setting over a matching appliance under the crane, before pasting.",
                    () => CopyPasteSettings.ShowPreview, v => CopyPasteSettings.ShowPreview = v),
            };
        }

        private static Dictionary<string, object> Bool(string id, string label, string description, Func<bool> get, Action<bool> set)
        {
            return new Dictionary<string, object>
            {
                ["id"] = id,
                ["type"] = "bool",
                ["label"] = label,
                ["description"] = description,
                ["get"] = get,
                ["set"] = (Action<bool>)(v =>
                {
                    set(v);
                    CopyPasteSettings.Save();
                }),
            };
        }
    }
}
