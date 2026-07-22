(function(){
  'use strict';

  function loginUrl(){
    return '/login?returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
  }

  function hideDocumentWhileChecking(){
    document.documentElement.style.visibility = 'hidden';
  }

  function showDocument(){
    document.documentElement.style.visibility = '';
  }

  async function revalidateOrRedirect(){
    hideDocumentWhileChecking();
    try{
      const response = await fetch('/api/scheduling/auth/me', {
        headers: { 'content-type': 'application/json' },
        cache: 'no-store'
      });
      if(!response.ok){
        window.location.replace(loginUrl());
        return;
      }
      showDocument();
    }catch{
      window.location.replace(loginUrl());
    }
  }

  window.addEventListener('pageshow', function(event){
    const nav = performance.getEntriesByType ? performance.getEntriesByType('navigation')[0] : null;
    if(event.persisted || (nav && nav.type === 'back_forward')){
      revalidateOrRedirect();
    }
  });
})();
