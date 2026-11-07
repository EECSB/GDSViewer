//The assistant in a real browser: the window, its models, and a conversation that changes the layout.
//
//**No provider is ever reached.** Anthropic's API and Google's are intercepted and answer from a script in their
//own wire formats, and every other address off this machine is refused - so the whole loop runs, request bodies
//and CORS preflights included, without a key or a network. What the provider code sends is checked by reading
//the intercepted requests back.
//
//The conversation itself is covered without a browser in AiChatTests; what is here is what needs one: the window
//and its buttons, the 2D view drawing what the model made, Undo taking it back, and look's picture drawn by the
//browser and sent.
const fs = require('fs');
const zlib = require('zlib');
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, selectView, editorText, expectEditorLoaded, captureScene, cameraPosition, layerLabel, MOSFET } = require('./helpers');

const CORS = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': '*',
    'Access-Control-Allow-Methods': '*'
};

const ANTHROPIC = 'https://api.anthropic.com/**';
const GOOGLE = 'https://generativelanguage.googleapis.com/**';

///A model of each kind, with a key that is never spent: the providers are intercepted.
const CLAUDE = { Name: 'Claude', ProviderType: 'Anthropic', ApiKey: 'test-key', MaxTokens: 8192 };
const GEMINI = { Name: 'Gemini', ProviderType: 'Gemini', ApiKey: 'test-key', Model: 'gemini-test', MaxTokens: 8192 };

///A rectangle on Mosfet's met1, by numbers so it does not depend on the layer having a name.
const RECTANGLE = { layer: '68/20', from: [0, 0], to: [1, 1] };

//
//**Everything off this machine is refused.** A spec that went wrong could otherwise reach a real provider - and
//a browser that had a real key saved would spend it. Failing the request is loud, local and free. Registered
//first, so the fakes below, registered after, are tried before it.
//
async function blockTheOutside(page) {
    await page.route(url => (url.protocol === 'http:' || url.protocol === 'https:') && url.hostname !== 'localhost', route => route.abort('blockedbyclient'));
}

//
//A provider that answers each request with the next reply of a script, and keeps what it was sent.
//
//The preflight a browser sends before a request with headers of its own is answered too, which is what a real
//provider does for a page that may call it - Anthropic for one that sends its browser header.
//
async function fakeProvider(page, pattern, replies, status = 200) {
    const sent = [];

    await page.route(pattern, async route => {
        const request = route.request();

        if (request.method() === 'OPTIONS') {
            await route.fulfill({ status: 204, headers: CORS });

            return;
        }

        sent.push({ url: request.url(), headers: request.headers(), body: request.postDataJSON() });

        const reply = replies[Math.min(sent.length - 1, replies.length - 1)];

        await route.fulfill({
            status,
            headers: { ...CORS, 'Content-Type': 'application/json' },
            body: JSON.stringify(reply)
        });
    });

    return sent;
}

function claudeSays(text) {
    return { content: [{ type: 'text', text }], stop_reason: 'end_turn' };
}

function claudeCalls(id, name, input) {
    return { content: [{ type: 'tool_use', id, name, input }], stop_reason: 'tool_use' };
}

function geminiSays(text) {
    return { choices: [{ message: { role: 'assistant', content: text }, finish_reason: 'stop' }] };
}

function geminiCalls(id, name, args) {
    return {
        choices: [{
            message: {
                role: 'assistant',
                content: null,
                tool_calls: [{
                    id,
                    type: 'function',
                    function: { name, arguments: JSON.stringify(args) },
                    extra_content: { google: { thought_signature: 'c2lnbmVk' } }
                }]
            },
            finish_reason: 'tool_calls'
        }]
    };
}

//
//A model saved before the page loads, where Settings keeps it.
//
//Into localStorage, which the app's storage falls back to and carries into IndexedDB on the first read - the same
//way a value an older version wrote is found. 'r' is its marker for a value stored as it is.
//
async function withModel(page, ...models) {
    const saved = {};

    for (const model of models)
        saved[model.Name] = model;

    await page.addInitScript(stored => {
        try {
            window.localStorage.setItem('gdsviewer.llmConnections', 'r' + stored);
        }
        catch (e) { }
    }, JSON.stringify(saved));
}

async function openAssistant(page) {
    await page.locator('#askAiButton').click();

    await expect(page.locator('#askAiWindow')).toBeVisible();
}

async function send(page, text) {
    await page.locator('#aiPrompt').fill(text);
    await page.locator('#aiSend').click();
}

function transcript(page) {
    return page.locator('#aiTranscript');
}

