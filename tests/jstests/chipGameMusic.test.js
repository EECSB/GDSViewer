const test = require('node:test');
const assert = require('node:assert');
const music = require('../../wwwroot/js/chipGameMusic.js');

//The chip game's music as it is written: the notes, the chords, and what turns them into a sixteenth-by-sixteenth list
//of what to play. The playing itself - Web Audio - is covered by the Playwright spec in e2e/.

test('a note is its letter, a sharp or a flat, and its octave - A4 is 69 and 440 hertz', () => {
    assert.strictEqual(music.pitchOf('A4'), 69);
    assert.strictEqual(music.pitchOf('C4'), 60);
    assert.strictEqual(music.pitchOf('F#5'), 78);
    assert.strictEqual(music.pitchOf('Bb3'), 58);
    assert.strictEqual(music.pitchOf('B#3'), music.pitchOf('C4'));

    assert.ok(Number.isNaN(music.pitchOf('H4')));
    assert.ok(Number.isNaN(music.pitchOf('A')));

    assert.strictEqual(music.frequencyOf(69), 440);
    assert.ok(Math.abs(music.frequencyOf(81) - 880) < 1e-9);
    assert.ok(Math.abs(music.frequencyOf(60) - 261.6256) < 1e-3);
});

test('a chord is its root and whether it is minor', () => {
    assert.deepStrictEqual(music.chordOf('Am'), { root: 9, third: 3 });
    assert.deepStrictEqual(music.chordOf('F#'), { root: 6, third: 4 });
    assert.deepStrictEqual(music.chordOf('Bb'), { root: 10, third: 4 });
    assert.deepStrictEqual(music.chordOf('Cb'), { root: 11, third: 4 });

    assert.strictEqual(music.chordOf('Amaj7'), null);
});

test('a line is read as notes with their steps, and a bar that does not come to a bar is said', () => {
    const read = music.readLine('A4:6 G4:2 . C5:7 | E5:16', 'a line');

    assert.strictEqual(read.bars, 2);
    assert.deepStrictEqual(read.notes, [[0, 6, 69], [6, 2, 67], [9, 7, 72], [16, 16, 76]]);

    assert.throws(() => music.readLine('A4:6 G4:2 | E5:16', 'short'), /short, bar 1: comes to 8 sixteenths, not 16/);
    assert.throws(() => music.readLine('A4:16 | E5:12 G5:8', 'long'), /long, bar 2: comes to 20/);
    assert.throws(() => music.readLine('H4:16', 'wrong'), /wrong, bar 1: "H4:16" is not a note/);
    assert.throws(() => music.readLine('A4:x .:15', 'lengthless'), /has no length/);
});

///
///**Every tune is written right**: compiled whole, every part of every bar comes to a bar and every chord is one - or the
///compile says which is not - and each goes round in a whole number of bars, its tune's notes inside what the voices play.
///
test('every tune compiles, a whole number of bars round, with its tune where the voices play it', () => {
    for (const [name, track] of Object.entries(music.TRACKS)) {
        const compiled = music.compile(track);

        assert.strictEqual(compiled.bpm, track.bpm);
        assert.strictEqual(compiled.steps.length % music.BAR, 0, `${name} does not end on a bar`);
        assert.ok(compiled.steps.length >= music.BAR * 16, `${name} is only ${compiled.steps.length / music.BAR} bars`);

        let lead = 0;

        for (const step of compiled.steps) {
            for (const one of step) {
                if (one.drum != null) {
                    assert.ok(['kick', 'snare', 'hat'].includes(one.drum), `${name} has a drum called ${one.drum}`);

                    continue;
                }

                for (const pitch of one.pitches)
                    assert.ok(pitch >= 28 && pitch <= 92, `${name} has a note at ${pitch}`);

                if (one.lead === true)
                    lead++;
            }
        }

        assert.ok(lead > 32, `${name}'s tune has ${lead} notes`);
    }
});

