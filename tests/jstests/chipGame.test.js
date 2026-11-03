const test = require('node:test');
const assert = require('node:assert');
const game = require('../../wwwroot/js/chipGame.js');

//The chip game's physics: a car driven over the tops of the 3D view's slabs. Pure arithmetic, so it runs
//under Node - the car on screen, the keys and the camera are covered by the Playwright spec in e2e/.

const FRAME = 1 / 60;

const IDLE = { throttle: 0, steer: 0, jump: false, boost: false };
const FORWARD = { throttle: 1, steer: 0, jump: false, boost: false };
const JUMPING = { throttle: 0, steer: 0, jump: true, boost: false };

///A slab with a rectangular footprint.
function slab(x0, y0, x1, y1, bottom, top) {
    return {
        outline: [{ x: x0, y: y0 }, { x: x1, y: y0 }, { x: x1, y: y1 }, { x: x0, y: y1 }],
        holes: [],
        bottom: bottom,
        top: top
    };
}

///A car put down somewhere particular, standing still and on the ground.
function placed(world, x, y, z, heading = 0) {
    const player = game.spawn(world);

    player.x = x;
    player.y = y;
    player.z = z;
    player.heading = heading;
    player.grounded = true;

    return player;
}

///Steps the game for this long with the same input held, and hands back every event that happened.
function run(world, player, input, seconds) {
    const events = [];

    for (let t = 0; t < seconds; t += FRAME)
        events.push(...game.step(world, player, input, FRAME));

    return events;
}

///The highest the car gets over the next while, starting with the input held.
function peak(world, player, input, seconds) {
    let highest = player.z;

    for (let t = 0; t < seconds; t += FRAME) {
        game.step(world, player, input, FRAME);

        highest = Math.max(highest, player.z);
    }

    return highest;
}

//A floor and five wires over it, so the median shape - which the car is sized off - is a wire.
function wiresOverAFloor() {
    const slabs = [slab(0, 0, 6000, 6000, 0, 100)];

    for (let i = 0; i < 5; i++)
        slabs.push(slab(500, 500 + (i * 1000), 5500, 700 + (i * 1000), 900, 1000));

    return slabs;
}

test('a car dropped over a plate comes to rest on its top', () => {
    const world = game.buildWorld([slab(0, 0, 4000, 4000, 0, 100)]);
    const player = game.spawn(world);

    assert.ok(player.z > 100, 'it starts above the plate, to fall in');

    const events = run(world, player, IDLE, 2);

    assert.strictEqual(player.grounded, true);
    assert.strictEqual(player.z, 100);
    assert.ok(events.some(event => event.type === 'landed'));
});

test('the car is sized to fit on a wire, whatever the units', () => {
    const world = game.buildWorld(wiresOverAFloor());

    //The wires are 200 wide: the car has to be narrower than one, and not a speck on it.
    assert.ok(world.unit < 200, `a car ${world.unit} wide does not fit on a 200 wire`);
    assert.ok(world.unit > 50, `a car ${world.unit} wide is a speck on a 200 wire`);

    //And the same layout a thousand times larger gets a car a thousand times larger.
    const scaled = wiresOverAFloor().map(one => slab(
        one.outline[0].x * 1000, one.outline[0].y * 1000, one.outline[2].x * 1000, one.outline[2].y * 1000,
        one.bottom * 1000, one.top * 1000));

    const large = game.buildWorld(scaled);

    assert.ok(Math.abs((large.unit / world.unit) - 1000) < 1e-6);
});

test('it starts on the top layer, facing along the wire', () => {
    const world = game.buildWorld(wiresOverAFloor());
    const player = game.spawn(world);

    run(world, player, IDLE, 2);

    assert.strictEqual(player.z, 1000);
    assert.strictEqual(player.heading, 0, 'the wires run along x');
});

test('the middle of an L is outside it, so the car starts in one of its arms', () => {
    const shape = {
        outline: [{ x: 0, y: 0 }, { x: 3000, y: 0 }, { x: 3000, y: 400 }, { x: 400, y: 400 }, { x: 400, y: 3000 }, { x: 0, y: 3000 }],
        holes: [],
        bottom: 0,
        top: 100
    };

    const world = game.buildWorld([shape]);
    const player = game.spawn(world);

    assert.ok(!game.contains(world.slabs[0], 1500, 1500), 'the box center is not in the shape');
    assert.ok(game.contains(world.slabs[0], player.x, player.y), 'the start is');
});

test('driving off the end of a wire drops the car onto the floor under it', () => {
    const world = game.buildWorld(wiresOverAFloor());
    const player = placed(world, 5000, 600, 1000, 0);

    //
    //At three widths a second rather than seven: under the floatier fall a car off the end at seven sails past the
    //floor's far edge before it comes down. Long enough to come off the end and land - at 2.4 seconds - and not so
    //long that it drives off the floor as well, at 3.3.
    //
    player.topSpeed = 3;

    const events = run(world, player, FORWARD, 2.8);

    assert.ok(player.x > 5500, 'it drove off the end');
    assert.strictEqual(player.grounded, true);
    assert.strictEqual(player.z, 100);
    assert.ok(events.some(event => event.type === 'landed'));
});

test('a held jump rises about the jump height and stays there while it is held, and comes down when let go', () => {
    const world = game.buildWorld(wiresOverAFloor());
    const player = placed(world, 3000, 3000, 100);

    //Three seconds held is several jumps' worth of time, and still one jump - up, and kept at the top.
    const once = peak(world, player, JUMPING, 3) - 100;

    assert.ok(Math.abs(once - world.jumpHeight) < world.jumpHeight * 0.05, `rose ${once} for a jump of ${world.jumpHeight}`);
    assert.ok(Math.abs((player.z - 100) - once) < 1e-9, `and was ${player.z - 100} up when it was let go`);

    run(world, player, IDLE, 2);

    assert.strictEqual(player.z, 100, 'and it came back down');
});

test('held at the top of a jump, the car flies: it keeps its height and drives the way it does on the ground', () => {
    const world = game.buildWorld(wiresOverAFloor());
    const turning = { throttle: 1, steer: 1, jump: false, boost: false };

    const driven = placed(world, 3000, 3000, 100);

    run(world, driven, turning, 1);

    const flown = placed(world, 3000, 3000, 100);

    run(world, flown, JUMPING, 1);

    const height = flown.z;

    //Up at the top, rather than come back down by now - a second is over twice the time to the top.
    assert.ok(height > 100 + (world.jumpHeight * 0.95), `only ${height - 100} up`);

    run(world, flown, { ...turning, jump: true }, 1);

    assert.strictEqual(flown.z, height, 'it kept its height');

    //The same speed and the same turn as on the ground, where the air's share of the steering would have been under half.
    assert.ok(Math.abs(flown.speed - driven.speed) < 1e-9, `flew at ${flown.speed} against ${driven.speed} driven`);
    assert.ok(Math.abs(flown.heading - driven.heading) < 1e-9, `turned to ${flown.heading} against ${driven.heading} driven`);
});

test('every press of the jump jumps again, so pressing over and over climbs a jump at a time', () => {
    const world = game.buildWorld(wiresOverAFloor());
    const player = placed(world, 3000, 3000, 100);

    //Pressed and held to the top of each jump, then let go for a frame so the next press is a press.
    const tops = [player.z];

    for (let press = 0; press < 5; press++) {
        game.step(world, player, JUMPING, FRAME);

        while (player.vz > 0)
            game.step(world, player, JUMPING, FRAME);

        tops.push(player.z);

        game.step(world, player, IDLE, FRAME);
    }

    for (let i = 1; i < tops.length; i++) {
        const gained = tops[i] - tops[i - 1];

        assert.ok(Math.abs(gained - world.jumpHeight) < world.jumpHeight * 0.05, `press ${i} gained ${gained} for a jump of ${world.jumpHeight}`);
    }

    //Five jumps up is well over the wires, which are 900 above the floor.
    assert.ok(player.z > 1000 + world.jumpHeight, `only got to ${player.z}`);
});

