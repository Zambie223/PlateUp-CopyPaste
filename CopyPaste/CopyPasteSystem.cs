using System;
using System.Collections.Generic;
using Controllers;
using Kitchen;
using KitchenMods;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace CopyPaste
{
    /// <summary>
    /// Host side. Watches every player's ready-up button (InputState.SecondaryAction1: K / Y by
    /// default, rebindable in the game's controls menu) while they're in Advanced Build Mode:
    /// a hold of HoldToCopyMs copies the appliance under the crane, a shorter press pastes.
    ///
    /// Why that button is free there: while anyone is in crane mode, the start-day board's top
    /// warning is PlayersInCraneMode, which outranks PlayersNotReady, so StartDayWarningView
    /// clears every ready vote and ignores the button for all players (the day can't start).
    /// Nothing else in vanilla reads it outside popups, which capture the input anyway.
    ///
    /// Skipped while the player's hands are full. That keeps clear of Blueprint Shop's build
    /// queue, which uses the same button while the player carries a queued item.
    ///
    /// The target is CAttemptingInteraction.Target, which vanilla's AttemptInteraction (first in
    /// InteractionGroup) recomputes every frame; in crane mode that's the appliance under the
    /// crane (SetCraneStatus sets the interaction offset to 0).
    ///
    /// Feedback for every player (popups and previews) goes out through FeedbackChannel as one
    /// snapshot, to the host's screen and to guests; each machine draws its own players'.
    /// </summary>
    [UpdateInGroup(typeof(InteractionGroup))]
    [UpdateAfter(typeof(AttemptInteraction))]
    public class CopyPasteSystem : GenericSystemBase, IModSystem
    {
        private struct HoldState
        {
            public bool Down;
            public float Since;
            /// <summary>This press has been used (copied) or doesn't count.</summary>
            public bool Done;
        }

        private struct RecentPopup
        {
            public PopupEvent Event;
            public float Born;
        }

        /// <summary>Popups stay in the snapshot this long, so every send in that time carries them.</summary>
        private const float PopupKeepSeconds = 1f;

        /// <summary>While a preview is up, resend this often, so receivers can drop a stale one.</summary>
        private const float HeartbeatSeconds = 0.5f;

        // Player ID -> appliance ID -> copied settings. Player IDs stay the same across scene
        // changes, so this lives for the game session.
        private static readonly Dictionary<int, Dictionary<int, ApplianceSettings>> Clipboards =
            new Dictionary<int, Dictionary<int, ApplianceSettings>>();

        private static int _popupSeq;

        private readonly Dictionary<int, HoldState> _holds = new Dictionary<int, HoldState>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _gone = new List<int>();
        private readonly List<RecentPopup> _recent = new List<RecentPopup>();
        private readonly List<PopupEvent> _popups = new List<PopupEvent>();
        private readonly List<PreviewState> _previews = new List<PreviewState>();
        private string _lastKey = "";
        private float _lastSend;
        private EntityQuery _players;
        private bool _loggedError;

        protected override void Initialise()
        {
            base.Initialise();
            _players = GetEntityQuery(new QueryHelper().All(typeof(CPlayer), typeof(CInputData)));
        }

        protected override void OnUpdate()
        {
            // Only the host runs the game. A guest's router is its LocalViewRouter alone; the
            // host's is a SplitRouter feeding its own screen and the network.
            IViewRouter router = World.GetExistingSystem<RouterManager>()?.Entrypoint;
            if (!(router is SplitRouter))
            {
                return;
            }
            _previews.Clear();
            try
            {
                Run();
                Publish(router);
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    ModMain.LogError($"Copy/paste failed (logged once): {e}");
                }
            }
        }

        private void Run()
        {
            float now = UnityEngine.Time.unscaledTime; // real time, like the hold in Blueprint Shop
            _seen.Clear();
            using (NativeArray<Entity> players = _players.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity player in players)
                {
                    CPlayer info = EntityManager.GetComponentData<CPlayer>(player);
                    CInputData input = EntityManager.GetComponentData<CInputData>(player);
                    _seen.Add(info.ID);

                    bool eligible = CopyPasteSettings.Enabled
                        && Has<CIsCraneMode>(player)
                        && !input.IsCaptured && !input.IsDisconnected
                        && HandsEmpty(player);
                    Entity target = eligible ? GetTarget(player) : Entity.Null;

                    ButtonState button = input.State.SecondaryAction1;
                    bool down = button == ButtonState.Pressed || button == ButtonState.Held;
                    _holds.TryGetValue(info.ID, out HoldState hold);
                    if (down)
                    {
                        if (!hold.Down)
                        {
                            // A press that starts outside crane mode (a ready vote) never counts.
                            hold = new HoldState { Down = true, Since = now, Done = !eligible };
                        }
                        else if (!eligible)
                        {
                            hold.Done = true;
                        }
                        if (!hold.Done && now - hold.Since >= CopyPasteSettings.HoldToCopySeconds)
                        {
                            hold.Done = true;
                            Copy(player, target, info);
                        }
                    }
                    else
                    {
                        if (hold.Down && !hold.Done && eligible)
                        {
                            Paste(player, target, info);
                        }
                        hold = default;
                    }
                    _holds[info.ID] = hold;

                    if (eligible && target != Entity.Null && !hold.Down)
                    {
                        AddPreview(info, target);
                    }
                }
            }

            // Forget presses of players who left.
            _gone.Clear();
            foreach (int id in _holds.Keys)
            {
                if (!_seen.Contains(id))
                {
                    _gone.Add(id);
                }
            }
            foreach (int id in _gone)
            {
                _holds.Remove(id);
            }
        }

        /// <summary>Sends the snapshot when it changes, and as a heartbeat while a preview is up.</summary>
        private void Publish(IViewRouter router)
        {
            float now = UnityEngine.Time.unscaledTime;
            _recent.RemoveAll(r => now - r.Born > PopupKeepSeconds);
            _popups.Clear();
            foreach (RecentPopup r in _recent)
            {
                PopupEvent e = r.Event;
                e.AgeMs = (int)((now - r.Born) * 1000f);
                _popups.Add(e);
            }
            // Compare without ages, which change every frame.
            string key = FeedbackChannel.Encode(_popups, _previews, includeAges: false);
            bool heartbeat = _previews.Count > 0 && now - _lastSend >= HeartbeatSeconds;
            if (key == _lastKey && !heartbeat)
            {
                return;
            }
            _lastKey = key;
            _lastSend = now;
            FeedbackChannel.Send(router, FeedbackChannel.Encode(_popups, _previews, includeAges: true));
        }

        private bool HandsEmpty(Entity player)
        {
            if (!EntityManager.HasComponent<CItemHolder>(player))
            {
                return true;
            }
            Entity held = EntityManager.GetComponentData<CItemHolder>(player).HeldItem;
            return held == Entity.Null || !EntityManager.Exists(held);
        }

        private Entity GetTarget(Entity player)
        {
            if (!EntityManager.HasComponent<CAttemptingInteraction>(player))
            {
                return Entity.Null;
            }
            Entity target = EntityManager.GetComponentData<CAttemptingInteraction>(player).Target;
            if (target == Entity.Null || !EntityManager.Exists(target) || !EntityManager.HasComponent<CAppliance>(target))
            {
                return Entity.Null;
            }
            return target;
        }

        private void Copy(Entity player, Entity target, CPlayer info)
        {
            if (target == Entity.Null || !ApplianceSettings.TryCapture(EntityManager, target, out ApplianceSettings settings))
            {
                Popup(info, player, target, FeedbackCode.NothingToCopy);
                return;
            }
            if (!Clipboards.TryGetValue(info.ID, out Dictionary<int, ApplianceSettings> clipboard))
            {
                clipboard = new Dictionary<int, ApplianceSettings>();
                Clipboards[info.ID] = clipboard;
            }
            clipboard[settings.ApplianceId] = settings;
            ModMain.Log($"Player {info.ID} copied appliance {settings.ApplianceId}: {settings.Describe()}");
            Popup(info, player, target, settings.CopiedCode());
        }

        private void Paste(Entity player, Entity target, CPlayer info)
        {
            if (target == Entity.Null)
            {
                return; // a tap on empty floor: nothing to say
            }
            int applianceId = EntityManager.GetComponentData<CAppliance>(target).ID;
            if (!Clipboards.TryGetValue(info.ID, out Dictionary<int, ApplianceSettings> clipboard)
                || !clipboard.TryGetValue(applianceId, out ApplianceSettings settings))
            {
                bool configurable = ApplianceSettings.TryCapture(EntityManager, target, out _);
                Popup(info, player, target, configurable ? FeedbackCode.CopyFirst : FeedbackCode.NothingToPaste);
                return;
            }
            bool clearsFilter = settings.ClearsFilter(EntityManager, target);
            if (settings.Apply(EntityManager, target, write: true))
            {
                if (clearsFilter)
                {
                    RebuildView(target);
                }
                ModMain.Log($"Player {info.ID} pasted onto appliance {applianceId}: {settings.Describe()}");
                Popup(info, player, target, FeedbackCode.Pasted);
            }
            else
            {
                Popup(info, player, target, FeedbackCode.AlreadySame);
            }
        }

        /// <summary>
        /// Replaces the appliance's view with a fresh one on every machine, as happens when a
        /// player picks it up and puts it down. Needed after clearing a filter: vanilla only ever
        /// sets filters, and ConveyItemsView.UpdateData looks up the new filter's prefab without
        /// checking for 0, so telling an existing view about 0 throws and leaves the old icon
        /// there for good. A fresh view starts at 0, so it does no lookup.
        ///
        /// These are the two steps vanilla's CleanViews does for CRemoveView, done now instead:
        /// CleanViews has no ordering against InteractionGroup, so with CRemoveView the old view
        /// could still be sent the 0 first. Without CLinkedView no view system sends to it, and
        /// AddNewViews (ViewSystemsGroup, last in the frame) creates the new view.
        /// </summary>
        private void RebuildView(Entity appliance)
        {
            if (!EntityManager.HasComponent<CLinkedView>(appliance))
            {
                return; // already being rebuilt
            }
            ViewIdentifier view = EntityManager.GetComponentData<CLinkedView>(appliance).Identifier;
            Router?.BroadcastUpdate(view, default(DestroyViewData), MessageType.DestroyView);
            EntityManager.RemoveComponent<CLinkedView>(appliance);
        }

        private void AddPreview(CPlayer info, Entity target)
        {
            int applianceId = EntityManager.GetComponentData<CAppliance>(target).ID;
            if (!Clipboards.TryGetValue(info.ID, out Dictionary<int, ApplianceSettings> clipboard)
                || !clipboard.TryGetValue(applianceId, out ApplianceSettings settings)
                || !settings.Apply(EntityManager, target, write: false)
                || !EntityManager.HasComponent<CPosition>(target))
            {
                return; // nothing copied for this type, or a paste wouldn't change anything
            }
            CPosition pos = EntityManager.GetComponentData<CPosition>(target);
            Vector3 arrow = Vector3.zero;
            if (settings.HasDirection)
            {
                // Direction is relative to the appliance, as in vanilla (ConveyItemsView moves its
                // pusher by PushDirection.ToOffset() in the appliance's local space).
                Quaternion rotation = pos.Rotation;
                arrow = rotation * settings.Direction.ToOffset();
                arrow.y = 0f;
                arrow = arrow.sqrMagnitude > 0.0001f ? arrow.normalized : Vector3.zero;
            }
            int item = settings.PreviewItem(EntityManager, target, out KitchenData.ItemList components);
            _previews.Add(new PreviewState
            {
                Source = info.InputSource,
                PlayerId = info.ID,
                Position = pos.Position,
                Arrow = arrow,
                ShowItem = settings.HasFilter || settings.HasVariable,
                Item = item,
                Components = components,
            });
        }

        private void Popup(CPlayer info, Entity player, Entity target, FeedbackCode code)
        {
            Entity at = target != Entity.Null && EntityManager.HasComponent<CPosition>(target) ? target : player;
            if (!EntityManager.HasComponent<CPosition>(at))
            {
                return;
            }
            _recent.Add(new RecentPopup
            {
                Born = UnityEngine.Time.unscaledTime,
                Event = new PopupEvent
                {
                    Seq = ++_popupSeq,
                    Source = info.InputSource,
                    Position = EntityManager.GetComponentData<CPosition>(at).Position,
                    Code = code,
                },
            });
        }
    }
}
