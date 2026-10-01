#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static RCK.AgentText;

namespace RCK.Quests
{
    /// <summary>
    ///   Map markers for RCK jobs. They never go in the quest list: each giver, recipient and target gets vanilla's
    ///   non-quest marker (the one <see cref="PlayfieldObject.MinimapDisplay()"/> makes), which vanilla keeps invisible
    ///   for ordinary NPCs, restyled with vanilla's quest sprites: "!" for a job on offer (faded while it runs), a tick
    ///   to report back, a yellow arrow for a target or recipient. Server only; the host sees them in multiplayer.
    /// </summary>
    internal static partial class QuestRuntime
    {
        private const float RefreshSeconds = 0.1f;
        private const float OfferRecheckSeconds = 3f;
        private const int MaxEnsureAttempts = 3;

        private sealed class MarkState
        {
            public QuestMarker Marker;
            public MarkerKind Kind;
            public string Label;
            public string Short;
            public bool HadVanilla;
        }

        private struct MarkWish
        {
            public MarkerKind Kind;
            public string Label;
            public string Short;
        }

        private static readonly Dictionary<PlayfieldObject, MarkState> marks = new Dictionary<PlayfieldObject, MarkState>();
        private static readonly Dictionary<PlayfieldObject, int> markFailures = new Dictionary<PlayfieldObject, int>();
        private static float nextRefresh;
        private static bool syncDue;

        private static void ResetMarks()
        {
            marks.Clear();
            markFailures.Clear();
            syncDue = false;
        }

        /// <summary>Something changed a job: sync the markers on the next refresh, not the next tick.</summary>
        private static void SyncSoon() => syncDue = true;

        // ---- Which objects want which marker ----

        private static void Sync(GameController gc)
        {
            syncDue = false;
            if (gc == null || !gc.loadComplete || (givers.Count == 0 && marks.Count == 0)) return;
            var want = new Dictionary<PlayfieldObject, MarkWish>();
            Agent player = gc.playerAgent;
            float now = Time.time;
            foreach (Giver g in givers.Values)
            {
                try { Wishes(g, player, now, want); }
                catch (Exception e) { Log($"Quests: couldn't work out {Describe(g.Agent)}'s markers: {e.Message}", warn: true); }
            }

            List<PlayfieldObject> drop = null;
            foreach (KeyValuePair<PlayfieldObject, MarkState> kv in marks)
                if (!want.ContainsKey(kv.Key)) (drop = drop ?? new List<PlayfieldObject>()).Add(kv.Key);
            for (int i = 0; drop != null && i < drop.Count; i++)
            {
                MarkState ms = marks[drop[i]];
                marks.Remove(drop[i]);
                Unmark(drop[i], ms);
            }

            foreach (KeyValuePair<PlayfieldObject, MarkWish> kv in want)
            {
                if (marks.TryGetValue(kv.Key, out MarkState ms))
                {
                    ms.Kind = kv.Value.Kind;
                    ms.Label = kv.Value.Label;
                    ms.Short = kv.Value.Short;
                }
                else Ensure(kv.Key, kv.Value);
            }
        }

