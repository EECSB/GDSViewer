//
//The AI assistant's microphone (2026-10-31, asked for): speech into the box, two ways.
//
//**The browser's own recognition**, the default: the words appear in the box as they are spoken, through the Web Speech
//API - Chrome's and Edge's, which send the audio to Google's and Microsoft's servers to be recognized. Other browsers
//have none, and the window says so on the button.
//
//**A recording sent to the AI provider**, chosen under Settings, AI settings: the microphone records until it is pressed
//again, and the recording goes - as a 16 kHz WAV, see speechWav.js - to the model's provider to be written out. Slower
//to come back and billed, but better at what a layout conversation is full of, and in any browser with a microphone.
//
//**A level meter while it listens** (asked for the same day, as the Claude app has one): bars beside Send that rise
//and fall with what the microphone hears, newest on the right, so a muted or wrong microphone shows at once rather than
//after a silence. The recognizer hands the page no sound, so in that mode the meter opens the microphone a second time
//for itself; a recording's own stream is measured as it is.
//
//Exposed as window.gdsSpeech; the window, AskAi.razor, calls it and is called back on OnSpeechEnded when either ends.
//
(function () {
    'use strict';

    //The longest recording sent to a provider. A question, not a meeting.
    const MOST_SECONDS = 120;

    let recognition = null;
    let recorder = null;
    let stream = null;
    let chunks = [];
    let limit = null;
    let caller = null;
    let meter = null;

    //How often the bars move along, and how loud a voice fills one: speech near the microphone is around 0.1 RMS.
    const LEVEL_MS = 70;
    const FULL_RMS = 0.12;

    function recognizer() {
        return window.SpeechRecognition || window.webkitSpeechRecognition || null;
    }

    //
    //Starts the bars in the element given - one child per bar - from the stream given, or from a stream of the meter's
    //own. Each tick takes the loudness of the last moment and moves the bars along one, so they read as a scrolling trace.
    //
    async function startMeter(element, from) {
        stopMeter();

        if (!element || !window.AudioContext && !window.webkitAudioContext)
            return;

        const own = !from;
        let source = from;

        if (own) {
            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia)
                return;

            try {
                source = await navigator.mediaDevices.getUserMedia({ audio: true });
            }
            catch (e) {
                return;
            }
        }

        const context = new (window.AudioContext || window.webkitAudioContext)();
        const analyser = context.createAnalyser();

        analyser.fftSize = 1024;
        context.createMediaStreamSource(source).connect(analyser);

        const samples = new Float32Array(analyser.fftSize);
        const bars = Array.prototype.slice.call(element.children);
        const levels = bars.map(function () {
            return 0;
        });

        const started = { context: context, stream: source, own: own, timer: null, element: element };

        started.timer = setInterval(function () {
            if (!element.isConnected) {
                stopMeter();

                return;
            }

            analyser.getFloatTimeDomainData(samples);

            let sum = 0;

            for (let i = 0; i < samples.length; i++)
                sum += samples[i] * samples[i];

            //The square root again, so a quiet voice still lifts the bars and a loud one does not pin them.
            const level = Math.min(1, Math.sqrt(Math.sqrt(sum / samples.length) / FULL_RMS));

            levels.shift();
            levels.push(level);

            for (let i = 0; i < bars.length; i++)
                bars[i].style.height = (15 + 85 * levels[i]).toFixed(0) + '%';

            element.dataset.level = level.toFixed(3);
        }, LEVEL_MS);

        meter = started;
    }

    function stopMeter() {
        if (!meter)
            return;

        clearInterval(meter.timer);

        if (meter.own) {
            meter.stream.getTracks().forEach(function (track) {
                track.stop();
            });
        }

        meter.context.close();
        meter = null;
    }

    function ended(problem) {
        const told = caller;

        caller = null;
        stopMeter();

        if (told)
            told.invokeMethodAsync('OnSpeechEnded', problem || '');
    }

    //Puts words into the box as if they were typed, so the page's binding and the box's own growing both hear them.
    function show(box, value) {
        box.value = value;
        box.dispatchEvent(new Event('input', { bubbles: true }));
    }

    window.gdsSpeech = {
        browserSupported: function () {
            return recognizer() !== null;
        },

        recordingSupported: function () {
            return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia && window.MediaRecorder);
        },

        //
        //Listens with the browser's recognition, writing what is heard after what the box already holds - the words
        //settled so far and the guess at the ones still being said, which the next result replaces. Ends on a press of
        //the button again, or when the recognizer gives up after a silence.
        //
        startListening: function (box, dotnet) {
            const Recognition = recognizer();

            if (!Recognition || !box || recognition)
                return false;

            caller = dotnet;

            let before = box.value;

            if (before.length > 0 && !/\s$/.test(before))
                before += ' ';

            let settled = '';

            recognition = new Recognition();
            recognition.continuous = true;
            recognition.interimResults = true;
            recognition.lang = navigator.language || 'en-US';

            recognition.onresult = function (event) {
                let guess = '';

                for (let i = event.resultIndex; i < event.results.length; i++) {
                    const said = event.results[i][0].transcript;

                    if (event.results[i].isFinal)
                        settled += said;
                    else
                        guess += said;
                }

                show(box, before + settled + guess);
            };

            recognition.onerror = function (event) {
                let problem = 'Speech could not be recognized: ' + event.error + '.';

                if (event.error === 'not-allowed' || event.error === 'service-not-allowed')
                    problem = 'The browser was not allowed to use the microphone. Allow it in the address bar, and press the microphone again.';
                else if (event.error === 'no-speech')
                    problem = '';
                else if (event.error === 'network')
                    problem = "The browser's speech recognition could not reach its server. Try again, or switch Speech to text to your AI provider under Settings, AI settings.";

                recognition = null;
                ended(problem);
            };

            recognition.onend = function () {
                recognition = null;
                ended('');
            };

            try {
                recognition.start();
            }
            catch (e) {
                recognition = null;
                caller = null;

                return false;
            }

            return true;
        },

        stopListening: function () {
            stopMeter();

            if (recognition)
                recognition.stop();
        },

        //The bars beside Send, in the element given: from the recording's own stream when there is one, else the meter's own.
        showLevel: async function (element) {
            await startMeter(element, stream);
        },

        //Starts recording for the provider. Answers why it could not, or '' once it is recording.
        startRecording: async function (dotnet) {
            if (recorder)
                return '';

            if (!window.gdsSpeech.recordingSupported())
                return 'This browser cannot record from a microphone.';

            try {
                stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            }
            catch (e) {
                return 'The browser was not allowed to use the microphone. Allow it in the address bar, and press the microphone again.';
            }

            caller = dotnet;
            chunks = [];
            recorder = new MediaRecorder(stream);

            recorder.ondataavailable = function (event) {
                if (event.data && event.data.size > 0)
                    chunks.push(event.data);
            };

            recorder.start();

            //A recording left going is ended at the limit, and handed over as if the button had been pressed.
            limit = setTimeout(function () {
                ended('limit');
            }, MOST_SECONDS * 1000);

            return '';
        },

        //
        //Ends the recording and hands it back as a 16 kHz mono WAV, in base64 - '' for nothing recorded. Decoded by the
        //browser's own audio decoder, which reads whatever it recorded in.
        //
        stopRecording: async function () {
            if (!recorder)
                return '';

            clearTimeout(limit);
            stopMeter();

            const done = new Promise(function (resolve) {
                recorder.onstop = resolve;
            });

            recorder.stop();
            await done;

            stream.getTracks().forEach(function (track) {
                track.stop();
            });

            const blob = new Blob(chunks, { type: recorder.mimeType || 'audio/webm' });

            recorder = null;
            stream = null;
            chunks = [];
            caller = null;

            if (blob.size === 0)
                return '';

            const context = new (window.AudioContext || window.webkitAudioContext)();

            try {
                const audio = await context.decodeAudioData(await blob.arrayBuffer());
                const channels = [];

                for (let c = 0; c < audio.numberOfChannels; c++)
                    channels.push(audio.getChannelData(c));

                const bytes = window.speechWav.speechWav(channels, audio.sampleRate);
                let binary = '';

                for (let i = 0; i < bytes.length; i += 0x8000)
                    binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));

                return btoa(binary);
            }
            finally {
                context.close();
            }
        },

        //Stops whichever is going, keeping nothing - the window closed, or the page left.
        cancel: function () {
            caller = null;
            clearTimeout(limit);
            stopMeter();

            if (recognition) {
                recognition.onend = null;
                recognition.abort();
                recognition = null;
            }

            if (recorder) {
                recorder.onstop = null;

                try {
                    recorder.stop();
                }
                catch (e) { }

                recorder = null;
            }

            if (stream) {
                stream.getTracks().forEach(function (track) {
                    track.stop();
                });

                stream = null;
            }

            chunks = [];
        }
    };
})();