test('a press on the way down catches the fall, so a ledge driven off can be climbed back onto', () => {
    const world = game.buildWorld(wiresOverAFloor());
    const player = placed(world, 5000, 600, 1000, 0);

    //Off the end of the wire, and a car's width below it.
    for (let frame = 0; frame < 300 && player.z > 1000 - world.unit; frame++)
        game.step(world, player, FORWARD, FRAME);

    assert.strictEqual(player.grounded, false);
    assert.ok(player.vz < 0, 'it is falling');

    //Pressed until it is back above the wire, then driven back over it and let down.
    player.heading = Math.PI;

    for (let press = 0; press < 3; press++) {
        run(world, player, { throttle: 0, steer: 0, jump: true, boost: false }, 0.3);
        run(world, player, IDLE, FRAME);
    }

    assert.ok(player.z > 1000, `only climbed back to ${player.z}`);

    run(world, player, FORWARD, 2);

    assert.strictEqual(player.grounded, true);
    assert.strictEqual(player.z, 1000, 'back on the wire');
});

test('a tap is a hop and a hold is a leap', () => {
    const world = game.buildWorld(wiresOverAFloor());

    const tapped = placed(world, 3000, 3000, 100);

    run(world, tapped, JUMPING, FRAME * 2);

    const hop = peak(world, tapped, IDLE, 1) - 100;
    const leap = peak(world, placed(world, 3000, 3000, 100), JUMPING, 1) - 100;

    assert.ok(hop < leap * 0.75, `a tap rose ${hop} against ${leap} held`);
});

test('the jump grows with the stack, so spreading the layers does not strand the car', () => {
    const close = game.buildWorld(wiresOverAFloor());

    const spread = wiresOverAFloor();

    for (const one of spread) {
        if (one.top === 1000) {
            one.bottom = 2900;
            one.top = 3000;
        }
    }

    const apart = game.buildWorld(spread);

    assert.ok(apart.jumpHeight > close.jumpHeight);
});

//
//A third of the step and a little more, under half the pull that would stop a climb of all of it at its top in half a
//second: a low jump that floats, and a drop that takes its time. The whole step read as too high, and so did two thirds
//of it and then half.
//
test('a jump is 0.35 of the step, under half the pull of the whole step', () => {
    //One step, from the floor's top at 100 to the wires' at 1000: three quarters of it is 675, and the jump 0.35 of that.
    const tall = game.buildWorld(wiresOverAFloor());

    assert.ok(Math.abs(tall.jumpHeight - 236.25) < 1e-6, `jumped ${tall.jumpHeight}`);

    //What brings a climb of 675 to its top in half a second, a tenth of it the push - twice 675 over a half, over
    //0.4 - and half of that.
    assert.ok(Math.abs(tall.gravity - 3375) < 1e-6, `pulled at ${tall.gravity}`);

    //A step of a hundred, which is under the floor of two and a half cars.
    const flat = wiresOverAFloor();

    for (const one of flat) {
        if (one.top === 1000) {
            one.bottom = 150;
            one.top = 200;
        }
    }

    const low = game.buildWorld(flat);
    const floor = low.unit * 2.5 * 0.35;

    assert.ok(Math.abs(low.jumpHeight - floor) < 1e-6, `jumped ${low.jumpHeight} with a car ${low.unit} wide`);
});

test('a wall stops the car rather than letting it through', () => {
    const world = game.buildWorld([...wiresOverAFloor(), slab(3500, 0, 3600, 6000, 100, 2000)]);
    const radius = game.TUNING.radius * world.unit;
    const player = placed(world, 2500, 3000, 100, 0);

    run(world, player, FORWARD, 3);

    assert.ok(player.x <= 3500 - radius + 1e-6, `got to ${player.x} against a wall at 3500`);
    assert.ok(player.x > 3500 - radius - (world.unit * 0.5), 'and right up against it');
    assert.strictEqual(player.z, 100);
});

test('driving into a wall at an angle slides along it, at the speed of the part of the move along it', () => {
    const world = game.buildWorld([...wiresOverAFloor(), slab(3500, 0, 3600, 6000, 100, 2000)]);
    const player = placed(world, 3400, 2000, 100, Math.PI / 4);

    //Up to speed and pressed against the wall first, then timed along it.
    run(world, player, FORWARD, 1.5);

    const from = player.y;

    run(world, player, FORWARD, 1);

    const along = player.y - from;
    const expected = game.TUNING.topSpeed * world.unit * Math.sin(Math.PI / 4);

    assert.ok(player.x < 3500);
    assert.ok(Math.abs(along - expected) < expected * 0.05, `slid ${along} in a second against ${expected}`);
});

test('driving straight into a wall stops the car', () => {
    const world = game.buildWorld([...wiresOverAFloor(), slab(3500, 0, 3600, 6000, 100, 2000)]);
    const player = placed(world, 2500, 3000, 100, 0);

    run(world, player, FORWARD, 3);

    assert.ok(player.speed < game.TUNING.topSpeed * world.unit * 0.25, `still doing ${player.speed} against the wall`);
});

test('a ledge shorter than a step is driven up rather than hit', () => {
    const plain = game.buildWorld(wiresOverAFloor());
    const ledge = 100 + (game.TUNING.stepUp * plain.unit * 0.5);
    const world = game.buildWorld([...wiresOverAFloor(), slab(3500, 2500, 5000, 3500, 100, ledge)]);
    const player = placed(world, 3000, 3000, 100, 0);

    //Long enough at four widths a second to be well onto the ledge, and not off its far end.
    run(world, player, FORWARD, 2.5);

    assert.ok(player.x > 3600, `it only got to ${player.x}, short of the ledge at 3500`);
    assert.strictEqual(player.z, world.slabs[world.slabs.length - 1].top);
});

test('a hole in a plate is fallen through', () => {
    const plate = slab(0, 0, 4000, 4000, 900, 1000);

    plate.holes = [[{ x: 1500, y: 1500 }, { x: 2500, y: 1500 }, { x: 2500, y: 2500 }, { x: 1500, y: 2500 }]];

    const world = game.buildWorld([slab(0, 0, 4000, 4000, 0, 100), plate]);
    const player = placed(world, 2000, 2000, 1000);

    run(world, player, IDLE, 2);

    assert.strictEqual(player.z, 100);
});

test('the car bumps its roof on a slab over it rather than jumping through', () => {
    const plain = game.buildWorld(wiresOverAFloor());
    const ceiling = 100 + (plain.jumpHeight * 0.5);
    const world = game.buildWorld([...wiresOverAFloor(), slab(2000, 2000, 4000, 4000, ceiling, ceiling + 100)]);
    const player = placed(world, 3000, 3000, 100);

    const highest = peak(world, player, JUMPING, 1);
    const roof = game.TUNING.height * world.unit;

    assert.ok(highest + roof <= ceiling + 1e-6, `the roof reached ${highest + roof} under a ceiling at ${ceiling}`);

    run(world, player, IDLE, 1);

    assert.strictEqual(player.z, 100, 'and it comes back down to where it was');
});

test('falling off the chip puts the car back at the start', () => {
    const world = game.buildWorld(wiresOverAFloor());
    const player = placed(world, 3000, 3000, 100);

    //Out past the edge of everything, in the air.
    player.x = -5000;
    player.grounded = false;

    const events = run(world, player, IDLE, 10);

    assert.ok(events.some(event => event.type === 'fell'));

    //Put back over the start and come down onto the top layer there, with nothing held to move it off.
    assert.strictEqual(player.x, player.start.x);
    assert.strictEqual(player.y, player.start.y);
    assert.strictEqual(player.z, 1000);
});

