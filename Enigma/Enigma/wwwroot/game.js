(function () {
    const playerStorageKey = "enigma.player";
    const userStorageKey = "enigma.user";
    const livePlayerStateKey = "enigma.game.live-player";
    const activeGameSessionKey = "enigma.game.active-run";
    let dotNetRef = null;
    let keyDownHandler = null;
    let keyUpHandler = null;
    let livePlayerState = null;

    function shouldHandleKey(code) {
        return ["ArrowUp", "ArrowRight", "ArrowDown", "ArrowLeft", "KeyW", "KeyA", "KeyS", "KeyD"].includes(code);
    }

    function removeListeners() {
        if (keyDownHandler) {
            window.removeEventListener("keydown", keyDownHandler);
            keyDownHandler = null;
        }

        if (keyUpHandler) {
            window.removeEventListener("keyup", keyUpHandler);
            keyUpHandler = null;
        }
    }

    function setStoredJson(storage, key, value) {
        storage.setItem(key, JSON.stringify(value || {}));
    }

    function readStoredJson(key) {
        const raw = window.sessionStorage.getItem(key) || window.localStorage.getItem(key);
        return raw ? JSON.parse(raw) : null;
    }

    function refreshStoredJson(key, value) {
        if (window.localStorage.getItem(key)) {
            setStoredJson(window.localStorage, key, value);
            return;
        }

        if (window.sessionStorage.getItem(key)) {
            setStoredJson(window.sessionStorage, key, value);
            return;
        }

        setStoredJson(window.sessionStorage, key, value);
    }

    function clearStoredJson(key) {
        window.sessionStorage.removeItem(key);
        window.localStorage.removeItem(key);
    }

    function emitPlayerStateChange(state) {
        window.dispatchEvent(new CustomEvent("enigma:player-state", { detail: state || null }));
    }

    window.enigmaGame = {
        registerInput: function (helper) {
            removeListeners();
            dotNetRef = helper;

            keyDownHandler = function (event) {
                if (!shouldHandleKey(event.code) || !dotNetRef) {
                    return;
                }

                event.preventDefault();
                dotNetRef.invokeMethodAsync("HandleKeyChange", event.code, true);
            };

            keyUpHandler = function (event) {
                if (!shouldHandleKey(event.code) || !dotNetRef) {
                    return;
                }

                event.preventDefault();
                dotNetRef.invokeMethodAsync("HandleKeyChange", event.code, false);
            };

            window.addEventListener("keydown", keyDownHandler, { passive: false });
            window.addEventListener("keyup", keyUpHandler, { passive: false });
        },

        disposeInput: function () {
            removeListeners();
            dotNetRef = null;
        },

        focusElement: function (elementId) {
            const element = document.getElementById(elementId);
            if (element) {
                element.focus();
            }
        },

        sessionSetJson: function (key, value) {
            window.sessionStorage.setItem(key, JSON.stringify(value));
        },

        sessionGetJson: function (key) {
            const raw = window.sessionStorage.getItem(key);
            return raw ? JSON.parse(raw) : null;
        },

        sessionRemove: function (key) {
            window.sessionStorage.removeItem(key);
        },

        setPlayerIdentity: function (identity, rememberMe) {
            if (rememberMe) {
                setStoredJson(window.localStorage, playerStorageKey, identity);
                window.sessionStorage.removeItem(playerStorageKey);
                return;
            }

            setStoredJson(window.sessionStorage, playerStorageKey, identity);
            window.localStorage.removeItem(playerStorageKey);
        },

        getPlayerIdentity: function () {
            return readStoredJson(playerStorageKey);
        },

        clearPlayerIdentity: function () {
            clearStoredJson(playerStorageKey);
        },

        setUserSession: function (session, rememberMe) {
            if (rememberMe) {
                setStoredJson(window.localStorage, userStorageKey, session);
                window.sessionStorage.removeItem(userStorageKey);
                return;
            }

            setStoredJson(window.sessionStorage, userStorageKey, session);
            window.localStorage.removeItem(userStorageKey);
        },

        getUserSession: function () {
            return readStoredJson(userStorageKey);
        },

        refreshUserSession: function (session) {
            refreshStoredJson(userStorageKey, session);
        },

        clearUserSession: function () {
            clearStoredJson(userStorageKey);
        },

        setActiveGameSession: function (session) {
            if (!session || !session.seed) {
                window.sessionStorage.removeItem(activeGameSessionKey);
                return;
            }

            window.sessionStorage.setItem(activeGameSessionKey, JSON.stringify(session));
        },

        getActiveGameSession: function () {
            const raw = window.sessionStorage.getItem(activeGameSessionKey);
            return raw ? JSON.parse(raw) : null;
        },

        clearActiveGameSession: function () {
            window.sessionStorage.removeItem(activeGameSessionKey);
        },

        setLivePlayerState: function (state) {
            livePlayerState = state || null;
            if (!livePlayerState) {
                window.sessionStorage.removeItem(livePlayerStateKey);
                emitPlayerStateChange(null);
                return;
            }

            window.sessionStorage.setItem(livePlayerStateKey, JSON.stringify(livePlayerState));
            emitPlayerStateChange(livePlayerState);
        },

        getLivePlayerState: function () {
            if (livePlayerState) {
                return livePlayerState;
            }

            const raw = window.sessionStorage.getItem(livePlayerStateKey);
            livePlayerState = raw ? JSON.parse(raw) : null;
            return livePlayerState;
        },

        clearLivePlayerState: function () {
            livePlayerState = null;
            window.sessionStorage.removeItem(livePlayerStateKey);
            emitPlayerStateChange(null);
        },

        goBack: function (fallbackUrl) {
            if (window.history.length > 1) {
                window.history.back();
                return;
            }

            window.location.href = fallbackUrl || "/home";
        }
    };
})();
