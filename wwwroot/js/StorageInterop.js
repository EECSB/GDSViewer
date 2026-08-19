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

//localStorage, for the small values that are worth keeping when IndexedDB is unavailable, and as the
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
//to localStorage on the way out - the one API that can still be called at that point.
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
window.gdsExitSave = {
    _key: null,
    _snapshot: null,
    _registered: false,

    //Hands over what to write if the page goes away. Called on every change, so it stays current.
    hold: function (key, snapshot) {
        this._key = key;
        this._snapshot = snapshot;

        this._register();
    },

    release: function () {
        this._snapshot = null;
    },

    _register: function () {
        if (this._registered)
            return;

        this._registered = true;

        var self = this;

        var write = function () {
            if (self._key === null || self._snapshot === null)
                return;

            window.gdsLocalStorage.set(self._key, self._snapshot);
        };

        window.addEventListener('pagehide', write);

        window.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'hidden')
                write();
        });
    }
};
