using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using Kitchen;
using KitchenData;
using UnityEngine;

namespace CopyPaste
{
    /// <summary>What a popup says. Sent as a number, so the text never has to cross the network.</summary>
    internal enum FeedbackCode
    {
        Copied = 0,
        CopiedDirection = 1,
        CopiedFilter = 2,
        CopiedNoFilter = 3,
        Pasted = 4,
        AlreadySame = 5,
        CopyFirst = 6,
        NothingToPaste = 7,
        NothingToCopy = 8,
    }

    internal struct PopupEvent
    {
        public int Seq;
        public int AgeMs;
        /// <summary>CPlayer.InputSource of the player it's for: each machine shows only its own players'.</summary>
        public int Source;
        public Vector3 Position;
        public FeedbackCode Code;

        public bool Good => Code == FeedbackCode.Copied || Code == FeedbackCode.CopiedDirection
            || Code == FeedbackCode.CopiedFilter || Code == FeedbackCode.CopiedNoFilter || Code == FeedbackCode.Pasted;

        public string Text
        {
            get
            {
                switch (Code)
                {
                    case FeedbackCode.CopiedDirection: return "Copied direction";
                    case FeedbackCode.CopiedFilter: return "Copied filter";
                    case FeedbackCode.CopiedNoFilter: return "Copied: no filter";
                    case FeedbackCode.Pasted: return "Pasted";
                    case FeedbackCode.AlreadySame: return "Already the same";
                    case FeedbackCode.CopyFirst: return "Copy one of these first";
                    case FeedbackCode.NothingToPaste: return "Nothing to paste here";
                    case FeedbackCode.NothingToCopy: return "Nothing to copy";
                    default: return "Copied";
                }
            }
        }
    }

    internal struct PreviewState
    {
        public int Source;
        public int PlayerId;
        public Vector3 Position;
        /// <summary>World-space push direction for the arrow; zero for none.</summary>
        public Vector3 Arrow;
        public bool ShowItem;
        public int Item;
        public ItemList Components;

        public bool SameLookAs(PreviewState other)
        {
            return Source == other.Source && PlayerId == other.PlayerId && Position == other.Position && Arrow == other.Arrow
                && ShowItem == other.ShowItem && Item == other.Item && Components.IsEquivalent(other.Components);
        }
    }

    /// <summary>
    /// Carries the feedback from the host to every machine, including the host's own screen.
    ///
    /// Why this way (all verified in the decompiled game, see CHANGELOG.internal.md):
    ///  - It uses a vanilla data type, so nothing has to be added to the game's message
    ///    formatters: AchievementDistributionView.ViewData, a single string, which is in the
    ///    IViewData union. (A custom type would need formatter patches on every machine.)
    ///  - It's sent as a ViewUpdate to a view ID that never exists. AddNewViews hands out IDs
    ///    counting up from 100, so a negative one can't collide. LocalViewRouter.HandleUpdate
    ///    finds no view, and CacheUpdate keeps it for 10 frames, retrying silently, then drops
    ///    it. A guest without the mod logs nothing and nothing happens.
    ///  - With the mod, the Harmony prefix on CacheUpdate (private, non-generic, 56 IL bytes,
    ///    so not inlined) takes the message instead.
    ///  - The host's router is a SplitRouter (its own LocalViewRouter + NetworkRouter), so the
    ///    host's screen gets the same message through the same path.
    ///  - Each message is a full snapshot: every current preview, plus popups from the last
    ///    second with sequence numbers, so a receiver shows each popup once and one message is
    ///    enough to be up to date.
    ///  - Steam P2P sends are reliable (Facepunch SendP2PPacket defaults to P2PSend.Reliable).
    /// </summary>
    internal static class FeedbackChannel
    {
        /// <summary>"CPST" as a negative number: can't be a real view (see above).</summary>
        public const int ViewId = -0x43505354;

        /// <summary>Format tag. A receiver ignores snapshots in a format it doesn't know.</summary>
        private const string Format = "CP1";

        /// <summary>Different each time the game starts; a new host resets the sequence numbers.</summary>
        private static readonly int Nonce = new System.Random().Next(1, int.MaxValue);

        private static readonly StringBuilder Sb = new StringBuilder(256);
        private static bool _loggedBadFormat;

