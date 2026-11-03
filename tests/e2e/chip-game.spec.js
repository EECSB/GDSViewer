//The chip game: Play game, under Settings, drops a car onto the top layer of the 3D view, and the keyboard drives
//it. The physics is unit-tested under Node in jstests/chipGame.test.js; what is here is everything that needs
//a browser - the menu, the keys reaching the game and nothing else, the car in the scene, the camera put
//back afterwards.
const { test, expect } = require('@playwright/test');
const { gotoExample, captureScene, cameraPosition, selectView, storedSession, openViewSettings, MOSFET } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();

    //
    //**The neon off**, which every game starts with otherwise - see the specs about it, after Apply texture's. Headless
    //Chromium draws in software, where the glow made a frame of the game three times as long, and the rest of these time
    //the game by its frames. Kept in the browser the way the panel keeps it, so a game started again, or the page loaded
    //again, has it off too.
    //
    await page.evaluate(() => localStorage.setItem('gdsviewer.game', JSON.stringify({ neon: false })));

    //
    //Before the game rather than during it: capturing nudges the Distance slider, which restacks the scene. **Until it
    //has caught it**: on a busy dev server the restack can come after the half second captureScene waits, and the specs
    //that read the game off the scene had nothing to read it from.
    //
    await expect.poll(async () => {
        await captureScene(page);

        return page.evaluate(() => window.__gdsScene != null);
    }).toBe(true);
});

///The drop-down under the gear, opened by its press and left alone if it already is.
async function openSettings(page) {
    if (await page.locator('#settingsPicker').count() === 0)
        await page.locator('#settingsMenu').click();

    await expect(page.locator('#settingsPicker')).toBeVisible();
}

///Play game, or Stop the game while one runs - the same entry under Settings.
async function pressGameItem(page) {
    await openSettings(page);

    await page.locator('#playGameItem').click();
}

///Where the car is, in the scene's axes, and which way the camera behind it reads as right - or null.
async function car(page) {
    return page.evaluate(() => {
        const scene = window.__gdsScene;
        const found = scene?.getObjectByName('chipGameCar');

        if (found == null)
            return null;

        const camera = scene.children.find(child => child.isCamera);

        camera.updateMatrixWorld();

        //The camera's own +X, in the world: what is to the right on screen.
        const elements = camera.matrixWorld.elements;

        return {
            x: found.position.x,
            y: found.position.y,
            z: found.position.z,
            right: { x: elements[0], y: elements[1], z: elements[2] }
        };
    });
}

//
//How long a jump has to come back down, by the wall clock. The game's own clock moves at most a twentieth of a second a
//frame, so on a machine busy enough to draw a few frames a second a jump of a second takes many more than five.
//
const COME_DOWN = { timeout: 15000 };

///Waits for the car to come down onto something, which is when the layer is named.
async function landed(page) {
    await expect(page.locator('#gameHud')).toBeVisible();
    await expect(page.locator('#gameLayer')).not.toBeEmpty({ timeout: 15000 });

    //Landed is named; settled is a moment later, once the bounce of arriving has gone out of the camera.
    await page.waitForTimeout(300);
}

///Starts a game from the menu and waits for the car to land.
async function startAndLand(page) {
    await pressGameItem(page);
    await landed(page);
}

test('Play game is under Settings, and drops a car onto the top layer and says which layer it is on', async ({ page }) => {
    await openSettings(page);

    await expect(page.locator('#playGameItem')).toHaveText('Play game');

    await page.locator('#playGameItem').click();

    //The menu goes as the game starts, like the tour's entry.
    await expect(page.locator('#settingsPicker')).toHaveCount(0);

    await landed(page);

    //met1 is the top of the MOSFET's stack.
    await expect(page.locator('#gameLayer')).toHaveText('met1 (68/20)');

    expect(await car(page)).not.toBeNull();

    //And the slab it is on is outlined, which is the one shape on its layer and not the whole layer.
    expect(await page.evaluate(() => window.__gdsScene.getObjectByName('chipGameOutline') != null)).toBe(true);
});

test('W drives the car forward, and A turns it to the left as the camera sees it', async ({ page }) => {
    await startAndLand(page);

    const parked = await car(page);

    await page.keyboard.down('KeyW');
    await page.waitForTimeout(400);

    const rolling = await car(page);

    expect(Math.hypot(rolling.x - parked.x, rolling.z - parked.z)).toBeGreaterThan(0);

    await page.keyboard.down('KeyA');
    await page.waitForTimeout(500);

    const turned = await car(page);

    await page.keyboard.up('KeyA');
    await page.keyboard.up('KeyW');

    //How far the turn carried the car along the screen's right, which a left turn makes negative.
    const sideways = ((turned.x - rolling.x) * rolling.right.x) + ((turned.z - rolling.z) * rolling.right.z);

    expect(sideways).toBeLessThan(0);
});

test('Space jumps the car and it comes back down, without pressing anything on the page', async ({ page }) => {
    await startAndLand(page);

    const parked = await car(page);

    await page.keyboard.down('Space');

    await expect.poll(async () => (await car(page)).y - parked.y, { timeout: 5000 }).toBeGreaterThan(0);

    await page.keyboard.up('Space');

    await expect.poll(async () => Math.abs((await car(page)).y - parked.y), COME_DOWN).toBeLessThan(1e-6);

    //Space presses a focused button, and the gear the game was started from is the one it would press.
    await expect(page.locator('#settingsPicker')).toHaveCount(0);
    await expect(page.locator('#gameHud')).toBeVisible();
});

///One jump from the ground: Space held until the car is up, let go, and the car back down where it was.
async function jumpOnce(page) {
    const parked = await car(page);

    await page.keyboard.down('Space');

    await expect.poll(async () => (await car(page)).y - parked.y, { timeout: 5000 }).toBeGreaterThan(0);

    await page.keyboard.up('Space');

    await expect.poll(async () => Math.abs((await car(page)).y - parked.y), COME_DOWN).toBeLessThan(1e-6);
}

///
///A jump blips, and with the sound off it does not.
///
///The blip is a note made for the jump that plays it - a new oscillator each time, where the engine's and the
///tires' are made once as the game starts - so counting the oscillators the page makes counts the blips.
///
test('a jump blips, and with the sound off it does not', async ({ page }) => {
    await page.evaluate(() => {
        window.__oscillators = 0;

        const make = BaseAudioContext.prototype.createOscillator;

        BaseAudioContext.prototype.createOscillator = function () {
            window.__oscillators++;

            return make.call(this);
        };
    });

    await startAndLand(page);

    //
    //**The music off first**: its notes are made as they are played, an oscillator or more each, and would be counted
    //as blips. Off, nothing more is made for it from the next frame on.
    //
    await page.locator('#gameMusic').uncheck();
    await page.waitForTimeout(300);

    //The engine's and the tires', as the game starts.
    const made = await page.evaluate(() => window.__oscillators);

    expect(made).toBeGreaterThan(0);

    await jumpOnce(page);

    expect(await page.evaluate(() => window.__oscillators)).toBe(made + 1);

    await page.keyboard.press('KeyM');

    await expect(page.locator('#gameSound')).not.toBeChecked();

    await jumpOnce(page);

    expect(await page.evaluate(() => window.__oscillators)).toBe(made + 1);
});

test('Escape stops the game and puts the camera back where it was', async ({ page }) => {
    const before = await cameraPosition(page);

    await startAndLand(page);

    expect(await cameraPosition(page)).not.toEqual(before);

    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    expect(await car(page)).toBeNull();
    expect(await page.evaluate(() => window.__gdsScene.getObjectByName('chipGameOutline') == null)).toBe(true);

    const after = await cameraPosition(page);

    expect(after.x).toBeCloseTo(before.x, 3);
    expect(after.y).toBeCloseTo(before.y, 3);
    expect(after.z).toBeCloseTo(before.z, 3);

    //And the menu offers a game again.
    await openSettings(page);

    await expect(page.locator('#playGameItem')).toHaveText('Play game');
});

test('while it runs the entry says Stop the game, and stops it', async ({ page }) => {
    await startAndLand(page);

    await openSettings(page);

    await expect(page.locator('#playGameItem')).toHaveText('Stop the game');

    await page.locator('#playGameItem').click();

    await expect(page.locator('#gameHud')).toHaveCount(0);

    expect(await car(page)).toBeNull();
});

test('starting a game turns Admire off, since only one of them can fly the camera', async ({ page }) => {
    const admire = page.locator('#viewAdmire');

    //Admire is a switch in the view's settings, under the cube.
    await openViewSettings(page);
    await admire.check();

    await page.mouse.move(5, 300);
    await expect(page.locator('#viewSettingsMenu')).toHaveCount(0);

    await pressGameItem(page);

    await expect(page.locator('#gameHud')).toBeVisible();

    await openViewSettings(page);

    await expect(admire).not.toBeChecked();
});

test('switching Admire on mid-game ends the game, the same question the other way round', async ({ page }) => {
    await startAndLand(page);

    await openViewSettings(page);

    const admire = page.locator('#viewAdmire');

    await admire.check();

    await expect(admire).toBeChecked();
    await expect(page.locator('#gameHud')).toHaveCount(0);

    expect(await car(page)).toBeNull();
});

///
///From another view the entry goes to 3D first, and the game starts once the scene is there to drive on.
///
///Started the moment the view switched, it would find nothing drawn: the 3D view builds its scene after it mounts.
///
test('Play game from the 2D view opens the 3D view and starts the game in it', async ({ page }) => {
    await selectView(page, 'View2DSvg');

    await expect(page.locator('#container canvas')).toHaveCount(0);

    await pressGameItem(page);

    await expect(page.locator('#container canvas')).toBeVisible();
    await expect(page).toHaveURL(/view=3d/);

    await landed(page);

    await expect(page.locator('#gameLayer')).toHaveText('met1 (68/20)');

    //The scene caught before the switch went with the view it was in. Catching this one nudges the Distance
    //slider, which the game rides out - the car goes up with what it is standing on.
    await captureScene(page);

    expect(await car(page)).not.toBeNull();
});

