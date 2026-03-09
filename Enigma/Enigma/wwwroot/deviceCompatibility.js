(function () {
    const pendingRouteKey = "enigma.compatibility.pending-route.v1";
    let dotNetRef = null;
    let resizeHandler = null;
    let orientationHandler = null;
    const mediaQueries = [];

    function getLocalStorage() {
        try {
            return window.localStorage;
        } catch {
            return null;
        }
    }

    function readMedia(query) {
        if (typeof window.matchMedia !== "function") {
            return false;
        }

        return window.matchMedia(query).matches;
    }

    function getSnapshot() {
        const userAgent = String(navigator.userAgent || "").toLowerCase();
        const userAgentMobile = /android|iphone|ipad|ipod|mobile|tablet/.test(userAgent);
        const touchCapable =
            "ontouchstart" in window ||
            (navigator.maxTouchPoints || 0) > 0;

        const orientation = window.innerWidth >= window.innerHeight ? "landscape" : "portrait";

        return {
            viewportWidth: Math.max(0, Math.round(window.innerWidth || 0)),
            viewportHeight: Math.max(0, Math.round(window.innerHeight || 0)),
            orientation: orientation,
            hasTouch: touchCapable,
            maxTouchPoints: navigator.maxTouchPoints || 0,
            primaryPointerFine: readMedia("(pointer: fine)"),
            primaryPointerCoarse: readMedia("(pointer: coarse)"),
            canHover: readMedia("(hover: hover)"),
            anyFinePointer: readMedia("(any-pointer: fine)"),
            anyCoarsePointer: readMedia("(any-pointer: coarse)"),
            userAgentMobile: userAgentMobile
        };
    }

    function notify() {
        if (!dotNetRef) {
            return;
        }

        dotNetRef.invokeMethodAsync("HandleSnapshotChanged", getSnapshot()).catch(() => {});
    }

    function registerMediaListener(query) {
        if (typeof window.matchMedia !== "function") {
            return;
        }

        const mediaQuery = window.matchMedia(query);
        const handler = function () {
            notify();
        };

        if (typeof mediaQuery.addEventListener === "function") {
            mediaQuery.addEventListener("change", handler);
        } else if (typeof mediaQuery.addListener === "function") {
            mediaQuery.addListener(handler);
        }

        mediaQueries.push({ mediaQuery, handler });
    }

    function unregisterMediaListeners() {
        for (const entry of mediaQueries.splice(0, mediaQueries.length)) {
            if (typeof entry.mediaQuery.removeEventListener === "function") {
                entry.mediaQuery.removeEventListener("change", entry.handler);
            } else if (typeof entry.mediaQuery.removeListener === "function") {
                entry.mediaQuery.removeListener(entry.handler);
            }
        }
    }

    function copyText(text) {
        if (!text) {
            return Promise.resolve(false);
        }

        if (navigator.clipboard && typeof navigator.clipboard.writeText === "function") {
            return navigator.clipboard.writeText(text)
                .then(() => true)
                .catch(() => false);
        }

        try {
            const textarea = document.createElement("textarea");
            textarea.value = text;
            textarea.setAttribute("readonly", "readonly");
            textarea.style.position = "absolute";
            textarea.style.left = "-9999px";
            document.body.appendChild(textarea);
            textarea.select();
            const copied = document.execCommand("copy");
            document.body.removeChild(textarea);
            return Promise.resolve(!!copied);
        } catch {
            return Promise.resolve(false);
        }
    }

    window.enigmaDeviceCompatibility = {
        getSnapshot: function () {
            return getSnapshot();
        },

        register: function (helper) {
            dotNetRef = helper;

            if (!resizeHandler) {
                resizeHandler = function () {
                    notify();
                };
                window.addEventListener("resize", resizeHandler, { passive: true });
            }

            if (!orientationHandler) {
                orientationHandler = function () {
                    notify();
                };
                window.addEventListener("orientationchange", orientationHandler, { passive: true });
            }

            if (mediaQueries.length === 0) {
                registerMediaListener("(pointer: fine)");
                registerMediaListener("(pointer: coarse)");
                registerMediaListener("(hover: hover)");
                registerMediaListener("(any-pointer: fine)");
                registerMediaListener("(any-pointer: coarse)");
            }

            notify();
        },

        unregister: function () {
            if (resizeHandler) {
                window.removeEventListener("resize", resizeHandler);
                resizeHandler = null;
            }

            if (orientationHandler) {
                window.removeEventListener("orientationchange", orientationHandler);
                orientationHandler = null;
            }

            unregisterMediaListeners();
            dotNetRef = null;
        },

        setPendingRoute: function (route) {
            const storage = getLocalStorage();
            if (!storage) {
                return;
            }

            try {
                storage.setItem(pendingRouteKey, route || "");
            } catch {
            }
        },

        getPendingRoute: function () {
            const storage = getLocalStorage();
            if (!storage) {
                return null;
            }

            try {
                return storage.getItem(pendingRouteKey);
            } catch {
                return null;
            }
        },

        clearPendingRoute: function () {
            const storage = getLocalStorage();
            if (!storage) {
                return;
            }

            try {
                storage.removeItem(pendingRouteKey);
            } catch {
            }
        },

        copyText: function (text) {
            return copyText(text);
        }
    };
})();
