//The chip game's music: a tune for each of the three - synthwave in the car, 8-bit in the plane and in the jet - written
//here as notes, and what turns them into a list of what to play on every sixteenth for js/ThreeInterop.js's GameMusic.
//No DOM and no Web Audio, so it can be checked under Node (see tests/jstests/): every bar of every part has to come to
//a bar, which a slip in the writing otherwise does not say.
//Exposed as window.chipGameMusic in the browser and as module.exports under Node.
//
//**All three are this app's own**, written for it on 2026-10-14: nothing to license and nothing to fetch, which keeps
//the app what it is offline - the way the game's sounds are made rather than played from files.
(function (factory) {
    const api = factory();

    if (typeof module !== 'undefined' && module.exports)
        module.exports = api;

    if (typeof window !== 'undefined')
        window.chipGameMusic = api;
})(function () {
    'use strict';

    //A bar is four beats of four sixteenths. Everything below is counted in sixteenths.
    const BAR = 16;

    //
    //**How a tune is written.** A part is notes and rests with how many sixteenths each lasts, and `|` between the bars,
    //which is what the bars are checked by: `A4:6 G4:2 A4:4 C5:4 | B4:6 ...`. A note is its letter, a sharp or a flat,
    //and its octave - middle C is C4 - and a rest is a dot. Chords are their names, a bar each: `Am | F | C | G`.
    //
    //**The parts besides the tune are made from the chords**, by how each one plays them - see STYLES: the pad holds
    //each chord for its bar, the bass goes up and down its octave in eighths, and so on. So a tune is written as its
    //chords and its melody, and the rest follows.
    //
    //Drums are a line of sixteenths each, an x where one is hit: the beat every bar, and the fill in the last bar of
    //each section - and a section can have a beat of its own.
    //
    const TRACKS = {
        //
        //**The car's: synthwave**, at a cruising 100 beats a minute in A minor - the arpeggio through an echo, a pad
        //under it, the bass pumping in octaves, and the drums. The verse has no tune over the arpeggio, the chorus has
        //one, and the bridge goes somewhere else before the chorus comes back; then round again.
        //
        car: {
            bpm: 100,
            form: ['A', 'B', 'C', 'B'],
            lead: 'saw',
            parts: [
                { voice: 'pad', style: 'pad', level: 1 },
                { voice: 'bass', style: 'octaves', level: 1 },
                { voice: 'pluck', style: 'arpeggio', level: 1 }
            ],
            kit: 'synth',
            beats: {
                beat: { kick: 'x.......x.......', snare: '....x.......x...', hat: '..x...x...x...x.' },
                drive: { kick: 'x...x...x...x...', snare: '....x.......x...', hat: '..x...x...x...x.' },
                fill: { kick: 'x.......x.......', snare: '....x.....x.xxxx', hat: '..x...x.........' }
            },
            sections: {
                A: {
                    chords: 'Am | F | C | G | Am | F | C | G'
                },
                B: {
                    chords: 'F | G | Am | Am | F | G | E | E',
                    tune: 'A4:6 G4:2 A4:4 C5:4 | B4:6 A4:2 G4:4 D5:4 | E5:8 D5:4 C5:4 | A4:12 .:4 | ' +
                        'A4:6 G4:2 A4:4 C5:4 | D5:6 C5:2 B4:4 G4:4 | G#4:8 B4:4 E5:4 | E5:12 .:4'
                },
                C: {
                    chords: 'Dm | Am | F | C | Dm | Am | F | G',
                    beat: 'drive',
                    tune: 'F5:4 E5:4 D5:4 A4:4 | C5:8 B4:4 A4:4 | A4:4 C5:4 F5:4 E5:4 | E5:12 .:4 | ' +
                        'D5:4 F5:4 A5:4 G5:4 | E5:8 C5:4 A4:4 | C5:4 D5:4 E5:4 F5:4 | G5:8 F5:4 D5:4'
                }
            }
        },

        //
        //**The plane's: bright 8-bit**, at 150 in C major - a lead on a narrow pulse, the chords broken in eighths on
        //another under it, the bass on the triangle, and the drums out of noise: the four voices a game console of the
        //eighties had. A verse, a chorus, and a bridge before the chorus again, the way the car's goes.
        //
        plane: {
            bpm: 150,
            form: ['A', 'B', 'C', 'B'],
            lead: 'pulse25',
            parts: [
                { voice: 'pulse12', style: 'broken', level: 1 },
                { voice: 'triangle', style: 'walk', level: 1 }
            ],
            kit: 'chip',
            beats: {
                beat: { kick: 'x.......x.......', snare: '....x.......x...', hat: 'x.x.x.x.x.x.x.x.' },
                fill: { kick: 'x.......x.......', snare: '....x.......x.xx', hat: 'x.x.x.x.x.x.....' }
            },
            sections: {
                A: {
                    chords: 'C | G | Am | F | C | G | F | G',
                    tune: 'E5:2 G5:2 C6:4 B5:2 G5:2 E5:4 | D5:2 G5:2 B5:4 A5:2 G5:2 D5:4 | ' +
                        'C5:2 E5:2 A5:4 G5:2 E5:2 C5:4 | F5:4 E5:2 F5:2 A5:8 | ' +
                        'G5:2 E5:2 C5:2 E5:2 G5:4 C6:4 | B5:4 A5:2 G5:2 D5:8 | ' +
                        'A5:4 G5:2 F5:2 E5:4 D5:4 | D5:4 G5:4 B4:8'
                },
                B: {
                    chords: 'Am | F | C | G | Am | F | G | G',
                    tune: 'A5:6 G5:2 E5:4 C5:4 | F5:6 E5:2 C5:4 A4:4 | G5:4 E5:2 G5:2 C6:8 | B5:4 A5:4 G5:4 D5:4 | ' +
                        'E5:2 A5:2 C6:4 B5:4 A5:4 | A5:2 F5:2 C5:4 F5:4 A5:4 | G5:4 B5:4 D6:4 B5:4 | G5:8 D5:4 G4:4'
                },
                C: {
                    chords: 'F | G | Em | Am | F | G | C | G',
                    tune: 'A5:6 G5:2 F5:4 C5:4 | B5:6 A5:2 G5:4 D5:4 | G5:4 E5:4 B4:4 E5:4 | C5:4 E5:4 A5:8 | ' +
                        'F5:4 A5:4 C6:4 A5:4 | G5:4 B5:4 D6:4 B5:4 | C6:8 G5:4 E5:4 | D5:8 B4:8'
                }
            }
        },

        //
        //**The jet's: an 8-bit action theme**, at 150 in E minor - a square lead, a fast arpeggio under it, the bass
        //galloping on the triangle, and the kick off the beat. Its chords go down to the major chord on B and back, which
        //is what makes a minor key sound like a chase. A verse, a chorus, a bridge, and the chorus again.
        //
        jet: {
            bpm: 150,
            form: ['A', 'B', 'C', 'B'],
            lead: 'pulse50',
            parts: [
                { voice: 'pulse12', style: 'arpeggio', level: 0.8 },
                { voice: 'triangle', style: 'gallop', level: 1 }
            ],
            kit: 'chip',
            beats: {
                beat: { kick: 'x.....x.x.......', snare: '....x.......x...', hat: 'x.x.x.x.x.x.x.x.' },
                fill: { kick: 'x.....x.x.......', snare: '....x...x.x.xxxx', hat: 'x.x.x.x.........' }
            },
            sections: {
                A: {
                    chords: 'Em | C | D | Em | Em | C | D | B',
                    tune: 'E5:4 B4:2 E5:2 G5:4 F#5:2 E5:2 | G5:4 E5:2 C5:2 E5:8 | ' +
                        'F#5:4 D5:2 F#5:2 A5:4 G5:2 F#5:2 | E5:12 .:4 | ' +
                        'B5:4 A5:2 G5:2 E5:4 G5:4 | C6:4 B5:2 A5:2 G5:4 E5:4 | ' +
                        'D5:4 F#5:4 A5:4 D6:4 | D#6:8 B5:4 F#5:4'
                },
                B: {
                    chords: 'C | D | Em | Em | C | D | B | B',
                    tune: 'G5:6 G5:2 E5:4 G5:4 | A5:6 A5:2 F#5:4 A5:4 | B5:4 G5:4 E5:4 B5:4 | B5:12 .:4 | ' +
                        'C6:6 B5:2 G5:4 E5:4 | D6:6 C6:2 A5:4 F#5:4 | F#5:4 A5:4 B5:4 D#6:4 | B5:8 F#5:4 D#5:4'
                },
                C: {
                    chords: 'Em | Am | C | D | Em | Am | B | B',
                    tune: 'E5:4 G5:4 B5:4 G5:4 | A5:4 C6:4 E6:4 C6:4 | C6:6 B5:2 A5:4 G5:4 | F#5:4 A5:4 D6:8 | ' +
                        'G5:4 B5:4 E6:4 B5:4 | C6:4 A5:4 E5:4 A5:4 | D#6:4 B5:4 F#5:4 B5:4 | D#6:8 F#6:8'
                }
            }
        }
    };

    //Each letter's place in the octave, C first.
    const LETTERS = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 };

    ///
    ///A note's MIDI number - A4 is 69, middle C 60 - from how it is written: `C4`, `F#5`, `Bb3`. NaN for anything else.
    ///
    function pitchOf(written) {
        const found = /^([A-G])([#b]?)(-?\d)$/.exec(written);

        if (found == null)
            return NaN;

        let pitch = LETTERS[found[1]] + ((Number(found[3]) + 1) * 12);

        if (found[2] === '#')
            pitch += 1;
        else if (found[2] === 'b')
            pitch -= 1;

        return pitch;
    }

    ///A MIDI number's frequency, in hertz: A4 is 440, and each semitone a twelfth of an octave.
    function frequencyOf(pitch) {
        return 440 * Math.pow(2, (pitch - 69) / 12);
    }

    ///
    ///A chord from its name - `Am`, `F#`, `Bb`, `E` - as the place of its root in the octave and its three notes above
    ///that root: the third, minor or major, and the fifth. Null for anything else.
    ///
    function chordOf(name) {
        const found = /^([A-G])([#b]?)(m?)$/.exec(name);

        if (found == null)
            return null;

        let root = LETTERS[found[1]];

        if (found[2] === '#')
            root += 1;
        else if (found[2] === 'b')
            root -= 1;

        let third = 4;

        if (found[3] === 'm')
            third = 3;

        return { root: (root + 12) % 12, third: third };
    }

    ///The chord's root in the octave that starts at `from`: the one MIDI number from there up to an octave above it.
    function rootFrom(chord, from) {
        return from + ((chord.root - (from % 12) + 12) % 12);
    }

    //
    //**How each part plays the chords**, a bar at a time: the notes it plays, as [step, length, pitches] - each pitch a
    //MIDI number - for a chord. Where each puts the root is a range an octave wide, so a change of chord is a step to the
    //nearest root rather than a leap.
    //
    const STYLES = {
        //The whole chord, held for the bar, between E3 and the D# above middle C.
        pad(chord) {
            const root = rootFrom(chord, 52);

            return [[0, BAR, [root, root + chord.third, root + 7]]];
        },

        //Eighths on the root, low and an octave up in turn - the synthwave bass's pump.
        octaves(chord) {
            const root = rootFrom(chord, 33);
            const notes = [];

            for (let step = 0; step < BAR; step += 2) {
                let pitch = root;

                if (step % 4 === 2)
                    pitch = root + 12;

                notes.push([step, 2, [pitch]]);
            }

            return notes;
        },

        //Sixteenths up the chord and over the octave - root, fifth, octave, the third above it - and round again.
        arpeggio(chord) {
            const root = rootFrom(chord, 53);
            const climb = [root, root + 7, root + 12, root + 12 + chord.third];
            const notes = [];

            for (let step = 0; step < BAR; step++)
                notes.push([step, 1, [climb[step % climb.length]]]);

            return notes;
        },

        //Eighths up and down the chord - root, third, fifth, third - an 8-bit tune's harmony on the second pulse.
        broken(chord) {
            const root = rootFrom(chord, 53);
            const turn = [root, root + chord.third, root + 7, root + chord.third];
            const notes = [];

            for (let step = 0; step < BAR; step += 2)
                notes.push([step, 2, [turn[(step / 2) % turn.length]]]);

            return notes;
        },

        //Eighths on the triangle between the root and the fifth, the octave between them - a walking 8-bit bass.
        walk(chord) {
            const root = rootFrom(chord, 36);
            const walk = [root, root + 7, root + 12, root + 7];
            const notes = [];

            for (let step = 0; step < BAR; step += 2)
                notes.push([step, 2, [walk[(step / 2) % walk.length]]]);

            return notes;
        },

        //An eighth and two sixteenths on the root, the last an octave up, four times a bar - the gallop under a chase.
        gallop(chord) {
            const root = rootFrom(chord, 35);
            const notes = [];

            for (let step = 0; step < BAR; step += 4) {
                notes.push([step, 2, [root]]);
                notes.push([step + 2, 1, [root]]);
                notes.push([step + 3, 1, [root + 12]]);
            }

            return notes;
        }
    };

    ///
    ///A tune's line, as notes: [step, length, pitch] for each, the step counted from the start of the line. Throws for
    ///a note that is not one, and for a bar that does not come to a bar - with which bar, of which line.
    ///
    function readLine(line, saying) {
        const notes = [];
        const bars = line.split('|');

        let at = 0;

        for (let bar = 0; bar < bars.length; bar++) {
            const start = at;

            for (const token of bars[bar].trim().split(/\s+/)) {
                if (token === '')
                    continue;

                const parts = token.split(':');

                let length = 1;

                if (parts.length > 1)
                    length = Number(parts[1]);

                if (!(Number.isInteger(length) && length > 0))
                    throw new Error(`${saying}, bar ${bar + 1}: "${token}" has no length`);

                if (parts[0] !== '.') {
                    const pitch = pitchOf(parts[0]);

                    if (!Number.isFinite(pitch))
                        throw new Error(`${saying}, bar ${bar + 1}: "${token}" is not a note`);

                    notes.push([at, length, pitch]);
                }

                at += length;
            }

            if (at - start !== BAR)
                throw new Error(`${saying}, bar ${bar + 1}: comes to ${at - start} sixteenths, not ${BAR}`);
        }

        return { notes: notes, bars: bars.length };
    }

    ///
    ///A tune, as what to play on each of its sixteenths, start to end, ready to go round: `steps[i]` is the list of what
    ///starts on the i-th - { voice, pitches, length, level } for a note or a chord, its length in sixteenths, and
    ///{ voice, drum } for a drum. Throws for anything written wrongly - see readLine.
    ///
    function compile(track) {
        const steps = [];

        for (const name of track.form) {
            const section = track.sections[name];
            const chords = section.chords.split('|').map(each => each.trim());
            const from = steps.length;

            for (let bar = 0; bar < chords.length; bar++) {
                const chord = chordOf(chords[bar]);

                if (chord == null)
                    throw new Error(`section ${name}, bar ${bar + 1}: "${chords[bar]}" is not a chord`);

                for (let step = 0; step < BAR; step++)
                    steps.push([]);

                const barAt = from + (bar * BAR);

                for (const part of track.parts) {
                    for (const [step, length, pitches] of STYLES[part.style](chord))
                        steps[barAt + step].push({ voice: part.voice, pitches: pitches, length: length, level: part.level });
                }

                let beat = track.beats[section.beat || 'beat'];

                if (bar === chords.length - 1 && track.beats.fill != null)
                    beat = track.beats.fill;

                for (const drum of Object.keys(beat)) {
                    if (beat[drum].length !== BAR)
                        throw new Error(`section ${name}'s ${drum} is ${beat[drum].length} sixteenths, not ${BAR}`);

                    for (let step = 0; step < BAR; step++) {
                        if (beat[drum][step] === 'x')
                            steps[barAt + step].push({ voice: track.kit, drum: drum });
                    }
                }
            }

            if (section.tune == null)
                continue;

            const tune = readLine(section.tune, `section ${name}'s tune`);

            if (tune.bars !== chords.length)
                throw new Error(`section ${name}'s tune is ${tune.bars} bars, and its chords ${chords.length}`);

            for (const [step, length, pitch] of tune.notes)
                steps[from + step].push({ voice: track.lead, pitches: [pitch], length: length, level: 1, lead: true });
        }

        return { bpm: track.bpm, steps: steps };
    }

    return {
        BAR: BAR,
        TRACKS: TRACKS,
        STYLES: STYLES,
        pitchOf: pitchOf,
        frequencyOf: frequencyOf,
        chordOf: chordOf,
        readLine: readLine,
        compile: compile
    };
});
