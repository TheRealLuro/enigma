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
    const audioOptOutKey = "enigma.game.audio-opt-out";
    const audioPreferenceKey = "enigma.game.audio-preference";
    const runAudioApprovalKey = "enigma.game.run-audio-approved";
    const tutorialRequestKey = "enigma.tutorial.requested";
    const tutorialJourneyStateKey = "enigma.tutorial.journey";

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
    let tutorialObjectiveHandler = null;
    let userSessionDotNetRef = null;
    let userSessionHandler = null;
    let audioContext = null;
    let radarPingAudio = null;
    let runAmbianceAudio = null;
    let runAmbianceSessionId = null;
    let runAmbianceCycleToken = 0;
    let runAmbianceLifecycleTimeout = null;
    let runAmbianceFadeFrame = null;
    let runAmbianceStartPending = false;
    let pageHideHandler = null;
    const dropdownClosers = new Map();
    let coopSocket = null;
    let coopSocketSessionId = null;
    let coopSocketDotNetRef = null;
    let coopSocketReconnectHandle = null;
    const desktopZoomOutValue = 0.8;
    const desktopZoomOutMinWidth = 1024;
    const radarPingAudioPath = "/sound%20effects/radar-ping.mp3";
    const runAmbianceTrackPaths = [
        "/sound%20effects/ambiance/ambiance1.mp3",
        "/sound%20effects/ambiance/ambiance2.mp3",
        "/sound%20effects/ambiance/ambiance3.mp3"
    ];
    const runAmbianceTargetVolume = 0.68;
    const runAmbianceFadeInMs = 2400;
    const runAmbianceFadeOutMs = 2400;
    const runAmbianceGapMs = 0;
    const runAmbianceLoopFloorVolume = 0.18;
    const runAmbianceStartOffsetSeconds = 0.06;
    const runAmbianceTailTrimMs = 120;
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

    function getStoredAudioPreference() {
        const storedPreference = String(getStorageItem("local", audioPreferenceKey) || "").trim().toLowerCase();
        if (storedPreference === "enabled" || storedPreference === "disabled") {
            return storedPreference;
        }

        if (getStorageItem("local", audioOptOutKey) === "true") {
            return "disabled";
        }

        return "";
    }

    function isRunAudioApproved() {
        return getStorageItem("session", runAudioApprovalKey) === "true";
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

        if (tutorialObjectiveHandler) {
            window.removeEventListener("enigma:tutorial-objective-completed", tutorialObjectiveHandler);
            tutorialObjectiveHandler = null;
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

    function resumeAudioContextIfNeeded() {
        const context = ensureAudioContext();
        if (!context || context.state !== "suspended") {
            return context;
        }

        context.resume().catch(() => {});
        return context;
    }

    function clearRunAmbianceTimers() {
        if (runAmbianceLifecycleTimeout) {
            window.clearTimeout(runAmbianceLifecycleTimeout);
            runAmbianceLifecycleTimeout = null;
        }

        if (runAmbianceFadeFrame) {
            window.cancelAnimationFrame(runAmbianceFadeFrame);
            runAmbianceFadeFrame = null;
        }
    }

    function withRunAmbianceAudio(callback) {
        if (!runAmbianceAudio || typeof callback !== "function") {
            return;
        }

        try {
            callback(runAmbianceAudio);
        } catch {
        }
    }

    function easeInOutQuad(progress) {
        if (progress <= 0) {
            return 0;
        }

        if (progress >= 1) {
            return 1;
        }

        return progress < 0.5
            ? 2 * progress * progress
            : 1 - (Math.pow(-2 * progress + 2, 2) / 2);
    }

    function getRunAmbianceStartTime(audio) {
        const baseStart = Math.max(0, Number(runAmbianceStartOffsetSeconds) || 0);
        const duration = Number(audio && audio.duration);
        if (!Number.isFinite(duration) || duration <= 0) {
            return baseStart;
        }

        return Math.max(0, Math.min(baseStart, Math.max(0, duration - 0.25)));
    }

    function fadeRunAmbianceTo(targetVolume, durationMs, token, onCompleted) {
        withRunAmbianceAudio(function (audio) {
            clearRunAmbianceTimers();
            const startVolume = Number(audio.volume) || 0;
            const clampedTarget = Math.max(0, Math.min(1, Number(targetVolume) || 0));
            const totalDurationMs = Math.max(0, Number(durationMs) || 0);
            if (totalDurationMs <= 0) {
                audio.volume = clampedTarget;
                if (onCompleted) {
                    onCompleted();
                }

                return;
            }

            const start = performance.now();
            const tick = function (now) {
                if (token !== runAmbianceCycleToken || !runAmbianceAudio) {
                    return;
                }

                const elapsed = Math.max(0, now - start);
                const progress = Math.min(1, elapsed / totalDurationMs);
                const easedProgress = easeInOutQuad(progress);
                audio.volume = startVolume + ((clampedTarget - startVolume) * easedProgress);
                if (progress < 1) {
                    runAmbianceFadeFrame = window.requestAnimationFrame(tick);
                    return;
                }

                runAmbianceFadeFrame = null;
                if (onCompleted) {
                    onCompleted();
                }
            };

            runAmbianceFadeFrame = window.requestAnimationFrame(tick);
        });
    }

    function scheduleRunAmbianceFadeCycle(token) {
        withRunAmbianceAudio(function (audio) {
            if (!Number.isFinite(audio.duration) || audio.duration <= 0) {
                runAmbianceLifecycleTimeout = window.setTimeout(function () {
                    if (token !== runAmbianceCycleToken || !runAmbianceAudio) {
                        return;
                    }

                    scheduleRunAmbianceFadeCycle(token);
                }, 500);
                return;
            }

            const totalMs = audio.duration * 1000;
            const trimmedTotalMs = Math.max(0, totalMs - Math.max(0, Number(runAmbianceTailTrimMs) || 0));
            const fadeLeadMs = runAmbianceFadeOutMs + runAmbianceGapMs;
            const fadeStartMs = Math.max(0, trimmedTotalMs - fadeLeadMs);
            runAmbianceLifecycleTimeout = window.setTimeout(function () {
                if (token !== runAmbianceCycleToken || !runAmbianceAudio) {
                    return;
                }

                fadeRunAmbianceTo(runAmbianceLoopFloorVolume, runAmbianceFadeOutMs, token, function () {
                    if (token !== runAmbianceCycleToken || !runAmbianceAudio) {
                        return;
                    }

                    withRunAmbianceAudio(function (activeAudio) {
                        const restartTime = getRunAmbianceStartTime(activeAudio);
                        try {
                            activeAudio.currentTime = restartTime;
                        } catch {
                            try {
                                activeAudio.currentTime = 0;
                            } catch {
                            }
                        }

                        const continueCycle = function () {
                            if (token !== runAmbianceCycleToken || !runAmbianceAudio) {
                                return;
                            }

                            fadeRunAmbianceTo(runAmbianceTargetVolume, runAmbianceFadeInMs, token);
                            scheduleRunAmbianceFadeCycle(token);
                        };

                        if (!activeAudio.paused) {
                            continueCycle();
                            return;
                        }

                        const attemptPlay = function (onSuccess, onFailure) {
                            try {
                                const playPromise = activeAudio.play();
                                if (playPromise && typeof playPromise.then === "function") {
                                    playPromise.then(onSuccess).catch(onFailure);
                                    return;
                                }

                                onSuccess();
                            } catch {
                                onFailure();
                            }
                        };

                        activeAudio.muted = false;
                        attemptPlay(
                            continueCycle,
                            function () {
                                try {
                                    activeAudio.currentTime = restartTime;
                                } catch {
                                    try {
                                        activeAudio.currentTime = 0;
                                    } catch {
                                    }
                                }

                                activeAudio.muted = true;
                                attemptPlay(
                                    function () {
                                        activeAudio.muted = false;
                                        continueCycle();
                                    },
                                    function () {
                                        activeAudio.muted = false;
                                        runAmbianceStartPending = true;
                                    });
                            });
                    });
                });
            }, fadeStartMs);
        });
    }

    function startRunAmbiancePlayback(token) {
        withRunAmbianceAudio(function (audio) {
            runAmbianceStartPending = false;
            const startTime = getRunAmbianceStartTime(audio);
            try {
                audio.currentTime = startTime;
            } catch {
                audio.currentTime = 0;
            }
            audio.volume = 0;

            const beginCycle = function () {
                if (token !== runAmbianceCycleToken || !runAmbianceAudio) {
                    return;
                }

                fadeRunAmbianceTo(runAmbianceTargetVolume, runAmbianceFadeInMs, token);
                scheduleRunAmbianceFadeCycle(token);
            };

            const attemptPlay = function (onSuccess, onFailure) {
                try {
                    const playPromise = audio.play();
                    if (playPromise && typeof playPromise.then === "function") {
                        playPromise.then(onSuccess).catch(onFailure);
                        return;
                    }

                    onSuccess();
                } catch {
                    onFailure();
                }
            };

            audio.muted = false;
            attemptPlay(
                beginCycle,
                function () {
                    // Fallback: many browsers allow muted autoplay even when unmuted autoplay is blocked.
                    try {
                        audio.currentTime = startTime;
                    } catch {
                        audio.currentTime = 0;
                    }
                    audio.muted = true;
                    attemptPlay(
                        function () {
                            audio.muted = false;
                            beginCycle();
                        },
                        function () {
                            audio.muted = false;
                            runAmbianceStartPending = true;
                        });
                });
        });
    }

    function stopRunAmbianceInternal(resetSessionId) {
        runAmbianceCycleToken++;
        runAmbianceStartPending = false;
        clearRunAmbianceTimers();
        withRunAmbianceAudio(function (audio) {
            audio.pause();
            audio.currentTime = 0;
            audio.src = "";
        });
        runAmbianceAudio = null;
        if (resetSessionId) {
            runAmbianceSessionId = null;
        }
    }

    function tryStartPendingRunAmbiance() {
        if (!runAmbianceStartPending || !runAmbianceAudio) {
            return;
        }

        startRunAmbiancePlayback(runAmbianceCycleToken);
    }

    function ensureRadarPingAudio() {
        if (radarPingAudio) {
            return radarPingAudio;
        }

        if (typeof window.Audio !== "function") {
            return null;
        }

        try {
            radarPingAudio = new window.Audio(radarPingAudioPath);
            radarPingAudio.preload = "auto";
            radarPingAudio.volume = 0.42;
        } catch {
            radarPingAudio = null;
        }

        return radarPingAudio;
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

                // Key input counts as a user gesture and helps unlock audio playback.
                resumeAudioContextIfNeeded();
                tryStartPendingRunAmbiance();
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
            stopRunAmbianceInternal(true);
            removeStorageItem("session", runAudioApprovalKey);
            applyDesktopZoomOut();
        },

        focusElement: function (elementId) {
            const element = document.getElementById(elementId);
            if (element) {
                element.focus();
            }
        },

        getElementBounds: function (element) {
            if (!element || typeof element.getBoundingClientRect !== "function") {
                return null;
            }

            const rect = element.getBoundingClientRect();
            return {
                left: rect.left,
                top: rect.top,
                width: rect.width,
                height: rect.height
            };
        },

        capturePointer: function (element, pointerId) {
            if (!element || typeof element.setPointerCapture !== "function") {
                return;
            }

            try {
                element.setPointerCapture(pointerId);
            } catch {
            }
        },

        releasePointer: function (element, pointerId) {
            if (!element || typeof element.releasePointerCapture !== "function") {
                return;
            }

            try {
                if (typeof element.hasPointerCapture === "function" && !element.hasPointerCapture(pointerId)) {
                    return;
                }

                element.releasePointerCapture(pointerId);
            } catch {
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

        localSetJson: function (key, value) {
            setStorageItem("local", key, JSON.stringify(value));
        },

        localGetJson: function (key) {
            return parseJsonValue(getStorageItem("local", key), "local", key);
        },

        localRemove: function (key) {
            removeStorageItem("local", key);
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

        setAudioOptOut: function (value) {
            if (value) {
                setStorageItem("local", audioOptOutKey, "true");
                return;
            }

            removeStorageItem("local", audioOptOutKey);
            removeStorageItem("local", audioPreferenceKey);
        },

        getAudioOptOut: function () {
            return getStorageItem("local", audioOptOutKey) === "true";
        },

        setAudioPreference: function (mode) {
            const normalizedMode = String(mode || "").trim().toLowerCase();
            if (normalizedMode !== "enabled" && normalizedMode !== "disabled") {
                removeStorageItem("local", audioPreferenceKey);
                removeStorageItem("local", audioOptOutKey);
                return;
            }

            setStorageItem("local", audioPreferenceKey, normalizedMode);
            setStorageItem("local", audioOptOutKey, "true");
        },

        getAudioPreference: function () {
            return getStoredAudioPreference();
        },

        clearAudioPreference: function () {
            removeStorageItem("local", audioPreferenceKey);
            removeStorageItem("local", audioOptOutKey);
        },

        approveAudioForCurrentRun: function (value) {
            if (!value) {
                removeStorageItem("session", runAudioApprovalKey);
                return;
            }

            setStorageItem("session", runAudioApprovalKey, "true");
            resumeAudioContextIfNeeded();
        },

        primeAudio: function () {
            resumeAudioContextIfNeeded();
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

        setTutorialJourneyState: function (state) {
            if (!state) {
                removeStorageItem("session", tutorialJourneyStateKey);
                return;
            }

            setStorageItem("session", tutorialJourneyStateKey, JSON.stringify(state));
        },

        getTutorialJourneyState: function () {
            return parseJsonValue(getStorageItem("session", tutorialJourneyStateKey), "session", tutorialJourneyStateKey);
        },

        clearTutorialJourneyState: function () {
            removeStorageItem("session", tutorialJourneyStateKey);
        },

        reportTutorialObjective: function (objectiveKey) {
            // Objective-based tutorial completion is intentionally disabled.
            return;
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

        playRadarPing: function () {
            const context = resumeAudioContextIfNeeded();
            tryStartPendingRunAmbiance();
            const playFallback = function () {
                if (!context) {
                    return;
                }

                try {
                    playOscillatorTone(context, 520, 0.03, 0.11, "triangle");
                } catch {
                }
            };

            const pingAudio = ensureRadarPingAudio();
            if (pingAudio) {
                try {
                    pingAudio.currentTime = 0;
                    const playPromise = pingAudio.play();
                    if (playPromise && typeof playPromise.catch === "function") {
                        playPromise.catch(() => {
                            playFallback();
                        });
                    }
                    return;
                } catch {
                    playFallback();
                    return;
                }
            }

            playFallback();
        },

        startRunAmbiance: function (sessionId) {
            const normalizedSessionId = String(sessionId || "").trim();
            if (!normalizedSessionId || runAmbianceTrackPaths.length === 0) {
                return;
            }

            const storedPreference = getStoredAudioPreference();
            if (storedPreference === "disabled" || (storedPreference !== "enabled" && !isRunAudioApproved())) {
                return;
            }

            if (runAmbianceSessionId === normalizedSessionId && runAmbianceAudio) {
                return;
            }

            stopRunAmbianceInternal(false);
            runAmbianceSessionId = normalizedSessionId;

            const trackIndex = Math.floor(Math.random() * runAmbianceTrackPaths.length);
            const trackPath = runAmbianceTrackPaths[Math.max(0, Math.min(trackIndex, runAmbianceTrackPaths.length - 1))];

            try {
                runAmbianceAudio = new window.Audio(trackPath);
                runAmbianceAudio.preload = "auto";
                runAmbianceAudio.loop = false;
                runAmbianceAudio.volume = 0;
                runAmbianceAudio.playsInline = true;
            } catch {
                runAmbianceAudio = null;
                return;
            }

            const token = runAmbianceCycleToken;
            startRunAmbiancePlayback(token);
        },

        stopRunAmbiance: function () {
            stopRunAmbianceInternal(true);
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
