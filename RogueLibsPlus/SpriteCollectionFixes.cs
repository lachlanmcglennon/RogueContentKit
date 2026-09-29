using System;
using System.Collections.Generic;
using HarmonyLib;
using RogueLibsCore;
using UnityEngine;

namespace RogueLibsPlus
{
    // RogueLibs' RogueSprite.AddDefinition/RemoveDefinition treat materials and textures as if they were indexed like
    // spriteDefinitions, and replace materials without resizing materialInsts. tk2d sizes materialInsts once, and
    // tile maps (e.g. the level editor's item layer) index materialInsts[materialId], so custom sprites can show the
    // wrong material or throw. These replacements keep materials, textures and materialInsts consistent.
    internal static class SpriteCollectionFixes
    {
        public static void Resolve() { }

        public static void Patch(Harmony harmony)
        {
            Type t = typeof(SpriteCollectionFixes);
            harmony.Patch(AccessTools.Method(typeof(RogueSprite), nameof(RogueSprite.AddDefinition), new Type[] { typeof(tk2dSpriteCollectionData), typeof(tk2dSpriteDefinition) }),
                          prefix: new HarmonyMethod(t, nameof(AddDefinition)));
            harmony.Patch(AccessTools.Method(typeof(RogueSprite), nameof(RogueSprite.RemoveDefinition), new Type[] { typeof(tk2dSpriteCollectionData), typeof(tk2dSpriteDefinition) }),
                          prefix: new HarmonyMethod(t, nameof(RemoveDefinition)));
        }

        public static bool AddDefinition(tk2dSpriteCollectionData collection, tk2dSpriteDefinition definition)
        {
            if (collection is null || definition is null)
                throw new ArgumentNullException(collection is null ? nameof(collection) : nameof(definition));

            tk2dSpriteDefinition[] newDefinitions = new tk2dSpriteDefinition[collection.spriteDefinitions.Length + 1];
            Array.Copy(collection.spriteDefinitions, 0, newDefinitions, 0, collection.spriteDefinitions.Length);
            newDefinitions[newDefinitions.Length - 1] = definition;
            collection.spriteDefinitions = newDefinitions;

            Material[] oldMats = collection.materials ?? Array.Empty<Material>();
            Material[] newMats;
            if (definition.material is null || Array.IndexOf(oldMats, definition.material) != -1)
                newMats = Array.FindAll(oldMats, static m => m is not null);
            else
            {
                newMats = new Material[oldMats.Length + 1];
                Array.Copy(oldMats, 0, newMats, 0, oldMats.Length);
                newMats[newMats.Length - 1] = definition.material;
                if (Array.IndexOf(newMats, null) != -1)
                    newMats = Array.FindAll(newMats, static m => m is not null);
            }
            SetMaterials(collection, oldMats, newMats, definition);

            Texture? tex = definition.material?.mainTexture;
            Texture[] oldTextures = collection.textures ?? Array.Empty<Texture>();
            if (tex is not null && Array.IndexOf(oldTextures, tex) == -1)
            {
                Texture[] newTextures = new Texture[oldTextures.Length + 1];
                Array.Copy(oldTextures, 0, newTextures, 0, oldTextures.Length);
                newTextures[newTextures.Length - 1] = tex;
                if (Array.IndexOf(newTextures, null) != -1)
                    newTextures = Array.FindAll(newTextures, static m => m is not null);
                collection.textures = newTextures;
            }

            Refresh(collection);
            return false;
        }

        public static bool RemoveDefinition(tk2dSpriteCollectionData collection, tk2dSpriteDefinition definition, ref bool __result)
        {
            if (collection is null || definition is null)
                throw new ArgumentNullException(collection is null ? nameof(collection) : nameof(definition));

            int index = Array.IndexOf(collection.spriteDefinitions, definition);
            if (index == -1)
            {
                __result = false;
                return false;
            }

            tk2dSpriteDefinition[] newDefinitions = new tk2dSpriteDefinition[collection.spriteDefinitions.Length - 1];
            Array.Copy(collection.spriteDefinitions, 0, newDefinitions, 0, index);
            Array.Copy(collection.spriteDefinitions, index + 1, newDefinitions, index, collection.spriteDefinitions.Length - index - 1);
            collection.spriteDefinitions = newDefinitions;

            // materials and textures can be shared, so only drop the definition's own once nothing else uses them
            Material? mat = definition.material;
            if (mat is not null && Array.FindIndex(newDefinitions, d => d is not null && ReferenceEquals(d.material, mat)) == -1)
            {
                Material[] oldMats = collection.materials ?? Array.Empty<Material>();
                Material[] newMats = Array.FindAll(oldMats, m => !ReferenceEquals(m, mat));
                if (newMats.Length > 0 && newMats.Length != oldMats.Length)
                    SetMaterials(collection, oldMats, newMats, null);

                Texture? tex = mat.mainTexture;
                if (tex is not null && Array.FindIndex(newMats, m => m is not null && ReferenceEquals(m.mainTexture, tex)) == -1)
                {
                    Texture[] oldTextures = collection.textures ?? Array.Empty<Texture>();
                    Texture[] newTextures = Array.FindAll(oldTextures, x => !ReferenceEquals(x, tex));
                    if (newTextures.Length > 0) collection.textures = newTextures;
                }
            }

            Refresh(collection);
            HookSystem.RemoveHook(definition);
            __result = true;
            return false;
        }

        private static void Refresh(tk2dSpriteCollectionData collection)
        {
            collection.inst.materialIdsValid = false;
            collection.InitMaterialIds();
            collection.ClearDictionary();
            collection.InitDictionary();
        }

        // materialInsts has to stay the same length and order as materials
        private static void SetMaterials(tk2dSpriteCollectionData collection, Material[] oldMats, Material[] newMats, tk2dSpriteDefinition? added)
        {
            Material[]? oldInsts = collection.materialInsts;
            collection.materials = newMats;
            if (oldInsts is null) return;

            Dictionary<Material, int> oldIndices = new Dictionary<Material, int>(oldMats.Length);
            for (int i = 0; i < oldMats.Length; i++)
                if (oldMats[i] is not null && !oldIndices.ContainsKey(oldMats[i]))
                    oldIndices[oldMats[i]] = i;

            Material[] newInsts = new Material[newMats.Length];
            for (int i = 0; i < newMats.Length; i++)
            {
                Material mat = newMats[i];
                if (oldIndices.TryGetValue(mat, out int oldIndex) && oldIndex < oldInsts.Length && oldInsts[oldIndex] is not null)
                    newInsts[i] = oldInsts[oldIndex];
                else if (added is not null && ReferenceEquals(added.material, mat) && added.materialInst is not null)
                    newInsts[i] = added.materialInst;
                else newInsts[i] = mat;
            }
            collection.materialInsts = newInsts;
        }
    }
}
