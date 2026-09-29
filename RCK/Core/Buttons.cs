using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using RogueLibsCore;

namespace RCK
{
    /// <summary>
    ///   Vanilla <see cref="AgentInteractions"/> button names that RCK adds to NPCs and dispatches to
    ///   <c>AgentInteractions.PressedButton</c>. Always use these constants, never string literals:
    ///   <c>tools\ButtonCheck</c> fails the build check if one has no <c>PressedButton</c> case or no
    ///   vanilla Interface label in the current game.
    /// </summary>
    public static class VanillaButtons
    {
        public const string AdministerBloodBag = "AdministerBloodBag";
        public const string AssistMe = "AssistMe";
        public const string BorrowMoney = "BorrowMoney";
        public const string BribeBeer = "BribeBeer";
        public const string BribeCops = "BribeCops";
        public const string BribeWhiskey = "BribeWhiskey";
        public const string Buy = "Buy";
        public const string BuyKey = "BuyKey";
        public const string BuyKeyHotel = "BuyKeyHotel";
        public const string BuyRound = "BuyRound";
        public const string BuySafeCombination = "BuySafeCombination";
        public const string CauseRuckus = "CauseRuckus";
        public const string ElectionBribe = "ElectionBribe";
        public const string GetElectionResults = "GetElectionResults";
        public const string GiveBlood = "GiveBlood";
        public const string GiveMeMayorBadge = "GiveMeMayorBadge";
        public const string HackSomething = "HackSomething";
        public const string Heal = "Heal";
        public const string HireAsProtection = "HireAsProtection";
        public const string Identify = "Identify";
        public const string JoinMe = "JoinMe";
        public const string LeaveWeaponsBehind = "LeaveWeaponsBehind";
        public const string LockpickDoor = "LockpickDoor";
        public const string PayBackDebt = "PayBackDebt";
        public const string PayEntranceFee = "PayEntranceFee";
        public const string PlayBadMusic = "PlayBadMusic";
        public const string PlayMayorEvidence = "PlayMayorEvidence";
        public const string PurchaseSlave = "PurchaseSlave";
        public const string PutMoneyTowardHome = "PutMoneyTowardHome";
        public const string RobotEnrage = "RobotEnrage";
        public const string RunForOffice = "RunForOffice";
        public const string TamperRobotAim = "TamperRobotAim";
        public const string UseBloodBag = "UseBloodBag";
        public const string UseVoucher = "UseVoucher";

        private static HashSet<string>? all;

        /// <summary>Every constant declared above.</summary>
        public static IReadOnlyCollection<string> All => all ??= ButtonLabels.ConstantsOf(typeof(VanillaButtons));

        public static bool IsKnown(string name) => name != null && (all ??= ButtonLabels.ConstantsOf(typeof(VanillaButtons))).Contains(name);
    }

    /// <summary>
    ///   The English label of a custom interaction button. Put it on a <c>const string</c> in a static class named
    ///   <c>CustomButtons</c> and call <see cref="ButtonLabels.Register"/> with that class. A <c>CustomButtons</c>
    ///   constant without this attribute must reuse a vanilla Interface label (checked by <c>tools\ButtonCheck</c>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class ButtonLabelAttribute : Attribute
    {
        public ButtonLabelAttribute(string english) => English = english;
        public string English { get; }
    }

    /// <summary>Registers and checks Interface names for interaction buttons.</summary>
    public static class ButtonLabels
    {
        private static readonly HashSet<string> registered = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Registers every <see cref="ButtonLabelAttribute"/> constant of <paramref name="holder"/>.</summary>
        public static void Register(Type holder)
        {
            foreach (FieldInfo field in holder.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                ButtonLabelAttribute? label = field.GetCustomAttribute<ButtonLabelAttribute>();
                if (label != null) Register((string)field.GetRawConstantValue(), label.English);
            }
        }

        /// <summary>Registers an English Interface label for <paramref name="buttonName"/>, once.</summary>
        public static void Register(string buttonName, string english)
        {
            if (!registered.Add(buttonName)) return;
            try { RogueLibs.CreateCustomName(buttonName, NameTypes.Interface, new CustomNameInfo(english)); }
            catch (ArgumentException) { Rck.Log.LogDebug($"Interface label '{buttonName}' was already registered."); }
        }

        /// <summary>True when the game will show a real label for <paramref name="buttonName"/>.</summary>
        public static bool HasLabel(string buttonName)
            => registered.Contains(buttonName) || Enum.IsDefined(typeof(Google2u.InterfaceNameDB.rowIds), buttonName);

        /// <summary>
        ///   Makes sure a button never shows as <c>E_&lt;name&gt;</c>. A missing label is an RCK bug: it is logged
        ///   once as an error and a readable label is generated from the name.
        /// </summary>
        public static void Ensure(string buttonName)
        {
            if (buttonName == null || HasLabel(buttonName)) return;
            if (reported.Add(buttonName)) Rck.Log.LogError($"Interaction button '{buttonName}' has no label; using a generated one.");
            Register(buttonName, Humanize(buttonName));
        }

        internal static string Humanize(string name)
        {
            string s = name.StartsWith("RCK_", StringComparison.Ordinal) ? name.Substring(4) : name;
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '_') { if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' '); continue; }
                if (char.IsUpper(c) && i > 0 && char.IsLower(s[i - 1]) && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        internal static HashSet<string> ConstantsOf(Type holder)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldInfo field in holder.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                if (field.IsLiteral && field.FieldType == typeof(string))
                    set.Add((string)field.GetRawConstantValue());
            return set;
        }
    }
}