test('the sections are played in their order, and each bar starts with its chord on the pad', () => {
    const track = music.TRACKS.car;
    const compiled = music.compile(track);

    let bar = 0;

    for (const name of track.form) {
        for (const chordName of track.sections[name].chords.split('|').map(each => each.trim())) {
            const chord = music.chordOf(chordName);
            const pad = compiled.steps[bar * music.BAR].find(one => one.voice === 'pad');

            assert.ok(pad != null, `no pad at bar ${bar + 1}`);
            assert.strictEqual(pad.pitches[0] % 12, chord.root, `bar ${bar + 1} is not ${chordName}`);
            assert.strictEqual(pad.pitches[1] - pad.pitches[0], chord.third);
            assert.strictEqual(pad.length, music.BAR);

            bar++;
        }
    }

    assert.strictEqual(bar * music.BAR, compiled.steps.length);

    //The verse has no tune over it, and the chorus's starts on its first sixteenth.
    const firstSung = compiled.steps.findIndex(step => step.some(one => one.lead === true));

    assert.strictEqual(firstSung, 8 * music.BAR);
    assert.strictEqual(compiled.steps[firstSung].find(one => one.lead === true).pitches[0], music.pitchOf('A4'));
});

test('a section\'s last bar has the fill, and the rest the beat', () => {
    const track = music.TRACKS.jet;
    const compiled = music.compile(track);
    const snaresIn = at => compiled.steps.slice(at * music.BAR, (at + 1) * music.BAR).filter(step => step.some(one => one.drum === 'snare')).length;

    const beat = [...track.beats.beat.snare].filter(hit => hit === 'x').length;
    const fill = [...track.beats.fill.snare].filter(hit => hit === 'x').length;

    assert.ok(fill > beat);
    assert.strictEqual(snaresIn(0), beat);
    assert.strictEqual(snaresIn(7), fill);
    assert.strictEqual(snaresIn(8), beat);
});

test('a tune that does not fit its chords is said, as is a drum line that is not a bar', () => {
    const wrong = {
        bpm: 120,
        form: ['A'],
        lead: 'pulse25',
        parts: [],
        kit: 'chip',
        beats: { beat: { kick: 'x...x...x...x...' } },
        sections: { A: { chords: 'C | G', tune: 'C5:16' } }
    };

    assert.throws(() => music.compile(wrong), /tune is 1 bars, and its chords 2/);

    wrong.sections.A.tune = 'C5:16 | D5:16';
    wrong.beats.beat.kick = 'x...x...x...x..';

    assert.throws(() => music.compile(wrong), /kick is 15 sixteenths, not 16/);

    wrong.beats.beat.kick = 'x...x...x...x...';
    wrong.sections.A.chords = 'C | Gsus4';

    assert.throws(() => music.compile(wrong), /"Gsus4" is not a chord/);
});

test('each style plays its chord where it should', () => {
    const am = music.chordOf('Am');

    //The pad, the whole chord for the bar, its root between E3 and D#4.
    assert.deepStrictEqual(music.STYLES.pad(am), [[0, 16, [57, 60, 64]]]);

    //The octaves, eighths low and high in turn.
    assert.deepStrictEqual(music.STYLES.octaves(am).map(note => note[2][0]), [33, 45, 33, 45, 33, 45, 33, 45]);

    //The arpeggio, root, fifth, octave and the third over it, every sixteenth.
    assert.deepStrictEqual(music.STYLES.arpeggio(am).slice(0, 5).map(note => note[2][0]), [57, 64, 69, 72, 57]);

    //The gallop, an eighth and two sixteenths, the last up an octave.
    assert.deepStrictEqual(music.STYLES.gallop(music.chordOf('Em')).slice(0, 3), [[0, 2, [40]], [2, 1, [40]], [3, 1, [52]]]);

    for (const style of Object.keys(music.STYLES)) {
        const lengths = music.STYLES[style](am).reduce((sum, note) => sum + note[1], 0);

        assert.strictEqual(lengths, 16, `${style} plays ${lengths} sixteenths of a bar`);
    }
});
