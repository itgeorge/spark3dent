(function(global){
  'use strict';

  var S3DOrders = global.S3DOrders = global.S3DOrders || {};
  var Dom = global.S3DDom || { esc: function(v){ return String(v == null ? '' : v).replace(/[&<>\"]/g,function(ch){ return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[ch]; }); } };

  function normalize(items){
    return (items || []).filter(function(item){ return !!item; });
  }

  function index(items){
    var map = Object.create(null);
    normalize(items).forEach(function(item){ if(item && item.material) map[item.material] = item; });
    return map;
  }

  function get(items, material){
    return index(items)[material] || null;
  }

  var BG_MATERIAL_LABELS = {
    fullContourZirconia: { title: 'Цирконий', description: 'Full-contour циркониева корона/мост' },
    pfzLayeredZrCrown: { title: 'Цирконий с керамика', description: 'PFZ / керамика, послоена върху ZR' },
    pfm: { title: 'Металокерамика', description: 'PFM корона/мост' },
    glassCeramics: { title: 'Стъклокерамика', description: 'Високоестетичен керамичен случай' },
    pmma: { title: 'Стандартен PMMA', description: 'Временна PMMA корона/мост' },
    pmmaTelio: { title: 'PMMA Telio', description: 'По-здрав временен PMMA с кръстосани връзки' }
  };

  function localizedMaterial(item, material){
    var key = item && item.material || material;
    return BG_MATERIAL_LABELS[key] || null;
  }

  function titleFor(items, material){
    var item = typeof material === 'object' && material ? material : get(items, material);
    var local = localizedMaterial(item, material);
    return local ? local.title : (item ? (item.title || item.material || '') : String(material || ''));
  }

  function descriptionFor(items, material){
    var item = typeof material === 'object' && material ? material : get(items, material);
    var local = localizedMaterial(item, material);
    return local ? local.description : (item ? (item.description || '') : '');
  }

  function visibleItems(items, actor, selectedValue){
    var isLab = !!actor && !!actor.isLab;
    return normalize(items).filter(function(item){
      if(isLab) return true;
      return item.hasAnyConfig || item.material === selectedValue;
    });
  }

  function selectedMarkHtml(){
    return global.S3DIcons ? global.S3DIcons.selectedMarkHtml() : '<span class="picker-selected-mark" aria-hidden="true"><svg class="picker-selected-mark-icon" viewBox="0 0 24 24"><path d="M6 12l4 4 8-8" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"></path></svg></span>';
  }

  function renderPicker(container, options){
    options = options || {};
    if(!container) return;
    var value = typeof options.value === 'function' ? options.value() : options.value;
    var actor = typeof options.actor === 'function' ? options.actor() : options.actor;
    var items = visibleItems(options.items || [], actor, value);
    container.innerHTML = items.map(function(item){
      var active = value && item.material === value;
      var disabled = !!(actor && actor.isLab) && !item.hasAnyConfig;
      var desc = descriptionFor(items, item);
      if(disabled) desc = desc ? desc + ' · Няма настройка за срокове' : 'Няма настройка за срокове';
      return '<button class="choice' + (active ? ' active' : '') + (disabled ? ' disabled' : '') + '" type="button" data-mat="' + Dom.esc(item.material) + '"' + (disabled ? ' disabled aria-disabled="true"' : '') + '>' + selectedMarkHtml() + '<b>' + Dom.esc(titleFor(items, item)) + '</b><span>' + Dom.esc(desc) + '</span></button>';
    }).join('');
    if(options.onChange){
      container.onclick = function(event){
        var button = event.target.closest && event.target.closest('.choice[data-mat]');
        if(!button || !container.contains(button) || button.disabled) return;
        options.onChange(button.dataset.mat, button, event);
      };
    }
  }

  function syncPicker(container, value){
    if(!container) return;
    container.querySelectorAll('.choice[data-mat]').forEach(function(button){
      button.classList.toggle('active', !!value && button.dataset.mat === value);
    });
  }

  S3DOrders.MaterialOptions = {
    normalize: normalize,
    index: index,
    get: get,
    titleFor: titleFor,
    descriptionFor: descriptionFor,
    visibleItems: visibleItems,
    renderPicker: renderPicker,
    syncPicker: syncPicker,
    selectedMarkHtml: selectedMarkHtml
  };
})(typeof window !== 'undefined' ? window : globalThis);
