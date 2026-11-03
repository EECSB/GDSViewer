//The live link: a GDS Viewer tab and a real `gds mcp`, joined by the code the tab shows.
//
//The protocol on both ends is C#'s and covered in LiveLinkTests, the app's own LiveLink against the server's
//TabLink in one process. What only a browser can show is the rest: that the page's WebSocket reaches the server,
//that what a model does over it lands in the view and on the tab's own undo, that look is the tab's picture, and
//that Stop in the tab is the end of it for the model too.
//
//`gds mcp` is the one the C# build makes - `dotnet build` or `dotnet test` first - spoken to the way a client speaks
//to it: newline-delimited JSON-RPC on its standard input and output. On a port of its own, so a gds mcp somebody
//has running for their own Claude is never the one the tab finds.
const { test, expect } = require('@playwright/test');
const { spawn } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const readline = require('readline');
const { gotoExample, selectExample, shapeCount, MOSFET, SKY130_CELL } = require('./helpers');

const GDS = path.join(__dirname, '..', '..', 'GdsII.Cli', 'bin', 'Debug', 'net10.0', 'gds.dll');

///A port a worker's server has to itself: the specs run side by side, and one port takes one listener.
function portOf(info) {
    return 43790 + info.parallelIndex;
}

///
///`gds mcp` in a folder of its own, with a client's half of the protocol: ask sends a request and waits for its
///answer, call is tools/call, and tell is a notification, which has none.
///
function startGdsMcp(folder, port) {
    const server = spawn('dotnet', [GDS, 'mcp', folder, '--link-port', String(port)], { stdio: ['pipe', 'pipe', 'pipe'] });
    const waiting = new Map();
    const said = [];
    let next = 1;

    readline.createInterface({ input: server.stdout }).on('line', line => {
        const message = JSON.parse(line);

        if (message.id !== undefined && waiting.has(message.id)) {
            waiting.get(message.id)(message);
            waiting.delete(message.id);
        }
    });

    readline.createInterface({ input: server.stderr }).on('line', line => said.push(line));

    const ask = (method, params) => new Promise((resolve, reject) => {
        const id = next++;
        const late = setTimeout(() => reject(new Error(`gds mcp did not answer ${method} in 60 s`)), 60000);

        waiting.set(id, message => {
            clearTimeout(late);
            resolve(message);
        });

        server.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
    });

    const tell = (method) => server.stdin.write(JSON.stringify({ jsonrpc: '2.0', method }) + '\n');

    const call = async (name, args = {}) => (await ask('tools/call', { name, arguments: args })).result;

    return { server, ask, tell, call, said };
}

///The text a tool answered.
function textOf(result) {
    return result.content.filter(block => block.type === 'text').map(block => block.text).join('\n');
}

