(function(global){
  'use strict';

  var S3DOrders = global.S3DOrders = global.S3DOrders || {};
  var Dom = global.S3DDom || { esc: function(v){ return String(v == null ? '' : v).replace(/[&<>"]/g,function(ch){ return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[ch]; }); } };

  function itemTeeth(item){
    var Teeth = S3DOrders.Teeth;
    var start = +(item && item.toothStart), end = +(item && (item.toothEnd || item.toothStart));
    if(!Number.isFinite(start) || !Number.isFinite(end)) return [];
    return Teeth.range(start, end) || [];
  }

  function defaultItemLabel(item){
    var construction = item && (item.constructionType || item.construction || 'case');
    var labels = { crown:'Корона', bridge:'Мост', inlayOverlay:'Инлей/Онлей' };
    var label = labels[construction] || 'Конструкция';
    if(!item || !item.toothStart) return label + ' —';
    return +item.toothStart === +(item.toothEnd || item.toothStart) ? label + ' ' + item.toothStart : label + ' ' + item.toothStart + '-' + item.toothEnd;
  }

  function markerHtml(item, crop, getItemLabel){
    var Teeth = S3DOrders.Teeth;
    var teeth = itemTeeth(item);
    var itemBounds = Teeth.cropBounds(teeth, 0);
    if(!itemBounds) return '';
    var klass = item && item.locked ? 'locked' : 'active';
    var left = (itemBounds.x - crop.x) / crop.w * 100;
    var top = (itemBounds.y - crop.y) / crop.h * 100;
    var width = itemBounds.w / crop.w * 100;
    var height = itemBounds.h / crop.h * 100;
    var title = (getItemLabel || defaultItemLabel)(item);
    return '<span class="tooth ' + klass + '" aria-hidden="true" title="' + Dom.esc(title) + '" style="left:' + left.toFixed(4) + '%;top:' + top.toFixed(4) + '%;width:' + width.toFixed(4) + '%;height:' + height.toFixed(4) + '%"></span>';
  }

  function render(container, options){
    var Teeth = S3DOrders.Teeth;
    options = options || {};
    if(!container) return;
    var range = options.teeth || [];
    var label = range.length ? (options.labelPrefix || 'Избрани зъби') + ': ' + range.join(', ') : 'Няма избрани зъби';
    if(!range.length){
      container.innerHTML = '';
      container.setAttribute('aria-hidden', 'true');
      return;
    }
    var crop = Teeth.cropBounds(range);
    if(!crop){
      container.innerHTML = '';
      container.setAttribute('aria-hidden', 'true');
      return;
    }
    var x = crop.x, y = crop.y, w = crop.w, h = crop.h;
    var scaleW = Teeth.FDI_IMAGE_W / w * 100;
    var scaleH = Teeth.FDI_IMAGE_H / h * 100;
    var offsetX = x / w * 100;
    var offsetY = y / h * 100;
    var maxPreviewW = w / h * 300;
    var previewItems = options.items && options.items.length ? options.items : [{ toothStart: range[0], toothEnd: range[range.length - 1], constructionType: 'crown' }];
    var markers = previewItems.map(function(item){ return markerHtml(item, crop, options.getItemLabel); }).join('');
    container.className = 'selected-teeth-preview-wrap';
    container.innerHTML = '<div class="selected-teeth-preview" role="img" aria-label="' + Dom.esc(label) + '" style="aspect-ratio:' + w + '/' + h + ';--preview-max-w:' + maxPreviewW.toFixed(2) + 'px"><div class="selected-teeth-preview-surface" style="left:-' + offsetX.toFixed(4) + '%;top:-' + offsetY.toFixed(4) + '%;width:' + scaleW.toFixed(4) + '%;height:' + scaleH.toFixed(4) + '%"></div><div class="selected-teeth-preview-markers">' + markers + '</div></div>';
    container.removeAttribute('aria-hidden');
  }

  S3DOrders.SelectedTeethPreview = {
    render: render,
    markerHtml: markerHtml,
    itemTeeth: itemTeeth
  };
})(typeof window !== 'undefined' ? window : globalThis);
