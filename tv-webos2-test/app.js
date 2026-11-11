// SPDX-License-Identifier: AGPL-3.0-only
(function () {
  'use strict';

  var host = document.getElementById('hdmi');
  var panel = document.getElementById('panel');
  var status = document.getElementById('status');
  var connection = document.getElementById('connection');
  var address = document.getElementById('address');
  var caption = document.getElementById('subtitle');
  var captionText = document.getElementById('text');
  var controls = panel.querySelectorAll('button, input');
  var selected = 2;
  var activePort = null;
  var videoTimer;
  var hideTimer;
  var holdUntil = 0;
  var stream = null;

  function load(key) {
    try { return window.localStorage.getItem(key); } catch (error) { return null; }
  }

  function save(key, value) {
    try { window.localStorage.setItem(key, value); } catch (error) { /* Storage is optional. */ }
  }

  function showPanel() {
    panel.style.display = 'block';
    controls[selected].focus();
  }

  function selectPort(port) {
    if (!/^[1-4]$/.test(port)) return;
    clearTimeout(videoTimer);
    activePort = String(port);
    save('hdmiPort', activePort);
    while (host.firstChild) host.removeChild(host.firstChild);

    var video = document.createElement('video');
    var source = document.createElement('source');
    video.setAttribute('autoplay', 'autoplay');
    source.setAttribute('type', 'service/webos-external');
    source.setAttribute('src', 'ext://hdmi:' + port);

    function update(message) {
      if (video.parentNode !== host) return;
      clearTimeout(videoTimer);
      status.textContent = 'HDMI ' + port + ': ' + message;
    }

    status.textContent = 'HDMI ' + port + ' açılıyor.';
    video.addEventListener('playing', function () { update('görüntü açıldı.'); });
    video.addEventListener('error', function () { update('görüntü açılamadı. PS5 ve kabloyu kontrol edin.'); });
    source.addEventListener('error', function () { update('HDMI sinyali alınamadı.'); });
    video.appendChild(source);
    videoTimer = setTimeout(function () { update('sinyal bekleniyor. PS5 açık mı?'); }, 10000);
    host.appendChild(video);
  }

  function hideCaption() {
    clearTimeout(hideTimer);
    holdUntil = 0;
    captionText.textContent = '';
    caption.style.display = 'none';
  }

  function showCaption(data) {
    if (typeof data.text !== 'string' || !data.text.trim()) return;
    var minimum = Math.min(5000, 2000 + data.text.length * 60);
    var duration = typeof data.duration === 'number' ? data.duration : minimum;
    captionText.textContent = data.text;
    caption.style.display = 'block';
    holdUntil = Date.now() + minimum;
    clearTimeout(hideTimer);
    hideTimer = setTimeout(hideCaption, Math.max(minimum, Math.min(15000, duration)));
  }

  function clearCaption() {
    var remaining = holdUntil - Date.now();
    if (remaining <= 0) return hideCaption();
    clearTimeout(hideTimer);
    hideTimer = setTimeout(hideCaption, remaining);
  }

  function applyStyle(data) {
    if (typeof data.fontSize === 'number') {
      captionText.style.fontSize = Math.max(16, Math.min(96, data.fontSize)) + 'px';
    }
    if (typeof data.background === 'number') {
      captionText.style.background = 'rgba(0,0,0,' + Math.max(0, Math.min(100, data.background)) / 100 + ')';
    }
    if (typeof data.bold === 'boolean') captionText.style.fontWeight = data.bold ? 'bold' : 'normal';
    if (typeof data.bottomPercent === 'number' && data.bottomPercent >= 0) {
      caption.style.bottom = Math.min(75, data.bottomPercent) + '%';
    }
  }

  function normalizeAddress(value) {
    var match = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?::(\d{1,5}))?$/.exec(value.trim());
    if (!match) return null;
    var octets = match.slice(1, 5).map(Number);
    var port = match[5] ? Number(match[5]) : 8787;
    if (octets.some(function (octet) { return octet > 255; }) || port < 1 || port > 65535) return null;
    return octets.join('.') + ':' + port;
  }

  function disconnect() {
    var old = stream;
    stream = null;
    if (old) old.close();
    hideCaption();
  }

  function connect() {
    var endpoint = normalizeAddress(address.value);
    if (!endpoint) {
      connection.textContent = 'IPv4 adresini kontrol edin. Örnek: 192.168.1.20';
      return;
    }
    disconnect();
    if (!window.EventSource) {
      connection.textContent = 'Bu TV yazılımında canlı altyazı bağlantısı (EventSource) bulunamadı.';
      return;
    }

    connection.textContent = 'Bağlanıyor: ' + endpoint;
    var source;
    try {
      source = new window.EventSource('http://' + endpoint + '/events');
    } catch (error) {
      connection.textContent = 'Canlı altyazı bağlantısı açılamadı.';
      return;
    }
    stream = source;
    address.blur();

    function onEvent(name, handler) {
      source.addEventListener(name, function (event) {
        if (stream !== source) return;
        var data;
        try { data = JSON.parse(event.data); } catch (error) { return; }
        if (data && typeof data === 'object') handler(data);
      });
    }

    source.onopen = function () {
      if (stream !== source) return;
      save('serverAddress', endpoint);
      connection.textContent = 'Bağlandı. Oyundaki İngilizce altyazıları açın.';
    };
    source.onerror = function () {
      if (stream !== source) return;
      connection.textContent = source.readyState === 2
        ? 'Bağlanamadı. Windows çevirisini, IPv4 adresini ve aynı ağda olduğunuzu kontrol edip Bağlan’a basın.'
        : 'Bağlantı kesildi. Yeniden bağlanılıyor…';
    };
    onEvent('subtitle', showCaption);
    onEvent('style', applyStyle);
    source.addEventListener('clear', function () { if (stream === source) clearCaption(); });
  }

  for (var i = 0; i < controls.length; i++) {
    (function (index) {
      controls[index].onfocus = function () { selected = index; };
      var port = controls[index].getAttribute('data-port');
      if (port) controls[index].onclick = function () { selectPort(port); };
    })(i);
  }
  document.getElementById('connect').onclick = connect;
  document.getElementById('toggle').onclick = function () { panel.style.display = 'none'; };

  document.addEventListener('keydown', function (event) {
    var key = event.keyCode;
    if (document.activeElement === address) {
      if (key === 461 || key === 27 || key === 40) {
        event.preventDefault();
        document.getElementById('connect').focus();
      }
      return;
    }
    if (key === 461 || key === 27) {
      event.preventDefault();
      disconnect();
      clearTimeout(videoTimer);
      while (host.firstChild) host.removeChild(host.firstChild);
      window.close();
      return;
    }
    if (key >= 49 && key <= 52) {
      event.preventDefault();
      selectPort(key - 48);
      return;
    }
    if (key === 13 && panel.style.display === 'none') {
      event.preventDefault();
      showPanel();
      return;
    }
    if (key >= 37 && key <= 40 && panel.style.display !== 'none') {
      event.preventDefault();
      var step = key === 37 || key === 38 ? -1 : 1;
      selected = (selected + step + controls.length) % controls.length;
      controls[selected].focus();
    }
  });

  window.addEventListener('unload', disconnect);
  var savedPort = load('hdmiPort');
  selectPort(/^[1-4]$/.test(savedPort) ? savedPort : '3');
  selected = Number(activePort) - 1;
  showPanel();
  address.value = load('serverAddress') || '';
  if (address.value) connect();
})();