test('leaving the 3D view mid-game gives the keys back to the page', async ({ page }) => {
    await startAndLand(page);

    //Held by the game while it runs, so a held key is not the browser's.
    const takenInGame = await page.evaluate(() => {
        const event = new KeyboardEvent('keydown', { code: 'KeyW', key: 'w', bubbles: true, cancelable: true });

        window.dispatchEvent(event);

        return event.defaultPrevented;
    });

    expect(takenInGame).toBe(true);

    await selectView(page, 'View2DSvg');

    //The old viewer runs one more frame on its detached canvas, and that frame is the one that lets go.
    await expect.poll(async () => page.evaluate(() => {
        const event = new KeyboardEvent('keydown', { code: 'KeyW', key: 'w', bubbles: true, cancelable: true });

        window.dispatchEvent(event);

        return event.defaultPrevented;
    })).toBe(false);
});

///
///The panel of keys and the frame rate both want the top left corner, and the panel steps down below the meter.
///
///Measured rather than trusted to the stylesheet's offset: in 3D the meter has three more rows than in 2D - the
///scene's frames, its draw calls and its triangles - so an offset that clears it in one view need not in the other.
///
test('with Debug info on, the panel of keys sits below the frame rate rather than over it', async ({ page }) => {
    await openSettings(page);
    await page.locator('#debugInfo').check();

    const meter = page.locator('#frameMeter');

    await expect(meter).toBeVisible();
    await expect(meter.locator('.frameMeterScene')).toBeVisible({ timeout: 10000 });

    await page.locator('#playGameItem').click();
    await landed(page);

    const shown = await meter.boundingBox();
    const hud = await page.locator('#gameHud').boundingBox();

    expect(hud.y).toBeGreaterThan(shown.y + shown.height);
});

///How big the car is drawn, in the scene's units.
async function carScale(page) {
    return page.evaluate(() => window.__gdsScene.getObjectByName('chipGameCar').scale.x);
}

///
///The panel's sliders reach the game, and this browser keeps them for the next one.
///
///The size is the car's own measure - the physics drives with it as well as the scene drawing it - so two and a half
///times the size is two and a half times the scale the car is drawn at. A tenth of the car the layout gets is where it
///starts, the slider's least; half of it is the largest.
///
test('the panel sets the car\'s size and top speed, and keeps them for the next game', async ({ page }) => {
    await startAndLand(page);

    const before = await carScale(page);

    await expect(page.locator('#gameCarSize')).toHaveValue('0.1');
    await expect(page.locator('#gameCarSize')).toHaveAttribute('max', '0.5');

    await page.locator('#gameCarSize').fill('0.25');

    await expect(page.locator('#gameCarSizeSaid')).toHaveText('0.25×');
    await expect.poll(async () => (await carScale(page)) / before).toBeCloseTo(2.5, 3);

    //Half a car width a second to fifteen, which are the ends of the slider, from the four it starts at.
    await expect(page.locator('#gameTopSpeed')).toHaveValue('4');
    await expect(page.locator('#gameTopSpeed')).toHaveAttribute('min', '0.5');
    await expect(page.locator('#gameTopSpeed')).toHaveAttribute('max', '15');

    await page.locator('#gameTopSpeed').fill('12');

    await expect(page.locator('#gameTopSpeedSaid')).toHaveText('12');

    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    await startAndLand(page);

    await expect(page.locator('#gameCarSize')).toHaveValue('0.25');
    await expect(page.locator('#gameTopSpeed')).toHaveValue('12');
    await expect.poll(async () => (await carScale(page)) / before).toBeCloseTo(2.5, 3);
});

///How wide the car is drawn, and how far the camera is from it in those widths - read together, in one frame.
async function framing(page) {
    return page.evaluate(() => {
        const scene = window.__gdsScene;
        const found = scene.getObjectByName('chipGameCar');
        const camera = scene.children.find(child => child.isCamera);
        const at = found.getWorldPosition(found.position.clone());

        return { width: found.scale.x, off: camera.position.distanceTo(at) / found.scale.x };
    });
}

///
///A new size takes the camera with it at once, so it stays as many of the car's widths off and the car keeps its size
///on screen. Eased in like a move of the car, it was left where it had been - 17 widths off a car shrunk to 0.3 - and
///took most of a second to come back to five and a half.
///
test('a new car size keeps the camera as many car widths off as it was', async ({ page }) => {
    await startAndLand(page);

    const before = await framing(page);

    await page.locator('#gameCarSize').fill('0.3');

    let after = before;

    await expect.poll(async () => {
        after = await framing(page);

        return after.width / before.width;
    }).toBeCloseTo(3, 3);

    //Read in one of the first frames at the new size, where the easing still had it twice as far off or more.
    expect(Math.abs(after.off - before.off)).toBeLessThan(before.off * 0.1);
});

///How far the camera is from the car across the ground, in the car's widths.
async function acrossTo(page) {
    return page.evaluate(() => {
        const scene = window.__gdsScene;
        const found = scene.getObjectByName('chipGameCar');
        const camera = scene.children.find(child => child.isCamera);
        const at = found.getWorldPosition(found.position.clone());

        return Math.hypot(camera.position.x - at.x, camera.position.z - at.z) / found.scale.x;
    });
}

///
///Speeding up does not leave the camera behind: it is a fixed number of the car's widths off, put there every frame.
///
///It used to ease towards that spot, which left it behind by the car's speed over the easing rate - nearly twelve
///widths off a car shrunk to 0.2 at top speed, against five and a half parked. Measured across the ground, since the
///camera does still ease up and down; and with Lock on, so the car stays on the top layer and nothing comes between it
///and the camera to pull the camera in.
///
test('speeding up does not leave the camera behind the car', async ({ page }) => {
    await startAndLand(page);

    await page.locator('#gameLockOn').check();
    await page.locator('#gameCarSize').fill('0.2');
    await page.waitForTimeout(300);

    const parked = await acrossTo(page);
    const start = await car(page);

    let furthest = 0;

    await page.keyboard.down('KeyW');

    for (let i = 0; i < 10; i++) {
        await page.waitForTimeout(100);

        furthest = Math.max(furthest, Math.abs((await acrossTo(page)) - parked));
    }

    await page.keyboard.up('KeyW');

    //It got going, so there was a speed to fall behind by.
    const went = await car(page);

    expect(Math.hypot(went.x - start.x, went.z - start.z)).toBeGreaterThan(0);

    expect(furthest).toBeLessThan(parked * 0.03);
});

///How far the camera draws, and the haze the far side fades into - or null where there is none.
async function reach(page) {
    return page.evaluate(() => {
        const scene = window.__gdsScene;
        const camera = scene.children.find(child => child.isCamera);
        let haze = null;

        if (scene.fog != null)
            haze = { from: scene.fog.near, to: scene.fog.far };

        return { far: camera.far, haze: haze };
    });
}

///
///A backdrop chosen in the view's settings, which hang from the cube: `file`, or 'none' for the plain gray. Put away
///again by the pointer leaving them, since they stay open as one is chosen.
///
async function chooseBackdrop(page, file) {
    await page.locator('#viewPick3D').hover();
    await page.locator(`#viewBackground [data-background="${file}"]`).click();

    await page.mouse.move(5, 300);
    await expect(page.locator('#viewSettingsMenu')).toHaveCount(0);
}

///
///The draw distance takes the far plane in and fades the far side into the background, and All draws everything with
///no haze. **Every game starts at All**: the draw distance is the 3D view's own - see view-settings.spec.js - which the
///game has while it runs and puts back as it stops.
///
///**Against a picture the far side is cut instead**, there being no one color to fade into - and a game starts against
///Neon. Taken down to the plain gray, the haze is there.
///
test('the draw distance takes the far plane in with the far side faded, and All draws everything', async ({ page }) => {
    await startAndLand(page);

    await expect.poll(async () => page.evaluate(() => window.__gdsScene.background != null), { timeout: 15000 }).toBe(true);

    const all = await reach(page);

    await expect(page.locator('#gameDrawDistanceSaid')).toHaveText('All');
    expect(all.haze).toBeNull();

    await page.locator('#gameDrawDistance').fill('0.25');

    await expect(page.locator('#gameDrawDistanceSaid')).toHaveText('25%');
    await expect.poll(async () => (await reach(page)).far).toBeLessThan(all.far / 4);

    //Against Neon, cut.
    expect((await reach(page)).haze).toBeNull();

    await chooseBackdrop(page, 'none');

    await expect.poll(async () => (await reach(page)).haze).not.toBeNull();

    const short = await reach(page);

    //Faded over the far side of it, and gone by its end.
    expect(short.haze.to).toBeCloseTo(short.far, 6);
    expect(short.haze.from).toBeLessThan(short.haze.to);

    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    //And the haze goes with the game: the view's own draw distance, All, is back.
    await expect.poll(async () => (await reach(page)).haze).toBeNull();

    //The next game starts at All again.
    await startAndLand(page);

    await expect(page.locator('#gameDrawDistance')).toHaveValue('1');
    await expect(page.locator('#gameDrawDistanceSaid')).toHaveText('All');
    await expect.poll(async () => (await reach(page)).haze).toBeNull();
});

///
///**A game has the view's two settings its own way** - everything drawn and the textures on - whatever the 3D view's
///menu had them at, and puts the view's own back when it stops, whatever it did with them while it ran.
///
test('a game draws everything with the textures on, and puts the view\'s own settings back as it stops', async ({ page }) => {
    //The view's own: a quarter of the chip, and the flat colors.
    await openViewSettings(page);
    await page.locator('#viewDrawDistance').fill('0.25');

    await expect(page.locator('#viewTextures')).not.toBeChecked();
    await expect.poll(async () => (await reach(page)).haze).not.toBeNull();

    const plain = await surfaced(page);

    await startAndLand(page);

    await expect(page.locator('#gameDrawDistance')).toHaveValue('1');
    await expect(page.locator('#gameTexture')).toBeChecked();
    await expect.poll(async () => (await surfaced(page)).on).toBe(plain.slabs);
    await expect.poll(async () => (await reach(page)).haze).toBeNull();

    //Changed while it runs, which the view's menu shows as well.
    await page.locator('#gameDrawDistance').fill('0.5');

    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);
    await expect.poll(async () => (await surfaced(page)).on).toBe(0);
    await expect.poll(async () => (await reach(page)).haze).not.toBeNull();

    await openViewSettings(page);

    await expect(page.locator('#viewDrawDistance')).toHaveValue('0.25');
    await expect(page.locator('#viewTextures')).not.toBeChecked();
});

