/* Página pública de seguimiento (RF-20): estado, ETA y posición del repartidor en tiempo real. */
(function () {
  'use strict';
  var R = {};
  var raiz = document.getElementById('seg');
  var token = raiz && raiz.dataset.token;
  var $ = function (id) { return document.getElementById(id); };

  // routify.js no se carga en el layout público: utilidades mínimas
  function esc(t) { return String(t == null ? '' : t).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function horaLima(iso) {
    if (!iso) return '—';
    var s = String(iso); if (!/[zZ]|[+-]\d\d:?\d\d$/.test(s)) s += 'Z';
    var d = new Date(new Date(s).getTime() - 5 * 3600 * 1000); if (isNaN(d)) return '—';
    return ('0' + d.getUTCHours()).slice(-2) + ':' + ('0' + d.getUTCMinutes()).slice(-2);
  }

  if (!token) { $('segNoVigente').hidden = false; return; }

  var mapa = null, marcaRep = null, marcaDest = null, estadoId = 0;

  function iniciarMapa(d) {
    if (mapa || !d.destino) return;
    mapa = L.map('mapaSeg').setView([d.destino.lat, d.destino.lng], 15);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '&copy; OpenStreetMap' }).addTo(mapa);
    marcaDest = L.marker([d.destino.lat, d.destino.lng], { icon: L.divIcon({ className: '', html: '<div class="pin-num" style="background:#0F172A;border-radius:6px">⌂</div>', iconSize: [26, 26], iconAnchor: [13, 13] }) }).addTo(mapa);
  }

  function moverRep(lat, lng, iso) {
    if (!mapa) return;
    var icono = L.divIcon({ className: '', html: '<div class="pin-moto" style="background:#4F46E5">🛵</div>', iconSize: [30, 30], iconAnchor: [15, 15] });
    if (!marcaRep) marcaRep = L.marker([lat, lng], { icon: icono }).addTo(mapa); else marcaRep.setLatLng([lat, lng]);
    mapa.fitBounds([[lat, lng], marcaDest.getLatLng()], { padding: [40, 40], maxZoom: 16 });
    $('segActualizado').textContent = 'Posición actualizada a las ' + horaLima(iso || new Date().toISOString());
  }

  function pintar(d) {
    estadoId = d.estadoId;
    $('segContenido').hidden = false; $('segNoVigente').hidden = true;
    $('segComercio').textContent = d.comercio;
    $('segSaludo').textContent = d.cliente ? 'Hola, ' + d.cliente : '';
    var titulos = { 1: 'Recibimos tu pedido', 2: 'Estamos preparando tu pedido', 3: 'Tu pedido está en camino', 4: '¡Tu pedido fue entregado!', 5: 'No pudimos entregar tu pedido' };
    $('segTitulo').textContent = titulos[d.estadoId] || d.estado;
    $('segEta').textContent = (d.etaUtc && d.estadoId < 4) ? 'Llegada estimada: ' + horaLima(d.etaUtc) : '';
    var det = 'Pedido ' + d.codigo + ' · ' + d.direccion;
    if (d.estadoId === 3 && d.paradasAntes > 0) det += ' · ' + d.paradasAntes + ' entrega(s) antes que la tuya';
    if (d.repartidor) det += ' · Repartidor: ' + d.repartidor + (d.vehiculo ? ' (' + d.vehiculo + ')' : '');
    $('segDetalle').textContent = det;

    document.querySelectorAll('#segPasos li').forEach(function (li) {
      var e = +li.dataset.e; li.className = e < Math.min(d.estadoId, 4) ? 'hecho' : (e === Math.min(d.estadoId, 4) ? 'hecho actual' : '');
    });
    if (d.estadoId === 5) $('segPasos').hidden = true;

    var h = $('segHistorial'); h.innerHTML = '';
    (d.historial || []).forEach(function (x) {
      var li = document.createElement('li'); li.textContent = horaLima(x.fechaUtc) + ' — ' + x.estado; h.appendChild(li);
    });

    $('mapaSeg').hidden = !(d.destino && d.estadoId <= 3);
    if (d.destino && d.estadoId <= 3) {
      iniciarMapa(d); setTimeout(function () { mapa.invalidateSize(); }, 100);
      if (d.posicion) moverRep(d.posicion.lat, d.posicion.lng, d.posicion.fechaUtc);
    }
  }

  function cargar() {
    return fetch('/api/seguimiento/' + encodeURIComponent(token), { headers: { Accept: 'application/json' } })
      .then(function (r) { if (!r.ok) throw new Error('nv'); return r.json(); })
      .then(pintar)
      .catch(function () { $('segContenido').hidden = true; $('segNoVigente').hidden = false; });
  }

  cargar().then(function () {
    if (estadoId >= 4) return;
    var con = new signalR.HubConnectionBuilder().withUrl('/hubs/tracking').withAutomaticReconnect().build();
    con.on('PosicionRepartidor', function (p) { moverRep(p.lat, p.lng, p.fechaUtc); });
    con.on('EtaPedido', function (e) {
      $('segEta').textContent = 'Llegada estimada: ' + horaLima(e.etaUtc);
    });
    con.on('EstadoPedido', function () { cargar(); });
    function unir() { return con.invoke('UnirseSeguimiento', token); }
    con.onreconnected(function () { unir(); cargar(); });
    con.start().then(unir).catch(function () { setInterval(cargar, 30000); }); // respaldo por sondeo
  });
})();
