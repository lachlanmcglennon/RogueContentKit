#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using RogueLibsCore;
using UnityEngine;
using static RCK.AgentText;

namespace RCK.Quests
{
    /// <summary>
    ///   Quest givers. An NPC whose Talk text is a <c>rck-quest:::</c> script offers its chain of jobs (kill, retrieve,
    ///   destroy, deliver, talk) with its own story text, one at a time; the <c>RCK_Radiant_Quests</c> mutator and the
    ///   <c>RCK_Radiant_Quest_Giver</c> trait hand ordinary NPCs a generated job. Jobs are per level and run on the
    ///   server; in multiplayer the host takes them. See FEATURES.md.
    /// </summary>
    public sealed class QuestsModule : IRckModule
    {
        public string Name => "Quests";

        public void Initialize()
        {
            ButtonLabels.Register(typeof(CustomButtons));
            LevelMutators.Require(QuestRules.RadiantMutator, "Quests: mutator");
            foreach (string trait in QuestRuntime.Traits)
                if (!Rck.IsRckTrait(trait)) Rck.Log.LogError($"Quests: trait {trait} is not in the generated data.");
            RckWorld.RegisterGateSwitch(QuestRules.GateSwitch, QuestRuntime.GateHolds);
            RogueInteractions.CreateProvider<Agent>(QuestRuntime.Provide);
        }
    }

    internal enum JobState { Locked, Offered, Active, Ready, Done, Failed }

    /// <summary>What a stage's target stands for right now: the people or objects, the rival, how many.</summary>
    internal sealed class Resolved
    {
        public string Rival;
        public readonly List<Agent> Agents = new List<Agent>();
        public readonly List<ObjectReal> Objects = new List<ObjectReal>();
        public Agent Holder;
        public ObjectReal HolderObject;
        public int Needed = 1;
        /// <summary>The <c>{target}</c> text.</summary>
        public string Target = "";
        public string Error;
    }

    internal sealed class Job
    {
        public QuestStage Stage;
        public Resolved R;
        public Agent Player;
        public bool[] AgentDown;
        public bool[] ObjectDown;
        public int Progress;
        public bool RecipientDone;
    }

    internal sealed class Giver
    {
        public Agent Agent;
        public int AgentId;
        public QuestScript Script;
        public bool Radiant;
        public string Faction;
        public bool FactionKnown;
        public int Stage;
        public JobState State;
        public Job Job;
        public string FailText;
        public bool Warned;
        /// <summary>The stage the cached "can offer it now" answer is for, when it was worked out, and the answer.</summary>
        public int OfferStage = -1;
        public float OfferCheckedAt;
        public bool OfferOk;

        public QuestStage Current => Stage < Script.Stages.Count ? Script.Stages[Stage] : null;
    }

    internal static partial class QuestRuntime
    {
        private const float TickSeconds = 0.5f;
        private const int MaxMarkers = 8;
        private const int MaxLogsPerLevel = 40;
        private const float RadiantSpacing = 6.4f;
        private const int MaxTitleInButton = 40;

        public static readonly string[] Traits =
        {
            QuestRules.RadiantGiverTrait, QuestRules.NoQuestsTrait,
            QuestRules.TargetTraitPrefix + "A", QuestRules.TargetTraitPrefix + "B",
            QuestRules.TargetTraitPrefix + "C", QuestRules.TargetTraitPrefix + "D",
        };

        // NPC types that never hand out radiant jobs: they can't talk, won't, or are the danger themselves.
        private static readonly string[] RadiantExcluded =
        {
            "Zombie", "Gorilla", "CopBot", "ButlerBot", "Robot", "Alien", "Ghost", "WerewolfB", "Assassin", "ShapeShifter",
            "Slave",
        };

        // The street's troublemakers: targets for a civilian's Pickpocket and Pest jobs.
        private static readonly string[] ShadyAgents = { "Thief", "Cannibal", "Slavemaster", "Vampire" };

        // What a Sabotage job asks the player to wreck, when a rival owns one.
        private static readonly string[] SabotageObjects =
        {
            "Generator", "Generator2", "PowerBox", "Computer", "SlotMachine", "Jukebox", "Television", "ATMMachine",
            "Refrigerator", "Stove", "VendorCart", "WaterPump", "SecurityCam", "Speaker",
        };

        private static bool radiantOn, starting;
        private static readonly Dictionary<Agent, Giver> givers = new Dictionary<Agent, Giver>();
        private static readonly HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static float nextTick;
        private static readonly CappedLog capped = new CappedLog(MaxLogsPerLevel);

        private static IRckFactions F => RckWorld.Factions;

        // ---- Level state ----

        // QuestsModule.Initialize reads Traits at startup, so this runs before any level; one already seen is replayed.
        static QuestRuntime()
        {
            LevelScope.Ended += OnEnded;
            LevelScope.Loaded += OnLoaded;
            if (LevelScope.Id > 0) OnEnded();
            if (LevelScope.Id > 0 && !LevelScope.Loading) OnLoaded();
        }

        private static void OnEnded()
        {
            radiantOn = false;
            starting = false;
            givers.Clear();
            done.Clear();
            ResetMarks();
            capped.Reset();
        }

        // The givers are set up by the server's next look, not here: that reads the factions, which drop their own
        // state at this same moment.
        private static void OnLoaded()
        {
            GameController gc = GameController.gameController;
            radiantOn = gc != null && gc.challenges != null && gc.challenges.Contains(QuestRules.RadiantMutator);
            starting = true;
        }

        private static GameController Server()
        {
            GameController gc = GameController.gameController;
            if (gc == null || !gc.serverPlayer) return null;
            LevelScope.Check(gc);
            if (starting && gc.loadComplete)
            {
                starting = false;
                StartLevel(gc);
            }
            return gc;
        }

        private static void StartLevel(GameController gc)
        {
            int scripts = 0;
            List<Agent> agents = gc.agentList;
            for (int i = 0; agents != null && i < agents.Count; i++)
            {
                Agent a = agents[i];
                if (IsNpc(a) && QuestRules.IsScript(a.extraVarString4) && GetGiver(a) != null) scripts++;
            }
            int radiant = AssignRadiant(gc);
            if (scripts > 0 || radiant > 0)
                Rck.Log.LogInfo($"Quests: {scripts} scripted and {radiant} radiant giver(s) this level{(radiantOn ? " (radiant quests on)" : "")}.");
        }

