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

        // Player ID -> appliance ID -> copied settings. Player IDs stay the same across scene
        // changes, so this lives for the game session.
        private static readonly Dictionary<int, Dictionary<int, ApplianceSettings>> Clipboards =
            new Dictionary<int, Dictionary<int, ApplianceSettings>>();

        private readonly Dictionary<int, HoldState> _holds = new Dictionary<int, HoldState>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _gone = new List<int>();
        private readonly List<CopyPasteVisuals.Preview> _previews = new List<CopyPasteVisuals.Preview>();
        private EntityQuery _players;
        private bool _loggedError;

        protected override void Initialise()
        {
            base.Initialise();
            _players = GetEntityQuery(new QueryHelper().All(typeof(CPlayer), typeof(CInputData)));
        }

        protected override void OnUpdate()
        {
            _previews.Clear();
            try
            {
                Run();
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    ModMain.LogError($"Copy/paste failed (logged once): {e}");
                }
            }
            CopyPasteVisuals.SetPreviews(_previews);
        }

        private void Run()
        {
            float now = UnityEngine.Time.unscaledTime; // real time, like the hold in Blueprint Shop
            int localSource = InputSourceIdentifier.Identifier.Value;
            _seen.Clear();
            using (NativeArray<Entity> players = _players.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity player in players)
                {
                    CPlayer info = EntityManager.GetComponentData<CPlayer>(player);
                    CInputData input = EntityManager.GetComponentData<CInputData>(player);
                    _seen.Add(info.ID);
                    // Feedback only for players at the host's own screen.
                    bool local = info.InputSource == localSource;

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
                            Copy(player, target, info.ID, local);
                        }
                    }
                    else
                    {
                        if (hold.Down && !hold.Done && eligible)
                        {
                            Paste(player, target, info.ID, local);
                        }
                        hold = default;
                    }
                    _holds[info.ID] = hold;

                    if (local && eligible && target != Entity.Null && !hold.Down && CopyPasteSettings.ShowPreview)
                    {
                        AddPreview(info.ID, target);
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

        private void Copy(Entity player, Entity target, int playerId, bool local)
        {
            if (target == Entity.Null || !ApplianceSettings.TryCapture(EntityManager, target, out ApplianceSettings settings))
            {
                Popup(local, player, target, "Nothing to copy", false);
                return;
            }
            if (!Clipboards.TryGetValue(playerId, out Dictionary<int, ApplianceSettings> clipboard))
            {
                clipboard = new Dictionary<int, ApplianceSettings>();
                Clipboards[playerId] = clipboard;
            }
            clipboard[settings.ApplianceId] = settings;
            ModMain.Log($"Player {playerId} copied appliance {settings.ApplianceId}: {settings.Describe()}");
            Popup(local, player, target, settings.Label(), true);
        }

        private void Paste(Entity player, Entity target, int playerId, bool local)
        {
            if (target == Entity.Null)
            {
                return; // a tap on empty floor: nothing to say
            }
            int applianceId = EntityManager.GetComponentData<CAppliance>(target).ID;
            if (!Clipboards.TryGetValue(playerId, out Dictionary<int, ApplianceSettings> clipboard)
                || !clipboard.TryGetValue(applianceId, out ApplianceSettings settings))
            {
                bool configurable = ApplianceSettings.TryCapture(EntityManager, target, out _);
                Popup(local, player, target, configurable ? "Copy one of these first" : "Nothing to paste here", false);
                return;
            }
            if (settings.Apply(EntityManager, target, write: true))
            {
                ModMain.Log($"Player {playerId} pasted onto appliance {applianceId}: {settings.Describe()}");
                Popup(local, player, target, "Pasted", true);
            }
            else
            {
                Popup(local, player, target, "Already the same", false);
            }
        }

        private void AddPreview(int playerId, Entity target)
        {
            int applianceId = EntityManager.GetComponentData<CAppliance>(target).ID;
            if (!Clipboards.TryGetValue(playerId, out Dictionary<int, ApplianceSettings> clipboard)
                || !clipboard.TryGetValue(applianceId, out ApplianceSettings settings)
                || !settings.Apply(EntityManager, target, write: false)
                || !EntityManager.HasComponent<CPosition>(target))
            {
                return; // nothing copied for this type, or a paste wouldn't change anything
            }
            CPosition pos = EntityManager.GetComponentData<CPosition>(target);
            int item = settings.PreviewItem(EntityManager, target, out KitchenData.ItemList components);
            _previews.Add(new CopyPasteVisuals.Preview
            {
                PlayerId = playerId,
                Target = target,
                Position = pos.Position,
                Rotation = pos.Rotation,
                HasDirection = settings.HasDirection,
                Direction = settings.Direction,
                ShowItem = settings.HasFilter || settings.HasVariable,
                Item = item,
                Components = components,
            });
        }

        private void Popup(bool local, Entity player, Entity target, string text, bool good)
        {
            if (!local || !CopyPasteSettings.ShowPopups)
            {
                return;
            }
            Entity at = target != Entity.Null && EntityManager.HasComponent<CPosition>(target) ? target : player;
            if (!EntityManager.HasComponent<CPosition>(at))
            {
                return;
            }
            CopyPasteVisuals.Popup(EntityManager.GetComponentData<CPosition>(at).Position, text, good);
        }
    }
}