test.describe('the window', () => {
    test.beforeEach(async ({ page }) => {
        await blockTheOutside(page);
    });

    ///
    ///**In the bar, after the Cells and Layers switches** - it was on the drawing beside the camera while the bar was
    ///full. Lit while its window is up, and pressed again it puts the window away. Opening it says, before anything is
    ///typed, what goes where and that nothing has.
    ///
    test('the spark in the bar opens it, and it says what is sent where', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        const spark = page.locator('.viewToolbar #askAiButton');
        const layers = await page.locator('#layersToggle').boundingBox();
        const at = await spark.boundingBox();

        expect(at.x).toBeGreaterThan(layers.x + layers.width);
        expect(Math.abs(at.y - layers.y)).toBeLessThan(2);

        //Nothing of it left on the drawing.
        await expect(page.locator('#svgWrapper #askAiButton')).toHaveCount(0);
        await expect(spark).toHaveAttribute('aria-pressed', 'false');

        await openAssistant(page);

        await expect(spark).toHaveAttribute('aria-pressed', 'true');
        await expect(transcript(page)).toContainText('is sent to the provider of the model picked below');
        await expect(transcript(page)).toContainText('Nothing is sent until you press Send');
        await expect(page.locator('#aiNoModel')).toBeVisible();

        await page.locator('#aiPrompt').fill('draw something');

        await expect(page.locator('#aiSend')).toBeDisabled();

        await spark.click();

        await expect(page.locator('#askAiWindow')).toBeHidden();
        await expect(spark).toHaveAttribute('aria-pressed', 'false');
    });

    ///The same spark in the same place over the 3D view and the text, opening the same window.
    test('the spark is in the bar over the 3D view and the text too', async ({ page }) => {
        for (const view of ['3d', 'text']) {
            await gotoExample(page, MOSFET, view);

            await expect(page.locator('.viewToolbar #askAiButton')).toBeEnabled();

            await openAssistant(page);

            await expect(transcript(page)).toContainText('Nothing is sent until you press Send');

            await page.locator('#askAiButton').click();

            await expect(page.locator('#askAiWindow')).toBeHidden();
        }
    });

    ///A model is added under Settings and offered in the chat at once; its key is a password field and goes nowhere.
    test('a model added under Settings is offered in the chat at once', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await page.locator('#settingsMenu').click();
        await page.locator('#aiSettingsItem').click();

        await expect(page.locator('#aiSettingsWindow')).toBeVisible();
        await expect(page.locator('#aiKeysKept')).toContainText('Keys are kept in this browser');

        //With none saved, the form is the one for the first, its name typed into the picker's head.
        await page.locator('#aiModelPickerName').fill('Mine');
        await page.locator('#aiModelPickerName').press('Enter');
        await page.locator('#aiProviderGemini').click();
        await page.locator('#aiModelId').fill('gemini-test');

        await expect(page.locator('#aiModelKey')).toHaveAttribute('type', 'password');

        await page.locator('#aiModelKey').fill('test-key');
        await page.locator('#aiModelSave').click();

        await expect(page.locator('#aiModelPickerFace')).toHaveText('Mine');

        //Put away, as somebody done with it would: it is a window, and it sits over whatever is under it.
        await page.locator('#aiSettingsWindow .closeButton').click();

        await expect(page.locator('#aiSettingsWindow')).toBeHidden();

        await openAssistant(page);

        await expect(page.locator('#aiModelChosen')).toHaveText('Mine');
        await expect(page.locator('#aiNoModel')).toHaveCount(0);
    });

    ///
    ///**The models' picker manages them in place** (2026-11-07, from GraphDBViewer): the chosen model's name heads it and
    ///is renamed there, each row has Rename, Duplicate and Delete, and Save joined to its end is orange only while the form
    ///holds something not saved.
    ///
    test('the models are renamed, duplicated and deleted in the picker, and Save says when there is something to save', async ({ page }) => {
        await withModel(page, GEMINI, CLAUDE);
        await gotoExample(page, MOSFET, '2d');

        await page.locator('#settingsMenu').click();
        await page.locator('#aiSettingsItem').click();

        await expect(page.locator('#aiModelPickerFace')).toHaveText('Gemini');
        await expect(page.locator('#aiModelSave')).not.toHaveClass(/savedPickerUnsaved/);

        //A change makes Save orange, and undoing it takes the orange away.
        await page.locator('#aiModelPdfAsText').check();
        await expect(page.locator('#aiModelSave')).toHaveClass(/savedPickerUnsaved/);
        await page.locator('#aiModelPdfAsText').uncheck();
        await expect(page.locator('#aiModelSave')).not.toHaveClass(/savedPickerUnsaved/);

        //Renamed in the head, where it is in the list.
        await page.locator('#aiModelPickerRename').click();
        await page.locator('#aiModelPickerName').fill('Flash');
        await page.locator('#aiModelPickerName').press('Enter');

        await expect(page.locator('#aiModelPickerFace')).toHaveText('Flash');

        await page.locator('#aiModelPickerChevron').click();

        const rows = page.locator('#aiModelPickerMenu .savedPickerRow');

        await expect(rows).toHaveText(['Flash', 'Claude']);

        //Duplicated right after itself, and chosen.
        await rows.first().hover();
        await rows.first().locator('[data-act="duplicate"]').click();

        await expect(page.locator('#aiModelPickerFace')).toHaveText('Flash (copy)');
        await expect(page.locator('#aiModelId')).toHaveValue('gemini-test');

        await page.locator('#aiModelPickerChevron').click();
        await expect(rows).toHaveText(['Flash', 'Flash (copy)', 'Claude']);

        //Deleted from its row: the chosen one goes, and the first left is chosen.
        await rows.nth(1).hover();
        await rows.nth(1).locator('[data-act="delete"]').click();

        await expect(rows).toHaveText(['Flash', 'Claude']);

        //A name taken is refused, and the head says why.
        await rows.nth(1).hover();
        await rows.nth(1).locator('[data-act="rename"]').click();
        await page.locator('#aiModelPickerName').fill('Flash');
        await page.locator('#aiModelPickerName').press('Enter');

        await expect(page.locator('#aiModelPickerError')).toHaveText('A model with that name already exists.');
    });

    test('a model is refused without the things it needs', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await page.locator('#settingsMenu').click();
        await page.locator('#aiSettingsItem').click();
        await page.locator('#aiModelPickerName').fill('Local');
        await page.locator('#aiModelPickerName').press('Enter');
        await page.locator('#aiProviderCustom').click();
        await page.locator('#aiModelId').fill('llama');
        await page.locator('#aiModelSave').click();

        await expect(page.locator('#aiModelError')).toHaveText('A server of your own needs its address.');
    });

    ///
    ///**What the models are for is behind an (i) at the end of the picker's row** (2026-11-01, asked for: bigger, and its
    ///words shown by pointing at it rather than by a press). Out of sight until then, and named to a screen reader all along.
    ///
    test('the (i) beside the models says what they are for when pointed at', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await page.locator('#settingsMenu').click();
        await page.locator('#aiSettingsItem').click();

        const info = page.locator('#aiModelsInfo');
        const words = page.locator('#aiModelsInfoPop');

        await expect(info).toBeVisible();
        await expect(info).toHaveAttribute('aria-describedby', 'aiModelsInfoPop');
        await expect(words).toBeHidden();

        //The bigger (i): 22 pixels across, where the row's other icons are 16.
        expect((await info.locator('svg').boundingBox()).width).toBeGreaterThanOrEqual(20);

        await info.hover();

        await expect(words).toBeVisible();
        await expect(words).toContainText('Add a model to turn the assistant on');

        //Away from it, the words go; a press was never what showed them.
        await page.locator('#aiModelPickerChevron').hover();

        await expect(words).toBeHidden();
    });

    ///
    ///A Development build adds the models a developer's dev-secrets.json holds under Settings, leaving one already saved
    ///under the same name as it is - and leaves a browser a test is driving alone, so no spec ever finds a real key there.
    ///The file is the test's own here, and the second visit is made to look like nobody is driving the browser.
    ///
    test('a Development build adds the models in dev-secrets.json, and not in a browser a test drives', async ({ page }) => {
        const asked = [];

        await page.route('**/dev-secrets.json', route => {
            asked.push(route.request().url());

            return route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify({
                    devSecrets: true,
                    llmConnections: {
                        'From file': { Name: 'From file', ProviderType: 'Gemini', ApiKey: 'test-key', Model: 'gemini-from-file', MaxTokens: 8192 },
                        Mine: { Name: 'Mine', ProviderType: 'Gemini', ApiKey: 'test-key', Model: 'gemini-from-file', MaxTokens: 8192 }
                    }
                })
            });
        });

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await expect(page.locator('#aiNoModel')).toBeVisible();
        expect(asked).toEqual([]);

        await withModel(page, { Name: 'Mine', ProviderType: 'Gemini', ApiKey: 'test-key', Model: 'gemini-mine', MaxTokens: 8192 });

        await page.addInitScript(() => {
            Object.defineProperty(Navigator.prototype, 'webdriver', { get: () => false, configurable: true });

            //Nobody driving is also somebody the page asks about a tour - answered already, as a returning visitor has.
            localStorage.setItem('gdsviewer.noTourQuestion', 'never');
        });

        await gotoExample(page, MOSFET, '2d');

        await expect.poll(() => asked.length).toBe(1);

        await page.locator('#settingsMenu').click();
        await page.locator('#aiSettingsItem').click();

        await page.locator('#aiModelPickerChevron').click();

        const rows = page.locator('#aiModelPickerMenu .savedPickerRow');

        await expect(rows).toHaveCount(2);
        await expect(rows.filter({ hasText: 'From file' })).toHaveCount(1);
        await expect(rows.filter({ hasText: 'Mine' })).toHaveCount(1);

        //The saved one kept what it had, rather than taking the file's.
        await page.locator('#aiModelPickerMenu .savedPickerRow[data-key="Mine"]').click();

        await expect(page.locator('#aiModelId')).toHaveValue('gemini-mine');
    });

    ///Read-only pages turn off every control that would change the file, and this is one.
    test('on a read-only page the spark is off', async ({ page }) => {
        await gotoExample(page, `${MOSFET}&mode=noedit`, '2d');

        await expect(page.locator('#askAiButton')).toBeDisabled();
        await expect(page.locator('#askAiButton')).toHaveAttribute('title', /read-only/);
    });

    ///A page that wants the drawing alone gets no assistant at all - not a spark that is off, but none.
    test('a page that is the drawing alone has no spark and no window', async ({ page }) => {
        //Not through gotoExample, which waits for the bar this page does not have.
        await page.goto(`/?file=${MOSFET}&view=2d&mode=viewer`, { waitUntil: 'domcontentloaded' });

        await expect.poll(() => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
        await expect(page.locator('#askAiButton')).toHaveCount(0);
        await expect(page.locator('#askAiWindow')).toHaveCount(0);
    });

    ///It is the page's, not the view's: switching to 3D and back keeps it up, conversation and all.
    test('the window and its conversation outlive a change of view', async ({ page }) => {
        await withModel(page, CLAUDE);
        await fakeProvider(page, ANTHROPIC, [claudeSays('Hello.')]);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);
        await send(page, 'Hi');

        await expect(transcript(page)).toContainText('Hello.');

        await selectView(page, 'View3D');

        await expect(page.locator('#askAiWindow')).toBeVisible();

        await selectView(page, 'View2DSvg');

        await expect(transcript(page)).toContainText('Hello.');
    });

    ///
    ///**The row along the box's foot is one line** at the size the window opens at: the paperclip, the mode, the model
    ///with its effort, the (i) and Send - inside the box's frame since 2026-10-31. At 560 half of it went to a line of its
    ///own - so here with a model whose name is long enough for the button to cut it off, the widest the row gets, and one
    ///that thinks, so the effort is there too.
    ///
    test('the row along the box\'s foot is one line at the size the window opens at', async ({ page }) => {
        await withModel(page, { Name: 'A model with a name long enough to be cut off', ProviderType: 'Gemini', ApiKey: 'test-key', Model: 'gemini-3.8-flash', MaxTokens: 8192 });
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await expect(page.locator('#aiModelEffort')).toBeVisible();

        const middles = await page.evaluate(() => ['#aiAttach', '#aiSend', '#aiMode', '#aiToolPickerToggle', '#aiModelMenuToggle', '#aiMeterToggle']
            .map(selector => {
                const box = document.querySelector(selector).getBoundingClientRect();

                return box.top + (box.height / 2);
            }));

        //One line: every control's middle within a few pixels of the others', where a second line is a row lower.
        expect(Math.max(...middles) - Math.min(...middles)).toBeLessThan(6);

        //Inside the box's frame, under the words.
        const frame = await page.locator('#askAiWindow .aiBox').boundingBox();
        const words = await page.locator('#aiPrompt').boundingBox();
        const send = await page.locator('#aiSend').boundingBox();

        expect(send.y).toBeGreaterThan(words.y + words.height - 1);
        expect(send.y + send.height).toBeLessThanOrEqual(frame.y + frame.height);

        //And nothing in the title bar but the cross: the tool picker's switch is in this row, the panels' crosses in the
        //panels, New chat in the chats (2026-10-31), and Export and Clear on the chats' rows.
        await expect(page.locator('#askAiWindow .aiBox #aiToolPickerToggle')).toBeVisible();
        await expect(page.locator('#askAiWindow #aiChats #aiChatsClose')).toBeVisible();
        await expect(page.locator('#askAiWindow #aiFiles #aiFilesClose')).toBeVisible();
        await expect(page.locator('#askAiWindow .aiWindowHead button')).toHaveCount(1);
        await expect(page.locator('#aiNewChat')).toHaveCount(0);
        await expect(page.locator('#aiClear')).toHaveCount(0);
        await expect(page.locator('#aiExport')).toHaveCount(0);
    });

    ///
    ///**Send and Stop are one button**, in one place: Stop while an answer is coming, Send again once it has stopped.
    ///Asked for 2026-10-18 - Stop was a second button that appeared beside a grayed Send. The provider here answers
    ///nothing until the test is done with it, so the answer is still coming for as long as the test looks.
    ///
    test('Send turns into Stop while an answer is coming, and back', async ({ page }) => {
        await withModel(page, GEMINI);
        await page.route(GOOGLE, async route => {
            if (route.request().method() === 'OPTIONS') {
                await route.fulfill({ status: 204, headers: CORS });

                return;
            }

            //Held: the chat stops it, and the browser lets go of it.
            await new Promise(() => { });
        });

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);
        await send(page, 'draw a met1 square');

        const button = page.locator('#aiSend');

        await expect(button).toHaveAttribute('aria-label', 'Stop');
        await expect(button).toBeEnabled();
        await expect(page.locator('#aiStop')).toHaveCount(0);

        await button.click();

        await expect(transcript(page)).toContainText('Stopped.');
        await expect(button).toHaveAttribute('aria-label', 'Send');
    });

    ///
    ///**A double-click on Send sends, and does not stop what it sent**: the first click sends and the button turns into
    ///Stop under the pointer, so the second would stop it - which is why a press the browser counts as the second of a
    ///pair is let go while an answer is coming. See AskAi's sendOrStop.
    ///
    test('a double-click on Send sends, and does not stop what it sent', async ({ page }) => {
        await withModel(page, GEMINI);
        await page.route(GOOGLE, async route => {
            if (route.request().method() === 'OPTIONS') {
                await route.fulfill({ status: 204, headers: CORS });

                return;
            }

            await new Promise(() => { });
        });

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await page.locator('#aiPrompt').fill('draw a met1 square');
        await page.locator('#aiSend').dblclick();

        await expect(page.locator('#aiSend')).toHaveAttribute('aria-label', 'Stop');
        await page.waitForTimeout(500);
        await expect(page.locator('#aiSend')).toHaveAttribute('aria-label', 'Stop');
        await expect(transcript(page)).not.toContainText('Stopped.');
    });

    ///
    ///**The effort's first row says what default is** for the model picked - what it does when it is sent no level, by
    ///its provider's documents. It said only "default", which told nobody how hard the model would think. Asked for
    ///2026-10-18; see LlmConnection.DefaultEffort, which the C# tests hold to each family. Beside the model's name, it is
    ///that default until a level is chosen.
    ///
    test('the effort\'s default says which effort it is', async ({ page }) => {
        await withModel(page, { Name: 'Flash', ProviderType: 'Gemini', ApiKey: 'test-key', Model: 'gemini-3.8-flash', MaxTokens: 8192 });
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await expect(page.locator('#aiModelEffort')).toHaveText('High');

        await page.locator('#aiModelMenuToggle').click();
        await page.locator('#aiModelMenuEffort').click();

        await expect(page.locator('#aiModelMenu [data-effort=""]')).toHaveText('Default (high)');
    });

    ///
    ///**Manage models, at the foot of the model menu, opens AI settings** (asked for 2026-11-01), and puts the menu away.
    ///
    test('Manage models in the model menu opens AI settings', async ({ page }) => {
        await withModel(page, { Name: 'Flash', ProviderType: 'Gemini', ApiKey: 'test-key', Model: 'gemini-3.8-flash', MaxTokens: 8192 });
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await page.locator('#aiModelMenuToggle').click();
        await page.locator('#aiModelMenuManage').click();

        await expect(page.locator('#aiModelMenu')).toHaveCount(0);
        await expect(page.locator('#aiSettingsWindow')).toBeVisible();
    });

    ///
    ///**The model and its effort are one button, as the Claude app has them** (asked for 2026-11-01): the name with the
    ///effort beside it, and a menu with the models, the effort - whose row opens its levels - and fast mode on an Opus
    ///that has it. What is chosen there is what is sent: the effort in output_config, fast mode as the speed with its
    ///beta. A model without fast mode is offered no switch, and changing to it turns fast mode off.
    ///
    test('one button picks the model, its effort and fast mode, and the request carries them', async ({ page }) => {
        await blockTheOutside(page);
        await withModel(page,
            { Name: 'Opus', ProviderType: 'Anthropic', ApiKey: 'test-key', Model: 'claude-opus-5-5', MaxTokens: 8192 },
            { Name: 'Sonnet', ProviderType: 'Anthropic', ApiKey: 'test-key', Model: 'claude-sonnet-5-5', MaxTokens: 8192 });

        const sent = await fakeProvider(page, ANTHROPIC, [claudeSays('Hello.')]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        const toggle = page.locator('#aiModelMenuToggle');
        const menu = page.locator('#aiModelMenu');

        await expect(page.locator('#aiModelChosen')).toHaveText('Opus');
        await expect(page.locator('#aiModelEffort')).toHaveText('Medium');
        await expect(page.locator('#aiModelFast')).toHaveCount(0);

        //The models with a check on the one picked, then the effort and fast mode.
        await toggle.click();

        await expect(menu.locator('[data-model]')).toHaveText([/Opus/, /Sonnet/]);
        await expect(menu.locator('[data-model="Opus"]')).toHaveAttribute('aria-checked', 'true');
        await expect(page.locator('#aiModelMenuEffort')).toContainText('Medium');
        await expect(page.locator('#aiModelMenuFast')).toHaveAttribute('aria-checked', 'false');

        //The effort's row opens its levels in the menu's place; one chosen closes the menu and shows beside the name.
        await page.locator('#aiModelMenuEffort').click();

        await expect(menu.locator('[data-effort]')).toHaveText(['Default (medium)', 'Low', 'Medium', 'High', 'Extra high', 'Max']);

        await menu.locator('[data-effort="high"]').click();

        await expect(menu).toHaveCount(0);
        await expect(page.locator('#aiModelEffort')).toHaveText('High');

        //Fast mode's switch leaves the menu up, and the bolt shows on the button.
        await toggle.click();
        await page.locator('#aiModelMenuFast').click();

        await expect(page.locator('#aiModelMenuFast')).toHaveAttribute('aria-checked', 'true');
        await expect(page.locator('#aiModelFast')).toBeVisible();

        //Escape puts it away.
        await menu.focus();
        await page.keyboard.press('Escape');

        await expect(menu).toHaveCount(0);

        await send(page, 'Hi');

        await expect(transcript(page)).toContainText('Hello.');
        expect(sent[0].body.output_config).toEqual({ effort: 'high' });
        expect(sent[0].body.speed).toBe('fast');
        expect(sent[0].headers['anthropic-beta']).toBe('fast-mode-2026-02-01');

        //Sonnet has no fast mode: no switch, no bolt, and the effort it was given is one it takes, so it stays.
        await toggle.click();
        await menu.locator('[data-model="Sonnet"]').click();

        await expect(page.locator('#aiModelChosen')).toHaveText('Sonnet');
        await expect(page.locator('#aiModelEffort')).toHaveText('High');
        await expect(page.locator('#aiModelFast')).toHaveCount(0);

        await toggle.click();

        await expect(page.locator('#aiModelMenuFast')).toHaveCount(0);
    });

    ///
    ///**A size comes back only when somebody chose it**, with the grip. The size a window merely had was recorded as it
    ///opened, which kept every browser that had opened the chat at its old 560 after the default grew - so one recorded
    ///that way is left behind. Where the window was is not kept at all since 2026-10-21: every window opens in the
    ///middle of the browser window - asked for, since off full screen the chat opened to one side of the app.
    ///
    test('a size dragged with the grip comes back, and one the window merely had does not', async ({ page }) => {
        await page.addInitScript(() => {
            try {
                if (window.localStorage.getItem('gdsviewer.window.ask-ai') == null)
                    window.localStorage.setItem('gdsviewer.window.ask-ai', JSON.stringify({ left: 300, top: 120, width: 560, height: 600 }));
            }
            catch (e) { }
        });

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        const chat = page.locator('#askAiWindow');
        const opened = await chat.boundingBox();

        const room = await page.evaluate(() => ({
            width: window.innerWidth,
            height: window.innerHeight,
            bar: document.querySelector('.viewToolbar').getBoundingClientRect().bottom
        }));

        //In the middle, at the size it opens at - not where the record left it. Across the browser window, and down the
        //room under the bar, which it keeps clear of: as far below the bar as it stops short of the foot.
        expect(Math.abs(opened.x - ((room.width - opened.width) / 2))).toBeLessThanOrEqual(1);
        expect(opened.y).toBeGreaterThan(room.bar);
        expect(Math.abs((opened.y - room.bar) - (room.height - (opened.y + opened.height)))).toBeLessThanOrEqual(2);
        expect(Math.round(opened.width)).toBe(960);

        //Sized with the grip, and opened again in a page of its own: that size comes back.
        const grip = await page.locator('#askAiWindow .aiWindowGrip').boundingBox();

        await page.mouse.move(grip.x + (grip.width / 2), grip.y + (grip.height / 2));
        await page.mouse.down();
        await page.mouse.move(grip.x + (grip.width / 2) - 80, grip.y + (grip.height / 2), { steps: 5 });
        await page.mouse.up();

        await expect.poll(async () => Math.round((await chat.boundingBox()).width)).toBe(880);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await expect.poll(async () => Math.round((await chat.boundingBox()).width)).toBe(880);
    });

    ///
    ///**Ctrl+C copies words selected in the conversation**, over the 2D view as anywhere: the drawing's own Ctrl+C, which
    ///copies the shape chosen, took the key whatever was selected, and the browser never copied. Heard as the copy the
    ///browser runs, which a key taken by the drawing never starts.
    ///
    ///And a press on the drawing puts the words away, which the browser leaves selected there - so after one, Ctrl+C is
    ///the drawing's again rather than a copy of words picked out a while before.
    ///
    test('Ctrl+C copies words selected in the conversation, and a press on the drawing puts them away', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        //What the window says before anything is typed, selected the way a drag would.
        await page.evaluate(() => {
            const words = document.querySelector('#aiTranscript .aiIntro p');
            const range = document.createRange();

            range.selectNodeContents(words);
            window.getSelection().removeAllRanges();
            window.getSelection().addRange(range);

            window.__copied = null;
            document.addEventListener('copy', () => { window.__copied = window.getSelection().toString(); }, { once: true });
        });

        await page.keyboard.press('Control+C');

        await expect.poll(async () => page.evaluate(() => window.__copied)).toContain('Describe a change to the layout');

        await page.locator('#gdsSVG').click({ position: { x: 20, y: 20 } });

        await expect.poll(async () => page.evaluate(() => window.getSelection().isCollapsed)).toBe(true);
    });
});

