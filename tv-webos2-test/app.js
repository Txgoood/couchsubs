// SPDX-License-Identifier: AGPL-3.0-only
// ES5 syntax: webOS 2 uses an older WebKit engine.
(function () {
 'use strict';
 var host=document.getElementById('hdmi'), panel=document.getElementById('panel');
 var status=document.getElementById('status'), caption=document.getElementById('subtitle');
 var buttons=document.getElementsByTagName('button'), selected=0, generation=0, timer;
 function showPanel(){panel.style.display='block';buttons[selected].focus();}
 function hidePanel(){panel.style.display='none';}
 function selectPort(port) {
  var token=++generation, video=document.createElement('video'), source=document.createElement('source');
  clearTimeout(timer);
  while(host.firstChild)host.removeChild(host.firstChild);
  video.setAttribute('autoplay','autoplay');
  source.setAttribute('type','service/webos-external');
  source.setAttribute('src','ext://hdmi:'+port);
  status.textContent='HDMI '+port+' açılıyor. Gerçek PS5 görüntüsünü gözle kontrol edin.';
  function update(message){if(token===generation)status.textContent='HDMI '+port+': '+message;}
  video.addEventListener('playing',function(){clearTimeout(timer);update('oynatma olayı alındı. PS5 ve yazıyı birlikte görüyor musunuz?');});
  video.addEventListener('error',function(){clearTimeout(timer);update('görüntü açılamadı. Kabloyu/girişi kontrol edin; eski TV desteği doğrulanamadı.');showPanel();});
  source.addEventListener('error',function(){clearTimeout(timer);update('harici giriş kaynağı desteklenmedi veya kullanılamıyor.');showPanel();});
  video.appendChild(source);host.appendChild(video);
  timer=setTimeout(function(){update('bekleniyor. Görüntü siyahsa başka giriş seçin.');},10000);
 }
 for(var i=0;i<buttons.length;i++)(function(index){
  buttons[index].onclick=function(){selected=index;var port=this.getAttribute('data-port');if(port)selectPort(port);else hidePanel();};
  buttons[index].onfocus=function(){selected=index;};
 })(i);
 document.addEventListener('keydown',function(e){
  var key=e.keyCode;
  if(key===461||key===27){e.preventDefault();clearTimeout(timer);generation++;while(host.firstChild)host.removeChild(host.firstChild);window.close();return;}
  if(key>=49&&key<=52){e.preventDefault();selectPort(key-48);return;}
  if(key===13&&panel.style.display==='none'){e.preventDefault();showPanel();return;}
  if(key===37||key===39||key===38||key===40){
   e.preventDefault();showPanel();selected=(selected+(key===37||key===38?-1:1)+buttons.length)%buttons.length;buttons[selected].focus();
  }
 });
 // Hide the static caption after five minutes; OK restores the test.
 setTimeout(function(){caption.style.display='none';status.textContent='5 dakikalık test tamamlandı. Yeniden başlatmak için uygulamayı kapatıp açın.';showPanel();},300000);
 showPanel();selectPort(1);
})();