        /// <summary>The giver <paramref name="npc"/> is (parsing its script the first time), or null.</summary>
        private static Giver GetGiver(Agent npc)
        {
            if (npc == null) return null;
            if (givers.TryGetValue(npc, out Giver g))
            {
                if (g.AgentId == npc.agentID) return g;
                givers.Remove(npc);
            }
            string text = npc.extraVarString4;
            if (!QuestRules.IsScript(text)) return null;
            IRckFactions f = F;
            QuestScript script = QuestRules.Parse(text, f != null ? (Func<string, string>)f.Resolve : null);
            foreach (string e in script.Errors) Log($"Quests: {Describe(npc)}'s script: {e}.", warn: true);
            foreach (string w in script.Warnings) Log($"Quests: {Describe(npc)}'s script: {w}.", warn: true);
            if (script.Stages.Count == 0 && script.Idle == null)
                Log($"Quests: {Describe(npc)}'s script has no usable stage and no Idle text; it says nothing.", warn: true);
            return Add(npc, script, radiant: false);
        }

        private static Giver Add(Agent npc, QuestScript script, bool radiant)
        {
            var g = new Giver { Agent = npc, AgentId = npc.agentID, Script = script, Radiant = radiant };
            givers[npc] = g;
            Enter(g, 0);
            return g;
        }

        private static void Enter(Giver g, int index)
        {
            g.Stage = index;
            g.Job = null;
            if (index >= g.Script.Stages.Count)
            {
                g.State = JobState.Done;
                return;
            }
            g.State = AfterMet(g.Script.Stages[index]) ? JobState.Offered : JobState.Locked;
        }

        private static bool AfterMet(QuestStage s)
        {
            foreach (string id in s.After)
                if (!done.Contains(id)) return false;
            return true;
        }

        private static string FactionOf(Giver g)
        {
            if (g.FactionKnown) return g.Faction;
            IRckFactions f = F;
            if (f == null) return null;
            g.FactionKnown = true;
            IList<string> own = f.FactionsOf(g.Agent);
            foreach (string k in own)
                if (f.RivalsOf(k).Count > 0) return g.Faction = k;
            return g.Faction = own.Count > 0 ? own[0] : null;
        }

        /// <summary>The <c>Quest=</c> level-gate switch: every listed job is done this level.</summary>
        internal static bool GateHolds(string arg)
        {
            if (Server() == null) return false;
            List<string> ids = QuestRules.GateIds(arg);
            if (ids.Count == 0) return false;
            foreach (string id in ids)
                if (!done.Contains(id)) return false;
            return true;
        }

        // ---- Buttons ----

        internal static void Provide(SimpleInteractionProvider<Agent> h)
        {
            Agent npc = h.Object;
            Agent player = h.Agent;
            if (npc == null || player == null || npc == player || npc.isPlayer > 0) return;
            // The script is ours to show; vanilla would print it raw.
            if (QuestRules.IsScript(npc.extraVarString4)) h.RemoveButton("TalkAgent");
            GameController gc = Server();
            if (gc == null || !gc.loadComplete || Fallen(npc) || h.Helper.interactingFar) return;

            AddRecipientButtons(h, npc, player);
            Giver g = GetGiver(npc);
            if (g == null) return;
            relStatus rel = npc.relationships.GetRelCode(player);
            if (rel == relStatus.Hostile || rel == relStatus.Annoyed) return;

            if (g.State == JobState.Locked && AfterMet(g.Current)) g.State = JobState.Offered;
            switch (g.State)
            {
                case JobState.Offered:
                    AddOfferButtons(h, g);
                    break;
                case JobState.Active:
                case JobState.Ready:
                    Job j = g.Job;
                    bool ready = g.State == JobState.Ready
                        || (j.Stage.Type == QuestType.Retrieve && Count(player, j.Stage.Item) >= j.R.Needed);
                    if (ready) h.AddButton(CustomButtons.Report, m => Report(m, g));
                    else h.AddButton(CustomButtons.About, m => About(m, g));
                    break;
                case JobState.Locked:
                    AddTalk(h, g.Current.Text("wait") ?? g.Script.Idle ?? "I might have work for you later. Check back with me.", g);
                    break;
                case JobState.Failed:
                    AddTalk(h, g.FailText ?? g.Script.Idle ?? "You had your chance.", g);
                    break;
                default:
                    AddTalk(h, g.Script.Idle ?? (g.Script.Stages.Count > 0 ? "Thanks again for your help." : null), g);
                    break;
            }
        }

        private static void AddTalk(SimpleInteractionProvider<Agent> h, string text, Giver g)
        {
            if (string.IsNullOrEmpty(text)) return;
            QuestStage s = g.Current ?? (g.Script.Stages.Count > 0 ? g.Script.Stages[g.Script.Stages.Count - 1] : null);
            h.AddButton(CustomButtons.Talk, m => Show(m, Compose(s != null ? QuestRules.Fill(text, Values(g, s, null)) : text, "")));
        }

        private static void AddOfferButtons(SimpleInteractionProvider<Agent> h, Giver g)
        {
            QuestStage s = g.Current;
            Resolved r = Resolve(g, s);
            if (r.Error != null)
            {
                if (!g.Warned)
                {
                    g.Warned = true;
                    Log($"Quests: {Describe(g.Agent)} can't offer stage {s.Number} ({s.Id}) yet: {r.Error}.", warn: true);
                }
                AddTalk(h, s.Text("wait") ?? g.Script.Idle ?? "Nothing for you right now. Come back later.", g);
                return;
            }
            h.AddButton(CustomButtons.Hear, m => Hear(m, g));
            h.AddButton(CustomButtons.Take, " (" + QuestRules.Clamp(s.Title, MaxTitleInButton) + ")", m => Take(m, g));
        }

        private static void AddRecipientButtons(SimpleInteractionProvider<Agent> h, Agent npc, Agent player)
        {
            if (givers.Count == 0) return;
            relStatus rel = npc.relationships.GetRelCode(player);
            if (rel == relStatus.Hostile) return;
            foreach (Giver g in givers.Values)
            {
                Job j = g.Job;
                if (g.State != JobState.Active || j == null || j.RecipientDone || g.Agent == npc) continue;
                QuestStage s = j.Stage;
                if (s.Type != QuestType.Deliver && s.Type != QuestType.Talk) continue;
                if (!j.R.Agents.Contains(npc)) continue;
                string extra = " (" + QuestRules.Clamp(s.Title, MaxTitleInButton) + ")";
                if (s.Type == QuestType.Deliver)
                {
                    if (Count(player, s.Item) >= j.R.Needed) h.AddButton(CustomButtons.HandOver, extra, m => Deliver(m, g));
                }
                else h.AddButton(CustomButtons.PassWord, extra, m => Deliver(m, g));
            }
        }