test.describe('a conversation', () => {
    test.beforeEach(async ({ page }) => {
        await blockTheOutside(page);
        await withModel(page, CLAUDE);
    });

    ///
    ///The whole of it: the model asks to draw, the card waits on the middle rung, Approve draws the rectangle in
    ///the view, the model is told what was drawn, and Undo takes it back like any other change.
    ///
    test('a change asked for waits for Approve, is drawn, and Undo takes it back', async ({ page }) => {
        const sent = await fakeProvider(page, ANTHROPIC, [
            claudeCalls('toolu_1', 'draw_rectangle', RECTANGLE),
            claudeSays('Drew a 1 µm square on met1.')
        ]);

        await gotoExample(page, MOSFET, '2d');

        const before = await shapeCount(page);

        await openAssistant(page);
        await send(page, 'Draw a 1 µm square on met1 at the origin');

        const card = page.locator('.aiCard');

        await expect(card).toContainText('Wants to change the layout: draw rectangle');
        await expect(card).toContainText('"layer":"68/20"');

        //Nothing has happened yet: the card is the question.
        expect(await shapeCount(page)).toBe(before);

        await card.locator('.aiApprove').click();

        await expect(transcript(page)).toContainText('Drew a 1 µm square on met1.');
        await expect.poll(() => shapeCount(page)).toBe(before + 1);
        await expect(card).toContainText('Changed the layout: draw rectangle');

        //The second request answered the call by its id, with what the tool said.
        const answer = sent[1].body.messages.at(-1).content[0];

        expect(answer.type).toBe('tool_result');
        expect(answer.tool_use_id).toBe('toolu_1');
        expect(answer.content).toMatch(/^Drew a 1 by 1 µm rectangle/);

        //And it went with Anthropic's headers, the browser's own permission among them.
        expect(sent[0].headers['x-api-key']).toBe('test-key');
        expect(sent[0].headers['anthropic-dangerous-direct-browser-access']).toBe('true');

        await page.locator('#undoEdit').click();

        await expect.poll(() => shapeCount(page)).toBe(before);
    });

    ///
    ///**A layer the assistant starts is named by the PDK in use**, as one added in the layer panel is: Mosfet has nothing
    ///on 69/20, and sky130 calls it met2. It came in as a bare number - no name, no height, no role - until the
    ///2026-10-21 audit put the naming where every way a layer arrives goes through (GDS.AddLayer).
    ///
    test('a layer the assistant starts is named by the PDK, as one added in the panel is', async ({ page }) => {
        await fakeProvider(page, ANTHROPIC, [
            claudeCalls('toolu_1', 'draw_rectangle', { layer: '69/20', from: [0, 0], to: [1, 1] }),
            claudeSays('Drew it on met2.')
        ]);

        await gotoExample(page, MOSFET, '2d');

        expect(await layerLabel(page, '69/20')).toBeUndefined();

        await openAssistant(page);
        await page.locator('#aiMode').selectOption('AutoReadWrite');
        await send(page, 'Draw a 1 µm square on 69/20');

        await expect(transcript(page)).toContainText('Drew it on met2.');
        await expect.poll(() => layerLabel(page, '69/20')).toBe('met2 (69/20)');
    });

    ///
    ///**A PDK put in use from the conversation** (2026-11-01, asked for): told to use sky130 on a new layout, the assistant
    ///drew on numbers one gray layer at a time, with nothing that did what Set PDK does. use_pdk lays the PDK's layers on -
    ///a change, so it waits on the middle rung - and the model, told the layout waits for one, draws on them by name after.
    ///
    test('on a new layout, use_pdk lays the PDK\'s layers on, and what is drawn after lands on them by name', async ({ page }) => {
        const sent = await fakeProvider(page, ANTHROPIC, [
            claudeCalls('toolu_1', 'use_pdk', { pdk: 'sky130' }),
            claudeCalls('toolu_2', 'draw_rectangle', { layer: 'met1', from: [0, 0], to: [1, 1] }),
            claudeSays('sky130 is set, and the square is on met1.')
        ]);

        await gotoExample(page, MOSFET, '2d');
        await page.locator('#newLayout').click();

        await expect(page.locator('#sidebarPdkName')).toHaveText('not set');

        await openAssistant(page);
        await send(page, 'Use the sky130 PDK and draw a 1 µm met1 square');

        await page.locator('.aiCard[data-tool="use_pdk"] .aiApprove').click();

        await expect(page.locator('#sidebarPdkName')).toHaveText('sky130');
        await expect.poll(async () => page.locator('#layerSidebar .layerRow').count(), { timeout: 15000 }).toBe(37);

        await page.locator('.aiCard[data-tool="draw_rectangle"] .aiApprove').click();

        await expect(transcript(page)).toContainText('the square is on met1.');
        await expect(page.locator('.aiCard[data-tool="use_pdk"]')).toContainText('sky130 is in use, and its 37 layers are laid on this layout');

        //The model was told the layout waits for a PDK, and which there are.
        expect(sent[0].body.system).toContain('No PDK has laid its layers on this layout yet');
        expect(sent[0].body.system).toContain('The person\'s PDKs are sky130');
        expect(sent[0].body.tools.map(tool => tool.name)).toContain('use_pdk');

        //And the square is on met1 among them, not on a layer of its own.
        await expect.poll(() => layerLabel(page, '68/20')).toBe('met1 (68/20)');
        await expect(page.locator('#layerSidebar .layerRow')).toHaveCount(37);
    });

    ///
    ///**And the ones a layout drawn in it lacks** (asked for the same day): a layer drawn on a number before any PDK was set
    ///comes in alone and gray, and use_pdk lays sky130's other layers on round it - and gives it a color of its own too.
    ///
    test('on a layout with a few of its layers, use_pdk adds the ones it lacks, and the gray one takes a color', async ({ page }) => {
        await fakeProvider(page, ANTHROPIC, [
            claudeCalls('toolu_1', 'draw_rectangle', { layer: '68/20', from: [0, 0], to: [1, 1] }),
            claudeCalls('toolu_2', 'use_pdk', { pdk: 'sky130' }),
            claudeSays('Done: sky130\'s layers are in.')
        ]);

        await gotoExample(page, MOSFET, '2d');
        await page.locator('#newLayout').click();

        await openAssistant(page);
        await send(page, 'Draw a 1 µm square on 68/20, then give me the rest of sky130\'s layers');

        await page.locator('.aiCard[data-tool="draw_rectangle"] .aiApprove').click();

        const swatch = page.locator('#layerSidebar .layerRow', { hasText: '68/20' }).locator('.layerSwatch');

        await expect(page.locator('#layerSidebar .layerRow')).toHaveCount(1);
        await expect(swatch).toHaveAttribute('title', '#808080');

        await page.locator('.aiCard[data-tool="use_pdk"] .aiApprove').click();

        await expect(transcript(page)).toContainText('sky130\'s layers are in.');
        await expect(page.locator('.aiCard[data-tool="use_pdk"]')).toContainText('the 36 of its layers this layout did not have are laid on beside the ones it had');
        await expect(page.locator('#layerSidebar .layerRow')).toHaveCount(37);
        await expect(swatch).not.toHaveAttribute('title', '#808080');
    });

    ///
    ///**A change shows what it did to the file's text** - asked for 2026-10-21, whichever view it was asked for from: its
    ///card gets a Monaco diff of the record dump, the records put in marked as added and numbered where the text view
    ///has them, rather than from one.
    ///
    test('a change made shows what it did to the file\'s text, as a diff on its card', async ({ page }) => {
        await fakeProvider(page, ANTHROPIC, [
            claudeCalls('toolu_1', 'draw_rectangle', RECTANGLE),
            claudeSays('Drew a 1 µm square on met1.')
        ]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);
        await send(page, 'Draw a 1 µm square on met1 at the origin');

        const card = page.locator('.aiCard');

        //Not before it is made: there is nothing yet to compare.
        await expect(card).toContainText('Wants to change the layout: draw rectangle');
        await expect(card.locator('.aiTextDiff')).toHaveCount(0);

        await card.locator('.aiApprove').click();

        await expect(transcript(page)).toContainText('Drew a 1 µm square on met1.');
        await expect(card).toContainText('What it did to the file\'s text');

        const diff = card.locator('.aiTextDiff');

        await expect(diff.locator('.monaco-diff-editor')).toBeVisible({ timeout: 30000 });
        await expect(diff.locator('.line-insert').first()).toBeVisible();

        const shown = await diff.locator('.editor.modified .view-lines').innerText();

        expect(shown).toContain('BOUNDARY');
        expect(shown).toMatch(/LAYER:\s+68/);

        //Numbered as the file's text is, a long way in, rather than from one.
        const numbers = (await diff.locator('.editor.modified .line-numbers').allInnerTexts()).map(Number).filter(number => number > 0);

        expect(Math.min(...numbers)).toBeGreaterThan(10);

        //A new chat lets it go with its card.
        await page.locator('#aiChatsNew').click();

        await expect(page.locator('.aiTextDiff')).toHaveCount(0);
        expect(await page.evaluate(() => monaco.editor.getDiffEditors().length)).toBe(0);
    });

    test('Decline leaves the layout as it was, and the model is told', async ({ page }) => {
        const sent = await fakeProvider(page, ANTHROPIC, [
            claudeCalls('toolu_1', 'draw_rectangle', RECTANGLE),
            claudeSays('All right, I left it alone.')
        ]);

        await gotoExample(page, MOSFET, '2d');

        const before = await shapeCount(page);

        await openAssistant(page);
        await send(page, 'Draw a square');
        await page.locator('.aiCard .aiDecline').click();

        await expect(transcript(page)).toContainText('All right, I left it alone.');
        await expect(page.locator('.aiCard')).toContainText('Declined');

        expect(await shapeCount(page)).toBe(before);
        expect(sent[1].body.messages.at(-1).content[0].content).toMatch(/did not approve/);
    });

    ///
    ///**A card that comes up while a diff is being drawn** (2026-10-21). The first diff loads Monaco, which takes a while,
    ///and the model's next call put its card up in the meantime - which threw and took the page down with an unhandled
    ///error, since the diffs were drawn walking the conversation itself across that wait. Two changes in one reply put
    ///the second card there every time. See showDiffs.
    ///
    test('a second change asked for while the first one\'s diff is drawn waits its turn, and nothing breaks', async ({ page }) => {
        await fakeProvider(page, ANTHROPIC, [
            {
                content: [
                    { type: 'tool_use', id: 'toolu_1', name: 'draw_rectangle', input: RECTANGLE },
                    { type: 'tool_use', id: 'toolu_2', name: 'draw_rectangle', input: { layer: '68/20', from: [2, 0], to: [3, 1] } }
                ],
                stop_reason: 'tool_use'
            },
            claudeSays('Drew two squares on met1.')
        ]);

        await gotoExample(page, MOSFET, '2d');

        const before = await shapeCount(page);

        await openAssistant(page);
        await send(page, 'Draw two 1 µm squares on met1');

        const cards = page.locator('.aiCard');

        await expect(cards).toHaveCount(1);
        await cards.nth(0).locator('.aiApprove').click();

        //The second asks while the first one's diff is still being made, and the first is made all the same.
        await expect(cards).toHaveCount(2);
        await expect(cards.nth(1)).toContainText('Wants to change the layout: draw rectangle');
        await expect(cards.nth(0).locator('.aiTextDiff .monaco-diff-editor')).toBeVisible({ timeout: 30000 });

        await cards.nth(1).locator('.aiApprove').click();

        await expect(transcript(page)).toContainText('Drew two squares on met1.');
        await expect.poll(() => shapeCount(page)).toBe(before + 2);
        await expect(cards.nth(1).locator('.aiTextDiff .monaco-diff-editor')).toBeVisible({ timeout: 30000 });
        await expect(page.locator('#blazor-error-ui')).toBeHidden();
    });

    ///
    ///**The rung chosen while an answer is coming is the one its calls meet** (asked for 2026-10-21): a change waiting on
    ///Ask before changes goes ahead the moment Change without asking is chosen, and the change after it is made unasked -
    ///where both used to wait for a press until the answer was finished. See AiChat.Mode.
    ///
    test('choosing Change without asking while a card waits lets it go, and the next change does not ask', async ({ page }) => {
        await fakeProvider(page, ANTHROPIC, [
            claudeCalls('toolu_1', 'draw_rectangle', RECTANGLE),
            claudeCalls('toolu_2', 'draw_rectangle', { layer: '68/20', from: [2, 0], to: [3, 1] }),
            claudeSays('Drew two squares on met1.')
        ]);

        await gotoExample(page, MOSFET, '2d');

        const before = await shapeCount(page);

        await openAssistant(page);
        await send(page, 'Draw two 1 µm squares on met1');

        const cards = page.locator('.aiCard');

        await expect(cards.nth(0)).toContainText('Wants to change the layout: draw rectangle');

        await page.locator('#aiMode').selectOption('AutoReadWrite');

        await expect(transcript(page)).toContainText('Drew two squares on met1.');
        await expect.poll(() => shapeCount(page)).toBe(before + 2);

        //Both shown as run without asking, and nothing left to press.
        await expect(cards).toHaveCount(2);
        await expect(cards.locator('.aiBadge')).toHaveCount(2);
        await expect(page.locator('.aiApprove')).toHaveCount(0);
    });

    ///
    ///**A long run that is working is paused, not failed** (2026-11-01, asked for): the rounds set under AI settings -
    ///thirty to begin with - end in the question whether to go on for another as many, rather than a red error, and
    ///Continue carries the run on.
    ///
    test('a run of calls that work pauses at the rounds set in AI settings, and Continue carries it on', async ({ page }) => {
        let requests = 0;

        await page.route(ANTHROPIC, async route => {
            if (route.request().method() === 'OPTIONS') {
                await route.fulfill({ status: 204, headers: CORS });

                return;
            }

            requests++;

            let reply = claudeSays('Drew them all.');

            if (requests <= 3)
                reply = claudeCalls('toolu_' + requests, 'draw_rectangle', { layer: '68/20', from: [requests * 2, 0], to: [requests * 2 + 1, 1] });

            await route.fulfill({ status: 200, headers: { ...CORS, 'Content-Type': 'application/json' }, body: JSON.stringify(reply) });
        });

        await gotoExample(page, MOSFET, '2d');

        await page.locator('#settingsMenu').click();
        await page.locator('#aiSettingsItem').click();

        const rounds = page.locator('#aiToolRounds');

        await expect(rounds).toHaveValue('30');
        await rounds.fill('3');
        await rounds.press('Tab');
        await page.locator('#aiSettingsWindow .closeButton').click();

        await openAssistant(page);
        await page.locator('#aiMode').selectOption('AutoReadWrite');
        await send(page, 'Draw a row of squares on met1');

        const paused = page.locator('.aiTurnPaused');

        await expect(paused).toContainText('reached the set limit of 3 rounds of tool calls');
        await expect(paused).toContainText('continue for another 3 rounds?');
        await expect(page.locator('.aiTurnError')).toHaveCount(0);
        expect(requests).toBe(3);

        await page.locator('#aiContinue').click();

        await expect(transcript(page)).toContainText('Drew them all.');
        await expect(page.locator('#aiContinue')).toHaveCount(0);
        expect(requests).toBe(4);

        //Kept: the settings show it again after a reload.
        await page.reload();
        await page.locator('#settingsMenu').click();
        await page.locator('#aiSettingsItem').click();
        await expect(page.locator('#aiToolRounds')).toHaveValue('3');
    });

    ///
    ///**The app's examples, read beside the layout** (2026-11-01, asked for): the model is told they are there, reads
    ///one with read_example - fetched from the app's own folder and named by the layermap that ships - and the layout
    ///open is left as it was.
    ///
    test('read_example reads one of the app examples beside the layout, named, and changes nothing', async ({ page }) => {
        const sent = await fakeProvider(page, ANTHROPIC, [
            claudeCalls('toolu_1', 'read_example', { name: 'sky130_fd_sc_hd__inv_1.gds' }),
            claudeSays('The inverter has its rails on met1.')
        ]);

        await gotoExample(page, MOSFET, '2d');

        const before = await shapeCount(page);

        await openAssistant(page);
        await send(page, 'How does sky130 draw an inverter?');

        await expect(transcript(page)).toContainText('The inverter has its rails on met1.');

        expect(JSON.stringify(sent[0].body.system)).toContain('read_example');
        expect(sent[0].body.tools.map(tool => tool.name)).toEqual(expect.arrayContaining(['list_examples', 'read_example']));

        const answer = JSON.stringify(sent[1].body.messages.at(-1).content);

        expect(answer).toContain('sky130_fd_sc_hd__inv_1');
        expect(answer).toContain('li1');
        expect(answer).toContain('The shapes in sky130_fd_sc_hd__inv_1');
        expect(await shapeCount(page)).toBe(before);
    });

    ///On the middle rung a read runs at once: look draws the cell in the browser, and the model is sent the PNG.
    test('look runs without asking, and the model is sent a picture of the cell', async ({ page }) => {
        const sent = await fakeProvider(page, ANTHROPIC, [
            claudeCalls('toolu_1', 'look', {}),
            claudeSays('I can see the transistor.')
        ]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);
        await send(page, 'What does it look like?');

        await expect(transcript(page)).toContainText('I can see the transistor.');

        const result = sent[1].body.messages.at(-1).content[0];
        const [words, picture] = result.content;

        expect(words.text).toContain('drawn the way the person\'s 2D view draws it');
        expect(picture.type).toBe('image');
        expect(picture.source.media_type).toBe('image/png');

        //A real PNG, and one that is not empty.
        expect(picture.source.data.startsWith('iVBORw0KGgo')).toBe(true);
        expect(picture.source.data.length).toBeGreaterThan(2000);

        //And the card shows the person the same picture.
        await expect(page.locator('.aiCard .aiPicture')).toBeVisible();
        await expect(page.locator('.aiCard .aiBadge')).toHaveText('auto');
    });

    ///What the model is told: the file, its units, its layers and where the person is.
    test('the model is told the file, the units, the layers and the view', async ({ page }) => {
        const sent = await fakeProvider(page, ANTHROPIC, [claudeSays('Noted.')]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);
        await send(page, 'Hi');

        await expect(transcript(page)).toContainText('Noted.');

        const system = sent[0].body.system;

        expect(system).toContain('One database unit is 0.001 µm.');
        expect(system).toContain('68/20');
        //Mosfet has one top cell, and the 2D view opens inside it.
        expect(system).toContain('The tools work in mosfet.');
        expect(system).toContain('The person has mosfet open for editing in the 2D view.');
        expect(system).toContain('Nothing is selected.');
        expect(system).toMatch(/The 2D view shows x -?[\d.]+ to -?[\d.]+ µm/);
        expect(sent[0].body.tools.map(tool => tool.name)).toContain('look');
    });

    ///
    ///**In the text view, what was typed is saved before the model is asked** - the assistant reads the file, which
    ///typing has not reached until it is saved. Here layer 65 typed over as 200, and the model told of layer 200.
    ///
    test('in the text view, typing is saved before the assistant reads the file', async ({ page }) => {
        const sent = await fakeProvider(page, ANTHROPIC, [claudeSays('There is a layer 200 now.')]);

        await gotoExample(page, MOSFET, 'text');
        await expectEditorLoaded(page);

        const text = await editorText(page);

        await page.evaluate(contents => window.SetMonacoContent(contents), text.replace('LAYER: 65 ', 'LAYER: 200 '));

        await openAssistant(page);
        await send(page, 'Which layers are there?');

        await expect(transcript(page)).toContainText('There is a layer 200 now.');
        await expect(page.locator('.editorMessageText')).toContainText('Saved');

        expect(sent[0].body.system).toMatch(/\b200\/\d+/);
    });

    ///And typing that cannot be read sends nothing: the strip under the editor says which line, and the words wait in the box.
    test('in the text view, typing that cannot be read is not sent, and the chat says so', async ({ page }) => {
        const sent = await fakeProvider(page, ANTHROPIC, [claudeSays('Nobody should have asked me.')]);

        await gotoExample(page, MOSFET, 'text');
        await expectEditorLoaded(page);

        const text = await editorText(page);

        await page.evaluate(contents => window.SetMonacoContent(contents), text.replace('LIBNAME: mosfet ', 'WIBBLE: mosfet '));

        await openAssistant(page);
        await send(page, 'Hi');

        await expect(transcript(page)).toContainText('Nothing was sent');
        await expect(page.locator('.editorMessageText')).toContainText('WIBBLE');
        await expect(page.locator('#aiPrompt')).toHaveValue('Hi');

        expect(sent.length).toBe(0);
    });

    ///
    ///**A change made while the 3D view is up is drawn there.** It was not: the view builds a file's scene once and
    ///knows the file by the object, which a change hands back unaltered, so it went on drawing the file as it was. And
    ///the camera stays where it was - it is the same file, being looked at.
    ///
    test('a change made over the 3D view is drawn there, and the camera stays put', async ({ page }) => {
        await fakeProvider(page, ANTHROPIC, [
            claudeCalls('toolu_1', 'draw_rectangle', { layer: '68/20', from: [-30, -30], to: [-28, -28] }),
            claudeSays('Drew a square off to the side.')
        ]);

        await gotoExample(page, MOSFET, '3d');

        await expect(page.locator('#container canvas')).toBeVisible();
        await expect.poll(async () => {
            await captureScene(page);

            return page.evaluate(() => window.__gdsScene != null);
        }).toBe(true);

        //Every corner of every layer's slab - one more outline on met1 is more of them.
        const corners = () => page.evaluate(() => {
            let count = 0;

            window.__gdsScene.traverse(object => {
                if (object.isMesh && object.userData.words != null)
                    count += object.geometry.attributes.position.count;
            });

            return count;
        });

        const before = await corners();
        const camera = await cameraPosition(page);

        await openAssistant(page);
        await send(page, 'Draw a 2 µm met1 square off to the side');
        await page.locator('.aiCard .aiApprove').click();

        await expect(transcript(page)).toContainText('Drew a square off to the side.');
        await expect.poll(corners, { timeout: 30000 }).toBeGreaterThan(before);

        expect(await cameraPosition(page)).toEqual(camera);
    });

    test('a refused key is said in the conversation, with the provider\'s words', async ({ page }) => {
        await fakeProvider(page, ANTHROPIC, [{ type: 'error', error: { type: 'authentication_error', message: 'invalid x-api-key' } }], 401);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);
        await send(page, 'Hi');

        await expect(page.locator('.aiTurnError')).toHaveText('Anthropic answered 401 Unauthorized: invalid x-api-key');
    });

    ///Enter is the send key, so the newline it would type is refused in the page itself - which only a browser can show.
    test('Enter sends, Shift+Enter starts a new line, and the box grows to fit', async ({ page }) => {
        await fakeProvider(page, ANTHROPIC, [claudeSays('Got it.')]);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        const box = page.locator('#aiPrompt');
        const oneLine = (await box.boundingBox()).height;

        await box.click();
        await page.keyboard.type('one');
        await page.keyboard.press('Shift+Enter');
        await page.keyboard.type('two');

        expect(await box.inputValue()).toBe('one\ntwo');
        expect((await box.boundingBox()).height).toBeGreaterThan(oneLine);

        await page.keyboard.press('Enter');

        await expect(transcript(page)).toContainText('Got it.');

        expect(await box.inputValue()).toBe('');
        expect((await box.boundingBox()).height).toBeLessThanOrEqual(oneLine + 1);
    });

    ///
    ///**New chat empties the window and asks nothing**, since nothing is thrown away: the chat it replaces is kept, in
    ///the chats panel. Clear, which threw it away, asked first; it went 2026-10-31.
    ///
    test('New chat empties the window without asking, and keeps the chat it replaced', async ({ page }) => {
        const asked = [];

        page.on('dialog', dialog => {
            asked.push(dialog.message());

            return dialog.dismiss();
        });

        await fakeProvider(page, ANTHROPIC, [claudeSays('Hello.')]);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await expect(page.locator('#aiChatsNew')).toBeDisabled();

        await send(page, 'Hi');

        await expect(transcript(page)).toContainText('Hello.');

        await page.locator('#aiChatsNew').click();

        await expect(transcript(page)).not.toContainText('Hello.');
        await expect(transcript(page)).toContainText('Nothing is sent until you press Send');
        expect(asked).toEqual([]);

        //The new one on screen first, not kept yet, and the one it replaced under it.
        await expect(page.locator('#aiChats .aiChatTitle')).toHaveText(['New chat', 'Hi']);
        await expect(page.locator('#aiChatNow')).toContainText('Not kept yet');
        await expect(page.locator('#aiChats .aiChatItem[data-chat] .aiChatWhen')).toContainText('Mosfet');
    });

    ///
    ///**The box starts empty**, saying what to do in its placeholder (2026-11-01, asked for): the examples over it take the
    ///place of the "draw a 2 µm met1 square at the origin" it started with from 2026-10-21. New chat leaves it empty too.
    ///
    test('the box starts empty, with what to do as its placeholder, and New chat leaves it so', async ({ page }) => {
        await fakeProvider(page, ANTHROPIC, [claudeSays('Which corner?')]);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        const box = page.locator('#aiPrompt');

        await expect(box).toHaveValue('');
        await expect(box).toHaveAttribute('placeholder', 'Describe a change, or type / for commands');

        await send(page, 'draw a 2 µm met1 square at the origin');

        await expect(transcript(page)).toContainText('Which corner?');
        await expect(box).toHaveValue('');

        await page.locator('#aiChatsNew').click();

        await expect(page.locator('#aiExamples')).toBeVisible();
        await expect(box).toHaveValue('');
    });

    ///
    ///**An empty conversation offers examples** (2026-11-01, asked for, the sky130 MOSFET first): a press puts one in the
    ///box with the keyboard there, and sends nothing until Send. They go with the first question.
    ///
    test('an empty conversation offers examples, put in the box by a press and sent only by Send', async ({ page }) => {
        const sent = await fakeProvider(page, ANTHROPIC, [claudeSays('Which length?')]);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        const box = page.locator('#aiPrompt');
        const mosfet = page.locator('#aiExamples .aiExample').first();

        await expect(mosfet).toHaveText('Create a simple MOSFET using the sky130 PDK');

        await mosfet.click();

        await expect(box).toHaveValue('Create a simple MOSFET using the sky130 PDK');
        await expect(box).toBeFocused();
        expect(sent).toHaveLength(0);

        await page.keyboard.press('Enter');

        await expect(transcript(page)).toContainText('Which length?');
        expect(JSON.stringify(sent[0].body.messages)).toContain('Create a simple MOSFET using the sky130 PDK');
        await expect(page.locator('#aiExamples')).toHaveCount(0);
    });
});

