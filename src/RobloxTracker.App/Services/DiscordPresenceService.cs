using DiscordRPC;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.Services;

/// <summary>
/// Affiche le jeu en cours dans le profil Discord (nom, icône, temps écoulé, boutons Rejoindre / Voir le jeu).
/// Communique en local avec le client Discord : si Discord n'est pas lancé, rien ne se passe.
/// </summary>
public sealed class DiscordPresenceService(SettingsService settings) : IDisposable
{
    /// <summary>Application Discord « Roblox Tracker » (son nom apparaît dans « Joue à … »).</summary>
    public const string DefaultAppId = "1566541180743848199";

    private DiscordRpcClient? _client;
    private SessionInfo? _session;
    private string? _shownKey;

    public string AppId => string.IsNullOrWhiteSpace(settings.Current.DiscordAppId)
        ? DefaultAppId
        : settings.Current.DiscordAppId.Trim();

    /// <summary>Publie la session active (ou efface la présence si aucun jeu n'est en cours).</summary>
    public void Update(SessionInfo? session)
    {
        _session = session;
        var key = session is null ? null : $"{session.Id}|{session.GameName}|{session.IconUrl}|{session.JobId}|{session.StartUtc:O}";
        if (key == _shownKey && _client is not null) return;
        Publish(key);
    }

    /// <summary>À appeler après un changement de réglage (activation ou Application ID).</summary>
    public void Reset()
    {
        Close();
        Publish(_session is null ? null : "");
    }

    private void Publish(string? key)
    {
        try
        {
            if (_session is null || !settings.Current.DiscordPresence || string.IsNullOrEmpty(AppId))
            {
                _client?.ClearPresence();
                _shownKey = null;
                return;
            }

            if (_client is null)
            {
                _client = new DiscordRpcClient(AppId) { SkipIdenticalPresence = true };
                _client.Initialize();
            }
            _client.SetPresence(Build(_session));
            _shownKey = key;
        }
        catch (Exception)
        {
            // Discord absent, ID invalide… : jamais bloquant pour l'app.
            _shownKey = null;
        }
    }

    private static RichPresence Build(SessionInfo session)
    {
        var buttons = new List<Button>();
        if (RobloxLinks.Join(session) is { } join) buttons.Add(new Button { Label = "Rejoindre", Url = join });
        buttons.Add(new Button { Label = "Voir le jeu", Url = RobloxLinks.Game(session) });

        var name = Fit(session.GameName);
        return new RichPresence
        {
            Details = name,
            State = "Sur Roblox",
            Timestamps = new Timestamps(session.StartUtc),
            Assets = new Assets
            {
                LargeImageKey = session.IconUrl ?? "roblox",
                LargeImageText = name,
            },
            Buttons = [.. buttons],
        };
    }

    /// <summary>Discord exige entre 2 et 128 octets par texte.</summary>
    private static string Fit(string text)
    {
        text = text.Trim();
        if (text.Length < 2) text = text.PadRight(2);
        while (System.Text.Encoding.UTF8.GetByteCount(text) > 128) text = text[..^2] + "…";
        return text;
    }

    private void Close()
    {
        try
        {
            _client?.ClearPresence();
            _client?.Dispose();
        }
        catch (Exception) { }
        _client = null;
        _shownKey = null;
    }

    public void Dispose() => Close();
}
