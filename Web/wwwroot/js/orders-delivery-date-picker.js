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
    var dayOrders = options.dayOrders || [];
    var labOverride = !!(options.isLabOverride && options.isLabOverride(status));
    var isImpression = iso === impressionIso;
    cell.classList.add('delivery-calendar-cell');
    if(dayOrders.length) cell.classList.add('delivery-calendar-cell-has-orders');
    cell.replaceChildren();
    var button = document.createElement('button');
    button.className = [
      'delivery-calendar-date',
      ctx.outsideMonth ? 'outside-month' : '',
      ctx.isNonWorkingDay ? 'delivery-calendar-non-working' : '',
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

    var orders = document.createElement('div');
    orders.className = 'delivery-calendar-orders';
    if(S3DOrders.CalendarCells && options.isLab && options.weeklyCapacity && S3DOrders.CalendarCells.buildWeeklyCapacityIndicator){
      var weekly = S3DOrders.CalendarCells.buildWeeklyCapacityIndicator(options.weeklyCapacity);
      if(weekly) orders.appendChild(weekly);
    }
    if(S3DOrders.CalendarCells && !options.isLab && options.capacityLoadLevel){
      var load = S3DOrders.CalendarCells.buildLoadIndicator(options.capacityLoadLevel);
      if(load) orders.appendChild(load);
    }
    if(S3DOrders.CalendarCells && dayOrders.length){
      S3DOrders.CalendarCells.renderDayOrders(orders, dayOrders, {
        iso: iso,
        orderClinics: options.orderClinics || {},
        isLab: !!options.isLab,
        capacity: options.capacity,
        orderClicksEnabled: options.orderClicksEnabled !== false,
        onOpenOrder: options.onOpenOrder,
        onOpenDay: options.onOpenDay
      });
    }

    var foot = document.createElement('div');
    foot.className = 'delivery-calendar-selected-foot';
    foot.insertAdjacentHTML('beforeend', selectedMarkHtml());

    button.append(main, orders, foot);
    if(iso === selectedIso) button.classList.add('sel');
    if(status.isSelectable || labOverride){
      var orderClicksEnabled = options.orderClicksEnabled !== false;
      button.onclick = function(e){
        if(e.target.closest('.orders-calendar-more,.orders-calendar-count')) return;
        if(orderClicksEnabled && e.target.closest('.orders-calendar-chip')) return;
        if(options.onSelect) options.onSelect(iso, status);
      };
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
