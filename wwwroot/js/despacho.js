/* Mapa en vivo del panel de despacho (RF-13): zonas, pedidos pendientes, repartidores y rutas. */
(function () {
  'use strict';
  var R = window.Routify;
  var el = document.getElementById('mapaDespacho');
  if (!el) return;

  var mapa = R.crearMapa('mapaDespacho', -12.0464, -77.0428, 12);
  var capaZonas = L.layerGroup().addTo(mapa), capaPend = L.layerGroup().addTo(mapa),
      capaRutas = L.layerGroup().addTo(mapa), capaRep = L.layerGroup().addTo(mapa);
  var marcadores = {};          // repartidorId -> marker
  var reps = {};                // repartidorId -> datos
  var ajustado = false;

  function dibujar(e) {
    capaZonas.clearLayers(); capaPend.clearLayers(); capaRutas.clearLayers(); capaRep.clearLayers();
    marcadores = {}; reps = {};
    var limites = [];

    if (e.almacen) {
      L.marker([e.almacen.lat, e.almacen.lng], { icon: R.iconoPunto('#0F172A', '⌂') }).addTo(capaRep)
        .bindPopup('<strong>' + R.esc(e.almacen.nombre) + '</strong><br>Punto de recogida');
      limites.push([e.almacen.lat, e.almacen.lng]);
    }
    (e.zonas || []).forEach(function (z) {
      L.polygon(z.vertices, { color: z.color, weight: 2, fillOpacity: .08 }).addTo(capaZonas).bindTooltip(R.esc(z.nombre), { sticky: true });
    });
    (e.pendientes || []).forEach(function (p) {
      L.marker([p.lat, p.lng], { icon: R.iconoPunto('#F59E0B', '!') }).addTo(capaPend)
        .bindPopup('<strong>' + R.esc(p.codigo) + '</strong><br>' + R.esc(p.cliente) + '<br>' + R.esc(p.direccion) +
                   '<br><a href="/Pedidos/Asignar/' + p.pedidoId + '">Asignar</a>');
      limites.push([p.lat, p.lng]);
    });
    (e.rutas || []).forEach(function (r) {
      var pts = (r.linea && r.linea.length > 1) ? r.linea : (r.paradas || []).map(function (p) { return [p.lat, p.lng]; });
      if (pts.length > 1) L.polyline(pts, { color: r.color, weight: 4, opacity: .8 }).addTo(capaRutas);
      (r.paradas || []).forEach(function (p) {
        if (p.estado !== 'Pendiente') return;
        L.marker([p.lat, p.lng], { icon: R.iconoNum(p.orden, r.color) }).addTo(capaRutas)
          .bindPopup('<strong>' + R.esc(p.codigo) + '</strong><br>' + R.esc(p.cliente) + '<br>ETA ' + R.hora(p.etaUtc));
        limites.push([p.lat, p.lng]);
      });
    });

    var leyenda = document.getElementById('leyendaRep'); leyenda.innerHTML = '';
    (e.repartidores || []).forEach(function (r) {
      reps[r.id] = r;
      if (r.lat != null) {
        var m = L.marker([r.lat, r.lng], { icon: R.iconoMoto(r.color, (r.nombre || '?')[0]) }).addTo(capaRep)
          .bindPopup(popRep(r));
        marcadores[r.id] = m; limites.push([r.lat, r.lng]);
      }
      var s = document.createElement('span');
      s.innerHTML = '<i class="muestra" style="background:' + r.color + '"></i> ' + R.esc(r.nombre) + ' (' + r.carga + ')';
      leyenda.appendChild(s);
    });

    if (!ajustado && limites.length) { mapa.fitBounds(limites, { padding: [30, 30], maxZoom: 14 }); ajustado = true; }
  }

  function popRep(r) {
    return '<strong>' + R.esc(r.nombre) + '</strong><br>' + (r.disponible ? 'Disponible' : 'No disponible') +
           '<br>Paradas pendientes: ' + r.carga + (r.fechaUtc ? '<br>Última posición: ' + R.hora(r.fechaUtc) : '');
  }

  function cargar() {
    return R.json('/api/despacho/estado').then(dibujar).catch(function () { /* se reintenta al reconectar */ });
  }

  // ---------------- Tiempo real (SignalR)
  var punto = document.getElementById('indConexion');
  function estadoConexion(on) { if (punto) { punto.className = 'punto ' + (on ? 'punto-on' : 'punto-off'); punto.title = on ? 'Tiempo real activo' : 'Sin conexión en tiempo real'; } }

  var con = new signalR.HubConnectionBuilder().withUrl('/hubs/tracking').withAutomaticReconnect().build();

  con.on('PosicionRepartidor', function (p) {
    var r = reps[p.repartidorId]; if (!r) return;
    r.lat = p.lat; r.lng = p.lng; r.fechaUtc = p.fechaUtc;
    var m = marcadores[p.repartidorId];
    if (m) { m.setLatLng([p.lat, p.lng]); m.setPopupContent(popRep(r)); }
    else { marcadores[p.repartidorId] = L.marker([p.lat, p.lng], { icon: R.iconoMoto(r.color, (r.nombre || '?')[0]) }).addTo(capaRep).bindPopup(popRep(r)); }
  });

  var recarga = null;
  function programarRecarga() { clearTimeout(recarga); recarga = setTimeout(cargar, 800); }

  con.on('EstadoPedido', function (e) {
    var fila = document.querySelector('#tablaActivos tr[data-pedido="' + e.pedidoId + '"]');
    if (fila) {
      var et = fila.querySelector('[data-estado]');
      if (et) { et.textContent = e.estado; et.className = 'etiqueta ' + ({ 1: 'e-pendiente', 2: 'e-prep', 3: 'e-ruta', 4: 'e-ok', 5: 'e-fallo' }[e.estadoId] || ''); }
    }
    programarRecarga();
  });
  con.on('EtaActualizado', programarRecarga);

  con.onreconnected(function () { estadoConexion(true); cargar(); });
  con.onclose(function () { estadoConexion(false); });
  con.start().then(function () { estadoConexion(true); }).catch(function () { estadoConexion(false); });

  cargar();
  setInterval(cargar, 60000); // refresco de respaldo
})();
