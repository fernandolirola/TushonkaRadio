(function(root,factory){if(typeof module==='object'&&module.exports)module.exports=factory();else root.RadioCore=factory();})(typeof globalThis!=='undefined'?globalThis:this,function(){
'use strict';
const preset={id:'starter',name:'100 Best FIFA Songs',url:'https://www.youtube.com/watch?v=EfqsJm6BKng&list=PLhFqlbCKo3yp2UL7pHPJ4DAJOCUxHyT8V&index=1'};
function parseYouTube(value){
 let u;try{u=new URL(String(value).trim());}catch{throw new Error('invalidLink');}
 if(u.protocol!=='https:'||u.username||u.password||u.port)throw new Error('invalidLink');
 const hosts=['youtube.com','www.youtube.com','m.youtube.com','music.youtube.com','youtu.be','www.youtube-nocookie.com'];
 if(!hosts.includes(u.hostname))throw new Error('invalidLink');
 const list=u.searchParams.get('list')||'';
 let video=u.searchParams.get('v')||'';
 if(u.hostname==='youtu.be')video=u.pathname.slice(1).split('/')[0];
 else if(/^\/(embed|shorts)\//.test(u.pathname))video=u.pathname.split('/')[2]||'';
 if(list&&!/^[A-Za-z0-9_-]{2,128}$/.test(list))throw new Error('invalidLink');
 if(video&&!/^[A-Za-z0-9_-]{11}$/.test(video))throw new Error('invalidLink');
 if(!list&&!video)throw new Error('invalidLink');
 return {list,video};
}
function safeTrack(track){return !!track&&typeof track.name==='string'&&typeof track.url==='string'&&/^Music\/[^/]+$/.test(track.url)&&!/[\\?#]/.test(track.url)&&!/%2f|%5c/i.test(track.url)&&!['.','..'].includes(decodeSafe(track.url.slice(6)));}
function decodeSafe(x){try{return decodeURIComponent(x);}catch{return '..';}}
function defaults(){return {version:1,language:'en',volume:35,shuffle:false,repeat:true,lastPlayedPlaylist:'',playlists:[{...preset}],contexts:{menu:{source:'youtube',playlist:'starter',track:''},hideout:{source:'youtube',playlist:'starter',track:''}}};}
function restore(text){let s;try{s=JSON.parse(text);}catch{return defaults();}if(!s||typeof s!=='object')return defaults();const d=defaults();
 d.language=s.language==='pt'?'pt':'en';d.volume=Number.isFinite(s.volume)?Math.max(0,Math.min(100,s.volume)):35;d.shuffle=s.shuffle===true;d.repeat=s.repeat!==false;
 if(Array.isArray(s.playlists)){d.playlists=s.playlists.slice(0,50).filter(x=>{try{return x&&/^[A-Za-z0-9_-]{1,64}$/.test(x.id)&&typeof x.name==='string'&&typeof x.url==='string'&&!!parseYouTube(x.url);}catch{return false;}}).map(x=>({id:x.id,name:x.name.slice(0,80),autoName:x.autoName===true,url:x.url.slice(0,512)}));}
 d.lastPlayedPlaylist=d.playlists.some(p=>p.id===s.lastPlayedPlaylist)?s.lastPlayedPlaylist:'';
 for(const mode of ['menu','hideout']){const c=s.contexts&&s.contexts[mode];if(c){d.contexts[mode]={source:c.source==='local'?'local':'youtube',playlist:d.playlists.some(p=>p.id===c.playlist)?c.playlist:d.playlists[0]?.id||'',track:typeof c.track==='string'&&safeTrack({name:'',url:c.track})?c.track:''};}}
 return d;
}
function nextIndex(index,count,shuffle,repeat,random){if(count<1)return -1;if(shuffle&&count>1){let n=Math.floor((random||Math.random)()*(count-1));return n>=index?n+1:n;}return index+1<count?index+1:repeat?0:-1;}
class Intent{
 constructor(){this.desired=false;this.suspended=false;this.allowed=false;this.visible=false;this.source='youtube';}
 runtime(allowed,visible,source,resume){this.allowed=allowed;this.visible=visible;this.source=source;
 if(!allowed){this.suspended=true;return 'pause';}
 if(this.suspended){this.suspended=false;if(!resume)this.desired=false;}
 return this.desired?'play':'pause';}
 canPlay(){return this.allowed;}
 userPlay(){this.desired=this.canPlay();return this.desired;}
 userPause(){this.desired=false;}
}
return {preset,parseYouTube,safeTrack,defaults,restore,nextIndex,Intent};
});


