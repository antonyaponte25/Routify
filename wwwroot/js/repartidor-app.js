/* Routify — Web App (PWA) del repartidor.
 * Funciona sin conexión: la ruta se guarda en IndexedDB y las acciones (iniciar ruta, entregas con foto/firma)
 * se encolan en una bandeja de salida ("outbox") con un GUID; se envían cuando hay red y el servidor es idempotente. */
(function () {
  'use strict';

  // ====================================================================== Utilidades
  var $ = function (s, r) { return (r || document).querySelector(s); };
  function esc(t) { return String(t == null ? '' : t).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function guid() {
    if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) { var r = Math.random() * 16 | 0; return (c === 'x' ? r : (r & 3 | 8)).toString(16); });
  }
  function horaLima(iso) {
    if (!iso) return '—';
    var s = String(iso); if (!/[zZ]|[+-]\d\d:?\d\d$/.test(s)) s += 'Z';
    var d = new Date(new Date(s).getTime() - 5 * 3600 * 1000); if (isNaN(d)) return '—';
    return ('0' + d.getUTCHours()).slice(-2) + ':' + ('0' + d.getUTCMinutes()).slice(-2);
  }
  var vista = $('#mVista'), avisoEl = $('#mAviso');
  function aviso(txt, ok) {
    if (!txt) { avisoEl.hidden = true; return; }
    avisoEl.textContent = txt; avisoEl.className = 'm-aviso' + (ok ? ' ok' : ''); avisoEl.hidden = false;
    if (ok) setTimeout(function () { if (avisoEl.textContent === txt) avisoEl.hidden = true; }, 4000);
  }

  // ====================================================================== IndexedDB
  var dbp = null;
  function db() {
    if (dbp) return dbp;
    dbp = new Promise(function (res, rej) {
      var rq = indexedDB.open('routify-repartidor', 1);
      rq.onupgradeneeded = function () {
        var d = rq.result;
        if (!d.objectStoreNames.contains('kv')) d.createObjectStore('kv');
        if (!d.objectStoreNames.contains('outbox')) d.createObjectStore('outbox', { keyPath: 'id', autoIncrement: true });
        if (!d.objectStoreNames.contains('gps')) d.createObjectStore('gps', { keyPath: 'id', autoIncrement: true });
      };
      rq.onsuccess = function () { res(rq.result); };
      rq.onerror = function () { rej(rq.error); };
    });
    return dbp;
  }
  function tx(store, modo, fn) {
    return db().then(function (d) {
      return new Promise(function (res, rej) {
        var t = d.transaction(store, modo), s = t.objectStore(store), out = fn(s);
        t.oncomplete = function () { res(out && out.result !== undefined ? out.result : undefined); };
        t.onerror = function () { rej(t.error); };
      });
    });
  }
  var kvGet = function (k) { return tx('kv', 'readonly', function (s) { return s.get(k); }); };
  var kvSet = function (k, v) { return tx('kv', 'readwrite', function (s) { return s.put(v, k); }); };
  var kvDel = function (k) { return tx('kv', 'readwrite', function (s) { return s.delete(k); }); };
  var obAll = function () { return tx('outbox', 'readonly', function (s) { return s.getAll(); }).then(function (r) { return r || []; }); };
  var obAdd = function (o) { return tx('outbox', 'readwrite', function (s) { return s.add(o); }); };
  var obDel = function (id) { return tx('outbox', 'readwrite', function (s) { return s.delete(id); }); };
  var gpsAll = function () { return tx('gps', 'readonly', function (s) { return s.getAll(); }).then(function (r) { return r || []; }); };
  var gpsAdd = function (o) { return tx('gps', 'readwrite', function (s) { return s.add(o); }); };
  var gpsClear = function () { return tx('gps', 'readwrite', function (s) { return s.clear(); }); };

  // ====================================================================== Estado
  var ruta = null;          // respuesta de /api/repartidor/ruta (con cambios pendientes aplicados)
  var cola = 0;             // acciones pendientes de enviar
  var enLinea = navigator.onLine;
  var sincronizando = false;
  var sesionVencida = false;

  function pintarRed() {
    var el = $('#mEstadoRed');
    el.textContent = sesionVencida ? 'Sesión vencida: vuelva a iniciar sesión' : (enLinea ? 'En línea' : 'Sin conexión — se guardará en el dispositivo');
  }
  function pintarCola() { var c = $('#mCola'); c.textContent = cola; c.hidden = cola === 0; }

  // ====================================================================== Datos de la ruta
  function aplicarPendientes(r, acciones) {
    if (!r || !r.ruta) return r;
    acciones.forEach(function (a) {
      if (a.tipo === 'iniciar' && r.ruta.estado === 'Planificada') r.ruta.estado = 'Activa';
      if (a.tipo === 'entrega') {
        var p = r.ruta.paradas.filter(function (x) { return x.id === a.paradaId; })[0];
        if (p && p.estado === 'Pendiente') p.estado = a.resultado;
      }
    });
    r.ruta.hechas = r.ruta.paradas.filter(function (p) { return p.estado !== 'Pendiente'; }).length;
    return r;
  }

  function cargarLocal() {
    return Promise.all([kvGet('ruta'), obAll()]).then(function (x) {
      cola = x[1].length; pintarCola();
      ruta = x[0] ? aplicarPendientes(x[0], x[1]) : null;
    });
  }

  function traerRuta() {
    if (!enLinea) return Promise.resolve();
    return fetch('/api/repartidor/ruta', { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } })
      .then(function (r) {
        if (r.status === 401 || r.status === 403) { sesionVencida = true; pintarRed(); throw new Error('sesion'); }
        if (!r.ok) throw new Error(r.status);
        sesionVencida = false; pintarRed();
        return r.json();
      })
      .then(function (j) { return kvSet('ruta', j); })
      .then(cargarLocal)
      .catch(function () { /* se usa lo guardado */ });
  }

  // ====================================================================== Sincronización
  function csrf() {
    return fetch('/api/csrf', { credentials: 'same-origin', cache: 'no-store' })
      .then(function (r) { if (r.status === 401) { sesionVencida = true; pintarRed(); throw new Error('sesion'); } if (!r.ok) throw new Error('csrf'); return r.json(); })
      .then(function (j) { return j.token; });
  }

  function enviar(a, token) {
    var f = new FormData();
    f.append('Guid', a.guid); f.append('Tipo', a.tipo);
    if (a.rutaId != null) f.append('RutaId', a.rutaId);
    if (a.paradaId != null) f.append('ParadaId', a.paradaId);
    if (a.resultado) f.append('Resultado', a.resultado);
    if (a.observacion) f.append('Observacion', a.observacion);
    if (a.lat != null) { f.append('Lat', String(a.lat)); f.append('Lng', String(a.lng)); }
    if (a.fechaUtc) f.append('FechaUtc', a.fechaUtc);
    if (a.foto) f.append('Foto', a.foto, 'foto.jpg');
    if (a.firma) f.append('Firma', a.firma, 'firma.png');
    return fetch('/api/repartidor/acciones', { method: 'POST', body: f, credentials: 'same-origin', headers: { 'RequestVerificationToken': token } });
  }

  function sincronizar(manual) {
    if (sincronizando || !enLinea) return Promise.resolve();
    sincronizando = true;
    var rechazadas = [];
    return obAll().then(function (acciones) {
      if (!acciones.length) return null;
      return csrf().then(function (token) {
        var cadena = Promise.resolve();
        acciones.forEach(function (a) {
          cadena = cadena.then(function () {
            return enviar(a, token).then(function (r) {
              if (r.ok) return obDel(a.id);
              if (r.status === 422) {                      // rechazada por reglas de negocio: no se reintenta
                return r.json().catch(function () { return {}; }).then(function (j) {
                  rechazadas.push((a.etiqueta || a.tipo) + ': ' + (j.error || 'rechazada'));
                  return obDel(a.id);
                });
              }
              if (r.status === 401 || r.status === 403) { sesionVencida = true; pintarRed(); throw new Error('sesion'); }
              throw new Error('http ' + r.status);          // error temporal: se conserva y se reintenta
            });
          });
        });
        return cadena;
      });
    }).then(function () { return enviarGps(); })
      .then(function () { return null; })
      .catch(function () { return null; })
      .then(function () {
        sincronizando = false;
        return traerRuta().then(function () {
          if (rechazadas.length) aviso('El servidor rechazó: ' + rechazadas.join(' · '));
          else if (manual) aviso('Sincronización completada.', true);
          render();
        });
      });
  }

  function enviarGps() {
    return gpsAll().then(function (pts) {
      if (!pts.length) return;
      return csrf().then(function (token) {
        var lote = pts.slice(0, 200).map(function (p) { return { lat: p.lat, lng: p.lng, precision: p.precision, velocidad: p.velocidad, fechaUtc: p.fechaUtc }; });
        return fetch('/api/repartidor/gps-lote', {
          method: 'POST', credentials: 'same-origin',
          headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token }, body: JSON.stringify(lote)
        }).then(function (r) { if (r.ok) return gpsClear(); });
      });
    }).catch(function () {});
  }

  // ====================================================================== GPS + tiempo real
  var ultimaPos = null, watchId = null, temporizadorGps = null, conexion = null, hubOk = false, wake = null;

  function iniciarConexion() {
    if (conexion || !window.signalR) return;
    conexion = new signalR.HubConnectionBuilder().withUrl('/hubs/tracking')
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: function () { return 5000; } }).build();
    conexion.onreconnected(function () { hubOk = true; });
    conexion.onreconnecting(function () { hubOk = false; });
    conexion.onclose(function () { hubOk = false; setTimeout(function () { if (enLinea) { conexion = null; iniciarConexion(); } }, 5000); });
    conexion.start().then(function () { hubOk = true; }).catch(function () { hubOk = false; conexion = null; });
  }

  function emitirPosicion() {
    if (!ultimaPos) return;
    var p = ultimaPos;
    var dto = { lat: p.coords.latitude, lng: p.coords.longitude, precision: p.coords.accuracy, velocidad: p.coords.speed, fechaUtc: new Date(p.timestamp).toISOString() };
    if (hubOk && conexion && conexion.state === 'Connected') {
      conexion.invoke('EnviarPosicion', dto).catch(function () { gpsAdd(dto); });
    } else {
      gpsAdd(dto);   // se envía por lote al recuperar la conexión
    }
  }

  function iniciarGps() {
    if (watchId != null || !navigator.geolocation) return;
    watchId = navigator.geolocation.watchPosition(function (p) { ultimaPos = p; },
      function (e) { if (e.code === 1) aviso('Active el permiso de ubicación para compartir su posición.'); },
      { enableHighAccuracy: true, maximumAge: 3000, timeout: 20000 });
    temporizadorGps = setInterval(emitirPosicion, 5000);       // frecuencia de 5 s
    iniciarConexion();
    if (navigator.wakeLock) navigator.wakeLock.request('screen').then(function (w) { wake = w; }).catch(function () {});
  }
  function detenerGps() {
    if (watchId != null) { navigator.geolocation.clearWatch(watchId); watchId = null; }
    if (temporizadorGps) { clearInterval(temporizadorGps); temporizadorGps = null; }
    if (wake) { wake.release().catch(function () {}); wake = null; }
  }
  function actualizarGps() {
    if (ruta && ruta.ruta && ruta.ruta.estado === 'Activa') iniciarGps(); else detenerGps();
  }
  document.addEventListener('visibilitychange', function () {
    if (document.visibilityState === 'visible' && ruta && ruta.ruta && ruta.ruta.estado === 'Activa' && navigator.wakeLock && !wake)
      navigator.wakeLock.request('screen').then(function (w) { wake = w; }).catch(function () {});
  });

  function posicionActual() {
    return new Promise(function (res) {
      if (ultimaPos && Date.now() - ultimaPos.timestamp < 60000) return res(ultimaPos.coords);
      if (!navigator.geolocation) return res(null);
      navigator.geolocation.getCurrentPosition(function (p) { res(p.coords); }, function () { res(null); }, { enableHighAccuracy: true, timeout: 8000, maximumAge: 30000 });
    });
  }

  // ====================================================================== Vistas
  var mapa = null;
  function ruteo() { return (location.hash.replace('#/', '') || 'ruta').split('?')[0]; }

  function render() {
    var v = ruteo();
    document.querySelectorAll('.m-nav a').forEach(function (a) { a.classList.toggle('act', a.dataset.ruta === v); });
    if (mapa) { mapa.remove(); mapa = null; }
    if (v === 'mapa') return vistaMapa();
    if (v === 'pendientes') return vistaPendientes();
    vistaRuta();
    actualizarGps();
  }

  function vistaRuta() {
    if (!ruta) { vista.innerHTML = '<p class="vacio">' + (enLinea ? 'Cargando…' : 'Sin datos guardados. Conéctese para descargar su ruta.') + '</p>'; return; }
    if (!ruta.ruta) { vista.innerHTML = '<p class="vacio">No tiene una ruta asignada para hoy.</p>'; return; }
    var r = ruta.ruta, h = '';
    h += '<div class="m-resumen"><div><strong>' + r.hechas + '/' + r.total + '</strong>paradas</div>' +
         '<div><strong>' + (r.distanciaKm != null ? Number(r.distanciaKm).toFixed(1) : '—') + '</strong>km</div>' +
         '<div><strong>' + (r.tiempoMin != null ? r.tiempoMin : '—') + '</strong>min</div></div>';
    h += '<p class="nota">Datos de las ' + horaLima(ruta.generadaUtc) + ' · Ruta ' + esc(r.estado) + '</p>';
    if (r.estado === 'Planificada') h += '<button class="btn btn-primario btn-bloque" id="btnIniciar" style="margin-bottom:.8rem">Iniciar ruta</button>';
    if (r.estado === 'Completada') h += '<div class="aviso aviso-ok">¡Ruta completada!</div>';

    r.paradas.forEach(function (p) {
      var hecha = p.estado !== 'Pendiente';
      h += '<div class="m-card' + (hecha ? ' hecha' : '') + '" data-parada="' + p.id + '">' +
        '<div><span class="orden">' + p.orden + '</span><strong>' + esc(p.codigo) + '</strong> · ' + esc(p.cliente) +
        ' <span class="etiqueta ' + (p.estado === 'Entregada' ? 'e-ok' : p.estado === 'Fallida' ? 'e-fallo' : 'e-pendiente') + '">' + esc(p.estado) + '</span></div>' +
        '<p style="margin:.4rem 0 0">' + esc(p.direccion) + '</p>' +
        (p.referencia ? '<small class="suave">Ref.: ' + esc(p.referencia) + '</small><br>' : '') +
        '<small class="suave">ETA ' + horaLima(p.etaUtc) + (p.ventana ? ' · Franja ' + esc(p.ventana) : '') + '</small>' +
        (p.items && p.items.length ? '<ul style="margin:.4rem 0 0;padding-left:1.1rem">' + p.items.map(function (i) { return '<li>' + i.cantidad + ' × ' + esc(i.producto) + '</li>'; }).join('') + '</ul>' : '') +
        (p.observaciones ? '<p class="nota">' + esc(p.observaciones) + '</p>' : '');
      if (!hecha) {
        h += '<div class="m-btns">' +
          '<a class="btn btn-linea" target="_blank" rel="noopener" href="https://www.google.com/maps/dir/?api=1&destination=' + p.lat + ',' + p.lng + '&travelmode=driving">Navegar</a>' +
          (p.telefono ? '<a class="btn btn-linea" href="tel:' + esc(p.telefono) + '">Llamar</a>' : '') +
          (r.estado === 'Activa' ? '<button class="btn btn-primario" data-entrega="' + p.id + '">Registrar entrega</button>' : '') + '</div>';
      }
      h += '</div>';
    });
    vista.innerHTML = h;

    var bi = $('#btnIniciar');
    if (bi) bi.addEventListener('click', iniciarRuta);
    vista.querySelectorAll('[data-entrega]').forEach(function (b) {
      b.addEventListener('click', function () { abrirEntrega(+b.dataset.entrega); });
    });
  }

  function vistaMapa() {
    vista.innerHTML = '<div id="mMapa" class="mapa" style="height:calc(100vh - 190px)"></div><p class="nota">Las teselas del mapa solo se ven sin conexión si ya las visitó.</p>';
    var c = ruta && ruta.almacen ? [ruta.almacen.lat, ruta.almacen.lng] : [-12.0464, -77.0428];
    mapa = L.map('mMapa').setView(c, 13);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '&copy; OpenStreetMap' }).addTo(mapa);
    var pts = [];
    if (ruta && ruta.ruta) ruta.ruta.paradas.forEach(function (p) {
      var color = p.estado === 'Entregada' ? '#059669' : p.estado === 'Fallida' ? '#DC2626' : '#4F46E5';
      L.marker([p.lat, p.lng], { icon: L.divIcon({ className: '', html: '<div class="pin-num" style="background:' + color + '">' + p.orden + '</div>', iconSize: [26, 26], iconAnchor: [13, 13] }) })
        .addTo(mapa).bindPopup('<strong>' + esc(p.codigo) + '</strong><br>' + esc(p.direccion));
      pts.push([p.lat, p.lng]);
    });
    if (ultimaPos) {
      var yo = [ultimaPos.coords.latitude, ultimaPos.coords.longitude];
      L.circleMarker(yo, { radius: 8, color: '#fff', weight: 3, fillColor: '#0EA5E9', fillOpacity: 1 }).addTo(mapa);
      pts.push(yo);
    }
    if (pts.length) mapa.fitBounds(pts, { padding: [30, 30], maxZoom: 16 });
    setTimeout(function () { if (mapa) mapa.invalidateSize(); }, 150);
  }

  function vistaPendientes() {
    obAll().then(function (acc) {
      cola = acc.length; pintarCola();
      var h = '<div class="m-card"><strong>Envío de datos</strong><p class="nota">' +
        (enLinea ? 'Hay conexión.' : 'Sin conexión.') + ' Las entregas registradas sin red se guardan aquí y se envían automáticamente.</p>' +
        '<button class="btn btn-primario btn-bloque" id="btnSync"' + (enLinea ? '' : ' disabled') + '>Sincronizar ahora</button></div>';
      if (!acc.length) h += '<p class="vacio">No hay acciones pendientes.</p>';
      acc.forEach(function (a) {
        h += '<div class="m-card"><strong>' + esc(a.etiqueta || a.tipo) + '</strong><br><small class="suave">Registrada a las ' + horaLima(a.fechaUtc) +
             (a.foto ? ' · con foto' : '') + (a.firma ? ' · con firma' : '') + '</small></div>';
      });
      vista.innerHTML = h;
      var b = $('#btnSync'); if (b) b.addEventListener('click', function () { sincronizar(true); });
    });
  }

  // ====================================================================== Acciones del repartidor
  function iniciarRuta() {
    if (!ruta || !ruta.ruta) return;
    if (!confirm('¿Iniciar la ruta? Se notificará a los clientes que su pedido va en camino.')) return;
    var a = { guid: guid(), tipo: 'iniciar', rutaId: ruta.ruta.id, etiqueta: 'Iniciar ruta', fechaUtc: new Date().toISOString() };
    obAdd(a).then(cargarLocal).then(function () { render(); sincronizar(false); });
  }

  // ---- Hoja de entrega (foto + firma)
  function abrirEntrega(paradaId) {
    var p = ruta.ruta.paradas.filter(function (x) { return x.id === paradaId; })[0];
    if (!p) return;
    var estado = { foto: null, firma: false };
    var hoja = document.createElement('div');
    hoja.className = 'm-hoja';
    hoja.innerHTML = '<div><h2>' + esc(p.codigo) + ' · ' + esc(p.cliente) + '</h2>' +
      '<p class="nota">' + esc(p.direccion) + '</p>' +
      '<label>Resultado</label><div class="conmutador"><label><input type="radio" name="res" value="Entregada" checked> Entregada</label><label><input type="radio" name="res" value="Fallida"> No se pudo entregar</label></div>' +
      '<div id="bloqueOk"><label>Fotografía de la entrega</label><input type="file" accept="image/*" capture="environment" id="inFoto"><img id="prevFoto" class="m-prev" alt="" hidden>' +
      '<label>Firma del cliente (opcional si hay foto)</label><canvas id="cvFirma" class="m-firma" width="600" height="320"></canvas>' +
      '<button type="button" class="btn btn-linea btn-peq" id="btnBorrarFirma" style="margin-top:.3rem">Borrar firma</button></div>' +
      '<label>Observación <span id="obligatorio" class="suave"></span></label><textarea id="inObs" rows="2" maxlength="500"></textarea>' +
      '<div id="errEntrega" class="error-campo" hidden></div>' +
      '<div class="m-btns"><button class="btn btn-linea" id="btnCancelar">Cancelar</button><button class="btn btn-primario" id="btnConfirmar">Confirmar</button></div></div>';
    document.body.appendChild(hoja);

    var rad = hoja.querySelectorAll('input[name="res"]'), bloque = $('#bloqueOk', hoja), obl = $('#obligatorio', hoja);
    function modo() {
      var f = hoja.querySelector('input[name="res"]:checked').value === 'Fallida';
      bloque.hidden = f; obl.textContent = f ? '(obligatoria: indique el motivo)' : '';
    }
    rad.forEach(function (r) { r.addEventListener('change', modo); }); modo();

    // Fotografía: se reduce a 1280 px y JPEG para ahorrar datos
    $('#inFoto', hoja).addEventListener('change', function (e) {
      var f = e.target.files[0]; if (!f) return;
      reducir(f, 1280, 0.8).then(function (blob) {
        estado.foto = blob;
        var im = $('#prevFoto', hoja); im.src = URL.createObjectURL(blob); im.hidden = false;
      }).catch(function () { $('#errEntrega', hoja).textContent = 'No se pudo leer la imagen.'; $('#errEntrega', hoja).hidden = false; });
    });

    // Firma en canvas
    var cv = $('#cvFirma', hoja), ctx = cv.getContext('2d'), dibujando = false;
    ctx.lineWidth = 3; ctx.lineCap = 'round'; ctx.strokeStyle = '#1E1B4B';
    function punto(ev) {
      var r = cv.getBoundingClientRect(), t = ev.touches ? ev.touches[0] : ev;
      return [(t.clientX - r.left) * cv.width / r.width, (t.clientY - r.top) * cv.height / r.height];
    }
    function ini(ev) { dibujando = true; var p = punto(ev); ctx.beginPath(); ctx.moveTo(p[0], p[1]); ev.preventDefault(); }
    function mov(ev) { if (!dibujando) return; var p = punto(ev); ctx.lineTo(p[0], p[1]); ctx.stroke(); estado.firma = true; ev.preventDefault(); }
    function fin() { dibujando = false; }
    cv.addEventListener('pointerdown', ini); cv.addEventListener('pointermove', mov);
    cv.addEventListener('pointerup', fin); cv.addEventListener('pointerleave', fin);
    $('#btnBorrarFirma', hoja).addEventListener('click', function () { ctx.clearRect(0, 0, cv.width, cv.height); estado.firma = false; });

    $('#btnCancelar', hoja).addEventListener('click', function () { hoja.remove(); });
    $('#btnConfirmar', hoja).addEventListener('click', function () {
      var res = hoja.querySelector('input[name="res"]:checked').value, obs = $('#inObs', hoja).value.trim(), err = $('#errEntrega', hoja);
      err.hidden = true;
      if (res === 'Entregada' && !estado.foto && !estado.firma) { err.textContent = 'Tome una fotografía o recoja la firma del cliente.'; err.hidden = false; return; }
      if (res === 'Fallida' && !obs) { err.textContent = 'Indique el motivo de la entrega fallida.'; err.hidden = false; return; }
      $('#btnConfirmar', hoja).disabled = true;

      var firmaP = (res === 'Entregada' && estado.firma) ? new Promise(function (ok) { cv.toBlob(ok, 'image/png'); }) : Promise.resolve(null);
      Promise.all([firmaP, posicionActual()]).then(function (x) {
        var c = x[1];
        var a = {
          guid: guid(), tipo: 'entrega', rutaId: ruta.ruta.id, paradaId: paradaId, resultado: res,
          observacion: obs || null, lat: c ? c.latitude : null, lng: c ? c.longitude : null,
          fechaUtc: new Date().toISOString(), foto: res === 'Entregada' ? estado.foto : null, firma: x[0],
          etiqueta: (res === 'Entregada' ? 'Entrega ' : 'Entrega fallida ') + p.codigo
        };
        return obAdd(a);
      }).then(cargarLocal).then(function () {
        hoja.remove(); render(); sincronizar(false);
      }).catch(function () { err.textContent = 'No se pudo guardar en el dispositivo (¿almacenamiento lleno?).'; err.hidden = false; $('#btnConfirmar', hoja).disabled = false; });
    });
  }

  function reducir(file, max, calidad) {
    return new Promise(function (res, rej) {
      var img = new Image(), url = URL.createObjectURL(file);
      img.onload = function () {
        var k = Math.min(1, max / Math.max(img.width, img.height)), c = document.createElement('canvas');
        c.width = Math.round(img.width * k); c.height = Math.round(img.height * k);
        c.getContext('2d').drawImage(img, 0, 0, c.width, c.height);
        URL.revokeObjectURL(url);
        c.toBlob(function (b) { b ? res(b) : rej(); }, 'image/jpeg', calidad);
      };
      img.onerror = function () { URL.revokeObjectURL(url); rej(); };
      img.src = url;
    });
  }

  // ====================================================================== Arranque
  window.addEventListener('online', function () { enLinea = true; pintarRed(); iniciarConexion(); sincronizar(false); });
  window.addEventListener('offline', function () { enLinea = false; pintarRed(); });
  window.addEventListener('hashchange', render);

  $('#formSalir').addEventListener('submit', function (e) {
    if (cola > 0 && !confirm('Tiene ' + cola + ' acción(es) sin enviar. Si cierra sesión podría perderlas. ¿Continuar?')) { e.preventDefault(); return; }
    kvDel('ruta');
  });

  if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js', { scope: '/' }).catch(function () {});
  if (navigator.storage && navigator.storage.persist) navigator.storage.persist().catch(function () {});

  pintarRed();
  cargarLocal().then(function () { render(); return traerRuta(); }).then(function () { render(); return sincronizar(false); });
  setInterval(function () { if (enLinea && !sincronizando) { obAll().then(function (a) { if (a.length) sincronizar(false); else traerRuta().then(function () { if (ruteo() === 'ruta' && !document.querySelector('.m-hoja')) render(); }); }); } }, 60000);
})();
