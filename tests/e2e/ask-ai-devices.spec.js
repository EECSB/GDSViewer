//
//The AI assistant's microphone set to the AI provider (2026-10-31, asked for): recorded until pressed again, sent as a 16 kHz WAV
//and written out. A file of its own because the recording needs Chromium's fake microphone - a tone, and a permission
//granted without asking - and a browser launched with it. No provider is reached: Gemini's API is answered from a script,
//and everything else off this machine is refused. The browser's own recognition, and the setting, are ask-ai.spec's.
//
const { test, expect } = require('@playwright/test');
const { gotoExample, MOSFET } = require('./helpers');

const CORS = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': '*',
    'Access-Control-Allow-Methods': '*'
};

test.use({
    launchOptions: { args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream'] },
    permissions: ['microphone']
});

test.beforeEach(async ({ page }) => {
    await page.route(url => (url.protocol === 'http:' || url.protocol === 'https:') && url.hostname !== 'localhost', route => route.abort('blockedbyclient'));

    await page.addInitScript(stored => {
        try {
            window.localStorage.setItem('gdsviewer.llmConnections', 'r' + stored);
        }
        catch (e) { }
    }, JSON.stringify({ Gemini: { Name: 'Gemini', ProviderType: 'Gemini', ApiKey: 'test-key', Model: 'gemini-test', MaxTokens: 8192 } }));
});

async function openAssistant(page) {
    await page.locator('#askAiButton').click();

    await expect(page.locator('#askAiWindow')).toBeVisible();
}

///
///**Waits for the meter to have said the microphone is loud, by the loudest it said rather than the last.** The fake
///microphone is not a steady tone: it beeps for about a fifth of a second every half second, and is silent for the first
///second. Read now and then, the latest reading could land in the gaps every time - a poll settles into one read a
///second - and the browser recognition's test failed about half its runs that way until 2026-11-03, stuck at 0.001.
///Every reading the meter publishes is watched instead.
///
async function meterHeard(page, level) {
    await level.evaluate(element => {
        window.__loudest = 0;

        new MutationObserver(() => {
            window.__loudest = Math.max(window.__loudest, Number(element.dataset.level || 0));
        }).observe(element, { attributes: true, attributeFilter: ['data-level'] });
    });

    await expect.poll(async () => page.evaluate(() => window.__loudest)).toBeGreaterThan(0.05);
}

///
///**Recorded until pressed again, and written out by Gemini**: sent as a 16 kHz WAV inline, with the key in its
///header and the layer names in the instruction, and what comes back goes into the box.
///
test('a recording goes to Gemini as a WAV, and what it writes out goes into the box', async ({ page }) => {
    await page.addInitScript(() => {
        try {
            window.localStorage.setItem('gdsviewer.speechInput', 'rProvider');
        }
        catch (e) { }
    });

    const sent = [];

    await page.route('https://generativelanguage.googleapis.com/v1beta/models/**', async route => {
        if (route.request().method() === 'OPTIONS') {
            await route.fulfill({ status: 204, headers: CORS });

            return;
        }

        sent.push({ url: route.request().url(), headers: route.request().headers(), body: route.request().postDataJSON() });

        await route.fulfill({
            status: 200,
            headers: { ...CORS, 'Content-Type': 'application/json' },
            body: JSON.stringify({ candidates: [{ content: { parts: [{ text: 'draw a met1 square' }] } }] })
        });
    });

    await gotoExample(page, MOSFET, '2d');
    await openAssistant(page);

    const mic = page.locator('#aiMic');

    await mic.click();

    await expect(mic).toHaveAttribute('aria-pressed', 'true');

    //The level meter in the box moves with the fake microphone's tone, and goes when the recording ends.
    const level = page.locator('#aiMicLevel');

    await expect(level.locator('.aiMicLevelBar')).toHaveCount(16);
    await meterHeard(page, level);

    await page.waitForTimeout(500);
    await mic.click();

    await expect(level).toHaveCount(0);

    await expect(page.locator('#aiPrompt')).toHaveValue('draw a met1 square', { timeout: 30000 });
    await expect(page.locator('#aiSpeechProblem')).toHaveCount(0);

    expect(sent).toHaveLength(1);
    expect(sent[0].url).toContain('/models/gemini-test:generateContent');
    expect(sent[0].headers['x-goog-api-key']).toBe('test-key');

    const parts = sent[0].body.contents[0].parts;

    expect(parts[0].text).toContain('met1');
    expect(parts[1].inline_data.mime_type).toBe('audio/wav');

    //RIFF, in base64: a WAV, whatever the browser recorded in.
    expect(parts[1].inline_data.data.startsWith('UklGR')).toBe(true);
});

///
///**The level meter while the browser listens** (2026-10-31, asked for, as the Claude app has one): the recognizer hands
///the page no sound, so the meter opens the microphone for itself - the fake one's tone lifts the bars - and lets it go
///when the listening ends.
///
test('the browser\'s recognition shows a level meter that moves with the microphone, and goes when it stops', async ({ page }) => {
    await page.addInitScript(() => {
        //A recognizer that hears nothing and ends only when stopped; under both names, as Chromium has both.
        window.SpeechRecognition = window.webkitSpeechRecognition = class {
            start() { }

            stop() {
                setTimeout(() => this.onend(), 10);
            }

            abort() { }
        };
    });

    await gotoExample(page, MOSFET, '2d');
    await openAssistant(page);

    const mic = page.locator('#aiMic');
    const level = page.locator('#aiMicLevel');
    const send = page.locator('#aiSend');
    const sendBefore = await send.boundingBox();

    await mic.click();

    await expect(mic).toHaveAttribute('aria-pressed', 'true');
    await expect(level.locator('.aiMicLevelBar')).toHaveCount(16);

    await meterHeard(page, level);

    //The bars read during a beep, which is when they are up.
    const tallest = await level.evaluate(async element => {
        while (Number(element.dataset.level || 0) <= 0.05)
            await new Promise(resolve => setTimeout(resolve, 10));

        return Math.max(...Array.from(element.children).map(bar => bar.getBoundingClientRect().height));
    });

    expect(tallest).toBeGreaterThan(5);

    //In the box above the row of controls, which does not move for it (asked for: it sat beside Send at first).
    const meterBox = await level.boundingBox();
    const promptBox = await page.locator('#aiPrompt').boundingBox();

    expect(await send.boundingBox()).toEqual(sendBefore);
    expect(meterBox.y + meterBox.height).toBeLessThanOrEqual(promptBox.y + promptBox.height + 1);

    await mic.click();

    await expect(mic).toHaveAttribute('aria-pressed', 'false');
    await expect(level).toHaveCount(0);
});

///
///**A picture from the camera goes with the next question** (2026-10-31, asked for): the camera live in a card over the
///box, and Capture attaches the frame on screen - Chromium's fake camera, a moving test pattern - as a JPEG named for when
///it was taken, through the paperclip's own input, and the card goes.
///
test('the camera shows live, and Capture attaches the picture to the next question', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');
    await openAssistant(page);

    await page.locator('#aiCameraToggle').click();

    await expect(page.locator('#aiCamera')).toBeVisible();
    await expect.poll(() => page.locator('#aiCameraVideo').evaluate(video => video.videoWidth), { timeout: 15000 }).toBeGreaterThan(0);
    await expect(page.locator('#aiCameraProblem')).toHaveCount(0);

    await page.locator('#aiCameraCapture').click();

    await expect(page.locator('#aiCamera')).toHaveCount(0);
    await expect(page.locator('#aiPending .aiChip')).toHaveCount(1);
    await expect(page.locator('#aiPending .aiChipName')).toHaveText(/^camera-\d{8}-\d{6}\.jpg$/);

    //Closed without one, nothing is attached.
    await page.locator('#aiCameraToggle').click();
    await page.locator('#aiCameraClose').click();

    await expect(page.locator('#aiCamera')).toHaveCount(0);
    await expect(page.locator('#aiPending .aiChip')).toHaveCount(1);
});

