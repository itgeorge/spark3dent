(function(global){
  'use strict';

  function createDirtyGuard(options){
    options = options || {};
    return {
      async beforeLeave(from, to, navOptions){
        navOptions = navOptions || {};
        if(navOptions.skipDirtyGuard || navOptions.skipGuard) return true;
        if(!options.isDirty || !options.isDirty(from, to, navOptions)) return true;
        if(options.isSafeTransition && options.isSafeTransition(from, to, navOptions)) return true;
        if(options.showPrompt) await options.showPrompt(to, from, navOptions);
        return false;
      }
    };
  }

  global.S3DDirtyNavigation = {
    createGuard: createDirtyGuard,
    createDirtyGuard: createDirtyGuard
  };
})(typeof window !== 'undefined' ? window : globalThis);
