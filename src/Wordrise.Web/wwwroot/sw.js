/* Wordrise service worker.
 *
 * Caching policy (see docs/adr/0002-pwa-caching.md):
 * - Static assets (/css, /js, /img, /icons, /fonts, manifest): cache-first, stored after the first successful fetch.
 *   Razor adds ?v=<hash> to css/js, so a new deploy produces new URLs and never serves stale code.
 * - Page navigations: network-only; when offline, the precached /offline.html is shown.
 *   Authenticated HTML is never cached (it contains the user's name, stats and rounds).
 * - /api/*, non-GET requests and other origins: not intercepted.
 *
 * Bump CACHE_VERSION when the precache list or this policy changes.
 */
"use strict";

const CACHE_VERSION = "v3";
const STATIC_CACHE = `wordrise-static-${CACHE_VERSION}`;
const MAX_STATIC_ENTRIES = 80;

const PRECACHE_URLS = [
  "/offline.html",
  "/css/site.css",
  "/fonts/rubik-latin-wght-normal.woff2",
  "/fonts/nunito-latin-wght-normal.woff2",
  "/js/theme.js",
  "/js/offline.js",
  "/img/logo.svg",
  "/icons/icon-192.png",
  "/icons/icon-512.png",
  "/icons/favicon-32.png",
  "/manifest.webmanifest"
];

const STATIC_PREFIXES = ["/css/", "/js/", "/img/", "/icons/", "/fonts/"];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(STATIC_CACHE).then((cache) => cache.addAll(PRECACHE_URLS)));
  // No skipWaiting here: the page asks the user before activating a new version (pwa.js).
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(
        keys.filter((k) => k.startsWith("wordrise-") && k !== STATIC_CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("message", (event) => {
  if (event.data && event.data.type === "SKIP_WAITING") {
    self.skipWaiting();
  }
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  if (request.method !== "GET") { return; }

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) { return; }
  if (url.pathname.startsWith("/api/") || url.pathname === "/sw.js") { return; }

  if (request.mode === "navigate") {
    event.respondWith(networkOnlyWithOfflineFallback(request));
    return;
  }

  if (isStatic(url)) {
    event.respondWith(cacheFirst(request));
  }
});

function isStatic(url) {
  return url.pathname === "/manifest.webmanifest" || STATIC_PREFIXES.some((p) => url.pathname.startsWith(p));
}

async function networkOnlyWithOfflineFallback(request) {
  try {
    return await fetch(request);
  } catch (err) {
    const offline = await caches.match("/offline.html");
    return offline || new Response("Offline", { status: 503, headers: { "Content-Type": "text/plain" } });
  }
}

async function cacheFirst(request) {
  const cache = await caches.open(STATIC_CACHE);
  const cached = await cache.match(request);
  if (cached) { return cached; }

  // Pre-cached entries have no ?v=; fall back to them when offline.
  try {
    const response = await fetch(request);
    if (response.ok && response.type === "basic") {
      await cache.put(request, response.clone());
      trimCache(cache);
    }
    return response;
  } catch (err) {
    const fallback = await cache.match(request, { ignoreSearch: true });
    if (fallback) { return fallback; }
    throw err;
  }
}

async function trimCache(cache) {
  const keys = await cache.keys();
  const precached = new Set(PRECACHE_URLS.map((u) => new URL(u, self.location.origin).href));
  const removable = keys.filter((k) => !precached.has(k.url));
  const excess = keys.length - MAX_STATIC_ENTRIES;
  for (let i = 0; i < excess && i < removable.length; i++) {
    await cache.delete(removable[i]);
  }
}
