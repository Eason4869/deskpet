using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    internal interface ILiquidThemeSound
    {
        void Advance(double progress);
        void Complete();
        void Cancel();
    }

    internal delegate ILiquidThemeSound BeginLiquidThemeSound(bool dark, TimeSpan duration, bool shortFeedback, string motion);

    internal sealed class LiquidThemeSoundBinding
    {
        readonly BeginLiquidThemeSound begin;
        ILiquidThemeSound active;

        internal LiquidThemeSoundBinding(BeginLiquidThemeSound begin) { this.begin = begin; }

        internal void Start(bool dark, TimeSpan duration, bool shortFeedback, string motion)
        {
            Cancel();
            try { if (begin != null) active = begin(dark, duration, shortFeedback, motion); }
            catch { /* The visual transition also works without audio. */ }
        }

        internal void Advance(double progress)
        {
            try { if (active != null) active.Advance(progress); }
            catch { Cancel(); }
        }

        internal void Complete()
        {
            try { if (active != null) active.Complete(); }
            catch { Cancel(); }
        }

        internal void Cancel()
        {
            var request = active;
            active = null;
            try { if (request != null) request.Cancel(); }
            catch { }
        }
    }

    // A single worker owns the output. UI frames publish the latest visual stage
    // so a slow device cannot play a backlog of sounds after the liquid has moved on.
    internal sealed class LiquidThemeSoundPlayback : ILiquidThemeSound
    {
        readonly object gate = new object();
        readonly SemaphoreSlim signal = new SemaphoreSlim(1, 1);
        readonly Func<ILiveAudioOutput> createOutput;
        readonly Func<int> allowedVolume;
        readonly bool dark;
        readonly double duration;
        readonly string motion;
        readonly double onset, travel, travelEnd;
        LiquidThemeCue next;
        long requestedAt;
        double progress;
        int lastVolume;
        bool terminal, cancelled, ended;

        internal LiquidThemeSoundPlayback(Func<ILiveAudioOutput> createOutput, Func<int> allowedVolume,
            bool dark, TimeSpan duration, bool shortFeedback, int volume, string motion)
        {
            this.createOutput = createOutput;
            this.allowedVolume = allowedVolume;
            this.dark = dark;
            this.duration = duration.TotalSeconds;
            this.motion = ThemeTransitionMotion.Normalize(motion);
            ThemeTransitionMotion.SoundStages(this.motion, out onset, out travel, out travelEnd);
            lastVolume = volume;
            terminal = shortFeedback;
            SetCue(shortFeedback ? LiquidThemeCue.Short : LiquidThemeCue.Drop, 0);
        }

        public void Advance(double visualProgress)
        {
            if (visualProgress < onset) return;
            var cue = visualProgress < travel ? LiquidThemeCue.Splash : LiquidThemeCue.Flow;
            lock (gate)
            {
                if (terminal || cancelled || ended || (int)cue <= (int)next) return;
                SetCue(cue, visualProgress);
                Wake();
            }
        }

        public void Complete()
        {
            lock (gate)
            {
                if (terminal || cancelled || ended) return;
                terminal = true;
                SetCue(LiquidThemeCue.Finish, 1);
                Wake();
            }
        }

        public void Cancel()
        {
            lock (gate)
            {
                if (cancelled || ended) return;
                cancelled = true;
                Wake();
            }
        }

        internal void Refresh(int volume)
        {
            if (volume <= 0) { Cancel(); return; }
            lock (gate)
            {
                if (cancelled || ended || lastVolume == volume) return;
                lastVolume = volume;
                Wake();
            }
        }

        void SetCue(LiquidThemeCue cue, double visualProgress)
        {
            next = cue;
            progress = visualProgress;
            requestedAt = Stopwatch.GetTimestamp();
        }

        void Wake()
        {
            if (signal.CurrentCount == 0) signal.Release();
        }

        bool Current(LiquidThemeCue cue)
        {
            lock (gate) return !cancelled && next == cue;
        }

        internal async Task PlayAsync()
        {
            ILiveAudioOutput output = null;
            ILiveAudioFailureSource failure = null;
            LiquidThemeCue? played = null;
            long tailEndsAt = 0;
            try
            {
                while (await WaitForCue(tailEndsAt, output as IGuardedLiveAudioOutput).ConfigureAwait(false))
                {
                    LiquidThemeCue cue;
                    double visualProgress;
                    long started;
                    lock (gate)
                    {
                        if (cancelled) break;
                        cue = next;
                        started = requestedAt;
                        visualProgress = progress;
                    }

                    int volume = allowedVolume();
                    if (volume <= 0) break;
                    if (played == cue)
                    {
                        if (output != null) output.SetVolume(volume);
                        continue;
                    }

                    bool isTail = cue == LiquidThemeCue.Finish || cue == LiquidThemeCue.Short;
                    double available = RemainingSeconds(cue, visualProgress, started);
                    if (available <= 0)
                    {
                        if (isTail) break;
                        continue;
                    }
                    var pcm = LiquidThemeAudio.Render(cue, dark, TimeSpan.FromSeconds(Math.Max(0, available)), motion);
                    if (pcm.Length == 0 || !Current(cue)) continue;

                    if (output == null)
                    {
                        output = createOutput();
                        if (output == null) break;
                        failure = output as ILiveAudioFailureSource;
                        if (failure != null) failure.Failed += OutputFailed;
                    }
                    if (!Current(cue)) continue;
                    volume = allowedVolume();
                    if (volume <= 0) break;
                    if (!isTail)
                        pcm = Trim(pcm, RemainingSeconds(cue, visualProgress, started));
                    if (pcm.Length == 0) continue;

                    var guarded = output as IGuardedLiveAudioOutput;
                    output.SetVolume(volume);
                    if (played.HasValue && (guarded == null || guarded.HasPendingAudio)) output.Interrupt();
                    Func<bool> permit = () => Current(cue) && RemainingSeconds(cue, visualProgress, started) > 0
                        && allowedVolume() > 0;
                    if (guarded != null)
                    {
                        if (!guarded.TryEnqueue(pcm, LiquidThemeAudio.SampleRate, permit))
                        {
                            if (isTail) break;
                            continue;
                        }
                    }
                    else
                    {
                        if (!permit())
                        {
                            if (isTail) break;
                            continue;
                        }
                        output.Enqueue(pcm, LiquidThemeAudio.SampleRate);
                    }
                    played = cue;
                    if (isTail)
                    {
                        // The deadline drains this last buffer. All earlier sounds
                        // are triggered exclusively by frame progress.
                        double seconds = (double)pcm.Length / (2 * LiquidThemeAudio.SampleRate);
                        double grace = guarded == null ? .035 : .5;
                        tailEndsAt = Stopwatch.GetTimestamp() + (long)((seconds + grace) * Stopwatch.Frequency);
                    }
                }
            }
            catch
            {
                // Device initialization and playback are optional.
            }
            finally
            {
                lock (gate) ended = true;
                signal.Dispose();
                if (failure != null) failure.Failed -= OutputFailed;
                if (output != null)
                {
                    try { output.Interrupt(); } catch { }
                    try { output.Dispose(); } catch { }
                }
            }
        }

        async Task<bool> WaitForCue(long tailEndsAt, IGuardedLiveAudioOutput output)
        {
            if (tailEndsAt == 0)
            {
                await signal.WaitAsync().ConfigureAwait(false);
                return true;
            }
            while (true)
            {
                double remaining = (tailEndsAt - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency;
                if (remaining <= 0 || (output != null && !output.HasPendingAudio)) return false;
                double interval = output == null ? remaining : Math.Min(.025, remaining);
                if (await signal.WaitAsync(TimeSpan.FromSeconds(Math.Max(.001, interval))).ConfigureAwait(false)) return true;
            }
        }

        double RemainingSeconds(LiquidThemeCue cue, double visualProgress, long started)
        {
            double end = travelEnd;
            if (cue == LiquidThemeCue.Drop) end = onset;
            if (cue == LiquidThemeCue.Splash) end = travel;
            double age = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            if (cue == LiquidThemeCue.Finish || cue == LiquidThemeCue.Short) return .35 - age;
            return duration * (end - visualProgress) - age;
        }

        static byte[] Trim(byte[] pcm, double available)
        {
            int count = (int)Math.Min(pcm.Length / 2, Math.Max(0, available) * LiquidThemeAudio.SampleRate);
            if (count == pcm.Length / 2) return pcm;
            var result = new byte[count * 2];
            Buffer.BlockCopy(pcm, 0, result, 0, result.Length);
            int fade = Math.Min(count, LiquidThemeAudio.SampleRate / 100);
            for (int i = 0; i < fade; i++)
            {
                int offset = result.Length - 2 * (fade - i);
                short sample = (short)(result[offset] | result[offset + 1] << 8);
                sample = (short)(sample * (fade - i - 1) / fade);
                result[offset] = (byte)sample;
                result[offset + 1] = (byte)(sample >> 8);
            }
            return result;
        }

        void OutputFailed(string _) { Cancel(); }
    }
}
