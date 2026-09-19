using System.IO;
using System.Media;
using Parrot.Core.Audio;

namespace Parrot.App.Services;

/// <summary>Plays the prompt chime. A sound that fails to play must never cost the user a card.</summary>
public static class SoundService
{
    private static readonly Lazy<SoundPlayer?> Player = new(Create);

    /// <summary>Returns immediately; the chime plays in the background.</summary>
    public static void PlayChime()
    {
        try
        {
            Player.Value?.Play();
        }
        catch (Exception ex)
        {
            Log.Error("Не вдалося відтворити звук", ex);
        }
    }

    private static SoundPlayer? Create()
    {
        try
        {
            var player = new SoundPlayer(new MemoryStream(Chime.CreateWav()));
            player.Load();
            return player;
        }
        catch (Exception ex)
        {
            Log.Error("Не вдалося підготувати звук", ex);
            return null;
        }
    }
}
