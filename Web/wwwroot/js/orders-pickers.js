(function(global){
  'use strict';

  var S3DOrders = global.S3DOrders = global.S3DOrders || {};
  var Dom = global.S3DDom || { esc: function(v){ return String(v == null ? '' : v).replace(/[&<>\"]/g,function(ch){ return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[ch]; }); } };
  var MaterialOptions = S3DOrders.MaterialOptions || null;

  function selectedMarkHtml(){
    return MaterialOptions && MaterialOptions.selectedMarkHtml
      ? MaterialOptions.selectedMarkHtml()
      : (global.S3DIcons ? global.S3DIcons.selectedMarkHtml() : '<span class="picker-selected-mark" aria-hidden="true"><svg class="picker-selected-mark-icon" viewBox="0 0 24 24"><path d="M6 12l4 4 8-8" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"></path></svg></span>');
  }

  function renderMaterial(container, options){
    options = options || {};
    if(MaterialOptions && MaterialOptions.renderPicker){
      MaterialOptions.renderPicker(container, {
        items: options.materials || [],
        actor: options.actor,
        value: options.value,
        onChange: options.onChange
      });
      return;
    }
    if(container) container.innerHTML = '';
  }

  function syncMaterial(container, value){
    if(MaterialOptions && MaterialOptions.syncPicker){
      MaterialOptions.syncPicker(container, value);
      return;
    }
    if(!container) return;
    container.querySelectorAll('.choice[data-mat]').forEach(function(button){
      button.classList.toggle('active', !!value && button.dataset.mat === value);
    });
  }

  function shadeCardHtml(code, selected, label){
    label = label || code;
    var active = code === selected;
    return '<button type="button" class="shade-card' + (active ? ' active' : '') + '" data-shade="' + Dom.esc(code) + '" aria-label="Shade ' + Dom.esc(label) + '" aria-pressed="' + active + '"><span class="shade-card-label">' + Dom.esc(label) + '</span>' + selectedMarkHtml() + '</button>';
  }

  function renderShade(container, options){
    options = options || {};
    if(!container) return;
    var value = typeof options.value === 'function' ? options.value() : options.value;
    var groups = options.groups || [];
    var unspecified = options.unspecifiedValue || 'unspecified';
    container.innerHTML = groups.map(function(group){
      return '<div class="shade-group">' + (group.shades || []).map(function(code){ return shadeCardHtml(code, value); }).join('') + '</div>';
    }).join('') + '<div class="shade-group">' + shadeCardHtml(unspecified, value, 'Unspecified') + '</div>';
    if(options.onChange){
      container.onclick = function(event){
        var button = event.target.closest && event.target.closest('.shade-card[data-shade]');
        if(button && container.contains(button)) options.onChange(button.dataset.shade, button, event);
      };
    }
  }

  function syncShade(container, value){
    if(!container) return;
    container.querySelectorAll('.shade-card[data-shade]').forEach(function(button){
      var active = button.dataset.shade === value;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', active);
    });
  }

  S3DOrders.MaterialPicker = {
    materials: [],
    render: renderMaterial,
    sync: syncMaterial,
    selectedMarkHtml: selectedMarkHtml
  };
  S3DOrders.ShadePicker = {
    render: renderShade,
    sync: syncShade,
    cardHtml: shadeCardHtml
  };
})(typeof window !== 'undefined' ? window : globalThis);
