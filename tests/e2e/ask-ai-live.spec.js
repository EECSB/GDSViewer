//The assistant against a real model, which costs money - so it runs only when asked:
//
//    GDS_LIVE_AI=1 npx playwright test e2e/ask-ai-live.spec.js
//
//**The key comes from .env at the repository root**, read here in Node and put where Settings keeps a model before
//the page loads - the app itself never reads the file, and an ordinary run never opens it. It is never printed:
//not here, not in a trace (traces are off for this file, and there are no retries, which is when one would be
//written), and not by the app, which shows a key only as a password field.
//
//.env holds a Gemini key, so these go through the OpenAI-compatible adapter at Google's endpoint. GDS_AI_MODEL
//names the model; without it, the newest stable Flash Google lists for the key is used.
//
//What is checked is what a person would see: the rectangle asked for is drawn, a picture of the layout reaches the
//model and is described, a file sent with a question is read and paid for, a change waits for its press and shows
//what it did to the file's text, and rules described in the PDK manager come back as lines its deck reads. A
//model's words differ from run to run, so they are read for what must be in them rather than compared.
const fs = require('fs');
const path = require('path');
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, MOSFET } = require('./helpers');

const GOOGLE = 'https://generativelanguage.googleapis.com/v1beta/openai';

test.skip(!process.env.GDS_LIVE_AI, 'Live tests run only with GDS_LIVE_AI=1: they call a real model with a real key.');

test.describe.configure({ retries: 0, mode: 'serial' });

test.use({ trace: 'off', video: 'off' });

