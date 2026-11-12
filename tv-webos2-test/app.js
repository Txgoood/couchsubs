// SPDX-License-Identifier: AGPL-3.0-only
(function () {
  'use strict';

  var host = document.getElementById('hdmi');
  var panel = document.getElementById('panel');
  var status = document.getElementById('status');
  var connection = document.getElementById('connection');
  var address = document.getElementById('address');
  var caption = document.getElementById('subtitle');
  var cover = document.getElementById('subtitle-cover');
  var screen = document.getElementById('screen-translations');
  var screenTimer;
  var currentBox = null;
  var placement = 'cover';
  var bottomPercent = 12;
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

  function validBox(box) {
    if (!box) return null;
    var keys = ['left', 'top', 'right', 'bottom'];
    for (var i = 0; i < keys.length; i++) {
      var value = box[keys[i]];
      if (typeof value !== 'number' || !isFinite(value) || value < 0 || value > 1) return null;
    }
    return box.right > box.left && box.bottom > box.top ? box : null;
  }

  function placeBox(element, box, padding) {
    var left = Math.max(0, box.left - padding);
    var top = Math.max(0, box.top - padding);
    element.style.left = left * 100 + '%';
    element.style.top = top * 100 + '%';
    element.style.width = (Math.min(1, box.right + padding) - left) * 100 + '%';
    element.style.height = (Math.min(1, box.bottom + padding) - top) * 100 + '%';
  }

  function layoutCaption() {
    cover.style.display = 'none';
    caption.style.left = '10%';
    caption.style.right = '10%';
    caption.style.top = 'auto';
    caption.style.bottom = bottomPercent + '%';
    if (!currentBox || placement === 'fixed' || caption.style.display !== 'block') return;

    var box = currentBox;
    var center = (box.left + box.right) / 2;
    var halfWidth = Math.max(0.18, (box.right - box.left) / 2 + 0.018);
    caption.style.left = Math.max(0, center - halfWidth) * 100 + '%';
    caption.style.right = Math.max(0, 1 - center - halfWidth) * 100 + '%';

    var height = document.documentElement.clientHeight || window.innerHeight || 1080;
    var textHeight = caption.offsetHeight;
    var gap = height * 0.012;
    var top;
    if (placement === 'cover') {
      placeBox(cover, box, 0.015);
      cover.style.display = 'block';
      top = (box.top + box.bottom) * height / 2 - textHeight / 2;
    } else if ((placement === 'above' && box.top * height >= textHeight + gap) ||
               (placement === 'below' && (1 - box.bottom) * height < textHeight + gap)) {
      top = box.top * height - textHeight - gap;
    } else {
      top = box.bottom * height + gap;
    }
    caption.style.bottom = 'auto';
    caption.style.top = Math.max(0, Math.min(height - textHeight, top)) + 'px';
  }

  function hideScreen() {
    clearTimeout(screenTimer);
    while (screen.firstChild) screen.removeChild(screen.firstChild);
  }

  function showScreen(data) {
    hideScreen();
    if (!Array.isArray(data.items)) return;
    var height = document.documentElement.clientHeight || window.innerHeight || 1080;
    for (var i = 0; i < data.items.length; i++) {
      var item = data.items[i];
      var box = validBox(item && item.box);
      if (!box || typeof item.text !== 'string' || !item.text.trim()) continue;
      var tile = document.createElement('div');
      tile.className = 'screen-translation';
      if (item.paragraphs === true) tile.style.textAlign = 'left';
      tile.textContent = item.text;
      placeBox(tile, box, 0.01);
      var lineHeight = typeof item.line === 'number' && item.line > 0 ? item.line : box.bottom - box.top;
      var size = Math.max(14, Math.min(58, Math.round(height * lineHeight * 0.85)));
      tile.style.fontSize = size + 'px';
      screen.appendChild(tile);
      while (size > 14 && tile.scrollHeight > tile.clientHeight) {
        size -= 2;
        tile.style.fontSize = size + 'px';
      }
    }
    if (!screen.firstChild) return;
    hideCaption();
    var duration = typeof data.duration === 'number' && data.duration > 0 ? data.duration : 20000;
    screenTimer = setTimeout(hideScreen, Math.min(60000, duration));
  }

  function hideCaption() {
    clearTimeout(hideTimer);
    holdUntil = 0;
    currentBox = null;
    cover.style.display = 'none';
    captionText.textContent = '';
    caption.style.display = 'none';
  }

  function showCaption(data) {
    if (screen.firstChild) return;
    if (typeof data.text !== 'string' || !data.text.trim()) return;
    var minimum = Math.min(5000, 2000 + data.text.length * 60);
    var duration = typeof data.duration === 'number' ? data.duration : minimum;
    currentBox = validBox(data.box);
    captionText.textContent = data.text;
    captionText.style.textAlign = data.paragraphs === true ? 'left' : 'center';
    caption.style.display = 'block';
    layoutCaption();
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
      bottomPercent = Math.min(75, data.bottomPercent);
    }
    if (/^(cover|above|below|fixed)$/.test(data.placement)) placement = data.placement;
    layoutCaption();
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
    hideScreen();
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
    onEvent('screen', showScreen);
    source.addEventListener('screen-clear', function () { if (stream === source) hideScreen(); });
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
