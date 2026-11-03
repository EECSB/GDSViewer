//A string key-value store over IndexedDB, backing AppStorage on the C# side.
//
//IndexedDB rather than localStorage because of what gets kept: the open file's bytes. localStorage caps an
//origin at about 5 MB and stores strings only, where the bundled examples alone reach 9 MB and a real
//layout is larger again. IndexedDB's quota is a fraction of free disk. Values here are opaque strings -
//the C# layer owns JSON and compression - so this file stays a store and nothing more.
//
//Nothing throws. Storage is unavailable outright in some private-browsing modes and a write can fail on a
//full quota, and neither is a reason to take down a viewer: set() resolves false and get() resolves null,
//so the caller decides what a failure means.
window.gdsStorage = {
    _dbName: 'GDSViewer',
    _store: 'kv',
    _dbPromise: null,

    _open: function () {
        if (this._dbPromise)
            return this._dbPromise;

        var self = this;

        this._dbPromise = new Promise(function (resolve, reject) {
            var request;

            try {
                request = indexedDB.open(self._dbName, 1);
            }
            catch (e) {
                reject(e);

                return;
            }

            request.onupgradeneeded = function () {
                request.result.createObjectStore(self._store);
            };

            request.onsuccess = function () {
                resolve(request.result);
            };

            request.onerror = function () {
                reject(request.error);
            };

            //Firefox leaves the request pending rather than erroring when storage is blocked, so a first
            //call would otherwise never settle and every await behind it would hang.
            request.onblocked = function () {
                reject(new Error('IndexedDB is blocked'));
            };
        });

        return this._dbPromise;
    },

    //The string stored under key, or null when absent or on any failure.
    get: async function (key) {
        try {
            var db = await this._open();
            var store = this._store;

            return await new Promise(function (resolve) {
                var transaction = db.transaction(store, 'readonly');
                var request = transaction.objectStore(store).get(key);

                request.onsuccess = function () {
                    if (request.result === undefined)
                        resolve(null);
                    else
                        resolve(request.result);
                };

                request.onerror = function () {
                    resolve(null);
                };
            });
        }
        catch (e) {
            return null;
        }
    },

    //
    //The same value handed over in parts, for one too big to hand over at once: a stored file's three megabytes took
    //47 ms to cross into C# in one piece, with the page stopped. getInParts reads it and holds it here, answering its
    //length (-1 when absent); partBytes hands over a slice; doneWithParts lets it go. See AppStorage.ReadAtAPaceAsync.
    //
    //**A slice as the bytes of its characters, not as a string.** What is read this way is base64 behind its marker,
    //all of it ASCII, so a character is a byte - and a string crossing into C# is copied three times on the way, as
    //JSON, as UTF-8 and as the string, where bytes are copied once.
    //
    _held: {},

    getInParts: async function (key) {
        var value = await this.get(key);

        if (value === null)
            return -1;

        this._held[key] = value;

        return value.length;
    },

    partBytes: function (key, from, count) {
        var value = this._held[key];

        if (value === undefined)
            return null;

        return new TextEncoder().encode(value.substring(from, from + count));
    },

    doneWithParts: function (key) {
        delete this._held[key];
    },

    //Writes value under key. True on success, false on a full quota or unavailable storage.
    set: async function (key, value) {
        try {
            var db = await this._open();
            var store = this._store;

            return await new Promise(function (resolve) {
                var transaction;

                try {
                    transaction = db.transaction(store, 'readwrite');
                }
                catch (e) {
                    resolve(false);

                    return;
                }

                transaction.objectStore(store).put(value, key);

                transaction.oncomplete = function () {
                    resolve(true);
                };

                transaction.onerror = function () {
                    resolve(false);
                };

                transaction.onabort = function () {
                    resolve(false);
                };
            });
        }
        catch (e) {
            return false;
        }
    },

    //
    //Writes several values in one transaction, so either all of them land or none do. Keys and values
    //alternate: key, value, key, value. True on success.
    //
    //For a session and the file it belongs to, which are kept apart so that a save need not write the file
    //- see SavedSession.BytesApart. Written together when the file has changed, so a reload can never find
    //the settings of one version beside the bytes of another, which the undo stack would misread.
    //
    setTogether: async function () {
        var pairs = Array.prototype.slice.call(arguments);

        try {
            var db = await this._open();
            var store = this._store;

            return await new Promise(function (resolve) {
                var transaction;

                try {
                    transaction = db.transaction(store, 'readwrite');
                }
                catch (e) {
                    resolve(false);

                    return;
                }

                var objects = transaction.objectStore(store);

                for (var i = 0; i + 1 < pairs.length; i += 2)
                    objects.put(pairs[i + 1], pairs[i]);

                transaction.oncomplete = function () {
                    resolve(true);
                };

                transaction.onerror = function () {
                    resolve(false);
                };

                transaction.onabort = function () {
                    resolve(false);
                };
            });
        }
        catch (e) {
            return false;
        }
    },

    remove: async function (key) {
        try {
            var db = await this._open();
            var store = this._store;

            await new Promise(function (resolve) {
                var transaction = db.transaction(store, 'readwrite');

                transaction.objectStore(store).delete(key);

                transaction.oncomplete = function () {
                    resolve();
                };

                transaction.onerror = function () {
                    resolve();
                };
            });
        }
        catch (e) { }
    },

    clear: async function () {
        try {
            var db = await this._open();
            var store = this._store;

            await new Promise(function (resolve) {
                var transaction = db.transaction(store, 'readwrite');

                transaction.objectStore(store).clear();

                transaction.oncomplete = function () {
                    resolve();
                };

                transaction.onerror = function () {
                    resolve();
                };
            });
        }
        catch (e) { }
    },

    //Whether IndexedDB can be used at all. Some private-browsing modes disable it outright.
    available: function () {
        try {
            return typeof indexedDB !== 'undefined' && indexedDB !== null;
        }
        catch (e) {
            return false;
        }
    }
};