test.describe('Gemini', () => {
    test.beforeEach(async ({ page }) => {
        await blockTheOutside(page);
        await withModel(page, GEMINI);
    });

    ///
    ///The Gemini key goes to Google's OpenAI-compatible endpoint, as a bearer token - and on the top rung a change is
    ///made with no card to answer, its thought signature going back with the call.
    ///
    test('goes through Google\'s OpenAI endpoint, and on the top rung changes without asking', async ({ page }) => {
        const sent = await fakeProvider(page, GOOGLE, [
            geminiCalls('call_1', 'draw_rectangle', RECTANGLE),
            geminiSays('Done.')
        ]);

        await gotoExample(page, MOSFET, '2d');

        const before = await shapeCount(page);

        await openAssistant(page);
        await page.locator('#aiMode').selectOption('AutoReadWrite');
        await send(page, 'Draw a square');

        await expect(transcript(page)).toContainText('Done.');
        await expect.poll(() => shapeCount(page)).toBe(before + 1);
        await expect(page.locator('.aiCard .aiBadge')).toHaveText('auto');

        expect(sent[0].url).toBe('https://generativelanguage.googleapis.com/v1beta/openai/chat/completions');
        expect(sent[0].headers['authorization']).toBe('Bearer test-key');
        expect(sent[0].body.model).toBe('gemini-test');

        const echoed = sent[1].body.messages.at(-2);

        expect(echoed.tool_calls[0].extra_content.google.thought_signature).toBe('c2lnbmVk');
        expect(sent[1].body.messages.at(-1).role).toBe('tool');
    });

    ///Chat completions take a picture only from the user, so look's goes in a user turn after the call's answer.
    test('a picture goes back in a user turn after the call', async ({ page }) => {
        const sent = await fakeProvider(page, GOOGLE, [
            geminiCalls('call_1', 'look', {}),
            geminiSays('I see it.')
        ]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);
        await send(page, 'Look at it');

        await expect(transcript(page)).toContainText('I see it.');

        const after = sent[1].body.messages.at(-1);

        expect(after.role).toBe('user');
        expect(after.content[1].type).toBe('image_url');
        expect(after.content[1].image_url.url.startsWith('data:image/png;base64,iVBORw0KGgo')).toBe(true);
    });
});

