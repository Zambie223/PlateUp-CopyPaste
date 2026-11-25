using System;
using System.Reflection;
using HarmonyLib;
using KitchenMods;
using UnityEngine;

namespace CopyPaste
{
    /// <summary>
    /// Entry point. Copy Paste lets a player in Advanced Build Mode (the night-time crane) copy
    /// the settings of one appliance and paste them onto another appliance of the same type:
    /// hold the game's ready-up button (K / Y by default) to copy, tap it to paste.
    ///
    /// Supported settings (the ones vanilla only lets you change during the day and in practice):
    ///   rotating grabbers        - direction (CConveyPushRotatable.Target)
    ///   smart grabbers           - item filter (CConveyPushItems.SpecificType/Components)
    ///   switchable providers     - selected item (CVariableProvider.Current)
    ///
    /// How it fits together:
    ///   Host:          CopyPasteSystem reads every player's vanilla input (CInputData), which
    ///                  online guests already send to the host, so copy/paste itself needs the
    ///                  mod on the host only. It sends feedback snapshots (FeedbackChannel).
    ///   Every machine: the FeedbackChannel patch receives the snapshots and CopyPasteVisuals
    ///                  draws that machine's own players' popups and previews. Guests without
    ///                  the mod silently drop the snapshots.
    /// </summary>
    public class ModMain : IModInitializer
    {
        public const string Id = "zambie.copypaste";
        public const string Name = "Copy Paste";
        public const string Version = "0.2.1";

        private static Harmony _harmony;

        public void PostActivate(KitchenMods.Mod mod)
        {
            CopyPasteSettings.Load();
            Log($"v{Version} loaded ({CopyPasteSettings.Describe()})");

            if (_harmony == null)
            {
                _harmony = new Harmony(Id);
                try
                {
                    _harmony.PatchAll(typeof(ModMain).Assembly);
                    foreach (MethodBase method in _harmony.GetPatchedMethods())
                    {
                        Log($"Patched {method.DeclaringType?.FullName}.{method.Name}");
                    }
                }
                catch (Exception e)
                {
                    // The copy/paste system doesn't depend on the patch; only the feedback does.
                    LogError($"Couldn't patch the game, so popups and previews won't show: {e}");
                }
            }
        }

        public void PreInject() { }

        public void PostInject() { }

        // Everything goes to Player.log:
        // %USERPROFILE%\AppData\LocalLow\It's Happening\PlateUp\Player.log
        internal static void Log(string message) => Debug.Log($"[{Name}] {message}");
        internal static void LogWarning(string message) => Debug.LogWarning($"[{Name}] {message}");
        internal static void LogError(string message) => Debug.LogError($"[{Name}] {message}");
    }
}
