(function(global){
  'use strict';

  var S3DOrders = global.S3DOrders = global.S3DOrders || {};

  function selectedMarkHtml(){
    return global.S3DOrders && global.S3DOrders.MaterialPicker ? global.S3DOrders.MaterialPicker.selectedMarkHtml() : (global.S3DIcons ? global.S3DIcons.selectedMarkHtml() : '');
  }

  function renderDateCell(ctx, options){
    options = options || {};
    var cell = ctx.cell, date = ctx.date, iso = ctx.iso;
    var status = options.status || { date: iso, isSelectable: false, reason: 'Unavailable' };
    var selectedIso = options.selectedIso || '';
    var impressionIso = options.impressionIso || '';
    var labOverride = !!(options.isLabOverride && options.isLabOverride(status));
    var isImpression = iso === impressionIso;
    cell.classList.add('delivery-calendar-cell');
    cell.replaceChildren();
    var button = document.createElement('button');
    button.className = [
      'delivery-calendar-date',
      ctx.outsideMonth ? 'outside-month' : '',
      ctx.isWeekend ? 'delivery-calendar-weekend' : '',
      isImpression ? 'impression-day' : '',
      ctx.isToday ? 'delivery-calendar-today' : '',
      labOverride ? 'delivery-calendar-before-minimum' : ''
    ].filter(Boolean).join(' ');
    button.type = 'button';
    button.dataset.iso = iso;
    button.disabled = !status.isSelectable && !labOverride;

    var main = document.createElement('div');
    main.className = 'delivery-calendar-date-main';
    var num = document.createElement('span');
    num.className = 'delivery-calendar-date-num';
    num.textContent = String(date.getDate());
    var weekday = document.createElement('span');
    weekday.className = 'delivery-calendar-date-weekday';
    weekday.textContent = (ctx.weekdayLabel || '').slice(0, 3);
    main.append(num, weekday);
    button.append(main);
    button.insertAdjacentHTML('beforeend', selectedMarkHtml());
    if(iso === selectedIso) button.classList.add('sel');
    if(status.isSelectable || labOverride){
      button.onclick = function(){ if(options.onSelect) options.onSelect(iso, status); };
    }
    if(options.bindReason){
      if(isImpression) options.bindReason(cell, 'impression');
      else if(!status.isSelectable) options.bindReason(cell, status, date);
    }
    cell.appendChild(button);
  }

  S3DOrders.DeliveryDatePicker = {
    renderDateCell: renderDateCell
  };
})(typeof window !== 'undefined' ? window : globalThis);
