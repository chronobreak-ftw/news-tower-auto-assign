using System.Reflection;
using Reportables;

namespace NewsTowerAutoAssign
{
    // Centralised reflection handles for the handful of game internals the mod
    // has to poke. All targets are resolved once at plugin startup (see
    // AutoAssignPlugin.VerifyReflection) so a game-update regression surfaces
    // at load time rather than the first time the affected automation fires.
    //
    // Why not scatter the FieldInfo/MethodInfo declarations across the files
    // that use them: a single rename in the game used to need fixing in two
    // places (AssignmentEvaluator + AdAutomation both held their own
    // NewsItemStoryFile.progressDoneEvent handle). One source of truth kills
    // that foot-gun and makes the reflection surface easy to audit.
    internal static class GameReflection
    {
        // NewsItemStoryFile.progressDoneEvent - private instance field the
        // game uses to track "this slot currently has a job running". We
        // never write to it; a non-null value means a second AssignTo would
        // ghost the first. The game has no public accessor; reflection is
        // the only option.
        internal static readonly FieldInfo ProgressDoneEventField =
            typeof(NewsItemStoryFile).GetField(
                ProgressDoneEventFieldName,
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        internal static bool ProgressDoneEventFieldAvailable => ProgressDoneEventField != null;

        // True when the slot already has a job running (progressDoneEvent is
        // non-null). Returns false when the reflection field itself isn't
        // available - callers rely on plugin-startup probing to surface that
        // case, so defaulting to "not running" here lets assignment proceed
        // and matches pre-reflection behaviour.
        internal static bool IsSlotAlreadyRunning(NewsItemStoryFile storyFile)
        {
            if (ProgressDoneEventField == null || storyFile == null)
                return false;
            return ProgressDoneEventField.GetValue(storyFile) != null;
        }

        private const string ProgressDoneEventFieldName = "progressDoneEvent";
    }
}
