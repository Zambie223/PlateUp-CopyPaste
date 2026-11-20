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
    /// Host only: CopyPasteSystem reads every player's vanilla input (CInputData), which online
    /// clients already send to the host, so other players don't need the mod. The feedback
    /// (CopyPasteVisuals) is drawn on the host's screen only.
    /// </summary>
    public class ModMain : IModInitializer
    {
        public const string Id = "zambie.copypaste";
        public const string Name = "Copy Paste";
        public const string Version = "0.1.0";

        public void PostActivate(KitchenMods.Mod mod)
        {
            CopyPasteSettings.Load();
            Log($"v{Version} loaded ({CopyPasteSettings.Describe()})");
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
