(function(global){
  'use strict';

  function esc(value){
    return String(value == null ? '' : value).replace(/[&<>"]/g,function(ch){
      return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[ch];
    });
  }

  function clear(element){
    if(element) element.replaceChildren();
  }

  function setHidden(element, hidden, hiddenClass){
    if(!element) return;
    element.classList.toggle(hiddenClass || 'hidden', !!hidden);
  }

  function resolve(value){
    return typeof value === 'function' ? value() : value;
  }

  function deferFocus(elementOrFn, options){
    options = options || {};
    setTimeout(function(){
      var element = resolve(elementOrFn);
      if(!element || !element.focus) return;
      element.focus();
      if(options.select && element.select) element.select();
    }, options.delay == null ? 0 : options.delay);
  }

  global.S3DDom = {
    esc: esc,
    clear: clear,
    setHidden: setHidden,
    deferFocus: deferFocus
  };
})(typeof window !== 'undefined' ? window : globalThis);
