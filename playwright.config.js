//Playwright e2e configuration. Starts the app's host itself (the same dotnet run the manual preview
//workflow uses) and runs the specs in e2e/ against it. The first startup compiles the app, so the
//webServer timeout is generous.
//
//--no-launch-profile on purpose: the URL is pinned here rather than taken from launchSettings.json, so
//the specs keep pointing at the port they expect however the profiles are changed.
const { defineConfig, devices } = require('@playwright/test');

module.exports = defineConfig({
    testDir: './e2e',
    timeout: 90000,
    expect: { timeout: 15000 },
    fullyParallel: true,

    //
    //Capped, rather than one worker per core.
    //
    //Every spec shares one dev server, and each one loads the WASM runtime, fetches an example and waits
    //on interop round trips. Past about four in flight the server is the bottleneck and specs start timing
    //out on work that is only slow, not wrong - a different one each run, every one of them passing on its
    //own. That is the worst kind of red, because it teaches you to re-run rather than to look.
    //
    workers: 4,

    //One retry, so a spec that fails for a reason nothing here controls is retried rather than believed
    //immediately. A spec that fails twice has something to say.
    retries: 1,

    reporter: [['list']],
    use: {
        baseURL: 'http://localhost:5105',
        viewport: { width: 1400, height: 900 },
        trace: 'on-first-retry'
    },
    projects: [
        {
            name: 'chromium',
            use: { ...devices['Desktop Chrome'] }
        }
    ],
    webServer: {
        command: 'dotnet run --no-launch-profile --project GDSViewer.csproj --urls http://localhost:5105',
        url: 'http://localhost:5105',
        timeout: 300000,

        //
        //**Never reuse what is already on the port.**
        //
        //This used to reuse a server already listening, so a preview left open was used rather than fought
        //over. What that actually bought was a run against whatever source that server was built from -
        //which is fine until it is not the source on disk, and then the specs are answering a question about
        //code that no longer exists.
        //
        //It cost most of a day. A run reported three failures that matched, exactly, a deliberate bug that
        //had already been taken back out; the source was clean and the server was not. A false red is worse
        //than a slow suite, because the only way to catch it is to already suspect it.
        //
        //The price is that a preview open on this port now fails the run instead of serving it. That is a
        //message that says what to do, which is what the old behavior never gave.
        //
        reuseExistingServer: false
    }
});
