(function(global){
  'use strict';

  var S3DOrders = global.S3DOrders = global.S3DOrders || {};
  var Dom = global.S3DDom || { esc: function(v){ return String(v == null ? '' : v).replace(/[&<>"]/g,function(ch){ return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[ch]; }); } };

  function buttonHtml(nums){
    var Teeth = S3DOrders.Teeth;
    return nums.map(function(t){
      var box = Teeth.hitBoxes[t];
      var x = box[0], y = box[1], w = box[2], h = box[3];
      return '<button class="tooth" type="button" data-t="' + t + '" aria-label="Tooth ' + t + '" title="Tooth ' + t + '" style="left:' + (x * 100 / Teeth.FDI_IMAGE_W).toFixed(4) + '%;top:' + (y * 100 / Teeth.FDI_IMAGE_H).toFixed(4) + '%;width:' + (w * 100 / Teeth.FDI_IMAGE_W).toFixed(4) + '%;height:' + (h * 100 / Teeth.FDI_IMAGE_H).toFixed(4) + '%"></button>';
    }).join('');
  }

  function defaultItemTeeth(item){
    var Teeth = S3DOrders.Teeth;
    var start = +(item && item.toothStart), end = +(item && (item.toothEnd || item.toothStart));
    if(!Number.isFinite(start) || !Number.isFinite(end)) return [];
    return Teeth.range(start, end) || [];
  }

  function markerHtml(item, options){
    var Teeth = S3DOrders.Teeth;
    options = options || {};
    var teeth = (options.getItemTeeth || defaultItemTeeth)(item);
    var bounds = Teeth.cropBounds(teeth, 0);
    if(!bounds) return '';
    var left = bounds.x / Teeth.FDI_IMAGE_W * 100;
    var top = bounds.y / Teeth.FDI_IMAGE_H * 100;
    var width = bounds.w / Teeth.FDI_IMAGE_W * 100;
    var height = bounds.h / Teeth.FDI_IMAGE_H * 100;
    var title = options.getItemLabel ? options.getItemLabel(item) : '';
    return '<span class="tooth active" aria-hidden="true" title="' + Dom.esc(title) + '" style="left:' + left.toFixed(4) + '%;top:' + top.toFixed(4) + '%;width:' + width.toFixed(4) + '%;height:' + height.toFixed(4) + '%"></span>';
  }

  function render(container, options){
    var Teeth = S3DOrders.Teeth;
    options = options || {};
    if(!container) return;
    var label = options.label || 'FDI teeth numbering chart';
    container.innerHTML = '<div class="fdi-map" role="img" aria-label="' + Dom.esc(label) + '"><div class="fdi-map-markers" aria-hidden="true"></div><div class="fdi-map-buttons">' + buttonHtml(Teeth.upper) + buttonHtml(Teeth.lower) + '</div></div>';
    if(options.onPickTooth){
      container.onclick = function(event){
        var button = event.target.closest && event.target.closest('.tooth[data-t]');
        if(!button || !container.contains(button)) return;
        options.onPickTooth(+button.dataset.t, button, event);
      };
    }
    syncHighlight(container, options);
  }

  function syncHighlight(container, options){
    var Teeth = S3DOrders.Teeth;
    options = options || {};
    if(!container) return;
    var markersEl = container.querySelector('.fdi-map-markers');
    var lockedItems = options.lockedItems || (options.getLockedItems && options.getLockedItems()) || [];
    if(markersEl) markersEl.innerHTML = lockedItems.map(function(item){ return markerHtml(item, options); }).join('');
    var range = options.range || (options.getActiveRange && options.getActiveRange()) || [];
    var inRange = range && range.length ? new Set(range.map(Number)) : new Set();
    container.querySelectorAll('.fdi-map-buttons .tooth').forEach(function(button){
      button.classList.toggle('active', inRange.has(+button.dataset.t));
    });
  }

  function create(container, options){
    options = options || {};
    return {
      render: function(nextOptions){ options = Object.assign({}, options, nextOptions || {}); render(container, options); },
      syncHighlight: function(nextOptions){ options = Object.assign({}, options, nextOptions || {}); syncHighlight(container, options); },
      container: container
    };
  }

  S3DOrders.ToothChart = {
    create: create,
    render: render,
    syncHighlight: syncHighlight,
    buttonHtml: buttonHtml,
    markerHtml: markerHtml
  };
})(typeof window !== 'undefined' ? window : globalThis);
