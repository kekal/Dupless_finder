/*! coi-serviceworker - enables SharedArrayBuffer via service worker */
if (typeof window === 'undefined') {
    // Service worker context
    self.addEventListener("install", () => self.skipWaiting());
    self.addEventListener("activate", (e) => e.waitUntil(self.clients.claim()));

    self.addEventListener("fetch", (e) => {
        // Only modify navigation requests (the HTML document)
        // This is enough to set crossOriginIsolated = true
        // Sub-resources are same-origin so they work with credentialless COEP
        if (e.request.mode === "navigate") {
            e.respondWith(
                fetch(e.request).then((response) => {
                    if (response.status === 0) return response;
                    const headers = new Headers(response.headers);
                    headers.set("Cross-Origin-Embedder-Policy", "credentialless");
                    headers.set("Cross-Origin-Opener-Policy", "same-origin");
                    return new Response(response.body, {
                        status: response.status,
                        statusText: response.statusText,
                        headers,
                    });
                }).catch((err) => {
                    console.error("COI SW fetch failed:", err);
                    return new Response("Service Unavailable", {
                        status: 503,
                        statusText: "Service Unavailable",
                    });
                })
            );
        }
        // All other requests pass through unmodified
    });
} else {
    // Window context — register the service worker, then reload once active
    if (window.crossOriginIsolated === false) {
        navigator.serviceWorker
            .register(new URL("coi-serviceworker.js", window.location.href).href)
            .then(
                (registration) => {
                    if (registration.active && !navigator.serviceWorker.controller) {
                        window.location.reload();
                    } else if (!registration.active) {
                        registration.addEventListener("updatefound", () => {
                            registration.installing.addEventListener("statechange", function () {
                                if (this.state === "activated") window.location.reload();
                            });
                        });
                    }
                },
                (err) => console.error("COI SW registration failed:", err)
            );
    }
}
