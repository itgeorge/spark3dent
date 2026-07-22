(function(){
  'use strict';
  const $=id=>document.getElementById(id);
  function localize(text){return String(text||'').replaceAll('Credentials are required.','Въведете потребителско име и парола.').replaceAll('Invalid organization or PIN.','Невалидна организация или парола.').replaceAll('Invalid organization or password.','Невалидна организация или парола.').replaceAll('Not authenticated.','Не сте влезли в системата.');}
  function returnUrl(){return new URLSearchParams(window.location.search).get('returnUrl')||'';}
  async function api(url,options){
    const response=await fetch(url,{headers:{'content-type':'application/json',...(options&&options.headers||{})},...(options||{})});
    const data=await response.json().catch(()=>({}));
    return {response,data};
  }
  async function resolveTarget(){
    const query=returnUrl()?('?returnUrl='+encodeURIComponent(returnUrl())):'';
    const {response,data}=await api('/api/app-pages/resolve-return-url'+query);
    if(response.ok&&data.path) return data.path;
    return '/orders';
  }
  function showMessage(text){const msg=$('loginMsg');msg.textContent=localize(text||'Login failed.');msg.classList.remove('hidden');}
  async function navigate(){window.location.replace(await resolveTarget());}
  async function bootstrap(){
    try{const {response}=await api('/api/scheduling/auth/me');if(response.ok)await navigate();}catch{}
  }
  async function login(){
    const btn=$('loginBtn');if(btn.disabled)return;btn.disabled=true;btn.textContent='Влизане…';$('loginMsg').classList.add('hidden');
    try{
      const {response,data}=await api('/api/scheduling/auth/login',{method:'POST',body:JSON.stringify({organizationCode:$('organizationCode').value,pin:$('pin').value})});
      if(!response.ok){showMessage(data.error);return;}
      await navigate();
    }catch{showMessage('Login failed.');}
    finally{btn.disabled=false;btn.textContent='Вход';}
  }
  $('loginBtn').addEventListener('click',login);
  ['organizationCode','pin'].forEach(id=>$(id).addEventListener('keydown',e=>{if(e.key==='Enter')login();}));
  bootstrap();
})();