test.describe('the live link', () => {
    test.skip(!fs.existsSync(GDS), 'needs gds mcp built: dotnet build');

    let mcp;
    let folder;
    let port;

    test.beforeEach(async ({ page }, info) => {
        folder = fs.mkdtempSync(path.join(os.tmpdir(), 'gds-link-'));
        port = portOf(info);
        mcp = startGdsMcp(folder, port);

        await mcp.ask('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'e2e', version: '0' } });

        mcp.tell('notifications/initialized');

        await gotoExample(page, MOSFET, 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
    });

    test.afterEach(async () => {
        mcp.server.kill();

        try {
            fs.rmSync(folder, { recursive: true, force: true });
        } catch { }
    });

    ///Settings, AI settings, the port this spec's server is on in the MCP server card, and Start: the code the tab then shows.
    async function startLink(page) {
        await page.locator('#settingsMenu').click();
        await page.locator('#aiSettingsItem').click();

        await expect(page.locator('#aiSettingsWindow #liveLinkCard')).toBeVisible();

        await page.locator('#liveLinkPort').fill(String(port));
        await page.locator('#liveLinkPort').blur();
        await page.locator('#liveLinkStart').click();

        await expect(page.locator('#liveLinkCode')).toHaveText(/^\d{4} \d{4}$/);

        return (await page.locator('#liveLinkCode').textContent()).trim();
    }

    async function linked(page) {
        const code = await startLink(page);
        const result = await mcp.call('link_tab', { code });

        expect(result.isError, textOf(result)).toBeFalsy();

        await expect(page.locator('#liveLinkStatus')).toContainText('Linked to e2e');

        return result;
    }

    test('the code the tab shows links it, and a change lands on screen as one step of its undo', async ({ page }) => {
        const result = await linked(page);

        //The link says what is open, so the model starts from where the person is.
        expect(textOf(result)).toContain('Mosfet');

        const before = await shapeCount(page);
        const drawn = await mcp.call('draw_rectangle', { layer: '68/20', from: [0, 0], to: [2, 1] });

        expect(drawn.isError, textOf(drawn)).toBeFalsy();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before + 1);
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo AI draw rectangle/);

        //And it is listed, with what the tab answered.
        await expect(page.locator('#liveLinkCalls .liveLinkCall').first()).toContainText('draw rectangle');

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
    });

    test('look answers with the tab\'s own picture, and describe_tab with what is open', async ({ page }) => {
        await linked(page);

        const looked = await mcp.call('look');

        expect(looked.isError, textOf(looked)).toBeFalsy();

        const picture = looked.content.find(block => block.type === 'image');

        expect(picture.mimeType).toBe('image/png');
        expect(Buffer.from(picture.data, 'base64').subarray(0, 4).toString('hex')).toBe('89504e47');

        const described = await mcp.call('describe_tab');

        expect(textOf(described)).toContain('The file is Mosfet');
    });

    test('Stop in the tab ends the link, and the next call is refused rather than run on the folder', async ({ page }) => {
        await linked(page);

        await page.locator('#liveLinkStop').click();

        await expect(page.locator('#liveLinkStart')).toBeVisible();

        await expect.poll(async () => {
            const result = await mcp.call('list_layers');

            if (result.isError)
                return textOf(result);

            return 'still linked';
        }, { timeout: 15000 }).toContain('closed the link');
    });

    test('while linked the file tools wait, and unlink_tab ends it in the tab too', async ({ page }) => {
        await linked(page);

        const saving = await mcp.call('save_file', { path: 'copy.gds' });

        expect(saving.isError).toBe(true);
        expect(textOf(saving)).toContain('save_file waits');

        const unlinked = await mcp.call('unlink_tab');

        expect(textOf(unlinked)).toContain('The link to the GDS Viewer tab is ended');

        await expect(page.locator('#liveLinkStatus')).toContainText('The link ended: Claude ended the link.');
        await expect(page.locator('#liveLinkStart')).toHaveText('Link again');
    });

    test('a wrong code is refused at once, and the tab goes on waiting', async ({ page }) => {
        await startLink(page);

        //Waiting on the server already, so the refusal is the quick one.
        await expect.poll(() => mcp.said.some(line => line.includes('is waiting to be linked')), { timeout: 15000 }).toBe(true);

        const wrong = await mcp.call('link_tab', { code: '0000 0001' });

        expect(wrong.isError).toBe(true);
        expect(textOf(wrong)).toContain('No GDS Viewer tab offered 0000 0001');

        await expect(page.locator('#liveLinkStatus')).toContainText('Waiting for gds mcp');
    });

    ///
    ///Shape numbers are one file's: another opened under the linked tab starts them again, and the next answer says
    ///so, since a number the model holds would otherwise name the wrong shape or none.
    ///
    test('another file opened under the link starts shape numbers again, and says so', async ({ page }) => {
        await linked(page);

        expect((await mcp.call('draw_rectangle', { layer: '68/20', from: [0, 0], to: [2, 1] })).isError).toBeFalsy();

        await page.locator('#aiSettingsWindow .closeButton').click();

        await selectExample(page, `${SKY130_CELL}.gds`);

        const listed = await mcp.call('list_layers');

        expect(textOf(listed)).toContain(`The person has ${SKY130_CELL}`);
        expect(textOf(listed)).toContain('open now, so shape numbers start again');
    });

    ///Put away, AI settings leave a chip saying the tab can be changed from outside, which brings them back at the card.
    test('closed, AI settings leave a chip that says so and open again at the MCP server', async ({ page }) => {
        await linked(page);

        await page.locator('#aiSettingsWindow .closeButton').click();

        await expect(page.locator('#liveLinkChip')).toContainText('Linked to Claude');

        await page.locator('#liveLinkChip').click();

        await expect(page.locator('#aiSettingsWindow')).toBeVisible();
        await expect(page.locator('#liveLinkChip')).toHaveCount(0);

        //Scrolled to, below the models' card: its top inside the window's body.
        await expect.poll(() => page.evaluate(() => {
            const body = document.querySelector('#aiSettingsWindow .aiWindowBody').getBoundingClientRect();
            const card = document.getElementById('liveLinkCard').getBoundingClientRect();

            return card.top >= body.top && card.bottom <= body.bottom + 1;
        })).toBe(true);
    });

    ///The settings menu has no line of its own for it any more: it is a card in AI settings, since 2026-11-03.
    test('the MCP server is a card in AI settings, not a line of the settings menu', async ({ page }) => {
        await page.locator('#settingsMenu').click();

        await expect(page.locator('#aiSettingsItem')).toBeVisible();
        await expect(page.locator('#liveLinkItem')).toHaveCount(0);

        await page.locator('#aiSettingsItem').click();

        await expect(page.locator('#liveLinkCard .aiSettingsCardTitle')).toHaveText('MCP server');
        await expect(page.locator('#liveLinkStart')).toBeVisible();
    });

    ///The whole request, not the digits alone: what is pasted into the chat is what the card says to tell Claude.
    test('Copy puts the request with its code on the clipboard, and says so', async ({ page, context }) => {
        await context.grantPermissions(['clipboard-read', 'clipboard-write']);

        const code = await startLink(page);

        await page.locator('#liveLinkCopy').click();

        //An icon alone, so what it says is its name - and its tick, for the moment it says Copied.
        await expect(page.locator('#liveLinkCopy')).toHaveAttribute('aria-label', 'Copied');
        expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(`Link to my GDS Viewer tab, code ${code}`);

        await expect(page.locator('#liveLinkCopy')).toHaveAttribute('aria-label', 'Copy');
    });
});