///
///The box after the size says how small the slider goes, the way the layers' Distance has a box for how far its
///slider reaches - and a car smaller than a least raised past it comes up to it.
///
///Enter sets it and gives the keys back to the car: a box keeps every key it is given, so without that the car would
///not answer until somebody clicked the view.
///
test('the box after the car size sets how small it goes, and Enter gives the keys back', async ({ page }) => {
    await startAndLand(page);

    const before = await carScale(page);
    const size = page.locator('#gameCarSize');
    const least = page.locator('#gameCarSmallest');

    await expect(least).toHaveValue('0.1');
    await expect(size).toHaveAttribute('min', '0.1');

    await least.fill('0.05');
    await least.press('Enter');

    await expect(size).toHaveAttribute('min', '0.05');

    await size.fill('0.05');

    //Half the tenth it starts at.
    await expect(page.locator('#gameCarSizeSaid')).toHaveText('0.05×');
    await expect.poll(async () => (await carScale(page)) / before).toBeCloseTo(0.5, 3);

    //Raised past where the car is, the least brings the car up with it.
    await least.fill('0.3');
    await least.press('Enter');

    await expect(size).toHaveValue('0.3');
    await expect.poll(async () => (await carScale(page)) / before).toBeCloseTo(3, 3);

    const parked = await car(page);

    await page.keyboard.down('KeyW');

    await expect.poll(async () => {
        const rolled = await car(page);

        return Math.hypot(rolled.x - parked.x, rolled.z - parked.z);
    }, { timeout: 5000 }).toBeGreaterThan(0);

    await page.keyboard.up('KeyW');

    //Kept for the next game, like the rest of the panel.
    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    await startAndLand(page);

    await expect(least).toHaveValue('0.3');
    await expect(size).toHaveAttribute('min', '0.3');
    await expect(size).toHaveValue('0.3');
});

///
///A slider keeps the focus once it is let go of, and that took every key off the game: the car stopped answering
///until somebody clicked the view. The arrows steer rather than move it now.
///
test('a slider just used keeps none of the game\'s keys', async ({ page }) => {
    await startAndLand(page);

    const slider = page.locator('#gameTopSpeed');

    await slider.fill('6');
    await slider.focus();

    const parked = await car(page);

    await page.keyboard.down('KeyW');
    await page.keyboard.press('ArrowRight');
    await page.waitForTimeout(500);
    await page.keyboard.up('KeyW');

    const rolled = await car(page);

    expect(Math.hypot(rolled.x - parked.x, rolled.z - parked.z)).toBeGreaterThan(0);

    await expect(slider).toHaveValue('6');
});

///The angle between two directions, either way round, from 0 to pi.
function apart(a, b) {
    return Math.abs(Math.atan2(Math.sin(a - b), Math.cos(a - b)));
}

///Which way round the car the camera is, seen from above.
async function cameraAround(page) {
    return page.evaluate(() => {
        const scene = window.__gdsScene;
        const found = scene.getObjectByName('chipGameCar');
        const camera = scene.children.find(child => child.isCamera);

        return Math.atan2(camera.position.z - found.position.z, camera.position.x - found.position.x);
    });
}

///
///Moving the mouse takes the camera round the car, with no button held, and left alone it goes back behind it.
///
///The movement is the pointer's own, as a mouse's is: a move event carries how far it came, which is what the game
///turns by, and this hands that over directly rather than relying on how a scripted move is reported.
///
test('moving the mouse looks round the car, and left alone the camera goes back behind it', async ({ page }) => {
    await startAndLand(page);

    const behind = await cameraAround(page);

    await page.evaluate(async () => {
        const canvas = document.querySelector('#container canvas');

        for (let i = 0; i < 10; i++) {
            canvas.dispatchEvent(new PointerEvent('pointermove', { movementX: 30, movementY: 0, pointerType: 'mouse', bubbles: true }));

            await new Promise(resolve => setTimeout(resolve, 30));
        }
    });

    await expect.poll(async () => apart(await cameraAround(page), behind)).toBeGreaterThan(0.8);

    await expect.poll(async () => apart(await cameraAround(page), behind), { timeout: 15000 }).toBeLessThan(0.05);
});

///
///Lock on: the end of the wire is a guardrail. Driven forward without it the car goes off the end and comes down on
///a layer under it, which is what makes the second drive mean anything.
///
test('with Lock on the car stays on the layer it is driving along rather than going off the end', async ({ page }) => {
    await startAndLand(page);

    const parked = await car(page);
    const started = Date.now();

    await page.keyboard.down('KeyW');

    await expect.poll(async () => (await car(page)).y, { timeout: 20000 }).toBeLessThan(parked.y - 1e-6);

    await page.keyboard.up('KeyW');

    const tookToFall = Date.now() - started;

    //Back to the start, which drops the car in from above it: down on the top layer again before the second drive.
    await page.keyboard.press('KeyR');

    await expect.poll(async () => (await car(page)).y, { timeout: 15000 }).toBeCloseTo(parked.y, 3);

    await page.locator('#gameLockOn').check();

    const again = await car(page);

    await page.keyboard.down('KeyW');
    await page.waitForTimeout(tookToFall + 2000);
    await page.keyboard.up('KeyW');

    const kept = await car(page);

    expect(Math.hypot(kept.x - again.x, kept.z - again.z)).toBeGreaterThan(0);
    expect(kept.y).toBeCloseTo(again.y, 3);

    await expect(page.locator('#gameLayer')).toHaveText('met1 (68/20)');
});

///The plane as the scene draws it: where it is, its bank and its nose - see ChipGame.placeCar - and whether it is shown.
async function plane(page) {
    return page.evaluate(() => {
        const found = window.__gdsScene.getObjectByName('chipGamePlane');

        if (found == null)
            return null;

        return {
            x: found.position.x,
            y: found.position.y,
            z: found.position.z,
            roll: found.rotation.x,
            pitch: found.rotation.z,
            width: found.scale.x,
            shown: found.visible
        };
    });
}

///Which of the three is on screen.
async function shown(page) {
    return page.evaluate(() => {
        const scene = window.__gdsScene;

        return {
            car: scene.getObjectByName('chipGameCar').visible,
            plane: scene.getObjectByName('chipGamePlane').visible,
            jet: scene.getObjectByName('chipGameJet').visible
        };
    });
}

///
///P is the plane: it takes off from where the car was and flies on by itself, the lever where boarding left it - and P
///again is the jet, and again the car. The panel's list says which, shows the keys for it, and offers what goes with
///each: Invert pitch for the plane and the jet, Lock on and Limit jump for the car. Kept for the next game, like the
///rest of the panel.
///
test('P puts the car into a plane that flies on by itself, then into the jet, and back into the car', async ({ page }) => {
    await startAndLand(page);

    const vehicle = page.locator('#gameVehicle');

    await expect(vehicle).toHaveValue('car');
    await expect(page.locator('#gameLockOn')).toBeVisible();
    await expect(page.locator('#gameLimitJump')).toBeVisible();
    await expect(page.locator('#gameInvertPitch')).toBeHidden();

    await page.keyboard.press('KeyP');

    await expect(vehicle).toHaveValue('plane');
    await expect(page.locator('#gameHud')).toHaveClass(/gameHudPlane/);
    await expect(page.locator('#gameInvertPitch')).toBeVisible();
    await expect(page.locator('#gameLockOn')).toBeHidden();
    await expect(page.locator('#gameLimitJump')).toBeHidden();

    expect(await shown(page)).toEqual({ car: false, plane: true, jet: false });

    const from = await plane(page);

    await expect.poll(async () => {
        const now = await plane(page);

        return Math.hypot(now.x - from.x, now.z - from.z);
    }).toBeGreaterThan(0);

    await page.keyboard.press('KeyP');

    await expect(vehicle).toHaveValue('jet');
    await expect(page.locator('#gameHud')).toHaveClass(/gameHudJet/);
    await expect(page.locator('#gameHud')).not.toHaveClass(/gameHudPlane/);
    await expect(page.locator('#gameInvertPitch')).toBeVisible();

    expect(await shown(page)).toEqual({ car: false, plane: false, jet: true });

    await page.keyboard.press('KeyP');

    await expect(vehicle).toHaveValue('car');
    await expect(page.locator('#gameHud')).not.toHaveClass(/gameHudPlane|gameHudJet/);

    expect(await shown(page)).toEqual({ car: true, plane: false, jet: false });

    //Into the plane from the list, and out of the game: the next one starts in the air in it.
    await vehicle.selectOption('plane');

    await expect.poll(async () => shown(page)).toEqual({ car: false, plane: true, jet: false });

    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    await pressGameItem(page);

    await expect(vehicle).toHaveValue('plane');
    await expect.poll(async () => shown(page)).toEqual({ car: false, plane: true, jet: false });
});

///
///The arrows are the plane's stick: up pushes the nose down and down pulls it up, the way a stick does, and right banks
///it right. Space levels both. Invert pitch turns up and down round, for anybody who reads up as up.
///
test('the arrows pitch and roll the plane, up pushing the nose down unless the pitch is inverted', async ({ page }) => {
    await startAndLand(page);

    await page.locator('#gameVehicle').selectOption('plane');

    await expect(page.locator('#gameHud')).toHaveClass(/gameHudPlane/);

    //
    //Two of its widths up first. The stick is slower than it was, and pushed down straight after taking off the plane
    //came back down onto the layer it left - where the wheels are down and the wings are kept level, so the roll below
    //read nothing.
    //
    const takeoff = await plane(page);

    await expect.poll(async () => (await plane(page)).y - takeoff.y, { timeout: 10000 }).toBeGreaterThan(takeoff.width * 2);

    //It takes off nose up, so up has the nose to push down past level.
    await page.keyboard.down('ArrowUp');
    await expect.poll(async () => (await plane(page)).pitch, { timeout: 5000 }).toBeLessThan(-0.05);
    await page.keyboard.up('ArrowUp');

    await page.keyboard.down('ArrowRight');
    await expect.poll(async () => (await plane(page)).roll, { timeout: 5000 }).toBeGreaterThan(0.3);
    await page.keyboard.up('ArrowRight');

    await page.keyboard.down('Space');

    await expect.poll(async () => {
        const now = await plane(page);

        return Math.max(Math.abs(now.pitch), Math.abs(now.roll));
    }, { timeout: 5000 }).toBeLessThan(0.05);

    await page.keyboard.up('Space');

    await page.locator('#gameInvertPitch').check();

    await page.keyboard.down('ArrowUp');
    await expect.poll(async () => (await plane(page)).pitch, { timeout: 5000 }).toBeGreaterThan(0.1);
    await page.keyboard.up('ArrowUp');
});