//
//The key, read out of .env without a library: NAME=value lines, comments and blanks skipped, quotes taken off.
//
//Returned and never logged. Null when there is no file or no Gemini key in it, which skips rather than fails -
//a machine without the file is not a failure of the app.
//
function geminiKey() {
    const file = path.join(__dirname, '..', '..', '.env');

    if (!fs.existsSync(file))
        return null;

    for (const line of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
        const match = /^\s*GEMINI_API_KEY\s*=\s*(.*?)\s*$/.exec(line);

        if (match == null)
            continue;

        const value = match[1].replace(/^["']|["']$/g, '');

        if (value.length > 0)
            return value;
    }

    return null;
}

//
//The model to ask: GDS_AI_MODEL, or the newest stable Flash in the list Google gives this key.
//
//Asked of Google rather than written here, because model names come and go faster than this file is edited, and a
//live test that fails because a name was retired says nothing about the app.
//
async function geminiModel(key) {
    if (process.env.GDS_AI_MODEL)
        return process.env.GDS_AI_MODEL;

    const listed = await fetch(`${GOOGLE}/models`, { headers: { Authorization: `Bearer ${key}` } });

    if (!listed.ok)
        throw new Error(`Google would not list its models for this key: ${listed.status} ${listed.statusText}`);

    const ids = (await listed.json()).data.map(model => model.id.replace(/^models\//, ''));
    let best = null;
    let bestVersion = -1;

    for (const id of ids) {
        const match = /^gemini-(\d+(?:\.\d+)?)-flash$/.exec(id);

        if (match == null)
            continue;

        const version = parseFloat(match[1]);

        if (version > bestVersion) {
            best = id;
            bestVersion = version;
        }
    }

    if (best == null)
        throw new Error(`No stable Flash model in Google's list: ${ids.join(', ')}`);

    return best;
}

let key = null;
let model = null;

test.beforeAll(async () => {
    key = geminiKey();

    if (key == null)
        return;

    model = await geminiModel(key);

    console.log(`Live AI tests: ${model}, through Google's OpenAI-compatible endpoint.`);
});

test.beforeEach(async ({ page }) => {
    test.skip(key == null, 'There is no GEMINI_API_KEY in .env.');

    //Google and this machine, and nothing else.
    await page.route(url => (url.protocol === 'http:' || url.protocol === 'https:')
        && url.hostname !== 'localhost'
        && url.hostname !== 'generativelanguage.googleapis.com', route => route.abort('blockedbyclient'));

    const saved = { Live: { Name: 'Live', ProviderType: 'Gemini', ApiKey: key, Model: model, MaxTokens: 8192 } };

    await page.addInitScript(stored => {
        try {
            window.localStorage.setItem('gdsviewer.llmConnections', 'r' + stored);
        }
        catch (e) { }
    }, JSON.stringify(saved));
});

async function ask(page, text) {
    //Opened unless it already is: the spark puts an open window away.
    if (!await page.locator('#askAiWindow').isVisible())
        await page.locator('#askAiButton').click();

    await expect(page.locator('#askAiWindow')).toBeVisible();
    await expect(page.locator('#aiModelChosen')).toHaveText('Live');

    //On the top rung, so nothing waits for a press: what is being tested is the model and the adapter.
    await page.locator('#aiMode').selectOption('AutoReadWrite');
    await page.locator('#aiPrompt').fill(text);
    await page.locator('#aiSend').click();

    //Done when the window stops thinking, however it ended.
    await expect(page.locator('#aiThinking')).toHaveCount(0, { timeout: 180000 });

    const failed = await page.locator('.aiTurnError').allInnerTexts();

    expect(failed, failed.join('\n')).toEqual([]);
}

test('a rectangle asked for in words is drawn', async ({ page }) => {
    test.setTimeout(240000);

    await gotoExample(page, MOSFET, '2d');

    const before = await shapeCount(page);

    await ask(page, 'Draw a rectangle 2 µm wide and 1 µm tall on layer 68/20, with its lower-left corner at x 10 µm, y 10 µm. Then say done.');

    await expect.poll(() => shapeCount(page), { timeout: 30000 }).toBe(before + 1);

    const drawn = page.locator('.aiCard[data-tool="draw_rectangle"]');

    await expect(drawn.first()).toContainText('Drew a 2 by 1 µm rectangle');
    await expect(page.locator('.aiBubbleAssistant').last()).not.toBeEmpty();
});

test('a picture of the layout reaches the model and is described', async ({ page }) => {
    test.setTimeout(240000);

    await gotoExample(page, MOSFET, '2d');

    await ask(page, 'Use the look tool once to see the cell, then tell me in one sentence which labels you can read in the picture.');

    await expect(page.locator('.aiCard[data-tool="look"] .aiPicture').first()).toBeVisible();

    //Mosfet's three labels are drain, source and gate; a model that saw the picture names at least one.
    const answer = (await page.locator('.aiBubbleAssistant').last().innerText()).toLowerCase();

    expect(['drain', 'source', 'gate'].some(label => answer.includes(label)), answer).toBe(true);
});

//
//**A file read with its question, what it cost, and a briefing that carries it**: a deck attached says a width no
//model could guess, the answer gives it, the usage line under the answer counts what Google billed, and after a
//compaction a question about the deck is answered from the briefing alone - the deck unticked on it.
//
test('a file attached is read, its cost is shown, and a compacted conversation still knows it', async ({ page }) => {
    test.setTimeout(480000);

    await gotoExample(page, MOSFET, '2d');

    await page.locator('#askAiButton').click();
    await page.locator('#aiAttachInput').setInputFiles({ name: 'rules.drc', mimeType: '', buffer: Buffer.from('#Our house rules\nwidth met1 0.437\nspace met1 0.291\n') });

    await expect(page.locator('#aiPending .aiChipName')).toHaveText('rules.drc');

    await ask(page, 'According to the attached rules.drc, what is the minimum met1 width? Answer with the number only.');

    await expect(page.locator('.aiBubbleAssistant').last()).toContainText('0.437');
    await expect(page.locator('.aiUsage').last()).toHaveText(/^\d+ requests? · [\d,]+ tokens in.* · [\d,]+ out/);

    await page.locator('#aiPrompt').fill('And the minimum met1 spacing? The number only.');
    await page.locator('#aiSend').click();
    await expect(page.locator('#aiThinking')).toHaveCount(0, { timeout: 180000 });

    await expect(page.locator('.aiBubbleAssistant').last()).toContainText('0.291');

    await page.locator('#aiToolPickerToggle').click();
    await page.locator('#aiCompact').click();

    await expect(page.locator('#aiThinking')).toHaveCount(0, { timeout: 180000 });
    await expect(page.locator('.aiTurnError')).toHaveCount(0);
    await expect(page.locator('.aiSummary')).toBeVisible();
    await expect(page.locator('.aiSummary + .aiUsage')).toHaveText(/tokens in/);

    console.log(`Briefing: ${await page.locator('.aiSummaryText').innerText()}`);

    await page.locator('#aiPrompt').fill('Remind me: what was the met1 width in the house rules? The number only.');
    await page.locator('#aiSend').click();
    await expect(page.locator('#aiThinking')).toHaveCount(0, { timeout: 180000 });

    await expect(page.locator('.aiBubbleAssistant').last()).toContainText('0.437');

    //On the line under the box, which is up to begin with.
    console.log(`Spent: ${await page.locator('#aiSpent').innerText()}; ${await page.locator('#aiSpent').getAttribute('title')}`);
});

//
//**A change waits for its press, then shows what it did to the file's text** (2026-10-21): on the rung the window
//opens on, the model's rectangle waits on its card with nothing drawn, Approve draws it, and the card gets a Monaco
//diff of the record dump with the boundary put in.
//
test('a change waits for Approve, then shows what it did to the file\'s text', async ({ page }) => {
    test.setTimeout(300000);

    await gotoExample(page, MOSFET, '2d');

    const before = await shapeCount(page);

    await page.locator('#askAiButton').click();

    await expect(page.locator('#aiModelChosen')).toHaveText('Live');
    await expect(page.locator('#aiMode')).toHaveValue('AutoRead');

    await page.locator('#aiPrompt').fill('Draw a rectangle 2 µm wide and 1 µm tall on layer 68/20, with its lower-left corner at x 10 µm, y 10 µm. Then say done.');
    await page.locator('#aiSend').click();

    const card = page.locator('.aiCard[data-tool="draw_rectangle"]').first();

    await expect(card.locator('.aiApprove')).toBeVisible({ timeout: 180000 });
    expect(await shapeCount(page)).toBe(before);

    await card.locator('.aiApprove').click();

    await expect(page.locator('#aiThinking')).toHaveCount(0, { timeout: 180000 });

    const failed = await page.locator('.aiTurnError').allInnerTexts();

    expect(failed, failed.join('\n')).toEqual([]);

    await expect.poll(() => shapeCount(page), { timeout: 30000 }).toBe(before + 1);

    const diff = card.locator('.aiTextDiff');

    await expect(diff.locator('.monaco-diff-editor')).toBeVisible({ timeout: 30000 });
    await expect(diff.locator('.line-insert').first()).toBeVisible();

    const shown = await diff.locator('.editor.modified .view-lines').innerText();

    expect(shown).toContain('BOUNDARY');
    expect(shown).toMatch(/LAYER:\s+68/);
});

//
//**Rules written in the AI assistant** (the PDK manager's Write with AI: a box of its own 2026-10-19, the AI assistant since 2026-10-31): a
//width no model could guess is read from the deck's grammar with read_rules and added with add_rules, as one rule the
//deck's reader takes, in nanometers - the card waiting for Approve on the rung the window opens on.
//
test('a rule described in words is written into the PDK by the AI assistant', async ({ page }) => {
    test.setTimeout(240000);

    await gotoExample(page, MOSFET, '2d');

    await page.locator('#settingsMenu').click();
    await page.locator('#pdkManagerItem').click();

    await expect(page.locator('#pdkManagerWindow')).toBeVisible();

    await page.locator('#pdkRulesTab').click();

    await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (30)');

    await page.locator('#pdkRulesAi').click();

    await expect(page.locator('#askAiWindow')).toBeVisible();

    await page.locator('#aiPrompt').fill('Add one rule to the deck: met2 at least 0.437 µm wide, with the id m2.house');
    await page.locator('#aiSend').click();

    const card = page.locator('.aiCard').filter({ hasText: 'add rules' });

    await expect(card.locator('.aiApprove')).toBeVisible({ timeout: 180000 });

    console.log(`Asked to add: ${await card.innerText()}`);

    await card.locator('.aiApprove').click();

    await expect(page.locator('#aiThinking')).toHaveCount(0, { timeout: 180000 });
    await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (31)');
    await expect(page.locator('#pdkRuleList tr[data-rule="m2.house"]')).toHaveCount(1);
    await expect(page.locator('#pdkRuleList tr[data-rule="m2.house"]')).toContainText('437');
});