test('a long frame is taken as a short one, so a tab coming back does not drop the car through the floor', () => {
    const world = game.buildWorld([slab(0, 0, 4000, 4000, 0, 100)]);
    const player = placed(world, 2000, 2000, 400);

    player.grounded = false;

    game.step(world, player, IDLE, 5);

    assert.ok(player.z > 100, 'one step of five seconds did not move it five seconds');
});

test('spreading the stack carries the car up with what it is standing on', () => {
    const slabs = wiresOverAFloor();
    const world = game.buildWorld(slabs);
    const player = placed(world, 3000, 600, 1000);

    run(world, player, IDLE, FRAME * 2);

    const heights = slabs.map(one => {
        if (one.top === 1000)
            return { bottom: 1900, top: 2000 };

        return { bottom: one.bottom, top: one.top };
    });

    game.restack(world, player, heights);

    assert.strictEqual(player.z, 2000);

    run(world, player, IDLE, 1);

    assert.strictEqual(player.z, 2000, 'and it stays there');
    assert.strictEqual(player.grounded, true);
});

test('the line from the car to the camera stops short of the first slab in the way', () => {
    const world = game.buildWorld([slab(0, 0, 4000, 4000, 0, 100), slab(0, 0, 4000, 4000, 500, 600)]);

    //Straight up from the floor, through the plate overhead: clear for the first 400 of 1000.
    const clear = game.sightLine(world, 2000, 2000, 100, 2000, 2000, 1100);

    assert.ok(clear > 0.399 && clear <= 0.4, `clear for ${clear} of the way`);

    //And along under the plate, nothing in the way at all.
    assert.strictEqual(game.sightLine(world, 1000, 2000, 300, 3000, 2000, 300), 1);
});

test('an empty world is no world at all, and stepping it does nothing', () => {
    const world = game.buildWorld([]);
    const player = game.spawn(world);

    assert.strictEqual(world.empty, true);
    assert.deepStrictEqual(game.step(world, player, FORWARD, FRAME), []);
});

///
///Room to drive: a floor wide enough for seconds of it at top speed, with the five wires up over one corner of
///it so the car is still sized off a wire. A plate on its own would get a car a twelfth of the layout across.
///
function openFloor() {
    const slabs = [slab(0, 0, 20000, 20000, 0, 100)];

    for (let i = 0; i < 5; i++)
        slabs.push(slab(500, 500 + (i * 1000), 5500, 700 + (i * 1000), 900, 1000));

    return slabs;
}

///How far the heading turns with the steering held for `held` seconds and then let go, driving forward.
function turnedBy(held) {
    const world = game.buildWorld(openFloor());
    const player = placed(world, 10000, 10000, 100, 0);

    //Up to speed first, so the turn is a moving car's.
    run(world, player, FORWARD, 1.5);

    const from = player.heading;

    run(world, player, { throttle: 1, steer: 1, jump: false, boost: false }, held);
    run(world, player, FORWARD, 0.5);

    return player.heading - from;
}

test('a tap of the steering is a nudge, and a hold still turns at the full rate', () => {
    const tap = turnedBy(0.1);
    const hold = turnedBy(1);

    //It was a sixth of a circle a tap: the full rate from the first frame, 0.28 rad in a tenth of a second.
    assert.ok(tap > 0, 'a tap still turns');
    assert.ok(tap < 0.08, `a tap turned ${tap} rad`);

    //Held, the wheel is round within a third of a second and the rest of the second is a full turn.
    assert.ok(hold > tap * 10, `a second held turned ${hold} against a tap's ${tap}`);
});

test('a jump eases off the ground rather than leaving it at full speed, and still rises its height', () => {
    const world = game.buildWorld(wiresOverAFloor());
    const player = placed(world, 3000, 3000, 100);

    game.step(world, player, JUMPING, FRAME);

    //One frame in, a sixth of the way through the push rather than at a jump's speed already.
    assert.ok(player.vz > 0, 'it has left the ground');
    assert.ok(player.vz < world.jumpSpeed * 0.3, `doing ${player.vz} of ${world.jumpSpeed} after one frame`);

    const rose = peak(world, player, JUMPING, 2) - 100;

    assert.ok(Math.abs(rose - world.jumpHeight) < world.jumpHeight * 0.05, `rose ${rose} for a jump of ${world.jumpHeight}`);
});

test('the top speed is the car\'s own, so the panel can set it', () => {
    const world = game.buildWorld(openFloor());
    const player = placed(world, 2000, 10000, 100, 0);

    player.topSpeed = 14;

    run(world, player, FORWARD, 3);

    assert.ok(Math.abs(player.speed - (14 * world.unit)) < world.unit * 1e-6, `doing ${player.speed} against ${14 * world.unit}`);
});

//A fixed three widths a second backed a car turned down to one forward away three times as fast as it drove.
test('reverse is a share of the top speed, so a slow car backs up slowly too', () => {
    const world = game.buildWorld(openFloor());
    const backing = { throttle: -1, steer: 0, jump: false, boost: false };

    for (const top of [1, 7]) {
        const player = placed(world, 2000, 10000, 100, 0);

        player.topSpeed = top;

        run(world, player, backing, 3);

        const wanted = -game.TUNING.reverseShare * top * world.unit;

        assert.ok(Math.abs(player.speed - wanted) < world.unit * 1e-6, `backing at ${player.speed} against ${wanted} for a top speed of ${top}`);
    }
});

test('a car twice the size is stopped twice as far from a wall', () => {
    const world = game.buildWorld([...wiresOverAFloor(), slab(3500, 0, 3600, 6000, 100, 2000)]);
    const player = placed(world, 2500, 3000, 100, 0);

    player.size = 2;

    run(world, player, FORWARD, 3);

    const radius = game.TUNING.radius * world.unit * 2;

    assert.ok(player.x <= 3500 - radius + 1e-6, `got to ${player.x} against a wall at 3500`);
    assert.ok(player.x > 3500 - radius - (world.unit * 0.5), 'and right up against it');
});

test('with lock on the end of a wire is a guardrail, not a drop', () => {
    const world = game.buildWorld(wiresOverAFloor());
    const player = placed(world, 5000, 600, 1000, 0);

    player.locked = true;

    run(world, player, FORWARD, 1.5);

    assert.ok(player.x <= 5500, `drove off to ${player.x}, past the end at 5500`);
    assert.ok(player.x > 5400, 'and right up to it');
    assert.strictEqual(player.z, 1000);
    assert.strictEqual(player.grounded, true);

    //And a jump is still the way off.
    run(world, player, JUMPING, 0.2);
    run(world, player, FORWARD, 2);

    assert.strictEqual(player.z, 100);
});

///A via on the floor and the metal over it: wider than the via by a hundred either side, as metal is.
function viaUnderMetal() {
    return [
        slab(0, 0, 6000, 6000, 0, 100),
        slab(3500, 2900, 3700, 3100, 100, 600),
        slab(3400, 2800, 3800, 3200, 600, 700)
    ];
}

