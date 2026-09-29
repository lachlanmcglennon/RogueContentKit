using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RCK.Appearance
{
    public sealed class AppearanceModule : IRckModule
    {
        public string Name => "Appearance";

        public void Initialize()
        {
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.SetupAgentStats), typeof(string))]
    internal static class Agent_SetupAgentStats_AppearancePatch
    {
        private static readonly HashSet<int> loggedFailures = new HashSet<int>();

        private static void Postfix(Agent __instance)
        {
            try
            {
                AppearanceRuntime.Apply(__instance);
            }
            catch (Exception ex)
            {
                int key = __instance != null ? __instance.agentID : 0;
                if (loggedFailures.Add(key))
                {
                    Rck.Log.LogError($"Appearance roll failed for {__instance}: {ex}");
                }
            }
        }
    }

    internal static class AppearanceRuntime
    {
        private static readonly HashSet<string> MaskHairTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "AlienHead", "AssassinMask", "ButlerBotHead", "CopBotHead", "GorillaHead", "HologramHead",
            "Hoodie", "RobotHead", "RobotPlayerHead", "SlavemasterMask", "WerewolfHead"
        };

        private static readonly Dictionary<string, string[]> ManualRolls = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "Normal_Colors", new[] { "Black", "Blonde", "Brown", "Grey", "Orange" } },
            { "Normal_No_Grey", new[] { "Black", "Blonde", "Brown", "Orange" } },
        };

        private static readonly Dictionary<string, string> BodyTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Alien", "Alien" },
            { "Assassin", "Assassin" },
            { "Athlete", "Athlete" },
            { "Bartender", "Bartender" },
            { "Bouncer", "Bouncer" },
            { "Butler_Bot", "ButlerBot" },
            { "Cannibal", "Cannibal" },
            { "Clerk", "Clerk" },
            { "Comedian", "Comedian" },
            { "Cop", "Cop" },
            { "Cop_Bot", "CopBot" },
            { "Courier", "Courier" },
            { "Demolitionist", "Demolitionist" },
            { "Doctor", "Doctor" },
            { "Drug_Dealer", "DrugDealer" },
            { "Firefighter", "Firefighter" },
            { "Gangbanger", "Gangbanger" },
            { "Generic", "Generic" },
            { "Goon", "Guard" },
            { "Gorilla", "Gorilla" },
            { "Hacker", "Hacker" },
            { "Investment_Banker", "Businessman" },
            { "Killer_Robot", "Robot" },
            { "Mayor", "Mayor" },
            { "Mech", "Mech" },
            { "Mech_Pilot", "MechPilot" },
            { "Mobster", "Mafia" },
            { "Musician", "Musician" },
            { "Office_Drone", "OfficeDrone" },
            { "Resistance_Leader", "ResistanceLeader" },
            { "Robot", "Robot" },
            { "Scientist", "Scientist" },
            { "Shapeshifter", "ShapeShifter" },
            { "Shopkeeper", "Shopkeeper" },
            { "Slave", "Slave" },
            { "Slavemaster", "Slavemaster" },
            { "Slum_Dweller", "Hobo" },
            { "Soldier", "Soldier" },
            { "Supercop", "Cop2" },
            { "Supergoon", "Guard2" },
            { "Thief", "Thief" },
            { "Upper_Cruster", "UpperCruster" },
            { "Vampire", "Vampire" },
            { "Werewolf", "WerewolfB" },
            { "Worker", "Worker" },
            { "Wrestler", "Wrestler" },
        };

        private static readonly HashSet<string> GreyscaleBodies = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assassin", "Athlete", "Bartender", "Bouncer", "Businessman", "Cannibal", "Clerk", "Comedian",
            "Cop", "Cop2", "CopBot", "Courier", "Demolitionist", "Doctor", "DrugDealer", "Firefighter",
            "Gangbanger", "Generic", "Gorilla", "Guard", "Hacker", "Hobo", "Mafia", "Mayor", "Mech",
            "MechPilot", "Musician", "OfficeDrone", "ResistanceLeader", "Robot", "Scientist", "Shopkeeper",
            "Slave", "Slavemaster", "Soldier", "Thief", "UpperCruster", "Vampire", "WerewolfB", "Worker", "Wrestler"
        };

        public static void Apply(Agent agent)
        {
            if (agent == null || agent.statusEffects == null)
            {
                return;
            }
            bool dynamicPlayerAppearance = AgentTraits.Has(agent, "Dynamic_Player_Appearance");
            if (agent.isPlayer != 0 && !dynamicPlayerAppearance)
            {
                return;
            }

            List<RckTraitInfo> traits = AgentTraits.InFolder(agent, "Appearance")
                .Where(static t => t.InHead)
                .OrderBy(static t => t.Id, StringComparer.Ordinal)
                .ToList();
            if (traits.Count == 0)
            {
                return;
            }

            var pools = new AppearancePools();
            var rules = new AppearanceRules();
            foreach (RckTraitInfo trait in traits)
            {
                AddTrait(trait, pools, rules);
            }

            var rng = new StableRng(agent, "Appearance");
            // Pooled agents can keep a stale customCharacterData; the game only reads it for "Custom" agents.
            SaveCharacterData? data = agent.agentName == "Custom" ? agent.customCharacterData : null;
            AgentHitbox hitbox = agent.agentHitboxScript;

            string skin = ChooseOrCurrent(pools.SkinColor, data?.skinColorName, hitbox.skinColorName, rng);
            string bodyColor = Current(data?.bodyColorName, "White");
            string legsColor = Current(data?.legsColorName, "White");
            string hairColor = Current(hitbox.hairColorName ?? data?.hairColorName, data?.hairColorName ?? "Brown");
            string eyesColor = Current(data?.eyesColorName, "White");
            string bodyType = Current(data?.bodyType, agent.agentName == "Custom" ? "Generic" : agent.agentName);
            string hairType = Current(hitbox.hairType ?? data?.hairType, data?.hairType ?? "Normal");
            string facialHair = Current(hitbox.facialHairType ?? data?.facialHair, data?.facialHair ?? "None");
            string eyesType = Current(data?.eyesType, "Eyes");
            string accessory = Current(agent.inventory?.startingHeadPiece ?? data?.startingHeadPiece, data?.startingHeadPiece ?? "");

            if (pools.BodyType.Count > 0)
            {
                bodyType = rng.Pick(pools.BodyType);
            }
            if (pools.Hairstyle.Count > 0)
            {
                hairType = PickHairType(pools.Hairstyle, rules, rng);
            }
            bool mask = IsMask(hairType);

            bool neutralBody = (rules.NeutralBody50 && rng.Chance(50)) || (rules.NeutralBody75 && rng.Chance(75));
            if (!neutralBody && pools.BodyColor.Count > 0)
            {
                bodyColor = rng.Pick(pools.BodyColor);
            }
            if (rules.Shirtless)
            {
                bodyColor = skin;
            }
            else if (rules.Shirtsome && bodyColor == skin && pools.BodyColor.Count > 1)
            {
                bodyColor = PickNotEqual(pools.BodyColor, skin, rng, bodyColor);
            }

            if (pools.LegsColor.Count > 0)
            {
                legsColor = rng.Pick(pools.LegsColor);
            }
            if (rules.Pantsless)
            {
                legsColor = skin;
            }
            else if (rules.Pantsuit)
            {
                legsColor = bodyColor;
            }
            else if (rules.Pantiful && legsColor == skin && pools.LegsColor.Count > 1)
            {
                legsColor = PickNotEqual(pools.LegsColor, skin, rng, legsColor);
            }

            if (pools.HairColor.Count > 0)
            {
                hairColor = rng.Pick(pools.HairColor);
            }
            if (rules.MelaninMashup)
            {
                bodyColor = skin;
                legsColor = skin;
                if (!mask)
                {
                    hairColor = skin;
                }
            }
            if (rules.FleshyFollicles && !mask)
            {
                hairColor = skin;
            }
            if (mask)
            {
                if (rules.MatchedMasks)
                {
                    hairColor = bodyColor;
                }
                else if (rules.UncoloredMasks)
                {
                    hairColor = "White";
                }
            }

            if (pools.FacialHair.Count > 0)
            {
                facialHair = rng.Pick(pools.FacialHair);
            }
            if ((rules.NoFacialHair50 && rng.Chance(50)) || (rules.NoFacialHair75 && rng.Chance(75)))
            {
                facialHair = "None";
            }

            if (pools.EyeType.Count > 0)
            {
                eyesType = rng.Pick(pools.EyeType);
            }
            if ((rules.NormalEyes50 && rng.Chance(50)) || (rules.NormalEyes75 && rng.Chance(75)))
            {
                eyesType = "Eyes";
            }
            if (pools.EyeColor.Count > 0)
            {
                eyesColor = rng.Pick(pools.EyeColor);
            }
            if (rules.BeadyEyed)
            {
                eyesColor = skin;
            }

            if (pools.Accessory.Count > 0)
            {
                accessory = rng.Pick(pools.Accessory);
            }
            if ((rules.NoAccessory50 && rng.Chance(50)) || (rules.NoAccessory75 && rng.Chance(75)))
            {
                accessory = "";
            }

            ApplyValues(agent, data, hitbox, bodyType, bodyColor, hairType, hairColor, facialHair, skin, legsColor, eyesType, eyesColor, accessory);
            ApplyVisibility(hitbox, rules);
        }

        private static void AddTrait(RckTraitInfo trait, AppearancePools pools, AppearanceRules rules)
        {
            switch (trait.Id)
            {
                case "No_Accessory_50": rules.NoAccessory50 = true; return;
                case "No_Accessory_75": rules.NoAccessory75 = true; return;
                case "Neutral_Body_50": rules.NeutralBody50 = true; return;
                case "Neutral_Body_75": rules.NeutralBody75 = true; return;
                case "Shirtless": rules.Shirtless = true; return;
                case "Shirtsome": rules.Shirtsome = true; return;
                case "Beady_Eyed": rules.BeadyEyed = true; return;
                case "Normal_Eyes_50": rules.NormalEyes50 = true; return;
                case "Normal_Eyes_75": rules.NormalEyes75 = true; return;
                case "No_Facial_Hair_50": rules.NoFacialHair50 = true; return;
                case "No_Facial_Hair_75": rules.NoFacialHair75 = true; return;
                case "Fleshy_Follicles": rules.FleshyFollicles = true; return;
                case "Matched_Masks": rules.MatchedMasks = true; return;
                case "Melanin_Mashup": rules.MelaninMashup = true; return;
                case "Uncolored_Masks": rules.UncoloredMasks = true; return;
                case "Mask_Override": rules.MaskOverride = true; return;
                case "Masks_50": rules.Masks50 = true; return;
                case "Pantiful": rules.Pantiful = true; return;
                case "Pantsless": rules.Pantsless = true; return;
                case "Pantsuit": rules.Pantsuit = true; return;
                case "Bodyless": rules.Bodyless = true; return;
                case "Eyeless": rules.Eyeless = true; return;
                case "Headless": rules.Headless = true; return;
                case "Left_Armless": rules.LeftArmless = true; return;
                case "Legless": rules.Legless = true; return;
                case "Shadowless": rules.Shadowless = true; return;
            }

            string folder = trait.Folder;
            if (folder == "Appearance/Accessory") AddRolls(pools.Accessory, Rolls(trait));
            else if (folder == "Appearance/Body Color") AddRolls(pools.BodyColor, Rolls(trait));
            else if (folder == "Appearance/Body Type") AddRolls(pools.BodyType, BodyRolls(trait, greyscale: false));
            else if (folder == "Appearance/Body Type Greyscale") AddRolls(pools.BodyType, BodyRolls(trait, greyscale: true));
            else if (folder == "Appearance/Eye Color") AddRolls(pools.EyeColor, Rolls(trait));
            else if (folder == "Appearance/Eye Type") AddRolls(pools.EyeType, Rolls(trait));
            else if (folder == "Appearance/Facial Hair") AddRolls(pools.FacialHair, Rolls(trait));
            else if (folder == "Appearance/Hair Color" || folder == "Appearance/Hair Color Grouped") AddRolls(pools.HairColor, Rolls(trait));
            else if (folder == "Appearance/Hairstyle" || folder == "Appearance/Hairstyle Grouped") AddRolls(pools.Hairstyle, Rolls(trait));
            else if (folder == "Appearance/Legs Color") AddRolls(pools.LegsColor, Rolls(trait));
            else if (folder == "Appearance/Skin Color" || folder == "Appearance/Skin Color Grouped") AddRolls(pools.SkinColor, Rolls(trait));
        }

        private static string[] Rolls(RckTraitInfo trait)
        {
            if (trait.Rolls.Length > 0)
            {
                return trait.Rolls;
            }
            return ManualRolls.TryGetValue(trait.Id, out string[] rolls) ? rolls : Array.Empty<string>();
        }

        private static string[] BodyRolls(RckTraitInfo trait, bool greyscale)
        {
            string[] rolls = Rolls(trait);
            if (rolls.Length == 0)
            {
                string id = trait.Id;
                id = id.Replace("_Body_Greyscale", "").Replace("_Body", "");
                if (!BodyTypes.TryGetValue(id, out string body))
                {
                    body = id.Replace("_", "");
                }
                rolls = new[] { body };
            }

            if (!greyscale)
            {
                return rolls;
            }

            string[] grey = new string[rolls.Length];
            for (int i = 0; i < rolls.Length; i++)
            {
                string body = rolls[i];
                grey[i] = body.StartsWith("G_", StringComparison.Ordinal) || !GreyscaleBodies.Contains(body) ? body : "G_" + body;
            }
            return grey;
        }

        private static string PickHairType(List<string> pool, AppearanceRules rules, StableRng rng)
        {
            if (rules.MaskOverride || rules.Masks50)
            {
                var masks = new List<string>();
                var nonMasks = new List<string>();
                foreach (string value in pool)
                {
                    (IsMask(value) ? masks : nonMasks).Add(value);
                }
                if (masks.Count > 0)
                {
                    if (rules.MaskOverride || nonMasks.Count == 0 || rng.Chance(50))
                    {
                        return rng.Pick(masks);
                    }
                    return rng.Pick(nonMasks);
                }
            }
            return rng.Pick(pool);
        }

        private static string PickNotEqual(List<string> pool, string avoid, StableRng rng, string fallback)
        {
            var filtered = new List<string>();
            foreach (string value in pool)
            {
                if (value != avoid)
                {
                    filtered.Add(value);
                }
            }
            return filtered.Count == 0 ? fallback : rng.Pick(filtered);
        }

        private static void AddRolls(List<string> pool, string[] rolls)
        {
            foreach (string roll in rolls)
            {
                if (roll != null)
                {
                    pool.Add(roll);
                }
            }
        }

        private static bool IsMask(string hairType) => MaskHairTypes.Contains(hairType);

        private static string ChooseOrCurrent(List<string> pool, string? primary, string? secondary, StableRng rng)
            => pool.Count > 0 ? rng.Pick(pool) : Current(primary, Current(secondary, "White"));

        private static string Current(string? value, string fallback)
            => string.IsNullOrEmpty(value) ? fallback : value!;

        private static void ApplyValues(Agent agent, SaveCharacterData? data, AgentHitbox hitbox, string bodyType,
            string bodyColor, string hairType, string hairColor, string facialHair, string skinColor, string legsColor,
            string eyesType, string eyesColor, string accessory)
        {
            if (data != null)
            {
                data.bodyType = bodyType;
                data.bodyColorName = bodyColor;
                data.hairType = hairType;
                data.hairColorName = hairColor;
                data.facialHair = facialHair;
                data.skinColorName = skinColor;
                data.legsColorName = legsColor;
                data.eyesType = eyesType;
                data.eyesColorName = eyesColor;
                data.startingHeadPiece = accessory;
            }

            hitbox.hairType = hairType;
            hitbox.hairColorName = hairColor;
            hitbox.GetColorFromString(hairColor, "Hair");
            hitbox.facialHairType = facialHair;
            hitbox.facialHairColorName = hairColor;
            hitbox.facialHairColor = hitbox.hairColor;
            hitbox.skinColorName = skinColor;
            hitbox.GetColorFromString(skinColor, "Skin");
            hitbox.GetColorFromString(bodyColor, "Body");
            hitbox.GetColorFromString(legsColor, "Legs");
            hitbox.GetColorFromString(eyesColor, "Eyes");

            agent.oma.skinColor = agent.oma.convertColorToInt(skinColor);
            agent.oma.hairColor = agent.oma.convertColorToInt(hairColor);
            agent.oma.hairType = agent.oma.convertHairTypeToInt(hairType);
            agent.oma.facialHairType = agent.oma.convertFacialHairTypeToInt(facialHair);
            agent.oma.bodyColor = agent.oma.convertColorToInt(bodyColor);
            agent.oma.legsColor = agent.oma.convertColorToInt(legsColor);
            agent.oma.eyesType = agent.oma.convertEyesTypeToInt(eyesType);
            agent.oma.eyesColor = agent.oma.convertColorToInt(eyesColor);
            agent.oma.startingHeadPiece = agent.oma.convertArmorHeadToInt(accessory);
            if (agent.objectMult != null)
            {
                agent.objectMult.NetworkbodyType = bodyType;
            }

            if (agent.inventory != null)
            {
                agent.inventory.startingHeadPiece = accessory;
                if (string.IsNullOrEmpty(accessory))
                {
                    if (agent.inventory.equippedArmorHead != null)
                    {
                        agent.inventory.UnequipArmorHead(sfx: false, dontUnequipMulti: true);
                    }
                    agent.inventory.defaultArmorHead = null;
                }
                else if (agent.inventory.equippedArmorHead == null || agent.inventory.equippedArmorHead.invItemName != accessory)
                {
                    agent.inventory.AddStartingHeadPiece(accessory, mustAddHeadPiece: true);
                }
            }

            if (facialHair == "None" || facialHair == "")
            {
                hitbox.facialHair.gameObject.SetActive(false);
                hitbox.facialHairWB.gameObject.SetActive(false);
            }
            else
            {
                hitbox.facialHair.gameObject.SetActive(true);
                hitbox.facialHairWB.gameObject.SetActive(true);
            }

            hitbox.SetUsesNewHead();
            hitbox.SetCantShowHairUnderHeadPiece();
            hitbox.SetupBodyStrings();
            hitbox.SetWBSprites();
            hitbox.MustRefresh();
            hitbox.UpdateAnim();
            if (agent.objectSprite != null)
            {
                agent.objectSprite.agentColorDirty = true;
                agent.objectSprite.spriteRendererDirty = true;
                agent.objectSprite.SetRenderer("Off");
                agent.objectSprite.RefreshRenderer();
            }
        }

        private static void ApplyVisibility(AgentHitbox hitbox, AppearanceRules rules)
        {
            if (rules.Bodyless)
            {
                SetActive(hitbox.body, false);
                SetActive(hitbox.bodyH, false);
                SetActive(hitbox.bodyWB, false);
                SetActive(hitbox.bodyWBH, false);
            }
            if (rules.Headless)
            {
                SetActive(hitbox.head, false);
                SetActive(hitbox.headH, false);
                SetActive(hitbox.headWB, false);
                SetActive(hitbox.headWBH, false);
                SetActive(hitbox.hair, false);
                SetActive(hitbox.hairH, false);
                SetActive(hitbox.hairWB, false);
                SetActive(hitbox.hairWBH, false);
                SetActive(hitbox.facialHair, false);
                SetActive(hitbox.facialHairH, false);
                SetActive(hitbox.facialHairWB, false);
                SetActive(hitbox.facialHairWBH, false);
                SetActive(hitbox.eyes, false);
                SetActive(hitbox.eyesH, false);
                SetActive(hitbox.eyesWB, false);
                SetActive(hitbox.eyesWBH, false);
                SetActive(hitbox.headPiece, false);
                SetActive(hitbox.headPieceH, false);
                SetActive(hitbox.headPieceWB, false);
            }
            else if (rules.Eyeless)
            {
                SetActive(hitbox.eyes, false);
                SetActive(hitbox.eyesH, false);
                SetActive(hitbox.eyesWB, false);
                SetActive(hitbox.eyesWBH, false);
            }
            if (rules.LeftArmless)
            {
                SetActive(hitbox.arm1, false);
                SetActive(hitbox.arm1H, false);
                SetActive(hitbox.arm1WB, false);
                SetActive(hitbox.arm1WBH, false);
                SetActive(hitbox.meleeArm1, false);
                SetActive(hitbox.meleeArm1H, false);
                SetActive(hitbox.gunArm1, false);
                SetActive(hitbox.gunArm1H, false);
            }
            if (rules.Legless)
            {
                SetActive(hitbox.leg1, false);
                SetActive(hitbox.leg2, false);
                SetActive(hitbox.leg1H, false);
                SetActive(hitbox.leg2H, false);
                SetActive(hitbox.leg1WB, false);
                SetActive(hitbox.leg2WB, false);
                SetActive(hitbox.leg1WBH, false);
                SetActive(hitbox.leg2WBH, false);
                SetActive(hitbox.footwear1, false);
                SetActive(hitbox.footwear2, false);
                SetActive(hitbox.footwear1H, false);
                SetActive(hitbox.footwear2H, false);
                SetActive(hitbox.footwear1WB, false);
                SetActive(hitbox.footwear2WB, false);
                SetActive(hitbox.footwear1WBH, false);
                SetActive(hitbox.footwear2WBH, false);
            }
            if (rules.Shadowless && hitbox.shadowGO != null)
            {
                hitbox.shadowGO.SetActive(false);
            }
        }

        private static void SetActive(tk2dSprite sprite, bool active)
        {
            if (sprite != null && sprite.gameObject != null)
            {
                sprite.gameObject.SetActive(active);
            }
        }

        private sealed class AppearancePools
        {
            public readonly List<string> Accessory = new List<string>();
            public readonly List<string> BodyColor = new List<string>();
            public readonly List<string> BodyType = new List<string>();
            public readonly List<string> EyeColor = new List<string>();
            public readonly List<string> EyeType = new List<string>();
            public readonly List<string> FacialHair = new List<string>();
            public readonly List<string> HairColor = new List<string>();
            public readonly List<string> Hairstyle = new List<string>();
            public readonly List<string> LegsColor = new List<string>();
            public readonly List<string> SkinColor = new List<string>();
        }

        private sealed class AppearanceRules
        {
            public bool NoAccessory50;
            public bool NoAccessory75;
            public bool NeutralBody50;
            public bool NeutralBody75;
            public bool Shirtless;
            public bool Shirtsome;
            public bool BeadyEyed;
            public bool NormalEyes50;
            public bool NormalEyes75;
            public bool NoFacialHair50;
            public bool NoFacialHair75;
            public bool FleshyFollicles;
            public bool MatchedMasks;
            public bool MelaninMashup;
            public bool UncoloredMasks;
            public bool MaskOverride;
            public bool Masks50;
            public bool Pantiful;
            public bool Pantsless;
            public bool Pantsuit;
            public bool Bodyless;
            public bool Eyeless;
            public bool Headless;
            public bool LeftArmless;
            public bool Legless;
            public bool Shadowless;
        }
    }

    internal struct StableRng
    {
        private uint state;

        public StableRng(Agent agent, string salt)
        {
            state = 2166136261u;
            Add(salt);
            Add(agent.gc?.loadLevel?.randomSeedNum ?? 0);
            if (agent.isPlayer == 0)
            {
                Add(agent.gc?.sessionDataBig?.curLevelEndless ?? 0);
                Add(agent.agentID);
                Add(agent.streamingChunkObjectID);
                Add(agent.startingChunk);
                Add(agent.startingSector);
                Add((int)Math.Round(agent.originalPosReal.x * 100f));
                Add((int)Math.Round(agent.originalPosReal.y * 100f));
            }
            else
            {
                Add(agent.isPlayer);
            }
            Add(agent.agentName);
            Add(agent.agentRealName);
            IReadOnlyCollection<string> traitNames = AgentTraits.Get(agent);
            var sorted = new List<string>(traitNames);
            sorted.Sort(StringComparer.Ordinal);
            foreach (string trait in sorted)
            {
                Add(trait);
            }
        }

        public string Pick(List<string> values) => values[Next(values.Count)];

        public bool Chance(int percent) => percent >= 100 || (percent > 0 && Next(100) < percent);

        private int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 1)
            {
                return 0;
            }
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state % (uint)exclusiveMax);
        }

        private void Add(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                Add(0);
                return;
            }
            for (int i = 0; i < text!.Length; i++)
            {
                state ^= text[i];
                state *= 16777619u;
            }
        }

        private void Add(int value)
        {
            unchecked
            {
                state ^= (uint)value;
                state *= 16777619u;
            }
        }
    }
}
