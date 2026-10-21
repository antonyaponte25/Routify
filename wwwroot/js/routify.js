/* Routify — utilidades comunes del panel web. */
(function () {
  'use strict';

  var R = window.Routify = window.Routify || {};

  // Menú lateral en móvil
  var btn = document.getElementById('btnMenu'), lat = document.getElementById('lateral');
  if (btn && lat) btn.addEventListener('click', function () { lat.classList.toggle('abierto'); });
  document.addEventListener('click', function (e) {
    if (lat && lat.classList.contains('abierto') && !lat.contains(e.target) && e.target !== btn) lat.classList.remove('abierto');
  });

  // Botones "Copiar" (data-copiar="#selector")
  document.addEventListener('click', function (e) {
    var b = e.target.closest('[data-copiar]');
    if (!b) return;
    var el = document.querySelector(b.getAttribute('data-copiar'));
    if (!el) return;
    el.select();
    (navigator.clipboard ? navigator.clipboard.writeText(el.value) : Promise.resolve(document.execCommand('copy')))
      .then(function () { var t = b.textContent; b.textContent = '¡Copiado!'; setTimeout(function () { b.textContent = t; }, 1500); });
  });

  // Hora de Lima (UTC-5 fija) a partir de una fecha ISO en UTC
  R.hora = function (iso) {
    if (!iso) return '—';
    var s = String(iso); if (!/[zZ]|[+-]\d\d:?\d\d$/.test(s)) s += 'Z';
    var d = new Date(new Date(s).getTime() - 5 * 3600 * 1000);
    if (isNaN(d)) return '—';
    return ('0' + d.getUTCHours()).slice(-2) + ':' + ('0' + d.getUTCMinutes()).slice(-2);
  };
  R.fechaHora = function (iso) {
    if (!iso) return '—';
    var s = String(iso); if (!/[zZ]|[+-]\d\d:?\d\d$/.test(s)) s += 'Z';
    var d = new Date(new Date(s).getTime() - 5 * 3600 * 1000);
    if (isNaN(d)) return '—';
    var p = function (n) { return ('0' + n).slice(-2); };
    return p(d.getUTCDate()) + '/' + p(d.getUTCMonth() + 1) + '/' + d.getUTCFullYear() + ' ' + p(d.getUTCHours()) + ':' + p(d.getUTCMinutes());
  };

  R.esc = function (t) {
    return String(t == null ? '' : t).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  };

  // Mapa base (OpenStreetMap)
  R.crearMapa = function (id, lat, lng, zoom) {
    var m = L.map(id, { zoomControl: true }).setView([lat, lng], zoom || 12);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(m);
    return m;
  };

  R.iconoNum = function (n, color) {
    return L.divIcon({ className: '', html: '<div class="pin-num" style="background:' + (color || '#4F46E5') + '">' + n + '</div>', iconSize: [26, 26], iconAnchor: [13, 13] });
  };
  R.iconoMoto = function (color, letra) {
    return L.divIcon({ className: '', html: '<div class="pin-moto" style="background:' + (color || '#4F46E5') + '">' + R.esc(letra || '●') + '</div>', iconSize: [30, 30], iconAnchor: [15, 15] });
  };
  R.iconoPunto = function (color, simbolo) {
    return L.divIcon({ className: '', html: '<div class="pin-num" style="background:' + (color || '#334155') + ';border-radius:6px">' + (simbolo || '') + '</div>', iconSize: [26, 26], iconAnchor: [13, 13] });
  };

  // Token antiforgery fresco para peticiones fetch con método POST
  R.csrf = function () {
    return fetch('/api/csrf', { credentials: 'same-origin', cache: 'no-store' })
      .then(function (r) { if (!r.ok) throw new Error('csrf'); return r.json(); })
      .then(function (j) { return j.token; });
  };

  R.json = function (url) {
    return fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); });
  };
})();
