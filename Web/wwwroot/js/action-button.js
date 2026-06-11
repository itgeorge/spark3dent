(function(global){
  'use strict';

  function uniqueElements(elements){
    var out = [];
    (elements || []).forEach(function(el){
      if(el && out.indexOf(el) < 0) out.push(el);
    });
    return out;
  }

  async function run(button, options){
    options = options || {};
    var disabled = uniqueElements([button].concat(options.disable || []));
    var snapshots = disabled.map(function(el){
      return { element: el, disabled: el.disabled, text: el.textContent };
    });
    disabled.forEach(function(el){ el.disabled = true; });
    if(button && Object.prototype.hasOwnProperty.call(options, 'busyText')) button.textContent = options.busyText;
    try{
      if(typeof options.action === 'function') return await options.action();
    }finally{
      snapshots.forEach(function(s){
        s.element.disabled = s.disabled;
        if(s.element === button && Object.prototype.hasOwnProperty.call(options, 'restoreText')) s.element.textContent = options.restoreText;
        else s.element.textContent = s.text;
      });
    }
  }

  global.S3DActionButton = {
    run: run,
    runWithBusyState: run
  };
})(typeof window !== 'undefined' ? window : globalThis);