///The bank and the nose of whatever is being flown, in radians, as the physics has them.
async function flown(page) {
    return page.evaluate(() => {
        const player = window.__gdsScene.userData.chipGame.player;

        return { roll: player.roll, pitch: player.pitch };
    });
}

///
///Waits for `seconds` of the game's own time in the air - which on a busy machine is slower than the wall clock's, since
///the game steps a twentieth of a second a frame at the most.
///
async function airFor(page, seconds) {
    const airTime = async () => page.evaluate(() => window.__gdsScene.userData.chipGame.player.airTime);
    const from = await airTime();

    await expect.poll(async () => (await airTime()) - from, { timeout: 20000 }).toBeGreaterThan(seconds);
}

///Where on screen the middle of the view is, and how far it is from there to its edges.
async function viewMiddle(page) {
    const box = await page.locator('#container canvas').boundingBox();

    return { x: box.x + (box.width / 2), y: box.y + (box.height / 2), across: box.width / 2, down: box.height / 2, top: box.y };
}

///
///In the plane the mouse is the stick rather than a look round, **held where the pointer is on the view**: right of the
///middle a bank to the right, below it - the stick back - the nose up, and the middle level. Moved there by the mouse
///itself, from wherever it was.
///
///**The middle is the view's, whatever the pointer did before.** It was the pointer's movement, put on as it moved, so
///the middle was wherever the pointer stood as the game started - where Play game was pressed, under Settings up in the
///corner - and the middle of the view was a hard bank to the left with the nose up. That is where this one starts.
///
///Held to one side, it stays banked, for longer than the wings would take to come back level by themselves; off the
///view, the mouse lets go, and they do. **A key takes the stick from it** until the mouse next moves: let go of, the
///wings stay where the key left them rather than going straight back to the pointer's place.
///
test('in the plane the mouse holds the stick where the pointer is, and the middle of the view is level', async ({ page }) => {
    await startAndLand(page);

    await page.keyboard.press('KeyP');

    await expect(page.locator('#gameHud')).toHaveClass(/gameHudPlane/);

    //Two of its widths up first, off the layer it left - where the wings are kept level and the nose no lower than level.
    const takeoff = await plane(page);

    await expect.poll(async () => (await plane(page)).y - takeoff.y, { timeout: 10000 }).toBeGreaterThan(takeoff.width * 2);

    const middle = await viewMiddle(page);

    //From where the game was started to the middle of the view: level.
    await page.mouse.move(middle.x, middle.y, { steps: 8 });

    await expect.poll(async () => {
        const now = await flown(page);

        return Math.max(Math.abs(now.roll), Math.abs(now.pitch));
    }, { timeout: 2000 }).toBeLessThan(0.01);

    //Right of it and below it: a bank to the right and the nose up, held there.
    await page.mouse.move(middle.x + (middle.across * 0.4), middle.y + (middle.down * 0.2));

    await expect.poll(async () => (await flown(page)).roll, { timeout: 2000 }).toBeGreaterThan(0.25);
    await expect.poll(async () => (await flown(page)).pitch, { timeout: 2000 }).toBeGreaterThan(0.05);

    //Come all the way round to it - a few tenths of a second - before it is read.
    await airFor(page, 0.5);

    const banked = (await flown(page)).roll;

    await airFor(page, 1.5);

    expect((await flown(page)).roll).toBeCloseTo(banked, 3);

    //Off the view, onto the bar above it: the mouse lets go, and the wings come back level by themselves.
    await page.mouse.move(middle.x, middle.top - 12);

    await expect.poll(async () => Math.abs((await flown(page)).roll), { timeout: 10000 }).toBeLessThan(0.05);

    //Back in the middle, and then a key: it banks the plane, and let go of, the bank stays rather than going back level.
    await page.mouse.move(middle.x, middle.y);

    await expect.poll(async () => Math.abs((await flown(page)).roll), { timeout: 2000 }).toBeLessThan(0.01);

    await page.keyboard.down('ArrowRight');
    await expect.poll(async () => (await flown(page)).roll, { timeout: 5000 }).toBeGreaterThan(0.3);
    await page.keyboard.up('ArrowRight');

    await airFor(page, 0.25);

    expect((await flown(page)).roll).toBeGreaterThan(0.25);

    expect((await game(page)).grounded).toBe(false);
});

///
///The plane's nose and the camera behind it, in one frame: how far the nose is up, how far off the camera is in the
///plane's widths, how far behind the nose it is - minus one straight behind it, one straight ahead - and how near its up
///is the plane's own up with the wings level, one being the same.
///
async function chase(page) {
    return page.evaluate(() => {
        const scene = window.__gdsScene;
        const player = scene.userData.chipGame.player;
        const found = scene.getObjectByName('chipGamePlane');
        const camera = scene.children.find(child => child.isCamera);
        const frame = window.chipGame.axes(player);
        const ch = Math.cos(player.heading);
        const sh = Math.sin(player.heading);
        const cp = Math.cos(player.pitch);
        const sp = Math.sin(player.pitch);

        //Out of the layout's axes and into the scene's, where layout y is Z.
        const nose = { x: frame.fx, y: frame.fz, z: frame.fy };
        const up = { x: -sp * ch, y: cp, z: -sp * sh };

        const off = {
            x: camera.position.x - found.position.x,
            y: camera.position.y - found.position.y,
            z: camera.position.z - found.position.z
        };

        const length = Math.hypot(off.x, off.y, off.z);

        return {
            pitch: player.pitch,
            off: length / found.scale.x,
            behind: ((off.x * nose.x) + (off.y * nose.y) + (off.z * nose.z)) / length,
            upright: (camera.up.x * up.x) + (camera.up.y * up.y) + (camera.up.z * up.z)
        };
    });
}

///
///**The stick held back is a loop**: the nose comes up past straight up and the plane goes over the top on its back and
///down the far side, where it stopped at 75 degrees and hung there nose up. **The camera goes round with it** - behind
///the plane and as far off the whole way, its up the plane's own: under it on the climb, over the top upside down with
///it, and down the far side. It tipped with the nose only so far, keeping the horizon level, and had no way over the top.
///
test('the stick held back loops the plane, and the camera goes round the loop behind it', async ({ page }) => {
    await startAndLand(page);

    await page.locator('#gameVehicle').selectOption('plane');

    //Two of its widths up first, off the layer it left.
    const takeoff = await plane(page);

    await expect.poll(async () => (await plane(page)).y - takeoff.y, { timeout: 10000 }).toBeGreaterThan(takeoff.width * 2);

    const before = await chase(page);
    const seen = [];

    let over = 0;

    await page.keyboard.down('ArrowDown');

    //Round until the nose is back near level, from over the top.
    await expect.poll(async () => {
        const now = await chase(page);

        seen.push(now);
        over = Math.max(over, Math.abs(now.pitch));

        return over > 2.8 && Math.abs(now.pitch) < 0.3;
    }, { timeout: 40000, intervals: [100] }).toBe(true);

    await page.keyboard.up('ArrowDown');

    expect(over).toBeGreaterThan(3);
    expect(seen.length).toBeGreaterThan(10);

    for (const one of seen) {
        expect(one.behind).toBeLessThan(-0.5);
        expect(Math.abs(one.off - before.off)).toBeLessThan(before.off * 0.15);
        expect(one.upright).toBeGreaterThan(0.8);
    }

    expect((await game(page)).grounded).toBe(false);
});

///
///**The neon's haze can be set from the panel**: Haze reach thins or thickens it and at its far end takes it off, and
///Thins with height unticked keeps it as thick up high as it is down by the chip. Both are kept for the next game, and
///both are grayed out with Neon glow off, which has no haze.
///
test('the panel sets how far the neon haze reaches, whether it thins with height, and kept for the next game', async ({ page }) => {
    await page.evaluate(() => localStorage.setItem('gdsviewer.game', JSON.stringify({ neon: true })));

    await startAndLand(page);

    const reach = page.locator('#gameHaze');
    const climbs = page.locator('#gameHazeClimbs');
    const density = async () => page.evaluate(() => {
        const fog = window.__gdsScene.fog;

        if (fog == null || fog.isFogExp2 !== true)
            return 0;

        return fog.density;
    });

    await expect(reach).toHaveValue('0');
    await expect(page.locator('#gameHazeSaid')).toHaveText('1×');
    await expect(climbs).toBeChecked();

    await expect.poll(density).toBeGreaterThan(0);

    const start = await density();

    //Twice as far is half as thick.
    await reach.fill('1');

    await expect(page.locator('#gameHazeSaid')).toHaveText('2×');
    await expect.poll(async () => (await density()) / start, { timeout: 5000 }).toBeCloseTo(0.5, 1);

    //The far end has none.
    await reach.fill('3');

    await expect(page.locator('#gameHazeSaid')).toHaveText('None');
    await expect.poll(density, { timeout: 5000 }).toBe(0);
    await expect(climbs).toBeDisabled();

    await reach.fill('-1');
    await climbs.uncheck();

    //Kept: the next game starts with them.
    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    await startAndLand(page);

    await expect(page.locator('#gameHaze')).toHaveValue('-1');
    await expect(page.locator('#gameHazeSaid')).toHaveText('0.5×');
    await expect(page.locator('#gameHazeClimbs')).not.toBeChecked();

    //Without the neon there is no haze to set.
    await page.locator('#gameNeon').uncheck();

    await expect(page.locator('#gameHaze')).toBeDisabled();
    await expect(page.locator('#gameHazeClimbs')).toBeDisabled();
});

