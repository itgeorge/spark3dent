(function(global){
  'use strict';

  var S3DOrders = global.S3DOrders = global.S3DOrders || {};

  function selectedMarkHtml(){
    return global.S3DOrders && global.S3DOrders.MaterialPicker ? global.S3DOrders.MaterialPicker.selectedMarkHtml() : (global.S3DIcons ? global.S3DIcons.selectedMarkHtml() : '');
  }

  function reservationDualFootHtml(isSelectedImpression, isSelectedDelivery){
    return '<div class="reservation-dual-date-markers">' +
      '<span class="reservation-dual-marker reservation-dual-marker-impression' + (isSelectedImpression ? ' active' : '') + '" title="Selected impression date">I</span>' +
      '<span class="reservation-dual-marker reservation-dual-marker-delivery' + (isSelectedDelivery ? ' active' : '') + '" title="Selected delivery date">D</span>' +
      '</div>';
  }

  function appendNudgeButton(row, kind, key, text, nudge){
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'reservation-cell-nudge-btn reservation-cell-nudge-' + key;
    btn.textContent = text;
    var canSearch = !!nudge[key + 'Searchable'];
    btn.disabled = !nudge[key] && !canSearch;
    btn.setAttribute('aria-label', (key === 'previous' ? 'Previous selectable ' : 'Next selectable ') + kind + ' date');
    btn.setAttribute('title', btn.getAttribute('aria-label'));
    btn.addEventListener('click', function(e){
      e.preventDefault();
      e.stopPropagation();
      if(btn.disabled || typeof nudge.onSelect !== 'function') return;
      nudge.onSelect(nudge[key] || '', key);
    });
    row.appendChild(btn);
  }

  function appendNudgeRow(overlay, kind, nudge){
    if(!nudge) return;
    var row = document.createElement('div');
    row.className = 'reservation-cell-nudge-row reservation-cell-nudge-row-' + kind;
    appendNudgeButton(row, kind, 'previous', '←', nudge);
    appendNudgeButton(row, kind, 'next', '→', nudge);
    overlay.appendChild(row);
  }

  function appendNudgeOverlay(cell, options){
    var overlay = document.createElement('div');
    var count = (options.impressionNudge ? 1 : 0) + (options.deliveryNudge ? 1 : 0);
    overlay.className = 'reservation-cell-nudges reservation-cell-nudges-' + count;
    appendNudgeRow(overlay, 'impression', options.impressionNudge);
    appendNudgeRow(overlay, 'delivery', options.deliveryNudge);
    if(overlay.childNodes.length) cell.appendChild(overlay);
  }

  function renderDateCell(ctx, options){
    options = options || {};
    var cell = ctx.cell, date = ctx.date, iso = ctx.iso;
    var status = options.status || { date: iso, isSelectable: false, reason: 'Unavailable' };
    var selectedIso = options.selectedIso || '';
    var impressionIso = options.impressionIso || '';
    var impressionStatus = options.impressionStatus || { date: iso, isSelectable: false, reason: 'Unavailable' };
    var dualReservation = !!options.dualReservation;
    var selectionMode = options.selectionMode === 'impression' ? 'impression' : 'delivery';
    var dayOrders = options.dayOrders || [];
    var deliveryReservations = options.deliveryReservations || [];
    var labOverride = !!(options.isLabOverride && options.isLabOverride(status));
    var isImpression = iso === impressionIso;
    var isDelivery = iso === selectedIso;
    var canSelect = dualReservation && selectionMode === 'impression' ? !!impressionStatus.isSelectable : (!!status.isSelectable || labOverride);
    cell.classList.add('delivery-calendar-cell');
    if(dayOrders.length || deliveryReservations.length) cell.classList.add('delivery-calendar-cell-has-orders');
    if(dualReservation) cell.classList.add('reservation-dual-date-cell');
    cell.replaceChildren();
    var button = document.createElement('button');
    button.className = [
      'delivery-calendar-date',
      dualReservation ? 'reservation-dual-date' : '',
      dualReservation ? 'reservation-dual-mode-' + selectionMode : '',
      ctx.outsideMonth ? 'outside-month' : '',
      ctx.isNonWorkingDay ? 'delivery-calendar-non-working' : '',
      isImpression ? 'impression-day reservation-selected-impression' : '',
      isDelivery ? 'reservation-selected-delivery' : '',
      ctx.isToday ? 'delivery-calendar-today' : '',
      labOverride ? 'delivery-calendar-before-minimum' : ''
    ].filter(Boolean).join(' ');
    button.type = 'button';
    button.dataset.iso = iso;
    button.disabled = !canSelect;

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
    if(S3DOrders.CalendarCells && (dayOrders.length || deliveryReservations.length)){
      if(S3DOrders.CalendarCells.renderDayCalendarEntries){
        S3DOrders.CalendarCells.renderDayCalendarEntries(orders, { orders: dayOrders, deliveryReservations: deliveryReservations }, {
          iso: iso,
          orderClinics: options.orderClinics || {},
          isLab: !!options.isLab,
          capacity: options.capacity,
          orderClicksEnabled: options.orderClicksEnabled !== false,
          reservationClicksEnabled: false,
          onOpenOrder: options.onOpenOrder,
          onOpenDay: options.onOpenDay
        });
      }else{
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
    }

    var foot = document.createElement('div');
    foot.className = 'delivery-calendar-selected-foot';
    if(dualReservation){
      if(isImpression || isDelivery) foot.innerHTML = reservationDualFootHtml(isImpression, isDelivery);
    }else foot.insertAdjacentHTML('beforeend', selectedMarkHtml());

    button.append(main, orders, foot);
    if(!dualReservation && iso === selectedIso) button.classList.add('sel');
    if(dualReservation){
      if(isImpression) button.classList.add('sel-impression');
      if(isDelivery) button.classList.add('sel-delivery');
      if(isImpression && isDelivery) button.classList.add('sel-both');
    }
    if(canSelect){
      var orderClicksEnabled = options.orderClicksEnabled !== false;
      button.onclick = function(e){
        if(e.target.closest('.orders-calendar-more,.orders-calendar-count,.orders-calendar-impression-dot,.orders-calendar-impression-count')) return;
        if(orderClicksEnabled && e.target.closest('.orders-calendar-chip')) return;
        if(dualReservation && selectionMode === 'impression'){
          if(options.onSelectImpression) options.onSelectImpression(iso, impressionStatus);
          return;
        }
        if(options.onSelect) options.onSelect(iso, status);
      };
    }
    if(options.bindReason){
      if(dualReservation && selectionMode === 'impression' && !impressionStatus.isSelectable) options.bindReason(cell, impressionStatus, date);
      else if(isImpression && !dualReservation) options.bindReason(cell, 'impression');
      else if(!status.isSelectable) options.bindReason(cell, status, date);
    }
    cell.appendChild(button);
    if(dualReservation) appendNudgeOverlay(cell, options);
  }

  S3DOrders.DeliveryDatePicker = {
    renderDateCell: renderDateCell
  };
})(typeof window !== 'undefined' ? window : globalThis);
