"""Reproduce the three user-approved original DeskPet operation cues."""
import argparse
import hashlib
import math
from pathlib import Path
import struct
import wave

RATE = 48000
MODES = ((1, 1, .058), (4, .095, .012), (9, .004, .005))
CUES = {
    'appear': (.38, [(.012, .17, 523.251, .60), (.075, .17, 659.255, .65),
                    (.143, .19, 783.991, .70)], MODES,
               'd4b228c2563eba803a5f6efa5fd74b19d1637aca250022b7504f17e51247b17a'),
    'hide': (.40, [(.012, .18, 783.991, .64), (.084, .18, 659.255, .56),
                  (.162, .19, 523.251, .47)], MODES,
             '8f7d97ac3f21e46d7544bb9a6cfddd14018b04a1b722a2ac345f8c6d6770f9ee'),
    'switch': (.43, [(.012, .21, 659.255, .62), (.079, .22, 987.767, .49),
                    (.151, .24, 1318.51, .38)], MODES + ((2.76, .055, .090), (5.40, .009, .045)),
               'c0b26a39be738d567f0b3fa7fa204d97315389cce91077bdb3615a7be7914c14'),
}


def wood(t, length, hz, modes):
    if t <= 0 or t >= length:
        return 0.0
    attack = math.sin(min(1, t / .010) * math.pi / 2) ** 2
    release = math.sin(min(1, (length - t) / .045) * math.pi / 2) ** 2
    return attack * release * sum(gain * math.sin(2 * math.pi * ratio * hz * t)
                                  * math.exp(-t / decay) for ratio, gain, decay in modes)


def render(output, name, definition):
    length, notes, modes, expected = definition
    samples = []
    for i in range(round(length * RATE)):
        t = i / RATE
        value = sum(gain * (wood(t - start, duration, hz, modes)
                            + .065 * wood(t - start - .019, duration, hz, modes)
                            + .025 * wood(t - start - .037, duration, hz, modes))
                    for start, duration, hz, gain in notes)
        samples.append(value * .14)
    fade = round(RATE * .02)
    for i in range(fade):
        samples[-1-i] *= math.sin(math.pi * i / (2 * fade)) ** 2
    pcm = [round(value * 32767) for value in samples]
    assert pcm[0] == pcm[-1] == 0 and max(map(abs, pcm)) < 7500
    path = output / ('companion-' + name + '.wav')
    with wave.open(str(path), 'wb') as stream:
        stream.setnchannels(1)
        stream.setsampwidth(2)
        stream.setframerate(RATE)
        stream.writeframes(struct.pack('<' + 'h' * len(pcm), *pcm))
    actual = hashlib.sha256(path.read_bytes()).hexdigest()
    if actual != expected:
        raise RuntimeError('Generated cue differs from approved bytes: ' + name)
    print(name + ': ' + actual)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    for name, definition in CUES.items():
        render(args.output, name, definition)
