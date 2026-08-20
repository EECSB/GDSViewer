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
    //Twelve here, four on CI, and both numbers are measured rather than reasoned.
    //
    //It was pinned at 4 on the belief that the shared dev server was the bottleneck past that. It is not:
    //sampled through a run the processor sits at 100% with memory to spare, so what runs out is cores.
    //Each worker drives its own browser booting its own copy of the WASM runtime, and that parallelizes.
    //On one 189-test subset, eleven physical cores and twenty-two logical:
    //
    //    workers   subset wall
    //    8         114s
    //    12         92s   <- here
    //    16         82s
    //    20        125s   and slower, not merely no faster
    //
    //**Sixteen looks best on that table and is not.** It ran the whole suite clean once in 7.9 minutes and
    //then came back flaky in 9.1, and the test that went was large-layout.spec.js asserting a frame
    //budget. Some of these specs measure *performance*, so a worker count that saturates the machine makes
    //them fail for being slow rather than wrong - a different one each run, every one passing on its own.
    //
    //**And a GitHub runner is four vCPUs, where twelve is three times oversubscribed.** The first CI run at
    //twelve took 21.9 minutes against 7.9 here, went flaky in three specs, and failed outright in
    //large-layout - which shells out to `dotnet run` mid-suite, so its build was competing with twelve
    //browsers for four cores. `process.env.CI` is set by Actions, so each machine gets a number that suits
    //it rather than one of them getting the other's.
    //
    //Re-measure after changing the machine, and take two full runs before believing a number: time a
    //subset at a few settings, run the whole suite at the winner twice, and check nothing came back flaky.
    //
    //**What this no longer buys much of is wall clock on the whole suite.** 792 of the 801 specs finish in
    //5.7 minutes; the whole suite takes about eight. The difference is the nine specs in
    //large-layout.spec.js, one of which is a single four-and-a-half-minute test that no number of workers
    //can divide.
    //
    workers: process.env.CI ? 4 : 12,

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
