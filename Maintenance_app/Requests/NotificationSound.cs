using System.Media;

namespace Maintenance.Shared;

// نغمة مضمّنة في التطبيق، تعمل باستقلال عن اختيار نغمات النظام في Windows.
public sealed class NotificationSound(Action? play = null, Func<DateTimeOffset>? clock = null)
{
    #region تشغيل الصوت مرة في دفعة التنبيهات مع دعم كتمه
    private const string ResourceName = "Maintenance.RequestNotification.wav";
    private static readonly Lazy<SoundPlayer> EmbeddedPlayer = new(CreateEmbeddedPlayer);
    private readonly Action player = play ?? PlayEmbeddedSound;
    private readonly Func<DateTimeOffset> utcNow = clock ?? (() => DateTimeOffset.UtcNow);
    private DateTimeOffset? lastPlayed;
    public bool TryPlay(bool enabled)
    {
        if (!enabled) return false;
        var now = utcNow();
        // قد تصل حالات مختلفة في قراءة واحدة؛ لا نشغّل أصواتًا متراكبة خلال الثانية نفسها.
        if (lastPlayed is { } previous && now - previous < TimeSpan.FromSeconds(1)) return false;
        lastPlayed = now;
        try { player(); return true; }
        catch (Exception ex)
        {
            // نحتفظ بسبب التعذر في سجل التشخيص ويستمر الإشعار في العرض والاستلام.
            System.Diagnostics.Debug.WriteLine($"تعذر تشغيل نغمة الإشعار: {ex.Message}");
            return false;
        }
    }

    private static void PlayEmbeddedSound() => EmbeddedPlayer.Value.Play();

    private static SoundPlayer CreateEmbeddedPlayer()
    {
        // نحمّل النغمة القصيرة مرة واحدة ونبقي المشغّل جاهزًا للتشغيل غير المتزامن.
        var stream = typeof(NotificationSound).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("مورد نغمة إشعار الصيانة غير موجود.");
        var sound = new SoundPlayer(stream);
        try
        {
            sound.Load();
            return sound;
        }
        catch
        {
            sound.Dispose();
            stream.Dispose();
            throw;
        }
    }
    #endregion
}
