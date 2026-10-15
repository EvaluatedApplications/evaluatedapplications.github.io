/*
 * UNREFERENCED as of 2026-08-28 (same day it was vendored) — WasmEnableThreads was reverted to false
 * after a real-device regression (a laptop's Continue button on /tools/prism stuck permanently
 * disabled while a phone loading the same deploy was fine — see Showroom.csproj's own comment for the
 * full incident writeup). This build no longer requires cross-origin isolation to boot at all, so
 * index.html no longer loads this file, and its boot script no longer gates on
 * window.crossOriginIsolated. Left in place, not deleted, only in case threading is deliberately
 * re-enabled later with that regression reproduced and fixed first — re-wire by restoring the
 * `<script src="coi-serviceworker.js"></script>` tag as the first script in index.html's <head> again.
 * index.html now also actively unregisters any stale registration of this exact worker on every load,
 * so a laptop that visited on the one day this was live doesn't stay stuck behind it.
 *
 * Vendored 2026-08-28 for real WASM multithreading (WasmEnableThreads=true, see Showroom.csproj).
 *
 * WHY THIS FILE EXISTS: the multithreaded .NET WASM runtime uses real OS threads (pthreads) backed
 * by SharedArrayBuffer. Browsers only expose SharedArrayBuffer / set `self.crossOriginIsolated =
 * true` on a page served with two response headers:
 *   Cross-Origin-Opener-Policy: same-origin
 *   Cross-Origin-Embedder-Policy: require-corp   (or "credentialless", see below)
 * GitHub Pages is static hosting with no server-side header control, so it can never send these
 * itself. This script is the standard client-side workaround: on first load it registers ITSELF as
 * a Service Worker; from then on, every response the SW's own fetch handler returns (including the
 * page's own HTML on the next navigation) has those two headers injected, so the browser treats the
 * page as cross-origin-isolated even though the origin server never set anything.
 *
 * SOURCE: coi-serviceworker v0.1.7 by Guido Zuidhof and contributors, MIT licensed
 * (https://github.com/gzuidhof/coi-serviceworker). Vendored (not CDN-referenced) so a production
 * deploy never depends on a third-party host being up, kept verbatim (well-tested, widely deployed
 * by pyodide.org and other WASM-threading sites) rather than hand-rewritten — only this header
 * comment was added.
 *
 * MECHANISM, traced end to end (not trusted blindly):
 *   - Loaded as a normal page <script>: on first-ever visit there's no active/controlling SW yet, so
 *     it calls `navigator.serviceWorker.register(thisFile)`; once that registration reaches
 *     "installing" (the `updatefound` event), it forces ONE `window.location.reload()`. This first
 *     visit's initial paint therefore runs WITHOUT cross-origin isolation — inherent to how Service
 *     Workers work (a SW can never control the very navigation that first registered it) — and then
 *     self-corrects via that one reload.
 *   - Loaded as the Service Worker itself (`self` has no `window`): its `fetch` handler rewrites
 *     every response passing through it to add COOP/COEP (+ `Cross-Origin-Resource-Policy` under
 *     strict `require-corp`), so on the SECOND and all later loads the page is cross-origin-isolated
 *     from the very first byte of the navigation response itself, before any script runs.
 *   - Default mode is `coepCredentialless: true` (COEP "credentialless", not "require-corp"): a
 *     cross-origin no-cors resource (e.g. The Analyst's free-form external CORS-permitting feed URL
 *     fetches) is still allowed through un-blocked, since we don't control other origins' CORP
 *     headers and "require-corp" would otherwise block anything cross-origin that lacks one.
 *
 * KNOWN, HONEST LIMITATION (not solved by this file, inherent to the technique): the auto-reload
 * race on a true first-ever visit can rarely fail to re-settle in one hop (documented behavior of
 * the upstream library, see its README/issues) — if the SW hasn't finished activating by the time
 * the reload navigation lands, `crossOriginIsolated` stays false and the script gives up rather than
 * looping forever. A manual refresh recovers it. This is a real, disclosed fragility of the
 * client-side COOP/COEP workaround itself, not something fixable from inside this file — see
 * Showroom/CLAUDE.md's "Real WASM multithreading" section for the full writeup.
 */
