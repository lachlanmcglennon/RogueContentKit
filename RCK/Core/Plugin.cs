using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using RogueLibsCore;
using UnityEngine;

namespace RCK
{
    /// <summary>
    ///   RCK (Rogue Content Kit): designer traits, goals, mutators and object data for Streets of Rogue campaigns. It
    ///   reads campaigns and characters made for Custom Content Utilities (CCU) unchanged; the stored names it keeps for
    ///   that are listed in <see cref="LegacyCcu"/>.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, Version)]
    [BepInDependency(RogueLibs.GUID, RogueLibs.CompiledVersion)]
    [BepInDependency(RogueLibsPlus.RogueLibsPlusPlugin.GUID, BepInDependency.DependencyFlags.HardDependency)]
    // CCU, or an old build of this plugin that used its GUID. Both patch the same game code. RckLoadCheck explains it.
    [BepInIncompatibility("Freiling87.streetsofrogue.CCU")]
    public sealed class RckPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "streetsofrogue.roguecontentkit";
        public const string PluginName = "RCK";
        // Keep in step with <Version> in RCK\Directory.Build.props (tools\verify-ccu.ps1 checks).
        public const string Version = "1.0.0";
        public const string MenuLineId = "RCKVersion";

        public static RckPlugin Instance { get; private set; } = null!;
        /// <summary>True once <see cref="Awake"/> has finished. BepInEx lists the plugin even when Awake throws.</summary>
        public static bool Loaded { get; private set; }

        public void Awake()
        {
            Instance = this;
            Rck.SetLog(Logger);
            Rck.Config = new RckConfig(Config);
            var sw = Stopwatch.StartNew();

            var harmony = new Harmony(PluginGuid);
            int patches = 0;
            foreach (Type t in typeof(RckPlugin).Assembly.GetTypes())
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0 && Rck.TryPatch(harmony, t))
                    patches++;

            TraitRegistry.RegisterAll();
            DesignerMode.Initialize();
            RckDialogue.RegisterAll();
            ModuleLoader.LoadAll(Path.GetDirectoryName(Info.Location)!);

