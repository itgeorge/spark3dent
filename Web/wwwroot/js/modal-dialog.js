(function(){
  var stack = [];
  function resolve(value){ return typeof value === 'function' ? value() : value; }
  function isHidden(overlay, hiddenClass, openClass){
    if(!overlay) return true;
    if(openClass) return !overlay.classList.contains(openClass);
    return overlay.classList.contains(hiddenClass || 'hidden');
  }
  function setOpen(overlay, open, hiddenClass, openClass){
    if(!overlay) return;
    if(openClass){
      overlay.classList.toggle(openClass, open);
      overlay.setAttribute('aria-hidden', open ? 'false' : 'true');
    }else{
      overlay.classList.toggle(hiddenClass || 'hidden', !open);
    }
  }
  function pruneStack(){
    stack = stack.filter(function(modal){ return modal && modal.isOpen && modal.isOpen(); });
  }
  function moveToTop(modal){
    var idx = stack.indexOf(modal);
    if(idx >= 0) stack.splice(idx, 1);
    stack.push(modal);
  }
  function bind(options){
    options = options || {};
    var overlay = resolve(options.overlay);
    var hiddenClass = options.hiddenClass || 'hidden';
    var openClass = options.openClass || '';
    var closeOnOverlay = options.closeOnOverlay !== false;
    var closeOnEscape = options.closeOnEscape !== false;
    var closeWhenBusy = options.closeWhenBusy || function(){ return false; };
    var api = {
      overlay: overlay,
      isOpen: function(){ return !isHidden(overlay, hiddenClass, openClass); },
      open: function(){
        overlay = resolve(options.overlay);
        if(!overlay) return;
        setOpen(overlay, true, hiddenClass, openClass);
        pruneStack();
        moveToTop(api);
        if(options.onOpen) options.onOpen(api);
        var target = resolve(options.initialFocus);
        if(target && target.focus){
          if(window.S3DDom) S3DDom.deferFocus(target, { select: !!options.selectInitialFocus });
          else setTimeout(function(){ target.focus(); if(options.selectInitialFocus && target.select) target.select(); }, 0);
        }
      },
      close: function(reason){
        overlay = resolve(options.overlay);
        if(!overlay || closeWhenBusy(reason)) return;
        setOpen(overlay, false, hiddenClass, openClass);
        var idx = stack.indexOf(api);
        if(idx >= 0) stack.splice(idx, 1);
        pruneStack();
        if(options.onClose) options.onClose(reason, api);
      }
    };
    if(overlay && closeOnOverlay){
      overlay.addEventListener('click', function(event){
        if(event.target === overlay) api.close('overlay');
      });
    }
    if(closeOnEscape){
      document.addEventListener('keydown', function(event){
        if(event.key !== 'Escape' || !api.isOpen()) return;
        pruneStack();
        var top = stack[stack.length - 1];
        if(top && top !== api) return;
        event.preventDefault();
        event.stopPropagation();
        if(event.stopImmediatePropagation) event.stopImmediatePropagation();
        api.close('escape');
      });
    }
    return api;
  }
  function confirm(options){
    var modal = bind(options);
    return modal;
  }
  window.S3DModal = { bind: bind, createModal: bind, confirm: confirm };
})();
