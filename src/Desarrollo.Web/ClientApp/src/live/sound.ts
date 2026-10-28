export type AlertKind = 'message' | 'notification';

export interface AlertSound {
  /** Call from a click/key interaction so the browser can permit audio. */
  unlock(): Promise<boolean>;
  play(kind: AlertKind): void;
  close(): void;
}

type Tone = { oscillator: OscillatorNode; gain: GainNode };

/** Quiet local tones: no media download and no audio context until user interaction. */
export function createAlertSound(): AlertSound {
  let context: AudioContext | null = null;
  let unlocking: Promise<boolean> | null = null;
  let ready = false;
  let closed = false;
  let lastPlayedAt = -Infinity;
  const tones = new Set<Tone>();

  const release = (tone: Tone) => {
    if (!tones.delete(tone)) return;
    tone.oscillator.onended = null;
    try { tone.oscillator.disconnect(); } catch { /* already disconnected */ }
    try { tone.gain.disconnect(); } catch { /* already disconnected */ }
  };

  const unlock = (): Promise<boolean> => {
    if (closed) return Promise.resolve(false);
    if (unlocking) return unlocking;
    unlocking = (async () => {
      try {
        if (!context || context.state === 'closed') {
          const Audio = typeof window === 'undefined' ? undefined : window.AudioContext
            || (window as Window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
          if (!Audio) return false;
          context = new Audio();
          lastPlayedAt = -Infinity;
        }
        const current = context;
        if (current.state !== 'running') await current.resume();
        ready = !closed && context === current && current.state === 'running';
        return ready;
      } catch {
        ready = false;
        return false;
      }
    })().finally(() => { unlocking = null; });
    return unlocking;
  };

  const play = (kind: AlertKind) => {
    const current = context;
    if (closed || !ready || !current || current.state !== 'running') return;
    const now = current.currentTime;
    if (now - lastPlayedAt < 0.7) return;

    const created: Tone[] = [];
    try {
      const frequencies = kind === 'message' ? [660, 880] : [523.25, 659.25];
      frequencies.forEach((frequency, index) => {
        const oscillator = current.createOscillator();
        let gain: GainNode;
        try { gain = current.createGain(); }
        catch (error) { try { oscillator.disconnect(); } catch { /* unconnected source */ } throw error; }
        const tone = { oscillator, gain };
        created.push(tone); tones.add(tone);
        const start = now + index * 0.12;
        tone.oscillator.type = 'sine';
        tone.oscillator.frequency.setValueAtTime(frequency, start);
        tone.gain.gain.setValueAtTime(0, start);
        tone.gain.gain.linearRampToValueAtTime(0.045, start + 0.012);
        tone.gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.11);
        tone.oscillator.connect(tone.gain);
        tone.gain.connect(current.destination);
        tone.oscillator.onended = () => release(tone);
        tone.oscillator.start(start);
        tone.oscillator.stop(start + 0.12);
      });
      lastPlayedAt = now;
    } catch {
      // Audio is optional; a browser/device error must not interrupt chat refresh.
      for (const tone of created) {
        try { tone.oscillator.stop(); } catch { /* source never started or already ended */ }
        release(tone);
      }
    }
  };

  const close = () => {
    closed = true; ready = false;
    for (const tone of [...tones]) {
      try { tone.oscillator.stop(); } catch { /* source already ended */ }
      release(tone);
    }
    const current = context;
    context = null;
    if (current && current.state !== 'closed') {
      try { void current.close().catch(() => {}); } catch { /* audio device already unavailable */ }
    }
  };

  return { unlock, play, close };
}