            if (Rck.Config.LogTraitCount.Value)
            {
                int head = RckData.Traits.Count(static t => t.InHead);
                Logger.LogInfo($"{PluginName} {Version}: {TraitRegistry.Registered} traits registered ({head} current, "
                    + $"{RckData.Traits.Length - head} legacy, {RckData.Extensions.Count} extensions, {TraitRegistry.Failed} failed), "
                    + $"{patches} core patches, {sw.ElapsedMilliseconds} ms.");
                foreach (ModuleLoader.Loaded r in ModuleLoader.Results)
                    Logger.LogInfo(r.Error != null
                        ? $"  {r.File}: FAILED ({r.Error})"
                        : $"  {r.File}: {r.Patches} patches{(r.FailedPatches > 0 ? $" ({r.FailedPatches} FAILED)" : "")}, "
                          + $"modules [{string.Join(", ", r.Modules)}]");
            }
            UpdateMenuLine();
            Loaded = true;
        }

        internal static void UpdateMenuLine()
            => RogueLibsPlus.MenuLines.Set(MenuLineId, $"{PluginName} v{Version}" + (DesignerMode.On ? " (designer mode)" : ""));
    }

    /// <summary>
    ///   BepInEx skips <see cref="RckPlugin"/> when an incompatible plugin is installed or a dependency is missing, and its
    ///   own error only names the GUID, in the log. This separate plugin in the same DLL still loads: it says what to do,
    ///   in the log and on the main menu. It also flags leftovers of an old install that break this one, even when RCK
    ///   loaded. It uses no RogueLibs or RogueLibsPlus code unless they are loaded.
    /// </summary>
    [BepInPlugin(RckPlugin.PluginGuid + ".loadcheck", RckPlugin.PluginName + " load check", RckPlugin.Version)]
    public sealed class RckLoadCheck : BaseUnityPlugin
    {
        private const string RogueLibsGuid = "abbysssal.streetsofrogue.roguelibscore";
        private const string RogueLibsPlusGuid = "streetsofrogue.roguelibsplus";
        private static readonly Version MinRogueLibs = new Version(4, 0, 0);

        private readonly List<string> problems = new List<string>();
        private bool onGui;
        private GUIStyle? style;

        // Plugins an old CCU install leaves behind that were built for an older game version and break on this one.
        private static readonly string[][] BrokenLeftovers = { new[] { "Freiling87.streetsofrogue.BunnyLibs", "BunnyLibs" } };

        // Start runs after every plugin's Awake, so the chainloader has finished by now.
        public void Start()
        {
            CheckLeftovers();
            if (!RckPlugin.Loaded) CheckLoad();
            if (problems.Count == 0) return;

            if (RogueLibsPlusLoaded())
            {
                try { ShowWithMenuLines(problems); return; }
                catch (Exception e) { Logger.LogWarning($"Menu lines unavailable: {e.Message}"); }
            }
            onGui = true;
        }

        private void CheckLeftovers()
        {
            foreach (string[] old in BrokenLeftovers)
            {
                if (!Chainloader.PluginInfos.TryGetValue(old[0], out PluginInfo info)) continue;
                Logger.LogWarning($"{old[1]} {info.Metadata.Version} is installed ({info.Location}). It is left over from Custom "
                    + "Content Utilities, was built for an older game version and fails on this one. Delete it; RCK does not need it.");
                problems.Add($"Old {old[1]} found: delete {GameRelative(info.Location)} and restart");
            }
            // Every RogueLibs 4.0 build says it is version 4.0.0, so with two copies BepInEx may load an old one.
            try
            {
                string pack = Path.GetFullPath(Path.Combine(Paths.PluginPath, "RogueLibsCore.dll"));
                foreach (string file in Directory.GetFiles(Paths.PluginPath, "RogueLibsCore.dll", SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetFullPath(file), pack, StringComparison.OrdinalIgnoreCase)) continue;
                    Logger.LogWarning($"A second RogueLibs copy is installed: {file}. BepInEx loads only one of them, maybe the old "
                        + $"one. Delete it and keep {pack}.");
                    problems.Add($"Extra RogueLibs copy found: delete {GameRelative(file)} and restart");
                }
            }
            catch (Exception e) { Logger.LogWarning($"Could not look for extra RogueLibs copies: {e.Message}"); }
        }

        private void CheckLoad()
        {
            int before = problems.Count;
            string reinstall = "extract the RCK Pack into the game folder again and restart";
            foreach (BepInIncompatibility bad in typeof(RckPlugin).GetCustomAttributes(typeof(BepInIncompatibility), false))
            {
                if (!Chainloader.PluginInfos.TryGetValue(bad.IncompatibilityGUID, out PluginInfo other)) continue;
                Logger.LogError($"{RckPlugin.PluginName} {RckPlugin.Version} did not load because \"{other.Metadata.Name}\" "
                    + $"{other.Metadata.Version} ({bad.IncompatibilityGUID}) is installed: {other.Location}. That is Custom Content "
                    + "Utilities, or an older build of this mod that used its ID, and it patches the same game code. Remove that "
                    + $"plugin's folder and restart the game. {RckPlugin.PluginName} reads the same campaigns and characters.");
                problems.Add($"RCK did not load: delete {PluginPath(other.Location)} and restart");
            }
            if (!Chainloader.PluginInfos.TryGetValue(RogueLibsGuid, out PluginInfo rl) || rl.Instance == null)
                Report("RogueLibs is missing", $"RCK did not load: RogueLibs is missing, {reinstall}");
            else if (rl.Metadata.Version < MinRogueLibs)
                Report($"RogueLibs {rl.Metadata.Version} is too old (it needs {MinRogueLibs} or later)",
                    $"RCK did not load: RogueLibs {rl.Metadata.Version} is too old, {reinstall}");
            else if (!RogueLibsPlusLoaded())
                Report("RogueLibsPlus is missing", $"RCK did not load: RogueLibsPlus is missing, {reinstall}");
            if (problems.Count == before)
            {
                Logger.LogError($"{RckPlugin.PluginName} {RckPlugin.Version} did not load. See the BepInEx errors above.");
                problems.Add("RCK did not load: see BepInEx\\LogOutput.log");
            }
        }

        private void Report(string log, string line)
        {
            Logger.LogError($"{RckPlugin.PluginName} {RckPlugin.Version} did not load: {log}. Extract the RCK Pack into the game folder again.");
            problems.Add(line);
        }

        private static bool RogueLibsPlusLoaded()
            => Chainloader.PluginInfos.TryGetValue(RogueLibsPlusGuid, out PluginInfo p) && p.Instance != null;

        private static string PluginPath(string location)
        {
            try
            {
                string dir = Path.GetDirectoryName(location)!;
                string plugins = Path.GetFullPath(Paths.PluginPath).TrimEnd('\\', '/');
                string target = Path.GetFullPath(dir).TrimEnd('\\', '/');
                // A DLL straight in plugins: name the file; one in a subfolder: name the top folder under plugins.
                if (string.Equals(target, plugins, StringComparison.OrdinalIgnoreCase)) target = Path.GetFullPath(location);
                else if (target.StartsWith(plugins + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    target = Path.Combine(plugins, target.Substring(plugins.Length + 1).Split(Path.DirectorySeparatorChar)[0]);
                return GameRelative(target);
            }
            catch (Exception) { return location; }
        }

        private static string GameRelative(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string game = Path.GetFullPath(Paths.GameRootPath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                return full.StartsWith(game, StringComparison.OrdinalIgnoreCase) ? full.Substring(game.Length) : full;
            }
            catch (Exception) { return path; }
        }

        // Only called when RogueLibsPlus is loaded, so its assembly resolves.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ShowWithMenuLines(List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
                RogueLibsPlus.MenuLines.SetProblem("RCKLoadProblem" + i, lines[i]);
        }

        // Without RogueLibsPlus: a plain red label just above the game's version text, while that is shown.
        private void OnGUI()
        {
            if (!onGui) return;
            try
            {
                GameController gc = GameController.gameController;
                if (gc == null || gc.versionText2 == null || !gc.versionText2.gameObject.activeInHierarchy) return;
                style ??= new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.LowerLeft,
                    fontSize = Mathf.Max(14, Screen.height / 45),
                    fontStyle = FontStyle.Bold,
                    wordWrap = false,
                    normal = { textColor = new Color(1f, 0.35f, 0.31f) },
                };
                var corners = new Vector3[4];
                gc.versionText2.rectTransform.GetWorldCorners(corners);
                Canvas canvas = gc.versionText2.canvas;
                Camera? cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                Vector2 topLeft = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
                float height = style.lineHeight * problems.Count + 8f;
                float x = Mathf.Clamp(topLeft.x, 0f, Screen.width - 50f);
                float y = Mathf.Clamp(Screen.height - topLeft.y - height, 0f, Screen.height - height);
                GUI.Label(new Rect(x, y, Screen.width - x, height), string.Join("\n", problems.ToArray()), style);
            }
            catch (Exception e)
            {
                onGui = false;
                Logger.LogWarning($"Could not show the load problem on screen: {e.Message}");
            }
        }
    }
}
