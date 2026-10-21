/* Editor de polígonos de zona (RF-04): clic para agregar vértices, arrastrar para moverlos. */
(function () {
  'use strict';
  var R = window.Routify;
  var form = document.getElementById('formZona');
  if (!form) return;

  var campo = document.getElementById('vertices'), color = form.querySelector('input[name="Color"]');
  var alm = [parseFloat(form.dataset.almacenLat) || -12.0464, parseFloat(form.dataset.almacenLng) || -77.0428];
  var mapa = R.crearMapa('mapaZona', alm[0], alm[1], 12);
  var puntos = [], marcas = [], poligono = null;

  try { var ini = JSON.parse(campo.value || '[]'); if (Array.isArray(ini)) puntos = ini.filter(function (p) { return p.length === 2; }); } catch (e) { puntos = []; }

  function pintar() {
    if (poligono) { mapa.removeLayer(poligono); poligono = null; }
    if (puntos.length >= 2) poligono = L.polygon(puntos, { color: color.value, weight: 2, fillOpacity: .15 }).addTo(mapa);
    campo.value = JSON.stringify(puntos.map(function (p) { return [+p[0].toFixed(6), +p[1].toFixed(6)]; }));
  }
  function crearMarcas() {
    marcas.forEach(function (m) { mapa.removeLayer(m); }); marcas = [];
    puntos.forEach(function (p, i) {
      var m = L.marker(p, { draggable: true, icon: R.iconoNum(i + 1, '#4F46E5') }).addTo(mapa);
      m.on('drag', function () { var ll = m.getLatLng(); puntos[i] = [ll.lat, ll.lng]; pintar(); });
      marcas.push(m);
    });
  }
  mapa.on('click', function (e) { puntos.push([e.latlng.lat, e.latlng.lng]); crearMarcas(); pintar(); });
  document.getElementById('btnDeshacer').addEventListener('click', function () { puntos.pop(); crearMarcas(); pintar(); });
  document.getElementById('btnLimpiar').addEventListener('click', function () { puntos = []; crearMarcas(); pintar(); });
  color.addEventListener('input', pintar);

  crearMarcas(); pintar();
  if (puntos.length) mapa.fitBounds(puntos, { padding: [30, 30] });

  // Otras zonas como referencia (solo lectura)
  R.json('/api/despacho/estado').then(function (e) {
    (e.zonas || []).forEach(function (z) {
      var mia = form.querySelector('input[name="Nombre"]').value;
      if (z.nombre === mia) return;
      L.polygon(z.vertices, { color: z.color, weight: 1, dashArray: '4', fillOpacity: .04, interactive: false }).addTo(mapa);
    });
  }).catch(function () {});
})();
