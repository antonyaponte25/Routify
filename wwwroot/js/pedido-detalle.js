/* Mapa del detalle de pedido. */
(function () {
  'use strict';
  var el = document.getElementById('mapaDetalle');
  if (!el) return;
  var lat = parseFloat(el.dataset.lat), lng = parseFloat(el.dataset.lng);
  var m = Routify.crearMapa('mapaDetalle', lat, lng, 16);
  L.marker([lat, lng], { icon: Routify.iconoPunto('#4F46E5', '●') }).addTo(m);
})();
