using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Schadenfreude
{
    [DefOf]
    public static class SFDefOf
    {
        public static ThoughtDef SF_PrivateReaction;
        public static ThoughtDef SF_UneasyAboutPleasure;
        static SFDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(SFDefOf)); }
    }

    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            new Harmony("tb.schadenfreude").PatchAll();
            Log.Message("[Schadenfreude] Development preview loaded: witnessed insults and mood breakdowns only.");
        }
    }

    // Optional APIs are resolved by exact signature. A failing bridge disables only itself and
    // reports the failure once; the standalone opinion-based mechanic still works.
    internal sealed class PairBridge
    {
        private readonly string label;
        private MethodInfo method;
        public PairBridge(string type, string member)
        {
            label = type + "." + member;
            var resolved = AccessTools.TypeByName(type);
            method = resolved?.GetMethod(member, BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(Pawn), typeof(Pawn) }, null);
            if (method != null && method.ReturnType != typeof(bool)) method = null;
        }
        public bool Read(Pawn a, Pawn b)
        {
            if (method == null) return false;
            try { return (bool)method.Invoke(null, new object[] { a, b }); }
            catch (Exception ex)
            {
                method = null;
                Log.Warning("[Schadenfreude] Optional bridge disabled for this session: " + label + ": " + ex.GetBaseException().Message);
                return false;
            }
        }
    }

    public class GameComponent_Schadenfreude : GameComponent
    {
        private sealed class SeenEvent
        {
            public Pawn subject;
            public Pawn[] witnesses;
            public bool serious;
        }
        private readonly Queue<SeenEvent> pending = new Queue<SeenEvent>();
        private PairBridge rivals, allies, grievances;
        private bool processing;

        public GameComponent_Schadenfreude(Game game) { }
        public static GameComponent_Schadenfreude Current => Verse.Current.Game?.GetComponent<GameComponent_Schadenfreude>();
        public static bool Eligible(Pawn p) => p != null && !p.Dead && p.Spawned &&
            p.IsColonist && !p.IsPrisoner && p.RaceProps.Humanlike &&
            !p.DevelopmentalStage.Juvenile() && p.needs?.mood != null;

        // Capture who actually saw the event now, before the victim or observers move.
        // Process next tick, outside other mods' interaction/mental-state patch call stacks.
        public void Observe(Pawn subject, Pawn instigator, bool serious)
        {
            if (processing || !Eligible(subject) || pending.Count >= 64) return;
            var witnesses = subject.Map.mapPawns.AllPawnsSpawned.ToArray()
                .Where(p => p != subject && p != instigator && Eligible(p) && !p.Downed &&
                    p.Awake() && !p.InMentalState && p.Position.DistanceToSquared(subject.Position) <= 144 &&
                    GenSight.LineOfSight(p.Position, subject.Position, subject.Map)).ToArray();
            if (witnesses.Length > 0)
                pending.Enqueue(new SeenEvent { subject = subject, witnesses = witnesses, serious = serious });
        }

        public override void GameComponentTick()
        {
            if (pending.Count == 0 || processing) return;
            if (rivals == null)
            {
                rivals = new PairBridge("Crew.GameComponent_Crews", "AreRivals");
                allies = new PairBridge("Crew.GameComponent_Crews", "SameCrew");
                grievances = new PairBridge("Resentment.Grievances", "Any");
            }
            processing = true;
            try
            {
                while (pending.Count > 0)
                {
                    SeenEvent e = pending.Dequeue();
                    if (!Eligible(e.subject)) continue;
                    foreach (Pawn witness in e.witnesses)
                    {
                        if (!Eligible(witness)) continue;
                        try { React(witness, e.subject, e.serious); }
                        catch (Exception ex)
                        {
                            Log.ErrorOnce("[Schadenfreude] Could not evaluate a witnessed setback: " + ex, 173925814);
                        }
                    }
                }
            }
            finally { processing = false; }
        }

        private void React(Pawn witness, Pawn subject, bool serious)
        {
            var memories = witness.needs.mood.thoughts.memories;
            // One reaction per half-day across ALL victims. Existing memories carry this cap
            // through save/load, without a second persistent ledger or an infinite refresh loop.
            if (memories.Memories.Any(m => m.def == SFDefOf.SF_PrivateReaction ||
                m.def == SFDefOf.SF_UneasyAboutPleasure)) return;
            int opinion = witness.relations.OpinionOf(subject);
            Reaction r = Appraisal.Evaluate(opinion, grievances.Read(witness, subject),
                rivals.Read(witness, subject), allies.Read(witness, subject),
                witness.story?.traits?.HasTrait(TraitDefOf.Kind) == true, serious);
            if (r == Reaction.None) return;
            var feeling = (Thought_Memory)ThoughtMaker.MakeThought(SFDefOf.SF_PrivateReaction, (int)r);
            memories.TryGainMemory(feeling, subject);
            // Do not add remorse for a feeling another mod refused. Merging is not expected:
            // the no-existing-memory check above enforces the global cap before this offer.
            if (r == Reaction.Mixed && memories.Memories.Contains(feeling))
                memories.TryGainMemory(SFDefOf.SF_UneasyAboutPleasure, subject);
        }
    }

    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
    public static class InsultHook
    {
        public static void Postfix(Pawn recipient, InteractionDef intDef, bool __result, bool __runOriginal, Pawn ___pawn)
        {
            if (__runOriginal && __result && intDef == InteractionDefOf.Insult)
                GameComponent_Schadenfreude.Current?.Observe(recipient, ___pawn, false);
        }
    }

    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class BreakdownHook
    {
        public static void Postfix(MentalStateHandler __instance, MentalStateDef stateDef,
            bool causedByMood, bool __result, bool __runOriginal, Pawn ___pawn)
        {
            if (__runOriginal && __result && causedByMood && stateDef != null &&
                stateDef != MentalStateDefOf.SocialFighting && __instance.InMentalState &&
                __instance.CurStateDef == stateDef)
                GameComponent_Schadenfreude.Current?.Observe(___pawn, null, true);
        }
    }
}