        private static void Hear(InteractionModel<Agent> m, Giver g)
        {
            QuestStage s = g.Current;
            if (g.State != JobState.Offered || s == null)
            {
                m.StopInteraction();
                return;
            }
            Resolved r = Resolve(g, s);
            if (r.Error != null)
            {
                m.StopInteraction();
                return;
            }
            Func<string, string> v = Values(g, s, r);
            string offer = QuestRules.Fill(s.Text("offer") ?? QuestRules.DefaultText(s.Type, "offer", s.AutoReport), v);
            Show(m, Compose(offer, JobLine(s, r, null) + RewardLine(g, s)));
        }

        private static void Take(InteractionModel<Agent> m, Giver g)
        {
            QuestStage s = g.Current;
            Agent player = m.Agent;
            if (g.State != JobState.Offered || s == null)
            {
                m.StopInteraction();
                return;
            }
            Resolved r = Resolve(g, s);
            if (r.Error != null || !Accept(g, s, r, player))
            {
                m.StopInteraction();
                return;
            }
            Func<string, string> v = Values(g, s, r);
            string accept = QuestRules.Fill(s.Text("accept") ?? QuestRules.DefaultText(s.Type, "accept", s.AutoReport), v);
            Show(m, Compose(accept, JobLine(s, r, null)));
        }

        private static void About(InteractionModel<Agent> m, Giver g)
        {
            Job j = g.Job;
            if (j == null || g.State != JobState.Active)
            {
                m.StopInteraction();
                return;
            }
            QuestStage s = j.Stage;
            string remind = QuestRules.Fill(s.Text("remind") ?? QuestRules.DefaultText(s.Type, "remind", s.AutoReport), Values(g, s, j.R));
            Show(m, Compose(remind, JobLine(s, j.R, j)));
        }

        private static void Report(InteractionModel<Agent> m, Giver g)
        {
            Job j = g.Job;
            Agent player = m.Agent;
            if (j == null || (g.State != JobState.Ready && !(g.State == JobState.Active && j.Stage.Type == QuestType.Retrieve)))
            {
                m.StopInteraction();
                return;
            }
            QuestStage s = j.Stage;
            if (s.Type == QuestType.Retrieve && !Take(player, s.Item, j.R.Needed))
            {
                m.StopInteraction();
                return;
            }
            string text = QuestRules.Fill(s.Text("done") ?? QuestRules.DefaultText(s.Type, "done", s.AutoReport), Values(g, s, j.R));
            string reward = RewardLine(g, s);
            Pay(g, j, player);
            Show(m, Compose(text, reward));
        }

        /// <summary>A Deliver or Talk recipient's button.</summary>
        private static void Deliver(InteractionModel<Agent> m, Giver g)
        {
            Job j = g.Job;
            Agent player = m.Agent;
            if (j == null || g.State != JobState.Active || j.RecipientDone)
            {
                m.StopInteraction();
                return;
            }
            QuestStage s = j.Stage;
            if (s.Type == QuestType.Deliver && !Take(player, s.Item, j.R.Needed))
            {
                m.StopInteraction();
                return;
            }
            j.RecipientDone = true;
            SyncSoon();
            Resolved named = j.R;
            // The recipient speaks: {target} is the one handed to, whoever else was in the set.
            if (named.Agents.Count > 1)
            {
                named = new Resolved { Rival = j.R.Rival, Needed = j.R.Needed, Target = NameFor(m.Object) };
                named.Agents.Add(m.Object);
            }
            string text = QuestRules.Fill(s.Text("targettext") ?? QuestRules.DefaultText(s.Type, "targettext", s.AutoReport), Values(g, s, named));
            string reward = "";
            if (s.AutoReport)
            {
                reward = RewardLine(g, s);
                Pay(g, j, player);
            }
            else Ready(g, j);
            Show(m, Compose(text, reward));
        }

        private static void Show(InteractionModel<Agent> m, string text)
        {
            m.Object.ShowBigImage(text, string.Empty, null);
            m.Agent.worldSpaceGUI?.HideObjectButtons();
        }

        /// <summary>The designer's text, cut to leave room for the job and reward lines under it.</summary>
        private static string Compose(string body, string suffix)
        {
            body = (body ?? "").Trim();
            suffix = suffix ?? "";
            string text = QuestRules.Clamp(body, Math.Max(150, QuestRules.MaxText - suffix.Length)) + suffix;
            return QuestRules.Clamp(text.Trim(), QuestRules.MaxText);
        }

        private static string JobLine(QuestStage s, Resolved r, Job j)
        {
            string line = "\n\nJob: " + QuestRules.Capitalize(Objective(s, r)) + ".";
            if (j != null && r.Needed > 1)
            {
                int have = s.Type == QuestType.Retrieve ? Math.Min(Count(j.Player, s.Item), r.Needed) : j.Progress;
                line += $" Progress: {have.ToString(CultureInfo.InvariantCulture)} of {r.Needed.ToString(CultureInfo.InvariantCulture)}.";
            }
            return line;
        }

        private static string RewardLine(Giver g, QuestStage s)
        {
            string text = QuestRules.RewardText(s.Rewards, ItemName, StandingName(g));
            return text.Length == 0 ? "" : "\n" + text;
        }

        private static string StandingName(Giver g)
        {
            string key = FactionOf(g);
            return key != null && F != null ? F.Plural(key) : null;
        }

        // ---- Resolving targets ----