test('with lock on a via driven into is climbed, out from under the metal over it and onto its top', () => {
    const world = game.buildWorld(viaUnderMetal());
    const player = placed(world, 3000, 3000, 100, 0);

    player.locked = true;

    const radius = game.TUNING.radius * world.unit;
    const roof = game.TUNING.height * world.unit;
    const events = [];

    let deepest = 0;

    for (let t = 0; t < 4; t += FRAME) {
        events.push(...game.step(world, player, FORWARD, FRAME));

        //Up the metal's side rather than through its edge: the metal starts at x 3400, the via under it at 3500.
        if (player.climbing && player.z < 700 && player.z + roof > 600)
            deepest = Math.max(deepest, (player.x + radius) - 3400);
    }

    assert.ok(deepest <= 1e-6, `the car went ${deepest} into the metal on the way up`);
    assert.ok(events.some(event => event.type === 'climb'), 'it started up the via');
    assert.ok(events.some(event => event.type === 'climbed'), 'and got to the top');

    assert.strictEqual(player.z, 700, 'on the metal');
    assert.strictEqual(player.grounded, true);
    assert.ok(game.contains(world.slabs[2], player.x, player.y), 'over it');
});

test('with lock off the same via is a wall', () => {
    const world = game.buildWorld(viaUnderMetal());
    const player = placed(world, 3000, 3000, 100, 0);

    run(world, player, FORWARD, 4);

    assert.strictEqual(player.z, 100);
    assert.ok(player.x < 3500);
});

test('letting go of the throttle on the way up lets go of the wall', () => {
    const world = game.buildWorld(viaUnderMetal());
    const player = placed(world, 3000, 3000, 100, 0);

    player.locked = true;

    for (let frame = 0; frame < 600 && !player.climbing; frame++)
        game.step(world, player, FORWARD, FRAME);

    run(world, player, FORWARD, 0.3);

    assert.ok(player.climbing, 'it is on the wall');
    assert.ok(player.z > 100, 'and part of the way up');

    run(world, player, IDLE, 2);

    assert.strictEqual(player.climbing, false);
    assert.strictEqual(player.z, 100, 'back down on the floor');
});

///How long the car takes to come down from a held jump's top, let go there - and how high that top was.
function jumped(world, player) {
    const from = player.z;

    game.step(world, player, JUMPING, FRAME);

    while (player.vz > 0 || player.lift > 0)
        game.step(world, player, JUMPING, FRAME);

    const height = player.z - from;

    let down = 0;

    while (!player.grounded && down < 10) {
        game.step(world, player, IDLE, FRAME);

        down += FRAME;
    }

    return { height: height, down: down };
}

//
//The camera is a set number of the car's widths behind it, so a car whose speed and jump were the layout's went past
//ten times as fast on screen at a tenth the size, and leapt ten of its own heights.
//
test('a car half the size goes half as fast and jumps half as high, in the same time', () => {
    const world = game.buildWorld(openFloor());
    const big = placed(world, 2000, 10000, 100, 0);
    const small = placed(world, 2000, 12000, 100, 0);

    small.size = 0.5;

    run(world, big, FORWARD, 3);
    run(world, small, FORWARD, 3);

    assert.ok(Math.abs((small.speed / big.speed) - 0.5) < 1e-9, `${small.speed} against ${big.speed}`);

    const bigJump = jumped(world, placed(world, 10000, 10000, 100));
    const smallJump = jumped(world, Object.assign(placed(world, 10000, 12000, 100), { size: 0.5 }));

    assert.ok(Math.abs((smallJump.height / bigJump.height) - 0.5) < 0.02, `rose ${smallJump.height} against ${bigJump.height}`);
    assert.ok(Math.abs(smallJump.down - bigJump.down) < FRAME * 1.5, `came down in ${smallJump.down} s against ${bigJump.down} s`);
});

//The plane, with nothing held: the lever where it was left, the stick in the middle and level flight.
const GLIDING = { throttle: 0, yaw: 0, pitch: 0, roll: 0, level: false, boost: false, jump: false };

///A plane - or the jet - put somewhere particular, flying level at its cruise.
function flying(world, x, y, z, heading = 0, vehicle = 'plane') {
    const player = placed(world, x, y, z, heading);

    player.grounded = false;

    game.board(world, player, vehicle);

    player.pitch = 0;

    return player;
}

test('into the plane, it takes off from where the car was and flies at its cruise', () => {
    const world = game.buildWorld(openFloor());
    const player = placed(world, 10000, 10000, 100);

    game.board(world, player, 'plane');

    const cruise = game.TUNING.plane.cruise * game.planeTop(world, player);

    assert.strictEqual(player.plane, true);
    assert.ok(Math.abs(player.speed - cruise) < 1e-9, `at ${player.speed} for a cruise of ${cruise}`);

    run(world, player, GLIDING, 1);

    assert.strictEqual(player.grounded, false);
    assert.ok(player.z > 100 + world.unit, `only ${player.z - 100} up after a second`);
    assert.ok(player.x > 10000 + (cruise * 0.9), `only got to ${player.x}`);
});

test('the plane\'s lever stays where it is left, and it never flies slower than it can', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 2000, 10000, 3000);
    const top = game.planeTop(world, player);

    run(world, player, { ...GLIDING, throttle: 1 }, 0.5);

    const opened = player.speed;

    assert.ok(opened > game.TUNING.plane.cruise * top, `still at ${player.speed}`);

    run(world, player, GLIDING, 1);

    assert.strictEqual(player.speed, opened, 'let go of, it stayed');

    run(world, player, { ...GLIDING, throttle: -1 }, 5);

    assert.ok(Math.abs(player.speed - (game.TUNING.plane.slowest * top)) < 1e-9, `closed right down, at ${player.speed}`);
});

test('the stick back climbs, forward dives, and in the middle the plane keeps its height', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 2000, 10000, 3000);

    run(world, player, GLIDING, 1);

    assert.ok(Math.abs(player.z - 3000) < 1e-6, `level flight went to ${player.z}`);

    //Three quarters of a second at the stick's 0.8 a second, since it was made slower.
    run(world, player, { ...GLIDING, pitch: 1 }, 0.75);

    const climbing = player.z;

    assert.ok(player.pitch > 0.5, `the nose only came up ${player.pitch}`);
    assert.ok(climbing > 3000, 'and it climbed');

    run(world, player, { ...GLIDING, pitch: -1 }, 1.5);

    assert.ok(player.pitch < 0, 'the nose went down');

    run(world, player, GLIDING, 0.5);

    assert.ok(player.z < climbing, 'and it dived');
});

test('a bank turns the plane, and let go of, the wings come back level by themselves', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 10000, 10000, 3000);

    run(world, player, { ...GLIDING, roll: 1 }, 0.7);

    assert.ok(player.roll > 0.9, `banked only ${player.roll}`);

    const banked = player.heading;

    run(world, player, GLIDING, 0.5);

    assert.ok(player.heading > banked + 0.2, `turned only ${player.heading - banked} in half a second banked`);

    run(world, player, GLIDING, 5);

    assert.ok(Math.abs(player.roll) < 0.05, `still banked ${player.roll}`);
});

///
///The mouse holds the stick at a place, which is a bank and a nose rather than a movement: the plane comes round to them
///and stays there, for longer than the wings would take to come back level by themselves - and the middle is level,
///from wherever the stick was held before. It was a movement put on, and the middle was wherever the game began.
///
test('the mouse holds the stick at a place, and the plane comes round to its bank and nose and stays there', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 10000, 10000, 3000);

    run(world, player, { ...GLIDING, bank: -0.4, nose: 0.3 }, 2);

    assert.ok(Math.abs(player.pitch - 0.3) < 1e-6, `pitched ${player.pitch}`);
    assert.ok(Math.abs(player.roll + 0.4) < 1e-6, `rolled ${player.roll}`);

    run(world, player, { ...GLIDING, bank: 0, nose: 0 }, 1);

    assert.ok(Math.abs(player.pitch) < 1e-6 && Math.abs(player.roll) < 1e-6, `still at ${player.pitch}, ${player.roll}`);
});