///
///**The mouse loops the plane too**: near the foot of the view it pulls the stick past what it holds, and the nose comes on
///round rather than stopping at a climb. Back nearer the middle it holds the nose again - and from over the top, that is
///the plane rolled the right way up to it, rather than the loop flown backwards.
///
test('the mouse near the foot of the view loops the plane, and back nearer the middle rolls it the right way up', async ({ page }) => {
    await startAndLand(page);

    await page.keyboard.press('KeyP');

    await expect(page.locator('#gameHud')).toHaveClass(/gameHudPlane/);

    const takeoff = await plane(page);

    await expect.poll(async () => (await plane(page)).y - takeoff.y, { timeout: 10000 }).toBeGreaterThan(takeoff.width * 2);

    const middle = await viewMiddle(page);

    await page.mouse.move(middle.x, middle.y + (middle.down * 0.97), { steps: 4 });

    await expect.poll(async () => Math.abs((await flown(page)).pitch), { timeout: 20000 }).toBeGreaterThan(2.2);

    await page.mouse.move(middle.x, middle.y + (middle.down * 0.1), { steps: 4 });

    await expect.poll(async () => page.evaluate(() => {
        const player = window.__gdsScene.userData.chipGame.player;

        return { up: window.chipGame.axes(player).uz > 0.95, level: Math.abs(player.roll) < 0.05 };
    }), { timeout: 5000 }).toEqual({ up: true, level: true });

    expect((await game(page)).grounded).toBe(false);
});

///
///What the running game says about itself, read off it where it leaves itself for a spec to find - the scene's
///userData. Whether the horn is sounding is the game's sound's own say: what a page plays is nothing a spec can hear.
///
async function game(page) {
    return page.evaluate(() => {
        const running = window.__gdsScene.userData.chipGame;
        const player = running.player;

        return {
            score: player.score,
            jumps: player.jumps,
            recharging: player.recharging,
            grounded: player.grounded,
            horn: running.sound != null && running.sound.honking,
            electrons: running.world.electrons.length
        };
    });
}

///
///An electron put down `widths` car widths along the nose of whatever is being played, turned `off` radians to the side
///of it - over the top the car is on, or with its middle on the line of the plane's guns. Kept as __placedElectron, so
///a spec can ask whether it has gone.
///
///`alone` takes away the ones the game put down and puts no more down, so this one is the only thing to aim at.
///
async function electronAhead(page, widths, off = 0, alone = false) {
    await page.evaluate(({ widths, off, alone }) => {
        const running = window.__gdsScene.userData.chipGame;
        const player = running.player;
        const width = window.chipGame.carWidth(running.world, player);
        const hover = window.chipGame.TUNING.electronHover * width;
        const heading = player.heading + off;
        const across = widths * width * Math.cos(player.pitch);

        if (alone) {
            window.chipGame.TUNING.electronCount = 0;
            running.world.electrons.length = 0;
        }

        let top = player.z;

        if (player.plane)
            top = player.z + (0.3 * width) + (Math.sin(player.pitch) * widths * width) - hover;

        const electron = {
            x: player.x + (Math.cos(heading) * across),
            y: player.y + (Math.sin(heading) * across),
            top: top,
            phase: 0,
            gone: false
        };

        running.world.electrons.push(electron);

        window.__placedElectron = electron;
    }, { widths, off, alone });
}

///Whether the electron electronAhead put down has been taken.
async function placedGone(page) {
    return page.evaluate(() => window.__placedElectron.gone);
}

test('H sounds the horn while it is held, and not with the sound off', async ({ page }) => {
    await startAndLand(page);

    expect((await game(page)).horn).toBe(false);

    await page.keyboard.down('KeyH');

    await expect.poll(async () => (await game(page)).horn).toBe(true);

    await page.keyboard.up('KeyH');

    await expect.poll(async () => (await game(page)).horn).toBe(false);

    await page.keyboard.press('KeyM');

    await expect(page.locator('#gameSound')).not.toBeChecked();

    await page.keyboard.down('KeyH');
    await page.waitForTimeout(300);

    expect((await game(page)).horn).toBe(false);

    await page.keyboard.up('KeyH');
});

///
///Limit jump: the meter shows with the tick, a cell a jump, and four presses run it dry - red while it fills again. The
///car is not held at the top of the last one, and a fifth press does not lift it at all, which a press always did.
///
test('with Limit jump four jumps run the meter dry, and a fifth does not lift the car', async ({ page }) => {
    await startAndLand(page);

    const meter = page.locator('#gameJumps');

    await expect(meter).toBeHidden();

    await page.locator('#gameLimitJump').check();

    await expect(meter).toBeVisible();
    await expect(meter.locator('.gameJumpCell')).toHaveCount(4);
    await expect(meter).not.toHaveClass(/gameJumpsRecharging/);

    const parked = await car(page);

    //Each held and let go for long enough that a frame sees both, which on a loaded machine is not every sixtieth.
    for (let press = 0; press < 4; press++) {
        await page.keyboard.down('Space');
        await page.waitForTimeout(150);
        await page.keyboard.up('Space');
        await page.waitForTimeout(150);
    }

    await expect(meter).toHaveClass(/gameJumpsRecharging/);

    //Down again, Space held or not, and then a press that does nothing.
    await page.keyboard.down('Space');

    await expect.poll(async () => Math.abs((await car(page)).y - parked.y), COME_DOWN).toBeLessThan(1e-6);

    await page.keyboard.up('Space');
    await page.keyboard.down('Space');
    await page.waitForTimeout(600);

    expect(Math.abs((await car(page)).y - parked.y)).toBeLessThan(1e-6);
    expect((await game(page)).recharging).toBe(true);

    await page.keyboard.up('Space');
});

///
///V puts the camera at the driver's eyes - inside the car, under a car width from where it stands - and V again back
///behind it. The panel's list says which and sets it too, and the next game starts the way this one was left.
///
test('V puts the camera in the driver\'s seat, and V again back behind the car', async ({ page }) => {
    await startAndLand(page);

    const view = page.locator('#gameView');

    await expect(view).toHaveValue('third');
    expect((await framing(page)).off).toBeGreaterThan(3);

    await page.keyboard.press('KeyV');

    await expect(view).toHaveValue('first');
    await expect.poll(async () => (await framing(page)).off).toBeLessThan(1);

    await page.keyboard.press('KeyV');

    await expect(view).toHaveValue('third');
    await expect.poll(async () => (await framing(page)).off).toBeGreaterThan(3);

    await view.selectOption('first');

    await expect.poll(async () => (await framing(page)).off).toBeLessThan(1);

    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    await startAndLand(page);

    await expect(view).toHaveValue('first');
    await expect.poll(async () => (await framing(page)).off).toBeLessThan(1);
});

///
///The car picks up an electron it drives into, and the score at the top of the view says so. The game puts electrons
///down round the car from the start; one put right in front of it is the one driven into.
///
test('the car picks up an electron it drives into, and the score says so', async ({ page }) => {
    await startAndLand(page);

    await expect(page.locator('#gameScore')).toHaveText('0');
    await expect.poll(async () => (await game(page)).electrons).toBeGreaterThan(0);

    await electronAhead(page, 3);

    await page.keyboard.down('KeyW');

    await expect.poll(async () => placedGone(page), { timeout: 10000 }).toBe(true);

    await page.keyboard.up('KeyW');

    await expect(page.locator('#gameScore')).not.toHaveText('0');
});

///
///In the plane the gun shoots an electron down: F holds the trigger - the mouse's button is the other - and a round
///passing close to one takes it, for the same points. The crosshair is the plane's, and not there in the car.
///
test('the plane\'s gun shoots an electron down, and the crosshair is the plane\'s', async ({ page }) => {
    await startAndLand(page);

    await expect(page.locator('#gameCrosshair')).toBeHidden();

    await page.keyboard.press('KeyP');

    await expect(page.locator('#gameCrosshair')).toBeVisible();

    await electronAhead(page, 12, 0, true);

    await page.keyboard.down('KeyF');

    await expect.poll(async () => placedGone(page), { timeout: 5000 }).toBe(true);

    await page.keyboard.up('KeyF');

    await expect(page.locator('#gameScore')).toHaveText('10');
});

///
///The jet's missile goes after the electron in the bracket: one off to the side of the nose, inside the missile's cone,
///is bracketed, and the missile G fires turns after it and takes it.
///
test('the jet\'s missile goes after the electron in the bracket', async ({ page }) => {
    await startAndLand(page);

    await page.locator('#gameVehicle').selectOption('jet');

    await expect(page.locator('#gameHud')).toHaveClass(/gameHudJet/);
    await expect(page.locator('#gameLock')).toBeHidden();

    await electronAhead(page, 25, 0.15, true);

    await expect(page.locator('#gameLock')).toBeVisible();

    await page.keyboard.press('KeyG');

    await expect.poll(async () => placedGone(page), { timeout: 8000 }).toBe(true);
    await expect(page.locator('#gameScore')).toHaveText('10');
    await expect(page.locator('#gameLock')).toBeHidden();
});

///Shift lights the jet's afterburner: the flame out of its nozzle twice as long and more, and back when let go.
test('Shift lights the jet\'s afterburner, and its flame says so', async ({ page }) => {
    await startAndLand(page);

    await page.locator('#gameVehicle').selectOption('jet');

    await expect(page.locator('#gameHud')).toHaveClass(/gameHudJet/);

    const flame = async () => page.evaluate(() => window.__gdsScene.getObjectByName('chipGameFlame').scale.x);

    await expect.poll(flame).toBeLessThan(1.2);

    await page.keyboard.down('ShiftLeft');

    await expect.poll(flame).toBeGreaterThan(1.4);

    await page.keyboard.up('ShiftLeft');

    await expect.poll(flame).toBeLessThan(1.2);
});

///Counts what the running game fires from here on, by kind, in window.__fired.
async function countFired(page) {
    await page.evaluate(() => {
        const running = window.__gdsScene.userData.chipGame;
        const hear = running.hear.bind(running);

        window.__fired = { bullet: 0, missile: 0 };

        running.hear = event => {
            if (event.type === 'fired')
                window.__fired[event.kind]++;

            hear(event);
        };
    });
}

///What has been fired since countFired, and the count started again.
async function firedSince(page) {
    return page.evaluate(() => {
        const fired = window.__fired;

        window.__fired = { bullet: 0, missile: 0 };

        return fired;
    });
}

