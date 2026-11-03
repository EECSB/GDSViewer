//The physics of the chip game: a little car driven over the tops of the layers in the 3D view, jumping
//from one to the next - or a plane or a jet flown over them - and the electrons the car picks up and the
//plane and the jet shoot down. No DOM and no three.js, so it can be unit-tested under Node (see tests/jstests/).
//Exposed as window.chipGame in the browser and as module.exports under Node.
//
//**Everything here is in layout coordinates**, which are not the scene's: x and y are the layout's own
//plane and z is up the process stack, the same X, Y and Z the 3D view's axis indicator letters. The scene
//puts the layout's y along its Z and the stack up its Y - see LAYOUT_ROTATION_X in ThreeInterop.js - and
//the interop does that one swap when it places the car.
(function (factory) {
    const api = factory();

    if (typeof module !== 'undefined' && module.exports)
        module.exports = api;

    if (typeof window !== 'undefined')
        window.chipGame = api;
})(function () {
    'use strict';

    //
    //**The world is not in game units, so nothing below is either.** A sky130 cell is a few thousand
    //database units across and a die is millions, and what a unit is worth depends on the file - so a speed
    //in units per second would be a crawl on one layout and a teleport on the next. Every distance here is
    //in car widths instead, and the car is sized off the layout it is driving on (see measureUnit). Times
    //are in seconds.
    //
    //**The car's own widths**, whatever size the panel has made it - see carWidth. A car half the size goes half
    //as far a second and jumps half as high, which on screen, where the camera is a set number of its widths
    //behind it, is the same game in a world twice the size.
    //
    const TUNING = {
        //Driving. Four widths a second, where it starts: the game's panel has a slider for it - see player.topSpeed.
        //It started at seven, which read as too fast once the speed went with the car's size and the car got smaller.
        topSpeed: 4,
        boost: 1.75,

        //
        //**Reverse is a share of the top speed**, a little over a width a second at the four the car starts at.
        //It was three widths a second whatever the panel said, so with the top speed turned down the car backed up
        //faster than it drove forward - at the slider's least, six times as fast.
        //
        reverseShare: 0.3,

        acceleration: 12,
        braking: 26,
        rolling: 5,

        //Radians a second at speed. Below about two widths a second the car turns more slowly, but never
        //not at all - a car that cannot turn while stopped against a wall is stuck there.
        turnRate: 2.8,
        turnInPlace: 0.35,

        //
        //**The wheel is turned, not flicked.** Seconds from straight ahead to full lock, and back again. A key
        //held turned the car at the full rate from its first frame, so a tap was a sixth of a circle and a car
        //on a wire could not be put straight; brought round over a third of a second, a tap is a nudge and a
        //hold is still a full turn. It comes back faster than it goes, the way a wheel let go of does.
        //
        steerIn: 0.35,
        steerOut: 0.15,

        //How much of the turn is gone at top speed, so a fast car holds its line rather than twitching off it.
        fastSteer: 0.35,

        //How much of the throttle and the steering still works in the air - rising or falling, that is. Held at the
        //top of a jump the car is flying, and has all of both; see climb.
        airControl: 0.45,

        //The car as the world sees it: a circle this many widths across the middle, standing this tall.
        radius: 0.5,
        height: 0.7,

        //A ledge this much higher than the wheels is driven up rather than hit, and a dip this much lower is
        //followed rather than fallen off.
        stepUp: 0.3,
        snapDown: 0.2,

        //A move into a wall that gets less than this much of the way is a crash rather than a slide.
        crash: 0.25,

        //Seconds to the top of a climb the height of the stack's step, which is what gravity is worked out from -
        //so everything up and down takes the same time however tall the stack is. See applyJump.
        apexTime: 0.5,

        //
        //**How much of that step a jump climbs: about a third.** The whole step read as too high, and so did two thirds
        //of it and then half.
        //
        jumpShare: 0.35,

        //
        //**And gravity is half what would stop a climb of the whole step at its top in apexTime**, so a jump floats
        //and a drop takes its time - asked for, once a lower jump under the full pull came and went in a blink. A drop
        //of five car widths takes about half a second where it took 0.38.
        //
        gravityShare: 0.5,

        //
        //**A push rather than a kick.** The first part of the climb to the top is the car being brought up to
        //a jump's speed at one steady rate, rather than handed it in a single frame - so it eases off the ground
        //instead of leaving it as a spike. It counts towards apexTime; see applyJump.
        //
        liftTime: 0.1,

        //
        //Heavier on the way down, but only once the fall has got going: the pull grows from gravity at the top
        //to this many times it as the car falls towards the step's speed. The top of a jump is round, and the drop
        //picks up speed into it rather than starting at it.
        //
        fallGravity: 1.35,
        releaseGravity: 2,
        terminalFall: 3,

        //
        //Lock on: how fast the car climbs a wall it is driven into, in widths a second - and how near head on
        //it has to be, as the share of the move a slide along the wall would still get. Further round than that
        //is driving along the wall, which slides, rather than into it.
        //
        climbSpeed: 3.5,
        climbWithin: 0.5,

        //
        //**The plane and the jet** - see fly. `speed` is the top speed, times the car's: a chip is hundreds of cars
        //across, and a plane at a car's pace takes a minute over it. It never flies slower than `slowest` of that, and
        //starts at `cruise` of it. **The throttle is a lever**: W and S move it, `throttle` of the top speed a second,
        //and it stays where it was left - which is what lets a plane be flown with the hands on the stick. `boost` is
        //Shift, past the top: the plane's, and the jet's afterburner.
        //
        //Radians a second: `yaw` the rudder, `pitch` the stick back and forward, `roll` the stick to the side - and
        //`bankTurn` how fast a bank turns it, at a right angle of it.
        //
        //**The stick a quarter slower than it was**, 1.1 and 2.2: it was asked to be less sensitive, the mouse above
        //all - see GAME_PLANE_PITCH_PER_PIXEL in ThreeInterop.js. **The jet twice as fast**, slower to spool and
        //quicker to roll, and it cannot fly as slowly.
        //
        plane: {
            speed: 2.5,
            slowest: 0.3,
            cruise: 0.6,
            throttle: 0.6,
            yaw: 0.9,
            pitch: 0.8,
            roll: 1.6,
            bankTurn: 1.1,
            boost: 1.75
        },

        jet: {
            speed: 5,
            slowest: 0.35,
            cruise: 0.6,
            throttle: 0.45,
            yaw: 0.8,
            pitch: 0.9,
            roll: 1.9,
            bankTurn: 1.25,
            boost: 1.8
        },

        //
        //**The nose goes all the way round**, so the stick held back is a loop - see fly. It went no steeper than 1.3 either
        //way, to keep the stick a climb or a dive, and a plane with the stick held back hung nose up under the top of a loop
        //it could not finish. **A bank turns it at its full rate with the nose up to planeTurnsFully** either way, and less
        //from there to straight up, where a bank has no way left to turn it; over the top it turns it the other way round
        //the compass, which is still to the plane's own right.
        //
        planeTurnsFully: 1.3,

        //
        //**The wings come back level by themselves**, planeLevelAfter seconds after the stick was last moved to the
        //side, at planeLevel a second - so a bank is a turn for as long as it is held, rather than one that goes on
        //turning until somebody works out which way is up. Space levels the wings and the nose at levelOut a second.
        //
        //**Not while the mouse holds the stick**, which it does at a place rather than by moving it: the bank and the
        //nose it asks for, which the plane comes round to over stickEase seconds rather than in the frame - see fly.
        //
        planeLevel: 1,
        planeLevelAfter: 0.6,
        levelOut: 3,
        stickEase: 0.06,

        //How far the nose is up when the car takes off in the plane, in radians, so it climbs away from what it was on.
        planeClimbOut: 0.25,

        //
        //**Limit jump** - see charge. A meter of jumpCharges jumps, one gone with every press, filling again by itself
        //at a jump every jumpRecharge over jumpCharges seconds. **Run dry, it has to fill all the way** - jumpRecharge
        //seconds from empty - before the car jumps again, or holds the top of one.
        //
        jumpCharges: 4,
        jumpRecharge: 8,

        //
        //**The electrons** - see scatter. As many as electronCount about the car at once, put down between electronNear
        //and electronFar of it and taken away past half as far again. **Both in seconds at the top speed**, so there is
        //as much to aim for in the jet as in the car however fast either is set to go. Each floats electronHover over a
        //top - the middle of the car - and is electronRadius across the middle, in car widths, and worth electronPoints.
        //electronTries is how many places a frame are tried for the next one, which keeps the cost of a refill spread.
        //
        electronCount: 30,
        electronNear: 1.5,
        electronFar: 10,
        electronHover: 0.45,
        electronRadius: 0.3,
        electronPoints: 10,
        electronTries: 4,

        //
        //**The gun**, the plane's and the jet's - see fire. gunRate rounds a second, from the nose, each at gunSpeed
        //seconds of the top speed a second on top of its own and gone after gunLife seconds - so it reaches six tenths of
        //the way out to the furthest electron. A round passing within gunReach car widths of an electron's edge hits it.
        //
        gunRate: 12,
        gunSpeed: 5,
        gunLife: 1.2,
        gunReach: 0.3,

        //
        //**The jet's missiles**, on a trigger of their own - see launch. One every missileReload seconds, up to
        //missileSpeed times the jet's top speed in missileBoost seconds, turning at missileTurn radians a second after the
        //electron nearest the nose within missileCone of it - see aim - and gone after missileLife. One blows up what is
        //within missileBlast car widths.
        //
        missileReload: 0.6,
        missileSpeed: 2.5,
        missileBoost: 0.5,
        missileTurn: 3,
        missileCone: 0.35,
        missileLife: 4,
        missileBlast: 2.5,

        //
        //**The heat** - see warm. The gun and the missiles have one each, from cold at zero to overheated at one. It
        //rises while the weapon fires - its trigger held, or the last shot's reload still running - **so heatBurst seconds
        //of fire from cold overheats it**: thirty-six rounds at sixty frames a second, or five missiles. It falls
        //whenever the weapon is not firing, from overheated to cold in heatCool seconds. **Overheated, it fires nothing
        //until it is cold again**, the whole of heatCool, the way the jump meter run dry waits to be full - see charge.
        //
        //The burst was six seconds at first, and halved so that a weapon overheats sooner; the cooling stayed five.
        //
        heatBurst: 3,
        heatCool: 5,

        //How far below the lowest thing in the layout the car falls before it is put back at the start. Far
        //enough to see it go, near enough that the fall is not a wait.
        respawnDepth: 12,

        //A frame longer than this is treated as this long. A tab that was in the background comes back with
        //a delta of seconds, and integrating that in one step drops the car through the floor.
        longestStep: 0.05
    };

    //The grid the slabs are bucketed into is never larger than this a side.
    const GRID_CELLS = 200;

    //The car is never smaller than this fraction of the layout, nor larger than this one.
    const SMALLEST_CAR = 1 / 300;
    const LARGEST_CAR = 1 / 12;

    //A car this many times as wide as half a typical wire fits on one with room either side.
    const CAR_PER_WIRE = 0.55;

    ///
    ///One slab, made ready to be asked about.
    ///
    ///Each ring - the outline and every hole - is flattened into one array of x, y pairs, because a point
    ///inside test walks every edge of every ring and that walk is the inner loop of the whole game.
    ///
    function prepareSlab(slab) {
        const rings = [];

        let minX = Infinity;
        let minY = Infinity;
        let maxX = -Infinity;
        let maxY = -Infinity;

        let area = 0;
        let perimeter = 0;

        const all = [slab.outline].concat(slab.holes || []);

        for (let r = 0; r < all.length; r++) {
            const points = all[r];

            if (points == null || points.length < 3)
                continue;

            const flat = new Float64Array(points.length * 2);
            let ringArea = 0;

            for (let i = 0; i < points.length; i++) {
                const here = points[i];
                const next = points[(i + 1) % points.length];

                flat[i * 2] = here.x;
                flat[(i * 2) + 1] = here.y;

                ringArea += (here.x * next.y) - (next.x * here.y);
                perimeter += Math.hypot(next.x - here.x, next.y - here.y);

                if (here.x < minX)
                    minX = here.x;

                if (here.x > maxX)
                    maxX = here.x;

                if (here.y < minY)
                    minY = here.y;

                if (here.y > maxY)
                    maxY = here.y;
            }

            //The outline adds and a hole takes away, whichever way round either was wound.
            if (r === 0)
                area += Math.abs(ringArea) / 2;
            else
                area -= Math.abs(ringArea) / 2;

            rings.push(flat);
        }

        return {
            rings: rings,
            minX: minX,
            minY: minY,
            maxX: maxX,
            maxY: maxY,
            area: Math.max(area, 0),
            perimeter: perimeter,
            bottom: slab.bottom,
            top: slab.top
        };
    }

    ///
    ///Whether a point is inside the slab's footprint, holes taken out.
    ///
    ///Even-odd across every ring at once, which is what makes a hole a hole without any case of its own: a
    ///ray from a point in a hole crosses the hole's edge and the outline's, an even number.
    ///
    function contains(slab, x, y) {
        if (x < slab.minX || x > slab.maxX || y < slab.minY || y > slab.maxY)
            return false;

        let inside = false;

        for (const ring of slab.rings) {
            const count = ring.length / 2;

            for (let i = 0, j = count - 1; i < count; j = i++) {
                const xi = ring[i * 2];
                const yi = ring[(i * 2) + 1];
                const xj = ring[j * 2];
                const yj = ring[(j * 2) + 1];

                if ((yi > y) !== (yj > y) && x < (((xj - xi) * (y - yi)) / (yj - yi)) + xi)
                    inside = !inside;
            }
        }

        return inside;
    }

    ///How far a point is from the nearest edge of the slab, inside or out.
    function edgeDistance(slab, x, y) {
        let best = Infinity;

        for (const ring of slab.rings) {
            const count = ring.length / 2;

            for (let i = 0, j = count - 1; i < count; j = i++) {
                const ax = ring[j * 2];
                const ay = ring[(j * 2) + 1];
                const bx = ring[i * 2];
                const by = ring[(i * 2) + 1];

                const dx = bx - ax;
                const dy = by - ay;
                const length = (dx * dx) + (dy * dy);

                let t = 0;

                if (length > 0)
                    t = Math.max(0, Math.min(1, (((x - ax) * dx) + ((y - ay) * dy)) / length));

                const distance = Math.hypot(x - (ax + (t * dx)), y - (ay + (t * dy)));

                if (distance < best)
                    best = distance;
            }
        }

        return best;
    }

    ///
    ///How far the car's middle is from the slab's edge: positive outside it, negative inside.
    ///
    ///A circle of radius r overlaps the slab exactly when this is under r, which is the whole of the wall
    ///test - and because it is a distance rather than a yes or no, a move that takes the car *out* of
    ///something it is already pressed into can be told apart from one that takes it further in.
    ///
    ///Anything further than `reach` from the slab's box comes back as `reach` without walking an edge, which
    ///is every slab the grid hands back that is merely nearby.
    ///
    function clearance(slab, x, y, reach) {
        const outsideX = Math.max(slab.minX - x, 0, x - slab.maxX);
        const outsideY = Math.max(slab.minY - y, 0, y - slab.maxY);

        if (Math.hypot(outsideX, outsideY) >= reach)
            return reach;

        const distance = edgeDistance(slab, x, y);

        if (contains(slab, x, y))
            return -distance;

        return distance;
    }

    ///The middle value, or NaN for none.
    function median(values) {
        if (values.length === 0)
            return NaN;

        const sorted = values.slice().sort((a, b) => a - b);

        return sorted[Math.floor(sorted.length / 2)];
    }

    ///
    ///How wide the car is, in layout units.
    ///
    ///**Off the wires, so it fits on one.** Twice a shape's area over its perimeter is its width when it is
    ///long and thin - which a wire is, and which a merged run of them still is, however many corners it
    ///turns. A square comes out at half its side. The median of that across the layout is a typical wire,
    ///and the car is a little over half of one so it can drive along it with room to spare.
    ///
    ///Held between a three-hundredth and a twelfth of the layout, so a file of one huge plate or of dust
    ///still gets a car you can see.
    ///
    function measureUnit(slabs, extent) {
        const widths = [];

        for (const slab of slabs) {
            if (slab.perimeter > 0 && slab.area > 0)
                widths.push((2 * slab.area) / slab.perimeter);
        }

        let unit = median(widths) * CAR_PER_WIRE;

        if (!(unit > 0))
            unit = extent / 40;

        return Math.min(Math.max(unit, extent * SMALLEST_CAR), extent * LARGEST_CAR);
    }

    ///
    ///How far the stack steps up, in layout units - the one height everything up and down is measured by.
    ///
    ///**Off the stack, so a jump is a share of a layer.** The tops of the slabs, sorted, step up the process one
    ///layer at a time; three quarters of a big step is the step, a jump climbs TUNING.jumpShare of it, and pressing
    ///again in the air climbs the rest - every press is another jump, so a stack of any height can be climbed a
    ///press at a time. Recomputed whenever the stack is spread, since the Distance slider is exactly a change in
    ///how far apart those steps are.
    ///
    ///This was the jump itself, until two and a half car widths - the floor here, and what most layouts came to -
    ///read as too high. Gravity is still worked out from it, so a fall is what it was; see applyJump.
    ///
    function measureStep(slabs, unit) {
        const tops = [];

        for (const slab of slabs)
            tops.push(slab.top);

        tops.sort((a, b) => a - b);

        const steps = [];

        for (let i = 1; i < tops.length; i++) {
            const step = tops[i] - tops[i - 1];

            //The same height twice is one layer drawn in two shapes, not a step.
            if (step > unit * 0.5)
                steps.push(step);
        }

        let height = unit * 3;

        if (steps.length > 0) {
            steps.sort((a, b) => a - b);

            height = steps[Math.floor(steps.length * 0.75)] * 0.75;
        }

        return Math.min(Math.max(height, unit * 2.5), unit * 14);
    }

    ///
    ///Gravity, the fall and the jump, all from the stack's step and the time to the top of it.
    ///
    ///**The push is part of the climb.** Brought from nothing to a speed v over liftTime, the car rises v times
    ///liftTime over two; then it coasts up against gravity g for v over g seconds, rising v squared over 2g.
    ///
    ///**Gravity is the step's.** A climb of the whole step to its top in apexTime is the speed twice the step over
    ///apexTime - the two rises together are v times apexTime over two - and what stops that speed in the time after
    ///the push is the full pull; gravity is TUNING.gravityShare of it. That speed is also the fall's measure: the
    ///pull grows towards it, and a fall is held to terminalFall times it.
    ///
    ///**The jump is TUNING.jumpShare of the step, under that gravity**: the two rises set to its height and solved
    ///for v. The two shares are set apart, so the jump's height and how floaty it is can each be had on its own.
    ///
    ///**All of it for the layout's car.** A car the panel has made another size has every one of them times its size -
    ///see lofted - so it jumps as many of its own widths, in the same time.
    ///
    function applyJump(world) {
        const step = measureStep(world.slabs, world.unit);
        const lift = TUNING.liftTime;

        world.fallSpeed = (2 * step) / TUNING.apexTime;
        world.gravity = (TUNING.gravityShare * world.fallSpeed) / (TUNING.apexTime - lift);

        //v times lift over two, plus v squared over 2g, is the height: the positive root.
        world.jumpHeight = step * TUNING.jumpShare;
        world.jumpSpeed = (world.gravity / 2) * (Math.sqrt((lift * lift) + ((8 * world.jumpHeight) / world.gravity)) - lift);

        let lowest = Infinity;

        for (const slab of world.slabs) {
            if (slab.bottom < lowest)
                lowest = slab.bottom;
        }

        world.lowest = lowest;
    }

    ///
    ///Builds the world the car drives in, from the slabs the 3D view drew.
    ///
    ///`slabs` is one entry per drawn shape, in any order: its outline and holes as arrays of {x, y}, and the
    ///heights of its underside and its top. The order is kept, so restack can be handed new heights by index.
    ///
    ///**Bucketed into a grid**, so the car asks only about what is near it. A merged layout is still tens of
    ///thousands of shapes, and testing every one of them against the car several times a frame is the
    ///difference between a game and a slideshow.
    ///
    function buildWorld(slabs) {
        const prepared = [];

        let minX = Infinity;
        let minY = Infinity;
        let maxX = -Infinity;
        let maxY = -Infinity;

        for (const slab of slabs) {
            const one = prepareSlab(slab);

            //Kept even when it is empty, so an index still means the same slab it meant to the caller.
            prepared.push(one);

            if (one.rings.length === 0)
                continue;

            minX = Math.min(minX, one.minX);
            minY = Math.min(minY, one.minY);
            maxX = Math.max(maxX, one.maxX);
            maxY = Math.max(maxY, one.maxY);
        }

        const world = {
            slabs: prepared,
            minX: minX,
            minY: minY,
            maxX: maxX,
            maxY: maxY,
            empty: !(maxX >= minX),
            unit: 1,
            jumpHeight: 1,
            gravity: 1,
            jumpSpeed: 1,
            fallSpeed: 1,
            lowest: 0,
            cellSize: 1,
            columns: 0,
            rows: 0,
            cells: [],
            stamp: new Uint32Array(prepared.length),
            query: 0,
            found: [],

            //What is in the world besides the layout: the electrons, and what has been fired at them - see scatter and
            //fire. And where a place for an electron is picked from, which a test can make the same every time.
            electrons: [],
            shots: [],
            random: Math.random
        };

        if (world.empty)
            return world;

        const extent = Math.max(maxX - minX, maxY - minY, 1);

        world.unit = measureUnit(prepared, extent);

        applyJump(world);

        world.cellSize = Math.max(world.unit * 4, extent / GRID_CELLS);
        world.columns = Math.floor((maxX - minX) / world.cellSize) + 1;
        world.rows = Math.floor((maxY - minY) / world.cellSize) + 1;
        world.cells = new Array(world.columns * world.rows);

        for (let index = 0; index < prepared.length; index++) {
            const slab = prepared[index];

            if (slab.rings.length === 0)
                continue;

            const fromColumn = Math.floor((slab.minX - minX) / world.cellSize);
            const toColumn = Math.floor((slab.maxX - minX) / world.cellSize);
            const fromRow = Math.floor((slab.minY - minY) / world.cellSize);
            const toRow = Math.floor((slab.maxY - minY) / world.cellSize);

            for (let row = fromRow; row <= toRow; row++) {
                for (let column = fromColumn; column <= toColumn; column++) {
                    const at = (row * world.columns) + column;

                    if (world.cells[at] == null)
                        world.cells[at] = [];

                    world.cells[at].push(index);
                }
            }
        }

        return world;
    }

    ///
    ///The slabs whose boxes come within `reach` of a point, into world.found.
    ///
    ///A slab spanning several cells is in each of them, so each is stamped with the query it was last found
    ///by and counted once. The array is the world's own and reused, because this runs several times a frame
    ///and a fresh one each time is garbage sixty times a second.
    ///
    function near(world, x, y, reach) {
        const found = world.found;

        found.length = 0;

        if (world.empty)
            return found;

        world.query++;

        //Wrapped round after four billion queries, which would otherwise read as every slab already found.
        if (world.query === 0xFFFFFFFF) {
            world.stamp.fill(0);
            world.query = 1;
        }

        const fromColumn = Math.max(0, Math.floor((x - reach - world.minX) / world.cellSize));
        const toColumn = Math.min(world.columns - 1, Math.floor((x + reach - world.minX) / world.cellSize));
        const fromRow = Math.max(0, Math.floor((y - reach - world.minY) / world.cellSize));
        const toRow = Math.min(world.rows - 1, Math.floor((y + reach - world.minY) / world.cellSize));

        for (let row = fromRow; row <= toRow; row++) {
            for (let column = fromColumn; column <= toColumn; column++) {
                const cell = world.cells[(row * world.columns) + column];

                if (cell == null)
                    continue;

                for (const index of cell) {
                    if (world.stamp[index] === world.query)
                        continue;

                    world.stamp[index] = world.query;
                    found.push(index);
                }
            }
        }

        return found;
    }

    ///
    ///The highest top under a point that is no higher than `limit`, or -Infinity when there is none.
    ///
    ///The slab it belongs to is left in world.under, for the caller that wants to know what it is standing
    ///on rather than only how high.
    ///
    function groundBelow(world, x, y, limit) {
        let best = -Infinity;

        world.under = -1;

        for (const index of near(world, x, y, 0)) {
            const slab = world.slabs[index];

            if (slab.top > limit || slab.top <= best)
                continue;

            if (!contains(slab, x, y))
                continue;

            best = slab.top;
            world.under = index;
        }

        return best;
    }

    ///The lowest underside over the car's footprint that is no lower than `from`, or Infinity.
    function ceilingAbove(world, x, y, from, radius) {
        let best = Infinity;

        for (const index of near(world, x, y, radius)) {
            const slab = world.slabs[index];

            if (slab.bottom < from || slab.bottom >= best)
                continue;

            if (clearance(slab, x, y, radius) >= radius)
                continue;

            best = slab.bottom;
        }

        return best;
    }

    ///Whether a point is inside the solid of any slab - strictly, so a point on a top face is not.
    function insideSolid(world, x, y, z) {
        for (const index of near(world, x, y, 0)) {
            const slab = world.slabs[index];

            if (z > slab.bottom && z < slab.top && contains(slab, x, y))
                return true;
        }

        return false;
    }

    ///
    ///How much of the way from a to b is clear of every slab, as a fraction from 0 to 1.
    ///
    ///**For the camera, which would otherwise sit inside the layout.** It flies a few car widths behind and
    ///above the car, and as soon as the car drops under a layer that is inside the layer over it - the whole
    ///view one slab's color. Walking out from the car to where the camera wants to be and stopping short of
    ///the first solid is what keeps the car in sight. Sampled every quarter of a car, which is finer than any
    ///layer is thin.
    ///
    function sightLine(world, ax, ay, az, bx, by, bz) {
        const length = Math.hypot(bx - ax, by - ay, bz - az);

        if (world.empty || !(length > 0))
            return 1;

        const steps = Math.min(200, Math.ceil(length / (world.unit * 0.25)));

        for (let i = 1; i <= steps; i++) {
            const t = i / steps;

            if (!insideSolid(world, ax + ((bx - ax) * t), ay + ((by - ay) * t), az + ((bz - az) * t)))
                continue;

            //
            //**And then halved down onto the face**, so the answer moves smoothly as the car does. Left at the
            //sample before, the camera came in and went out in quarter-car jumps as the slab's edge passed from
            //one sample to the next.
            //
            let free = (i - 1) / steps;
            let solid = t;

            for (let half = 0; half < 6; half++) {
                const middle = (free + solid) / 2;

                if (insideSolid(world, ax + ((bx - ax) * middle), ay + ((by - ay) * middle), az + ((bz - az) * middle)))
                    solid = middle;
                else
                    free = middle;
            }

            return free;
        }

        return 1;
    }

    ///
    ///How wide the car is, in layout units: the layout's car width, times the size the game's panel set.
    ///
    ///**Everything the car does goes with it.** Its footprint, its height and the ledge it can drive up, and its
    ///speeds, its jump and the pull on it too - see drive, pull and climb, where the world's jump, gravity and fall,
    ///which are the layout's car's, are each taken times the size. They were the layout's for everything, so a larger
    ///car on the same chip was not also a faster one; but the camera is a set number of the car's widths behind it, so
    ///a car a tenth the size went past at ten times the speed on screen, and leapt ten of its own heights.
    ///
    function carWidth(world, player) {
        return world.unit * player.size;
    }

    ///
    ///Whether a wall stands between the car's middle where it is and (x, y).
    ///
    ///A wall is any slab whose side the car's footprint would overlap between its wheels and its roof - with
    ///anything shorter than a step left out, since that is driven up instead.
    ///
    ///**Deeper is refused, out is not.** The car can end up pressed into a wall without having driven into
    ///it: falling past a ledge it was hanging over, or the stack being spread under it. Refusing every move
    ///that still overlaps would leave it stuck there; refusing only the moves that overlap *more* lets it
    ///back out of anything, and still never lets it through.
    ///
    function walled(world, player, x, y) {
        const width = carWidth(world, player);
        const radius = TUNING.radius * width;
        const feet = player.z + (TUNING.stepUp * width);
        const roof = player.z + (TUNING.height * width);

        for (const index of near(world, x, y, radius)) {
            const slab = world.slabs[index];

            if (slab.top <= feet || slab.bottom >= roof)
                continue;

            const after = clearance(slab, x, y, radius);

            if (after >= radius)
                continue;

            const before = clearance(slab, player.x, player.y, radius);

            if (after < before - (world.unit * 1e-6))
                return true;
        }

        return false;
    }

    ///
    ///Lock on: whether (x, y) is off the edge of what the car is driving on - nothing under it there within a
    ///step up or a dip down. The edge is a guardrail rather than a drop.
    ///
    ///Only on the ground. In the air there is nothing to be kept on, and a jump is still the way off a wire. Never for
    ///the plane, which taxis off an edge and flies.
    ///
    function railed(world, player, x, y) {
        if (!player.locked || !player.grounded || player.plane)
            return false;

        const width = carWidth(world, player);
        const ground = groundBelow(world, x, y, player.z + (TUNING.stepUp * width));

        return ground < player.z - (TUNING.snapDown * width);
    }

    ///Whether the car may move its middle from where it is to (x, y): no wall in the way, and no rail.
    function blocked(world, player, x, y) {
        if (railed(world, player, x, y))
            return true;

        return walled(world, player, x, y);
    }

    ///
    ///Whether anything is inside the car's body at (x, y): its footprint, from its wheels to its roof.
    ///
    ///Wheels and all, unlike a wall, which leaves out what is short enough to drive up - this is for a car on
    ///the side of a wall, which is standing on nothing.
    ///
    function overlaps(world, player, x, y) {
        const width = carWidth(world, player);
        const radius = TUNING.radius * width;
        const roof = player.z + (TUNING.height * width);

        for (const index of near(world, x, y, radius)) {
            const slab = world.slabs[index];

            if (slab.top <= player.z || slab.bottom >= roof)
                continue;

            if (clearance(slab, x, y, radius) < radius - (world.unit * 1e-6))
                return true;
        }

        return false;
    }

    ///
    ///A little way ahead of the car, as (dx, dy): far enough to reach a wall the last step of a drive stopped
    ///short of, which is a quarter of the smaller of the two widths - see travel.
    ///
    function ahead(world, player) {
        const reach = Math.min(world.unit, carWidth(world, player)) * 0.3;

        return { x: Math.cos(player.heading) * reach, y: Math.sin(player.heading) * reach, reach: reach };
    }

    ///
    ///Whether the car is up against a wall it is facing into rather than along.
    ///
    ///Into is a slide along the wall getting less than climbWithin of the way - within thirty degrees of head on.
    ///Further round than that the car runs along the wall the way it always has.
    ///
    function intoWall(world, player) {
        const step = ahead(world, player);

        if (!walled(world, player, player.x + step.x, player.y + step.y))
            return false;

        let along = 0;

        if (!walled(world, player, player.x + step.x, player.y))
            along = Math.abs(step.x);

        if (!walled(world, player, player.x, player.y + step.y))
            along = Math.max(along, Math.abs(step.y));

        return along < step.reach * TUNING.climbWithin;
    }

    ///
    ///Backs the car off the wall it is climbing until nothing is inside it - a tenth of its width at a time, and
    ///no more than two widths. False when that was not enough.
    ///
    ///**What makes a via climbable.** The metal over a via is wider than the via, so the car climbing the via's
    ///side comes up under the metal's edge; backed out from under it, it is on the metal's side, and climbs that.
    ///
    function backOff(world, player) {
        const width = carWidth(world, player);
        const dx = -Math.cos(player.heading) * width * 0.1;
        const dy = -Math.sin(player.heading) * width * 0.1;

        for (let i = 0; i < 20; i++) {
            if (!overlaps(world, player, player.x, player.y))
                return true;

            player.x += dx;
            player.y += dy;
        }

        return !overlaps(world, player, player.x, player.y);
    }

    ///
    ///Lock on, one step up the wall the car is driving into: up at the climbing speed, out from under anything
    ///that puts in the way, and over the top once nothing is ahead at the car's height.
    ///
    ///**Over the top is driving onto it.** Clear ahead means the car's wheels are within a step of the top, so it
    ///goes forward until its middle is over the top and is put down on it. Nothing to put it down on - a wall that
    ///was thinner than the car - and it lets go and falls, as it does when it cannot be backed out from under
    ///something.
    ///
    function scale(world, player, dt) {
        const width = carWidth(world, player);

        player.grounded = false;
        player.standing = -1;
        player.speed = 0;
        player.vz = 0;
        player.lift = 0;
        player.z += TUNING.climbSpeed * width * dt;

        if (!backOff(world, player)) {
            player.climbing = false;

            return;
        }

        const step = ahead(world, player);

        if (walled(world, player, player.x + step.x, player.y + step.y))
            return;

        for (let i = 0; i < 12; i++) {
            if (walled(world, player, player.x + step.x, player.y + step.y))
                break;

            player.x += step.x;
            player.y += step.y;

            const ground = groundBelow(world, player.x, player.y, player.z + (TUNING.stepUp * width));

            if (ground < player.z - (TUNING.snapDown * width))
                continue;

            player.z = ground;
            player.grounded = true;
            player.standing = world.under;
            player.climbing = false;
            player.airTime = 0;

            player.events.push({ type: 'climbed', standing: player.standing });

            return;
        }

        player.climbing = false;
    }

    ///
    ///Moves the car's middle by (dx, dy), sliding along a wall rather than stopping dead at one, and says how
    ///far it actually went.
    ///
    ///The whole move first, then each axis alone. Layouts are almost all right angles, so the axis that is
    ///left after a wall takes the other is the one running along it - which is what a slide is.
    ///
    function moveBy(world, player, dx, dy) {
        if (!blocked(world, player, player.x + dx, player.y + dy)) {
            player.x += dx;
            player.y += dy;

            return Math.hypot(dx, dy);
        }

        if (dx !== 0 && !blocked(world, player, player.x + dx, player.y)) {
            player.x += dx;

            return Math.abs(dx);
        }

        if (dy !== 0 && !blocked(world, player, player.x, player.y + dy)) {
            player.y += dy;

            return Math.abs(dy);
        }

        return 0;
    }

    ///
    ///Where the car starts: in the widest part of the largest shape on the top layer, a little above it.
    ///
    ///**The widest part rather than the middle**, because the middle of an L or a U is outside it. A grid of
    ///points over the shape's box, kept if inside, and the one furthest from any edge wins - which on a wire
    ///is its center line, and on a plate is its middle.
    ///
    ///Facing along the longer side of the shape, which on a wire is down its length.
    ///
    function spawnPoint(world) {
        let highest = -Infinity;

        for (const slab of world.slabs) {
            if (slab.rings.length > 0 && slab.top > highest)
                highest = slab.top;
        }

        let chosen = null;

        for (const slab of world.slabs) {
            if (slab.rings.length === 0 || slab.top < highest - (world.unit * 0.5))
                continue;

            if (chosen == null || slab.area > chosen.area)
                chosen = slab;
        }

        if (chosen == null)
            return { x: 0, y: 0, z: 0, heading: 0 };

        const samples = 16;

        let bestX = (chosen.minX + chosen.maxX) / 2;
        let bestY = (chosen.minY + chosen.maxY) / 2;
        let bestRoom = -Infinity;

        for (let i = 0; i <= samples; i++) {
            for (let j = 0; j <= samples; j++) {
                const x = chosen.minX + (((chosen.maxX - chosen.minX) * i) / samples);
                const y = chosen.minY + (((chosen.maxY - chosen.minY) * j) / samples);

                if (!contains(chosen, x, y))
                    continue;

                const room = edgeDistance(chosen, x, y);

                if (room > bestRoom) {
                    bestRoom = room;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        let heading = 0;

        if (chosen.maxY - chosen.minY > chosen.maxX - chosen.minX)
            heading = Math.PI / 2;

        return { x: bestX, y: bestY, z: chosen.top + (world.unit * 3), heading: heading };
    }

    ///
    ///A car at the start, falling in.
    ///
    ///The plane: whether this is one - the jet is one too, flown with its own handling - its nose and its bank in
    ///radians, and how long since the stick was last moved to the side; see fly. Then its gun, and the jet's missiles:
    ///each a weapon, with how long until its next shot can go and how hot it is - see warm - and which side the next
    ///goes from; see fire and launch. The score is what the electrons have come to.
    ///
    ///The last four are what the game's panel sets: the top speed in widths a second, the size as a multiple of the
    ///layout's car, whether lock on is on - see railed and scale - and whether the jumps are limited, with the meter
    ///that limits them; see charge.
    ///
    function spawn(world) {
        const player = {
            x: 0,
            y: 0,
            z: 0,
            heading: 0,
            speed: 0,
            vz: 0,
            steer: 0,
            lift: 0,
            grounded: false,
            hovering: false,
            climbing: false,
            standing: -1,
            jumpHeld: false,
            airTime: 0,
            start: spawnPoint(world),
            events: [],
            plane: false,
            jet: false,
            pitch: 0,
            roll: 0,
            rolled: 0,
            gun: weapon(),
            side: 1,
            missiles: weapon(),
            wing: 1,
            score: 0,
            topSpeed: TUNING.topSpeed,
            size: 1,
            locked: false,
            limitJump: false,
            jumps: TUNING.jumpCharges,
            recharging: false
        };

        respawn(world, player);

        return player;
    }

    ///
    ///A weapon - the gun or the missiles - cold and ready: how long until its next shot can go, how hot it is, from
    ///zero to one, whether it has overheated, and whether its trigger was held the step before; see warm.
    ///
    function weapon() {
        return { wait: 0, heat: 0, hot: false, held: false };
    }

    ///Back to the start, stopped - or for the plane, level and flying at its cruise.
    function respawn(world, player) {
        player.x = player.start.x;
        player.y = player.start.y;
        player.z = player.start.z;
        player.heading = player.start.heading;
        player.speed = 0;
        player.vz = 0;
        player.steer = 0;
        player.lift = 0;
        player.grounded = false;
        player.climbing = false;
        player.standing = -1;
        player.airTime = 0;
        player.pitch = 0;
        player.roll = 0;
        player.rolled = 0;

        if (player.plane)
            player.speed = craftOf(player).cruise * planeTop(world, player);
    }

    ///
    ///New heights for every slab, by index - the Distance slider moving the stack under the car.
    ///
    ///**The car goes with what it is standing on**, or spreading the stack would leave it in the air over a
    ///layer that has just risen through it. In the air it is left where it is and lands on whatever is there.
    ///
    ///**The electrons go**, and are put down again by the next step: each floats over a top that has just moved,
    ///and would be left hanging where it was, or inside the layer that rose through it.
    ///
    function restack(world, player, heights) {
        let standingWas = NaN;

        if (player != null && player.grounded && player.standing >= 0)
            standingWas = world.slabs[player.standing].top;

        for (let i = 0; i < world.slabs.length && i < heights.length; i++) {
            world.slabs[i].bottom = heights[i].bottom;
            world.slabs[i].top = heights[i].top;
        }

        for (const one of world.electrons)
            one.gone = true;

        world.electrons.length = 0;

        applyJump(world);

        if (player == null)
            return;

        //A wall being climbed has just moved, and the car on its side would be left wherever it was. It lets go.
        player.climbing = false;

        if (!isNaN(standingWas))
            player.z += world.slabs[player.standing].top - standingWas;

        player.start = spawnPoint(world);
    }

    function approach(value, target, by) {
        if (value < target)
            return Math.min(value + by, target);

        return Math.max(value - by, target);
    }

    ///
    ///The throttle and the steering, for one step.
    ///
    ///`input.throttle` and `input.steer` run from -1 to 1, steer positive turning the heading counterclockwise
    ///in the layout's own plane. Which way that is on screen is the view's business - see ThreeInterop.js,
    ///where the scene's handedness is.
    ///
    ///**The steering is where the keys want the wheel, not where it is.** player.steer is brought round to it
    ///over steerIn, and back over steerOut - see TUNING.
    ///
    ///Every speed in the car's own widths - see carWidth.
    ///
    function drive(world, player, input, dt) {
        const unit = carWidth(world, player);

        //On the ground, or held up at the top of a jump - which drives the way the ground does.
        const footed = player.grounded || player.hovering;

        let control = 1;

        if (!footed)
            control = TUNING.airControl;

        let direction = 1;

        if (player.speed < -unit * 0.01)
            direction = -1;

        const wanted = input.steer || 0;

        //Turned towards the keys at one rate, and back towards straight ahead - or through it - at the other.
        let rate = 1 / TUNING.steerOut;

        if (wanted !== 0 && player.steer * wanted >= 0 && Math.abs(wanted) > Math.abs(player.steer))
            rate = 1 / TUNING.steerIn;

        player.steer = approach(player.steer, wanted, rate * dt);

        const grip = Math.min(1, (Math.abs(player.speed) / (2 * unit)) + TUNING.turnInPlace);
        const fast = Math.min(1, Math.abs(player.speed) / (player.topSpeed * unit));
        const steady = 1 - (TUNING.fastSteer * fast);

        player.heading += player.steer * TUNING.turnRate * steady * grip * control * direction * dt;

        let top = player.topSpeed * unit;

        if (input.boost)
            top *= TUNING.boost;

        const reverse = TUNING.reverseShare * player.topSpeed * unit;
        const throttle = input.throttle || 0;

        if (throttle > 0) {
            if (player.speed < 0)
                player.speed = approach(player.speed, 0, TUNING.braking * unit * dt);
            else if (player.speed < top)
                player.speed = Math.min(player.speed + (TUNING.acceleration * unit * control * throttle * dt), top);
            else
                player.speed = approach(player.speed, top, TUNING.rolling * unit * dt);
        }
        else if (throttle < 0) {
            if (player.speed > 0)
                player.speed = approach(player.speed, 0, TUNING.braking * unit * dt);
            else
                player.speed = Math.max(player.speed + (TUNING.acceleration * unit * control * throttle * dt), -reverse);
        }
        else if (footed)
            player.speed = approach(player.speed, 0, TUNING.rolling * unit * dt);
    }

    ///
    ///Along the heading, in steps short enough that nothing thin can be driven through.
    ///
    ///A quarter of the car a step. The car is a little over half a typical wire, so a step is about an eighth
    ///of one - and a wall thinner than that is not something a layout has. A car made smaller than the layout's
    ///takes a quarter of itself, and a larger one a quarter of the layout's.
    ///
    function travel(world, player, dt) {
        const distance = player.speed * dt;

        if (distance === 0)
            return;

        const stride = Math.min(world.unit, carWidth(world, player)) * 0.25;
        const steps = Math.min(40, Math.max(1, Math.ceil(Math.abs(distance) / stride)));
        const dx = (Math.cos(player.heading) * distance) / steps;
        const dy = (Math.sin(player.heading) * distance) / steps;

        let moved = 0;

        for (let i = 0; i < steps; i++)
            moved += moveBy(world, player, dx, dy);

        //
        //**Head on, the car stops. At an angle, it keeps its speed and slides.**
        //
        //The slide already costs what it should: only the part of the move along the wall happens, so a car
        //pressed into one at 45 degrees runs along it at 70% of its speed. Scrubbing the speed on top of that
        //every frame it touched took it to a crawl within a few frames, which is not what scraping along a
        //wall feels like. What is left to catch is driving straight into one - within about fifteen degrees,
        //where so little of the move happens that keeping the speed would be keeping a number nothing shows.
        //
        if (moved < Math.abs(distance) * TUNING.crash)
            player.speed *= moved / Math.abs(distance);
    }

    ///
    ///The pull on the car for a step, by which way it is going.
    ///
    ///Heavier on the way down than up, growing with the fall rather than all at once - see fallGravity - and
    ///heavier on the way up once the jump is let go, which is what makes a tap a hop and a hold a leap.
    ///
    ///The world's gravity and fall times the car's size, as everything is - see carWidth.
    ///
    function pull(world, player, input) {
        const gravity = world.gravity * player.size;

        if (player.vz <= 0) {
            const falling = Math.min(1, -player.vz / (world.fallSpeed * player.size));

            return gravity * (1 + ((TUNING.fallGravity - 1) * falling));
        }

        if (!input.jump)
            return gravity * TUNING.releaseGravity;

        return gravity;
    }

    ///
    ///Up and down: following the ground, leaving it, jumping, falling and landing.
    ///
    ///**Every press of the jump is a jump, on the ground or off it.** Each one brings the car up to a jump's
    ///speed from whatever it is doing, so pressing again near the top of a jump climbs another jump's height
    ///above it - and pressing over and over climbs as high as anybody likes. A press on the way down catches
    ///the fall the same way, which is also how a ledge driven off by mistake is saved.
    ///
    ///**Brought up to it, not given it.** The speed is pushed in over liftTime at one steady rate, gravity set
    ///aside while it is, so a jump leaves the ground the way a car on springs would rather than in a single
    ///frame - and a press while falling pushes for longer, since it has the fall to undo first.
    ///
    ///**Landing is tested against the highest top under the car that it was above before this step**, rather
    ///than against where it ends up - so a fall of any speed lands on the first thing it passes, however thin,
    ///and nothing is fallen through between two frames.
    ///
    ///**Held, the top of a jump is where the car stays**, for as long as the jump is held: it flies about at that
    ///height, with all the steering and the throttle it has on the ground - see drive - and falls once it is let go.
    ///A press while it is up there jumps again from there, so it can be flown higher a press at a time. Held while
    ///driving off an edge, the edge's height is where it stays.
    ///
    function climb(world, player, input, pressed, dt) {
        const unit = world.unit;
        const width = carWidth(world, player);
        const events = player.events;

        //The jump and the fall times the car's size - see carWidth.
        const jumpSpeed = world.jumpSpeed * player.size;
        const fallSpeed = world.fallSpeed * player.size;

        //Up there only for a step that finds it so - below - so letting go or landing let go of it. Read by drive,
        //which runs first, so it drives by what the last step found.
        player.hovering = false;

        if (player.grounded) {
            const ground = groundBelow(world, player.x, player.y, player.z + (TUNING.stepUp * width));

            if (ground >= player.z - (TUNING.snapDown * width)) {
                player.z = ground;
                player.standing = world.under;
                player.vz = 0;
            }
            else {
                //Driven off an edge.
                player.grounded = false;
                player.standing = -1;
                player.vz = 0;
            }
        }

        if (pressed) {
            let type = 'airJump';

            if (player.grounded)
                type = 'jump';

            if (player.vz < jumpSpeed)
                player.lift = TUNING.liftTime * ((jumpSpeed - player.vz) / jumpSpeed);

            player.grounded = false;
            player.standing = -1;

            events.push({ type: type });
        }

        if (player.grounded)
            return;

        player.airTime += dt;

        //
        //**Moved by the average of the speed going in and coming out**, which under a constant pull is the
        //exact answer rather than an approximation of it. Stepping by the new speed alone loses half a frame of
        //rise on every jump - 4.6% of the height at sixty frames a second and twice that at thirty - so the same
        //press reached a different height on a slower machine, and a ledge one jump high was out of reach there.
        //
        //The push and the pull are two different constants, so a step that ends the push is taken in two parts.
        //
        let rise = 0;
        let left = dt;

        if (player.lift > 0) {
            const pushing = Math.min(left, player.lift);
            const pushed = Math.min(player.vz + ((jumpSpeed / TUNING.liftTime) * pushing), jumpSpeed);

            rise += ((player.vz + pushed) / 2) * pushing;

            player.vz = pushed;
            player.lift -= pushing;

            left -= pushing;
        }

        if (left > 0) {
            const pulled = pull(world, player, input);
            const after = Math.max(player.vz - (pulled * left), -fallSpeed * TUNING.terminalFall);

            //Held, up to the top in this step if it gets there and no further - and kept there. Not with the jumps run
            //dry: the meter is what holds the car up, and an empty one lets it fall - see charge.
            if (input.jump && !player.recharging && after < 0) {
                if (player.vz > 0)
                    rise += (player.vz * player.vz) / (2 * pulled);

                player.vz = 0;
                player.hovering = true;
            }
            else {
                rise += ((player.vz + after) / 2) * left;

                player.vz = after;
            }
        }

        const next = player.z + rise;

        if (player.vz <= 0) {
            const ground = groundBelow(world, player.x, player.y, player.z);

            if (next <= ground) {
                player.z = ground;
                player.vz = 0;
                player.lift = 0;
                player.grounded = true;
                player.standing = world.under;

                events.push({ type: 'landed', airTime: player.airTime, standing: player.standing });

                player.airTime = 0;

                return;
            }

            player.z = next;

            return;
        }

        const roof = TUNING.height * width;
        const ceiling = ceilingAbove(world, player.x, player.y, player.z + roof - (unit * 1e-6), TUNING.radius * width);

        if (next + roof > ceiling) {
            player.z = ceiling - roof;
            player.vz = 0;
            player.lift = 0;

            events.push({ type: 'bonk' });

            return;
        }

        player.z = next;
    }

    ///The plane's handling, or the jet's - see TUNING.plane.
    function craftOf(player) {
        if (player.jet)
            return TUNING.jet;

        return TUNING.plane;
    }

    ///The plane's top speed, or the jet's, in layout units a second: the panel's top speed, so many times over, in its own widths.
    function planeTop(world, player) {
        return player.topSpeed * craftOf(player).speed * carWidth(world, player);
    }

    ///
    ///How fast whatever is being played goes flat out, in layout units a second - the car's top speed, or the plane's or
    ///the jet's. What the electrons are put down by, in seconds of it - see scatter.
    ///
    function paceOf(world, player) {
        if (player.plane)
            return planeTop(world, player);

        return player.topSpeed * carWidth(world, player);
    }

    ///An angle brought round to within half a turn either way of nothing.
    function wrapped(angle) {
        return Math.atan2(Math.sin(angle), Math.cos(angle));
    }

    ///
    ///The plane's heading, nose and bank said the right way up: for a nose more than a right angle from level - over the top
    ///of a loop, on its back - the same way of pointing the other way round, which is the heading half a turn on, the nose
    ///as far from straight up or down on the other side, and the wings half a turn over. Anything else as it is.
    ///
    ///**Nothing moves.** What changes is what level means: from here, the wings come level by a half roll the right way
    ///up, rather than by the rest of the loop flown backwards. The jet's HUD says it this way too.
    ///
    function attitude(player) {
        if (Math.abs(player.pitch) <= Math.PI / 2)
            return { heading: player.heading, pitch: player.pitch, roll: player.roll };

        return {
            heading: player.heading + Math.PI,
            pitch: wrapped(Math.PI - player.pitch),
            roll: wrapped(player.roll + Math.PI)
        };
    }

    ///The plane said the right way up - see attitude - for whatever comes next to level it from there.
    function upright(player) {
        const said = attitude(player);

        player.heading = said.heading;
        player.pitch = said.pitch;
        player.roll = said.roll;
    }

    ///
    ///Into the car, the plane or the jet - `vehicle` is 'car', 'plane' or 'jet' - where the one there was is, facing the
    ///way it faced.
    ///
    ///**Into the plane or the jet, up and away.** It takes off from wherever the car was - on a wire, in the air - with
    ///its wings level, its nose planeClimbOut up and at no less than its cruise, so it is flying from the first frame
    ///rather than sitting on the ground waiting to be told how. **From the one into the other it flies on**, at no less
    ///than the new one's cruise; the jet's speed comes down to the plane's top over the next moment, the way a boost
    ///lets go. **Back into the car, it drops** from where the plane was, at the plane's speed along the ground, as far
    ///as the car's own top speed goes - and the way the plane was going, which over the top of a loop is not the way its
    ///heading says until it is said the right way up; see attitude.
    ///
    function board(world, player, vehicle) {
        const plane = vehicle === 'plane' || vehicle === 'jet';
        const jet = vehicle === 'jet';
        const flying = Boolean(player.plane);

        if (flying === plane && Boolean(player.jet) === jet)
            return;

        player.plane = plane;
        player.jet = jet;

        if (plane && flying) {
            player.speed = Math.max(player.speed, craftOf(player).cruise * planeTop(world, player));

            return;
        }

        player.climbing = false;
        player.hovering = false;
        player.lift = 0;
        player.vz = 0;
        player.steer = 0;
        player.rolled = 0;

        if (plane) {
            player.speed = Math.max(Math.abs(player.speed), craftOf(player).cruise * planeTop(world, player));
            player.pitch = TUNING.planeClimbOut;
            player.roll = 0;
            player.grounded = false;
            player.standing = -1;

            return;
        }

        upright(player);

        player.speed = Math.min(player.speed * Math.cos(player.pitch), player.topSpeed * carWidth(world, player));
        player.pitch = 0;
        player.roll = 0;
        player.airTime = 0;
    }

    ///
    ///The plane up or down by dz, onto whatever it comes down on and under whatever it comes up against.
    ///
    ///**A top is a runway.** Coming down onto one, it lands - nose up to level, wings level - and runs along it, kept
    ///on it over a step up or a dip down the way the car is, until it runs off the end and is flying again. A ledge it
    ///has flown into low down, within a step of its wheels, it lands on rather than going into. **One over it is a
    ///ceiling**, which puts its nose level.
    ///
    ///**Either is met the right way up** - see attitude - so on its back, a loop's top half, it lands facing the way it
    ///was going and the ceiling leaves it flying on that way, where leveling the nose as it was said turned it round.
    ///
    function soar(world, player, dz) {
        const width = carWidth(world, player);

        if (dz <= 0) {
            const ground = groundBelow(world, player.x, player.y, player.z + (TUNING.stepUp * width));

            if (player.grounded) {
                if (ground >= player.z - (TUNING.snapDown * width)) {
                    player.z = ground;
                    player.standing = world.under;

                    return;
                }

                player.grounded = false;
                player.standing = -1;
            }

            if (player.z + dz <= ground) {
                upright(player);

                player.z = ground;
                player.grounded = true;
                player.standing = world.under;
                player.pitch = Math.max(player.pitch, 0);
                player.roll = 0;

                player.events.push({ type: 'touchdown', airTime: player.airTime, standing: player.standing });

                player.airTime = 0;

                return;
            }

            player.z += dz;

            return;
        }

        player.grounded = false;
        player.standing = -1;

        const roof = TUNING.height * width;
        const ceiling = ceilingAbove(world, player.x, player.y, player.z + roof - (world.unit * 1e-6), TUNING.radius * width);

        if (player.z + dz + roof > ceiling) {
            upright(player);

            player.z = ceiling - roof;
            player.pitch = Math.min(player.pitch, 0);

            player.events.push({ type: 'bonk' });

            return;
        }

        player.z += dz;
    }

    ///
    ///One frame of the plane: the lever, the stick and the rudder, and then along its nose.
    ///
    ///`input` is { throttle, yaw, pitch, roll, bank, nose, level, boost }. The first four run from -1 to 1: the
    ///lever, the rudder, the stick back and forward - positive is the nose up - and the stick to the side, positive
    ///banking into a turn the way a positive yaw turns. Those are the keys, which move the stick for as long as they are
    ///held. **bank and nose are the mouse, which holds it at a place**: the bank and the nose up it asks for, in radians,
    ///or left out where it is not holding that way of the stick. The plane comes round to them over stickEase, and the
    ///wings stay at the bank held rather than leveling by themselves. level is Space, leveling both.
    ///
    ///**Arcade rather than aerodynamic.** It goes where its nose points at the speed the lever says - no lift to lose
    ///and no stall - and a bank turns it, at bankTurn for a right angle of bank, the way a banked plane does. The
    ///stick tips the nose up or down from level whatever the bank, so back climbs and forward dives.
    ///
    ///**And the stick held back is a loop**: the nose comes up past straight up, and the plane goes over the top on its
    ///back and down the far side to level again. Over the top the heading is the way the loop began - the plane flies the
    ///other way along it - so a turn there goes the other way round the compass, which is still to the plane's own right;
    ///see planeTurnsFully. **Let go of over the top, it is turned the right way up** - see attitude - whenever what comes
    ///next is a leveling: the wings left to level by themselves, Space, or the mouse holding the nose at a place. Each
    ///then levels it by a half roll, where it would have flown the rest of the loop backwards; held back, the stick goes
    ///on round instead.
    ///
    ///**The jet flies the same way, by its own numbers** - see TUNING.jet - and its boost is the afterburner.
    ///
    ///**It cannot go through the layout, and it does not crash into it.** It slides along a wall the way the car does
    ///- see moveBy - and head on, stops against it as the car does, to be climbed or dived or turned off it. A slab
    ///under it is a runway, and one over it a ceiling - see soar.
    ///
    function fly(world, player, input, dt) {
        const craft = craftOf(player);
        const width = carWidth(world, player);
        const top = planeTop(world, player);
        const lever = craft.throttle * top * dt;

        //The lever, inside its travel. A boost pulls past the top of it, and lets go back down to it.
        if (input.boost)
            player.speed = approach(player.speed, top * craft.boost, lever * 2);
        else if (player.speed > top)
            player.speed = approach(player.speed, top, lever);
        else
            player.speed = Math.min(player.speed + ((input.throttle || 0) * lever), top);

        //Never slower than it can fly, in the air. On a runway it can be brought to a stop.
        let slowest = craft.slowest * top;

        if (player.grounded)
            slowest = 0;

        player.speed = Math.max(player.speed, slowest);

        const held = Number.isFinite(input.bank);
        const holdsNose = Number.isFinite(input.nose);
        const sideways = (input.roll || 0) !== 0 || held;
        const pulled = (input.pitch || 0) !== 0;
        const eased = 1 - Math.exp(-dt / TUNING.stickEase);

        if (sideways)
            player.rolled = 0;
        else
            player.rolled += dt;

        //The right way up for whatever levels it below, unless the stick is taking it on round.
        if (holdsNose || input.level || (player.rolled > TUNING.planeLevelAfter && !pulled))
            upright(player);

        player.pitch = wrapped(player.pitch + ((input.pitch || 0) * craft.pitch * dt));
        player.roll = wrapped(player.roll + ((input.roll || 0) * craft.roll * dt));

        //The shorter way round to the bank held, which is never the long way past upside down.
        if (held)
            player.roll = wrapped(player.roll + (wrapped(input.bank - player.roll) * eased));

        if (holdsNose)
            player.pitch += (input.nose - player.pitch) * eased;

        if (input.level) {
            const leveling = 1 - Math.exp(-TUNING.levelOut * dt);

            player.pitch -= player.pitch * leveling;
            player.roll -= player.roll * leveling;
        }
        else if (player.rolled > TUNING.planeLevelAfter)
            player.roll -= player.roll * (1 - Math.exp(-TUNING.planeLevel * dt));

        //On a runway the wheels are down: the wings level, and the nose no lower than level.
        if (player.grounded) {
            player.roll = 0;
            player.pitch = Math.max(player.pitch, 0);
        }

        //
        //**Over the top a turn goes the other way round the compass** - to the plane's right, which on its back is the left
        //of its heading - and straight up a bank turns it no way at all. Fully up to planeTurnsFully, so a climb or a dive
        //turns as it always has.
        //
        const turning = Math.min(Math.max(Math.cos(player.pitch) / Math.cos(TUNING.planeTurnsFully), -1), 1);

        player.heading += (((input.yaw || 0) * craft.yaw) + (Math.sin(player.roll) * craft.bankTurn)) * turning * dt;

        if (!player.grounded)
            player.airTime += dt;

        const along = player.speed * dt;

        if (!(along > 0))
            return;

        //In steps a quarter of the car long, as the car drives - see travel. Over the top, back along the heading.
        const across = along * Math.cos(player.pitch);
        const stride = Math.min(world.unit, width) * 0.25;
        const steps = Math.min(40, Math.max(1, Math.ceil(along / stride)));
        const dx = (Math.cos(player.heading) * across) / steps;
        const dy = (Math.sin(player.heading) * across) / steps;
        const dz = (along * Math.sin(player.pitch)) / steps;

        let moved = 0;

        for (let i = 0; i < steps; i++) {
            moved += moveBy(world, player, dx, dy);

            soar(world, player, dz);
        }

        const flat = Math.abs(across);

        if (flat > 0 && moved < flat * TUNING.crash)
            player.speed *= moved / flat;
    }

    ///One frame of the car: the throttle and the wheel, then along the ground and up and down - or, lock on, up a wall.
    function ride(world, player, input, pressed, dt) {
        const forward = (input.throttle || 0) > 0;

        drive(world, player, input, dt);

        if (player.climbing && (pressed || !forward || !player.locked))
            player.climbing = false;

        if (player.climbing) {
            player.hovering = false;

            scale(world, player, dt);

            return;
        }

        travel(world, player, dt);
        climb(world, player, input, pressed, dt);

        if (player.locked && player.grounded && forward && intoWall(world, player)) {
            player.climbing = true;

            player.events.push({ type: 'climb' });
        }
    }

    ///
    ///Limit jump: the meter, filled for the time gone by and drawn on for a press - and whether the press is a jump.
    ///
    ///**A jump a press, filling again by itself** - one every jumpRecharge over jumpCharges seconds - so a car that
    ///jumps now and then never runs out. **Run dry, it waits for all of it**: the press that leaves less than a whole
    ///jump starts it recharging, and until it is full again a press is not a jump and the top of one is not held - see
    ///climb. 'noJump' says a press came to nothing, and 'recharged' that the wait is over.
    ///
    ///Off, the meter is kept full, so it is full when it is turned on. Never the plane's, which does not jump - though it
    ///fills while the plane flies.
    ///
    function charge(player, pressed, dt) {
        const full = TUNING.jumpCharges;

        if (!player.limitJump) {
            player.jumps = full;
            player.recharging = false;

            return pressed;
        }

        player.jumps = Math.min(full, player.jumps + ((full / TUNING.jumpRecharge) * dt));

        if (player.recharging && player.jumps >= full) {
            player.recharging = false;

            player.events.push({ type: 'recharged' });
        }

        if (!pressed || player.plane)
            return pressed;

        if (player.recharging || player.jumps < 1) {
            player.events.push({ type: 'noJump' });

            return false;
        }

        player.jumps -= 1;

        if (player.jumps < 1)
            player.recharging = true;

        return true;
    }

    ///Where an electron's middle is, up the stack: electronHover of the car's width over its top.
    function electronZ(world, player, one) {
        return one.top + (TUNING.electronHover * carWidth(world, player));
    }

    ///
    ///A top under (x, y) an electron can float over, picked at random from all there are - or -Infinity for none.
    ///
    ///**Somewhere the car can get to**: nothing round the electron, and room for the car between the top and whatever is
    ///over it - so one under a layer is still driven into. Any top there rather than only the highest, since the car
    ///drives under the layers as well as over them.
    ///
    function perch(world, x, y, width) {
        const room = TUNING.height * width;
        const radius = TUNING.radius * width;
        const tops = [];

        let limit = Infinity;

        for (let i = 0; i < 8; i++) {
            const top = groundBelow(world, x, y, limit);

            if (top === -Infinity)
                break;

            limit = top - (world.unit * 1e-6);

            if (insideSolid(world, x, y, top + (TUNING.electronHover * width)))
                continue;

            if (ceilingAbove(world, x, y, top, radius) - top < room)
                continue;

            tops.push(top);
        }

        if (tops.length === 0)
            return -Infinity;

        return tops[Math.floor(world.random() * tops.length)];
    }

    ///
    ///The electrons round the car, for one step: those left too far behind taken away, and more put down until there are
    ///electronCount - a few places tried a step, so a field filling up is spread over frames.
    ///
    ///**Between electronNear and electronFar seconds at the top speed**, evenly over that ring, so there is one every few
    ///seconds whatever the panel's top speed - and as often in the jet as in the car. **All round the car, and ahead of
    ///the plane**, which does not turn round for them. Each over a top - see perch - and taken away past half as far
    ///again as the furthest.
    ///
    function scatter(world, player) {
        const electrons = world.electrons;
        const width = carWidth(world, player);
        const pace = paceOf(world, player);
        const near = TUNING.electronNear * pace;
        const far = TUNING.electronFar * pace;

        for (let i = electrons.length - 1; i >= 0; i--) {
            const one = electrons[i];

            if (Math.hypot(one.x - player.x, one.y - player.y) <= far * 1.5)
                continue;

            one.gone = true;
            electrons.splice(i, 1);
        }

        let spread = Math.PI;

        if (player.plane)
            spread = Math.PI / 2;

        for (let tries = 0; tries < TUNING.electronTries && electrons.length < TUNING.electronCount; tries++) {
            const angle = player.heading + (((world.random() * 2) - 1) * spread);
            const distance = Math.sqrt((near * near) + (world.random() * ((far * far) - (near * near))));
            const x = player.x + (Math.cos(angle) * distance);
            const y = player.y + (Math.sin(angle) * distance);
            const top = perch(world, x, y, width);

            if (top === -Infinity)
                continue;

            electrons.push({ x: x, y: y, top: top, phase: world.random() * Math.PI * 2, gone: false });
        }
    }

    ///An electron off the field and onto the score, said as `type` with where it was and what it was worth.
    function take(world, player, index, type) {
        const one = world.electrons[index];

        one.gone = true;
        world.electrons.splice(index, 1);

        player.score += TUNING.electronPoints;

        player.events.push({ type: type, x: one.x, y: one.y, z: electronZ(world, player, one), points: TUNING.electronPoints });
    }

    ///How far along the segment from a to b its nearest point to c is, from 0 to 1.
    function nearestAlong(ax, ay, az, bx, by, bz, cx, cy, cz) {
        const dx = bx - ax;
        const dy = by - ay;
        const dz = bz - az;
        const length = (dx * dx) + (dy * dy) + (dz * dz);

        if (!(length > 0))
            return 0;

        return Math.max(0, Math.min(1, (((cx - ax) * dx) + ((cy - ay) * dy) + ((cz - az) * dz)) / length));
    }

    ///How close the segment from a to b comes to c.
    function passes(ax, ay, az, bx, by, bz, cx, cy, cz) {
        const t = nearestAlong(ax, ay, az, bx, by, bz, cx, cy, cz);

        return Math.hypot(ax + ((bx - ax) * t) - cx, ay + ((by - ay) * t) - cy, az + ((bz - az) * t) - cz);
    }

    ///
    ///The electrons the car has driven into this step, taken: from (fromX, fromY), where it was, to where it is - so one
    ///is not driven past between two frames. Into is within its footprint and electronRadius of it, and between its
    ///wheels and its roof.
    ///
    function pickUp(world, player, fromX, fromY, fromZ) {
        const width = carWidth(world, player);
        const reach = (TUNING.radius + TUNING.electronRadius) * width;
        const low = Math.min(fromZ, player.z) - (TUNING.electronRadius * width);
        const high = Math.max(fromZ, player.z) + ((TUNING.height + TUNING.electronRadius) * width);
        const electrons = world.electrons;

        for (let i = electrons.length - 1; i >= 0; i--) {
            const one = electrons[i];
            const z = electronZ(world, player, one);

            if (z < low || z > high)
                continue;

            if (passes(fromX, fromY, 0, player.x, player.y, 0, one.x, one.y, 0) > reach)
                continue;

            take(world, player, i, 'pickedUp');
        }
    }

    ///
    ///Which way the plane's nose points, and its up and its right as it is flown - bank and all - as unit vectors in the
    ///layout's axes: f, u and r. The right is the model's, the one on the right of the screen from behind it - which in
    ///the layout is the left, since the scene is the layout's mirror; see ThreeInterop.js.
    ///
    function axes(player) {
        const ch = Math.cos(player.heading);
        const sh = Math.sin(player.heading);
        const cp = Math.cos(player.pitch);
        const sp = Math.sin(player.pitch);
        const cr = Math.cos(player.roll);
        const sr = Math.sin(player.roll);

        //Level, the up is the nose crossed with the right: tipped back as far as the nose is up.
        const ux = -sp * ch;
        const uy = -sp * sh;
        const uz = cp;

        //Banked, the right goes down and the up over towards it.
        return {
            fx: cp * ch,
            fy: cp * sh,
            fz: sp,
            ux: (ux * cr) + (-sh * sr),
            uy: (uy * cr) + (ch * sr),
            uz: uz * cr,
            rx: (-sh * cr) - (ux * sr),
            ry: (ch * cr) - (uy * sr),
            rz: -uz * sr
        };
    }

    ///
    ///The electron the jet's missile would go after - of those within missileCone of the nose and inside the field, the
    ///one nearest the line of it - or null. The bracket on screen is round it.
    ///
    function aim(world, player) {
        const width = carWidth(world, player);
        const frame = axes(player);
        const range = TUNING.electronFar * paceOf(world, player);
        const eye = player.z + (0.3 * width);

        let best = null;
        let bestLine = Math.cos(TUNING.missileCone);

        for (const one of world.electrons) {
            const dx = one.x - player.x;
            const dy = one.y - player.y;
            const dz = electronZ(world, player, one) - eye;
            const distance = Math.hypot(dx, dy, dz);

            if (!(distance > width) || distance > range)
                continue;

            const line = ((dx * frame.fx) + (dy * frame.fy) + (dz * frame.fz)) / distance;

            if (line <= bestLine)
                continue;

            best = one;
            bestLine = line;
        }

        return best;
    }

    ///
    ///A weapon a step on - the gun or the missiles, `kind` 'bullet' or 'missile' - with `trigger` its trigger held or
    ///not: and whether a shot goes now, its last one's reload over and the weapon not overheated.
    ///
    ///**It warms while it fires** - its trigger held, or the last shot's reload still running - by heatBurst to overheated
    ///from cold, and **cools the rest of the time** by heatCool to cold from overheated. **By the time it fires rather
    ///than by the shot**: a burst is three seconds at any frame rate, though how many rounds are in it is up to
    ///the frames they go on; and a shot's reload is firing too, so a press for each missile is no way round it.
    ///**Overheated, it fires nothing until it is cold again**, cooling with the trigger held or not.
    ///
    ///'overheated' says it has, 'cooled' that it is cold again after it, and 'noShot' that its trigger was pressed in
    ///between, which fired nothing.
    ///
    function warm(player, weapon, kind, trigger, dt) {
        const pressed = trigger && !weapon.held;

        let firing = 0;

        if (trigger && !weapon.hot)
            firing = dt;
        else if (!weapon.hot)
            firing = Math.min(dt, weapon.wait);

        weapon.held = trigger;
        weapon.wait = Math.max(0, weapon.wait - dt);
        weapon.heat += (firing / TUNING.heatBurst) - ((dt - firing) / TUNING.heatCool);

        if (weapon.heat >= 1 - 1e-9 && !weapon.hot) {
            weapon.hot = true;

            player.events.push({ type: 'overheated', kind: kind });
        }

        weapon.heat = Math.min(Math.max(weapon.heat, 0), 1);

        if (weapon.hot && weapon.heat <= 0) {
            weapon.hot = false;

            player.events.push({ type: 'cooled', kind: kind });
        }

        if (weapon.hot && pressed)
            player.events.push({ type: 'noShot', kind: kind });

        return trigger && !weapon.hot && weapon.wait <= 1e-6;
    }

    ///
    ///The gun, the plane's and the jet's, for a step: with `trigger` held, a round goes whenever the last has had its
    ///time - gunRate a second - for as long as it has not overheated; see warm.
    ///
    ///**A round goes from the nose and along it**, at gunSpeed seconds of the top speed a second on top of its own, from
    ///either side of the propeller or the jet's nose in turn - close enough to the middle that the crosshair is where it
    ///goes.
    ///
    function fire(world, player, trigger, dt) {
        if (!warm(player, player.gun, 'bullet', trigger, dt))
            return;

        player.side = -player.side;

        const shot = loose(world, player, 'bullet', 0.75, 0.3, 0.12 * player.side);

        shot.speed = player.speed + (TUNING.gunSpeed * planeTop(world, player));
        shot.life = TUNING.gunLife;

        player.gun.wait = 1 / TUNING.gunRate;
    }

    ///
    ///The jet's missiles, for a step: with `trigger` held, one goes every missileReload, for as long as they have not
    ///overheated - see warm. The plane has none, and its trigger for them is never held.
    ///
    ///**A missile goes from under a wing**, either one in turn, at the jet's own speed to begin with - see homeIn - after
    ///whatever aim finds.
    ///
    function launch(world, player, trigger, dt) {
        if (!warm(player, player.missiles, 'missile', trigger, dt))
            return;

        player.wing = -player.wing;

        //Off the rail under a wing, where the jet's model carries one - see buildJet in ThreeInterop.js.
        const shot = loose(world, player, 'missile', -0.25, 0.2, 0.4 * player.wing);

        shot.speed = player.speed;
        shot.top = TUNING.missileSpeed * planeTop(world, player);
        shot.life = TUNING.missileLife;
        shot.target = aim(world, player);

        player.missiles.wait = TUNING.missileReload;
    }

    ///
    ///A round or a missile put into the world, and said: `kind`, from `along` the nose, `up` and `out` to the right of the
    ///middle, in car widths, and pointed along the nose. How fast it goes and for how long are the caller's to set.
    ///
    function loose(world, player, kind, along, up, out) {
        const width = carWidth(world, player);
        const frame = axes(player);

        const shot = {
            kind: kind,
            x: player.x + (((frame.fx * along) + (frame.ux * up) + (frame.rx * out)) * width),
            y: player.y + (((frame.fy * along) + (frame.uy * up) + (frame.ry * out)) * width),
            z: player.z + (((frame.fz * along) + (frame.uz * up) + (frame.rz * out)) * width),
            dx: frame.fx,
            dy: frame.fy,
            dz: frame.fz,
            speed: 0,
            top: 0,
            life: 0,
            target: null
        };

        world.shots.push(shot);

        player.events.push({ type: 'fired', kind: kind, x: shot.x, y: shot.y, z: shot.z });

        return shot;
    }

    ///
    ///A missile, a step on: up to its top speed over missileBoost, and turned towards its electron at missileTurn a
    ///second - if it was given one, and the electron is still there. Without one it flies straight on.
    ///
    function homeIn(world, player, shot, dt) {
        shot.speed = Math.min(shot.top, shot.speed + ((shot.top / TUNING.missileBoost) * dt));

        const target = shot.target;

        if (target == null || target.gone)
            return;

        let wx = target.x - shot.x;
        let wy = target.y - shot.y;
        let wz = electronZ(world, player, target) - shot.z;

        const distance = Math.hypot(wx, wy, wz);

        if (!(distance > 0))
            return;

        wx /= distance;
        wy /= distance;
        wz /= distance;

        const angle = Math.acos(Math.max(-1, Math.min(1, (shot.dx * wx) + (shot.dy * wy) + (shot.dz * wz))));

        if (!(angle > 1e-9))
            return;

        //That share of the way round, which for a step this short is a turn of missileTurn times dt near enough.
        const share = Math.min(1, (TUNING.missileTurn * dt) / angle);
        const nx = shot.dx + ((wx - shot.dx) * share);
        const ny = shot.dy + ((wy - shot.dy) * share);
        const nz = shot.dz + ((wz - shot.dz) * share);
        const length = Math.hypot(nx, ny, nz);

        if (!(length > 0))
            return;

        shot.dx = nx / length;
        shot.dy = ny / length;
        shot.dz = nz / length;
    }

    ///
    ///How much of the way from a to b is clear of the layout, from 0 to 1 - asked every half a car width, and at no more
    ///than eight places, so a fast round is not a hundred questions a frame. A layer thinner than that can be shot through.
    ///
    function openFor(world, ax, ay, az, bx, by, bz, width) {
        const length = Math.hypot(bx - ax, by - ay, bz - az);
        const steps = Math.min(8, Math.max(1, Math.ceil(length / (width * 0.5))));

        for (let i = 1; i <= steps; i++) {
            const t = i / steps;

            if (insideSolid(world, ax + ((bx - ax) * t), ay + ((by - ay) * t), az + ((bz - az) * t)))
                return (i - 1) / steps;
        }

        return 1;
    }

    ///The first electron the shot passes within gunReach of on its way to (toX, toY, toZ) - its index - or -1.
    function struck(world, player, shot, toX, toY, toZ, width) {
        const reach = (TUNING.electronRadius + TUNING.gunReach) * width;

        let found = -1;
        let first = Infinity;

        for (let i = 0; i < world.electrons.length; i++) {
            const one = world.electrons[i];
            const z = electronZ(world, player, one);
            const t = nearestAlong(shot.x, shot.y, shot.z, toX, toY, toZ, one.x, one.y, z);

            if (t >= first)
                continue;

            const missed = Math.hypot(shot.x + ((toX - shot.x) * t) - one.x, shot.y + ((toY - shot.y) * t) - one.y, shot.z + ((toZ - shot.z) * t) - z);

            if (missed > reach)
                continue;

            found = i;
            first = t;
        }

        return found;
    }

    ///A missile blowing up at (x, y, z), and every electron within missileBlast car widths of it shot down with it.
    function blast(world, player, x, y, z) {
        const radius = TUNING.missileBlast * carWidth(world, player);

        for (let i = world.electrons.length - 1; i >= 0; i--) {
            const one = world.electrons[i];

            if (Math.hypot(one.x - x, one.y - y, electronZ(world, player, one) - z) <= radius)
                take(world, player, i, 'shotDown');
        }

        player.events.push({ type: 'blast', x: x, y: y, z: z });
    }

    ///
    ///Everything fired, a step on: a missile brought round after its electron and up to speed, and each moved - to the
    ///first thing in its way. **An electron it passes close to is shot down**, and a round goes with it while a missile
    ///blows up there, taking whatever else is near. **The layout stops either**, a missile blowing up against it; and at
    ///the end of its time a round is gone and a missile blows up where it is.
    ///
    function travelShots(world, player, dt) {
        const shots = world.shots;
        const width = carWidth(world, player);

        for (let i = shots.length - 1; i >= 0; i--) {
            const shot = shots[i];

            if (shot.kind === 'missile')
                homeIn(world, player, shot, dt);

            const step = shot.speed * dt;
            const toX = shot.x + (shot.dx * step);
            const toY = shot.y + (shot.dy * step);
            const toZ = shot.z + (shot.dz * step);
            const clear = openFor(world, shot.x, shot.y, shot.z, toX, toY, toZ, width);
            const endX = shot.x + ((toX - shot.x) * clear);
            const endY = shot.y + ((toY - shot.y) * clear);
            const endZ = shot.z + ((toZ - shot.z) * clear);
            const hit = struck(world, player, shot, endX, endY, endZ, width);

            if (hit >= 0) {
                const one = world.electrons[hit];
                const z = electronZ(world, player, one);

                take(world, player, hit, 'shotDown');
                shots.splice(i, 1);

                if (shot.kind === 'missile')
                    blast(world, player, one.x, one.y, z);

                continue;
            }

            shot.x = endX;
            shot.y = endY;
            shot.z = endZ;
            shot.life -= dt;

            if (clear >= 1 && shot.life > 0)
                continue;

            shots.splice(i, 1);

            if (shot.kind === 'missile')
                blast(world, player, shot.x, shot.y, shot.z);
        }
    }

    ///
    ///The electrons, and what is fired at them, for one step: put down round the car, driven into by the car and shot at
    ///from the plane and the jet - see scatter, pickUp, fire and launch - and whatever is in flight moved on, whichever is
    ///being played. **The weapons go on cooling in the car**, their triggers never held there, so one overheated in the
    ///air is cold again however it was left.
    ///
    function tend(world, player, input, fromX, fromY, fromZ, dt) {
        scatter(world, player);

        fire(world, player, Boolean(player.plane && input.fire), dt);
        launch(world, player, Boolean(player.jet && input.missile), dt);

        if (!player.plane)
            pickUp(world, player, fromX, fromY, fromZ);

        travelShots(world, player, dt);
    }

    ///
    ///One frame of the game.
    ///
    ///`input` is { throttle, steer, jump, boost }: the first two from -1 to 1, the last two held or not - and the
    ///plane's own besides, for a player that is one, with `fire` the gun's trigger and `missile` the jet's other; see fly,
    ///fire and launch. What happened that a player would want to hear about - a jump, a landing, a fall off the chip, an
    ///electron taken - is left in player.events, which is emptied at the start of every step.
    ///
    ///**Lock on, a wall driven into is climbed** - see scale - for as long as the throttle is held. Letting go
    ///lets go of the wall, and a jump is the other way off it.
    ///
    function step(world, player, input, dt) {
        player.events.length = 0;

        if (world.empty || !(dt > 0))
            return player.events;

        dt = Math.min(dt, TUNING.longestStep);

        //
        //A press is the moment the key goes down. Holding it is one jump, held at its top for as long as it is held -
        //not a stream of them, which would turn the jump into a jetpack. See climb. And with the jumps limited, a press
        //is a jump only when the meter has one - see charge.
        //
        const pressed = charge(player, Boolean(input.jump) && !player.jumpHeld, dt);

        player.jumpHeld = Boolean(input.jump);

        //Where it was, so an electron passed between this frame and the last is still driven into.
        let fromX = player.x;
        let fromY = player.y;
        let fromZ = player.z;

        if (player.plane)
            fly(world, player, input, dt);
        else
            ride(world, player, input, pressed, dt);

        if (player.z < world.lowest - (TUNING.respawnDepth * world.unit)) {
            respawn(world, player);

            player.events.push({ type: 'fell' });

            //Put back at the start, which is not a drive past everything between there and here.
            fromX = player.x;
            fromY = player.y;
            fromZ = player.z;
        }

        tend(world, player, input, fromX, fromY, fromZ, dt);

        return player.events;
    }

    return {
        TUNING: TUNING,
        buildWorld: buildWorld,
        restack: restack,
        spawn: spawn,
        respawn: respawn,
        step: step,
        board: board,
        planeTop: planeTop,
        paceOf: paceOf,
        groundBelow: groundBelow,
        sightLine: sightLine,
        contains: contains,
        carWidth: carWidth,
        electronZ: electronZ,
        axes: axes,
        attitude: attitude,
        aim: aim
    };
});
