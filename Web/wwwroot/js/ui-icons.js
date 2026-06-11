(function(){
  function esc(value){
    return window.S3DDom ? S3DDom.esc(value) : String(value == null ? '' : value).replace(/[&<>"]/g,function(ch){return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[ch];});
  }
  function attrs(options){
    options = options || {};
    var className = options.className || options.class || '';
    var label = options.label || '';
    var title = options.title || '';
    var hidden = options.ariaHidden !== false && !label;
    var out = '';
    if(className) out += ' class="' + esc(className) + '"';
    if(label) out += ' aria-label="' + esc(label) + '"';
    else if(hidden) out += ' aria-hidden="true"';
    if(title) out += ' title="' + esc(title) + '"';
    return out;
  }
  var paths = {
    close: '<path d="M6 6l12 12M18 6L6 18" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round"></path>',
    plus: '<path d="M12 5v14M5 12h14" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round"></path>',
    search: '<path d="M10.5 18a7.5 7.5 0 1 1 5.3-2.2L21 21" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"></path>',
    check: '<path d="M6 12l4 4 8-8" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"></path>',
    refresh: '<path d="M20 6v5h-5M4 18v-5h5" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"></path><path d="M18.5 10A7 7 0 0 0 6.1 6.6L4 9m1.5 5A7 7 0 0 0 17.9 17.4L20 15" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"></path>'
  };
  function svgHtml(name, options){
    options = options || {};
    var viewBox = options.viewBox || '0 0 24 24';
    return '<svg' + attrs(options) + ' viewBox="' + esc(viewBox) + '">' + (paths[name] || '') + '</svg>';
  }
  function create(name, options){
    var wrap = document.createElement('span');
    wrap.innerHTML = svgHtml(name, options);
    return wrap.firstElementChild;
  }
  function selectedMarkHtml(options){
    options = options || {};
    var markClass = options.className || 'picker-selected-mark';
    var iconClass = options.iconClassName || 'picker-selected-mark-icon';
    return '<span class="' + esc(markClass) + '" aria-hidden="true">' + svgHtml('check', { className: iconClass }) + '</span>';
  }
  function hydrate(root){
    root = root || document;
    root.querySelectorAll('[data-s3d-icon]').forEach(function(el){
      var name = el.getAttribute('data-s3d-icon');
      var className = el.getAttribute('data-s3d-icon-class') || el.getAttribute('class') || '';
      el.outerHTML = svgHtml(name, { className: className });
    });
  }
  window.S3DIcons = {
    svgHtml: svgHtml,
    create: create,
    hydrate: hydrate,
    closeHtml: function(options){ return svgHtml('close', options); },
    plusHtml: function(options){ return svgHtml('plus', options); },
    searchHtml: function(options){ return svgHtml('search', options); },
    checkHtml: function(options){ return svgHtml('check', options); },
    backCloseHtml: function(options){ return svgHtml('close', options); },
    refreshHtml: function(options){ return svgHtml('refresh', options); },
    selectedMarkHtml: selectedMarkHtml
  };
})();