///
///The jet has the plane's gun as well as its missiles, each on a button of the mouse - the main one the gun, the other
///one a missile - and on a key, F and G. **The other button brings up no menu** over the view while the game runs: the
///orbit controls keep the browser's off the view, and the game turns them off.
///
test('in the jet the main button fires the gun and the other a missile, and no menu comes up', async ({ page }) => {
    await startAndLand(page);

    await page.locator('#gameVehicle').selectOption('jet');

    await expect(page.locator('#gameHud')).toHaveClass(/gameHudJet/);

    await countFired(page);

    const middle = await viewMiddle(page);

    await page.mouse.move(middle.x, middle.y);

    await page.mouse.down();
    await expect.poll(async () => page.evaluate(() => window.__fired.bullet)).toBeGreaterThan(2);
    await page.mouse.up();

    expect((await firedSince(page)).missile).toBe(0);

    await page.mouse.down({ button: 'right' });
    await expect.poll(async () => page.evaluate(() => window.__fired.missile)).toBeGreaterThan(0);
    await page.mouse.up({ button: 'right' });

    expect((await firedSince(page)).bullet).toBe(0);

    //And no menu: the press is the game's.
    const menu = await page.evaluate(() => {
        const asked = new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 });

        document.querySelector('#container canvas').dispatchEvent(asked);

        return asked.defaultPrevented;
    });

    expect(menu).toBe(true);

    //The keys the same: F the gun, G a missile.
    await page.keyboard.down('KeyF');
    await expect.poll(async () => page.evaluate(() => window.__fired.bullet)).toBeGreaterThan(2);
    await page.keyboard.up('KeyF');

    expect((await firedSince(page)).missile).toBe(0);

    //Held until one goes, as F is: a press of a frame or two could land inside the reload of the missile the other
    //button fired a moment ago, and fire nothing - which a slow machine made a failure now and then.
    await page.keyboard.down('KeyG');
    await expect.poll(async () => page.evaluate(() => window.__fired.missile)).toBeGreaterThan(0);
    await page.keyboard.up('KeyG');

    expect((await firedSince(page)).bullet).toBe(0);
});

///
///The size is named for what is being played - Car size, Plane size or Jet size - the way the keys above it are shown for
///it. It said Car size in the plane and the jet.
///
test('the size is named for what is being played', async ({ page }) => {
    await startAndLand(page);

    const named = page.locator('label[for="gameCarSize"]');

    await expect(named).toHaveText('Car size', { useInnerText: true });

    await page.locator('#gameVehicle').selectOption('plane');

    await expect(named).toHaveText('Plane size', { useInnerText: true });

    await page.locator('#gameVehicle').selectOption('jet');

    await expect(named).toHaveText('Jet size', { useInnerText: true });
});

///The backdrop the view's settings have ticked - opened by the cube, and put away again by the pointer leaving them.
async function backdropTicked(page) {
    await page.locator('#viewPick3D').hover();

    const ticked = await page.locator('#viewBackground .backgroundPick.shapePickOn').getAttribute('data-background');

    await page.mouse.move(5, 300);
    await expect(page.locator('#viewSettingsMenu')).toHaveCount(0);

    return ticked;
}

///The backdrop the session keeps, from where it was last written.
async function keptBackdrop(page) {
    const session = await storedSession(page);

    if (session === '')
        return null;

    return JSON.parse(session).g;
}

///
///A game starts against Neon, and stopping puts the view's own backdrop back - whatever the view's settings chose while
///it ran, which was the game's - the way it puts back the view's draw distance and textures. **The session keeps the
///view's own** all the while.
///
test('a game starts against Neon, and stopping puts the view\'s own backdrop back', async ({ page }) => {
    //The view's own, before the game: White.
    await chooseBackdrop(page, 'background1.jpg');

    await expect.poll(async () => keptBackdrop(page)).toBe('background1.jpg');

    await startAndLand(page);

    expect(await backdropTicked(page)).toBe('background4.jpg');

    //Another one chosen while it runs is the game's too, and written as the view's own it is not.
    await chooseBackdrop(page, 'background2.jpg');

    expect(await backdropTicked(page)).toBe('background2.jpg');

    await page.waitForTimeout(1000);

    expect(await keptBackdrop(page)).toBe('background1.jpg');

    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    expect(await backdropTicked(page)).toBe('background1.jpg');
    expect(await keptBackdrop(page)).toBe('background1.jpg');
});

///
///**A game stopped before its Neon has come keeps the view's own backdrop**: the picture arriving afterwards is not put
///up over it. The last backdrop asked for is the one that stands. The picture is held back here until the game has
///stopped, which on a slow connection is how it comes anyway.
///
test('a game stopped before its Neon has come does not have it land afterwards', async ({ page }) => {
    let release = null;

    const held = new Promise(resolve => {
        release = resolve;
    });

    await page.route('**/background4.jpg', async route => {
        await held;
        await route.continue();
    });

    await startAndLand(page);

    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    expect(await backdropTicked(page)).toBe('none');

    const came = page.waitForResponse(response => response.url().includes('background4.jpg'));

    release();

    await came;

    //Its time to be decoded and drawn, which is when it would have been put up.
    await page.waitForTimeout(1000);

    expect(await page.evaluate(() => window.__gdsScene.background == null)).toBe(true);
});

///The electrons are yellow - they were blue - and the gun's rounds red, so what is fired is not the color of its mark.
test('the electrons are yellow, and the gun\'s rounds red', async ({ page }) => {
    await startAndLand(page);

    const colors = await page.evaluate(() => {
        const scene = window.__gdsScene;
        const electron = scene.getObjectByName('chipGameElectrons').material.color;
        const round = scene.getObjectByName('chipGameRounds').material.color;

        return { electron: [electron.r, electron.g, electron.b], round: [round.r, round.g, round.b] };
    });

    expect(colors.electron[0]).toBeGreaterThan(0.9);
    expect(colors.electron[1]).toBeGreaterThan(0.6);
    expect(colors.electron[2]).toBeLessThan(0.3);

    expect(colors.round[0]).toBeGreaterThan(0.9);
    expect(colors.round[1]).toBeLessThan(0.3);
    expect(colors.round[2]).toBeLessThan(0.3);
});

///
///The jet has a HUD: its pitch ladder, heading, speed and height drawn in green in the middle of the view - the middle
///the mouse's stick is held from - and not in the car or the plane. It says what the jet is doing.
///
///**In first person only, and there in place of the white crosshair**: the glass is the jet's sight from the cockpit,
///and behind the jet the crosshair is, with no HUD over it. They were both up, the HUD in the middle of the view in
///third person where the nose is not.
///
test('the jet\'s HUD is its sight in first person, and behind it the crosshair is', async ({ page }) => {
    await startAndLand(page);

    const hud = page.locator('#gameJetHud');
    const crosshair = page.locator('#gameCrosshair');

    await expect(hud).toBeHidden();

    await page.locator('#gameVehicle').selectOption('plane');

    await expect(page.locator('#gameHud')).toHaveClass(/gameHudPlane/);
    await expect(hud).toBeHidden();

    await page.locator('#gameVehicle').selectOption('jet');

    //Behind the jet: the crosshair, and no HUD.
    await expect(page.locator('#gameView')).toHaveValue('third');
    await expect(crosshair).toBeVisible();
    await expect(hud).toBeHidden();

    //From the cockpit: the HUD, and no crosshair.
    await page.keyboard.press('KeyV');

    await expect(page.locator('#gameView')).toHaveValue('first');
    await expect(hud).toBeVisible();
    await expect(crosshair).toBeHidden();

    //Green lines on it.
    await expect.poll(async () => page.evaluate(() => {
        const canvas = document.getElementById('gameJetHud');
        const pixels = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;

        let green = 0;

        for (let i = 0; i < pixels.length; i += 4) {
            if (pixels[i + 1] > 200 && pixels[i] < 170 && pixels[i + 3] > 150)
                green++;
        }

        return green;
    })).toBeGreaterThan(300);

    //In the middle of the view.
    const glass = await hud.boundingBox();
    const middle = await viewMiddle(page);

    expect(Math.abs((glass.x + (glass.width / 2)) - middle.x)).toBeLessThan(1.5);
    expect(Math.abs((glass.y + (glass.height / 2)) - middle.y)).toBeLessThan(1.5);

    //Saying the jet's own nose and bank, in degrees, and its heading as a compass would.
    const said = await page.evaluate(() => {
        const running = window.__gdsScene.userData.chipGame;

        return { hud: running.hudSaid, pitch: running.player.pitch, roll: running.player.roll, heading: running.player.heading };
    });

    expect(said.hud.pitch).toBeCloseTo(said.pitch * 180 / Math.PI, 6);
    expect(said.hud.roll).toBeCloseTo(said.roll * 180 / Math.PI, 6);
    expect(said.hud.heading).toBe((((Math.round((said.heading * 180 / Math.PI) - 90)) % 360) + 360) % 360);
    expect(said.hud.speed).toBeGreaterThan(0);

    //Back behind it, the other way round again.
    await page.keyboard.press('KeyV');

    await expect(page.locator('#gameView')).toHaveValue('third');
    await expect(hud).toBeHidden();
    await expect(crosshair).toBeVisible();

    //And in the car, neither.
    await page.locator('#gameVehicle').selectOption('car');

    await expect(hud).toBeHidden();
    await expect(crosshair).toBeHidden();
});

