using Kitchen;
using KitchenData;
using Unity.Entities;

namespace CopyPaste
{
    /// <summary>
    /// The player-changeable settings of one appliance: the ones vanilla only lets players change
    /// during the day and in practice. Copy Paste reads and writes them at night instead. Each one
    /// is only captured or applied where vanilla would allow changing it by hand:
    ///   Direction  rotating grabbers; InteractRotatePush skips Orientation.Null. Stored as
    ///              vanilla stores it: relative to the appliance, so it turns with the appliance.
    ///   Filter     smart grabbers; ConfigureGrabbers only sets it when GrabSpecificType.
    ///   Variable   switchable providers; SwitchVariableProvider needs CItemProvider too.
    /// Night-time changes need nothing else: these are plain components, saved with the world
    /// (FullWorldSaveSystem serializes every component) and not reset when the day starts.
    /// </summary>
    internal struct ApplianceSettings
    {
        public int ApplianceId;

        public bool HasDirection;
        public Orientation Direction;

        public bool HasFilter;
        public int FilterItem;
        public ItemList FilterComponents;

        public bool HasVariable;
        public int VariableCurrent;

        public bool IsEmpty => !HasDirection && !HasFilter && !HasVariable;

        /// <summary>Reads the target's settings. False if it has none we support.</summary>
        public static bool TryCapture(EntityManager em, Entity target, out ApplianceSettings settings)
        {
            settings = default;
            if (!em.HasComponent<CAppliance>(target))
            {
                return false;
            }
            settings.ApplianceId = em.GetComponentData<CAppliance>(target).ID;

            if (em.HasComponent<CConveyPushRotatable>(target))
            {
                CConveyPushRotatable rotatable = em.GetComponentData<CConveyPushRotatable>(target);
                if (rotatable.Target != Orientation.Null)
                {
                    settings.HasDirection = true;
                    settings.Direction = rotatable.Target;
                }
            }

            if (em.HasComponent<CConveyPushItems>(target))
            {
                CConveyPushItems push = em.GetComponentData<CConveyPushItems>(target);
                if (push.GrabSpecificType)
                {
                    // An unset filter (0) is copied too, so pasting it clears the target's filter.
                    settings.HasFilter = true;
                    settings.FilterItem = push.SpecificType;
                    settings.FilterComponents = push.SpecificComponents;
                }
            }

            if (em.HasComponent<CVariableProvider>(target) && em.HasComponent<CItemProvider>(target))
            {
                settings.HasVariable = true;
                settings.VariableCurrent = em.GetComponentData<CVariableProvider>(target).Current;
            }

            return !settings.IsEmpty;
        }

        /// <summary>
        /// Writes these settings onto the target, which must be the same appliance type. With
        /// write false nothing is changed. Returns whether anything changed (or would change).
        /// </summary>
        public bool Apply(EntityManager em, Entity target, bool write)
        {
            if (!em.HasComponent<CAppliance>(target) || em.GetComponentData<CAppliance>(target).ID != ApplianceId)
            {
                return false;
            }
            bool changed = false;

            if (HasDirection && em.HasComponent<CConveyPushRotatable>(target))
            {
                CConveyPushRotatable rotatable = em.GetComponentData<CConveyPushRotatable>(target);
                if (rotatable.Target != Orientation.Null && rotatable.Target != Direction)
                {
                    changed = true;
                    if (write)
                    {
                        rotatable.Target = Direction;
                        em.SetComponentData(target, rotatable);
                    }
                }
            }

            if (HasFilter && em.HasComponent<CConveyPushItems>(target))
            {
                CConveyPushItems push = em.GetComponentData<CConveyPushItems>(target);
                if (push.GrabSpecificType
                    && (push.SpecificType != FilterItem || !push.SpecificComponents.IsEquivalent(FilterComponents)))
                {
                    changed = true;
                    if (write)
                    {
                        push.SpecificType = FilterItem;
                        push.SpecificComponents = FilterComponents;
                        em.SetComponentData(target, push);
                    }
                }
            }

            if (HasVariable && em.HasComponent<CVariableProvider>(target) && em.HasComponent<CItemProvider>(target))
            {
                CVariableProvider variable = em.GetComponentData<CVariableProvider>(target);
                if (variable.Current != VariableCurrent)
                {
                    changed = true;
                    if (write)
                    {
                        // Same two writes as vanilla's SwitchVariableProvider.
                        variable.Current = VariableCurrent;
                        em.SetComponentData(target, variable);
                        CItemProvider provider = em.GetComponentData<CItemProvider>(target);
                        provider.SetAsItem(variable.Provide);
                        em.SetComponentData(target, provider);
                    }
                }
            }

            return changed;
        }

        /// <summary>Whether pasting would remove a filter the target has now (call before Apply).</summary>
        public bool ClearsFilter(EntityManager em, Entity target)
        {
            if (!HasFilter || FilterItem != 0 || !em.HasComponent<CConveyPushItems>(target))
            {
                return false;
            }
            CConveyPushItems push = em.GetComponentData<CConveyPushItems>(target);
            return push.GrabSpecificType && push.SpecificType != 0;
        }

        /// <summary>
        /// The item to show in a preview over the target: the filter, or what the provider would
        /// hand out after a paste. 0 for none.
        /// </summary>
        public int PreviewItem(EntityManager em, Entity target, out ItemList components)
        {
            components = default;
            if (HasFilter)
            {
                components = FilterComponents;
                return FilterItem;
            }
            if (HasVariable && em.HasComponent<CVariableProvider>(target))
            {
                CVariableProvider variable = em.GetComponentData<CVariableProvider>(target);
                variable.Current = VariableCurrent;
                int item = variable.Provide;
                components = new ItemList(item);
                return item;
            }
            return 0;
        }

        /// <summary>Which "Copied" popup to show.</summary>
        public FeedbackCode CopiedCode()
        {
            if (HasDirection && !HasFilter && !HasVariable)
            {
                return FeedbackCode.CopiedDirection;
            }
            if (HasFilter && !HasDirection && !HasVariable)
            {
                return FilterItem == 0 ? FeedbackCode.CopiedNoFilter : FeedbackCode.CopiedFilter;
            }
            return FeedbackCode.Copied;
        }

        public string Describe()
        {
            string text = "";
            if (HasDirection)
            {
                text += $" direction={Direction}";
            }
            if (HasFilter)
            {
                text += $" filter={FilterItem}";
            }
            if (HasVariable)
            {
                text += $" variable={VariableCurrent}";
            }
            return text.Trim();
        }
    }
}
