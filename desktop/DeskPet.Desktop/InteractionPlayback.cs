namespace DeskPet.Desktop;

internal sealed class InteractionPlayback
{
    private readonly Func<string, bool, ClipSpec> resolve;
    private double elapsed;
    public string Action { get; private set; } = "idle";
    public bool Original { get; private set; }
    public int Frame { get; private set; }
    public ClipSpec Clip { get; private set; }
    public InteractionPlayback(Func<string, bool, ClipSpec> resolver)
    {
        resolve = resolver; Clip = resolve("idle", false);
    }
    public void Play(string action, bool original)
    {
        Action = action; Original = original; Frame = 0; elapsed = 0; Clip = resolve(action, original);
    }
    public void Land(bool stumble) => Play(stumble ? "stumble" : "landing", Original);
    public void RefreshAppearance()
    {
        var progress = (Frame + elapsed * Clip.Fps) / Clip.Frames;
        Clip = resolve(Action, Original);
        var position = Math.Clamp(progress, 0, Math.BitDecrement(1d)) * Clip.Frames;
        Frame = Math.Min(Clip.Frames - 1, (int)position);
        elapsed = (position - Frame) / Clip.Fps;
    }
    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        elapsed += Math.Min(seconds, 1.0 / Clip.Fps);
        if (elapsed + 1e-9 < 1.0 / Clip.Fps) return;
        // Consume one frame only, even after a delayed dispatcher or wakeup.
        elapsed = Math.Min(Math.Max(0, elapsed - 1.0 / Clip.Fps), 1.0 / Clip.Fps);
        Frame++;
        if (Frame < Clip.Frames) return;
        if (Clip.Loop) Frame = 0;
        else Play(Action == "feeding" ? "happy" : "idle", Original);
    }
}
