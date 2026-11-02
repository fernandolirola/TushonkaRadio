(()=>{'use strict';
const C=RadioCore,$=id=>document.getElementById(id),bridge=window.chrome&&window.chrome.webview,preview=!bridge;
let settings;try{settings=C.restore(localStorage.getItem('zezinho.hideoutradio.v1'));}catch{settings=C.defaults();}
let lang=settings.language,mode='menu',epoch=0,visible=true,resume=true,source='youtube',localTracks=[],onlineTracks=[],index=-1,expanded=false,playing=false,serial=0,listRequest='',audioRequest='',selectedPreset='',failure='',history=[];
const intent=new C.Intent(),audio=$('audio'),lists=new Map();
const words={en:{local:'Local music',playlists:'Playlists',tracks:'Tracks',add:'Add playlist or video',name:'Custom Name (optional)',link:'Link',save:'Save',refresh:'Refresh',path:'Add files to HideoutRadio/Player/Music.',shuffle:'Shuffle',repeat:'Repeat',volume:'Volume',choose:'Choose a playlist',play:'Play',pause:'Pause',previous:'Previous track',next:'Next track',open:'Open library',show:'Show library',collapse:'Collapse library',close:'Close player',empty:'No tracks yet.',invalidLink:'Use an HTTPS YouTube video or playlist link.',loadingList:'Loading playlist...',loadingAudio:'Preparing audio...',ready:'Ready.',paused:'Paused',raid:'Paused for raid. Your place is saved.',error:'Could not play this track. Try another one.',limit:'You can save up to 50 playlists.',remove:'Remove playlist',end:'End of playlist',menu:'MENU',hideout:'HIDEOUT',blocked:'PAUSED'},pt:{local:'M\u00fasicas locais',playlists:'Playlists',tracks:'Faixas',add:'Adicionar playlist ou v\u00eddeo',name:'Nome personalizado (opcional)',link:'Link',save:'Salvar',refresh:'Atualizar',path:'Adicione arquivos em HideoutRadio/Player/Music.',shuffle:'Aleat\u00f3rio',repeat:'Repetir',volume:'Volume',choose:'Escolha uma playlist',play:'Reproduzir',pause:'Pausar',previous:'Faixa anterior',next:'Pr\u00f3xima faixa',open:'Abrir biblioteca',show:'Mostrar biblioteca',collapse:'Recolher biblioteca',close:'Fechar player',empty:'Nenhuma faixa ainda.',invalidLink:'Use um link HTTPS de v\u00eddeo ou playlist do YouTube.',loadingList:'Carregando playlist...',loadingAudio:'Preparando \u00e1udio...',ready:'Pronto.',paused:'Pausado',raid:'Pausado para raid. Sua posi\u00e7\u00e3o foi salva.',error:'N\u00e3o foi poss\u00edvel tocar esta faixa. Tente outra.',limit:'Voc\u00ea pode salvar at\u00e9 50 playlists.',remove:'Remover playlist',end:'Fim da playlist',menu:'MENU',hideout:'ESCONDERIJO',blocked:'PAUSADO'}};
const t=k=>words[lang][k]||k,prefs=()=>settings.contexts[mode==='hideout'?'hideout':'menu'],queue=()=>source==='local'?localTracks:onlineTracks,current=()=>queue()[index];
function save(){try{localStorage.setItem('zezinho.hideoutradio.v1',JSON.stringify(settings));}catch{}}
function send(message){if(bridge)bridge.postMessage(JSON.stringify(message));else fetch('/api/command',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(message)}).then(r=>r.json()).then(receive).catch(()=>{if(message.type.startsWith('youtube')){failure=t('error');status(failure,true);intent.userPause();renderPlay();}});}
let heartbeat, initialContext = true;
let preloadEnabled = false, upcoming = null;

function clearUpcoming(){
  if(upcoming?.request)send({type:'cancel-preload',request:upcoming.request});
  upcoming=null;
}

function preloadNext(){
  if(!preloadEnabled||!playing||source!=='youtube'||!intent.canPlay()||upcoming)return;
  const nextIndex=C.nextIndex(index,queue().length,settings.shuffle,settings.repeat);
  const track=queue()[nextIndex];
  if(!track||nextIndex===index)return;
  upcoming={index:nextIndex,track,request:''};
  {
    upcoming.request='preload'+(++serial);
    send({type:'youtube-audio',id:track.id,request:upcoming.request,preload:true,protectedUrl:audio.getAttribute("src")});
  }
}
function report(){send({type:'status',epoch,playing:playing&&intent.canPlay()});}
function status(value,error=false){$('status').textContent=value;$('status').classList.toggle('error',error);}
function renderPlay(){const wants=playing||intent.desired;$('play').textContent=wants?'\u275a\u275a':'\u25b6';$('play').setAttribute('aria-label',t(wants?'pause':'play'));document.body.classList.toggle('playing',playing);}
function setPlaying(value){playing=value;renderPlay();clearInterval(heartbeat);if(value&&bridge)heartbeat=setInterval(report,1000);report();}
function shape(){const element=expanded?$('shell'):$('mini');try{window.overlay?.setShape([element]);}catch{}}
function expand(value){expanded=value;$('panel').hidden=!value;$('expand').setAttribute('aria-expanded',String(value));$('expand').textContent=value?'\u2304':'\u2303';$('expand').setAttribute('aria-label',t(value?'collapse':'show'));requestAnimationFrame(shape);}
function coverUrl(value){return typeof value==='string'&&(/^(Artwork\/[a-f0-9]{64}\.(png|jpg))$/.test(value)||/^https:\/\/i\.ytimg\.com\/vi\/[A-Za-z0-9_-]{11}\/[a-z0-9]+\.jpg$/.test(value))?value:'';}
function info(){const track=current();$('songTitle').textContent=track?.name||'Tushonka Radio';$('artist').textContent=track?.artist||track?.album||t('choose');const cover=coverUrl(track?.cover);$('cover').hidden=!cover;$('artFallback').hidden=!!cover;if(cover)$('cover').src=cover;else $('cover').removeAttribute('src');$('trackInfo').title=track?[track.name,track.artist].filter(Boolean).join(' - '):'Tushonka Radio';}
$('cover').onerror=()=>{$('cover').hidden=true;$('artFallback').hidden=false;};
function translate(){document.documentElement.lang=lang;document.querySelectorAll('[data-i18n]').forEach(el=>el.textContent=t(el.dataset.i18n));$('context').textContent=t(mode);$('previous').setAttribute('aria-label',t('previous'));$('next').setAttribute('aria-label',t('next'));$('close').setAttribute('aria-label',t('close'));$('artButton').setAttribute('aria-label',t('open'));$('trackInfo').setAttribute('aria-label',t('open'));$('language').textContent=lang==='en'?'EN / PT':'PT / EN';$('expand').setAttribute('aria-label',t(expanded?'collapse':'show'));renderLists();info();renderPlay();}
function pause(manual=true){if(manual)intent.userPause();audio.pause();setPlaying(false);if(!failure)status(t(mode==='blocked'?'raid':'paused'));}
function playable(url){return typeof url==='string'&&(C.safeTrack({name:'',url})||/^YouTubeCache\/[A-Za-z0-9_-]{11}\.(m4a|webm|opus)$/.test(url)||(preview&&source==='local'&&url.startsWith('blob:')));}
function play(){failure='';if(!intent.userPlay())return;renderPlay();if(!current()){
 if(source==='youtube'){const preset=settings.playlists.find(p=>p.id===prefs().playlist)||settings.playlists[0];if(preset){if(!listRequest||selectedPreset!==preset.id)loadList(preset,true);return;}}
 if(queue().length){select(0,true);return;}intent.userPause();renderPlay();expand(true);status(t('empty'));return;}
 const track=current();if(source==='youtube'&&!track.url){if(audioRequest)return;audioRequest='audio'+(++serial);status(t('loadingAudio'));send({type:'youtube-audio',id:track.id,request:audioRequest});return;}
 if(!playable(track.url)){intent.userPause();renderPlay();status(t('error'),true);return;}
 if(audio.getAttribute('src')!==track.url)audio.src=track.url;audio.volume=settings.volume/100;
 audio.play().catch(()=>{failure=t('error');intent.userPause();setPlaying(false);status(failure,true);expand(true);});}
function select(i,autoplay=true,pendingRequest=""){if(i<0||i>=queue().length)return;clearUpcoming();if(index>=0&&index!==i)history.push(index);pause();audioRequest=pendingRequest;index=i;if(source==="youtube")delete current().url;audio.removeAttribute('src');audio.load();prefs().track=source==='local'?current().url:current().id;save();failure='';info();renderLists();if(autoplay&&pendingRequest){intent.userPlay();renderPlay();status(t('loadingAudio'));}else if(autoplay)play();}
function next(){
  const planned=upcoming;
  if(planned&&queue()[planned.index]===planned.track){
    upcoming=null;
    select(planned.index,true,planned.request);
    return;
  }
  const i=C.nextIndex(index,queue().length,settings.shuffle,settings.repeat);
  if(i>=0)select(i,true);else{pause();status(t('end'));}
}
function previous(){if(audio.currentTime>3){audio.currentTime=0;return;}const i=history.length?history.pop():Math.max(0,index-1);select(i,true);history.pop();}
function sourceTo(next,remember=true){if(source!==next){clearUpcoming();pause();audioRequest='';source=next;index=-1;history=[];audio.removeAttribute('src');audio.load();}intent.source=source;
 if(remember){prefs().source=source;save();}$('youtubeSection').hidden=source!=='youtube';$('musicPath').hidden=source!=='local';$('previewPick').hidden=!preview||source!=='local';$('youtubeTab').classList.toggle('selected',source==='youtube');$('localTab').classList.toggle('selected',source==='local');
 if(source==='local'&&index<0&&localTracks.length){const i=localTracks.findIndex(x=>x.url===prefs().track);select(i>=0?i:0,false);}renderLists();info();}
function loadList(preset,autoplay=false){try{C.parseYouTube(preset.url);}catch{status(t('invalidLink'),true);return;}clearUpcoming();pause();sourceTo('youtube');prefs().playlist=preset.id;save();selectedPreset=preset.id;listRequest='';onlineTracks=lists.get(preset.id)||[];index=-1;history=[];renderLists();
 if(onlineTracks.length){select(0,autoplay);return;}listRequest='list'+(++serial);intent.desired=autoplay&&intent.canPlay();renderPlay();status(t('loadingList'));send({type:'youtube-list',url:preset.url,request:listRequest,fetchTitle:preset.autoName===true});}
function renderLists(){const presets=$('playlists');presets.replaceChildren();for(const preset of settings.playlists){const row=document.createElement('div');row.className='row'+(preset.id===prefs().playlist?' active':'');const button=document.createElement('button');button.className='choose';const text=document.createElement('span');text.className='title';text.textContent=preset.name;button.append(text);button.onclick=()=>loadList(preset,false);const remove=document.createElement('button');remove.className='remove';remove.textContent='\u00d7';remove.setAttribute('aria-label',t('remove')+': '+preset.name);remove.onclick=()=>{if(selectedPreset===preset.id){clearUpcoming();pause();onlineTracks=[];index=-1;listRequest='';audioRequest='';}settings.playlists=settings.playlists.filter(p=>p.id!==preset.id);if(settings.lastPlayedPlaylist===preset.id)settings.lastPlayedPlaylist='';for(const c of Object.values(settings.contexts))if(c.playlist===preset.id)c.playlist=settings.playlists[0]?.id||'';save();renderLists();info();};row.append(button,remove);presets.append(row);}
 const tracks=$('tracks');tracks.replaceChildren();queue().forEach((track,i)=>{const row=document.createElement('div');row.className='row'+(i===index?' active':'');const button=document.createElement('button');button.className='choose';const image=coverUrl(track.cover);if(image){const img=document.createElement('img');img.className='thumb';img.loading='lazy';img.decoding='async';img.src=image;img.alt='';img.onerror=()=>img.hidden=true;button.append(img);}const labels=document.createElement('span');labels.className='labels';const name=document.createElement('span');name.className='title';name.textContent=track.name;const artist=document.createElement('span');artist.className='subtitle';artist.textContent=track.artist||track.album||'';labels.append(name,artist);button.append(labels);button.onclick=()=>select(i,true);row.append(button);tracks.append(row);});if(!queue().length){const empty=document.createElement('p');empty.className='empty';empty.textContent=t('empty');tracks.append(empty);}requestAnimationFrame(shape);}
function receive(value){let msg;try{msg=typeof value==='string'?JSON.parse(value):value;}catch{return;}if(!msg||typeof msg!=='object')return;
 if(msg.type==='runtime'){const oldMode=mode,wasVisible=visible,wanted=intent.desired;mode=['menu','hideout'].includes(msg.mode)?msg.mode:'blocked';epoch=Number(msg.epoch)||0;visible=msg.visible===true;if(visible&&!wasVisible)updateSeek();resume=msg.resume!==false;lang=msg.language==='pt'?'pt':'en';
  if (mode !== 'blocked' && (initialContext || mode !== oldMode)) {
    const startupPlaylist = initialContext && settings.lastPlayedPlaylist;
    initialContext = false;
    sourceTo(prefs().source, false);
    if (source === 'youtube') {
      const preset = settings.playlists.find(p => p.id === startupPlaylist)
        || settings.playlists.find(p => p.id === prefs().playlist);
      if (preset && preset.id !== selectedPreset) loadList(preset, false);
    }
    intent.desired = wanted;
  }
  preloadEnabled=msg.preloadNext===true;
  if(!preloadEnabled||mode==='blocked')clearUpcoming();
  const action=intent.runtime(mode!=='blocked',visible,source,resume);translate();if(action==='pause')pause(false);else if(!playing)play();else preloadNext();
 }else if(msg.type==='release-cache'){
   clearUpcoming();
   if(source==='youtube'){
     audioRequest='';
     pause();
     audio.removeAttribute('src');
     audio.load();
     updateSeek();
   }
   for(const tracks of lists.values())for(const track of tracks)delete track.url;
   send({type:'cache-released'});
 }else if(msg.type==='bar-height' && Number.isFinite(msg.height)){
   document.documentElement.style.setProperty('--bar-height', Math.max(90, Math.min(220, msg.height)) + 'px');
   requestAnimationFrame(shape);
 }else if(msg.type==='library'){const id=current()?.url;localTracks=Array.isArray(msg.tracks)?msg.tracks.filter(C.safeTrack):[];if(source==='local'){index=id?localTracks.findIndex(t=>t.url===id):-1;if(id&&index<0)pause();if(index<0&&localTracks.length){const i=localTracks.findIndex(x=>x.url===prefs().track);select(i>=0?i:0,false);}}renderLists();info();if(msg.error)status(String(msg.error),true);
 }else if(msg.type==='youtube-list'&&msg.request===listRequest){listRequest='';
  const preset = settings.playlists.find(p => p.id === selectedPreset);
  if (preset?.autoName && typeof msg.title === 'string' && msg.title.trim()) {
    preset.name = msg.title.trim().slice(0,80);
    preset.autoName = false;
    save();
    renderLists();
  }
  const wanted=intent.desired;onlineTracks=Array.isArray(msg.tracks)?msg.tracks.filter(t=>t&&/^[A-Za-z0-9_-]{11}$/.test(t.id)):[];lists.set(selectedPreset,onlineTracks);index=-1;if(onlineTracks.length)select(0,false);else status(t('empty'));intent.desired=wanted;renderPlay();if(wanted&&intent.canPlay())play();
 }else if(upcoming?.request && msg.request===upcoming.request && (msg.type==='youtube-audio'||msg.type==='youtube-error')){
   upcoming.request='';
   if(msg.type==='youtube-audio'&&playable(msg.url))upcoming.track.url=msg.url;
 }else if(msg.type==='youtube-audio'&&msg.request===audioRequest&&source==='youtube'&&current()){audioRequest='';if(!playable(msg.url)){status(t('error'),true);return;}current().url=msg.url;if(intent.desired&&intent.canPlay())play();
 }else if(msg.type==='youtube-error'&&(msg.request===audioRequest||msg.request===listRequest)){if(msg.request===listRequest)listRequest='';if(msg.request===audioRequest)audioRequest='';failure=String(msg.error||t('error'));intent.userPause();setPlaying(false);status(failure,true);expand(true);}
}
audio.addEventListener('play',()=>{if(!intent.canPlay()){pause(false);return;}if(source==='youtube'&&selectedPreset&&settings.lastPlayedPlaylist!==selectedPreset){settings.lastPlayedPlaylist=selectedPreset;save();}setPlaying(true);preloadNext();status(current()?.name||t('ready'));});audio.addEventListener('pause',()=>setPlaying(false));audio.addEventListener('ended',()=>{if(intent.canPlay())next();});audio.addEventListener('error',()=>{if(audio.getAttribute('src')){failure=t('error');intent.userPause();setPlaying(false);status(failure,true);expand(true);}});function updateSeek(){$('seek').value=Number.isFinite(audio.duration)&&audio.duration>0?String(audio.currentTime/audio.duration*1000):'0';}audio.addEventListener('timeupdate',()=>{if(visible)updateSeek();});$('seek').oninput=()=>{if(Number.isFinite(audio.duration))audio.currentTime=Number($('seek').value)/1000*audio.duration;};
$('play').onclick=()=>playing||intent.desired?pause():play();$('next').onclick=next;$('previous').onclick=previous;$('expand').onclick=()=>expand(!expanded);$('artButton').onclick=()=>expand(!expanded);$('trackInfo').onclick=()=>expand(!expanded);$('close').onclick=()=>{if(bridge)send({type:'close'});else{document.getElementById('shell').hidden=true;}};
$('youtubeTab').onclick=()=>sourceTo('youtube');$('localTab').onclick=()=>sourceTo('local');$('refresh').onclick=()=>{if(source==='local')send({type:'refresh'});else{const p=settings.playlists.find(x=>x.id===prefs().playlist);if(p){lists.delete(p.id);loadList(p,false);}}};
$('shuffle').setAttribute('aria-pressed',String(settings.shuffle));$('repeat').setAttribute('aria-pressed',String(settings.repeat));$('shuffle').onclick=()=>{settings.shuffle=!settings.shuffle;clearUpcoming();preloadNext();save();$('shuffle').setAttribute('aria-pressed',String(settings.shuffle));};$('repeat').onclick=()=>{settings.repeat=!settings.repeat;clearUpcoming();preloadNext();save();$('repeat').setAttribute('aria-pressed',String(settings.repeat));};$('volume').value=String(settings.volume);$('volumeValue').textContent=settings.volume+'%';audio.volume=settings.volume/100;$('volume').oninput=()=>{settings.volume=Number($('volume').value);audio.volume=settings.volume/100;$('volumeValue').textContent=settings.volume+'%';save();};
$('playlistForm').onsubmit=event=>{event.preventDefault();const url=$('playlistUrl').value.trim();try{C.parseYouTube(url);}catch{status(t('invalidLink'),true);return;}if(settings.playlists.length>=50){status(t('limit'),true);return;}const p=settings.playlists.find(p=>p.url===url)||{id:'p'+Date.now().toString(36),name:$('playlistName').value.trim()||'YouTube playlist',autoName:!playlistName.value.trim(),url};if(!settings.playlists.some(x=>x.id===p.id))settings.playlists.push(p);save();$('playlistForm').reset();$('addDetails').open=false;loadList(p,false);};$('language').onclick=()=>{lang=lang==='en'?'pt':'en';settings.language=lang;save();send({type:'language',language:lang});translate();};
$('previewFiles').onchange=()=>{pause();localTracks=Array.from($('previewFiles').files).map(f=>({id:f.name,name:f.name,url:URL.createObjectURL(f),artist:'',cover:''}));index=-1;sourceTo('local');if(localTracks.length)select(0,false);};
window.radio={receive};if(bridge)bridge.addEventListener('message',e=>receive(e.data));if(preview)document.body.classList.add('preview');window.addEventListener('resize',shape);new ResizeObserver(shape).observe($('shell'));sourceTo(prefs().source,false);intent.runtime(preview,true,source,true);translate();expand(false);send({type:'ready'});

if (bridge) {
  for (const [id, type] of [['moveGrip', 'move'], ['resizeGrip', 'resize']]) {
    document.getElementById(id).addEventListener('pointerdown', event => {
      if (event.button !== 0) return;
      event.preventDefault();
      send({type: 'drag', action: type, height: mini.offsetHeight, scale: window.devicePixelRatio});
    });
  }
} else {
  document.getElementById('moveGrip').hidden = true;
  document.getElementById('resizeGrip').hidden = true;
}
})();










