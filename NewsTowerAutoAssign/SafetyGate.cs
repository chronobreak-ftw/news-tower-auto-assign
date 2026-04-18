namespace NewsTowerAutoAssign
{
    internal static class SafetyGate
    {
        private static bool _isOpen = false;

        internal static bool IsOpen => _isOpen;

        internal static void Open() => _isOpen = true;

        internal static void Close() => _isOpen = false;
    }
}
