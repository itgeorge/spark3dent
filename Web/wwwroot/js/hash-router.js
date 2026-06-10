(function(global){
  'use strict';

  function stripHash(hash){
    let value=String(hash||'');
    if(value.startsWith('#'))value=value.slice(1);
    if(value.startsWith('/'))value=value.slice(1);
    return value.replace(/^\/+|\/+$/g,'');
  }

  function currentPath(){
    return stripHash(global.location&&global.location.hash);
  }

  function splitPath(path){
    const raw=stripHash(path);
    const q=raw.indexOf('?');
    return{path:q>=0?raw.slice(0,q):raw,query:q>=0?raw.slice(q+1):''};
  }

  function segments(path){
    const p=splitPath(path).path;
    return p?p.split('/').filter(Boolean):[];
  }

  function compileRoute(route){
    const parts=segments(route.pattern||'');
    return{
      route,
      match(path){
        const actual=segments(path);
        if(actual.length!==parts.length)return null;
        const params={};
        for(let i=0;i<parts.length;i++){
          const expected=parts[i], got=actual[i];
          if(expected.startsWith(':')){
            const key=expected.slice(1);
            try{params[key]=decodeURIComponent(got)}catch{params[key]=got}
          }else if(expected!==got)return null;
        }
        return params;
      }
    };
  }

  function appendHash(path){
    const normalized=stripHash(path);
    const base=(global.location&&global.location.pathname||'/')+(global.location&&global.location.search||'');
    return normalized?`${base}#${normalized}`:base;
  }

  function createContext(path, compiledRoutes, notFound){
    const normalized=stripHash(path);
    const parts=splitPath(normalized);
    for(const compiled of compiledRoutes){
      const params=compiled.match(normalized);
      if(params)return{name:compiled.route.name||compiled.route.pattern||'',path:normalized,params,query:parts.query,route:compiled.route,handler:compiled.route.handler};
    }
    return{name:'notFound',path:normalized,params:{},query:parts.query,route:null,handler:notFound};
  }

  function createHashRouter(options){
    const opts=options||{};
    const compiledRoutes=(opts.routes||[]).map(compileRoute);
    let started=false;
    let currentCtx=null;
    let dispatchSeq=0;
    let handlingExternal=false;

    function setUrl(path, replace){
      const url=appendHash(path);
      if(global.history&&(global.history.pushState||global.history.replaceState)){
        const fn=replace?'replaceState':'pushState';
        global.history[fn]({},'',url);
      }else if(global.location){
        if(replace)global.location.replace(url);else global.location.hash=stripHash(path);
      }
    }

    async function runGuard(from,to,navOptions){
      if(navOptions&&navOptions.skipGuard)return true;
      if(!opts.beforeLeave||!from)return true;
      return (await opts.beforeLeave(from,to,navOptions||{}))!==false;
    }

    async function dispatch(path, navOptions){
      const ctx=createContext(path,compiledRoutes,opts.notFound);
      const seq=++dispatchSeq;
      try{
        if(typeof ctx.handler==='function')await ctx.handler(ctx);
        if(seq===dispatchSeq)currentCtx=ctx;
        return true;
      }catch(err){
        if(typeof opts.onError==='function')opts.onError(err,ctx);else setTimeout(()=>{throw err},0);
        if(seq===dispatchSeq)currentCtx=ctx;
        return false;
      }
    }

    async function handleExternalChange(){
      if(handlingExternal)return;
      handlingExternal=true;
      try{
        const path=currentPath();
        if(currentCtx&&path===currentCtx.path)return;
        const to=createContext(path,compiledRoutes,opts.notFound);
        const allowed=await runGuard(currentCtx,to,{external:true});
        if(!allowed){
          if(currentCtx)setUrl(currentCtx.path,true);
          return;
        }
        await dispatch(path,{external:true});
      }finally{
        handlingExternal=false;
      }
    }

    return{
      start(){
        if(started)return;
        started=true;
        global.addEventListener&&global.addEventListener('hashchange',handleExternalChange);
        global.addEventListener&&global.addEventListener('popstate',handleExternalChange);
        return dispatch(currentPath(),{initial:true,skipGuard:true});
      },
      stop(){
        if(!started)return;
        started=false;
        global.removeEventListener&&global.removeEventListener('hashchange',handleExternalChange);
        global.removeEventListener&&global.removeEventListener('popstate',handleExternalChange);
      },
      async navigate(path,navOptions){
        const options=navOptions||{};
        const normalized=stripHash(path);
        const to=createContext(normalized,compiledRoutes,opts.notFound);
        const allowed=await runGuard(currentCtx,to,options);
        if(!allowed)return false;
        if(!currentCtx||normalized!==currentCtx.path)setUrl(normalized,false);
        return dispatch(normalized,options);
      },
      async replace(path,navOptions){
        const options=navOptions||{};
        const normalized=stripHash(path);
        const to=createContext(normalized,compiledRoutes,opts.notFound);
        const allowed=await runGuard(currentCtx,to,options);
        if(!allowed)return false;
        setUrl(normalized,true);
        return dispatch(normalized,{...options,replace:true});
      },
      refresh(navOptions){
        return dispatch(currentPath(),{...(navOptions||{}),refresh:true,skipGuard:true});
      },
      current(){return currentCtx},
      parse(path){return createContext(path,compiledRoutes,opts.notFound)},
      normalize:stripHash,
      build(pattern,params){
        const values=params||{};
        return segments(pattern).map(part=>part.startsWith(':')?encodeURIComponent(values[part.slice(1)]??''):part).join('/');
      }
    };
  }

  global.HashRouter={createHashRouter};
  if(typeof module!=='undefined'&&module.exports)module.exports=global.HashRouter;
})(typeof window!=='undefined'?window:globalThis);