test('it comes round over a few frames rather than in one, and the short way', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 10000, 10000, 3000);

    game.step(world, player, { ...GLIDING, bank: 0.5 }, FRAME);

    assert.ok(player.roll > 0 && player.roll < 0.25, `rolled ${player.roll} in a frame`);

    run(world, player, { ...GLIDING, bank: 0.5 }, game.TUNING.stickEase * 6);

    assert.ok(Math.abs(player.roll - 0.5) < 0.01, `rolled ${player.roll}`);

    //From nearly upside down one way to nearly upside down the other is past upside down, not back through level.
    player.roll = -3;

    let nearestLevel = Math.PI;

    for (let t = 0; t < 0.5; t += FRAME) {
        game.step(world, player, { ...GLIDING, bank: 3 }, FRAME);

        nearestLevel = Math.min(nearestLevel, Math.abs(player.roll));
    }

    assert.ok(Math.abs(player.roll - 3) < 0.01, `rolled ${player.roll}`);
    assert.ok(nearestLevel > 2.9, `came within ${nearestLevel} of level`);
});

test('let go of by the mouse, the wings level by themselves and the nose stays - and Space levels both', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 10000, 10000, 3000);

    run(world, player, { ...GLIDING, bank: 0.6, nose: 0.2 }, 1);
    run(world, player, GLIDING, 5);

    assert.ok(Math.abs(player.roll) < 0.05, `still banked ${player.roll}`);
    assert.ok(Math.abs(player.pitch - 0.2) < 1e-6, `the nose went to ${player.pitch}`);

    run(world, player, { ...GLIDING, level: true }, 2);

    assert.ok(Math.abs(player.pitch) < 0.01 && Math.abs(player.roll) < 0.01, `still at ${player.pitch}, ${player.roll}`);
});

///
///The stick held back flies the plane round until its nose has come all the way back to level - or for `seconds`, if it
///never does - and says how far over it went and where it got to. Up high, so nothing is in the way.
///
function looped(vehicle, seconds = 20) {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 10000, 10000, 6000, 0, vehicle);
    const from = { x: player.x, y: player.y, z: player.z };

    let over = 0;
    let highest = player.z;
    let took = 0;

    for (; took < seconds; took += FRAME) {
        const was = player.pitch;

        game.step(world, player, { ...GLIDING, pitch: 1 }, FRAME);

        over = Math.max(over, Math.abs(player.pitch));
        highest = Math.max(highest, player.z);

        if (over > 3 && was < 0 && player.pitch >= 0)
            break;
    }

    return { world: world, player: player, from: from, over: over, highest: highest, took: took };
}

///
///**The stick held back is a loop**: the nose comes up past straight up, the plane goes over the top on its back and
///down the far side, and comes out level where it went in, the way it was going. It stopped at 75 degrees, and hung
///there nose up under the top of a loop it could not finish.
///
test('the stick held back loops the plane, over the top on its back and round to level where it began', () => {
    for (const vehicle of ['plane', 'jet']) {
        const loop = looped(vehicle);
        const player = loop.player;
        const width = game.carWidth(loop.world, player);

        assert.ok(loop.over > 3.1, `${vehicle}: the nose only got to ${loop.over}`);
        assert.ok(loop.took < 10, `${vehicle}: round in ${loop.took} s`);
        assert.ok(loop.highest > loop.from.z + (width * 5), `${vehicle}: only ${(loop.highest - loop.from.z) / width} widths up`);
        assert.ok(Math.hypot(player.x - loop.from.x, player.y - loop.from.y, player.z - loop.from.z) < width * 0.5, `${vehicle}: came out ${(player.x - loop.from.x) / width} widths along`);
        assert.ok(Math.abs(player.heading) < 1e-9 && Math.abs(player.roll) < 1e-9, `${vehicle}: heading ${player.heading}, roll ${player.roll}`);
        assert.ok(Math.abs(player.pitch) < 0.05, `${vehicle}: nose at ${player.pitch}`);
        assert.strictEqual(player.grounded, false);
    }
});

///
///**On its back over the top a bank still turns the plane to its own right**, which is the left of its heading - so the
///turn goes the other way round the compass. The same turn as the right way up would take it to its left.
///
test('over the top a bank turns the plane to its own right, the other way round the compass', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 10000, 10000, 6000);

    player.pitch = Math.PI;

    const right = game.axes(player);
    const from = { x: player.x, y: player.y, heading: player.heading };

    run(world, player, { ...GLIDING, roll: 1 }, 1);

    const across = ((player.x - from.x) * right.rx) + ((player.y - from.y) * right.ry);

    assert.ok(across > 0, `went ${across} to its right`);
    assert.ok(player.heading < from.heading - 0.2, `the heading went from ${from.heading} to ${player.heading}`);
    assert.ok(Math.abs(player.pitch) > Math.PI / 2, 'and it is still over the top, the bank having been held');
});

///
///**Let go of over the top, it is turned the right way up**: the same way of pointing said the other way round, which the
///plane on screen does not show - its nose goes on the way it was - and then its wings come level by themselves, which
///from there is a half roll rather than the rest of the loop flown backwards.
///
test('let go of over the top, the plane goes on the way it was and its wings come level the right way up', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 10000, 10000, 6000);

    while (Math.abs(player.pitch) < 2.6)
        game.step(world, player, { ...GLIDING, pitch: 1 }, FRAME);

    //Said the right way up, it points the same way, nose, wings and all.
    const was = game.axes(player);
    const said = game.axes({ ...player, ...game.attitude(player) });

    for (const axis of ['fx', 'fy', 'fz', 'ux', 'uy', 'uz', 'rx', 'ry', 'rz'])
        assert.ok(Math.abs(said[axis] - was[axis]) < 1e-9, `${axis} went from ${was[axis]} to ${said[axis]}`);

    game.step(world, player, GLIDING, FRAME);

    const now = game.axes(player);

    assert.ok(Math.abs(player.pitch) <= Math.PI / 2, `said with its nose at ${player.pitch}`);
    assert.ok(Math.hypot(now.fx - was.fx, now.fy - was.fy, now.fz - was.fz) < 0.01, 'its nose where it was');

    run(world, player, GLIDING, 6);

    const level = game.axes(player);

    assert.ok(level.uz > 0.8, `still not the right way up, its up at ${level.uz}`);
    assert.ok(Math.abs(player.roll) < 0.05, `banked ${player.roll}`);
});

///
///Space over the top levels the plane the right way up, flying on the way it was going - back over where it began -
///rather than flying the loop backwards, up through straight up and round to the way it went in.
///
test('Space over the top levels the plane the right way up, flying on the way it was going', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 10000, 10000, 6000);

    while (Math.abs(player.pitch) < 2.6)
        game.step(world, player, { ...GLIDING, pitch: 1 }, FRAME);

    run(world, player, { ...GLIDING, level: true }, 3);

    const now = game.axes(player);

    assert.ok(Math.abs(player.pitch) < 0.01 && Math.abs(player.roll) < 0.01, `still at ${player.pitch}, ${player.roll}`);
    assert.ok(now.uz > 0.99, `its up at ${now.uz}`);
    assert.ok(now.fx < -0.5, `going ${now.fx} along x, where it was going back along it`);
});

///
///The mouse holding the nose over the top - the pointer back nearer the middle - rolls the plane the right way up to the
///nose and the bank it holds, rather than pulling the nose back through straight up.
///
test('the mouse holding the nose over the top rolls the plane the right way up rather than flying the loop backwards', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 10000, 10000, 6000);

    while (Math.abs(player.pitch) < 2.6)
        game.step(world, player, { ...GLIDING, pitch: 1 }, FRAME);

    let steepest = 0;

    for (let t = 0; t < 1; t += FRAME) {
        game.step(world, player, { ...GLIDING, bank: 0, nose: 0.1 }, FRAME);

        steepest = Math.max(steepest, game.axes(player).fz);
    }

    const now = game.axes(player);

    assert.ok(Math.abs(player.pitch - 0.1) < 0.01 && Math.abs(player.roll) < 0.01, `at ${player.pitch}, ${player.roll}`);
    assert.ok(now.uz > 0.95 && now.fx < -0.9, `up ${now.uz}, going ${now.fx} along x`);
    assert.ok(steepest < 0.6, `the nose went back up to ${steepest} on the way`);
});

