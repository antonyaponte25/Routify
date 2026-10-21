/* Detalle de ruta: trazado por calles con OSRM público (solo dibujo) y respaldo con líneas rectas (opción híbrida). */
(function () {
  'use strict';
  var R = window.Routify;
  var el = document.getElementById('mapaRuta');
  if (!el) return;

  var alm = [parseFloat(el.dataset.almacenLat), parseFloat(el.dataset.almacenLng)];
  var mapa = R.crearMapa('mapaRuta', alm[0], alm[1], 13);
  L.marker(alm, { icon: R.iconoPunto('#0F172A', '⌂') }).addTo(mapa).bindPopup('<strong>' + R.esc(el.dataset.almacen) + '</strong>');

  var filas = Array.prototype.slice.call(document.querySelectorAll('#tablaParadas tbody tr'));
  var pts = [alm];
  filas.forEach(function (tr) {
    var p = [parseFloat(tr.dataset.lat), parseFloat(tr.dataset.lng)];
    pts.push(p);
    L.marker(p, { icon: R.iconoNum(tr.dataset.orden, '#4F46E5') }).addTo(mapa).bindPopup('<strong>' + R.esc(tr.dataset.codigo) + '</strong>');
  });
  if (pts.length > 1) mapa.fitBounds(pts, { padding: [30, 30] });

  function rectas() {
    L.polyline(pts, { color: '#4F46E5', weight: 4, dashArray: '8 6', opacity: .85 }).addTo(mapa);
    var n = document.getElementById('notaRuta'); if (n) n.textContent = 'Trazado aproximado con líneas rectas (el servicio de calles no está disponible).';
  }
  if (pts.length < 2) return;

  var coords = pts.map(function (p) { return p[1] + ',' + p[0]; }).join(';');
  var ctrl = new AbortController(), t = setTimeout(function () { ctrl.abort(); }, 8000);
  fetch('https://router.project-osrm.org/route/v1/driving/' + coords + '?overview=full&geometries=geojson', { signal: ctrl.signal })
    .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
    .then(function (j) {
      clearTimeout(t);
      if (!j.routes || !j.routes.length) throw new Error('sin ruta');
      var linea = j.routes[0].geometry.coordinates.map(function (c) { return [c[1], c[0]]; });
      L.polyline(linea, { color: '#4F46E5', weight: 5, opacity: .85 }).addTo(mapa);
    })
    .catch(function () { clearTimeout(t); rectas(); });
})();