//
//Files with a question, the files panel, and what it costs - ported from ConfigurationRuleEditor's assistant on
//2026-10-29. The bytes never leave the machine here either: the providers are the same intercepted fakes.
//
//A one-pixel PNG, as a screenshot would arrive; and a PDF of one page, which is all the checks look at.
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==', 'base64');
const PDF = Buffer.from('%PDF-1.7\n<< /Type /Page >>\n%%EOF');

///A real one-page PDF that says what it is given, in Helvetica, with its cross-reference table where it says it is.
function onePagePdf(words) {
    const stream = `BT /F1 12 Tf 72 720 Td (${words}) Tj ET`;
    const objects = [
        '<< /Type /Catalog /Pages 2 0 R >>',
        '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
        '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
        `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`,
        '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'
    ];

    let pdf = '%PDF-1.4\n';
    const offsets = [];

    objects.forEach((object, i) => {
        offsets.push(pdf.length);
        pdf += `${i + 1} 0 obj\n${object}\nendobj\n`;
    });

    const xref = pdf.length;

    pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;

    for (const offset of offsets)
        pdf += `${String(offset).padStart(10, '0')} 00000 n \n`;

    pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`;

    return Buffer.from(pdf, 'latin1');
}

//
//A zip of the parts given, each packed with deflate as Office packs them - which is all an .xlsx or a .docx is, and
//makes the page unpack them the way it unpacks a real one.
//
function zip(parts) {
    const records = [];
    const directory = [];
    let offset = 0;

    for (const [name, text] of Object.entries(parts)) {
        const path = Buffer.from(name, 'utf8');
        const data = Buffer.from(text, 'utf8');
        const packed = zlib.deflateRawSync(data);
        const crc = zlib.crc32(data);
        const local = Buffer.alloc(30);
        const central = Buffer.alloc(46);

        local.writeUInt32LE(0x04034b50, 0);
        local.writeUInt16LE(20, 4);
        local.writeUInt16LE(8, 8);
        local.writeUInt32LE(crc, 14);
        local.writeUInt32LE(packed.length, 18);
        local.writeUInt32LE(data.length, 22);
        local.writeUInt16LE(path.length, 26);

        central.writeUInt32LE(0x02014b50, 0);
        central.writeUInt16LE(20, 4);
        central.writeUInt16LE(20, 6);
        central.writeUInt16LE(8, 10);
        central.writeUInt32LE(crc, 16);
        central.writeUInt32LE(packed.length, 20);
        central.writeUInt32LE(data.length, 24);
        central.writeUInt16LE(path.length, 28);
        central.writeUInt32LE(offset, 42);

        records.push(local, path, packed);
        directory.push(central, path);
        offset += local.length + path.length + packed.length;
    }

    const listing = Buffer.concat(directory);
    const end = Buffer.alloc(22);
    const count = Object.keys(parts).length;

    end.writeUInt32LE(0x06054b50, 0);
    end.writeUInt16LE(count, 8);
    end.writeUInt16LE(count, 10);
    end.writeUInt32LE(listing.length, 12);
    end.writeUInt32LE(offset, 16);

    return Buffer.concat([...records, listing, end]);
}

const PACKAGE = 'http://schemas.openxmlformats.org/package/2006/relationships';
const RELATIONS = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships';
const SHEET = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
const WORD = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main';

function rels(...relations) {
    const listed = relations.map(([id, type, target]) => `<Relationship Id="${id}" Type="${RELATIONS}/${type}" Target="${target}"/>`);

    return `<Relationships xmlns="${PACKAGE}">${listed.join('')}</Relationships>`;
}

//A sheet of rules as Excel writes one: its words in the shared strings, and a date in a cell styled as one.
const WORKBOOK = zip({
    '_rels/.rels': rels(['rId1', 'officeDocument', 'xl/workbook.xml']),
    'xl/workbook.xml': `<workbook xmlns="${SHEET}" xmlns:r="${RELATIONS}"><sheets><sheet name="Rules" sheetId="1" r:id="rId1"/></sheets></workbook>`,
    'xl/_rels/workbook.xml.rels': rels(['rId1', 'worksheet', 'worksheets/sheet1.xml'], ['rId2', 'sharedStrings', 'sharedStrings.xml'], ['rId3', 'styles', 'styles.xml']),
    'xl/sharedStrings.xml': `<sst xmlns="${SHEET}"><si><t>Layer</t></si><si><t>Width (µm)</t></si><si><t>met1</t></si><si><t>Checked</t></si></sst>`,
    'xl/styles.xml': `<styleSheet xmlns="${SHEET}"><cellXfs><xf numFmtId="0"/><xf numFmtId="14"/></cellXfs></styleSheet>`,
    'xl/worksheets/sheet1.xml': `<worksheet xmlns="${SHEET}"><sheetData>`
        + '<row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>3</v></c></row>'
        + '<row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2"><v>0.14000000000000001</v></c><c r="C2" s="1"><v>45231</v></c></row>'
        + '</sheetData></worksheet>'
});

//A page of a manual: a paragraph, and a table of one row.
const DOCUMENT = zip({
    '_rels/.rels': rels(['rId1', 'officeDocument', 'word/document.xml']),
    'word/document.xml': `<w:document xmlns:w="${WORD}"><w:body>`
        + '<w:p><w:r><w:t>Metal rules</w:t></w:r></w:p>'
        + '<w:tbl><w:tr><w:tc><w:p><w:r><w:t>met1</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>0.14 µm</w:t></w:r></w:p></w:tc></w:tr></w:tbl>'
        + '</w:body></w:document>'
});

async function attach(page, ...files) {
    await page.locator('#aiAttachInput').setInputFiles(files);
}

//
//A file let go over an element, the way a drag from the desktop arrives: a dragover the page answers, then the drop -
//through fileDrop.js, which is what decides where it goes. Returns what the highlight said while the file was held.
//
async function dropOn(page, selector, name, type, bytes) {
    return page.locator(selector).evaluate((target, file) => {
        const data = Uint8Array.from(atob(file.base64), c => c.charCodeAt(0));
        const transfer = new DataTransfer();

        transfer.items.add(new File([data], file.name, { type: file.type }));
        target.dispatchEvent(new DragEvent('dragover', { dataTransfer: transfer, bubbles: true, cancelable: true }));

        const lit = document.querySelector('.fileDropOver');
        let said = '';

        if (lit)
            said = getComputedStyle(lit, '::after').content;

        target.dispatchEvent(new DragEvent('drop', { dataTransfer: transfer, bubbles: true, cancelable: true }));

        return said;
    }, { name, type, base64: Buffer.from(bytes).toString('base64') });
}

test.describe('files, and what it costs', () => {
    test.beforeEach(async ({ page }) => {
        await blockTheOutside(page);
    });

    ///
    ///**Files go ahead of the question, each named**, a picture after a line with its name and a text file between tags;
    ///they leave the box for the question, go again with the next one, and stop going once unticked in the files panel
    ///- said in a line where they were, rather than missing without a word.
    ///
    test('files attached go ahead of the question, and again with the next until unticked', async ({ page }) => {
        await withModel(page, CLAUDE);

        const sent = await fakeProvider(page, ANTHROPIC, [claudeSays('A transistor.'), claudeSays('Its gate.')]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await attach(page, { name: 'shot.png', mimeType: 'image/png', buffer: PNG }, { name: 'rules.drc', mimeType: '', buffer: Buffer.from('width met1 0.14\n') });

        await expect(page.locator('#aiPending .aiChip')).toHaveCount(2);

        //Open to begin with, so the count is on its arrow only once the panel is closed.
        await expect(page.locator('#aiFilesCount')).toHaveCount(0);
        await page.locator('#aiFilesClose').click();
        await expect(page.locator('#aiFiles')).toHaveCount(0);
        await expect(page.locator('#aiFilesCount')).toHaveText('2');
        await page.locator('#aiFilesToggle').click();

        await send(page, 'What is this?');

        await expect(transcript(page)).toContainText('A transistor.');

        const first = sent[0].body.messages[0].content;

        expect(first.map(block => block.type)).toEqual(['text', 'image', 'text', 'text']);
        expect(first[0].text).toBe('Attached file: shot.png');
        expect(first[1].source.media_type).toBe('image/png');
        expect(first[2].text.startsWith('<attachment name="rules.drc">')).toBe(true);
        expect(first[3].text).toBe('What is this?');

        await expect(page.locator('#aiPending')).toHaveCount(0);
        await expect(page.locator('.aiQuestionFiles .aiChip')).toHaveCount(2);

        await page.locator('#aiFiles .aiFileRow', { hasText: 'shot.png' }).locator('input').uncheck();

        await expect(page.locator('.aiQuestionFiles .aiChipOff')).toHaveText('shot.png');

        await send(page, 'And its gate?');

        await expect(transcript(page)).toContainText('Its gate.');

        const again = sent[1].body.messages[0].content;

        expect(JSON.stringify(again)).not.toContain(PNG.toString('base64'));
        expect(again.some(block => (block.text || '').includes('has since left it out of the conversation'))).toBe(true);
        expect(again.some(block => (block.text || '').startsWith('<attachment name="rules.drc">'))).toBe(true);
    });

    ///A screenshot pasted into the box goes with the question, named for when it was pasted - and is not put down on the drawing.
    test('a picture pasted into the box goes with the question, named for when it was pasted', async ({ page }) => {
        await withModel(page, CLAUDE);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        const before = await shapeCount(page);

        await page.locator('#aiPrompt').evaluate((box, base64) => {
            const data = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
            const transfer = new DataTransfer();

            transfer.items.add(new File([data], 'image.png', { type: 'image/png' }));
            box.focus();
            box.dispatchEvent(new ClipboardEvent('paste', { clipboardData: transfer, bubbles: true, cancelable: true }));
        }, PNG.toString('base64'));

        await expect(page.locator('#aiPending .aiChipName')).toHaveText(/^pasted-\d{6}\.png$/);

        expect(await shapeCount(page)).toBe(before);
    });

    ///A file let go over the conversation goes into the box - and a layout let go there is turned away, not opened over the one on screen.
    test('a file dropped on the conversation goes into the box, and a layout dropped there is turned away', async ({ page }) => {
        await withModel(page, CLAUDE);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        const before = await shapeCount(page);

        expect(await dropOn(page, '#aiTranscript', 'notes.txt', 'text/plain', Buffer.from('a note'))).toBe('"Drop to attach to your question"');

        await expect(page.locator('#aiPending .aiChipName')).toHaveText('notes.txt');

        await dropOn(page, '#aiTranscript', 'other.gds', 'application/octet-stream', Buffer.from([0, 6, 0, 2, 0, 3]));

        await expect(page.locator('#aiAttachProblem')).toContainText('other.gds is a layout. Open it with Open');

        expect(await shapeCount(page)).toBe(before);
    });

    ///Google's OpenAI-compatible endpoint documents no PDFs, so Send waits on one with a Gemini model, and says why.
    test('a PDF waits for a model that reads one, and the window says why', async ({ page }) => {
        await withModel(page, GEMINI);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        //A question to send, so what holds Send back is the PDF alone - the box starts empty since 2026-11-01.
        await page.locator('#aiPrompt').fill('What do these rules say?');
        await attach(page, { name: 'rules.pdf', mimeType: 'application/pdf', buffer: PDF });

        await expect(page.locator('#aiCannotSend')).toContainText('rules.pdf cannot go to a Gemini model');
        await expect(page.locator('#aiPending .aiChipBlocked')).toHaveCount(1);
        await expect(page.locator('#aiSend')).toBeDisabled();

        await page.locator('#aiPending .aiChipRemove').click();

        await expect(page.locator('#aiCannotSend')).toHaveCount(0);
        await expect(page.locator('#aiSend')).toBeEnabled();
    });

    ///
    ///**A model set to take PDFs as their text is sent their words** (2026-11-07, from GraphDBViewer): ticked under AI
    ///settings, the PDF Gemini cannot be sent as a file goes as the words PdfPig reads out of it in the page, inside its
    ///name's tags.
    ///
    test('a PDF goes to a model set to take PDFs as text as its words', async ({ page }) => {
        await withModel(page, { ...GEMINI, PdfAsText: true });

        const sent = await fakeProvider(page, GOOGLE, [geminiSays('0.14 µm.')]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await attach(page, { name: 'rules.pdf', mimeType: 'application/pdf', buffer: onePagePdf('met1 width is 0.14 um') });

        await expect(page.locator('#aiCannotSend')).toHaveCount(0);
        await expect(page.locator('#aiPending .aiChipBlocked')).toHaveCount(0);

        await send(page, 'How wide is met1?');

        await expect(transcript(page)).toContainText('0.14 µm.');

        const question = JSON.stringify(sent[0].body.messages.find(message => message.role === 'user'));

        expect(question).toContain('<attachment name=\\"rules.pdf\\">');
        expect(question).toContain('met1 width is 0.14 um');
        expect(question).not.toContain('application/pdf');
    });

    ///And one with no words, as a scan has none, keeps the question in the box and says why.
    test('a PDF with no words to read is not sent as text, and the window says why', async ({ page }) => {
        await withModel(page, { ...GEMINI, PdfAsText: true });

        const sent = await fakeProvider(page, GOOGLE, [geminiSays('Nothing.')]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await attach(page, { name: 'scan.pdf', mimeType: 'application/pdf', buffer: PDF });
        await send(page, 'What does it say?');

        await expect(page.locator('#aiAttachProblem')).toContainText('scanned PDF');
        await expect(page.locator('#aiPrompt')).toHaveValue('What does it say?');
        expect(sent).toHaveLength(0);
    });

    ///
    ///**An Excel workbook and a Word document go as their words** (2026-10-30), unpacked and read in the page: every sheet
    ///a row to a line with its number first and the date as a date, the document's paragraph and table - to Gemini as
    ///well, which is sent no file it cannot read. A workbook from before 2007 is turned away with what to save it as.
    ///
    test('an Excel workbook and a Word document go as their words, and an old workbook is turned away', async ({ page }) => {
        await withModel(page, GEMINI);

        const sent = await fakeProvider(page, GOOGLE, [geminiSays('0.14 µm.')]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await attach(
            page,
            { name: 'rules.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: WORKBOOK },
            { name: 'manual.docx', mimeType: '', buffer: DOCUMENT },
            { name: 'old.xls', mimeType: 'application/vnd.ms-excel', buffer: Buffer.from([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]) });

        await expect(page.locator('#aiPending .aiChip')).toHaveCount(2);
        await expect(page.locator('#aiAttachProblem')).toHaveText('old.xls is an Excel workbook from before 2007, which this page cannot read. Save it as an .xlsx, or a sheet as a CSV, and attach that.');
        await expect(page.locator('#aiCannotSend')).toHaveCount(0);

        await send(page, 'How wide is met1?');

        await expect(transcript(page)).toContainText('0.14 µm.');

        const question = sent[0].body.messages.at(-1).content;

        expect(question.map(part => part.type)).toEqual(['text', 'text', 'text']);
        expect(question[0].text).toBe('<attachment name="rules.xlsx">\n'
            + 'Sheet "Rules" - each line is a row: its number, then its cells, separated by tabs.\n'
            + '1\tLayer\tWidth (µm)\tChecked\n'
            + '2\tmet1\t0.14\t2023-11-01\n'
            + '</attachment>');
        expect(question[1].text).toBe('<attachment name="manual.docx">\nMetal rules\nmet1\t0.14 µm\n</attachment>');
        expect(question[2].text).toBe('How wide is met1?');
    });

    ///A refused file would be refused with every later question, so a failed question puts its files back in the box, and its words.
    test('a failed question gives its files back to the box, with its words', async ({ page }) => {
        await withModel(page, CLAUDE);
        await fakeProvider(page, ANTHROPIC, [{ type: 'error', error: { type: 'invalid_request_error', message: 'The PDF could not be read.' } }], 400);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await attach(page, { name: 'rules.pdf', mimeType: 'application/pdf', buffer: PDF });
        await send(page, 'Summarize the rules');

        await expect(transcript(page)).toContainText('The PDF could not be read.');
        await expect(page.locator('#aiPending .aiChipName')).toHaveText('rules.pdf');
        await expect(page.locator('#aiPrompt')).toHaveValue('Summarize the rules');
    });

    ///
    ///**What an answer cost is under it**, from what the provider reported; what has been spent and what the next
    ///request will be, with a link to where the provider publishes its rates, are on a line at the window's foot,
    ///on to begin with - and a press on the (i) in the box's row takes it off, after which the (i) shows them in a card
    ///while it is pointed at, until it is pressed again (2026-10-31; a card that opened on a press the day before, and
    ///a line under the row before that).
    ///
    test('what an answer cost shows under it, the spend is on a line at the foot, and the (i) takes it off for a hover card', async ({ page }) => {
        await withModel(page, CLAUDE);

        const reply = claudeSays('A transistor.');

        reply.usage = { input_tokens: 1000, output_tokens: 50, cache_read_input_tokens: 9000, cache_creation_input_tokens: 0 };

        await fakeProvider(page, ANTHROPIC, [reply]);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        const card = page.locator('#aiMeterCard');
        const toggle = page.locator('#aiMeterToggle');

        //On to begin with, under the box and at the window's foot, with no card beside it.
        await expect(toggle).toHaveAttribute('aria-pressed', 'true');
        await expect(toggle).toHaveAttribute('aria-label', /^Next request: about [\d,]+ tokens, \$[\d.]+ to send\./);
        await expect(page.locator('#aiMeter #aiEstimate')).toHaveText(/^Next request: about [\d,]+ tokens, \$[\d.]+ to send$/);
        await expect(page.locator('#aiMeter #aiRates')).toHaveAttribute('href', 'https://platform.claude.com/docs/en/about-claude/pricing');
        await expect(page.locator('#aiSpent')).toHaveCount(0);
        await expect(card).toHaveCount(0);

        const box = await page.locator('#askAiWindow .aiBox').boundingBox();
        const line = await page.locator('#aiMeter').boundingBox();
        const window = await page.locator('#askAiWindow').boundingBox();

        expect(line.y).toBeGreaterThanOrEqual(box.y + box.height);
        expect((window.y + window.height) - (line.y + line.height)).toBeLessThan(12);

        await send(page, 'What is this?');

        //On claude-opus-5-5: 1,000 fresh at $4, 9,000 from the cache at $0.20 and 50 out at $20 a million.
        await expect(page.locator('.aiUsage')).toHaveText('1 request · 10,000 tokens in (9,000 cached) · 50 out · $0.007');
        await expect(page.locator('#aiMeter #aiSpent')).toHaveText('Spent: $0.007');

        //A press elsewhere leaves it where it is.
        await page.locator('#aiTranscript').click();

        await expect(page.locator('#aiMeter')).toBeVisible();

        //Pressed, off - and then the (i) pointed at shows the same in a card, gone with the pointer.
        await toggle.click();

        await expect(page.locator('#aiMeter')).toHaveCount(0);
        await expect(toggle).toHaveAttribute('aria-pressed', 'false');

        await toggle.hover();

        await expect(card).toBeVisible();
        await expect(card.locator('#aiSpent')).toHaveText('Spent: $0.007');

        await page.locator('#aiTranscript').hover();

        await expect(card).toBeHidden();

        //Pressed again, on.
        await toggle.click();

        await expect(page.locator('#aiMeter')).toBeVisible();
        await expect(toggle).toHaveAttribute('aria-pressed', 'true');
    });

    ///A model nobody published a rate for shows no cost, until it is given its own rates under Settings.
    test('a model\'s own rates, set under Settings, price it', async ({ page }) => {
        await withModel(page, GEMINI);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await expect(page.locator('#aiMeter #aiEstimate')).toContainText('Next request: about');
        await expect(page.locator('#aiMeter #aiEstimate')).not.toContainText('$');

        await page.locator('#settingsMenu').click();
        await page.locator('#aiSettingsItem').click();

        await expect(page.locator('#aiPriceNote')).toContainText('No published rate is known here for gemini-test');

        await page.locator('#aiPriceInput').fill('1');
        await page.locator('#aiPriceOutput').fill('4');
        await page.locator('#aiModelSave').click();
        await page.locator('#aiSettingsWindow .closeButton').click();

        //The line under the box is still on, and prices it now; so do the (i)'s own words.
        await expect(page.locator('#aiMeter #aiEstimate')).toContainText('to send');
        await expect(page.locator('#aiMeterToggle')).toHaveAttribute('aria-label', /to send\./);
    });
});

test.describe('the tool picker, compacting and saving', () => {
    test.beforeEach(async ({ page }) => {
        await blockTheOutside(page);
    });

    ///
    ///**A tool switched off is not offered, and the model is told which**, so it says so rather than that it cannot.
    ///The picker counts what is off on its switch, and a press anywhere else - or Escape - puts it away.
    ///
    test('a tool switched off in the picker is not offered, and the model is told so', async ({ page }) => {
        await withModel(page, CLAUDE);

        const sent = await fakeProvider(page, ANTHROPIC, [claudeSays('Writing is switched off.')]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await page.locator('#aiToolPickerToggle').click();
        await page.locator('#aiToolPicker input[data-tool="draw_text"]').uncheck();

        await expect(page.locator('#aiToolPickerCount')).toHaveText('1');

        await page.locator('#aiPrompt').click();

        await expect(page.locator('#aiToolPicker')).toHaveCount(0);

        await page.locator('#aiToolPickerToggle').click();
        await page.keyboard.press('Escape');

        await expect(page.locator('#aiToolPicker')).toHaveCount(0);

        await send(page, 'Write MCP on met1');

        await expect(transcript(page)).toContainText('Writing is switched off.');

        const names = sent[0].body.tools.map(tool => tool.name);

        expect(names).not.toContain('draw_text');
        expect(names).toContain('draw_rectangle');
        expect(sent[0].body.system).toContain('switched these tools off in this conversation: draw_text.');
    });

    ///Compact in the picker says why it cannot be pressed with nothing to compact, and what it would cost once there is.
    test('the picker\'s Compact says what it would cost, or why it cannot be done', async ({ page }) => {
        await withModel(page, CLAUDE);
        await fakeProvider(page, ANTHROPIC, [claudeSays('A transistor.')]);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await page.locator('#aiToolPickerToggle').click();

        await expect(page.locator('#aiCompact')).toBeDisabled();
        await expect(page.locator('#aiCompactPrice')).toHaveText('There is nothing to compact yet.');

        await page.locator('#aiToolPickerToggle').click();
        await send(page, 'What is this?');

        await expect(transcript(page)).toContainText('A transistor.');

        await page.locator('#aiToolPickerToggle').click();

        await expect(page.locator('#aiCompact')).toBeEnabled();
        await expect(page.locator('#aiCompactPrice')).toHaveText(/^Reads about [\d,]+ tokens of history once, about \$[\d.]+\.$/);
    });

    ///
    ///**A long conversation is offered compacting, priced**, after three questions; compacted, it shows the briefing in
    ///place of what came before - folded away, there when asked for - keeps the picture on it unticked, and sends the
    ///briefing with the next question rather than the conversation.
    ///
    test('a long conversation is offered compacting, and after it the briefing is sent in its place', async ({ page }) => {
        await withModel(page, CLAUDE);

        const long = claudeSays('a'.repeat(40000));
        const sent = await fakeProvider(page, ANTHROPIC, [long, long, long, claudeSays('Working on: the gate of Mosfet.'), claudeSays('It is on poly.')]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await attach(page, { name: 'shot.png', mimeType: 'image/png', buffer: PNG });

        const questions = ['One', 'Two', 'Three'];

        for (let i = 0; i < questions.length; i++) {
            await send(page, questions[i]);

            await expect(page.locator('.aiBubbleAssistant')).toHaveCount(i + 1);
        }

        await expect(page.locator('#aiCompactSuggestion')).toContainText(/sends about [\d,]+ tokens of history with every question, \$[\d.]+ each time/);

        await page.locator('#aiCompactNow').click();

        await expect(page.locator('.aiSummary')).toContainText('Working on: the gate of Mosfet.');
        await expect(page.locator('#aiCompactSuggestion')).toHaveCount(0);
        await expect(page.locator('.aiBubbleUser').first()).toBeHidden();

        await page.locator('#aiShowEarlier').click();

        await expect(page.locator('.aiBubbleUser').first()).toBeVisible();

        expect(sent[3].body.system).toContain('You compress a conversation');
        expect(sent[3].body.tools).toBeUndefined();

        const kept = page.locator('#aiFiles .aiFileRow', { hasText: 'shot.png' }).last();

        await expect(kept.locator('input')).not.toBeChecked();
        await expect(page.locator('#aiFiles')).toContainText('From before the summary');

        await send(page, 'Which layer is the gate on?');

        await expect(transcript(page)).toContainText('It is on poly.');

        const after = JSON.stringify(sent[4].body.messages);

        expect(after).toContain('Summary of the earlier conversation, compacted to save tokens:');
        expect(after).toContain('Working on: the gate of Mosfet.');
        expect(after).not.toContain('a'.repeat(100));
        expect(after).not.toContain(PNG.toString('base64'));
    });
});

test.describe('chats', () => {
    test.beforeEach(async ({ page }) => {
        await blockTheOutside(page);
        await withModel(page, CLAUDE);
    });

    ///
    ///**A chat is kept from its first question, and opened again from the chats panel** down the window's left - the
    ///Claude app's sidebar, asked for 2026-10-31. Opened again, it says so, and the model is sent all of it with the
    ///warning that the shape numbers in it were another session's; and the list is still there after a reload, since it
    ///is kept in this browser (IndexedDB, through the IAiService the app registers).
    ///
    test('a chat is kept from its first question, and opened again and carried on from the chats panel', async ({ page }) => {
        const sent = await fakeProvider(page, ANTHROPIC, [claudeSays('A transistor.'), claudeSays('Four.'), claudeSays('Still a transistor.')]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        //Open to begin with, the chat on screen its first row before anything is kept.
        await expect(page.locator('#aiChatNow .aiChatTitle')).toHaveText('New chat');
        await expect(page.locator('#aiChats .aiChatsKept')).toHaveText('Kept in this browser');

        await send(page, 'What is this?');

        await expect(transcript(page)).toContainText('A transistor.');

        const rows = page.locator('#aiChats .aiChatItem[data-chat]');

        await expect(rows).toHaveCount(1);
        await expect(rows.first().locator('.aiChatTitle')).toHaveText('What is this?');
        await expect(rows.first()).toHaveAttribute('aria-current', 'true');

        await page.locator('#aiChatsNew').click();
        await send(page, 'How many layers?');

        await expect(transcript(page)).toContainText('Four.');
        await expect(transcript(page)).not.toContainText('A transistor.');

        //The one changed last first.
        await expect(rows.locator('.aiChatTitle')).toHaveText(['How many layers?', 'What is this?']);

        await rows.nth(1).click();

        await expect(transcript(page)).toContainText('A transistor.');
        await expect(transcript(page)).not.toContainText('Four.');
        await expect(transcript(page).locator('.aiTurnNote')).toHaveText('Opened again.');
        await expect(rows.nth(1)).toHaveAttribute('aria-current', 'true');

        await send(page, 'And now?');

        await expect(transcript(page)).toContainText('Still a transistor.');

        const carried = JSON.stringify(sent[2].body.messages);

        expect(carried).toContain('What is this?');
        expect(carried).toContain('A transistor.');
        expect(carried).toContain('no longer name the same shapes');
        expect(carried).not.toContain('How many layers?');

        //Kept through a reload, with the one carried on at the top.
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await expect(rows.locator('.aiChatTitle')).toHaveText(['What is this?', 'How many layers?']);
        await expect(transcript(page)).toContainText('Nothing is sent until you press Send');
    });

    ///
    ///**Saved as text and deleted from its own row** - where Export and Clear in the title bar went. The bin asks in the
    ///row rather than in a dialog, and the chat on screen deleted gives way to a new one.
    ///
    test('a chat is saved as text from its row, and deleted from it after asking there', async ({ page }) => {
        const reply = claudeSays('A transistor.');

        reply.usage = { input_tokens: 1000, output_tokens: 50 };

        await fakeProvider(page, ANTHROPIC, [reply]);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);
        await send(page, 'What is this?');

        await expect(transcript(page)).toContainText('A transistor.');

        const row = page.locator('#aiChats .aiChatItem[data-chat]').first();

        await row.hover();

        const started = page.waitForEvent('download');

        await row.locator('.aiChatExport').click();

        const download = await started;
        const text = fs.readFileSync(await download.path(), 'utf8');

        expect(download.suggestedFilename()).toMatch(/^ai-assistant-Mosfet-\d{8}-\d{4}\.txt$/);
        expect(text).toContain('AI assistant conversation - Mosfet');
        expect(text).toContain('YOU\nWhat is this?');
        expect(text).toContain('ASSISTANT\nA transistor.');

        //1,000 in at $4 and 50 out at $20 a million.
        expect(text).toContain('Billed: 1 request · 1,000 tokens in · 50 out · $0.005, on claude-opus-5-5');

        //The bin asks in the row; Keep leaves it be.
        await row.locator('.aiChatDelete').click();

        await expect(page.locator('#aiChats .aiChatItemDeleting')).toContainText('Delete this chat?');

        await page.locator('#aiChats .aiChatDeleteNo').click();

        await expect(page.locator('#aiChats .aiChatItem[data-chat] .aiChatTitle')).toHaveText('What is this?');

        await row.hover();
        await row.locator('.aiChatDelete').click();
        await page.locator('#aiChats .aiChatDeleteYes').click();

        await expect(page.locator('#aiChats .aiChatItem[data-chat]')).toHaveCount(0);
        await expect(page.locator('#aiChatNow')).toBeVisible();
        await expect(transcript(page)).toContainText('Nothing is sent until you press Send');

        //And it stays gone.
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await expect(page.locator('#aiChatNow')).toBeVisible();
        await expect(page.locator('#aiChats .aiChatItem[data-chat]')).toHaveCount(0);
    });

    ///
    ///**Another chat is not opened while an answer is coming**, which belongs to the one on screen - Stop first. Nor is a
    ///new one started.
    ///
    test('another chat is not opened, nor a new one started, while an answer is coming', async ({ page }) => {
        let answered = 0;

        await page.route(ANTHROPIC, async route => {
            if (route.request().method() === 'OPTIONS') {
                await route.fulfill({ status: 204, headers: CORS });

                return;
            }

            answered++;

            if (answered === 1) {
                await route.fulfill({ status: 200, headers: { ...CORS, 'Content-Type': 'application/json' }, body: JSON.stringify(claudeSays('A transistor.')) });

                return;
            }

            //Held: the chat stops it, and the browser lets go of it.
            await new Promise(() => { });
        });

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);
        await send(page, 'What is this?');

        await expect(transcript(page)).toContainText('A transistor.');

        await page.locator('#aiChatsNew').click();
        await send(page, 'Draw a square');

        await expect(page.locator('#aiSend')).toHaveAttribute('aria-label', 'Stop');
        await expect(page.locator('#aiChatsNew')).toBeDisabled();

        await page.locator('#aiChats .aiChatItem').filter({ hasText: 'What is this?' }).click();

        await expect(transcript(page)).toContainText('Draw a square');
        await expect(transcript(page)).not.toContainText('A transistor.');

        await page.locator('#aiSend').click();

        await expect(transcript(page)).toContainText('Stopped.');
        await expect(page.locator('#aiChatsNew')).toBeEnabled();
    });
});

test.describe('the panels', () => {
    test.beforeEach(async ({ page }) => {
        await blockTheOutside(page);
        await withModel(page, CLAUDE);
    });

    ///
    ///**The chats and the files are open to begin with, each closed by the cross at its top right and opened again by the
    ///arrow in the window's margin**, there only while it is closed (2026-11-07, from GraphDBViewer), and sized by dragging
    ///its divider, the size kept for the next visit (2026-10-31, asked for, in place of two switches in the title bar).
    ///
    test('the panels open to begin with, close by their crosses, open by their arrows, and are sized by their dividers, the size kept', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await expect(page.locator('#aiChats')).toBeVisible();
        await expect(page.locator('#aiFiles')).toBeVisible();

        //No arrow while its panel is open: the cross at the panel's top right closes it.
        await expect(page.locator('#aiChatsToggle')).toHaveCount(0);
        await expect(page.locator('#aiFilesToggle')).toHaveCount(0);

        const chats = await page.locator('#aiChats').boundingBox();
        const cross = await page.locator('#aiChatsClose').boundingBox();
        const files = await page.locator('#aiFiles').boundingBox();
        const filesCross = await page.locator('#aiFilesClose').boundingBox();

        expect(cross.x + cross.width).toBeGreaterThan(chats.x + (chats.width / 2));
        expect(cross.y).toBeLessThan(chats.y + 40);
        expect(filesCross.x + filesCross.width).toBeGreaterThan(files.x + (files.width / 2));
        expect(filesCross.y).toBeLessThan(files.y + 40);

        await page.locator('#aiChatsClose').click();
        await page.locator('#aiFilesClose').click();

        await expect(page.locator('#aiChats')).toHaveCount(0);
        await expect(page.locator('#aiFiles')).toHaveCount(0);

        //Each arrow in the window's margin on its side, outside the conversation.
        const window = await page.locator('#askAiWindow').boundingBox();
        const arrow = await page.locator('#aiChatsToggle').boundingBox();
        const filesArrow = await page.locator('#aiFilesToggle').boundingBox();
        const conversation = await page.locator('#askAiWindow .aiBox').boundingBox();

        await expect(page.locator('#aiChatsToggle')).toHaveAttribute('aria-expanded', 'false');
        expect(arrow.x).toBeGreaterThan(window.x);
        expect(arrow.x + arrow.width).toBeLessThan(conversation.x);
        expect(filesArrow.x).toBeGreaterThan(conversation.x + conversation.width);
        expect(filesArrow.x + filesArrow.width).toBeLessThan(window.x + window.width);

        await page.locator('#aiChatsToggle').click();
        await page.locator('#aiFilesToggle').click();

        await expect(page.locator('#aiChats')).toBeVisible();
        await expect(page.locator('#aiFiles')).toBeVisible();

        //Dragged by the divider, away from its arrow: 80 pixels wider.
        const before = (await page.locator('#aiChats').boundingBox()).width;
        const split = await page.locator('.aiSplit[data-split="chats"]').boundingBox();
        const x = split.x + (split.width / 2);
        const y = split.y + 20;

        await page.mouse.move(x, y);
        await page.mouse.down();
        await page.mouse.move(x + 80, y, { steps: 5 });
        await page.mouse.up();

        await expect.poll(async () => Math.round((await page.locator('#aiChats').boundingBox()).width)).toBe(Math.round(before + 80));

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await expect.poll(async () => Math.round((await page.locator('#aiChats').boundingBox()).width)).toBe(Math.round(before + 80));

        //The files the other way: dragged left, wider.
        const filesBefore = (await page.locator('#aiFiles').boundingBox()).width;
        const filesSplit = await page.locator('.aiSplit[data-split="files"]').boundingBox();
        const fx = filesSplit.x + (filesSplit.width / 2);
        const fy = filesSplit.y + 20;

        await page.mouse.move(fx, fy);
        await page.mouse.down();
        await page.mouse.move(fx - 40, fy, { steps: 5 });
        await page.mouse.up();

        await expect.poll(async () => Math.round((await page.locator('#aiFiles').boundingBox()).width)).toBe(Math.round(filesBefore + 40));
    });
});

//
//
//**Slash commands** (2026-10-31, asked for): a slash at the start of the box lists what the window can do, narrowed as a
//name is typed - the arrows move through it, Enter runs the one marked, Escape puts it away - and a command written out
//with words after it runs with them.
//
test.describe('slash commands', () => {
    test.beforeEach(async ({ page }) => {
        await blockTheOutside(page);
        await withModel(page, CLAUDE);
    });

    ///
    ///**Listed, narrowed and put away**: every command, the ones that cannot run now dimmed with why - nothing to compact
    ///in a new chat - one name left as more is typed, and Escape hides the list until the words change.
    ///
    test('a slash lists the commands, narrowed as a name is typed, and Escape puts them away', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        const box = page.locator('#aiPrompt');
        const menu = page.locator('#aiSlashMenu');

        await box.fill('/');

        await expect(menu).toBeVisible();

        for (const name of ['new', 'compact', 'export', 'tools', 'camera', 'screenshot', 'settings', 'describe', 'check'])
            await expect(menu.locator(`[data-command="${name}"]`)).toHaveCount(1);

        await expect(menu.locator('[data-command="compact"]')).toHaveClass(/aiSlashItemOff/);
        await expect(menu.locator('[data-command="tools"]')).not.toHaveClass(/aiSlashItemOff/);
        await expect(menu.locator('.aiSlashItem').first()).toHaveAttribute('aria-selected', 'true');

        await box.press('ArrowDown');

        await expect(menu.locator('.aiSlashItem').nth(1)).toHaveAttribute('aria-selected', 'true');

        await box.press('ArrowUp');
        await box.press('ArrowUp');

        await expect(menu.locator('.aiSlashItem').last()).toHaveAttribute('aria-selected', 'true');

        await box.fill('/ex');

        await expect(menu.locator('.aiSlashItem')).toHaveCount(1);
        await expect(menu.locator('[data-command="export"]')).toHaveCount(1);

        await box.press('Escape');

        await expect(menu).toHaveCount(0);
        await expect(box).toHaveValue('/ex');

        await box.press('p');

        await expect(menu).toBeVisible();

        await box.fill('/ex nothing');

        await expect(menu).toHaveCount(0);
    });

    ///
    ///**A question command sends its question, with the words after it**; and an action runs from the list: /tools opens
    ///the picker, /new starts a chat with the box empty, and a command that cannot run says why.
    ///
    test('/check sends its question with the words after it, and /tools and /new run from the list', async ({ page }) => {
        const sent = await fakeProvider(page, ANTHROPIC, [claudeSays('No faults on met1.')]);

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        const box = page.locator('#aiPrompt');

        await box.fill('/compact');
        await box.press('Enter');

        await expect(page.locator('#aiSlashProblem')).toContainText('/compact:');
        expect(sent).toHaveLength(0);

        await box.fill('/check met1 only');
        await box.press('Enter');

        await expect(transcript(page)).toContainText('No faults on met1.');

        const asked = JSON.stringify(sent[0].body.messages);

        expect(asked).toContain('Check the open cell against the design rules');
        expect(asked).toContain('met1 only');
        await expect(page.locator('#aiSlashProblem')).toHaveCount(0);

        await box.fill('/t');
        await box.press('Enter');

        await expect(page.locator('#aiToolPicker')).toBeVisible();
        await expect(box).toHaveValue('');

        await page.keyboard.press('Escape');
        await box.fill('/new');
        await page.locator('#aiSlashMenu [data-command="new"]').click();

        await expect(transcript(page)).not.toContainText('No faults on met1.');
        await expect(box).toHaveValue('');
    });
});

//**The microphone** (2026-10-31, asked for): speech into the box by the browser's own recognition - the default - or
//recorded and written out by the AI provider, chosen under Settings, AI settings. No real speech is heard: the browser's
//recognizer is a stand-in that says its words when started. The recording, which needs Chromium's fake microphone and so
//a browser of its own, is ask-ai-devices.spec.js's.
//
test.describe('the microphone', () => {
    test.beforeEach(async ({ page }) => {
        await blockTheOutside(page);
        await withModel(page, GEMINI);
    });

    ///A recognizer that hears "draw a met1 square" when started - a guess first, then the settled words - and then ends.
    async function fakeRecognizer(page) {
        await page.addInitScript(() => {
            //Under both names: Chromium has the unprefixed one too now, which would be the real one.
            window.SpeechRecognition = window.webkitSpeechRecognition = class {
                start() {
                    setTimeout(() => {
                        const said = (text, isFinal) => {
                            const result = [{ transcript: text }];

                            result.isFinal = isFinal;

                            return result;
                        };

                        this.onresult({ resultIndex: 0, results: [said('draw a', false)] });
                        this.onresult({ resultIndex: 0, results: [said('draw a met1 square', true)] });
                        this.onend();
                    }, 50);
                }

                stop() { }

                abort() { }
            };
        });
    }

    ///
    ///**By the browser to begin with**: the words go into the box, and the microphone says, when
    ///pointed at, that the provider is more accurate and where to switch to it.
    ///
    test('the browser\'s recognition writes into the box, and the microphone points to the provider for accuracy', async ({ page }) => {
        await fakeRecognizer(page);
        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        const mic = page.locator('#aiMic');

        await expect(mic).toHaveAttribute('title', /your browser writes it into the box as you talk/);
        await expect(mic).toHaveAttribute('title', /For better accuracy .* AI provider under Settings, AI settings/);

        await mic.click();

        await expect(page.locator('#aiPrompt')).toHaveValue('draw a met1 square');
        await expect(mic).toHaveAttribute('aria-pressed', 'false');
        await expect(page.locator('#aiSpeechProblem')).toHaveCount(0);
    });

    ///
    ///**Switched under Settings, AI settings**, kept for the next visit, and the microphone says what it does now.
    ///
    test('the setting switches the microphone to the provider, and is kept', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await page.locator('#settingsMenu').click();
        await page.locator('#aiSettingsItem').click();

        await expect(page.locator('#aiSpeechBrowser')).toHaveAttribute('aria-pressed', 'true');

        await page.locator('#aiSpeechProvider').click();

        await expect(page.locator('#aiSpeechProvider')).toHaveAttribute('aria-pressed', 'true');
        await expect(page.locator('#aiSpeechNote')).toContainText('More accurate');

        await page.locator('#aiSettingsWindow .closeButton').click();
        await openAssistant(page);

        await expect(page.locator('#aiMic')).toHaveAttribute('title', /an OpenAI or Gemini model writes it out/);

        await expect.poll(() => page.evaluate(async () => window.gdsStorage.get('gdsviewer.speechInput')), { timeout: 15000 }).toContain('Provider');

        await gotoExample(page, MOSFET, '2d');
        await openAssistant(page);

        await expect(page.locator('#aiMic')).toHaveAttribute('title', /an OpenAI or Gemini model writes it out/);
    });
});