//The mouse holding the wings level and nothing else, which keeps a plane over the top there until something levels it.
const WINGS_HELD = { ...GLIDING, bank: 0 };

///
///Brought down onto a top on its back - the loop's far side, too low - the plane lands the right way up and facing the way
///it was going, where leveling the nose as it was said turned it round to face back the way it came.
///
test('brought down onto a top on its back, the plane lands the right way up and facing the way it was going', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 8000, 10000, 400);

    player.pitch = -2.8;

    const events = run(world, player, WINGS_HELD, 3);
    const now = game.axes(player);

    assert.ok(events.some(event => event.type === 'touchdown'), 'it touched down');
    assert.strictEqual(player.grounded, true);
    assert.strictEqual(player.pitch, 0);
    assert.strictEqual(player.roll, 0);
    assert.ok(now.fx < -0.9, `facing ${now.fx} along x, going back along it`);
});

///A ceiling met on its back puts the nose level and leaves the plane flying on the way it was going.
test('a ceiling met over the top puts the nose level and the plane flying on the way it was going', () => {
    const world = game.buildWorld([...openFloor(), slab(0, 0, 20000, 20000, 3000, 3100)]);
    const player = flying(world, 10000, 10000, 2700);

    player.pitch = 2.6;

    const events = run(world, player, WINGS_HELD, 1);
    const now = game.axes(player);

    assert.ok(events.some(event => event.type === 'bonk'), 'it met the ceiling');
    assert.ok(Math.abs(now.fz) < 1e-9, `its nose at ${now.fz} up`);
    assert.ok(now.fx < -0.9, `going ${now.fx} along x`);
});

test('brought down onto a top, the plane lands and runs along it, and the stick lifts it off again', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 8000, 10000, 600);

    //A gentle dive at its cruise, which is down in under three seconds.
    player.pitch = -0.3;

    const events = run(world, player, GLIDING, 4);

    assert.ok(events.some(event => event.type === 'touchdown'), 'it touched down');
    assert.strictEqual(player.grounded, true);
    assert.strictEqual(player.z, 100);
    assert.strictEqual(player.pitch, 0, 'nose up to level');

    run(world, player, { ...GLIDING, throttle: -1 }, 4);

    assert.strictEqual(player.speed, 0, 'and on the runway it can be stopped');

    run(world, player, { ...GLIDING, throttle: 1, pitch: 1 }, 1);

    assert.strictEqual(player.grounded, false);
    assert.ok(player.z > 100, 'and lifted off again');
});

test('a wall stops the plane rather than letting it through', () => {
    const world = game.buildWorld([...openFloor(), slab(13500, 0, 13600, 20000, 100, 9000)]);
    const player = flying(world, 12000, 10000, 3000);
    const radius = game.TUNING.radius * world.unit;

    run(world, player, GLIDING, 4);

    assert.ok(player.x <= 13500 - radius + 1e-6, `got to ${player.x} against a wall at 13500`);
});

test('back into the car, it drops from where the plane was and lands', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 10000, 10000, 1500);

    game.board(world, player, 'car');

    assert.strictEqual(player.plane, false);
    assert.ok(player.speed <= player.topSpeed * world.unit + 1e-9, `driving off at ${player.speed}`);

    run(world, player, IDLE, 4);

    assert.strictEqual(player.grounded, true);
    assert.strictEqual(player.z, 100);
});

test('the jet flies twice as fast as the plane, and its afterburner takes it past its top', () => {
    const world = game.buildWorld(openFloor());
    const plane = flying(world, 2000, 8000, 3000);
    const jet = flying(world, 2000, 12000, 3000, 0, 'jet');

    assert.strictEqual(jet.jet, true);
    assert.strictEqual(jet.plane, true, 'a jet is flown the way the plane is, by its own numbers');
    assert.ok(Math.abs((game.planeTop(world, jet) / game.planeTop(world, plane)) - 2) < 1e-9);

    const top = game.planeTop(world, jet);

    run(world, jet, { ...GLIDING, throttle: 1 }, 3);

    assert.ok(Math.abs(jet.speed - top) < 1e-6, `flat out at ${jet.speed} for a top of ${top}`);

    run(world, jet, { ...GLIDING, boost: true }, 3);

    assert.ok(Math.abs(jet.speed - (top * game.TUNING.jet.boost)) < 1e-6, `the afterburner at ${jet.speed}`);
});

test('from the plane into the jet it flies on, nose and all', () => {
    const world = game.buildWorld(openFloor());
    const player = flying(world, 10000, 10000, 3000);

    player.pitch = 0.2;

    game.board(world, player, 'jet');

    assert.strictEqual(player.jet, true);
    assert.strictEqual(player.grounded, false);
    assert.strictEqual(player.pitch, 0.2, 'not put back to a take-off');
    assert.ok(player.speed >= (game.TUNING.jet.cruise * game.planeTop(world, player)) - 1e-9, `at ${player.speed}`);

    game.board(world, player, 'plane');

    assert.strictEqual(player.jet, false);
    assert.strictEqual(player.plane, true);
});

test('with Limit jump four presses jump and a fifth does not, nor holds the car up, until the meter is full again', () => {
    const world = game.buildWorld(wiresOverAFloor());
    const player = placed(world, 3000, 3000, 100);

    player.limitJump = true;

    //Four presses close together, so the meter has no time to fill a jump back between them.
    const events = [];

    for (let press = 0; press < 4; press++) {
        events.push(...run(world, player, JUMPING, FRAME * 3));
        events.push(...run(world, player, IDLE, FRAME));
    }

    assert.strictEqual(events.filter(event => event.type === 'jump' || event.type === 'airJump').length, 4);
    assert.strictEqual(player.recharging, true, 'run dry');

    const fifth = run(world, player, JUMPING, FRAME * 3);

    assert.ok(fifth.some(event => event.type === 'noJump'), 'said it came to nothing');
    assert.ok(!fifth.some(event => event.type === 'jump' || event.type === 'airJump'));

    //Held, it is not kept at the top the way a jump is: it comes down.
    run(world, player, JUMPING, 3);

    assert.strictEqual(player.grounded, true);
    assert.strictEqual(player.z, 100);

    //Eight seconds from empty it is full, and says so - and a press is a jump again.
    const waited = run(world, player, IDLE, 8);

    assert.ok(waited.some(event => event.type === 'recharged'));
    assert.strictEqual(player.recharging, false);
    assert.strictEqual(player.jumps, game.TUNING.jumpCharges);
    assert.ok(run(world, player, JUMPING, FRAME * 3).some(event => event.type === 'jump'));
});

test('with Limit jump the meter fills again by itself, a jump every two seconds', () => {
    const world = game.buildWorld(wiresOverAFloor());
    const player = placed(world, 3000, 3000, 100);

    player.limitJump = true;

    run(world, player, JUMPING, FRAME);
    run(world, player, IDLE, FRAME);
    run(world, player, JUMPING, FRAME);

    assert.ok(Math.abs(player.jumps - 2) < 0.05, `left with ${player.jumps}`);
    assert.strictEqual(player.recharging, false, 'two left is not run dry');

    run(world, player, IDLE, 2);

    assert.ok(Math.abs(player.jumps - 3) < 0.05, `filled to ${player.jumps}`);
});

