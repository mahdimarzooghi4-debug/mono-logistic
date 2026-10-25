const CACHE_NAME = 'mono-courier-shell-v1'
const SHELL_URLS = ['/', '/offline.html', '/icon-192.png', '/icon-512.png', '/apple-touch-icon.png', '/icon.svg']

self.addEventListener('install', event => {
  event.waitUntil((async () => {
    await self.skipWaiting()
    const cache = await caches.open(CACHE_NAME)
    const shell = await fetch('/')
    if (!shell.ok) throw new Error('Courier application shell could not be cached')
    await cache.put('/', shell.clone())
    const html = await shell.text()
    const buildAssets = [...html.matchAll(/(?:src|href)=["']([^"']+)["']/g)]
      .map(match => new URL(match[1], self.location.origin))
      .filter(url => url.origin === self.location.origin && /\.(?:js|css|webmanifest)$/.test(url.pathname))
      .map(url => url.pathname + url.search)
    await cache.addAll([...new Set([...SHELL_URLS, ...buildAssets])])
  })())
})

self.addEventListener('activate', event => {
  event.waitUntil((async () => {
    const names = await caches.keys()
    await Promise.all(names.filter(name => name.startsWith('mono-courier-shell-') && name !== CACHE_NAME).map(name => caches.delete(name)))
    await self.clients.claim()
  })())
})

self.addEventListener('fetch', event => {
  const request = event.request
  const url = new URL(request.url)
  if (request.method !== 'GET' || url.origin !== self.location.origin || url.pathname.startsWith('/courier/')) return

  if (request.mode === 'navigate') {
    event.respondWith((async () => {
      const cache = await caches.open(CACHE_NAME)
      try {
        const response = await fetch(request)
        if (response.ok) await cache.put('/', response.clone())
        return response
      } catch {
        return await cache.match('/') || await cache.match('/offline.html') || Response.error()
      }
    })())
    return
  }

  if (!['script', 'style', 'image', 'font', 'manifest'].includes(request.destination)) return
  event.respondWith((async () => {
    const cache = await caches.open(CACHE_NAME)
    const cached = await cache.match(request)
    const update = fetch(request).then(async response => {
      if (response.ok) await cache.put(request, response.clone())
      return response
    }).catch(() => undefined)
    event.waitUntil(update.then(() => undefined))
    return cached || await update || Response.error()
  })())
})