        private static void Wishes(Giver g, Agent player, float now, Dictionary<PlayfieldObject, MarkWish> want)
        {
            Agent a = g.Agent;
            if (Fallen(a) || a.agentID != g.AgentId) return;
            relStatus rel = player != null && a.relationships != null ? a.relationships.GetRelCode(player) : relStatus.Neutral;
            bool cold = rel == relStatus.Hostile || rel == relStatus.Annoyed;
            if (g.State == JobState.Locked && g.Current != null && AfterMet(g.Current)) g.State = JobState.Offered;

            Job j = g.Job;
            switch (g.State)
            {
                case JobState.Offered:
                    QuestStage offer = g.Current;
                    if (offer == null || cold || !CanOffer(g, offer, now)) return;
                    Want(want, a, MarkerKind.Offer, NameFor(a), offer.Title);
                    return;
                case JobState.Ready:
                    if (j != null && !cold) Want(want, a, MarkerKind.Report, NameFor(a), j.Stage.Title);
                    return;
                case JobState.Active:
                    if (j == null) return;
                    break;
                default:
                    return;
            }

            QuestStage s = j.Stage;
            bool haveAll = s.Type == QuestType.Retrieve && Count(j.Player, s.Item) >= j.R.Needed;
            if (haveAll && !cold) Want(want, a, MarkerKind.Report, NameFor(a), s.Title);
            else Want(want, a, MarkerKind.Running, NameFor(a), s.Title);

            int marked = 0;
            switch (s.Type)
            {
                case QuestType.Kill:
                    for (int i = 0; i < j.R.Agents.Count && marked < MaxMarkers; i++)
                    {
                        Agent t = j.R.Agents[i];
                        if ((i < j.AgentDown.Length && j.AgentDown[i]) || Fallen(t)) continue;
                        Want(want, t, MarkerKind.Target, NameFor(t), s.Title);
                        marked++;
                    }
                    break;
                case QuestType.Deliver:
                case QuestType.Talk:
                    if (j.RecipientDone) break;
                    for (int i = 0; i < j.R.Agents.Count && marked < MaxMarkers; i++)
                    {
                        Agent t = j.R.Agents[i];
                        if (Fallen(t)) continue;
                        Want(want, t, MarkerKind.Recipient, NameFor(t), s.Title);
                        marked++;
                    }
                    break;
                case QuestType.Destroy:
                    for (int i = 0; i < j.R.Objects.Count && marked < MaxMarkers; i++)
                    {
                        ObjectReal o = j.R.Objects[i];
                        if ((i < j.ObjectDown.Length && j.ObjectDown[i]) || Gone(o)) continue;
                        Want(want, o, MarkerKind.Target, ObjectName(o.objectName), s.Title);
                        marked++;
                    }
                    break;
                case QuestType.Retrieve:
                    if (haveAll) break;
                    if (j.R.Holder != null && !Fallen(j.R.Holder)) Want(want, j.R.Holder, MarkerKind.Target, NameFor(j.R.Holder), s.Title);
                    else if (!Gone(j.R.HolderObject)) Want(want, j.R.HolderObject, MarkerKind.Target, ObjectName(j.R.HolderObject.objectName), s.Title);
                    break;
            }
        }

        /// <summary>Whether <paramref name="g"/> could hand out <paramref name="s"/> right now; worked out every few seconds.</summary>
        private static bool CanOffer(Giver g, QuestStage s, float now)
        {
            if (g.OfferStage == g.Stage && now >= g.OfferCheckedAt && now < g.OfferCheckedAt + OfferRecheckSeconds) return g.OfferOk;
            g.OfferStage = g.Stage;
            g.OfferCheckedAt = now;
            g.OfferOk = Resolve(g, s).Error == null;
            return g.OfferOk;
        }

        private static void Want(Dictionary<PlayfieldObject, MarkWish> want, PlayfieldObject po, MarkerKind kind, string name, string title)
        {
            if (po == null || !Markable(po)) return;
            if (want.TryGetValue(po, out MarkWish old) && old.Kind >= kind) return;
            want[po] = new MarkWish
            {
                Kind = kind,
                Label = QuestRules.MarkerLabel(kind, name, title),
                Short = QuestRules.Capitalize(string.IsNullOrEmpty(name) ? "someone" : name),
            };
        }

        /// <summary>Vanilla's own quest markers win: never restyle an object that has one.</summary>
        private static bool Markable(PlayfieldObject po)
        {
            if (po.questObjectMarker != null) return false;
            if (po.isBigQuestObject && !po.noBigQuestMarker) return false;
            QuestMarker existing = po.nonQuestObjectMarker;
            return existing == null || !existing.isBigQuestMarker;
        }

        // ---- Making and dropping markers ----

        private static void Ensure(PlayfieldObject po, MarkWish w)
        {
            if (!po.gameObject.activeInHierarchy) return;
            if (markFailures.TryGetValue(po, out int failures) && failures >= MaxEnsureAttempts) return;
            var ms = new MarkState { Kind = w.Kind, Label = w.Label, Short = w.Short };
            try
            {
                if (po.nonQuestObjectMarker != null) ms.HadVanilla = true;
                else po.MinimapDisplay();
                ms.Marker = po.nonQuestObjectMarker;
            }
            catch (Exception e) { Log($"Quests: couldn't mark {po.name}: {e.Message}", warn: true); }
            if (ms.Marker == null)
            {
                markFailures[po] = failures + 1;
                if (failures + 1 == MaxEnsureAttempts) Log($"Quests: gave up marking {po.name} on the map.", warn: true);
                return;
            }
            marks[po] = ms;
        }

