(function () {
    const playerStorageKey = "enigma.player";
    const userStorageKey = "enigma.user";
    const livePlayerStateKey = "enigma.game.live-player";
    const activeGameSessionKey = "enigma.game.active-run";
    const pendingLossSummaryKey = "enigma.game.pending-loss";
    const pendingLossDraftKey = "enigma.game.pending-loss-draft";
    const runLoadoutKey = "enigma.game.run-loadout";
    const fullscreenOptOutKey = "enigma.game.fullscreen-opt-out";
    const tutorialRequestKey = "enigma.tutorial.requested";

    let dotNetRef = null;
    let keyDownHandler = null;
    let keyUpHandler = null;
    let beforeUnloadHandler = null;
    let livePlayerState = null;
    let currentAbandonUrl = null;

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

    function getDraftLossSummary() {
        const raw = window.sessionStorage.getItem(pendingLossDraftKey);
        return raw ? JSON.parse(raw) : null;
    }

    function setDraftLossSummary(summary) {
        if (!summary) {
            window.sessionStorage.removeItem(pendingLossDraftKey);
            return;
        }

        window.sessionStorage.setItem(pendingLossDraftKey, JSON.stringify(summary));
    }

    function promoteDraftLossSummary() {
        const summary = getDraftLossSummary();
        if (!summary) {
            return null;
        }

        window.sessionStorage.setItem(pendingLossSummaryKey, JSON.stringify(summary));
        return summary;
    }

    function removeBeforeUnload() {
        if (beforeUnloadHandler) {
            window.removeEventListener("beforeunload", beforeUnloadHandler);
            beforeUnloadHandler = null;
        }
    }

    function sendAbandonBeacon(summary) {
        if (!summary || !currentAbandonUrl || !navigator.sendBeacon) {
            return;
        }

        try {
            const payload = new Blob([JSON.stringify(summary)], { type: "application/json" });
            navigator.sendBeacon(currentAbandonUrl, payload);
        } catch {
        }
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

        setPendingLossDraft: function (summary) {
            setDraftLossSummary(summary);
        },

        getPendingLossDraft: function () {
            return getDraftLossSummary();
        },

        clearPendingLossDraft: function () {
            window.sessionStorage.removeItem(pendingLossDraftKey);
        },

        setPendingLossSummary: function (summary) {
            if (!summary) {
                window.sessionStorage.removeItem(pendingLossSummaryKey);
                return;
            }

            window.sessionStorage.setItem(pendingLossSummaryKey, JSON.stringify(summary));
        },

        getPendingLossSummary: function () {
            const raw = window.sessionStorage.getItem(pendingLossSummaryKey);
            return raw ? JSON.parse(raw) : null;
        },

        consumePendingLossSummary: function () {
            const raw = window.sessionStorage.getItem(pendingLossSummaryKey);
            window.sessionStorage.removeItem(pendingLossSummaryKey);
            return raw ? JSON.parse(raw) : null;
        },

        clearPendingLossSummary: function () {
            window.sessionStorage.removeItem(pendingLossSummaryKey);
            window.sessionStorage.removeItem(pendingLossDraftKey);
        },

        registerLossUnload: function (abandonUrl) {
            currentAbandonUrl = abandonUrl || null;
            removeBeforeUnload();
            beforeUnloadHandler = function (event) {
                const summary = promoteDraftLossSummary();
                sendAbandonBeacon(summary);
                if (summary) {
                    event.preventDefault();
                    event.returnValue = "";
                }
            };
            window.addEventListener("beforeunload", beforeUnloadHandler);
        },

        clearLossUnload: function () {
            currentAbandonUrl = null;
            removeBeforeUnload();
            window.sessionStorage.removeItem(pendingLossDraftKey);
        },

        setRunLoadout: function (loadout) {
            if (!loadout) {
                window.sessionStorage.removeItem(runLoadoutKey);
                return;
            }

            window.sessionStorage.setItem(runLoadoutKey, JSON.stringify(loadout));
        },

        getRunLoadout: function () {
            const raw = window.sessionStorage.getItem(runLoadoutKey);
            return raw ? JSON.parse(raw) : [];
        },

        clearRunLoadout: function () {
            window.sessionStorage.removeItem(runLoadoutKey);
        },

        setFullscreenOptOut: function (value) {
            window.localStorage.setItem(fullscreenOptOutKey, value ? "true" : "false");
        },

        getFullscreenOptOut: function () {
            return window.localStorage.getItem(fullscreenOptOutKey) === "true";
        },

        startTutorial: function () {
            window.sessionStorage.setItem(tutorialRequestKey, "true");
            window.dispatchEvent(new CustomEvent("enigma:tutorial-requested"));
        },

        consumeTutorialRequest: function () {
            const requested = window.sessionStorage.getItem(tutorialRequestKey) === "true";
            window.sessionStorage.removeItem(tutorialRequestKey);
            return requested;
        },

        requestFullscreen: async function (elementId) {
            const element = document.getElementById(elementId) || document.documentElement;
            if (element.requestFullscreen) {
                await element.requestFullscreen();
            }
        },

        isFullscreen: function () {
            return !!document.fullscreenElement;
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