        public static string Encode(List<PopupEvent> popups, List<PreviewState> previews, bool includeAges)
        {
            Sb.Clear();
            Sb.Append(Format).Append(';').Append(I(Nonce));
            foreach (PopupEvent p in popups)
            {
                Sb.Append("|P,").Append(I(p.Seq)).Append(',').Append(I(includeAges ? p.AgeMs : 0)).Append(',').Append(I(p.Source))
                    .Append(',').Append(F(p.Position.x)).Append(',').Append(F(p.Position.y)).Append(',').Append(F(p.Position.z))
                    .Append(',').Append(I((int)p.Code));
            }
            foreach (PreviewState v in previews)
            {
                Sb.Append("|V,").Append(I(v.Source)).Append(',').Append(I(v.PlayerId))
                    .Append(',').Append(F(v.Position.x)).Append(',').Append(F(v.Position.y)).Append(',').Append(F(v.Position.z))
                    .Append(',').Append(F(v.Arrow.x)).Append(',').Append(F(v.Arrow.z))
                    .Append(',').Append(v.ShowItem ? 1 : 0).Append(',').Append(I(v.Item)).Append(',');
                int[] components = v.Components.AsArray();
                for (int i = 0; i < components.Length; i++)
                {
                    if (i > 0)
                    {
                        Sb.Append(':');
                    }
                    Sb.Append(I(components[i]));
                }
            }
            return Sb.ToString();
        }

        public static void Send(IViewRouter router, string payload)
        {
            router.BroadcastUpdate(new ViewIdentifier { Identifier = ViewId },
                new AchievementDistributionView.ViewData { Achievement = payload });
        }

        /// <summary>Every machine: a snapshot arrived (from the Harmony patch).</summary>
        public static void Receive(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return;
            }
            string[] parts = payload.Split('|');
            string[] head = parts[0].Split(';');
            if (head.Length < 2 || head[0] != Format || !TryI(head[1], out int nonce))
            {
                if (!_loggedBadFormat)
                {
                    _loggedBadFormat = true;
                    ModMain.LogWarning("Feedback from the host is in a format this version doesn't know. Are the versions the same?");
                }
                return;
            }
            List<PopupEvent> popups = new List<PopupEvent>();
            List<PreviewState> previews = new List<PreviewState>();
            for (int i = 1; i < parts.Length; i++)
            {
                string[] f = parts[i].Split(',');
                if (f[0] == "P" && f.Length >= 8
                    && TryI(f[1], out int seq) && TryI(f[2], out int age) && TryI(f[3], out int src)
                    && TryF(f[4], out float px) && TryF(f[5], out float py) && TryF(f[6], out float pz)
                    && TryI(f[7], out int code) && Enum.IsDefined(typeof(FeedbackCode), code))
                {
                    popups.Add(new PopupEvent { Seq = seq, AgeMs = age, Source = src, Position = new Vector3(px, py, pz), Code = (FeedbackCode)code });
                }
                else if (f[0] == "V" && f.Length >= 11
                    && TryI(f[1], out int vsrc) && TryI(f[2], out int player)
                    && TryF(f[3], out float x) && TryF(f[4], out float y) && TryF(f[5], out float z)
                    && TryF(f[6], out float ax) && TryF(f[7], out float az)
                    && TryI(f[9], out int item))
                {
                    List<int> components = new List<int>();
                    foreach (string c in f[10].Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (TryI(c, out int id))
                        {
                            components.Add(id);
                        }
                    }
                    previews.Add(new PreviewState
                    {
                        Source = vsrc,
                        PlayerId = player,
                        Position = new Vector3(x, y, z),
                        Arrow = new Vector3(ax, 0f, az),
                        ShowItem = f[8] == "1",
                        Item = item,
                        Components = components.Count > 0 ? new ItemList(components) : default,
                    });
                }
            }
            CopyPasteVisuals.Apply(nonce, popups, previews);
        }

        // Invariant culture both ways: IDs can be negative, and the minus sign varies by culture.
        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        private static bool TryI(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static bool TryF(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>See FeedbackChannel. Runs on the host and on guests that have the mod.</summary>
    [HarmonyPatch(typeof(LocalViewRouter), "CacheUpdate")]
    internal static class LocalViewRouter_CacheUpdate_Patch
    {
        private static bool _loggedError;

        private static bool Prefix(ViewIdentifier entity, IViewData data)
        {
            if (entity.Identifier != FeedbackChannel.ViewId)
            {
                return true;
            }
            try
            {
                if (data is AchievementDistributionView.ViewData snapshot)
                {
                    FeedbackChannel.Receive(snapshot.Achievement);
                }
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    ModMain.LogError($"Couldn't read feedback (logged once): {e}");
                }
            }
            return false; // never cache it: there's no view to deliver it to
        }
    }
}
