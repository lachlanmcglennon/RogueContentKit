using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RCK.Items
{
    /// <summary>
    ///   Makes the Grappling Hook work. The game defines it as a gun with no case in <c>Gun.Shoot</c>. RCK fires a hook
    ///   along the shot's aim: hitting a character pulls them to the shooter, and hitting a wall, door or solid object
    ///   pulls the shooter to it. It never runs out; a short wait between shots is the only limit.
    /// </summary>
    internal static class GrapplingHook
    {
        internal const string ItemName = "GrapplingHook";

        /// <summary>Hook reach in world units (8 tiles).</summary>
        internal const float Range = 5.12f;

        /// <summary>Pull speed in units per second. Speeds of 17 and up would make the game smash walls.</summary>
        internal const float Speed = 9f;

        internal const float Cooldown = 0.8f;

        /// <summary>How far from the anchor point a self-pull stops.</summary>
        internal const float AnchorGap = 0.45f;

        /// <summary>How close to the shooter a pulled character stops.</summary>
        internal const float PullGap = 0.7f;

        private const float KnockStrength = 120f;

        internal static bool Enabled { get; private set; }
        private static bool reportedError;
        private static Material? ropeMaterial;
        private static bool ropeFailed;

        internal static void Initialize()
        {
            Enabled = !Vanilla.Mentions(AccessTools.Method(typeof(Gun), nameof(Gun.Shoot), new[] { typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(string) }), ItemName, "Grappling Hook fix");
            ItemText.Description(ItemName,
                "Fire at a wall or a solid object to pull yourself to it, or at someone to pull them to you. Never runs out.",
                "Выстрелите в стену или твёрдый предмет, чтобы подтянуться к нему, или в кого-нибудь, чтобы притянуть его к себе. Не расходуется.",
                "朝墙壁或坚固物体发射可将自己拉过去，朝别人发射则能把对方拉到身边。永不耗尽。");
        }

        internal static void Fire(Gun gun, InvItem item, int bulletNetID)
        {
            try
            {
                FireUnsafe(gun, item, bulletNetID);
            }
            catch (Exception e)
            {
                if (reportedError) return;
                reportedError = true;
                Rck.Log.LogError($"Grappling Hook shot failed for {gun.agent}: {e}");
            }
        }

        private static void FireUnsafe(Gun gun, InvItem item, int bulletNetID)
        {
            Agent shooter = gun.agent;
            GameController gc = GameController.gameController;

            // The game's own bullet gives the real aim (spread, auto-aim, the remote replay) on every machine; the hook
            // uses its direction and removes it at once.
            Bullet probe = gun.spawnBullet(bulletStatus.Normal, item, bulletNetID, false);
            if (probe == null) return;
            Vector2 dir = Quaternion.AngleAxis(probe.tr.eulerAngles.z, Vector3.forward) * Vector3.up;
            probe.DestroyMe();
            dir.Normalize();

            gun.SetWeaponCooldown(Cooldown, item);
            gc.audioHandler.Play(shooter, "ThrowItem");
            gc.playerControl.Vibrate(shooter.isPlayer, 0.1f, 0.1f);

            Vector2 origin = shooter.tr.position;
            Hook hook = Cast(shooter, origin, dir);
            RckPlugin.Instance.StartCoroutine(Rope(shooter, hook));
            if (hook.Kind == HookKind.Miss) return;

            if (hook.Kind == HookKind.Agent)
            {
                Agent target = hook.Target!;
                gc.audioHandler.Play(target, "Hoist");
                if (gc.serverPlayer && target.isPlayer == 0 && !target.dead && !CutGun.IsFriendly(target, shooter))
                    target.relationships.AddRelHate(shooter, 2);
                if (CutGun.OwnsPhysics(target))
                {
                    float distance = Vector2.Distance(target.tr.position, shooter.tr.position);
                    target.StartCoroutine(Pull(target, () => shooter != null ? (Vector2)shooter.tr.position : (Vector2)target.tr.position,
                        PullGap, shooter, distance / Speed + 0.3f));
                }
                return;
            }

            gc.audioHandler.Play(shooter, "BulletHitWall");
            Vector2 goal = hook.Point - dir * AnchorGap;
            float travel = Vector2.Distance(origin, goal);
            if (travel < 0.2f) return;
            gc.audioHandler.Play(shooter, "Hoist");
            if (CutGun.OwnsPhysics(shooter))
                shooter.StartCoroutine(Pull(shooter, () => goal, 0.05f, null, travel / Speed + 0.3f));
        }

        private enum HookKind { Miss, Anchor, Agent }

        private readonly struct Hook
        {
            public Hook(HookKind kind, Vector2 point, Agent? target) { Kind = kind; Point = point; Target = target; }
            public HookKind Kind { get; }
            public Vector2 Point { get; }
            public Agent? Target { get; }
        }

        private static Hook Cast(Agent shooter, Vector2 origin, Vector2 dir)
        {
            int mask = shooter.movement.myLayerMask | shooter.movement.myLayerMaskAllObjectsAndDoors;
            RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, Range, mask);
            foreach (RaycastHit2D hit in hits.OrderBy(h => h.distance))
            {
                Collider2D c = hit.collider;
                if (c == null) continue;

                Agent? agent = null;
                if (c.CompareTag("AgentSprite")) agent = c.GetComponent<AgentColliderBox>()?.objectSprite?.agent;
                else if (c.CompareTag("Agent")) agent = c.GetComponent<Agent>();
                if (agent != null)
                {
                    if (agent == shooter || agent.ghost || agent.disappeared) continue;
                    // Things too heavy to pull act as anchors instead.
                    if (agent.mechEmpty || agent.mechFilled || agent.oma.bodyGuarded) return new Hook(HookKind.Anchor, hit.point, null);
                    return new Hook(HookKind.Agent, hit.point, agent);
                }

                if (c.CompareTag("Wall")) return new Hook(HookKind.Anchor, hit.point, null);
                if (c.CompareTag("ObjectReal") || c.CompareTag("ObjectRealSprite"))
                {
                    ObjectReal? obj = c.GetComponent<ObjectReal>() ?? c.GetComponentInParent<ObjectReal>() ?? c.GetComponent<ObjectSprite>()?.objectReal;
                    if (obj == null || obj.bulletsCanPass) continue;
                    return new Hook(HookKind.Anchor, hit.point, null);
                }
            }
            return new Hook(HookKind.Miss, origin + dir * Range, null);
        }

        // Runs on the machine that owns the mover's physics. The game's own knockback call keeps the mover's physics,
        // stun lock and network state right; the velocity is refreshed every physics step toward the goal.
        private static IEnumerator Pull(Agent mover, Func<Vector2> goal, float stopDistance, PlayfieldObject? knocker, float timeout)
        {
            float end = Time.time + timeout;
            float nextKnock = 0f;
            float nextStallCheck = Time.time + 0.15f;
            Vector2 lastPosition = mover.tr.position;
            while (mover != null && !mover.disappeared && Time.time < end)
            {
                Vector2 position = mover.tr.position;
                Vector2 delta = goal() - position;
                float distance = delta.magnitude;
                if (distance <= stopDistance) break;
                Vector2 velocity = delta / distance * Mathf.Min(Speed, distance / Time.fixedDeltaTime);
                if (Time.time >= nextKnock)
                {
                    mover.movement.KnockbackSpecificVelocity(velocity, KnockStrength, knocker);
                    nextKnock = Time.time + 0.3f;
                }
                else
                {
                    mover.rb.velocity = velocity;
                }
                if (Time.time >= nextStallCheck)
                {
                    if ((position - lastPosition).sqrMagnitude < 0.03f * 0.03f) break;
                    lastPosition = position;
                    nextStallCheck = Time.time + 0.15f;
                }
                yield return new WaitForFixedUpdate();
            }
            if (mover != null && mover.rb != null) mover.rb.velocity = Vector2.zero;
        }

        // A thin line from the shooter to the hook, on every machine. Best effort: no line if the shader is missing.
        private static IEnumerator Rope(Agent shooter, Hook hook)
        {
            LineRenderer? line = MakeRope();
            if (line == null) yield break;
            float end = Time.time + (hook.Kind == HookKind.Miss ? 0.15f : 0.2f + Range / Speed);
            try
            {
                while (Time.time < end && shooter != null)
                {
                    Vector3 from = shooter.tr.position;
                    Vector3 to = hook.Kind == HookKind.Agent && hook.Target != null ? hook.Target.tr.position : (Vector3)hook.Point;
                    to.z = from.z;
                    line.SetPosition(0, from);
                    line.SetPosition(1, to);
                    if (hook.Kind != HookKind.Miss && Vector2.Distance(from, to) <= PullGap + 0.05f) break;
                    yield return null;
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(line.gameObject);
            }
        }

        private static LineRenderer? MakeRope()
        {
            if (ropeFailed) return null;
            try
            {
                if (ropeMaterial == null)
                {
                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader == null) { ropeFailed = true; return null; }
                    ropeMaterial = new Material(shader);
                }
                GameObject go = new GameObject("RCK_GrapplingRope");
                LineRenderer line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = ropeMaterial;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.startWidth = line.endWidth = 0.035f;
                line.startColor = line.endColor = new Color(0.55f, 0.42f, 0.25f, 1f);
                SortingLayer[] layers = SortingLayer.layers;
                if (layers.Length > 0) line.sortingLayerID = layers[layers.Length - 1].id;
                line.sortingOrder = 100;
                return line;
            }
            catch (Exception e)
            {
                ropeFailed = true;
                Rck.Log.LogWarning($"Grappling Hook rope can't be drawn: {e.Message}");
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(Gun), nameof(Gun.Shoot), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(string))]
    internal static class Gun_Shoot_GrapplingHookPatch
    {
        private static void Prefix(Gun __instance, bool specialAbility, out InvItem? __state)
            => __state = GrapplingHook.Enabled ? CutGun.Pending(__instance, specialAbility, GrapplingHook.ItemName) : null;

        private static void Postfix(Gun __instance, int bulletNetID, InvItem? __state)
        {
            if (__state != null) GrapplingHook.Fire(__instance, __state, bulletNetID);
        }
    }
}