///The same run of numbers every time, for where the electrons are put down.
function seeded(seed) {
    let state = seed >>> 0;

    return function () {
        state = (state + 0x6D2B79F5) >>> 0;

        let mixed = Math.imul(state ^ (state >>> 15), state | 1);

        mixed ^= mixed + Math.imul(mixed ^ (mixed >>> 7), mixed | 61);

        return ((mixed ^ (mixed >>> 14)) >>> 0) / 4294967296;
    };
}

///With no new electrons put down, so the ones a test puts down are the only ones there are.
function alone(body) {
    const count = game.TUNING.electronCount;

    game.TUNING.electronCount = 0;

    try {
        body();
    }
    finally {
        game.TUNING.electronCount = count;
    }
}

///An electron floating with its middle at height z.
function electronAt(world, x, y, z) {
    return { x: x, y: y, top: z - (game.TUNING.electronHover * world.unit), phase: 0, gone: false };
}

test('electrons are put down round the car, over tops it can get to, as many as there are to be', () => {
    const world = game.buildWorld(openFloor());
    const player = placed(world, 5000, 3000, 100);

    world.random = seeded(7);

    run(world, player, IDLE, 2);

    assert.strictEqual(world.electrons.length, game.TUNING.electronCount);

    const pace = game.paceOf(world, player);

    for (const one of world.electrons) {
        const distance = Math.hypot(one.x - player.x, one.y - player.y);

        assert.ok(distance >= (game.TUNING.electronNear * pace) - 1e-6, `one only ${distance} off`);
        assert.ok(distance <= (game.TUNING.electronFar * pace) + 1e-6, `one ${distance} off`);
        assert.ok(one.top === 100 || one.top === 1000, `over ${one.top}, which is neither the floor nor a wire`);
        assert.ok(game.groundBelow(world, one.x, one.y, one.top) === one.top, 'and over that top');
    }
});

test('an electron left far behind is taken away, for another to be put down', () => {
    const world = game.buildWorld(openFloor());
    const player = placed(world, 10000, 10000, 100);
    const far = game.TUNING.electronFar * game.paceOf(world, player);
    const behind = electronAt(world, 10000 - (far * 1.6), 10000, 150);

    world.electrons.push(behind);

    run(world, player, IDLE, FRAME);

    assert.strictEqual(behind.gone, true);
    assert.ok(!world.electrons.includes(behind));
});

test('the car picks up an electron it drives into, for points, and not one beside it', () => {
    alone(() => {
        const world = game.buildWorld(openFloor());
        const player = placed(world, 10000, 10000, 100, 0);
        const ahead = { x: 10000 + (world.unit * 3), y: 10000, top: 100, phase: 0, gone: false };
        const beside = { x: 10000 + (world.unit * 3), y: 10000 + (world.unit * 2), top: 100, phase: 0, gone: false };

        world.electrons.push(ahead, beside);

        const events = run(world, player, FORWARD, 2);

        assert.strictEqual(ahead.gone, true);
        assert.strictEqual(beside.gone, false);
        assert.strictEqual(player.score, game.TUNING.electronPoints);
        assert.ok(events.some(event => event.type === 'pickedUp' && event.points === game.TUNING.electronPoints));
    });
});

test('the plane flies through an electron rather than picking it up - it is there to be shot', () => {
    alone(() => {
        const world = game.buildWorld(openFloor());
        const player = flying(world, 10000, 10000, 3000);
        const one = electronAt(world, 10000 + (world.unit * 3), 10000, 3000 + (world.unit * 0.3));

        world.electrons.push(one);

        run(world, player, GLIDING, 2);

        assert.ok(player.x > one.x, 'it flew past it');
        assert.strictEqual(one.gone, false);
        assert.strictEqual(player.score, 0);
    });
});

test('the plane\'s gun shoots down an electron ahead of it, and not one behind a wall', () => {
    alone(() => {
        const world = game.buildWorld([...openFloor(), slab(13500, 0, 13600, 20000, 100, 9000)]);
        const player = flying(world, 10000, 10000, 3000);
        const gun = 3000 + (world.unit * 0.3);
        const ahead = electronAt(world, 10000 + (world.unit * 8), 10000, gun);
        const walledOff = electronAt(world, 13800, 10000, gun);

        world.electrons.push(ahead, walledOff);

        const events = run(world, player, { ...GLIDING, fire: true }, 1.5);

        assert.strictEqual(ahead.gone, true, 'shot down');
        assert.strictEqual(walledOff.gone, false, 'the wall took the rest');
        assert.strictEqual(player.score, game.TUNING.electronPoints);
        assert.ok(events.some(event => event.type === 'fired' && event.kind === 'bullet'));
        assert.ok(events.some(event => event.type === 'shotDown'));

        //A dozen rounds a second, give or take the one the first frame fires.
        const fired = events.filter(event => event.type === 'fired').length;

        assert.ok(Math.abs(fired - (1.5 * game.TUNING.gunRate)) <= 2, `fired ${fired} in a second and a half`);
    });
});

test('the jet\'s missile goes after the electron nearest its nose, and blows up the one beside it with it', () => {
    alone(() => {
        const world = game.buildWorld(openFloor());
        const player = flying(world, 5000, 10000, 3000, 0, 'jet');
        const width = world.unit;
        const nose = 3000 + (width * 0.3);

        //A tenth of a radian off the nose, twenty-five car widths out - and another a car width further round.
        const target = electronAt(world, 5000 + (Math.cos(0.1) * width * 25), 10000 + (Math.sin(0.1) * width * 25), nose);
        const beside = electronAt(world, target.x, target.y + width, nose);

        world.electrons.push(beside, target);

        assert.strictEqual(game.aim(world, player), target, 'the one nearer the line of the nose');

        const events = run(world, player, { ...GLIDING, missile: true }, FRAME);

        events.push(...run(world, player, GLIDING, 3));

        assert.strictEqual(events.filter(event => event.type === 'fired' && event.kind === 'missile').length, 1);
        assert.strictEqual(target.gone, true);
        assert.strictEqual(beside.gone, true, 'taken by the blast');
        assert.strictEqual(player.score, game.TUNING.electronPoints * 2);
        assert.ok(events.some(event => event.type === 'blast'));
    });
});

test('nothing is aimed at outside the missile\'s cone', () => {
    alone(() => {
        const world = game.buildWorld(openFloor());
        const player = flying(world, 5000, 10000, 3000, 0, 'jet');
        const width = world.unit;

        world.electrons.push(electronAt(world, 5000 + (width * 20), 10000 + (width * 20), 3000 + (width * 0.3)));

        assert.strictEqual(game.aim(world, player), null);
    });
});

///What a run fired, by kind.
function firedOf(events, kind) {
    return events.filter(event => event.type === 'fired' && event.kind === kind);
}

///
///The jet has the plane's gun and its missiles both, each on a trigger of its own - the mouse's buttons, left and right -
///and each at its own rate, so holding the two together waits on neither. The missiles were its only weapon, on the one
///trigger the plane's gun has.
///
test('the jet fires its gun on one trigger and its missiles on the other, each at its own rate', () => {
    alone(() => {
        const world = game.buildWorld(openFloor());
        const player = flying(world, 2000, 10000, 3000, 0, 'jet');

        let events = run(world, player, { ...GLIDING, fire: true }, 1.5);

        assert.ok(Math.abs(firedOf(events, 'bullet').length - (1.5 * game.TUNING.gunRate)) <= 2, `${firedOf(events, 'bullet').length} rounds`);
        assert.strictEqual(firedOf(events, 'missile').length, 0, 'the gun\'s trigger fires no missile');

        events = run(world, player, { ...GLIDING, missile: true }, 1.5);

        const missiles = firedOf(events, 'missile');

        //At once, then every missileReload: at zero, 0.6 and 1.2 seconds.
        assert.strictEqual(missiles.length, 3, `${missiles.length} missiles`);
        assert.strictEqual(firedOf(events, 'bullet').length, 0, 'the missiles\' trigger fires no round');

        //From under one wing and then the other, either side of the line it flies along.
        assert.ok((missiles[0].y - 10000) * (missiles[1].y - 10000) < 0, `from ${missiles[0].y} and ${missiles[1].y}`);

        events = run(world, player, { ...GLIDING, fire: true, missile: true }, 1.25);

        assert.ok(firedOf(events, 'bullet').length >= 14, `${firedOf(events, 'bullet').length} rounds with both held`);
        assert.ok(firedOf(events, 'missile').length >= 2, `${firedOf(events, 'missile').length} missiles with both held`);
    });
});