//localStorage, for the exit handler below - the one store a closing page can still write - and as the
//source of the one-time migration for anything written before there was a store. Wrapped for the same
//reason: it throws outright in a private window and with site data blocked.
window.gdsLocalStorage = {
    get: function (key) {
        try {
            return window.localStorage.getItem(key);
        }
        catch (e) {
            return null;
        }
    },

    set: function (key, value) {
        try {
            window.localStorage.setItem(key, value);

            return true;
        }
        catch (e) {
            return false;
        }
    },

    remove: function (key) {
        try {
            window.localStorage.removeItem(key);
        }
        catch (e) { }
    }
};

//
//Saving when the window closes.
//
//The last edit has to survive the tab going away, and there is no reliable "closing" event to await an
//async save in: beforeunload cannot hold the page open for IndexedDB, and on mobile a tab is often killed
//without firing it at all. So the app keeps a snapshot up to date here, and this writes it synchronously
//to localStorage on the way out - the one API that can still be called at that point. Written as it was
//handed over, already encoded; AppStorage.GetAsync decodes it when it reads it back.
//
//visibilitychange is what actually fires on mobile, which is why it is listened to alongside pagehide.
//
//**And not beforeunload.** It was listened to as well, which bought nothing - pagehide fires on a
//navigation, a reload and a tab closing alike, so every case beforeunload covered was already covered. What
//it cost was real: registering a beforeunload handler at all puts the browser into the "should I ask about
//leaving" path, and a navigation that arrives while that is being settled is aborted. That was the suite's
//one repeatable flake, at two runs in ten, reported as `Not attached to an active page` from a dialog
//nobody asked for and no dialog anybody ever saw. pagehide and visibilitychange are what MDN recommends for
//exactly this, and they are what is left.
//
//**More than one value, by key**: the session and, kept apart from it, the file it belongs to - see
//SavedSession.BytesApart. The file is handed over when it changes and the session on every save, so each is
//held under its own key and replaced only by a newer copy of itself.
//
window.gdsExitSave = {
    _held: new Map(),
    _registered: false,
    _hidden: null,

    //Hands over what to write under a key if the page goes away. Called on every change, so it stays current.
    hold: function (key, snapshot) {
        this._held.set(key, snapshot);

        this._register();
    },

    //
    //Two at once: a session and the bytes it belongs to, which have to be written out as a pair. Its saved steps
    //are taken again only on the bytes they were saved against, and held one call at a time there is a moment
    //between the two when a page going away would write the new one of one beside the old one of the other.
    //
    holdTogether: function (firstKey, firstSnapshot, secondKey, secondSnapshot) {
        this._held.set(firstKey, firstSnapshot);
        this._held.set(secondKey, secondSnapshot);

        this._register();
    },

    //
    //Who to tell when the page is hidden, once the snapshots are written: the app, which then writes the open file
    //whole in place of the steps it has been saving since it last did. Hidden is the last moment worth trusting -
    //on a phone it is often the last one there is - and a write it starts may not finish, which leaves what was
    //stored before it, steps and all.
    //
    whenHidden: function (handle, method) {
        this._hidden = { handle: handle, method: method };

        this._register();
    },

    //Lets go of what is held under a key, or of everything when no key is given.
    release: function (key) {
        if (key === undefined || key === null)
            this._held.clear();
        else
            this._held.delete(key);
    },

    _register: function () {
        if (this._registered)
            return;

        this._registered = true;

        var self = this;

        var write = function () {
            self._held.forEach(function (snapshot, key) {
                window.gdsLocalStorage.set(key, snapshot);
            });
        };

        window.addEventListener('pagehide', write);

        window.addEventListener('visibilitychange', function () {
            if (document.visibilityState !== 'hidden')
                return;

            write();

            if (self._hidden !== null)
                self._hidden.handle.invokeMethodAsync(self._hidden.method).catch(function () { });
        });
    }
};