        private static void Unmark(PlayfieldObject po, MarkState ms)
        {
            if (po == null) return;
            try
            {
                QuestMarker marker = ms.Marker;
                if (marker == null || po.nonQuestObjectMarker != marker) return;
                bool seen = marker.playerSeen;
                marker.DestroyMe();
                po.nonQuestObjectMarker = null;
                if (!ms.HadVanilla) return;
                bool intact = po is Agent a ? !a.dead && !a.ghost : !(po is ObjectReal o) || !Gone(o);
                if (!intact || !po.gameObject.activeInHierarchy) return;
                // Put vanilla's marker back as it was.
                po.MinimapDisplay();
                if (po.nonQuestObjectMarker != null) po.nonQuestObjectMarker.playerSeen = seen;
            }
            catch (Exception e) { Log($"Quests: couldn't remove a marker from {po.name}: {e.Message}", warn: true); }
        }

        /// <summary>A pooled agent coming back as someone new: its old marker, if still there, isn't about the new one.</summary>
        private static void ForgetMark(Agent agent)
        {
            markFailures.Remove(agent);
            if (!marks.TryGetValue(agent, out MarkState ms)) return;
            marks.Remove(agent);
            try
            {
                QuestMarker marker = ms.Marker;
                if (marker != null && marker.myObject == agent) marker.DestroyMe();
                if (agent.nonQuestObjectMarker == marker) agent.nonQuestObjectMarker = null;
            }
            catch (Exception e) { Log($"Quests: couldn't drop a recycled agent's marker: {e.Message}", warn: true); }
        }

        // ---- Styling ----

        /// <summary>Ten times a second on the server: keeps each marker looking like a quest marker (vanilla hides them).</summary>
        internal static void Refresh()
        {
            if (marks.Count == 0 && !syncDue) return;
            float now = Time.time;
            if (!Interval.Due(ref nextRefresh, now, RefreshSeconds)) return;
            GameController gc = Server();
            if (gc == null || !gc.loadComplete) return;
            if (syncDue) Sync(gc);
            if (marks.Count == 0) return;

            List<PlayfieldObject> drop = null;
            foreach (KeyValuePair<PlayfieldObject, MarkState> kv in marks)
            {
                PlayfieldObject po = kv.Key;
                MarkState ms = kv.Value;
                if (po == null || ms.Marker == null || po.nonQuestObjectMarker != ms.Marker)
                {
                    (drop = drop ?? new List<PlayfieldObject>()).Add(po);
                    continue;
                }
                try { Style(ms); }
                catch (Exception e)
                {
                    Log($"Quests: couldn't style a marker on {po.name}: {e.Message}", warn: true);
                    (drop = drop ?? new List<PlayfieldObject>()).Add(po);
                }
            }
            // Gone or taken over: forget it; the next sync marks the object again if it still needs one.
            for (int i = 0; drop != null && i < drop.Count; i++) marks.Remove(drop[i]);
        }

        private static void Style(MarkState ms)
        {
            QuestMarker m = ms.Marker;
            if (!m.reallyStarted) return;
            int sprite;
            Sprite small;
            Color world, map;
            switch (ms.Kind)
            {
                case MarkerKind.Offer:
                    sprite = m.missionMarker;
                    small = m.missionMarkerSmall;
                    world = m.playerSeen ? m.vis : m.invis;
                    map = m.playerSeen ? m.vis : m.transparentMore;
                    break;
                case MarkerKind.Running:
                    sprite = m.missionMarker;
                    small = m.missionMarkerSmall;
                    world = m.transparent;
                    map = m.transparent;
                    break;
                case MarkerKind.Report:
                    sprite = m.missionComplete;
                    small = m.missionCompleteSmall;
                    world = m.vis;
                    map = m.vis;
                    break;
                default:
                    sprite = m.targetSpriteYellow;
                    small = m.targetSmallYellow;
                    world = m.vis;
                    map = m.vis;
                    break;
            }

            if (m.mySprite != null)
            {
                if (m.mySprite.spriteId != sprite) m.mySprite.SetSprite(sprite);
                if (m.mySprite.color != world) m.mySprite.color = world;
            }
            Paint(m.smallImage, small, map);
            Paint(m.smallImage2, small, map);
            m.colorInvis = false;
            m.colorVis = map == m.vis;
            NameMarker(m.questMarkerSmall, ms.Label, null);
            NameMarker(m.questMarkerSmall2, ms.Label, ms.Short);
        }

        private static void Paint(Image image, Sprite sprite, Color color)
        {
            if (image == null) return;
            if (sprite != null && image.sprite != sprite) image.sprite = sprite;
            if (image.color != color) image.color = color;
        }

        private static void NameMarker(QuestMarkerSmall small, string label, string text)
        {
            if (small == null) return;
            if (small.markerName != label) small.markerName = label;
            if (text != null && small.myText != null && small.myText.text != text) small.myText.text = text;
        }
    }
}