///
///The music plays the tune of what is being played - synthwave in the car, 8-bit in the plane and the jet - and the
///panel's Music turns it off, as the sound does. On to begin with. What a page plays is nothing a spec can hear, so this
///reads which tune the game's music says it is playing, and counts the notes it makes.
///
test('the music plays the tune of what is being played, and Music turns it off', async ({ page }) => {
    await page.evaluate(() => {
        window.__oscillators = 0;

        const make = BaseAudioContext.prototype.createOscillator;

        BaseAudioContext.prototype.createOscillator = function () {
            window.__oscillators++;

            return make.call(this);
        };
    });

    await startAndLand(page);

    const playing = async () => page.evaluate(() => window.__gdsScene.userData.chipGame.sound.music.playing);
    const made = async () => page.evaluate(() => window.__oscillators);

    await expect(page.locator('#gameMusic')).toBeChecked();
    await expect.poll(playing).toBe('car');

    //Notes being made for it, a beat at a time.
    const before = await made();

    await expect.poll(made).toBeGreaterThan(before + 10);

    await page.locator('#gameVehicle').selectOption('plane');
    await expect.poll(playing).toBe('plane');

    await page.locator('#gameVehicle').selectOption('jet');
    await expect.poll(playing).toBe('jet');

    await page.locator('#gameMusic').uncheck();
    await expect.poll(playing).toBe(null);

    //And none made for it once it is off.
    await page.waitForTimeout(300);

    const stopped = await made();

    await page.waitForTimeout(1000);

    expect(await made()).toBe(stopped);

    await page.locator('#gameMusic').check();
    await expect.poll(playing).toBe('jet');

    //The sound off silences it with everything else.
    await page.keyboard.press('KeyM');
    await expect.poll(playing).toBe(null);

    await page.keyboard.press('KeyM');
    await expect.poll(playing).toBe('jet');
});

///
///The jet's engine is its own - a turbine's whine over the roar - and the car's is silent in it. It was the car's engine
///pitched up under the roar, and heard as the car and the jet playing at once. What a page plays is nothing a spec can
///hear, so this reads how loud each is set to be.
///
test('in the jet the car\'s engine is silent, and the jet\'s own whine plays', async ({ page }) => {
    await startAndLand(page);

    const levels = async () => page.evaluate(() => {
        const sound = window.__gdsScene.userData.chipGame.sound;

        return { car: sound.voice.gain.value, jet: sound.whine.gain.value };
    });

    await expect.poll(async () => (await levels()).car).toBeGreaterThan(0.9);

    expect((await levels()).jet).toBeLessThan(0.001);

    await page.locator('#gameVehicle').selectOption('jet');

    await expect.poll(async () => (await levels()).car).toBeLessThan(0.01);
    await expect.poll(async () => (await levels()).jet).toBeGreaterThan(0.01);

    await page.locator('#gameVehicle').selectOption('plane');

    await expect.poll(async () => (await levels()).car).toBeGreaterThan(0.9);
    await expect.poll(async () => (await levels()).jet).toBeLessThan(0.001);
});

///
///**The jet's gun is an A-10's**: a growl for as long as the trigger is held - the GAU-8's BRRRT, its rounds too close
///together to be heard one at a time - which goes when it is let go of. The plane's gun cracks a round at a time instead,
///and neither is heard with the sound off. What a page plays is nothing a spec can hear, so this reads whether the
///game's sound says the gun is going, and the gun's loudness off its node.
///
test('the jet\'s gun growls for as long as the trigger is held, and the plane\'s does not', async ({ page }) => {
    await startAndLand(page);

    const gun = async () => page.evaluate(() => {
        const sound = window.__gdsScene.userData.chipGame.sound;

        return { firing: sound.firing, loud: sound.cannon.gain.value };
    });

    await page.locator('#gameVehicle').selectOption('jet');

    expect((await gun()).firing).toBe(false);

    await page.keyboard.down('KeyF');

    await expect.poll(async () => (await gun()).loud).toBeGreaterThan(0.1);

    expect((await gun()).firing).toBe(true);

    await page.keyboard.up('KeyF');

    await expect.poll(async () => (await gun()).loud).toBeLessThan(0.01);

    expect((await gun()).firing).toBe(false);

    //The plane's gun is a crack a round, and no growl.
    await page.locator('#gameVehicle').selectOption('plane');
    await page.keyboard.down('KeyF');
    await page.waitForTimeout(300);

    expect((await gun()).firing).toBe(false);

    await page.keyboard.up('KeyF');

    //And with the sound off, the jet's is not heard either.
    await page.locator('#gameVehicle').selectOption('jet');
    await page.keyboard.press('KeyM');

    await expect(page.locator('#gameSound')).not.toBeChecked();

    await page.keyboard.down('KeyF');
    await page.waitForTimeout(300);

    expect((await gun()).firing).toBe(false);

    await page.keyboard.up('KeyF');
});

///How full a weapon's bar on the heat meter is, in percent - `part` gameHeatGun or gameHeatMissiles.
async function heatShown(page, part) {
    return page.locator(`#${part} .gameHeatFill`).evaluate(fill => parseFloat(fill.style.width) || 0);
}

///
///**Three seconds of fire overheats a weapon** - see chipGame's warm - and the heat is under the score: a bar for the
///gun in the plane, and one for the missiles too in the jet, red and pulsing as it cools from overheated. **The jet's
///gun stops growling** as it overheats, its trigger still held. From the cockpit the HUD says it instead of the meter.
///
test('the weapons\' heat is under the score, and the jet\'s gun overheated stops growling', async ({ page }) => {
    await startAndLand(page);

    const meter = page.locator('#gameHeat');
    const gunPart = page.locator('#gameHeatGun');
    const missilePart = page.locator('#gameHeatMissiles');

    await expect(meter).toBeHidden();

    await page.locator('#gameVehicle').selectOption('plane');

    await expect(meter).toBeVisible();
    await expect(gunPart).toBeVisible();
    await expect(missilePart).toBeHidden();

    await page.locator('#gameVehicle').selectOption('jet');

    await expect(missilePart).toBeVisible();
    await expect(gunPart).not.toHaveClass(/gameHeatHot/);

    //Firing fills the gun's bar, and not the missiles'.
    await page.keyboard.down('KeyF');

    await expect.poll(async () => heatShown(page, 'gameHeatGun')).toBeGreaterThan(3);

    expect(await heatShown(page, 'gameHeatMissiles')).toBe(0);

    //Most of the way there, and on over: red, and the gun quiet with its trigger still held.
    await page.evaluate(() => {
        window.__gdsScene.userData.chipGame.player.gun.heat = 0.97;
    });

    await expect(gunPart).toHaveClass(/gameHeatHot/);
    await expect.poll(async () => page.evaluate(() => window.__gdsScene.userData.chipGame.sound.cannon.gain.value)).toBeLessThan(0.01);

    expect(await page.evaluate(() => window.__gdsScene.userData.chipGame.sound.firing)).toBe(false);

    await page.keyboard.up('KeyF');

    //From the cockpit: no meter, and the HUD saying the gun is hot.
    await page.keyboard.press('KeyV');

    await expect(page.locator('#gameView')).toHaveValue('first');
    await expect(meter).toBeHidden();
    await expect.poll(async () => page.evaluate(() => {
        const said = window.__gdsScene.userData.chipGame.hudSaid;

        return said != null && said.gunHot === true && said.gunHeat > 0.5 && said.missileHot === false;
    })).toBe(true);

    await page.keyboard.press('KeyV');

    await expect(meter).toBeVisible();
});

///
///**A missile away is heard over the jet's engine flat out**: louder at its height by about eight decibels, through a band
///from 500 Hz to 8 kHz - an ear's hearing, near enough. It was a whoosh ten under the engine, and lost under it. Off the
///game's own sounds, rendered offline, since what a page plays is nothing a spec can hear.
///
test('a missile away is heard over the jet\'s engine flat out', async ({ page }) => {
    await startAndLand(page);

    const louder = await page.evaluate(async () => {
        const GameSound = window.__gdsScene.userData.chipGame.sound.constructor;
        const rate = 48000;

        //A second and a half of what `play` starts, through that band: its loudest 85 ms and the mean of them all.
        async function heard(play) {
            const context = new OfflineAudioContext(1, rate * 1.5, rate);
            const sound = new GameSound(context);
            const low = context.createBiquadFilter();
            const high = context.createBiquadFilter();

            low.type = 'highpass';
            low.frequency.value = 500;
            high.type = 'lowpass';
            high.frequency.value = 8000;

            sound.master.disconnect();
            sound.master.connect(low);
            low.connect(high);
            high.connect(context.destination);
            sound.master.gain.value = 0.55;

            play(context, sound);

            const data = (await context.startRendering()).getChannelData(0);
            const stretch = Math.round(rate * 0.085);
            const powers = [];

            for (let start = Math.round(rate * 0.3); start + stretch <= data.length; start += stretch / 4) {
                let sum = 0;

                for (let i = start; i < start + stretch; i++)
                    sum += data[i] * data[i];

                powers.push(sum / stretch);
            }

            return { most: Math.max(...powers), mean: powers.reduce((a, b) => a + b, 0) / powers.length };
        }

        const engine = await heard((context, sound) => sound.update(true, 1, 1, 0, 'jet', 0));
        const missile = await heard((context, sound) => {
            context.suspend(0.5).then(() => {
                sound.whoosh();
                context.resume();
            });
        });

        return 10 * Math.log10(missile.most / engine.mean);
    });

    expect(louder).toBeGreaterThan(5);
});

///How many of the layers' slabs there are in the scene, and how many are drawn with their surfaces on - see surfaceOf.
async function surfaced(page) {
    return page.evaluate(() => {
        const slabs = [];

        window.__gdsScene.traverse(object => {
            if (object.isMesh && object.userData.words != null)
                slabs.push(object);
        });

        return { slabs: slabs.length, on: slabs.filter(slab => slab.material.userData.surfaced === true).length };
    });
}

///
///Apply texture is the layers' surfaces - the grid on the tops, the shaded walls and the polished metal: the 3D view's
///Show textures, which a game ticks as it starts. Off when it is unticked, and off again when the game stops, since the
///view's own is off. **Every game starts with them on**, unticked in the last or not - a game is played with them. And
///the shader they are drawn with compiles, which nothing but a browser can say.
///
test('Apply texture puts the layers\' surfaces on while the game runs, and stopping takes them off', async ({ page }) => {
    const problems = [];

    page.on('console', message => {
        if (message.type() === 'error' && /shader|WebGLProgram/i.test(message.text()))
            problems.push(message.text());
    });

    const plain = await surfaced(page);

    expect(plain.slabs).toBeGreaterThan(0);
    expect(plain.on).toBe(0);

    await startAndLand(page);

    const texture = page.locator('#gameTexture');

    await expect(texture).toBeChecked();
    await expect.poll(async () => (await surfaced(page)).on).toBe(plain.slabs);

    await texture.uncheck();

    await expect.poll(async () => (await surfaced(page)).on).toBe(0);

    await texture.check();

    await expect.poll(async () => (await surfaced(page)).on).toBe(plain.slabs);

    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    expect((await surfaced(page)).on).toBe(0);

    //Unticked and out of the game, the next one starts with them on all the same.
    await startAndLand(page);
    await texture.uncheck();
    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    await startAndLand(page);

    await expect(texture).toBeChecked();
    await expect.poll(async () => (await surfaced(page)).on).toBe(plain.slabs);

    //A frame drawn with them, for the shader to have compiled and said so if it would not.
    await page.waitForTimeout(500);

    expect(problems).toEqual([]);
});

