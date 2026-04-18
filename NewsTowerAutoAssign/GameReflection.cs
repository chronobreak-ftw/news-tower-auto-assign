using System.Reflection;
using Reportables;

namespace NewsTowerAutoAssign
{
    internal static class GameReflection
    {
        internal static readonly FieldInfo ProgressDoneEventField =
            typeof(NewsItemStoryFile).GetField(
                ProgressDoneEventFieldName,
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        internal static bool ProgressDoneEventFieldAvailable => ProgressDoneEventField != null;

        internal static bool IsSlotAlreadyRunning(NewsItemStoryFile storyFile)
        {
            if (ProgressDoneEventField == null || storyFile == null)
                return false;
            return ProgressDoneEventField.GetValue(storyFile) != null;
        }

        private const string ProgressDoneEventFieldName = "progressDoneEvent";
    }
}
