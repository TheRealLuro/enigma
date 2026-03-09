(function () {
    const storageFallback = {
        local: Object.create(null),
        session: Object.create(null)
    };

    let ambientGlowElement = null;
    let ambientGlowHandler = null;
    let bootState = null;

    function getStorageArea(storageName) {
        try {
            return storageName === "local" ? window.localStorage : window.sessionStorage;
        } catch {
            return null;
        }
    }

    function dispatchArchiveEvent(name, detail) {
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

    function clearBootState() {
        if (!bootState) {
            return;
        }

        bootState.timeouts.forEach(function (timeoutHandle) {
            window.clearTimeout(timeoutHandle);
        });

        bootState = null;
    }

    window.enigmaArchiveTerminal = {
        getStorageItem: function (storageName, key) {
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
        },

        setStorageItem: function (storageName, key, value) {
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
        },

        removeStorageItem: function (storageName, key) {
            const storage = getStorageArea(storageName);
            if (storage) {
                try {
                    storage.removeItem(key);
                } catch {
                }
            }

            delete storageFallback[storageName][key];
        },

        emitArchiveEvent: function (name, detail) {
            dispatchArchiveEvent(name, detail);
        },

        focusElement: function (elementId) {
            const element = document.getElementById(elementId);
            if (element) {
                element.focus();
            }
        },

        registerAmbientGlow: function (element) {
            if (!element) {
                return;
            }

            if (ambientGlowHandler) {
                document.removeEventListener("pointermove", ambientGlowHandler, true);
            }

            ambientGlowElement = element;
            ambientGlowHandler = function (event) {
                if (!ambientGlowElement || typeof ambientGlowElement.getBoundingClientRect !== "function") {
                    return;
                }

                const rect = ambientGlowElement.getBoundingClientRect();
                const x = ((event.clientX - rect.left) / Math.max(rect.width, 1)) * 100;
                const y = ((event.clientY - rect.top) / Math.max(rect.height, 1)) * 100;
                ambientGlowElement.style.setProperty("--archive-pointer-x", x.toFixed(2) + "%");
                ambientGlowElement.style.setProperty("--archive-pointer-y", y.toFixed(2) + "%");
            };

            document.addEventListener("pointermove", ambientGlowHandler, true);
        },

        disposeAmbientGlow: function () {
            if (ambientGlowHandler) {
                document.removeEventListener("pointermove", ambientGlowHandler, true);
            }

            ambientGlowHandler = null;
            ambientGlowElement = null;
        },

        startBootSequence: function (element, dotNetRef, isFullBoot) {
            clearBootState();

            if (!element || !dotNetRef) {
                return;
            }

            dispatchArchiveEvent("enigma:archive-boot", { mode: isFullBoot ? "full" : "reentry" });

            const lines = Array.from(element.querySelectorAll("[data-boot-line='true']"));
            const cursors = lines.map(function (line) {
                return line.querySelector(".archive-boot-cursor");
            });

            const state = {
                cancelled: false,
                timeouts: []
            };

            bootState = state;

            function liveCursor(index) {
                cursors.forEach(function (cursor, cursorIndex) {
                    if (!cursor) {
                        return;
                    }

                    cursor.classList.toggle("is-live", cursorIndex === index);
                });
            }

            const revealOrder = isFullBoot
                ? lines.map(function (_, index) { return index; })
                : [0, 2, lines.length - 1];
            const stepDelay = isFullBoot ? 300 : 90;
            const completeDelay = isFullBoot ? 3200 : 320;

            revealOrder.forEach(function (lineIndex, sequenceIndex) {
                const timeoutHandle = window.setTimeout(function () {
                    if (state.cancelled || !lines[lineIndex]) {
                        return;
                    }

                    lines[lineIndex].classList.add("is-visible");
                    liveCursor(lineIndex);
                }, sequenceIndex * stepDelay);

                state.timeouts.push(timeoutHandle);
            });

            state.timeouts.push(window.setTimeout(function () {
                if (state.cancelled) {
                    return;
                }

                liveCursor(-1);
                dotNetRef.invokeMethodAsync("NotifyBootComplete").catch(function () { });
            }, completeDelay));
        },

        cancelBootSequence: function () {
            if (bootState) {
                bootState.cancelled = true;
            }

            clearBootState();
        },

        playDossierTransition: function (element, fileId, isCorrupted) {
            if (!element) {
                return;
            }

            element.classList.remove("transition-live");
            void element.offsetWidth;
            element.classList.add("transition-live");
            window.setTimeout(function () {
                element.classList.remove("transition-live");
            }, 460);

            if (isCorrupted) {
                dispatchArchiveEvent("enigma:archive-corruption", { fileId: fileId || null });
            }
        }
    };
})();
