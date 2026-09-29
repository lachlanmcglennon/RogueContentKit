using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RogueLibsCore;

namespace RogueLibsPlus
{
    // Isolates interaction providers, buttons and callbacks from each other: one that throws no longer stops every other
    // mod's buttons from showing, and it's logged once with the name of the mod that owns it. Also stops the model from
    // running the providers when the object has no interacting agent (e.g. a button refresh a frame after the interaction ended).
    internal static class InteractionFixes
    {
        private static AccessTools.FieldRef<InteractionModel, List<Interaction>> interactionsRef = null!;
        private static AccessTools.FieldRef<InteractionModel, bool> shouldStopRef = null!;
        private static AccessTools.FieldRef<InteractionModel, bool> forcedStopRef = null!;
        private static AccessTools.FieldRef<InteractionModel, bool> initialInteractRef = null!;
        private static Action<Interaction, InteractionModel> setModel = null!;
        private static Action<PlayfieldObject> originalStopInteraction = null!;
        private static Func<InteractionModel, bool> isControlIntercepted = null!;

        public static void Resolve()
        {
            interactionsRef = AccessTools.FieldRefAccess<InteractionModel, List<Interaction>>(
                Fixes.Need(AccessTools.Field(typeof(InteractionModel), "interactions"), "InteractionModel.interactions"));
            shouldStopRef = AccessTools.FieldRefAccess<InteractionModel, bool>(
                Fixes.Need(AccessTools.Field(typeof(InteractionModel), "shouldStop"), "InteractionModel.shouldStop"));
            forcedStopRef = AccessTools.FieldRefAccess<InteractionModel, bool>(
                Fixes.Need(AccessTools.Field(typeof(InteractionModel), "forcedStop"), "InteractionModel.forcedStop"));
            initialInteractRef = AccessTools.FieldRefAccess<InteractionModel, bool>(
                Fixes.Need(AccessTools.Field(typeof(InteractionModel), "initialInteract"), "InteractionModel.initialInteract"));
            setModel = AccessTools.MethodDelegate<Action<Interaction, InteractionModel>>(
                Fixes.Need(AccessTools.PropertySetter(typeof(Interaction), nameof(Interaction.Model)), "Interaction.Model setter"));
            originalStopInteraction = AccessTools.MethodDelegate<Action<PlayfieldObject>>(
                Fixes.Need(AccessTools.Method(typeof(InteractionModel), "OriginalStopInteraction", new Type[] { typeof(PlayfieldObject) }),
                           "InteractionModel.OriginalStopInteraction(PlayfieldObject)"));
            isControlIntercepted = AccessTools.MethodDelegate<Func<InteractionModel, bool>>(
                Fixes.Need(AccessTools.Method(typeof(InteractionModel), "IsControlIntercepted", Type.EmptyTypes), "InteractionModel.IsControlIntercepted()"));
        }

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(InteractionModel), "OnDetermineButtons2", Type.EmptyTypes),
                          prefix: new HarmonyMethod(typeof(InteractionFixes), nameof(OnDetermineButtons2)));
            harmony.Patch(AccessTools.Method(typeof(InteractionModel), "OnPressedButton2", new Type[] { typeof(string), typeof(int) }),
                          prefix: new HarmonyMethod(typeof(InteractionFixes), nameof(OnPressedButton2)));
            harmony.Patch(AccessTools.Method(typeof(InteractionModel), "IsInteractable2", Type.EmptyTypes),
                          prefix: new HarmonyMethod(typeof(InteractionFixes), nameof(IsInteractable2)));
            harmony.Patch(AccessTools.Method(typeof(InteractionModel), nameof(InteractionModel.IsInteractable), new Type[] { typeof(Agent), typeof(bool) }),
                          prefix: new HarmonyMethod(typeof(InteractionFixes), nameof(IsInteractable)));
        }

        private static void ResetState(InteractionModel model, bool determining)
        {
            if (determining) interactionsRef(model).Clear();
            shouldStopRef(model) = false;
            forcedStopRef(model) = false;
            model.StopCallback = null;
            model.SideEffect = null;
        }

        public static bool OnDetermineButtons2(InteractionModel __instance)
        {
            InteractionModel model = __instance;
            List<Interaction> interactions = interactionsRef(model);
            PlayfieldObject obj = model.Instance;

            // reset state
            ResetState(model, true);
            model.AddDoneButton = true;

            // No interacting agent (e.g. WorldSpaceGUI.RefreshObjectButtons2 running a frame after the interaction
            // ended): there is nobody to offer buttons to, so don't run the providers and leave the object's lists empty.
            if (obj.interactingAgent is null) return false;

            // repopulate the interactions
            PopulateInteractions(model, interactions);

            try
            {
                interactions.Sort();
            }
            catch (Exception e)
            {
                InteractionDiagnostics.LogFailure("Sorting interactions", obj, null, e, true);
            }

            // invoke the SideEffect
            InvokeSideEffect(model);

            // if there are no buttons, or one of them cancelled the entire interaction
            if (interactions.Count == 0 || shouldStopRef(model) || forcedStopRef(model))
            {
                InvokeStopCallback(model);
                if (!forcedStopRef(model))
                    originalStopInteraction(obj);
                return false;
            }
            // if there's only one button and its action is implicit
            Interaction single = interactions[0];
            bool implicitAction = false;
            if (interactions.Count == 1 && initialInteractRef(model))
            {
                try
                {
                    implicitAction = single.ImplicitAction;
                }
                catch (Exception e)
                {
                    InteractionDiagnostics.LogFailure($"ImplicitAction of '{single.ButtonName}'", obj, single, e, true);
                }
            }
            if (implicitAction)
            {
                if (!TryPress(model, single, true))
                {
                    AbortAfterFailure(model);
                    return false;
                }
                if (isControlIntercepted(model)) return false;
                originalStopInteraction(obj);
                return false;
            }

            // add button information to the object
            foreach (Interaction interaction in interactions)
            {
                obj.buttons.Add(interaction.ButtonName);
                obj.buttonPrices.Add(interaction.ButtonPrice ?? 0);
                obj.buttonsExtra.Add(interaction.ButtonExtra ?? string.Empty);
            }
            return false;
        }

        public static bool OnPressedButton2(InteractionModel __instance, string buttonName, int buttonPrice)
        {
            InteractionModel model = __instance;
            List<Interaction> interactions = interactionsRef(model);
            PlayfieldObject obj = model.Instance;

            // reset state
            ResetState(model, false);
            model.AddDoneButton = true;

            // the interaction already ended (a stale button press): nothing to press, nothing to stop
            if (obj.interactingAgent is null) return false;

            // handle the default "Done" button
            if (buttonName == "Done")
            {
                originalStopInteraction(obj);
                return false;
            }
            // find the button that was pressed
            Interaction? pressed = interactions.Find(i => i.ButtonName == buttonName && (i.ButtonPrice ?? 0) == buttonPrice);
            if ((pressed ??= interactions.Find(i => i.ButtonName == buttonName)) is null)
            {
                RogueLibsPlusPlugin.Log.LogError($"Couldn't find '{buttonName}' button on {obj}.");
                RogueLibsPlusPlugin.Log.LogDebug($"Available: {string.Join(",", interactions.ConvertAll(static i => i.ButtonName))}.");
                return false;
            }
            // press the button
            if (!TryPress(model, pressed, false))
            {
                AbortAfterFailure(model);
                return false;
            }
            // if the button's action is 'final' or was unsuccessful
            if (shouldStopRef(model) || forcedStopRef(model))
            {
                InvokeStopCallback(model);
                if (!forcedStopRef(model))
                    originalStopInteraction(obj);
                return false;
            }
            if (isControlIntercepted(model)) return false;

            initialInteractRef(model) = false; // make subsequent interaction ignore the implicit button
            // refresh the buttons (restarts the cycle)
            model.Agent.worldSpaceGUI?.RefreshObjectButtons(obj);
            return false;
        }

        public static bool IsInteractable2(InteractionModel __instance, ref bool __result)
        {
            InteractionModel model = __instance;
            List<Interaction> interactions = interactionsRef(model);

            // reset state
            ResetState(model, true);

            // repopulate the interactions
            PopulateInteractions(model, interactions);

            __result = interactions.Count > 0 || model.StopCallback is not null || model.SideEffect is not null;
            return false;
        }

        public static bool IsInteractable(Agent agent, ref bool __result)
        {
            if (agent is not null) return true;
            __result = false;
            return false;
        }

        private static void PopulateInteractions(InteractionModel model, List<Interaction> interactions)
        {
            List<IInteractionProvider> providers = RogueInteractions.Providers;
            for (int i = 0; i < providers.Count; i++)
            {
                IInteractionProvider provider = providers[i];
                Interaction[]? provided;
                try
                {
                    provided = provider.GetInteractions(model);
                }
                catch (Exception e)
                {
                    InteractionDiagnostics.LogFailure("Interaction provider", model.Instance, provider, e, true);
                    continue;
                }
                if (provided is null) continue;
                for (int j = 0, length = provided.Length; j < length; j++)
                {
                    Interaction interaction = provided[j];
                    if (interaction is null) continue;
                    try
                    {
                        setModel(interaction, model);
                        bool success = interaction.SetupButton() && interaction.ButtonName is not null;
                        if (success) interactions.Add(interaction);
                    }
                    catch (Exception e)
                    {
                        InteractionDiagnostics.LogFailure("SetupButton", model.Instance, interaction, e, true);
                    }
                }
            }
        }
        private static bool TryPress(InteractionModel model, Interaction interaction, bool implicitly)
        {
            try
            {
                if (implicitly) interaction.OnPressedImplicitly();
                else interaction.OnPressed();
                return true;
            }
            catch (Exception e)
            {
                InteractionDiagnostics.LogFailure($"Button '{interaction.ButtonName}'", model.Instance, interaction, e, false);
                return false;
            }
        }
        // A button threw: run the stop callback and close the menu, so the object stays usable and the next interaction starts clean.
        private static void AbortAfterFailure(InteractionModel model)
        {
            InvokeStopCallback(model);
            initialInteractRef(model) = true;
            PlayfieldObject? obj = model.Instance;
            if (!forcedStopRef(model) && obj is not null && obj.interactingAgent is not null)
            {
                try
                {
                    originalStopInteraction(obj);
                }
                catch (Exception e)
                {
                    InteractionDiagnostics.LogFailure("StopInteraction after a failed button", obj, null, e, true);
                }
            }
        }
        private static void InvokeStopCallback(InteractionModel model)
        {
            Action? callback = model.StopCallback;
            if (callback is null) return;
            try
            {
                callback();
            }
            catch (Exception e)
            {
                InteractionDiagnostics.LogFailure("Stop callback", model.Instance, callback, e, false);
            }
        }
        private static void InvokeSideEffect(InteractionModel model)
        {
            Action? sideEffect = model.SideEffect;
            if (sideEffect is null) return;
            try
            {
                sideEffect();
            }
            catch (Exception e)
            {
                InteractionDiagnostics.LogFailure("Side effect", model.Instance, sideEffect, e, true);
            }
        }
    }

    // Names the mod that owns a failing interaction provider, button or callback, and logs each distinct failure once.
    internal static class InteractionDiagnostics
    {
        private static readonly HashSet<string> loggedOnce = new HashSet<string>();
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static string Describe(object? source)
        {
            if (source is null) return "<null>";
            if (source is Delegate d) return DescribeDelegate(d);
            Type type = source.GetType();
            // RogueLibs' simple providers and interactions wrap the mod's delegate: name the delegate instead
            if (type.Assembly == typeof(InteractionModel).Assembly)
            {
                Delegate? inner = FindDelegate(source, "_handler") ?? FindDelegate(source, "_action");
                if (inner is not null) return $"{DescribeDelegate(inner)} via {type.Name}";
            }
            return $"{type.Assembly.GetName().Name}: {type.FullName}";
        }
        private static Delegate? FindDelegate(object source, string fieldName)
        {
            try
            {
                return AccessTools.Field(source.GetType(), fieldName)?.GetValue(source) as Delegate;
            }
            catch (Exception)
            {
                return null;
            }
        }
        private static string DescribeDelegate(Delegate d)
        {
            // unwrap RogueLibs' own adapter closures (e.g. SetStopCallback's () => callback(model)) to name the mod's delegate
            object? target = d.Target;
            if (target is not null && (target.GetType().Assembly == typeof(InteractionModel).Assembly
                                       || target.GetType().Assembly == typeof(InteractionDiagnostics).Assembly)
                && target.GetType().Name.StartsWith("<", StringComparison.Ordinal))
            {
                foreach (FieldInfo field in target.GetType().GetFields(InstanceFields))
                    if (typeof(Delegate).IsAssignableFrom(field.FieldType) && field.GetValue(target) is Delegate inner && inner != d)
                        return DescribeDelegate(inner);
            }
            MethodInfo method = d.Method;
            Type? type = method.DeclaringType;
            // compiler-generated closure classes are nested inside the type that declared the lambda
            while (type?.DeclaringType is not null && type.Name.StartsWith("<", StringComparison.Ordinal))
                type = type.DeclaringType;
            string asm = type?.Assembly.GetName().Name ?? method.Module.Assembly.GetName().Name;
            return $"{asm}: {type?.FullName}.{method.Name}";
        }

        public static void LogFailure(string what, PlayfieldObject? obj, object? source, Exception e, bool once)
        {
            string owner = Describe(source);
            if (once)
            {
                string key = $"{what}|{owner}|{obj?.GetType().Name}|{e.GetType().FullName}";
                if (!loggedOnce.Add(key)) return;
            }
            string objName = obj is null ? "<null>" : $"{obj.GetType().Name} '{obj.name}'";
            RogueLibsPlusPlugin.Log.LogError($"{what} on {objName} threw {e.GetType().Name} [{owner}]{(once ? " (further identical errors suppressed)" : "")}.");
            RogueLibsPlusPlugin.Log.LogError(e.ToString());
        }
    }

    // Vanilla closes the turntables' menu after a hack or the mayor's evidence. RogueLibs leaves it open, so the hack
    // could be pressed again while the first danger was still alive: SpawnDanger then returns null and PlayBadMusic throws.
    internal static class TurntablesFix
    {
        private static FieldInfo actionField = null!;

        public static void Resolve()
            => actionField = Fixes.Need(AccessTools.Field(typeof(SimpleInteraction<Turntables>), "_action"), "SimpleInteraction<T>._action");

        public static void Apply(Harmony _)
        {
            RogueInteractions.CreateProvider<Turntables>(static h =>
            {
                if (!h.Object.functional) return;
                if (h.Helper.interactingFar)
                {
                    if (h.Object.badMusicPlaying && h.Object.hasDanger)
                    {
                        h.Model.RemoveInteraction("PlayBadMusic");
                        h.Model.RemoveInteraction("PlayBadMusic2");
                        h.SetStopCallback(static m => m.gc.audioHandler.Play(m.Agent, "CantDo"));
                        return;
                    }
                    CloseAfter(h.Model, "PlayBadMusic");
                    CloseAfter(h.Model, "PlayBadMusic2");
                }
                else CloseAfter(h.Model, "PlayMayorEvidence");
            });
        }

        // wraps the existing button's action in place, so the buttons keep their order
        private static void CloseAfter(InteractionModel model, string buttonName)
        {
            foreach (Interaction interaction in model.Interactions)
            {
                if (interaction.ButtonName != buttonName || interaction is not SimpleInteraction<Turntables> simple) continue;
                if (actionField.GetValue(simple) is not Action<InteractionModel<Turntables>> action) continue;
                actionField.SetValue(simple, (Action<InteractionModel<Turntables>>)(m =>
                {
                    action(m);
                    m.StopInteraction();
                }));
            }
        }
    }
}