test('the plane has no missiles, so its other trigger fires nothing', () => {
    alone(() => {
        const world = game.buildWorld(openFloor());
        const player = flying(world, 2000, 10000, 3000);

        assert.strictEqual(run(world, player, { ...GLIDING, missile: true }, 1).filter(event => event.type === 'fired').length, 0);
        assert.strictEqual(player.missiles.heat, 0, 'nor warms them');
    });
});

///What a run said of `type` about a weapon of `kind`.
function saidOf(events, type, kind) {
    return events.filter(event => event.type === type && event.kind === kind);
}

///
///**Three seconds of fire overheats a weapon** - thirty-six rounds at sixty frames a second, the last as the three
///seconds run out - and it fires nothing more until it is cold again: the whole five seconds of cooling, its trigger
///held all the while. Then it fires on.
///
test('three seconds of fire overheats the gun, and it fires nothing more until it has cooled all the way', () => {
    alone(() => {
        const world = game.buildWorld(openFloor());
        const player = flying(world, 2000, 10000, 3000, 0, 'jet');
        const burst = game.TUNING.heatBurst;

        let events = run(world, player, { ...GLIDING, fire: true }, burst - 0.1);

        assert.strictEqual(player.gun.hot, false, 'not yet');
        assert.ok(Math.abs(player.gun.heat - ((burst - 0.1) / burst)) < 0.01, `${player.gun.heat} hot`);

        events = events.concat(run(world, player, { ...GLIDING, fire: true }, 0.2));

        assert.strictEqual(player.gun.hot, true);
        assert.strictEqual(firedOf(events, 'bullet').length, burst * game.TUNING.gunRate);
        assert.strictEqual(saidOf(events, 'overheated', 'bullet').length, 1);

        events = run(world, player, { ...GLIDING, fire: true }, game.TUNING.heatCool - 0.2);

        assert.strictEqual(firedOf(events, 'bullet').length, 0, 'nothing while it cools');
        assert.strictEqual(player.gun.hot, true);

        events = run(world, player, { ...GLIDING, fire: true }, 0.4);

        assert.strictEqual(player.gun.hot, false);
        assert.strictEqual(saidOf(events, 'cooled', 'bullet').length, 1);
        assert.ok(firedOf(events, 'bullet').length >= 1, 'firing again');
    });
});

///
///The missiles overheat the same way, after five - and **a press of their trigger while they cool fires nothing**, and
///is said, a press at a time. The gun has a heat of its own, so it fires on.
///
test('five missiles overheat the missiles, and a press while they cool fires nothing and says so', () => {
    alone(() => {
        const world = game.buildWorld(openFloor());
        const player = flying(world, 2000, 10000, 3000, 0, 'jet');

        let events = run(world, player, { ...GLIDING, missile: true }, game.TUNING.heatBurst + 0.1);

        assert.strictEqual(firedOf(events, 'missile').length, Math.round(game.TUNING.heatBurst / game.TUNING.missileReload));
        assert.strictEqual(player.missiles.hot, true);
        assert.strictEqual(saidOf(events, 'overheated', 'missile').length, 1);

        run(world, player, GLIDING, 0.5);

        events = run(world, player, { ...GLIDING, missile: true }, 0.3);
        events = events.concat(run(world, player, GLIDING, 0.3), run(world, player, { ...GLIDING, missile: true }, 0.3));

        assert.strictEqual(firedOf(events, 'missile').length, 0);
        assert.strictEqual(saidOf(events, 'noShot', 'missile').length, 2);

        events = run(world, player, { ...GLIDING, fire: true }, 0.5);

        assert.ok(firedOf(events, 'bullet').length >= 5, `${firedOf(events, 'bullet').length} rounds`);
        assert.strictEqual(player.gun.hot, false);
    });
});

///
///**A press for each missile is no way round the heat**: a shot's reload is firing too, so a frame's press as each reload
///runs out overheats them after the same five that holding the trigger does - and the presses for the seconds after
///it, with the missiles still cooling, fire none.
///
test('a press for each missile, as fast as they come, overheats them after the same five', () => {
    alone(() => {
        const world = game.buildWorld(openFloor());
        const player = flying(world, 2000, 10000, 3000, 0, 'jet');
        const every = Math.round(game.TUNING.missileReload / FRAME);
        const events = [];

        for (let frame = 0; frame < 6 / FRAME; frame++)
            events.push(...game.step(world, player, { ...GLIDING, missile: frame % every === 0 }, FRAME));

        assert.strictEqual(firedOf(events, 'missile').length, 5);
        assert.strictEqual(saidOf(events, 'overheated', 'missile').length, 1);
    });
});

///
///**A weapon cools whenever it is not firing**, so a burst of a second and a half with twice as long between them never
///overheats it: it is cold again before the next.
///
test('a weapon cools whenever it is not firing, so bursts with long enough between them go on', () => {
    alone(() => {
        const world = game.buildWorld(openFloor());
        const player = flying(world, 2000, 10000, 3000, 0, 'jet');

        let events = [];

        for (let burst = 0; burst < 5; burst++)
            events = events.concat(run(world, player, { ...GLIDING, fire: true }, 1.5), run(world, player, GLIDING, 3));

        assert.strictEqual(events.filter(event => event.type === 'overheated').length, 0);
        assert.ok(firedOf(events, 'bullet').length >= (5 * 1.5 * game.TUNING.gunRate) - 5, `${firedOf(events, 'bullet').length} rounds`);
        assert.strictEqual(player.gun.heat, 0);
    });
});

///
///**A weapon goes on cooling in the car**, so one overheated in the air and left there is cold when the plane is boarded
///again, rather than as hot as it was left.
///
test('a gun overheated in the air goes on cooling in the car', () => {
    alone(() => {
        const world = game.buildWorld(openFloor());
        const player = flying(world, 2000, 10000, 3000, 0, 'jet');

        run(world, player, { ...GLIDING, fire: true }, game.TUNING.heatBurst + 0.1);

        assert.strictEqual(player.gun.hot, true);

        game.board(world, player, 'car');

        run(world, player, IDLE, game.TUNING.heatCool);

        assert.strictEqual(player.gun.hot, false);
        assert.strictEqual(player.gun.heat, 0);

        game.board(world, player, 'jet');

        assert.ok(firedOf(run(world, player, { ...GLIDING, fire: true }, 0.2), 'bullet').length >= 2);
    });
});

test('spreading the stack takes the electrons away, to be put down again over the tops where they are now', () => {
    const slabs = wiresOverAFloor();
    const world = game.buildWorld(slabs);
    const player = placed(world, 3000, 600, 1000);
    const one = electronAt(world, 3000, 3000, 150);

    world.electrons.push(one);

    game.restack(world, player, slabs.map(each => ({ bottom: each.bottom, top: each.top })));

    assert.strictEqual(world.electrons.length, 0);
    assert.strictEqual(one.gone, true);
});
