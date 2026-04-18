using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace NewsTowerAutoAssign
{
    // Plugin bootstrap. Owns configuration, logger, and Harmony patch install.
    // All decision logic lives in AssignmentEvaluator; this class just wires it up.
    //
    // Every ConfigEntry below is currently DEVELOPER-ONLY: hidden from the
    // in-game ConfigurationManager UI and tagged IsAdvanced so even mods that
    // bypass Browsable still categorise them as "power user only". The .cfg
    // file on disk still works for my own testing. When we decide which knobs
    // real players should see, we'll drop the `Hidden` helper for just those.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class AutoAssignPlugin : BaseUnityPlugin
    {
        // Kept as constants so the Harmony instance id, BepInPlugin metadata,
        // and any future "tell me your version" diagnostic all agree by
        // construction. Bump PluginVersion in one place per release.
        private const string PluginGuid = "newstower.autoassign";
        private const string PluginName = "News Tower Auto Assign";
        private const string PluginVersion = "1.0.3";

        // Harmony instance id. Distinct from PluginGuid deliberately - the
        // reverse-domain string here matches the namespace convention other
        // mods use for their Harmony ids and is already on the wire for
        // anyone monitoring Harmony patch ownership.
        private const string HarmonyId = "com.yourname.newstower.autoassign";

        // Default values for configuration entries. Duplicated from the
        // summary review recommendation: any default that's ever tuned
        // lives as a named constant so the code reads as intent rather
        // than magic numbers.
        private const float DefaultDiscardIfNoReporterHours = 4.0f;
        private const int DefaultMinReportersToActivate = 3;

        private const string ConfigSection = "Dev";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> AutoAssignAds;
        internal static ConfigEntry<bool> AutoAssignOnlyObviousPath;
        internal static ConfigEntry<bool> AutoResolveBribes;
        internal static ConfigEntry<bool> AutoSkipRiskPopups;
        internal static ConfigEntry<bool> AutoSkipSuitcasePopups;
        internal static ConfigEntry<bool> AvoidRisksEnabled;
        internal static ConfigEntry<bool> ChaseGoalsEnabled;
        internal static ConfigEntry<bool> DiscardFreshStoriesOnWeekend;
        internal static ConfigEntry<float> DiscardIfNoReporterForHours;
        internal static ConfigEntry<bool> AutoAssignEnabled;
        internal static ConfigEntry<bool> GlobePinOwnershipEnabled;
        internal static ConfigEntry<int> MinReportersToActivate;

#if DEBUG
        // Developer-only logging knobs. In Release builds AssignmentLog's
        // non-error helpers are [Conditional("DEBUG")] no-ops, so these
        // toggles have nothing to gate and are omitted from the .cfg file.
        internal static ConfigEntry<bool> VerboseLogs;
        internal static ConfigEntry<bool> OnlyLogTests;
#endif

        private void Awake()
        {
            Log = BepInEx.Logging.Logger.CreateLogSource("AutoAssign");
            try
            {
                BindConfig();
                ClampMinReportersToActivate();
                MinReportersToActivate.SettingChanged += (_, __) => ClampMinReportersToActivate();
                new Harmony(HarmonyId).PatchAll();
                VerifyReflection();
                AssignmentLog.Info(
                    "SYSTEM",
                    "Loaded and Harmony patches applied. Auto-assign is "
                        + (AutoAssignEnabled.Value ? "enabled." : "disabled.")
                );
            }
            catch (System.Exception ex)
            {
                // A patch that fails to install is the single most dangerous
                // thing we can do to someone's save. Log loudly and let the
                // game continue unpatched rather than crash.
                AssignmentLog.Error("Awake failed - auto-assign will not run this session: " + ex);
            }
        }

        // Builds a ConfigDescription that ConfigurationManager will hide from
        // its UI entirely (Browsable=false) and, as a belt-and-braces fallback
        // for custom UI forks, also marks as IsAdvanced. Every flag uses this
        // for now - no knob is ready to expose to regular players yet.
        private static ConfigDescription Hidden(string description) =>
            new ConfigDescription(
                description,
                null,
                new ConfigurationManagerAttributes { Browsable = false, IsAdvanced = true }
            );

        // Binds every [Dev] key (alphabetical in BindDevSectionConfig) plus DEBUG-only entries.
        private void BindConfig()
        {
            BindDevSectionConfig();
            BindDebugConfig();
        }

        // All [Dev] keys in alphabetical order by setting name (matches README).
        private void BindDevSectionConfig()
        {
            AutoAssignAds = BindHidden(
                "AutoAssignAds",
                true,
                "Automatically assign idle staff to ads on the Ads tab. Uses the same "
                    + "skill-matching logic as the news automation - whoever has the right "
                    + "skill and is free gets the work. Boycotted ads are skipped. The "
                    + "MinReportersToActivate gate does NOT apply to ads."
            );
            AutoAssignOnlyObviousPath = BindHidden(
                "AutoAssignOnlyObviousPath",
                false,
                "Skip auto-assign for multi-path stories for manual assignment. If ChaseGoals "
                    + "is on, auto-assign only when the goal priority yields exactly one "
                    + "winning assignable path."
            );
            AutoResolveBribes = BindHidden(
                "AutoResolveBribes",
                true,
                "Automatically pay bribes when affordable. Cost matches manual play. "
                    + "Left for manual handling if not affordable. When false, any story "
                    + "that has an incomplete bribe is fully manual."
            );
            AutoSkipRiskPopups = BindHidden(
                "AutoSkipRiskPopups",
                true,
                "Automatically dismiss risk spinner popups. Outcome is identical; the popup is cosmetic."
            );
            AutoSkipSuitcasePopups = BindHidden(
                "AutoSkipSuitcasePopups",
                true,
                "Automatically handle new-item suitcase rewards: pre-resolves unlocked "
                    + "suitcases so the chain never stalls waiting for the player to view "
                    + "the story, and auto-skips the popup if it still manages to open. "
                    + "Unlock side-effect is identical to manual play."
            );
            AvoidRisksEnabled = BindHidden(
                "AvoidRisks",
                true,
                "Skip risky news items (Injury, Lawsuit, etc.) unless they also match a "
                    + "weekly goal. If ChaseGoals is off, risky news items are always skipped."
            );
            ChaseGoalsEnabled = BindHidden(
                "ChaseGoals",
                true,
                "Prefer story file paths whose skill matches a current weekly goal tag."
            );
            DiscardFreshStoriesOnWeekend = BindHidden(
                "DiscardFreshStoriesOnWeekend",
                true,
                "Discard fresh (unstarted) stories that arrive on Saturday or Sunday. If "
                    + "ChaseGoals is on, fresh stories that match an uncovered weekly goal "
                    + "are kept even on weekends."
            );
            DiscardIfNoReporterForHours = BindHidden(
                "DiscardIfNoReporterForHours",
                DefaultDiscardIfNoReporterHours,
                "Discard a news item if no reporter with the right skill will be free "
                    + "within this many in-game hours (0 = disabled). Fractional values "
                    + "are accepted."
            );
            AutoAssignEnabled = BindHidden(
                "Enabled",
                true,
                "Automatically assign reporters to news items when they appear. The mod "
                    + "does nothing when set to false."
            );
            GlobePinOwnershipEnabled = BindHidden(
                "GlobePinOwnershipEnabled",
                true,
                "Globe pins are tinted green when all stories at the pin are mod-tracked, "
                    + "white when none, amber when mixed."
            );
            MinReportersToActivate = BindHidden(
                "MinReportersToActivate",
                DefaultMinReportersToActivate,
                "Below this many reporters the mod will not auto-assign news stories. The "
                    + "default is 3 and cannot be lowered: any value below 3 is clamped "
                    + "back to 3. Values above 3 are accepted."
            );
        }

        private static void ClampMinReportersToActivate()
        {
            int v = MinReportersToActivate.Value;
            if (v < DefaultMinReportersToActivate)
                MinReportersToActivate.Value = DefaultMinReportersToActivate;
        }

        // Developer-only knobs. Conditional-compiled out of Release builds
        // entirely because the code paths they gate (AssignmentLog.Verbose,
        // test-only log filtering) are [Conditional("DEBUG")].
        private void BindDebugConfig()
        {
#if DEBUG
            VerboseLogs = BindHidden(
                "VerboseLogs",
                false,
                "Enable low-level diagnostic logs. Intended for bug reports."
            );
            OnlyLogTests = BindHidden(
                "OnlyLogTests",
                false,
                "Suppress every non-test log line. Only in-game test banners + PASS/FAIL/SKIP summaries print. Errors still fire."
            );
#endif
        }

        // Typed overloads so every bind site reads like
        //   X = BindHidden("Key", default, "Description.");
        // rather than the Section / ConfigDescription ceremony. Section is
        // fixed at ConfigSection ("Dev") for every developer knob below.
        private ConfigEntry<bool> BindHidden(string key, bool defaultValue, string description) =>
            Config.Bind(ConfigSection, key, defaultValue, Hidden(description));

        private ConfigEntry<int> BindHidden(string key, int defaultValue, string description) =>
            Config.Bind(ConfigSection, key, defaultValue, Hidden(description));

        private ConfigEntry<float> BindHidden(string key, float defaultValue, string description) =>
            Config.Bind(ConfigSection, key, defaultValue, Hidden(description));

        // Every reflection target in the mod is probed at startup rather than
        // lazily at first use so a game-update compat regression surfaces once
        // at plugin load rather than once per scan target (which would be
        // noisy) and independent of whether the relevant automation ever fires
        // in the player's session. Every error below is routed through
        // AssignmentLog.Error so it survives Release-build log suppression.
        private void VerifyReflection()
        {
            VerifyProgressDoneEventReflection();
            VerifySuitcaseReflection();
        }

        private static void VerifyProgressDoneEventReflection()
        {
            if (GameReflection.ProgressDoneEventFieldAvailable)
                return;
            AssignmentLog.Error(
                "REFLECTION: NewsItemStoryFile.progressDoneEvent not found - ghost-assignment detection disabled. This usually means News Tower got a game update that renamed or removed the field."
            );
        }

        private static void VerifySuitcaseReflection()
        {
            var (suitcaseType, missingSuitcaseMembers) =
                SuitcaseAutomation.ProbeReflectionTargets();
            if (missingSuitcaseMembers == null || missingSuitcaseMembers.Length == 0)
                return;
            AssignmentLog.Error(
                "REFLECTION: SuitcaseAutomation cannot find ["
                    + string.Join(", ", missingSuitcaseMembers)
                    + "] on "
                    + suitcaseType
                    + " - suitcase auto-resolve will no-op this session. "
                    + "Likely a News Tower game update; file a bug report including the game version."
            );
        }
    }
}
