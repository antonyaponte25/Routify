/* Routify — Service Worker de la Web App del repartidor.
 * Guarda el "shell" de la aplicación para poder abrirla sin conexión. Los datos (ruta, entregas pendientes,
 * puntos GPS) se guardan en IndexedDB desde la propia página; las llamadas a /api y /hubs nunca se cachean aquí. */
const VERSION = 'routify-v1';
const CACHE_SHELL = VERSION + '-shell';
const CACHE_TILES = VERSION + '-tiles';
const PRECARGA = [
  '/css/routify.css', '/lib/leaflet/leaflet.css', '/lib/leaflet/leaflet.js', '/lib/signalr/signalr.min.js',
  '/lib/leaflet/images/layers.png', '/icons/icon-192.png', '/icons/icon-512.png', '/manifest.webmanifest'
];
const MAX_TILES = 400;

self.addEventListener('install', (e) => {
  e.waitUntil((async () => {
    const c = await caches.open(CACHE_SHELL);
    await Promise.all(PRECARGA.map(async (u) => { try { await c.add(u); } catch (_) { /* se cacheará al usarse */ } }));
    try { const r = await fetch('/Repartidor', { credentials: 'same-origin' }); if (r.ok && !r.redirected) await c.put('/Repartidor', r); } catch (_) {}
    self.skipWaiting();
  })());
});

self.addEventListener('activate', (e) => {
  e.waitUntil((async () => {
    for (const k of await caches.keys()) if (!k.startsWith(VERSION)) await caches.delete(k);
    await self.clients.claim();
  })());
});

async function limitar(cache, max) {
  const claves = await cache.keys();
  for (let i = 0; i < claves.length - max; i++) await cache.delete(claves[i]);
}

self.addEventListener('fetch', (e) => {
  const req = e.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);

  // Teselas del mapa: primero caché, luego red
  if (url.hostname.endsWith('tile.openstreetmap.org')) {
    e.respondWith((async () => {
      const c = await caches.open(CACHE_TILES);
      const hit = await c.match(req);
      if (hit) return hit;
      try { const r = await fetch(req); c.put(req, r.clone()).then(() => limitar(c, MAX_TILES)); return r; }
      catch (_) { return new Response('', { status: 504 }); }
    })());
    return;
  }

  if (url.origin !== location.origin) return;
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/')) return; // lo gestiona la aplicación

  // Página de la aplicación: red primero, caché como respaldo sin conexión
  if (req.mode === 'navigate' && url.pathname.toLowerCase() === '/repartidor') {
    e.respondWith((async () => {
      const c = await caches.open(CACHE_SHELL);
      try {
        const r = await fetch(req);
        if (r.ok && !r.redirected) c.put('/Repartidor', r.clone());
        return r;
      } catch (_) {
        return (await c.match('/Repartidor')) || new Response('Sin conexión', { status: 503, headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
      }
    })());
    return;
  }

  // Recursos estáticos: caché primero y actualización en segundo plano
  if (/^\/(css|js|lib|icons)\//.test(url.pathname) || url.pathname === '/manifest.webmanifest') {
    e.respondWith((async () => {
      const c = await caches.open(CACHE_SHELL);
      const hit = await c.match(req, { ignoreSearch: false }) || await c.match(url.pathname);
      const red = fetch(req).then((r) => { if (r.ok) c.put(req, r.clone()); return r; }).catch(() => null);
      return hit || (await red) || new Response('', { status: 504 });
    })());
  }
});
