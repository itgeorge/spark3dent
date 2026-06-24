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

  function dayToothTotalText(dayOrders){
    var total = dayOrders.reduce(function(sum, o){ return sum + orderToothCount(o); }, 0);
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
      var code = o && o.clinicCode || o && o.orderCode || '__' + index;
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
    if(level === 'low') return 'M8 14.2c1.2 1.5 2.6 2.3 4 2.3s2.8-.8 4-2.3';
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

  function buildCapacityIndicator(capacity){
    var c = normalizeCapacity(capacity);
    if(!c) return null;
    var ratio = c.used / c.limit;
    var level = ratio < 0.4 ? 'low' : (ratio < 0.8 ? 'medium' : 'high');
    var el = document.createElement('span');
    el.className = 'orders-calendar-capacity orders-calendar-capacity-' + level;
    var usedText = String(Math.round(c.used));
    var limitText = String(Math.round(c.limit));
    el.textContent = usedText + '/' + limitText;
    el.title = 'Capacity used: ' + usedText + ' / ' + limitText;
    el.setAttribute('aria-label', 'Capacity used ' + usedText + ' of ' + limitText);
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

  function buildOrdersCalendarCountButton(dayOrders, onOpen, orderClinics, isLab){
    var count = document.createElement('button');
    count.type = 'button';
    count.className = 'orders-calendar-count';
    count.onclick = function(e){ e.stopPropagation(); onOpen(); };
    var inner = document.createElement('span');
    inner.className = 'orders-calendar-count-inner';
    var label = document.createElement('span');
    label.className = 'orders-calendar-count-label';
    label.textContent = '#' + dayOrders.length;
    inner.appendChild(label);
    appendClinicColorBar(inner, dayOrders, orderClinics, isLab);
    count.appendChild(inner);
    return count;
  }

  function buildOrdersCalendarMoreButton(dayOrders, onOpen, orderClinics, isLab){
    var more = document.createElement('button');
    more.type = 'button';
    more.className = 'orders-calendar-more';
    more.onclick = function(e){ e.stopPropagation(); onOpen(); };
    more.title = 'View all ' + dayOrders.length + ' · ' + dayToothTotalText(dayOrders);
    var inner = document.createElement('span');
    inner.className = 'orders-calendar-more-inner';
    var label = document.createElement('span');
    label.className = 'orders-calendar-more-label';
    label.textContent = 'View all ' + dayOrders.length;
    inner.appendChild(label);
    appendClinicColorBar(inner, dayOrders, orderClinics, isLab);
    more.appendChild(inner);
    return more;
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
    orderTeethLabel: orderTeethLabel,
    orderToothCount: orderToothCount,
    dayToothTotalText: dayToothTotalText,
    normalizeCapacity: normalizeCapacity,
    buildLoadIndicator: buildLoadIndicator,
    renderDayOrders: renderDayOrders
  };
})(typeof window !== 'undefined' ? window : globalThis);

