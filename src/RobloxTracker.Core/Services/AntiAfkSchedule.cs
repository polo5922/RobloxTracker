namespace RobloxTracker.Core.Services;

public enum AntiAfkAction
{
    /// <summary>L'utilisateur joue : rien à faire.</summary>
    Skip,
    /// <summary>Roblox a déjà le focus : on appuie sans changer de fenêtre.</summary>
    PressInPlace,
    /// <summary>Il faut passer sur Roblox puis revenir à la fenêtre d'origine.</summary>
    SwitchWindow,
}

/// <summary>Règles de l'anti-AFK : quand appuyer, et comment.</summary>
public static class AntiAfkSchedule
{
    public const int WarningSeconds = 5;

    /// <summary>Roblox déconnecte après 20 minutes sans action.</summary>
    public const int RobloxKickMinutes = 20;

    /// <summary>En dessous de cette inactivité, un utilisateur sur Roblox est considéré comme en train de jouer.</summary>
    public static readonly TimeSpan ActiveThreshold = TimeSpan.FromSeconds(30);

    /// <summary>Délai aléatoire entre deux appuis (bornes inversées corrigées, 1 minute au minimum).</summary>
    public static TimeSpan NextDelay(int minMinutes, int maxMinutes, Random random)
    {
        var low = Math.Max(1, Math.Min(minMinutes, maxMinutes));
        var high = Math.Max(1, Math.Max(minMinutes, maxMinutes));
        return TimeSpan.FromSeconds(random.Next(low * 60, high * 60 + 1));
    }

    public static AntiAfkAction Decide(bool robloxIsForeground, TimeSpan idleTime)
    {
        if (!robloxIsForeground) return AntiAfkAction.SwitchWindow;
        return idleTime < ActiveThreshold ? AntiAfkAction.Skip : AntiAfkAction.PressInPlace;
    }
}