///
///What the game's neon has on the scene now - see Viewer3D.setNeon: how many slabs there are, how many have their edges
///lit and their grid, the brightest of the edges, whether the scene is drawn through the glow, whether there is a haze,
///how dim the backdrop is, and how bright the electrons' glow.
///
async function neon(page) {
    return page.evaluate(() => {
        const scene = window.__gdsScene;
        const slabs = [];

        scene.traverse(object => {
            if (object.isMesh && object.userData.words != null)
                slabs.push(object);
        });

        const edges = slabs.flatMap(slab => slab.children.filter(child => child.name === 'neonEdges'));
        const game = scene.userData.chipGame;
        const electrons = scene.getObjectByName('chipGameGlow');

        let glow = 0;

        if (electrons != null)
            glow = Math.max(electrons.material.color.r, electrons.material.color.g, electrons.material.color.b);

        return {
            slabs: slabs.length,
            edged: edges.length,
            gridded: slabs.filter(slab => slab.material.userData.surface.surfaceNeon.value === 1).length,
            brightest: Math.max(0, ...edges.map(edge => Math.max(edge.material.color.r, edge.material.color.g, edge.material.color.b))),
            glow: game != null && game.viewer.neonGlow != null,
            haze: scene.fog != null && scene.fog.isFogExp2 === true,
            backdrop: scene.backgroundIntensity,
            electrons: glow
        };
    });
}

///
///**Neon glow** (2026-11-01, asked for): every game starts with it on - the layers' edges lines of light and their grid lit
///in their own colors, past white for the glow the scene is drawn through, the electrons lit for it too, a haze over the
///distance and the backdrop dimmed. Unticked, all of it comes off, and the next game is played without it; ticked, it all
///comes back. Stopping the game takes it off the view, which has no neon of its own. And the shaders it is drawn with
///compile, which nothing but a browser can say.
///
test('Neon glow lights the layers\' edges and draws the scene through a glow, and unticked the game is played plain', async ({ page }) => {
    const problems = [];

    page.on('console', message => {
        if (message.type() === 'error' && /shader|WebGLProgram|framebuffer/i.test(message.text()))
            problems.push(message.text());
    });

    //As a game starts the first time - the beforeEach has it off for the rest.
    await page.evaluate(() => localStorage.removeItem('gdsviewer.game'));

    await startAndLand(page);

    const box = page.locator('#gameNeon');

    await expect(box).toBeChecked();

    await expect.poll(async () => {
        const now = await neon(page);

        return now.edged === now.slabs && now.gridded === now.slabs && now.glow && now.haze;
    }).toBe(true);

    const lit = await neon(page);

    expect(lit.slabs).toBeGreaterThan(0);
    expect(lit.brightest).toBeGreaterThan(1);
    expect(lit.electrons).toBeGreaterThan(1);
    expect(lit.backdrop).toBeLessThan(1);

    //The viewer, to be asked once the game has gone and taken the way to it with it.
    await page.evaluate(() => {
        window.__neonViewer = window.__gdsScene.userData.chipGame.viewer;
    });

    await box.uncheck();

    await expect.poll(async () => {
        const now = await neon(page);

        return now.edged + now.gridded;
    }).toBe(0);

    const plain = await neon(page);

    expect(plain.glow).toBe(false);
    expect(plain.haze).toBe(false);
    expect(plain.backdrop).toBe(1);
    expect(plain.electrons).toBeLessThanOrEqual(1);

    //Kept, so the next game is played without it.
    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    await startAndLand(page);

    await expect(box).not.toBeChecked();

    expect((await neon(page)).edged).toBe(0);

    await box.check();

    await expect.poll(async () => (await neon(page)).edged).toBe(lit.slabs);

    //A few frames drawn through the glow, for its shaders to have compiled and said so if they would not.
    await page.waitForTimeout(500);

    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    const after = await page.evaluate(() => {
        const viewer = window.__neonViewer;

        let edged = 0;

        window.__gdsScene.traverse(object => {
            if (object.name === 'neonEdges')
                edged++;
        });

        return { neon: viewer.neon, glow: viewer.neonGlow != null, edged: edged, backdrop: window.__gdsScene.backgroundIntensity };
    });

    expect(after).toEqual({ neon: false, glow: false, edged: 0, backdrop: 1 });
    expect(problems).toEqual([]);
});

///
///The view as the page shows it, read back in the page: how bright it is on average and at its brightest, as 0 to 255 -
///the canvas and nothing else, with the game's panel put away. A spec cannot read the canvas itself, which the browser
///lets go of once a frame is shown.
///
async function pictured(page) {
    const picture = await page.locator('#container canvas').screenshot();

    return page.evaluate(async data => {
        const image = new Image();

        image.src = `data:image/png;base64,${data}`;

        await image.decode();

        const canvas = document.createElement('canvas');

        canvas.width = image.width;
        canvas.height = image.height;

        const pen = canvas.getContext('2d');

        pen.drawImage(image, 0, 0);

        const pixels = pen.getImageData(0, 0, canvas.width, canvas.height).data;

        let sum = 0;
        let brightest = 0;

        for (let i = 0; i < pixels.length; i += 4) {
            const light = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;

            sum += light;
            brightest = Math.max(brightest, light);
        }

        return { mean: sum / (pixels.length / 4), brightest: brightest };
    }, picture.toString('base64'));
}

///
///**And it is on the canvas**: the glow draws the scene rather than a black or an empty picture, darker over all than the
///game played plain - the backdrop dimmed and the faces with it - with lines of light brighter than anything in it.
///
test('the neon is what the canvas shows: darker over all than plain, and lit by lines brighter than anything in it', async ({ page }) => {
    await page.evaluate(() => localStorage.removeItem('gdsviewer.game'));

    await startAndLand(page);

    await page.locator('#gameHudClose').click();

    await expect(page.locator('#gameHud')).toBeHidden();

    //Neon's backdrop up and the camera settled behind the car.
    await expect.poll(async () => page.evaluate(() => window.__gdsScene.background != null)).toBe(true);
    await page.waitForTimeout(1000);

    const lit = await pictured(page);

    await openSettings(page);
    await page.locator('#gameControlsItem').click();
    await page.locator('#gameNeon').uncheck();
    await page.locator('#gameHudClose').click();

    await expect.poll(async () => (await neon(page)).glow).toBe(false);
    await page.waitForTimeout(1000);

    const plain = await pictured(page);

    expect(lit.mean).toBeGreaterThan(15);
    expect(lit.mean).toBeLessThan(plain.mean * 0.85);
    expect(lit.brightest).toBeGreaterThan(200);
});

///
///The panel's × puts it away, the game going on without it, and Settings, Game controls brings it back. Put away, it
///stays put away for the next game, since this browser keeps that with the rest of the panel.
///
test('the panel\'s × puts it away, and Settings, Game controls brings it back', async ({ page }) => {
    await startAndLand(page);

    await page.locator('#gameHudClose').click();

    await expect(page.locator('#gameHud')).toBeHidden();

    const parked = await car(page);

    await page.keyboard.down('KeyW');

    await expect.poll(async () => {
        const rolled = await car(page);

        return Math.hypot(rolled.x - parked.x, rolled.z - parked.z);
    }).toBeGreaterThan(0);

    await page.keyboard.up('KeyW');

    await openSettings(page);
    await page.locator('#gameControlsItem').click();

    await expect(page.locator('#gameHud')).toBeVisible();
    await expect(page.locator('#settingsPicker')).toHaveCount(0);

    await page.locator('#gameHudClose').click();

    await expect(page.locator('#gameHud')).toBeHidden();

    await page.keyboard.press('Escape');

    await expect(page.locator('#gameHud')).toHaveCount(0);

    //Not in the menu without a game to show it for.
    await openSettings(page);

    await expect(page.locator('#gameControlsItem')).toHaveCount(0);

    await page.locator('#playGameItem').click();

    await expect(page.locator('#gameHud')).toHaveCount(1);
    await expect(page.locator('#gameHud')).toBeHidden();

    await openSettings(page);
    await page.locator('#gameControlsItem').click();

    await expect(page.locator('#gameHud')).toBeVisible();
});

test.describe('in a short window', () => {
    test.use({ viewport: { width: 1100, height: 560 } });

    ///
    ///**The options stay in reach**: the panel is no taller than the view, and gives up the last of its keys before any of
    ///its options. Run off the foot of the view, the last rows - the sound and the music - were out of reach.
    ///
    test('the panel\'s options stay inside the view, the music among them', async ({ page }) => {
        await startAndLand(page);

        await page.locator('#gameVehicle').selectOption('jet');

        await expect(page.locator('#gameHud')).toHaveClass(/gameHudJet/);

        const view = await page.locator('#container canvas').boundingBox();
        const panel = await page.locator('#gameHud').boundingBox();
        const music = await page.locator('#gameMusic').boundingBox();

        expect(panel.y + panel.height).toBeLessThanOrEqual(view.y + view.height + 0.5);
        expect(music.y + music.height).toBeLessThanOrEqual(panel.y + panel.height);

        await page.locator('#gameMusic').uncheck();

        await expect(page.locator('#gameMusic')).not.toBeChecked();
    });
});

test.describe('on a touch device', () => {
    //The properties the rule keys on, as three-controls.spec.js names them for Open On Phone.
    test.use({ viewport: { width: 393, height: 851 }, hasTouch: true, isMobile: true });

    test('Play game is not offered, with no keyboard to drive with', async ({ page }) => {
        await openSettings(page);

        await expect(page.locator('#tourItem')).toBeVisible();
        await expect(page.locator('#playGameItem')).toBeHidden();
    });
});
