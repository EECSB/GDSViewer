const test = require('node:test');
const assert = require('node:assert');
const wav = require('../../wwwroot/js/speechWav.js');

//What the AI assistant's microphone sends a provider to transcribe, in the speech-to-text mode that records: a 16 kHz mono 16-bit
//WAV. A header a provider cannot read is a request refused with a message about the file, which says nothing about why.

test('a WAV carries the RIFF header a reader looks for, and its sizes', () => {
    const bytes = wav.encodeWav(new Float32Array([0, 0.5, -0.5, 1]), 16000);
    const view = new DataView(bytes.buffer);
    const text = (at, length) => String.fromCharCode(...bytes.slice(at, at + length));

    assert.strictEqual(bytes.length, 44 + 8);
    assert.strictEqual(text(0, 4), 'RIFF');
    assert.strictEqual(view.getUint32(4, true), 36 + 8);
    assert.strictEqual(text(8, 4), 'WAVE');
    assert.strictEqual(text(12, 4), 'fmt ');
    assert.strictEqual(view.getUint16(20, true), 1);
    assert.strictEqual(view.getUint16(22, true), 1);
    assert.strictEqual(view.getUint32(24, true), 16000);
    assert.strictEqual(view.getUint32(28, true), 32000);
    assert.strictEqual(view.getUint16(34, true), 16);
    assert.strictEqual(text(36, 4), 'data');
    assert.strictEqual(view.getUint32(40, true), 8);
});

test('samples are written as 16-bit numbers, a full swing to the ends and past it clipped', () => {
    const bytes = wav.encodeWav(new Float32Array([0, 1, -1, 2, -2]), 16000);
    const view = new DataView(bytes.buffer);
    const read = index => view.getInt16(44 + (index * 2), true);

    assert.deepStrictEqual([0, 1, 2, 3, 4].map(read), [0, 32767, -32768, 32767, -32768]);
});

test('a recording is brought down to the rate speech models listen at, three samples to one from 48 kHz', () => {
    const samples = new Float32Array(48000).fill(0.25);
    const bytes = wav.speechWav([samples], 48000);
    const view = new DataView(bytes.buffer);

    assert.strictEqual(view.getUint32(24, true), 16000);
    assert.strictEqual(view.getUint32(40, true), 16000 * 2);
    assert.strictEqual(view.getInt16(44, true), Math.round(0.25 * 0x7FFF));
});

test('two channels are averaged into one, and a rate already low is left as it is', () => {
    const mixed = wav.mixDown([new Float32Array([1, 0]), new Float32Array([0, 0])]);

    assert.deepStrictEqual(Array.from(mixed), [0.5, 0]);

    const kept = wav.downsample(new Float32Array([0.1, 0.2, 0.3]), 8000, 16000);

    assert.strictEqual(kept.length, 3);

    const bytes = wav.speechWav([new Float32Array(8000)], 8000);

    assert.strictEqual(new DataView(bytes.buffer).getUint32(24, true), 8000);
});
