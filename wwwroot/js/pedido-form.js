/* Formulario de pedido/cliente: geocodificación (Nominatim vía servidor) + pin arrastrable (opción combinada). */
(function () {
  'use strict';
  var R = window.Routify;
  var form = document.getElementById('formPedido');
  if (!form) return;

  var inLat = document.getElementById('lat'), inLng = document.getElementById('lng');
  var inDir = document.getElementById('direccion'), lista = document.getElementById('resultadosGeo');
  var estado = document.getElementById('estadoPunto');
  var alm = [parseFloat(form.dataset.almacenLat) || -12.0464, parseFloat(form.dataset.almacenLng) || -77.0428];

  var lat0 = parseFloat((inLat.value || '').replace(',', '.')), lng0 = parseFloat((inLng.value || '').replace(',', '.'));
  var tiene = !isNaN(lat0) && !isNaN(lng0);
  var mapa = R.crearMapa('mapaPedido', tiene ? lat0 : alm[0], tiene ? lng0 : alm[1], tiene ? 17 : 12);
  var pin = null;

  function fijar(lat, lng, centrar) {
    inLat.value = lat.toFixed(6); inLng.value = lng.toFixed(6);
    if (!pin) {
      pin = L.marker([lat, lng], { draggable: true, icon: R.iconoPunto('#4F46E5', '●') }).addTo(mapa);
      pin.on('dragend', function () { var p = pin.getLatLng(); fijar(p.lat, p.lng, false); });
    } else pin.setLatLng([lat, lng]);
    if (centrar) mapa.setView([lat, lng], Math.max(mapa.getZoom(), 17));
    if (estado) estado.textContent = 'Punto fijado: ' + lat.toFixed(5) + ', ' + lng.toFixed(5);
  }
  if (tiene) fijar(lat0, lng0, false);
  mapa.on('click', function (e) { fijar(e.latlng.lat, e.latlng.lng, false); });
  setTimeout(function () { mapa.invalidateSize(); }, 200);

  // ---- Búsqueda de dirección
  function buscar() {
    var q = (inDir.value || '').trim();
    if (q.length < 4) { if (estado) estado.textContent = 'Escriba al menos 4 caracteres.'; return; }
    if (estado) estado.textContent = 'Buscando…';
    fetch('/api/geocode?q=' + encodeURIComponent(q), { credentials: 'same-origin' })
      .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, j: j }; }); })
      .then(function (x) {
        lista.innerHTML = '';
        if (!x.ok) { estado.textContent = x.j.error || 'No se pudo buscar. Ajuste el punto manualmente.'; lista.hidden = true; return; }
        if (!x.j.length) { estado.textContent = 'Sin resultados: haga clic en el mapa para fijar el punto.'; lista.hidden = true; return; }
        estado.textContent = 'Elija el resultado correcto o ajuste el punto.';
        x.j.forEach(function (it) {
          var li = document.createElement('li'); li.textContent = it.descripcion;
          li.addEventListener('click', function () { fijar(it.lat, it.lng, true); lista.hidden = true; });
          lista.appendChild(li);
        });
        lista.hidden = false;
        if (x.j.length === 1) { fijar(x.j[0].lat, x.j[0].lng, true); lista.hidden = true; }
      })
      .catch(function () { if (estado) estado.textContent = 'No se pudo buscar. Ajuste el punto manualmente.'; });
  }
  var btn = document.getElementById('btnBuscar');
  if (btn) btn.addEventListener('click', buscar);
  if (inDir) inDir.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); buscar(); } });

  // ---- Modo de cliente (solo en pedidos)
  var radios = form.querySelectorAll('input[name="ModoCliente"]');
  var bEx = document.getElementById('bloqueExistente'), bNu = document.getElementById('bloqueNuevo');
  function modo() {
    var v = 'existente';
    radios.forEach(function (r) { if (r.checked || (r.type === 'hidden')) v = r.value; });
    if (bEx && bNu) { bEx.hidden = v === 'nuevo'; bNu.hidden = v !== 'nuevo'; }
  }
  radios.forEach(function (r) { r.addEventListener('change', modo); }); modo();

  // ---- Detalle de productos (filas dinámicas)
  var tbody = document.querySelector('#tablaItems tbody'), btnAdd = document.getElementById('btnAgregarItem');
  function reindexar() {
    Array.prototype.forEach.call(tbody.querySelectorAll('tr'), function (tr, i) {
      tr.querySelectorAll('input').forEach(function (inp) { inp.name = inp.name.replace(/Detalles\[\d+\]/, 'Detalles[' + i + ']'); });
    });
  }
  if (tbody && btnAdd) {
    btnAdd.addEventListener('click', function () {
      var tr = document.createElement('tr');
      tr.innerHTML = '<td><input name="Detalles[0].Producto" maxlength="200" /></td>' +
        '<td><input name="Detalles[0].Cantidad" type="number" min="1" value="1" /></td>' +
        '<td><input name="Detalles[0].Observacion" maxlength="250" /></td>' +
        '<td><button type="button" class="btn-icono quitar" title="Quitar" aria-label="Quitar">✕</button></td>';
      tbody.appendChild(tr); reindexar(); tr.querySelector('input').focus();
    });
    tbody.addEventListener('click', function (e) {
      if (!e.target.classList.contains('quitar')) return;
      if (tbody.querySelectorAll('tr').length > 1) { e.target.closest('tr').remove(); reindexar(); }
      else e.target.closest('tr').querySelectorAll('input').forEach(function (i) { if (i.type !== 'number') i.value = ''; });
    });
  }
})();