let coepCredentialless = false;
if (typeof window === 'undefined') {
    self.addEventListener("install", () => self.skipWaiting());
    self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));

    self.addEventListener("message", (ev) => {
        if (!ev.data) {
            return;
        } else if (ev.data.type === "deregister") {
            self.registration
                .unregister()
                .then(() => {
                    return self.clients.matchAll();
                })
                .then(clients => {
                    clients.forEach((client) => client.navigate(client.url));
                });
        } else if (ev.data.type === "coepCredentialless") {
            coepCredentialless = ev.data.value;
        }
    });

    self.addEventListener("fetch", function (event) {
        const r = event.request;
        if (r.cache === "only-if-cached" && r.mode !== "same-origin") {
            return;
        }

        const request = (coepCredentialless && r.mode === "no-cors")
            ? new Request(r, {
                credentials: "omit",
            })
            : r;
        event.respondWith(
            fetch(request)
                .then((response) => {
                    if (response.status === 0) {
                        return response;
                    }

                    const newHeaders = new Headers(response.headers);
                    newHeaders.set("Cross-Origin-Embedder-Policy",
                        coepCredentialless ? "credentialless" : "require-corp"
                    );
                    if (!coepCredentialless) {
                        newHeaders.set("Cross-Origin-Resource-Policy", "cross-origin");
                    }
                    newHeaders.set("Cross-Origin-Opener-Policy", "same-origin");

                    return new Response(response.body, {
                        status: response.status,
                        statusText: response.statusText,
                        headers: newHeaders,
                    });
                })
                .catch((e) => console.error(e))
        );
    });

} else {
    (() => {
        const reloadedBySelf = window.sessionStorage.getItem("coiReloadedBySelf");
        window.sessionStorage.removeItem("coiReloadedBySelf");
        const coepDegrading = (reloadedBySelf == "coepdegrade");

        // You can customize the behavior of this script through a global `coi` variable.
        const coi = {
            shouldRegister: () => !reloadedBySelf,
            shouldDeregister: () => false,
            coepCredentialless: () => true,
            coepDegrade: () => true,
            doReload: () => window.location.reload(),
            quiet: false,
            ...window.coi
        };

        const n = navigator;
        const controlling = n.serviceWorker && n.serviceWorker.controller;

        // Record the failure if the page is served by serviceWorker.
        if (controlling && !window.crossOriginIsolated) {
            window.sessionStorage.setItem("coiCoepHasFailed", "true");
        }
        const coepHasFailed = window.sessionStorage.getItem("coiCoepHasFailed");

        if (controlling) {
            // Reload only on the first failure.
            const reloadToDegrade = coi.coepDegrade() && !(
                coepDegrading || window.crossOriginIsolated
            );
            n.serviceWorker.controller.postMessage({
                type: "coepCredentialless",
                value: (reloadToDegrade || coepHasFailed && coi.coepDegrade())
                    ? false
                    : coi.coepCredentialless(),
            });
            if (reloadToDegrade) {
                !coi.quiet && console.log("Reloading page to degrade COEP.");
                window.sessionStorage.setItem("coiReloadedBySelf", "coepdegrade");
                coi.doReload("coepdegrade");
            }

            if (coi.shouldDeregister()) {
                n.serviceWorker.controller.postMessage({ type: "deregister" });
            }
        }

        // If we're already coi: do nothing. Perhaps it's due to this script doing its job, or COOP/COEP are
        // already set from the origin server. Also if the browser has no notion of crossOriginIsolated, just give up here.
        if (window.crossOriginIsolated !== false || !coi.shouldRegister()) return;

        if (!window.isSecureContext) {
            !coi.quiet && console.log("COOP/COEP Service Worker not registered, a secure context is required.");
            return;
        }

        // In some environments (e.g. Firefox private mode) this won't be available
        if (!n.serviceWorker) {
            !coi.quiet && console.error("COOP/COEP Service Worker not registered, perhaps due to private mode.");
            return;
        }

        n.serviceWorker.register(window.document.currentScript.src).then(
            (registration) => {
                !coi.quiet && console.log("COOP/COEP Service Worker registered", registration.scope);

                registration.addEventListener("updatefound", () => {
                    !coi.quiet && console.log("Reloading page to make use of updated COOP/COEP Service Worker.");
                    window.sessionStorage.setItem("coiReloadedBySelf", "updatefound");
                    coi.doReload();
                });

                // If the registration is active, but it's not controlling the page
                if (registration.active && !n.serviceWorker.controller) {
                    !coi.quiet && console.log("Reloading page to make use of COOP/COEP Service Worker.");
                    window.sessionStorage.setItem("coiReloadedBySelf", "notcontrolling");
                    coi.doReload();
                }
            },
            (err) => {
                !coi.quiet && console.error("COOP/COEP Service Worker failed to register:", err);
            }
        );
    })();
}
