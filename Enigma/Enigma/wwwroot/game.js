(function () {
    const playerStorageKey = "enigma.player";
    const userStorageKey = "enigma.user";
    const livePlayerStateKey = "enigma.game.live-player";
    const activeGameSessionKey = "enigma.game.active-run";
    const pendingLossSummaryKey = "enigma.game.pending-loss";
    const pendingLossDraftKey = "enigma.game.pending-loss-draft";
    const runLoadoutKey = "enigma.game.run-loadout";
    const fullscreenOptOutKey = "enigma.game.fullscreen-opt-out";
    const fullscreenPreferenceKey = "enigma.game.fullscreen-preference";
    const tutorialRequestKey = "enigma.tutorial.requested";

    let dotNetRef = null;
    let keyDownHandler = null;
    let keyUpHandler = null;
    let beforeUnloadHandler = null;
    let livePlayerState = null;
    let currentAbandonUrl = null;
    let currentCoopLeaveUrl = null;
    let currentCoopLeavePayload = null;
    let tutorialDotNetRef = null;
    let tutorialRequestHandler = null;
    let userSessionDotNetRef = null;
    let userSessionHandler = null;
    let audioContext = null;
    let pageHideHandler = null;
    const dropdownClosers = new Map();
    let coopSocket = null;
    let coopSocketSessionId = null;
    let coopSocketDotNetRef = null;
    let coopSocketReconnectHandle = null;
    const desktopZoomOutValue = 0.8;
    const desktopZoomOutMinWidth = 1024;
    let viewportZoomHandler = null;
    let historyZoomPatchApplied = false;
    const storageFallback = {
        session: Object.create(null),
        local: Object.create(null)
    };

    function getStorageArea(storageName) {
        try {
            return storageName === "local" ? window.localStorage : window.sessionStorage;
        } catch {
            return null;
        }
    }

    function getStorageItem(storageName, key) {
        const storage = getStorageArea(storageName);
        if (storage) {
            try {
                return storage.getItem(key);
            } catch {
            }
        }

        return Object.prototype.hasOwnProperty.call(storageFallback[storageName], key)
            ? storageFallback[storageName][key]
            : null;
    }

    function setStorageItem(storageName, key, value) {
        const storage = getStorageArea(storageName);
        if (storage) {
            try {
                storage.setItem(key, value);
                delete storageFallback[storageName][key];
                return;
            } catch {
            }
        }

        storageFallback[storageName][key] = value;
    }

    function removeStorageItem(storageName, key) {
        const storage = getStorageArea(storageName);
        if (storage) {
            try {
                storage.removeItem(key);
            } catch {
            }
        }

        delete storageFallback[storageName][key];
    }

    function hasStorageItem(storageName, key) {
        const storage = getStorageArea(storageName);
        if (storage) {
            try {
                return storage.getItem(key) !== null;
            } catch {
            }
        }

        return Object.prototype.hasOwnProperty.call(storageFallback[storageName], key);
    }

    function parseJsonValue(raw, storageName, key) {
        if (!raw) {
            return null;
        }

        try {
            return JSON.parse(raw);
        } catch {
            if (storageName && key) {
                removeStorageItem(storageName, key);
            }

            return null;
        }
    }

    function dispatchCustomEvent(name, detail) {
        try {
            if (typeof window.CustomEvent === "function") {
                window.dispatchEvent(new CustomEvent(name, { detail: detail || null }));
                return;
            }

            const event = document.createEvent("CustomEvent");
            event.initCustomEvent(name, false, false, detail || null);
            window.dispatchEvent(event);
        } catch {
        }
    }

    function unregisterDropdownOutsideClick(dropdownId) {
        const entry = dropdownClosers.get(dropdownId);
        if (!entry) {
            return;
        }

        document.removeEventListener("mousedown", entry.mouseHandler, true);
        document.removeEventListener("touchstart", entry.touchHandler, true);
        dropdownClosers.delete(dropdownId);
    }

    function registerDropdownOutsideClick(dropdownId, element, helper) {
        unregisterDropdownOutsideClick(dropdownId);
        if (!dropdownId || !element || !helper) {
            return;
        }

        const closeIfOutside = function (event) {
            if (!element.contains(event.target)) {
                helper.invokeMethodAsync("CloseDropdownAsync").catch(() => {});
            }
        };

        const entry = {
            mouseHandler: closeIfOutside,
            touchHandler: closeIfOutside
        };

        dropdownClosers.set(dropdownId, entry);
        document.addEventListener("mousedown", closeIfOutside, true);
        document.addEventListener("touchstart", closeIfOutside, true);
    }

    function getNormalizedCode(event) {
        if (event.code) {
            return event.code;
        }

        switch (String(event.key || "").toLowerCase()) {
            case "arrowup":
            case "w":
                return event.key.length === 1 ? "KeyW" : "ArrowUp";
            case "arrowright":
            case "d":
                return event.key.length === 1 ? "KeyD" : "ArrowRight";
            case "arrowdown":
            case "s":
                return event.key.length === 1 ? "KeyS" : "ArrowDown";
            case "arrowleft":
            case "a":
                return event.key.length === 1 ? "KeyA" : "ArrowLeft";
            case "1":
                return "Digit1";
            case "2":
                return "Digit2";
            case "3":
                return "Digit3";
            default:
                return "";
        }
    }

    function shouldHandleKey(code) {
        return [
            "ArrowUp", "ArrowRight", "ArrowDown", "ArrowLeft",
            "KeyW", "KeyA", "KeyS", "KeyD",
            "KeyE", "Escape",
            "Digit1", "Digit2", "Digit3",
            "Numpad1", "Numpad2", "Numpad3"
        ].includes(code);
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

    function addWindowListener(name, handler, options) {
        try {
            window.addEventListener(name, handler, options);
        } catch {
            window.addEventListener(name, handler);
        }
    }

    function getNormalizedPathname() {
        try {
            const path = String(window.location.pathname || "").trim().toLowerCase();
            return path.replace(/\/+$/, "") || "/";
        } catch {
            return "/";
        }
    }

    function isGameplayPath() {
        const path = getNormalizedPathname();
        return path === "/game" || path.startsWith("/game/");
    }

    function shouldApplyDesktopZoomOut() {
        if (isGameplayPath()) {
            return false;
        }

        if (typeof window.matchMedia === "function") {
            return window.matchMedia(`(min-width: ${desktopZoomOutMinWidth}px)`).matches;
        }

        return window.innerWidth >= desktopZoomOutMinWidth;
    }

    function applyDesktopZoomOut() {
        const root = document.documentElement;
        const body = document.body;
        if (!root || !body) {
            return;
        }

        const enabled = shouldApplyDesktopZoomOut();
        if (!enabled) {
            root.style.removeProperty("zoom");
            root.classList.remove("enigma-global-zoom-fallback");
            body.style.removeProperty("transform");
            body.style.removeProperty("transform-origin");
            body.style.removeProperty("width");
            return;
        }

        const supportsZoom = !!(window.CSS && typeof window.CSS.supports === "function" && window.CSS.supports("zoom", "1"));
        if (supportsZoom) {
            root.style.setProperty("zoom", String(desktopZoomOutValue));
            root.classList.remove("enigma-global-zoom-fallback");
            body.style.removeProperty("transform");
            body.style.removeProperty("transform-origin");
            body.style.removeProperty("width");
            return;
        }

        root.style.removeProperty("zoom");
        root.classList.add("enigma-global-zoom-fallback");
        body.style.setProperty("transform", `scale(${desktopZoomOutValue})`);
        body.style.setProperty("transform-origin", "top left");
        body.style.setProperty("width", `${100 / desktopZoomOutValue}vw`);
    }

    function registerDesktopZoomOut() {
        if (viewportZoomHandler) {
            return;
        }

        viewportZoomHandler = function () {
            applyDesktopZoomOut();
        };

        addWindowListener("resize", viewportZoomHandler, { passive: true });
        addWindowListener("orientationchange", viewportZoomHandler, { passive: true });
        addWindowListener("popstate", viewportZoomHandler, { passive: true });
        if (!historyZoomPatchApplied && window.history) {
            const historyStateChanged = function () {
                setTimeout(applyDesktopZoomOut, 0);
            };

            const originalPushState = window.history.pushState;
            const originalReplaceState = window.history.replaceState;
            window.history.pushState = function () {
                const result = originalPushState.apply(this, arguments);
                historyStateChanged();
                return result;
            };
            window.history.replaceState = function () {
                const result = originalReplaceState.apply(this, arguments);
                historyStateChanged();
                return result;
            };

            historyZoomPatchApplied = true;
        }
        if (document.readyState === "loading") {
            addWindowListener("DOMContentLoaded", viewportZoomHandler, { once: true });
        }

        applyDesktopZoomOut();
    }

    function setStoredJson(storageName, key, value) {
        setStorageItem(storageName, key, JSON.stringify(value ?? {}));
    }

    function readStoredJson(key) {
        const sessionValue = parseJsonValue(getStorageItem("session", key), "session", key);
        if (sessionValue !== null) {
            return sessionValue;
        }

        return parseJsonValue(getStorageItem("local", key), "local", key);
    }

    function refreshStoredJson(key, value) {
        if (hasStorageItem("local", key)) {
            setStoredJson("local", key, value);
            return;
        }

        if (hasStorageItem("session", key)) {
            setStoredJson("session", key, value);
            return;
        }

        setStoredJson("session", key, value);
    }

    function clearStoredJson(key) {
        removeStorageItem("session", key);
        removeStorageItem("local", key);
    }

    function emitPlayerStateChange(state) {
        dispatchCustomEvent("enigma:player-state", state);
    }

    function getDraftLossSummary() {
        return parseJsonValue(getStorageItem("session", pendingLossDraftKey), "session", pendingLossDraftKey);
    }

    function setDraftLossSummary(summary) {
        if (!summary) {
            removeStorageItem("session", pendingLossDraftKey);
            return;
        }

        setStorageItem("session", pendingLossDraftKey, JSON.stringify(summary));
    }

    function promoteDraftLossSummary() {
        const summary = getDraftLossSummary();
        if (!summary) {
            return null;
        }

        setStorageItem("session", pendingLossSummaryKey, JSON.stringify(summary));
        return summary;
    }

    function removeBeforeUnload() {
        if (beforeUnloadHandler) {
            window.removeEventListener("beforeunload", beforeUnloadHandler);
            beforeUnloadHandler = null;
        }

        if (pageHideHandler) {
            window.removeEventListener("pagehide", pageHideHandler);
            pageHideHandler = null;
        }
    }

    function removeTutorialListener() {
        if (tutorialRequestHandler) {
            window.removeEventListener("enigma:tutorial-requested", tutorialRequestHandler);
            tutorialRequestHandler = null;
        }

        tutorialDotNetRef = null;
    }

    function removeUserSessionListener() {
        if (userSessionHandler) {
            window.removeEventListener("enigma:user-session-updated", userSessionHandler);
            userSessionHandler = null;
        }

        userSessionDotNetRef = null;
    }

    function emitUserSessionUpdate(session) {
        dispatchCustomEvent("enigma:user-session-updated", { session: session || null });
    }

    function clearCoopReconnect() {
        if (coopSocketReconnectHandle) {
            window.clearTimeout(coopSocketReconnectHandle);
            coopSocketReconnectHandle = null;
        }
    }

    function disposeCoopSocketInternal() {
        clearCoopReconnect();
        if (coopSocket) {
            try {
                coopSocket.onopen = null;
                coopSocket.onmessage = null;
                coopSocket.onclose = null;
                coopSocket.onerror = null;
                if (coopSocket.readyState === WebSocket.OPEN || coopSocket.readyState === WebSocket.CONNECTING) {
                    coopSocket.close(1000, "dispose");
                }
            } catch {
            }
        }

        coopSocket = null;
        coopSocketSessionId = null;
        coopSocketDotNetRef = null;
    }

    function notifyCoopSocketStatus(isOpen) {
        if (!coopSocketDotNetRef) {
            return;
        }

        coopSocketDotNetRef.invokeMethodAsync("HandleCoopSocketStatusChanged", !!isOpen).catch(() => {});
    }

    function scheduleCoopReconnect() {
        clearCoopReconnect();
        if (!coopSocketSessionId || !coopSocketDotNetRef) {
            return;
        }

        coopSocketReconnectHandle = window.setTimeout(function () {
            window.enigmaGame.connectCoopSocket(coopSocketSessionId, coopSocketDotNetRef);
        }, 1500);
    }

    function ensureAudioContext() {
        if (audioContext) {
            return audioContext;
        }

        const AudioCtor = window.AudioContext || window.webkitAudioContext;
        if (!AudioCtor) {
            return null;
        }

        try {
            audioContext = new AudioCtor();
        } catch {
            audioContext = null;
        }

        return audioContext;
    }

    function playOscillatorTone(context, frequency, peakGain, duration, type) {
        const oscillator = context.createOscillator();
        const gainNode = context.createGain();
        oscillator.type = type || "sine";
        oscillator.frequency.value = frequency;
        gainNode.gain.setValueAtTime(0.0001, context.currentTime);
        gainNode.gain.exponentialRampToValueAtTime(peakGain, context.currentTime + 0.012);
        gainNode.gain.exponentialRampToValueAtTime(0.0001, context.currentTime + duration);
        oscillator.connect(gainNode);
        gainNode.connect(context.destination);
        oscillator.start();
        oscillator.stop(context.currentTime + duration + 0.02);
    }

    function sendJsonBeacon(url, payloadObject) {
        if (!url || !payloadObject) {
            return;
        }

        try {
            const body = JSON.stringify(payloadObject);

            if (navigator.sendBeacon) {
                const payload = new Blob([body], { type: "application/json" });
                navigator.sendBeacon(url, payload);
                return;
            }

            if (window.fetch) {
                window.fetch(url, {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: body,
                    credentials: "include",
                    keepalive: true
                }).catch(() => {});
            }
        } catch {
        }
    }

    function sendAbandonBeacon(summary) {
        if (!summary || !currentAbandonUrl) {
            return;
        }

        sendJsonBeacon(currentAbandonUrl, summary);
    }

    window.enigmaGame = {
        registerInput: function (helper) {
            removeListeners();
            dotNetRef = helper;

            keyDownHandler = function (event) {
                const code = getNormalizedCode(event);
                if (!shouldHandleKey(code) || !dotNetRef) {
                    return;
                }

                event.preventDefault();
                dotNetRef.invokeMethodAsync("HandleKeyChange", code, true);
            };

            keyUpHandler = function (event) {
                const code = getNormalizedCode(event);
                if (!shouldHandleKey(code) || !dotNetRef) {
                    return;
                }

                event.preventDefault();
                dotNetRef.invokeMethodAsync("HandleKeyChange", code, false);
            };

            addWindowListener("keydown", keyDownHandler, { passive: false });
            addWindowListener("keyup", keyUpHandler, { passive: false });
            applyDesktopZoomOut();
        },

        disposeInput: function () {
            removeListeners();
            dotNetRef = null;
            applyDesktopZoomOut();
        },

        focusElement: function (elementId) {
            const element = document.getElementById(elementId);
            if (element) {
                element.focus();
            }
        },

        sessionSetJson: function (key, value) {
            setStorageItem("session", key, JSON.stringify(value));
        },

        sessionGetJson: function (key) {
            return parseJsonValue(getStorageItem("session", key), "session", key);
        },

        sessionRemove: function (key) {
            removeStorageItem("session", key);
        },

        setPlayerIdentity: function (identity, rememberMe) {
            if (rememberMe) {
                setStoredJson("local", playerStorageKey, identity);
                removeStorageItem("session", playerStorageKey);
                return;
            }

            setStoredJson("session", playerStorageKey, identity);
            removeStorageItem("local", playerStorageKey);
        },

        getPlayerIdentity: function () {
            return readStoredJson(playerStorageKey);
        },

        clearPlayerIdentity: function () {
            clearStoredJson(playerStorageKey);
        },

        setUserSession: function (session, rememberMe) {
            if (rememberMe) {
                setStoredJson("local", userStorageKey, session);
                removeStorageItem("session", userStorageKey);
                emitUserSessionUpdate(session);
                return;
            }

            setStoredJson("session", userStorageKey, session);
            removeStorageItem("local", userStorageKey);
            emitUserSessionUpdate(session);
        },

        getUserSession: function () {
            return readStoredJson(userStorageKey);
        },

        isUserSessionRemembered: function () {
            return hasStorageItem("local", userStorageKey) || hasStorageItem("local", playerStorageKey);
        },

        refreshUserSession: function (session) {
            refreshStoredJson(userStorageKey, session);
            emitUserSessionUpdate(session);
        },

        clearUserSession: function () {
            clearStoredJson(userStorageKey);
            emitUserSessionUpdate(null);
        },

        setActiveGameSession: function (session) {
            if (!session || !session.seed) {
                removeStorageItem("session", activeGameSessionKey);
                return;
            }

            setStorageItem("session", activeGameSessionKey, JSON.stringify(session));
        },

        getActiveGameSession: function () {
            return parseJsonValue(getStorageItem("session", activeGameSessionKey), "session", activeGameSessionKey);
        },

        clearActiveGameSession: function () {
            removeStorageItem("session", activeGameSessionKey);
        },

        setLivePlayerState: function (state) {
            livePlayerState = state || null;
            if (!livePlayerState) {
                removeStorageItem("session", livePlayerStateKey);
                emitPlayerStateChange(null);
                return;
            }

            setStorageItem("session", livePlayerStateKey, JSON.stringify(livePlayerState));
            emitPlayerStateChange(livePlayerState);
        },

        getLivePlayerState: function () {
            if (livePlayerState) {
                return livePlayerState;
            }

            livePlayerState = parseJsonValue(getStorageItem("session", livePlayerStateKey), "session", livePlayerStateKey);
            return livePlayerState;
        },

        clearLivePlayerState: function () {
            livePlayerState = null;
            removeStorageItem("session", livePlayerStateKey);
            emitPlayerStateChange(null);
        },

        setPendingLossDraft: function (summary) {
            setDraftLossSummary(summary);
        },

        getPendingLossDraft: function () {
            return getDraftLossSummary();
        },

        clearPendingLossDraft: function () {
            removeStorageItem("session", pendingLossDraftKey);
        },

        setPendingLossSummary: function (summary) {
            if (!summary) {
                removeStorageItem("session", pendingLossSummaryKey);
                return;
            }

            setStorageItem("session", pendingLossSummaryKey, JSON.stringify(summary));
        },

        getPendingLossSummary: function () {
            return parseJsonValue(getStorageItem("session", pendingLossSummaryKey), "session", pendingLossSummaryKey);
        },

        consumePendingLossSummary: function () {
            const raw = getStorageItem("session", pendingLossSummaryKey);
            removeStorageItem("session", pendingLossSummaryKey);
            return parseJsonValue(raw);
        },

        clearPendingLossSummary: function () {
            removeStorageItem("session", pendingLossSummaryKey);
            removeStorageItem("session", pendingLossDraftKey);
        },

        registerLossUnload: function (abandonUrl) {
            currentAbandonUrl = abandonUrl || null;
            removeBeforeUnload();
            beforeUnloadHandler = function (event) {
                const summary = promoteDraftLossSummary();
                sendAbandonBeacon(summary);
                sendJsonBeacon(currentCoopLeaveUrl, currentCoopLeavePayload);
                if (summary) {
                    event.preventDefault();
                    event.returnValue = "";
                }
            };
            addWindowListener("beforeunload", beforeUnloadHandler);
            pageHideHandler = function () {
                const summary = promoteDraftLossSummary();
                sendAbandonBeacon(summary);
                sendJsonBeacon(currentCoopLeaveUrl, currentCoopLeavePayload);
            };
            addWindowListener("pagehide", pageHideHandler);
        },

        clearLossUnload: function () {
            currentAbandonUrl = null;
            currentCoopLeaveUrl = null;
            currentCoopLeavePayload = null;
            removeBeforeUnload();
            removeStorageItem("session", pendingLossDraftKey);
        },

        registerCoopLeaveUnload: function (leaveUrl, sessionId, reason) {
            currentCoopLeaveUrl = leaveUrl || null;
            currentCoopLeavePayload = currentCoopLeaveUrl && sessionId
                ? {
                    sessionId: sessionId,
                    reason: reason || "page_unload"
                }
                : null;
        },

        clearCoopLeaveUnload: function () {
            currentCoopLeaveUrl = null;
            currentCoopLeavePayload = null;
        },

        setRunLoadout: function (loadout) {
            if (!loadout) {
                removeStorageItem("session", runLoadoutKey);
                return;
            }

            setStorageItem("session", runLoadoutKey, JSON.stringify(loadout));
        },

        getRunLoadout: function () {
            return parseJsonValue(getStorageItem("session", runLoadoutKey), "session", runLoadoutKey) || [];
        },

        clearRunLoadout: function () {
            removeStorageItem("session", runLoadoutKey);
        },

        setFullscreenOptOut: function (value) {
            if (value) {
                setStorageItem("local", fullscreenOptOutKey, "true");
                return;
            }

            removeStorageItem("local", fullscreenOptOutKey);
            removeStorageItem("local", fullscreenPreferenceKey);
        },

        getFullscreenOptOut: function () {
            return getStorageItem("local", fullscreenOptOutKey) === "true";
        },

        setFullscreenPreference: function (mode) {
            const normalizedMode = String(mode || "").trim().toLowerCase();
            if (normalizedMode !== "fullscreen" && normalizedMode !== "windowed") {
                removeStorageItem("local", fullscreenPreferenceKey);
                removeStorageItem("local", fullscreenOptOutKey);
                return;
            }

            setStorageItem("local", fullscreenPreferenceKey, normalizedMode);
            setStorageItem("local", fullscreenOptOutKey, "true");
        },

        getFullscreenPreference: function () {
            const storedPreference = String(getStorageItem("local", fullscreenPreferenceKey) || "").trim().toLowerCase();
            if (storedPreference === "fullscreen" || storedPreference === "windowed") {
                return storedPreference;
            }

            if (getStorageItem("local", fullscreenOptOutKey) === "true") {
                return "windowed";
            }

            return "";
        },

        clearFullscreenPreference: function () {
            removeStorageItem("local", fullscreenPreferenceKey);
            removeStorageItem("local", fullscreenOptOutKey);
        },

        startTutorial: function () {
            setStorageItem("session", tutorialRequestKey, "true");
            dispatchCustomEvent("enigma:tutorial-requested");
        },

        registerTutorialListener: function (helper) {
            removeTutorialListener();
            tutorialDotNetRef = helper;
            tutorialRequestHandler = function () {
                if (tutorialDotNetRef) {
                    tutorialDotNetRef.invokeMethodAsync("HandleTutorialRequestedAsync");
                }
            };

            addWindowListener("enigma:tutorial-requested", tutorialRequestHandler);
        },

        disposeTutorialListener: function () {
            removeTutorialListener();
        },

        registerUserSessionListener: function (helper) {
            removeUserSessionListener();
            userSessionDotNetRef = helper;
            userSessionHandler = function () {
                if (userSessionDotNetRef) {
                    userSessionDotNetRef.invokeMethodAsync("HandleUserSessionUpdatedAsync").catch(() => {});
                }
            };

            addWindowListener("enigma:user-session-updated", userSessionHandler);
        },

        disposeUserSessionListener: function () {
            removeUserSessionListener();
        },

        registerDropdownOutsideClick: function (dropdownId, element, helper) {
            registerDropdownOutsideClick(dropdownId, element, helper);
        },

        unregisterDropdownOutsideClick: function (dropdownId) {
            unregisterDropdownOutsideClick(dropdownId);
        },

        consumeTutorialRequest: function () {
            const requested = getStorageItem("session", tutorialRequestKey) === "true";
            removeStorageItem("session", tutorialRequestKey);
            return requested;
        },

        requestFullscreen: async function (elementId) {
            const element = document.getElementById(elementId) || document.documentElement;
            const requestFullscreen =
                element.requestFullscreen ||
                element.webkitRequestFullscreen ||
                element.msRequestFullscreen;

            if (!requestFullscreen) {
                return false;
            }

            try {
                await Promise.resolve(requestFullscreen.call(element));
                return true;
            } catch {
                return false;
            }
        },

        isFullscreen: function () {
            return !!(
                document.fullscreenElement ||
                document.webkitFullscreenElement ||
                document.msFullscreenElement
            );
        },

        playTone: function (scaleValue) {
            const context = ensureAudioContext();
            if (!context) {
                return;
            }

            try {
                if (context.state === "suspended") {
                    context.resume().catch(() => {});
                }

                const oscillator = context.createOscillator();
                const gainNode = context.createGain();
                oscillator.type = "sine";
                oscillator.frequency.value = 220 + (Number(scaleValue) * 48);
                gainNode.gain.setValueAtTime(0.0001, context.currentTime);
                gainNode.gain.exponentialRampToValueAtTime(0.05, context.currentTime + 0.01);
                gainNode.gain.exponentialRampToValueAtTime(0.0001, context.currentTime + 0.22);
                oscillator.connect(gainNode);
                gainNode.connect(context.destination);
                oscillator.start();
                oscillator.stop(context.currentTime + 0.24);
            } catch {
            }
        },

        playDing: function (kind) {
            const context = ensureAudioContext();
            if (!context) {
                return;
            }

            try {
                if (context.state === "suspended") {
                    context.resume().catch(() => {});
                }

                const toneKind = String(kind || "").toLowerCase();
                if (toneKind === "success") {
                    playOscillatorTone(context, 740, 0.055, 0.12, "triangle");
                    window.setTimeout(function () {
                        playOscillatorTone(context, 988, 0.05, 0.18, "triangle");
                    }, 70);
                    return;
                }

                playOscillatorTone(context, 660, 0.04, 0.14, "sine");
            } catch {
            }
        },

        goBack: function (fallbackUrl) {
            if (window.history.length > 1) {
                window.history.back();
                return;
            }

            window.location.href = fallbackUrl || "/home";
        },

        connectCoopSocket: function (sessionId, helper) {
            if (!sessionId || typeof WebSocket !== "function") {
                return false;
            }

            if (coopSocket &&
                coopSocketSessionId === sessionId &&
                coopSocket.readyState !== WebSocket.CLOSING &&
                coopSocket.readyState !== WebSocket.CLOSED) {
                coopSocketDotNetRef = helper;
                return true;
            }

            disposeCoopSocketInternal();
            coopSocketSessionId = sessionId;
            coopSocketDotNetRef = helper;

            const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
            const url = `${protocol}//${window.location.host}/api/auth/multiplayer/session/ws/${encodeURIComponent(sessionId)}`;

            try {
                coopSocket = new WebSocket(url);
            } catch {
                notifyCoopSocketStatus(false);
                scheduleCoopReconnect();
                return false;
            }

            coopSocket.onopen = function () {
                clearCoopReconnect();
                notifyCoopSocketStatus(true);
            };

            coopSocket.onmessage = function (event) {
                if (!coopSocketDotNetRef) {
                    return;
                }

                coopSocketDotNetRef.invokeMethodAsync("HandleCoopSocketMessage", String(event.data || "")).catch(() => {});
            };

            coopSocket.onerror = function () {
                notifyCoopSocketStatus(false);
            };

            coopSocket.onclose = function () {
                notifyCoopSocketStatus(false);
                if (coopSocketSessionId) {
                    scheduleCoopReconnect();
                }
            };

            return true;
        },

        sendCoopSocketMessage: function (payload) {
            if (!coopSocket || coopSocket.readyState !== WebSocket.OPEN) {
                return false;
            }

            try {
                coopSocket.send(JSON.stringify(payload || {}));
                return true;
            } catch {
                return false;
            }
        },

        disposeCoopSocket: function () {
            disposeCoopSocketInternal();
            notifyCoopSocketStatus(false);
        }
    };

    registerDesktopZoomOut();
})();