        private static Resolved Resolve(Giver g, QuestStage s)
        {
            var r = new Resolved();
            GameController gc = GameController.gameController;
            IRckFactions f = F;
            QuestTarget t = s.Target;
            string faction = FactionOf(g);
            IList<string> rivals = faction != null && f != null ? f.RivalsOf(faction) : new List<string>();
            List<Agent> agents = gc.agentList;
            bool needsFactions = t.Kind == TargetKind.Leader || t.Kind == TargetKind.Faction || (t.Kind == TargetKind.Object && t.Owner != null && t.Owner != "Giver");
            if (needsFactions && f == null)
            {
                r.Error = "faction targets need RCK.Social";
                return r;
            }

            switch (t.Kind)
            {
                case TargetKind.Label:
                    string trait = QuestRules.TargetTraitPrefix + t.Value;
                    foreach (Agent a in agents)
                        if (IsNpc(a) && a != g.Agent && AgentTraits.Has(a, trait)) r.Agents.Add(a);
                    break;
                case TargetKind.Agent:
                    foreach (Agent a in agents)
                        if (IsNpc(a) && a != g.Agent && (string.Equals(a.agentRealName, t.Value, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(a.agentName, t.Value, StringComparison.OrdinalIgnoreCase)))
                            r.Agents.Add(a);
                    break;
                case TargetKind.AgentId:
                    foreach (Agent a in agents)
                        if (IsNpc(a) && a != g.Agent && a.agentID == t.Id) r.Agents.Add(a);
                    break;
                case TargetKind.Leader:
                    string leaderOf = t.Value;
                    if (leaderOf == "Rival")
                    {
                        leaderOf = null;
                        foreach (string k in rivals)
                            if (HasLivingLeader(k)) { leaderOf = k; break; }
                        if (leaderOf == null)
                        {
                            r.Error = faction == null ? "the giver belongs to no faction, so it has no rival" : "no rival faction has a leader standing";
                            return r;
                        }
                    }
                    r.Rival = leaderOf;
                    foreach (Agent a in agents)
                        if (IsNpc(a) && a != g.Agent && f.IsLeader(a, leaderOf)) r.Agents.Add(a);
                    break;
                case TargetKind.Faction:
                    string of = t.Value;
                    if (of == "Rival")
                    {
                        of = null;
                        foreach (string k in rivals)
                            if (f.MembersOf(k).Count > 0) { of = k; break; }
                        if (of == null)
                        {
                            r.Error = faction == null ? "the giver belongs to no faction, so it has no rival" : "no rival faction has members standing";
                            return r;
                        }
                    }
                    r.Rival = of;
                    foreach (Agent a in f.MembersOf(of))
                        if (a != g.Agent) r.Agents.Add(a);
                    break;
                case TargetKind.Object:
                    if (t.Owner == "Rival")
                    {
                        foreach (string k in rivals)
                        {
                            FindObjects(t.Value, OwnersOf(k, null), r.Objects);
                            if (r.Objects.Exists(o => !Gone(o))) { r.Rival = k; break; }
                            r.Objects.Clear();
                        }
                        if (r.Rival == null)
                        {
                            r.Error = faction == null ? "the giver belongs to no faction, so it has no rival" : $"no rival faction owns a {t.Value}";
                            return r;
                        }
                    }
                    else
                    {
                        HashSet<long> owners = t.Owner == null ? null : t.Owner == "Giver" ? OwnersOf(null, g.Agent) : OwnersOf(t.Owner, null);
                        if (t.Owner != null && t.Owner != "Giver") r.Rival = t.Owner;
                        FindObjects(t.Value, owners, r.Objects);
                    }
                    break;
            }
            if (r.Rival == null && rivals.Count > 0) r.Rival = rivals[0];

            int living = 0;
            foreach (Agent a in r.Agents)
                if (!Fallen(a)) living++;
            switch (s.Type)
            {
                case QuestType.Kill:
                    if (r.Agents.Count == 0) r.Error = $"nobody matches {t}";
                    else if (t.Kind == TargetKind.Faction)
                    {
                        r.Agents.RemoveAll(Fallen);
                        r.Needed = Math.Min(s.Count > 0 ? s.Count : QuestRules.DefaultFactionCount, living);
                        if (living == 0) r.Error = $"no member of {r.Rival} is left standing";
                    }
                    else r.Needed = s.Count > 0 ? Math.Min(s.Count, r.Agents.Count) : r.Agents.Count;
                    break;
                case QuestType.Destroy:
                    if (r.Objects.Count == 0) r.Error = $"nothing matches {t}";
                    else r.Needed = s.Count > 0 ? Math.Min(s.Count, r.Objects.Count) : r.Objects.Count;
                    break;
                case QuestType.Retrieve:
                    r.Needed = s.Count > 0 ? s.Count : 1;
                    if (t.IsAgentKind)
                    {
                        foreach (Agent a in r.Agents)
                        {
                            if (Fallen(a)) continue;
                            if (r.Holder == null || (t.Kind == TargetKind.Faction && f.IsLeader(r.Holder, r.Rival) && !f.IsLeader(a, r.Rival))) r.Holder = a;
                        }
                        if (r.Holder == null) r.Error = $"nobody matching {t} is left to hold the {s.Item}";
                    }
                    else if (t.Kind == TargetKind.Object)
                    {
                        r.HolderObject = r.Objects.Find(o => !Gone(o));
                        if (r.HolderObject == null) r.Error = $"nothing matching {t} is left to hold the {s.Item}";
                    }
                    break;
                case QuestType.Deliver:
                case QuestType.Talk:
                    r.Agents.RemoveAll(Fallen);
                    if (r.Agents.Count == 0) r.Error = $"nobody matching {t} is left to receive it";
                    r.Needed = s.Type == QuestType.Deliver && s.Count > 0 ? s.Count : 1;
                    break;
            }
            if (r.Error == null) r.Target = TargetName(s, r);
            return r;
        }

        private static bool HasLivingLeader(string key)
        {
            foreach (Agent a in F.MembersOf(key))
                if (F.IsLeader(a, key)) return true;
            return false;
        }

        /// <summary>The (ownerID, chunk) pairs whose objects a faction's members, or one agent, own.</summary>
        private static HashSet<long> OwnersOf(string key, Agent one)
        {
            var owners = new HashSet<long>();
            if (one != null)
            {
                if (one.ownerID > 0) owners.Add(OwnerKey(one.ownerID, one.startingChunk));
                return owners;
            }
            foreach (Agent a in GameController.gameController.agentList)
                if (IsNpc(a) && a.ownerID > 0 && F.IsMember(a, key)) owners.Add(OwnerKey(a.ownerID, a.startingChunk));
            return owners;
        }

        private static long OwnerKey(int owner, int chunk) => ((long)owner << 32) | (uint)chunk;

        private static void FindObjects(string name, HashSet<long> owners, List<ObjectReal> into)
        {
            foreach (ObjectReal o in GameController.gameController.objectRealList)
            {
                if (o == null || !string.Equals(o.objectName, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (owners != null && (o.owner <= 0 || !owners.Contains(OwnerKey(o.owner, o.startingChunk)))) continue;
                into.Add(o);
            }
        }

        private static string TargetName(QuestStage s, Resolved r)
        {
            IRckFactions f = F;
            if (s.Type == QuestType.Retrieve)
            {
                if (r.Holder != null) return NameFor(r.Holder);
                if (r.HolderObject != null) return "the " + ObjectName(r.HolderObject.objectName);
            }
            switch (s.Target.Kind)
            {
                case TargetKind.Faction:
                    return f.Plural(r.Rival);
                case TargetKind.Leader:
                    bool generic = true;
                    foreach (Agent a in r.Agents)
                        if (!IsGeneric(a)) generic = false;
                    if (generic && r.Agents.Count > 0)
                    {
                        string name = f.Display(r.Rival);
                        bool proper = name.StartsWith("The ", StringComparison.OrdinalIgnoreCase);
                        if (r.Agents.Count == 1) return proper ? "the leader of " + name : $"the {name} leader";
                        return proper ? "the leaders of " + name : $"the {name} leaders";
                    }
                    return AgentNames(r.Agents);
                case TargetKind.Object:
                    var names = new List<KeyValuePair<string, bool>>();
                    foreach (ObjectReal o in r.Objects) names.Add(new KeyValuePair<string, bool>(ObjectName(o.objectName), true));
                    return QuestRules.Names(names);
                case TargetKind.None:
                    return "";
                default:
                    return AgentNames(r.Agents);
            }
        }

        private static string Objective(QuestStage s, Resolved r)
        {
            string holder = null;
            bool holderIsObject = false;
            if (s.Type == QuestType.Retrieve && (r.Holder != null || r.HolderObject != null))
            {
                holder = r.Target;
                holderIsObject = r.HolderObject != null;
            }
            int set = s.Type == QuestType.Destroy ? r.Objects.Count : r.Agents.Count;
            return QuestRules.Objective(s.Type, s.Target.Kind, r.Needed, set, r.Target, ItemName(s.Item), holder, holderIsObject);
        }

        private static Func<string, string> Values(Giver g, QuestStage s, Resolved r)
        {
            return name =>
            {
                switch (name)
                {
                    case "giver": return NameFor(g.Agent);
                    case "faction":
                        string key = FactionOf(g);
                        return key != null && F != null ? F.Plural(key) : "my people";
                    case "rival": return r != null && r.Rival != null && F != null ? F.Plural(r.Rival) : "the competition";
                    case "target": return r != null && r.Target.Length > 0 ? r.Target : "someone";
                    case "item": return s.Item != null ? ItemName(s.Item) : "package";
                    case "count": return (r != null ? r.Needed : Math.Max(1, s.Count)).ToString(CultureInfo.InvariantCulture);
                    case "reward": return QuestRules.RewardList(s.Rewards, ItemName, StandingName(g));
                    case "objective": return r != null ? Objective(s, r) : "";
                    default: return null;
                }
            };
        }

        // ---- Accepting, progress, paying ----

        private static bool Accept(Giver g, QuestStage s, Resolved r, Agent player)
        {
            if (s.Type == QuestType.Deliver && !GivePlayer(player, s.Item, r.Needed, refuseWhenFull: true)) return false;
            var j = new Job
            {
                Stage = s,
                R = r,
                Player = player,
                AgentDown = new bool[r.Agents.Count],
                ObjectDown = new bool[r.Objects.Count],
            };
            if (s.Type == QuestType.Retrieve)
            {
                if (r.Holder != null) PlaceOn(r.Holder, s.Item, r.Needed);
                else if (r.HolderObject != null) PlaceIn(r.HolderObject, s.Item, r.Needed);
            }
            g.Job = j;
            g.State = JobState.Active;
            SyncSoon();

            Play(player, "QuestAccept");
            Text(player, "Buff", "New job: " + QuestRules.Clamp(s.Title, MaxTitleInButton));
            Log($"Quests: {Describe(player)} took {Describe(g.Agent)}'s job {s.Id} ({s.Type} {s.Target}, {r.Needed} needed).");
            Evaluate(g);
            return true;
        }

        /// <summary>Twice a second on the server: counts what has fallen, fails jobs whose giver is gone or turned, and syncs the map markers.</summary>
        internal static void Tick()
        {
            float now = Time.time;
            if (!Interval.Due(ref nextTick, now, TickSeconds)) return;
            GameController gc = Server();
            if (gc == null || !gc.loadComplete) return;
            List<Giver> list = null;
            foreach (Giver g in givers.Values)
                if (g.State == JobState.Active || g.State == JobState.Ready) (list = list ?? new List<Giver>()).Add(g);
            for (int i = 0; list != null && i < list.Count; i++)
            {
                Giver g = list[i];
                Job j = g.Job;
                if (j == null) continue;
                if (Fallen(g.Agent) || g.Agent.agentID != g.AgentId)
                {
                    Fail(g, "the giver is gone");
                    continue;
                }
                if (j.Player != null && g.Agent.relationships != null && g.Agent.relationships.GetRelCode(j.Player) == relStatus.Hostile)
                {
                    Fail(g, "the giver turned on the player");
                    continue;
                }
                if (g.State == JobState.Active) Evaluate(g);
            }
            Sync(gc);
        }

        private static void Evaluate(Giver g)
        {
            Job j = g.Job;
            QuestStage s = j.Stage;
            switch (s.Type)
            {
                case QuestType.Kill:
                    j.Progress = 0;
                    for (int i = 0; i < j.R.Agents.Count; i++)
                    {
                        if (!j.AgentDown[i] && Fallen(j.R.Agents[i])) j.AgentDown[i] = true;
                        if (j.AgentDown[i]) j.Progress++;
                    }
                    if (j.Progress >= j.R.Needed) Complete(g);
                    break;
                case QuestType.Destroy:
                    j.Progress = 0;
                    for (int i = 0; i < j.R.Objects.Count; i++)
                    {
                        if (!j.ObjectDown[i] && Gone(j.R.Objects[i])) j.ObjectDown[i] = true;
                        if (j.ObjectDown[i]) j.Progress++;
                    }
                    if (j.Progress >= j.R.Needed) Complete(g);
                    break;
                case QuestType.Deliver:
                case QuestType.Talk:
                    if (j.RecipientDone) break;
                    bool anyone = false;
                    foreach (Agent a in j.R.Agents)
                        if (!Fallen(a)) anyone = true;
                    if (!anyone) Fail(g, "nobody is left to receive it");
                    break;
            }
        }

        private static void Complete(Giver g)
        {
            Job j = g.Job;
            if (j.Stage.AutoReport) Pay(g, j, j.Player);
            else Ready(g, j);
        }

        private static void Ready(Giver g, Job j)
        {
            g.State = JobState.Ready;
            SyncSoon();
            Text(j.Player, "Buff", "Job done: report back");
            Log($"Quests: {Describe(g.Agent)}'s job {j.Stage.Id} is done; waiting for the report.");
        }

        private static void Pay(Giver g, Job j, Agent player)
        {
            QuestStage s = j.Stage;
            MakeFriendly(g.Agent, player);
            foreach (QuestReward reward in s.Rewards)
            {
                try
                {
                    switch (reward.Kind)
                    {
                        case RewardKind.Money:
                            player.inventory.AddItem("Money", reward.Amount);
                            break;
                        case RewardKind.Item:
                            GivePlayer(player, reward.Item, reward.Amount, refuseWhenFull: false);
                            break;
                        case RewardKind.XP:
                            player.skillPoints.AddPoints("CompleteMission", 1);
                            break;
                        case RewardKind.Recruit:
                            Recruit(g, player);
                            break;
                        case RewardKind.Standing:
                            string key = FactionOf(g);
                            if (key != null && F != null)
                                Log($"Quests: {F.Befriend(player, key)} member(s) of {key} turned Friendly.");
                            break;
                    }
                }
                catch (Exception e)
                {
                    Log($"Quests: reward {reward.Kind} {reward.Item} for job {s.Id} failed: {e.Message}", warn: true);
                }
            }
            done.Add(s.Id);
            Play(player, "QuestComplete");
            Text(player, "Buff", "Job done: " + QuestRules.Clamp(s.Title, MaxTitleInButton));
            Log($"Quests: {Describe(g.Agent)}'s job {s.Id} paid out.");
            Enter(g, g.Stage + 1);
            SyncSoon();
        }

        private static void Fail(Giver g, string why)
        {
            Job j = g.Job;
            QuestStage s = j != null ? j.Stage : g.Current;
            g.State = JobState.Failed;
            SyncSoon();
            g.FailText = s != null && s.Text("fail") != null ? QuestRules.Fill(s.Text("fail"), Values(g, s, j != null ? j.R : null)) : null;
            Agent player = j != null ? j.Player : null;
            if (player != null)
            {
                Play(player, "QuestFail");
                Text(player, "Debuff", "Job failed: " + QuestRules.Clamp(s.Title, MaxTitleInButton));
            }
            Log($"Quests: {Describe(g.Agent)}'s job {(s != null ? s.Id : "?")} failed: {why}.");
        }

        private static void Recruit(Giver g, Agent player)
        {
            IRckFactions f = F;
            string key = FactionOf(g);
            Agent who = g.Agent;
            if (key != null && f != null && f.IsLeader(who, key))
            {
                who = null;
                float best = float.MaxValue;
                foreach (Agent a in f.MembersOf(key))
                {
                    if (a == g.Agent || f.IsLeader(a, key) || a.employer != null) continue;
                    float d = (a.tr.position - g.Agent.tr.position).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        who = a;
                    }
                }
            }
            if (who == null || Fallen(who) || who.employer != null)
            {
                Text(player, "Debuff", "Nobody to spare");
                return;
            }
            int following = who.FindNumFollowing(player);
            int cap = player.statusEffects.hasTrait("MoreFollowers") ? 3
                : player.statusEffects.hasTrait("ZombieArmy") ? 5
                : player.statusEffects.hasTrait("NoFollowers") ? 0 : 1;
            if (following >= cap)
            {
                Text(player, "Debuff", "No room in your party");
                return;
            }
            MakeFriendly(who, player);
            who.SayDialogue("Joined");
            GameController.gameController.audioHandler.Play(who, "AgentJoin");
            who.agentInteractions.HireAsProtection(who, player);
        }

        private static void MakeFriendly(Agent npc, Agent player)
        {
            if (npc == null || player == null || npc.relationships == null) return;
            Relationship rel = npc.relationships.GetRelationship(player);
            if (rel == null) return;
            relStatus code = rel.relTypeCode;
            if (code == relStatus.Aligned || code == relStatus.Loyal || code == relStatus.Submissive || code == relStatus.Friendly) return;
            rel.relHate = 0;
            rel.relStrikes = 0;
            npc.relationships.SetRel(player, "Friendly");
        }

        // ---- Items ----

        private static InvItem Probe(string name)
        {
            try
            {
                var probe = new InvItem { invItemName = name, itemNetID = -1 };
                probe.ItemSetup(false);
                return probe;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>How many of <paramref name="name"/> the agent carries (a non-stacking item counts once).</summary>
        private static int Count(Agent agent, string name)
        {
            if (agent == null || agent.inventory == null || string.IsNullOrEmpty(name)) return 0;
            int n = 0;
            foreach (InvItem item in agent.inventory.InvItemList)
                if (item != null && item.invItemName == name) n += item.stackable ? item.invItemCount : 1;
            return n;
        }

        private static bool Take(Agent agent, string name, int count)
        {
            if (Count(agent, name) < count) return false;
            foreach (InvItem item in new List<InvItem>(agent.inventory.InvItemList))
            {
                if (count <= 0) break;
                if (item == null || item.invItemName != name) continue;
                int take = item.stackable ? Math.Min(count, item.invItemCount) : 1;
                if (item.stackable && take < item.invItemCount) agent.inventory.SubtractFromItemCount(item, take);
                else agent.inventory.DestroyItem(item);
                count -= take;
            }
            return true;
        }

        /// <summary>
        ///   Gives the player <paramref name="count"/> of an item (0: one full item), dropping what doesn't fit at their
        ///   feet. With <paramref name="refuseWhenFull"/>, a full inventory refuses the first one instead, as vanilla does.
        /// </summary>
        private static bool GivePlayer(Agent player, string name, int count, bool refuseWhenFull)
        {
            InvItem probe = Probe(name);
            if (probe == null)
            {
                Log($"Quests: can't give unknown item {name}.", warn: true);
                return !refuseWhenFull;
            }
            bool nugget = probe.itemType == "Nugget";
            bool stacks = probe.stackable || nugget;
            int copies = stacks ? 1 : Math.Max(1, count);
            int each = stacks ? (count > 0 ? count : nugget ? 1 : Math.Max(1, probe.initCount)) : Math.Max(1, probe.initCount);
            for (int i = 0; i < copies; i++)
            {
                if (!nugget && !player.inventory.hasEmptySlotForItem(probe))
                {
                    if (refuseWhenFull && i == 0)
                    {
                        player.inventory.PlayerFullResponse(player);
                        return false;
                    }
                    var drop = new InvItem { invItemName = name, invItemCount = each };
                    drop.ItemSetup(false);
                    drop.invItemCount = each;
                    GameController.gameController.spawnerMain.SpawnItem(player.tr.position, drop);
                    continue;
                }
                player.inventory.AddItem(name, each);
            }
            return true;
        }

        private static InvItem QuestItem(string name, int count)
        {
            var item = new InvItem { invItemName = name, invItemCount = count };
            item.SetupDetails(false);
            return item;
        }

        // As vanilla places a quest item on an NPC (Quests.cs, the Retrieve set-up).
        private static void PlaceOn(Agent holder, string name, int count)
        {
            InvItem probe = Probe(name);
            if (probe == null || holder.inventory == null) return;
            if (probe.stackable) holder.inventory.AddItem(QuestItem(name, count));
            else for (int i = 0; i < count; i++) holder.inventory.AddItem(QuestItem(name, 1));
        }

        private static void PlaceIn(ObjectReal holder, string name, int count)
        {
            InvItem probe = Probe(name);
            if (probe == null) return;
            InvDatabase inv = holder.objectInvDatabase;
            int copies = probe.stackable ? 1 : count;
            for (int i = 0; i < copies; i++)
            {
                InvItem item = QuestItem(name, probe.stackable ? count : 1);
                if (inv == null)
                {
                    GameController.gameController.spawnerMain.SpawnItem(holder.tr.position, item);
                    continue;
                }
                if (!inv.InvItemList.Contains(null)) inv.InvItemList[0] = new InvItem();
                inv.AddItem(item);
                item.stealable = true;
            }
        }

        // ---- Radiant jobs ----

        private static int AssignRadiant(GameController gc)
        {
            List<Agent> agents = gc.agentList;
            int count = 0;
            for (int i = 0; agents != null && i < agents.Count; i++)
            {
                Agent a = agents[i];
                if (IsNpc(a) && !givers.ContainsKey(a) && AgentTraits.Has(a, QuestRules.RadiantGiverTrait) && Eligible(gc, a, forced: true) && TryRadiant(gc, a))
                    count++;
            }
            if (!radiantOn) return count;

            var candidates = new List<Agent>();
            for (int i = 0; agents != null && i < agents.Count; i++)
                if (IsNpc(agents[i]) && !givers.ContainsKey(agents[i]) && Eligible(gc, agents[i], forced: false)) candidates.Add(agents[i]);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int k = UnityEngine.Random.Range(0, i + 1);
                Agent tmp = candidates[i];
                candidates[i] = candidates[k];
                candidates[k] = tmp;
            }
            // Faction members with a rival first: their jobs feed the war.
            IRckFactions f = F;
            if (f != null)
            {
                var withRival = new Dictionary<Agent, bool>();
                foreach (Agent a in candidates)
                {
                    bool rival = false;
                    foreach (string key in f.FactionsOf(a))
                        if (f.RivalsOf(key).Count > 0) { rival = true; break; }
                    withRival[a] = rival;
                }
                var ordered = new List<Agent>();
                foreach (Agent a in candidates) if (withRival[a]) ordered.Add(a);
                foreach (Agent a in candidates) if (!withRival[a]) ordered.Add(a);
                candidates = ordered;
            }

            int total = 0;
            foreach (Giver g in givers.Values) if (g.Radiant) total++;
            foreach (Agent a in candidates)
            {
                if (total >= QuestRules.MaxRadiantGivers) break;
                if (TooClose(a)) continue;
                if (!TryRadiant(gc, a)) continue;
                total++;
                count++;
            }
            return count;
        }

        private static bool TooClose(Agent a)
        {
            foreach (Giver g in givers.Values)
                if (g.Radiant && g.Agent != null && (g.Agent.tr.position - a.tr.position).magnitude < RadiantSpacing) return true;
            return false;
        }

        private static bool Eligible(GameController gc, Agent a, bool forced)
        {
            if (Fallen(a) || a.employer != null || a.questGiverQuest != null || a.questEnderQuest != null || a.isBigQuestObject) return false;
            if (a.killForQuest != null || a.rescueForQuest != null || a.prisoner > 0) return false;
            if (AgentTraits.Has(a, QuestRules.NoQuestsTrait) || Array.IndexOf(RadiantExcluded, a.agentName) >= 0) return false;
            if (QuestRules.IsScript(a.extraVarString4)) return false;
            if (!forced && !string.IsNullOrEmpty(a.extraVarString4)) return false;
            foreach (Agent p in gc.playerAgentList)
            {
                if (p == null || a.relationships == null) continue;
                relStatus rel = a.relationships.GetRelCode(p);
                if (rel == relStatus.Hostile || rel == relStatus.Annoyed) return false;
            }
            return true;
        }

        private sealed class Option
        {
            public QuestRules.RadiantTemplate T;
            public string Target;
            public string Item;
            public int Count;
        }

        private static bool TryRadiant(GameController gc, Agent a)
        {
            IRckFactions f = F;
            var probe = new Giver { Agent = a, AgentId = a.agentID };
            string faction = FactionOf(probe);
            var options = new List<Option>();
            void Offer(string template, string target, string item = null, int count = 0)
                => options.Add(new Option { T = QuestRules.FindRadiant(template), Target = target, Item = item, Count = count });

            if (faction != null && f != null)
            {
                IList<string> rivals = f.RivalsOf(faction);
                if (rivals.Count > 0)
                {
                    string rival = rivals[0];
                    IList<Agent> members = f.MembersOf(rival);
                    Agent leader = null;
                    var rank = new List<Agent>();
                    foreach (Agent m in members)
                    {
                        if (f.IsLeader(m, rival)) leader = leader ?? m;
                        else rank.Add(m);
                    }
                    Agent holder = rank.Count > 0 ? rank[UnityEngine.Random.Range(0, rank.Count)] : null;
                    if (leader != null) Offer("Hit", "Leader:" + rival);
                    if (members.Count > 0) Offer("Thin", "Faction:" + rival, count: QuestRules.DefaultFactionCount);
                    if (holder != null) Offer("Recover", "Agent:#" + holder.agentID.ToString(CultureInfo.InvariantCulture), RandomItem());
                    string target = SabotageTarget(rival);
                    if (target != null) Offer("Sabotage", "Object:" + target + "@" + rival, count: 1);
                }
                Agent fellow = RandomAgent(gc, x => x != a && x.employer == null && f.IsMember(x, faction) && !f.IsLeader(x, faction)
                    && (x.tr.position - a.tr.position).magnitude > RadiantSpacing);
                if (fellow != null)
                {
                    string id = "Agent:#" + fellow.agentID.ToString(CultureInfo.InvariantCulture);
                    Offer("Courier", id, RandomItem());
                    Offer("Word", id);
                }
            }
            else
            {
                Agent shady = RandomAgent(gc, x => x != a && Array.IndexOf(ShadyAgents, x.agentName) >= 0);
                if (shady != null)
                {
                    string id = "Agent:#" + shady.agentID.ToString(CultureInfo.InvariantCulture);
                    Offer("Pickpocket", id, RandomItem());
                    Offer("Pest", id);
                }
                Agent other = RandomAgent(gc, x => x != a && x.employer == null && Array.IndexOf(ShadyAgents, x.agentName) < 0
                    && Array.IndexOf(RadiantExcluded, x.agentName) < 0 && (f == null || f.FactionsOf(x).Count == 0)
                    && (x.tr.position - a.tr.position).magnitude > RadiantSpacing);
                if (other != null)
                {
                    string id = "Agent:#" + other.agentID.ToString(CultureInfo.InvariantCulture);
                    Offer("Parcel", id, RandomItem());
                    Offer("CheckIn", id);
                }
            }
            options.RemoveAll(o => o.T == null);
            if (options.Count == 0) return false;

            Option pick = options[UnityEngine.Random.Range(0, options.Count)];
            int money = QuestRules.RadiantPay(LevelScope.Level, pick.T.Pay);
            string text = QuestRules.RadiantScript(pick.T, UnityEngine.Random.Range(0, pick.T.Variants.Length),
                "radiant-" + a.agentID.ToString(CultureInfo.InvariantCulture), pick.Target, pick.Item, pick.Count, money);
            QuestScript script = QuestRules.Parse(text, f != null ? (Func<string, string>)f.Resolve : null);
            if (script.Errors.Count > 0 || script.Stages.Count != 1)
            {
                Log($"Quests: radiant {pick.T.Name} job for {Describe(a)} didn't parse: {string.Join("; ", script.Errors.ToArray())}", warn: true);
                return false;
            }
            // A radiant giver with its own Talk text says it again once the job is over.
            if (!string.IsNullOrEmpty(a.extraVarString4)) script.Idle = a.extraVarString4;
            Giver g = Add(a, script, radiant: true);
            g.Faction = probe.Faction;
            g.FactionKnown = probe.FactionKnown;
            Log($"Quests: {Describe(a)} offers a radiant {pick.T.Name} job ({pick.Target}, ${money}).");
            return true;
        }

        private static string RandomItem() => QuestRules.QuestItems[UnityEngine.Random.Range(0, QuestRules.QuestItems.Length)];

        private static Agent RandomAgent(GameController gc, Predicate<Agent> ok)
        {
            var pool = new List<Agent>();
            foreach (Agent x in gc.agentList)
                if (IsNpc(x) && !Fallen(x) && ok(x)) pool.Add(x);
            return pool.Count == 0 ? null : pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        private static string SabotageTarget(string rival)
        {
            var found = new List<ObjectReal>();
            HashSet<long> owners = OwnersOf(rival, null);
            foreach (string name in SabotageObjects) FindObjects(name, owners, found);
            found.RemoveAll(Gone);
            return found.Count == 0 ? null : found[UnityEngine.Random.Range(0, found.Count)].objectName;
        }

        // ---- Helpers ----

        private static bool IsNpc(Agent a) => a != null && a.isPlayer == 0 && !a.objectAgent;

        private static bool Fallen(Agent a) => a == null || a.dead || a.arrested || a.ghost || (a.zombified && a.agentName != "Zombie");

        private static bool Gone(ObjectReal o) => o == null || o.destroyed || o.destroying;

        private static bool IsGeneric(Agent a)
        {
            string type = AgentType(a);
            return string.IsNullOrEmpty(a.agentRealName) || a.agentRealName == type;
        }

        private static string AgentType(Agent a)
        {
            try
            {
                string name = GameController.gameController.nameDB.GetName(a.agentName, "Agent");
                return string.IsNullOrEmpty(name) ? a.agentName : name;
            }
            catch
            {
                return a.agentName;
            }
        }

        /// <summary><c>Big Tony</c>, or <c>the Bartender</c> for an NPC without a name of its own.</summary>
        private static string NameFor(Agent a) => a == null ? "someone" : IsGeneric(a) ? "the " + AgentType(a) : a.agentRealName;

        private static string AgentNames(List<Agent> agents)
        {
            var names = new List<KeyValuePair<string, bool>>();
            foreach (Agent a in agents)
                if (a != null) names.Add(IsGeneric(a) ? new KeyValuePair<string, bool>(AgentType(a), true) : new KeyValuePair<string, bool>(a.agentRealName, false));
            return names.Count == 0 ? "someone" : QuestRules.Names(names);
        }

        private static string ItemName(string item)
        {
            if (string.IsNullOrEmpty(item)) return "";
            try
            {
                string name = GameController.gameController.nameDB.GetName(item, "Item");
                return string.IsNullOrEmpty(name) ? item : name;
            }
            catch
            {
                return item;
            }
        }

        private static string ObjectName(string obj)
        {
            try
            {
                string name = GameController.gameController.nameDB.GetName(obj, "Object");
                return string.IsNullOrEmpty(name) ? obj : name;
            }
            catch
            {
                return obj;
            }
        }

        private static void Play(Agent agent, string clip)
        {
            try { GameController.gameController.audioHandler.Play(agent, clip); }
            catch (Exception e) { Log($"Quests: sound {clip} failed: {e.Message}", warn: true); }
        }

        private static void Text(Agent player, string type, string text)
        {
            if (player == null) return;
            try { GameController.gameController.spawnerMain.SpawnStatusText(player, type, text); }
            catch (Exception e) { Log($"Quests: status text failed: {e.Message}", warn: true); }
        }

        private static void Log(string line, bool warn = false)
        {
            if (warn) capped.Warn(line);
            else capped.Info(line);
        }

        /// <summary>A pooled agent coming back as someone new: drop it from every job it was part of.</summary>
        internal static void Forget(Agent agent)
        {
            if (agent == null) return;
            ForgetMark(agent);
            if (givers.Count == 0) return;
            if (givers.TryGetValue(agent, out Giver own))
            {
                if (own.State == JobState.Active || own.State == JobState.Ready) Fail(own, "the giver is gone");
                givers.Remove(agent);
            }
            foreach (Giver g in givers.Values)
            {
                Job j = g.Job;
                if (j == null) continue;
                for (int i = 0; i < j.R.Agents.Count; i++)
                {
                    if (j.R.Agents[i] != agent) continue;
                    j.R.Agents[i] = null;
                    if (i < j.AgentDown.Length) j.AgentDown[i] = true;
                }
                if (j.R.Holder == agent) j.R.Holder = null;
            }
        }
    }

    /// <summary>Quest buttons. Each needs a [ButtonLabel] or a vanilla Interface label (checked by tools\ButtonCheck).</summary>
    internal static class CustomButtons
    {
        [ButtonLabel("Hear them out")] public const string Hear = "RCK_QuestHear";
        [ButtonLabel("Take the job")] public const string Take = "RCK_QuestTake";
        [ButtonLabel("About the job")] public const string About = "RCK_QuestAbout";
        [ButtonLabel("Report back")] public const string Report = "RCK_QuestReport";
        [ButtonLabel("Talk")] public const string Talk = "RCK_QuestTalk";
        [ButtonLabel("Hand over")] public const string HandOver = "RCK_QuestHandOver";
        [ButtonLabel("Pass on the word")] public const string PassWord = "RCK_QuestPassWord";
    }

    [HarmonyPatch(typeof(BrainUpdate), nameof(BrainUpdate.MyUpdate))]
    internal static class BrainUpdate_MyUpdate_Quests_Patch
    {
        private static bool broken;

        private static void Postfix()
        {
            if (broken) return;
            try
            {
                QuestRuntime.Refresh();
                QuestRuntime.Tick();
            }
            catch (Exception e)
            {
                broken = true;
                Rck.Log.LogError($"Quests: tick failed, quests stop updating until restart: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.RecycleAwake))]
    internal static class Agent_RecycleAwake_Quests_Patch
    {
        private static void Postfix(Agent __instance)
        {
            try { QuestRuntime.Forget(__instance); }
            catch (Exception e) { Rck.Log.LogError($"Quests: forgetting a recycled agent failed: {e.Message}"); }
        }
    }
}
