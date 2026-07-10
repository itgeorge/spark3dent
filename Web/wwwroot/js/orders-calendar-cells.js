(function(global){
  'use strict';

  var S3DOrders = global.S3DOrders = global.S3DOrders || {};
  var Format = S3DOrders.Format;

  function orderTeethLabel(o){
    return Format.orderWorkItems(o).map(function(i){
      return +i.toothStart === +i.toothEnd ? String(i.toothStart) : i.toothStart + '-' + i.toothEnd;
    }).join(', ');
  }

  function orderToothCount(o){
    var range = Format.orderTeethRange(o);
    if(range && range.length) return range.length;
    return Format.orderWorkItems(o).length;
  }

  function orderChipLabel(o){
    var teeth = orderTeethLabel(o);
    var prefix = Format.orderMaterialCalendarShort(o) + ' · ' + teeth;
    return prefix.length > 28
      ? orderToothCount(o) + ' teeth · ' + Format.orderMaterialCalendarShort(o) + ' · ' + (o.caseName || '—')
      : prefix + ' · ' + (o.caseName || '—');
  }

  function reservationChipLabel(r){
    return 'Res · ' + orderChipLabel(r);
  }

  function dayToothTotalText(dayOrders){
    var total = dayOrders.reduce(function(sum, o){ return sum + orderToothCount(o); }, 0);
    return total + ' total ' + (total === 1 ? 'tooth' : 'teeth');
  }

  function dayEntryTotalText(entries){
    var total = entries.reduce(function(sum, o){ return sum + orderToothCount(o); }, 0);
    return total + ' total ' + (total === 1 ? 'tooth' : 'teeth');
  }

  function applyClinicAccent(el, o, orderClinics){
    var color = Format.clinicColorForOrder(o, orderClinics);
    if(!color) return;
    el.style.setProperty('--clinic-accent', color);
    el.classList.add('orders-calendar-chip-clinic');
  }

  function clinicColorBarsForOrders(dayOrders, orderClinics){
    var order = [];
    var counts = new Map();
    dayOrders.forEach(function(o, index){
      var code = o && o.clinicCode || o && o.orderCode || o && o.id || '__' + index;
      if(!counts.has(code)){
        order.push(code);
        counts.set(code, { count: 0, color: Format.clinicColorForOrder(o, orderClinics) });
      }
      counts.get(code).count++;
    });
    return order.map(function(code){
      var entry = counts.get(code);
      return { count: entry.count, color: entry.color };
    });
  }

  function appendClinicColorBar(parent, dayOrders, orderClinics, isLab){
    if(!isLab) return;
    var bars = clinicColorBarsForOrders(dayOrders, orderClinics);
    if(!bars.length) return;
    var bar = document.createElement('span');
    bar.className = 'orders-calendar-count-colors';
    bar.setAttribute('aria-hidden', 'true');
    bars.forEach(function(entry){
      var seg = document.createElement('span');
      seg.style.flex = entry.count + ' 1 0';
      if(entry.color) seg.style.setProperty('--clinic-color', entry.color);
      else seg.classList.add('orders-calendar-count-color-neutral');
      bar.appendChild(seg);
    });
    parent.appendChild(bar);
  }

  function normalizeCapacity(capacity){
    if(!capacity) return null;
    var used = Number(capacity.used ?? capacity.Used ?? capacity.dailyUsed ?? capacity.DailyUsed);
    var limit = Number(capacity.limit ?? capacity.Limit ?? capacity.dailyCapacityLimit ?? capacity.DailyCapacityLimit);
    if(!Number.isFinite(used) || !Number.isFinite(limit) || limit <= 0) return null;
    return { used: used, limit: limit };
  }

  function normalizeLoadLevel(level){
    level = String(level || '').toLowerCase();
    return level === 'low' || level === 'medium' || level === 'high' ? level : '';
  }

  function loadLevelLabel(level){
    return level === 'low' ? 'Low' : (level === 'medium' ? 'Medium' : 'High');
  }

  function loadLevelMouthPath(level){
    if(level === 'low') return 'M8 14.2c1.2 1.5 2.6 2.3s2.8-.8 4-2.3';
    if(level === 'medium') return 'M8.5 15h7';
    return 'M8 16.2c1.2-1.5 2.6-2.3 4-2.3s2.8.8 4 2.3';
  }

  function loadLevelSvgHtml(level){
    return '<svg class="orders-calendar-load-icon" viewBox="0 0 24 24" aria-hidden="true">'
      + '<circle cx="12" cy="12" r="8.25" fill="none" stroke="currentColor" stroke-width="2"></circle>'
      + '<circle cx="9" cy="10" r="1.15" fill="currentColor"></circle>'
      + '<circle cx="15" cy="10" r="1.15" fill="currentColor"></circle>'
      + '<path d="' + loadLevelMouthPath(level) + '" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"></path>'
      + '</svg>';
  }

  function capacityLevel(ratio){
    return ratio < 0.4 ? 'low' : (ratio < 0.8 ? 'medium' : 'high');
  }

  function appendCapacityPie(el, ratio){
    var pie = document.createElement('span');
    pie.className = 'orders-calendar-capacity-pie';
    pie.setAttribute('aria-hidden', 'true');
    pie.style.setProperty('--capacity-deg', Math.max(0, Math.min(1, ratio)) * 360 + 'deg');
    el.appendChild(pie);
  }

  function buildCapacityIndicator(capacity){
    var c = normalizeCapacity(capacity);
    if(!c) return null;
    var ratio = c.used / c.limit;
    var level = capacityLevel(ratio);
    var el = document.createElement('span');
    el.className = 'orders-calendar-capacity orders-calendar-capacity-exact orders-calendar-capacity-' + level;
    var usedText = String(Math.round(c.used));
    var limitText = String(Math.round(c.limit));
    var text = document.createElement('span');
    text.className = 'orders-calendar-capacity-text';
    text.textContent = usedText + '/' + limitText;
    el.appendChild(text);
    appendCapacityPie(el, ratio);
    el.title = 'Capacity used: ' + usedText + ' / ' + limitText;
    el.setAttribute('aria-label', 'Capacity used ' + usedText + ' of ' + limitText);
    return el;
  }

  function buildWeeklyCapacityIndicator(capacity){
    var c = normalizeCapacity(capacity);
    if(!c) return null;
    var ratio = c.used / c.limit;
    var level = capacityLevel(ratio);
    var el = document.createElement('span');
    el.className = 'orders-calendar-capacity orders-calendar-capacity-exact orders-calendar-weekly-capacity orders-calendar-capacity-' + level;
    var usedText = String(Math.round(c.used));
    var limitText = String(Math.round(c.limit));
    var text = document.createElement('span');
    text.className = 'orders-calendar-capacity-text';
    var longLabel = document.createElement('span');
    longLabel.className = 'orders-calendar-week-label-long';
    longLabel.textContent = 'week';
    var shortLabel = document.createElement('span');
    shortLabel.className = 'orders-calendar-week-label-short';
    shortLabel.textContent = 'W';
    text.append(longLabel, shortLabel, document.createTextNode(': ' + usedText + '/' + limitText));
    el.appendChild(text);
    appendCapacityPie(el, ratio);
    el.title = 'Weekly capacity used: ' + usedText + ' / ' + limitText;
    el.setAttribute('aria-label', 'Weekly capacity used ' + usedText + ' of ' + limitText);
    return el;
  }

  function buildLoadIndicator(level){
    level = normalizeLoadLevel(level);
    if(!level) return null;
    var el = document.createElement('span');
    el.className = 'orders-calendar-capacity orders-calendar-capacity-' + level + ' orders-calendar-load-level';
    var label = loadLevelLabel(level);
    el.innerHTML = loadLevelSvgHtml(level);
    el.title = label + ' lab load';
    el.setAttribute('aria-label', label + ' lab load');
    return el;
  }

  function appendCapacityIndicator(parent, capacity, isLab){
    if(!isLab) return;
    var indicator = buildCapacityIndicator(capacity);
    if(indicator) parent.appendChild(indicator);
  }

  function buildCalendarCountButton(countValue, entries, onOpen, orderClinics, isLab){
    var count = document.createElement('button');
    count.type = 'button';
    count.className = 'orders-calendar-count';
    count.onclick = function(e){ e.stopPropagation(); onOpen(); };
    var inner = document.createElement('span');
    inner.className = 'orders-calendar-count-inner';
    var label = document.createElement('span');
    label.className = 'orders-calendar-count-label';
    label.textContent = '#' + countValue;
    inner.appendChild(label);
    appendClinicColorBar(inner, entries, orderClinics, isLab);
    count.appendChild(inner);
    return count;
  }

  function buildOrdersCalendarCountButton(dayOrders, onOpen, orderClinics, isLab){
    return buildCalendarCountButton(dayOrders.length, dayOrders, onOpen, orderClinics, isLab);
  }

  function buildCalendarMoreButton(countValue, entries, onOpen, orderClinics, isLab, titleText){
    var more = document.createElement('button');
    more.type = 'button';
    more.className = 'orders-calendar-more';
    more.onclick = function(e){ e.stopPropagation(); onOpen(); };
    more.title = titleText || ('View all ' + countValue + ' entries');
    var inner = document.createElement('span');
    inner.className = 'orders-calendar-more-inner';
    var label = document.createElement('span');
    label.className = 'orders-calendar-more-label';
    label.textContent = 'View all ' + countValue;
    inner.appendChild(label);
    appendClinicColorBar(inner, entries, orderClinics, isLab);
    more.appendChild(inner);
    return more;
  }

  function buildOrdersCalendarMoreButton(dayOrders, onOpen, orderClinics, isLab){
    return buildCalendarMoreButton(dayOrders.length, dayOrders, onOpen, orderClinics, isLab, 'View all ' + dayOrders.length + ' · ' + dayToothTotalText(dayOrders));
  }

  function normalizeDayEntryGroups(entries){
    if(Array.isArray(entries)) return { orders: entries, deliveryReservations: [], impressionReservations: [] };
    entries = entries || {};
    return {
      orders: entries.orders || [],
      deliveryReservations: entries.deliveryReservations || entries.reservations || [],
      impressionReservations: entries.impressionReservations || []
    };
  }

  function buildReservationDeliveryChip(r, options, openDay){
    var clicksEnabled = options.reservationClicksEnabled !== false && options.entryClicksEnabled !== false;
    var chip = document.createElement(clicksEnabled ? 'button' : 'span');
    if(clicksEnabled) chip.type = 'button';
    chip.className = 'orders-calendar-chip orders-calendar-chip-reservation' + (clicksEnabled ? '' : ' orders-calendar-entry-static');
    chip.textContent = reservationChipLabel(r);
    var clinicName = Format.clinicDisplayNameForOrder(r, options.orderClinics || {});
    chip.title = 'Reservation delivery · ' + (r.caseName || '') + ' ' + clinicName;
    if(options.isLab) applyClinicAccent(chip, r, options.orderClinics || {});
    if(clicksEnabled){
      chip.onclick = function(e){
        e.stopPropagation();
        if(options.onOpenReservation) options.onOpenReservation(r.id);
        else openDay();
      };
    }
    return chip;
  }

  function buildOrderChip(o, options){
    var orderClicksEnabled = options.orderClicksEnabled !== false && options.entryClicksEnabled !== false;
    var chip = document.createElement(orderClicksEnabled ? 'button' : 'span');
    if(orderClicksEnabled) chip.type = 'button';
    chip.className = 'orders-calendar-chip' + (orderClicksEnabled ? '' : ' orders-calendar-entry-static');
    chip.textContent = orderChipLabel(o);
    var clinicName = Format.clinicDisplayNameForOrder(o, options.orderClinics || {});
    chip.title = (o.caseName || '') + ' ' + clinicName;
    if(options.isLab) applyClinicAccent(chip, o, options.orderClinics || {});
    if(orderClicksEnabled){
      chip.onclick = function(e){
        e.stopPropagation();
        if(options.onOpenOrder) options.onOpenOrder(o.orderCode);
      };
    }
    return chip;
  }

  function buildImpressionIndicator(impressionReservations, options, openDay){
    if(!impressionReservations.length) return null;
    var clicksEnabled = options.reservationClicksEnabled !== false && options.entryClicksEnabled !== false;
    var wrap = document.createElement('span');
    wrap.className = 'orders-calendar-impression-strip';
    var makeDot = function(r){
      var dot = document.createElement(clicksEnabled ? 'button' : 'span');
      if(clicksEnabled) dot.type = 'button';
      dot.className = 'orders-calendar-impression-dot' + (clicksEnabled ? '' : ' orders-calendar-entry-static');
      dot.title = 'Reservation impression · ' + (r.caseName || Format.orderMaterialShort(r));
      dot.setAttribute('aria-label', dot.title);
      if(options.isLab) applyClinicAccent(dot, r, options.orderClinics || {});
      if(clicksEnabled){
        dot.onclick = function(e){
          e.stopPropagation();
          if(options.onOpenReservation) options.onOpenReservation(r.id);
          else openDay();
        };
      }
      return dot;
    };
    if(impressionReservations.length === 1){
      wrap.appendChild(makeDot(impressionReservations[0]));
      return wrap;
    }
    var count = document.createElement('button');
    count.type = 'button';
    count.className = 'orders-calendar-impression-count';
    count.title = impressionReservations.length + ' reservation impressions';
    count.setAttribute('aria-label', count.title);
    count.onclick = function(e){ e.stopPropagation(); openDay(); };
    count.textContent = '• ' + impressionReservations.length;
    if(options.isLab) appendClinicColorBar(count, impressionReservations, options.orderClinics || {}, true);
    wrap.appendChild(count);
    return wrap;
  }

  function renderDayCalendarEntries(content, entries, options){
    options = options || {};
    if(!content) return false;
    var groups = normalizeDayEntryGroups(entries);
    var dayOrders = groups.orders;
    var deliveryReservations = groups.deliveryReservations;
    var impressionReservations = groups.impressionReservations;
    var totalEntries = dayOrders.length + deliveryReservations.length + impressionReservations.length;
    if(!totalEntries) return false;

    var orderClinics = options.orderClinics || {};
    var isLab = !!options.isLab;
    var onOpenDay = options.onOpenDay || function(){};
    var maxChips = options.maxChips == null ? 3 : options.maxChips;
    var allEntriesForColor = dayOrders.concat(deliveryReservations).concat(impressionReservations);
    var openDay = function(){ onOpenDay(options.iso, groups); };

    appendCapacityIndicator(content, options.capacity, isLab);
    content.appendChild(buildCalendarCountButton(totalEntries, allEntriesForColor, openDay, orderClinics, isLab));

    var chipsRendered = 0;
    dayOrders.forEach(function(o){
      if(chipsRendered >= maxChips) return;
      content.appendChild(buildOrderChip(o, options));
      chipsRendered++;
    });
    deliveryReservations.forEach(function(r){
      if(chipsRendered >= maxChips) return;
      content.appendChild(buildReservationDeliveryChip(r, options, openDay));
      chipsRendered++;
    });

    var impression = buildImpressionIndicator(impressionReservations, options, openDay);
    if(impression) content.appendChild(impression);

    if(dayOrders.length + deliveryReservations.length > maxChips){
      content.appendChild(buildCalendarMoreButton(totalEntries, allEntriesForColor, openDay, orderClinics, isLab, 'View all ' + totalEntries + ' · ' + dayEntryTotalText(dayOrders.concat(deliveryReservations))));
    }
    return true;
  }

  function renderDayOrders(content, dayOrders, options){
    options = options || {};
    if(!content || !dayOrders || !dayOrders.length) return false;
    var orderClinics = options.orderClinics || {};
    var isLab = !!options.isLab;
    var onOpenOrder = options.onOpenOrder;
    var onOpenDay = options.onOpenDay || function(){};
    var maxChips = options.maxChips == null ? 3 : options.maxChips;
    var orderClicksEnabled = options.orderClicksEnabled !== false;
    var openDay = function(){ onOpenDay(options.iso, dayOrders); };

    appendCapacityIndicator(content, options.capacity, isLab);
    content.appendChild(buildOrdersCalendarCountButton(dayOrders, openDay, orderClinics, isLab));
    dayOrders.slice(0, maxChips).forEach(function(o){
      var chip = document.createElement(orderClicksEnabled ? 'button' : 'span');
      if(orderClicksEnabled) chip.type = 'button';
      chip.className = 'orders-calendar-chip' + (orderClicksEnabled ? '' : ' orders-calendar-entry-static');
      chip.textContent = orderChipLabel(o);
      var clinicName = Format.clinicDisplayNameForOrder(o, orderClinics);
      chip.title = (o.caseName || '') + ' ' + clinicName;
      if(isLab) applyClinicAccent(chip, o, orderClinics);
      if(orderClicksEnabled){
        chip.onclick = function(e){
          e.stopPropagation();
          if(onOpenOrder) onOpenOrder(o.orderCode);
        };
      }
      content.appendChild(chip);
    });
    if(dayOrders.length > maxChips){
      content.appendChild(buildOrdersCalendarMoreButton(dayOrders, openDay, orderClinics, isLab));
    }
    return true;
  }

  S3DOrders.CalendarCells = {
    orderChipLabel: orderChipLabel,
    reservationChipLabel: reservationChipLabel,
    orderTeethLabel: orderTeethLabel,
    orderToothCount: orderToothCount,
    dayToothTotalText: dayToothTotalText,
    dayEntryTotalText: dayEntryTotalText,
    normalizeCapacity: normalizeCapacity,
    buildLoadIndicator: buildLoadIndicator,
    buildWeeklyCapacityIndicator: buildWeeklyCapacityIndicator,
    renderDayOrders: renderDayOrders,
    renderDayCalendarEntries: renderDayCalendarEntries
  };
})(typeof window !== 'undefined' ? window : globalThis);
