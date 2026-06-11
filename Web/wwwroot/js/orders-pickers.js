(function(global){
  'use strict';

  var S3DOrders = global.S3DOrders = global.S3DOrders || {};
  var Dom = global.S3DDom || { esc: function(v){ return String(v == null ? '' : v).replace(/[&<>"]/g,function(ch){ return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[ch]; }); } };

  var MATERIALS = [
    { value:'fullContourZirconia', title:'Zirconia', description:'Full contour zirconia crown/bridge' },
    { value:'pfzLayeredZrCrown', title:'Layered zirconia', description:'PFZ / ceramic layered on ZR' },
    { value:'pfm', title:'Metal-ceramic', description:'PFM crown/bridge' },
    { value:'glassCeramics', title:'Glass ceramics', description:'High-esthetic ceramic case' },
    { value:'pmma', title:'Temporary PMMA', description:'Temporary crown/bridge' }
  ];

  function selectedMarkHtml(){
    return global.S3DIcons ? global.S3DIcons.selectedMarkHtml() : '<span class="picker-selected-mark" aria-hidden="true"><svg class="picker-selected-mark-icon" viewBox="0 0 24 24"><path d="M6 12l4 4 8-8" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"></path></svg></span>';
  }

  function renderMaterial(container, options){
    options = options || {};
    if(!container) return;
    var value = typeof options.value === 'function' ? options.value() : options.value;
    var materials = options.materials || MATERIALS;
    container.innerHTML = materials.map(function(material){
      var active = value && material.value === value;
      return '<button class="choice' + (active ? ' active' : '') + '" type="button" data-mat="' + Dom.esc(material.value) + '">' + selectedMarkHtml() + '<b>' + Dom.esc(material.title) + '</b><span>' + Dom.esc(material.description) + '</span></button>';
    }).join('');
    if(options.onChange){
      container.onclick = function(event){
        var button = event.target.closest && event.target.closest('.choice[data-mat]');
        if(button && container.contains(button)) options.onChange(button.dataset.mat, button, event);
      };
    }
  }

  function syncMaterial(container, value){
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
    materials: MATERIALS,
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