///
///**A screenshot goes with the next question** (2026-10-31, asked for): the browser's own picker - stood in for here by a
///drawn canvas, since a test cannot pick from it - and one frame of what was picked, as a PNG named for when it was taken,
///through the paperclip's own input. Closing the picker without one attaches nothing and says nothing.
///
test('a screenshot is attached to the next question, and a picker closed without one attaches nothing', async ({ page }) => {
    await page.addInitScript(() => {
        window.gdsRefuseShare = false;

        navigator.mediaDevices.getDisplayMedia = async () => {
            if (window.gdsRefuseShare)
                throw new DOMException('Permission denied', 'NotAllowedError');

            const canvas = document.createElement('canvas');

            canvas.width = 320;
            canvas.height = 200;

            const context = canvas.getContext('2d');

            setInterval(() => {
                context.fillStyle = '#3366cc';
                context.fillRect(0, 0, 320, 200);
            }, 20);

            return canvas.captureStream(30);
        };
    });

    await gotoExample(page, MOSFET, '2d');
    await openAssistant(page);

    await page.locator('#aiScreenshot').click();

    await expect(page.locator('#aiPending .aiChip')).toHaveCount(1);
    await expect(page.locator('#aiPending .aiChipName')).toHaveText(/^screenshot-\d{8}-\d{6}\.png$/);

    await page.evaluate(() => {
        window.gdsRefuseShare = true;
    });

    await page.locator('#aiScreenshot').click();
    await page.waitForTimeout(500);

    await expect(page.locator('#aiPending .aiChip')).toHaveCount(1);
    await expect(page.locator('#aiAttachProblem')).toHaveCount(0);
});
