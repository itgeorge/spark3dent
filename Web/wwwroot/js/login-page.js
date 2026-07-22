(function(){
  'use strict';
  const pages={
    '/':{access:'lab',fallback:'/orders'},
    '/orders':{access:'any',fallback:'/orders'},
    '/iam':{access:'lab',fallback:'/orders'},
    '/scheduling-config':{access:'lab',fallback:'/orders'}
  };
  const $=id=>document.getElementById(id);
  function localize(text){return String(text||'').replaceAll('Credentials are required.','Въведете потребителско име и парола.').replaceAll('Invalid organization or PIN.','Невалидна организация или парола.').replaceAll('Invalid organization or password.','Невалидна организация или парола.').replaceAll('Not authenticated.','Не сте влезли в системата.');}
  function defaultPath(actor){return actor&&actor.isLab?'/':'/orders'}
  function normalizeReturnUrl(raw){
    if(!raw)return null;
    try{
      if(/^\/\//.test(raw))return null;
      const url=new URL(raw, window.location.origin);
      if(url.origin!==window.location.origin)return null;
      return {path:url.pathname, value:url.pathname+url.search};
    }catch{return null;}
  }
  function canAccess(page,actor){return !!actor&&(page.access==='any'||(page.access==='lab'&&!!actor.isLab));}
  function resolveTarget(actor){
    const target=normalizeReturnUrl(new URLSearchParams(window.location.search).get('returnUrl'));
    if(!target)return defaultPath(actor);
    const page=pages[target.path];
    if(!page)return defaultPath(actor);
    if(canAccess(page,actor))return target.value;
    const fallback=pages[page.fallback];
    return fallback&&canAccess(fallback,actor)?page.fallback:defaultPath(actor);
  }
  async function api(url,options){
    const response=await fetch(url,{headers:{'content-type':'application/json',...(options&&options.headers||{})},...(options||{})});
    const data=await response.json().catch(()=>({}));
    return {response,data};
  }
  function showMessage(text){const msg=$('loginMsg');msg.textContent=localize(text||'Login failed.');msg.classList.remove('hidden');}
  function navigate(actor){window.location.replace(resolveTarget(actor));}
  async function bootstrap(){
    try{const {response,data}=await api('/api/scheduling/auth/me');if(response.ok)navigate(data);}catch{}
  }
  async function login(){
    const btn=$('loginBtn');if(btn.disabled)return;btn.disabled=true;btn.textContent='Влизане…';$('loginMsg').classList.add('hidden');
    try{
      const {response,data}=await api('/api/scheduling/auth/login',{method:'POST',body:JSON.stringify({organizationCode:$('organizationCode').value,pin:$('pin').value})});
      if(!response.ok){showMessage(data.error);return;}
      navigate(data);
    }catch{showMessage('Login failed.');}
    finally{btn.disabled=false;btn.textContent='Вход';}
  }
  $('loginBtn').addEventListener('click',login);
  ['organizationCode','pin'].forEach(id=>$(id).addEventListener('keydown',e=>{if(e.key==='Enter')login();}));
  bootstrap();
})();
