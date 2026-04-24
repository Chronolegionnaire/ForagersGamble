using System;

namespace ForagersGamble.Patches;

internal static class NameMaskingScope
{
    [ThreadStatic] private static int _depth;
    public static bool IsActive => _depth > 0;

    public static void Enter() { _depth++; }
    public static void Exit() { if (_depth > 0) _depth--; }
    public static IDisposable Push() => new Scope();

    private sealed class Scope : IDisposable
    {
        public Scope() { Enter(); }
        public void Dispose() => Exit();
    }
}