(function(global){
  'use strict';
  var S3DOrders = global.S3DOrders = global.S3DOrders || {};

  function create(options){
    options = options || {};
    const $ = id => document.getElementById(id);
    const ordersApi = options.api;
    const getActor = options.getActor || (() => null);
    const loadClinics = options.loadClinics || (async () => {});
    const openUiModal = options.openUiModal || ((name, overlay, focusTarget) => { S3DDom.setHidden(overlay, false); if(focusTarget)S3DDom.deferFocus(focusTarget); });
    const closeUiModal = options.closeUiModal || ((name, overlay) => S3DDom.setHidden(overlay, true));
    const onOpenOrder = options.onOpenOrder || (() => {});
    const onNewOrder = options.onNewOrder || (() => {});
    const onShowShell = options.onShowShell || (() => {});
    const onCloseFlowModals = options.onCloseFlowModals || (() => {});
    const onCloseCancelOrder = options.onCloseCancelOrder || (() => {});
    const consumeRouteError = options.consumeRouteError || (() => '');
    const Format = S3DOrders.Format;
    const CalendarCells = S3DOrders.CalendarCells;
    const ORDERS_VIEW_MODE_KEY = 's3d.orders.viewMode';
    // Deployment 2: set to false and re-enable creation in Web/SchedulingOrderCreationGate.cs.
    // Also remove temporary test ignores: rg "Deployment 1: order creation temporarily disabled"
    const ORDER_CREATION_TEMPORARILY_DISABLED = true;

    const app = $('app'), list = $('list'), reviewCard = $('reviewCard'), newOrderBtn = $('newOrderBtn');
    const ordersMsg = $('ordersMsg'), ordersBody = $('ordersBody'), loadMoreOrdersBtn = $('loadMoreOrdersBtn');
    const reloadOrdersBtn = $('reloadOrdersBtn'), openFindOrderBtn = $('openFindOrderBtn'), orderFindBtn = $('orderFindBtn'), orderFindCancelBtn = $('orderFindCancelBtn');
    const findOrderPopup = $('findOrderPopup'), findOrderMsg = $('findOrderMsg'), orderFindInput = $('orderFindInput');
    const ordersListModeBtn = $('ordersListModeBtn'), ordersCalendarModeBtn = $('ordersCalendarModeBtn'), ordersListWrap = $('ordersListWrap'), ordersCalendarWrap = $('ordersCalendarWrap'), ordersCalendar = $('ordersCalendar');
    const ordersDayPopup = $('ordersDayPopup'), ordersDayPopupCloseBtn = $('ordersDayPopupCloseBtn'), ordersDayPopupTitle = $('ordersDayPopupTitle'), ordersDayPopupSub = $('ordersDayPopupSub'), ordersDayPopupList = $('ordersDayPopupList');

    let orders = [], orderClinics = {}, listHighlightCode = null;
    let ordersNextCursor = null, ordersHasMore = false, ordersLoadingPage = false, ordersFinding = false;
    let ordersViewMode = 'list', ordersCalendarMonth = new Date(new Date().getFullYear(), new Date().getMonth(), 1), ordersCalendarRequest = 0, ordersCalendarInstance = null, ordersCalendarByDate = new Map(), ordersCalendarCapacityByDate = new Map(), ordersCalendarWeeklyCapacityByDate = new Map(), ordersCalendarHighlightDate = null, ordersCalendarHighlightTimer = null;
    let pendingFindListHighlightCode = null, pendingFindCalendarHighlightDate = null;

    function actor(){return getActor()}
    function esc(v){return S3DDom.esc(v)}
    function mergeOrderClinics(map){if(!map||typeof map!=='object')return;orderClinics={...orderClinics,...map}}
    function applyClinicAccent(el,o){const color=Format.clinicColorForOrder(o,orderClinics);if(!color)return;el.style.setProperty('--clinic-accent',color);el.classList.add('orders-calendar-chip-clinic')}
    function localizeFindText(text){return String(text||'').replaceAll('Order not found.','Поръчката не е намерена.').replaceAll('Multiple orders match this code; enter the full order code.','Няколко поръчки съвпадат с този код; въведете пълния код на поръчката.').replaceAll('Cancelled orders are only visible in list view.','Отказаните поръчки се виждат само в изглед списък.').replaceAll('Order code is required.','Необходим е код на поръчка.').replaceAll('Not authenticated.','Не сте влезли в системата.')}

    function defaultOrdersViewMode(){return actor()?.isLab?'calendar':'list'}
    function loadOrdersViewMode(){try{const saved=localStorage.getItem(ORDERS_VIEW_MODE_KEY);if(saved==='calendar'||saved==='list')return saved}catch{}return defaultOrdersViewMode()}
    function saveOrdersViewMode(mode){try{localStorage.setItem(ORDERS_VIEW_MODE_KEY,mode)}catch{}}
    function renderOrdersViewModeShell(){const calendar=ordersViewMode==='calendar';ordersListWrap.classList.toggle('hidden',calendar);ordersCalendarWrap.classList.toggle('hidden',!calendar);ordersListModeBtn.classList.toggle('active',!calendar);ordersCalendarModeBtn.classList.toggle('active',calendar);ordersListModeBtn.setAttribute('aria-pressed',calendar?'false':'true');ordersCalendarModeBtn.setAttribute('aria-pressed',calendar?'true':'false');syncLoadMoreButton()}
    async function setOrdersViewMode(mode){if(mode!==ordersViewMode){ordersViewMode=mode;saveOrdersViewMode(mode)}renderOrdersViewModeShell();await reload()}
    async function reload(){return ordersViewMode==='calendar'?loadOrdersCalendar():loadOrders(true)}
    function syncLoadMoreButton(){if(!loadMoreOrdersBtn)return;loadMoreOrdersBtn.classList.toggle('hidden',ordersViewMode!=='list'||!ordersHasMore);loadMoreOrdersBtn.disabled=ordersLoadingPage||!ordersHasMore;loadMoreOrdersBtn.textContent=ordersLoadingPage?'Зареждане…':'Зареди още'}
    async function loadOrders(reset=true){if(ordersLoadingPage)return;ordersLoadingPage=true;syncLoadMoreButton();ordersMsg.classList.add('hidden');if(reset){orders=[];ordersNextCursor=null;ordersHasMore=false;orderClinics={};renderOrders()}const result=await ordersApi.listOrders({limit:'50',cursor:!reset?ordersNextCursor:null});const j=result.data;ordersLoadingPage=false;if(!result.ok){ordersMsg.textContent=j.error||'Поръчките не можаха да се заредят.';ordersMsg.classList.remove('hidden');syncLoadMoreButton();return}mergeOrderClinics(j.clinics);const incoming=j.items||[];if(reset)orders=incoming;else{const seen=new Set(orders.map(o=>o.orderCode));orders=orders.concat(incoming.filter(o=>!seen.has(o.orderCode)))}ordersNextCursor=j.nextCursor||null;ordersHasMore=!!j.hasMore;renderOrders();syncLoadMoreButton()}
    async function loadOrdersCalendar(){ordersMsg.classList.add('hidden');const request=++ordersCalendarRequest;const bounds=MonthCalendar.bounds(ordersCalendarMonth),startIso=Format.toIsoDate(bounds.start),endIso=Format.toIsoDate(bounds.end);const [calendarResult,nonWorkingResult]=await Promise.all([ordersApi.calendarOrders(startIso,endIso),ordersApi.nonWorkingDays(startIso,endIso)]);const j=calendarResult.data;if(request!==ordersCalendarRequest)return;if(!calendarResult.ok){ordersMsg.textContent=j.error||'Календарът не можа да се зареди.';ordersMsg.classList.remove('hidden');return}orderClinics={};mergeOrderClinics(j.clinics);ordersCalendarByDate=new Map((j.days||[]).map(d=>[d.date,d.orders||[]]));ordersCalendarCapacityByDate=new Map((j.days||[]).filter(d=>d.capacity).map(d=>[d.date,d.capacity]));ordersCalendarWeeklyCapacityByDate=new Map((j.days||[]).filter(d=>d.weeklyCapacity).map(d=>[d.date,d.weeklyCapacity]));const nonWorkingDays=new Set(nonWorkingResult.ok?(nonWorkingResult.data.dates||[]):[]);renderOrdersCalendar(nonWorkingDays)}
    function clearListHighlight(){listHighlightCode=null;ordersBody.querySelectorAll('.review-row-highlight').forEach(tr=>tr.classList.remove('review-row-highlight'))}
    function clearFindHighlight(){pendingFindListHighlightCode=null;pendingFindCalendarHighlightDate=null}
    function highlightOrderInList(code){if(!code)return;if(ordersViewMode!=='list')return;listHighlightCode=code;const tr=ordersBody.querySelector(`tr[data-code="${CSS.escape(code)}"]`);if(!tr)return;tr.classList.remove('review-row-highlight');void tr.offsetWidth;tr.classList.add('review-row-highlight');tr.scrollIntoView({block:'nearest',behavior:'smooth'});tr.addEventListener('animationend',()=>{if(listHighlightCode===code){listHighlightCode=null;tr.classList.remove('review-row-highlight')}},{once:true})}
    function renderOrders(){ordersBody.innerHTML='';for(const o of orders){const tr=document.createElement('tr');const cancelled=o.status==='cancelled';tr.className=`review-row${cancelled?' review-row-cancelled':''}${listHighlightCode===o.orderCode?' review-row-highlight':''}`;tr.tabIndex=0;tr.dataset.code=o.orderCode;const teethHtml=Format.orderWorkItems(o).map(i=>`<div>${esc(Format.orderWorkItemLabel(i))}</div>`).join('');const delivery=cancelled?'—':Format.formatDeliveryShortBg(o.requestedDeliveryDate);const lab=!!actor()?.isLab;const caseCell=`<td class="col-case">${esc(o.caseName||'—')}</td>`;tr.innerHTML=`<td class="col-code"><span class="order-code-cell">${Format.statusIconHtml(o.status)}${lab?Format.clinicSwatchDotHtml(o,orderClinics):''}<b>${esc(o.shortenedOrderCode||o.orderCode)}</b></span></td>${caseCell}<td class="col-teeth">${teethHtml}<span class="mobile-shade"> · ${esc(Format.shadeShort(o.shade))}</span></td><td class="col-shade">${esc(Format.shadeDisplay(o.shade))}</td><td class="col-delivery">${esc(delivery)}</td><td class="col-action"><button class="btn" type="button" data-review-code="${esc(o.orderCode)}">Преглед</button></td>`;tr.onclick=e=>{if(e.target.closest('button'))return;onOpenOrder(o.orderCode)};tr.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();onOpenOrder(o.orderCode)}};ordersBody.appendChild(tr)}}
    function orderTeethLabel(o){return CalendarCells.orderTeethLabel(o)}
    function orderToothCount(o){return CalendarCells.orderToothCount(o)}
    function orderChipLabel(o){return CalendarCells.orderChipLabel(o)}
    function dayToothTotalText(dayOrders){return CalendarCells.dayToothTotalText(dayOrders)}
    function orderTeethCountWithRange(o){const count=orderToothCount(o);return `${count} ${count===1?'зъб':'зъба'} (${orderTeethLabel(o)})`}
    function orderPopupPrimaryLabel(o){return `${orderTeethCountWithRange(o)} · ${Format.orderMaterialCalendarShort(o)} · ${o.caseName||'—'}`}
    function renderOrdersCalendar(nonWorkingDays){const options={month:ordersCalendarMonth,nonWorkingDays,renderCell:renderOrdersCalendarCell,onMonthChange:m=>{ordersCalendarMonth=m;loadOrdersCalendar()}};if(!ordersCalendarInstance)ordersCalendarInstance=MonthCalendar.create(ordersCalendar,options);else ordersCalendarInstance.setOptions(options)}
    function setOrdersCalendarHighlight(iso){if(ordersCalendarHighlightTimer)clearTimeout(ordersCalendarHighlightTimer);ordersCalendarHighlightDate=iso||null;ordersCalendarHighlightTimer=iso?setTimeout(()=>{if(ordersCalendarHighlightDate===iso){ordersCalendarHighlightDate=null;ordersCalendarHighlightTimer=null;if(ordersCalendarInstance)loadOrdersCalendar()}},5000):null}
    function renderOrdersCalendarCell({cell,content,iso}){if(iso===ordersCalendarHighlightDate)cell.classList.add('orders-calendar-date-highlight');const dayOrders=ordersCalendarByDate.get(iso)||[],weeklyCapacity=ordersCalendarWeeklyCapacityByDate.get(iso);if(weeklyCapacity&&CalendarCells.buildWeeklyCapacityIndicator){const weekly=CalendarCells.buildWeeklyCapacityIndicator(weeklyCapacity);if(weekly)content.appendChild(weekly)}if(!dayOrders.length)return;cell.classList.add('orders-calendar-cell-has-orders');const openDay=()=>openOrdersDayPopup(iso,dayOrders);cell.onclick=e=>{if(e.target.closest('.orders-calendar-chip,.orders-calendar-more,.orders-calendar-count'))return;openDay()};CalendarCells.renderDayOrders(content,dayOrders,{iso,orderClinics,isLab:!!actor()?.isLab,capacity:ordersCalendarCapacityByDate.get(iso),onOpenOrder:onOpenOrder,onOpenDay:openDay})}
    function openOrdersDayPopup(iso,dayOrders,popupOptions={}){const orderClicksEnabled=popupOptions.orderClicksEnabled!==false;ordersDayPopupTitle.textContent=Format.formatDateBulgarianWithWeekday(iso);ordersDayPopupSub.textContent=`${dayOrders.length} ${dayOrders.length===1?'активна поръчка':'активни поръчки'} • ${dayToothTotalText(dayOrders)}`;ordersDayPopupList.innerHTML='';for(const o of dayOrders){const row=document.createElement(orderClicksEnabled?'button':'div');if(orderClicksEnabled)row.type='button';row.className=`orders-day-row${orderClicksEnabled?'':' orders-day-row-static'}`;if(actor()?.isLab)applyClinicAccent(row,o);const clinicMeta=actor()?.isLab?esc(Format.clinicDisplayNameForOrder(o,orderClinics)):'';row.innerHTML=`<b>${esc(orderPopupPrimaryLabel(o))}</b><span>${esc(o.shortenedOrderCode||o.orderCode)} · ${esc(Format.shadeDisplay(o.shade))}${clinicMeta?` · ${clinicMeta}`:''}</span>`;if(orderClicksEnabled)row.onclick=()=>{closeOrdersDayPopup();onOpenOrder(o.orderCode)};ordersDayPopupList.appendChild(row)}openUiModal('ordersDay',ordersDayPopup,ordersDayPopupCloseBtn)}
    function closeOrdersDayPopup(){closeUiModal('ordersDay',ordersDayPopup)}
    function openFindOrderPopup(){findOrderMsg.classList.add('hidden');openUiModal('find',findOrderPopup,()=>orderFindInput)}
    function closeFindOrderPopup(){if(ordersFinding)return;closeUiModal('find',findOrderPopup);findOrderMsg.classList.add('hidden')}
    function applyFindListContext(result){setOrdersCalendarHighlight(null);ordersViewMode='list';saveOrdersViewMode('list');renderOrdersViewModeShell();const page=result.listPage||{};orders=page.items||[];ordersNextCursor=page.nextCursor||null;ordersHasMore=!!page.hasMore;orderClinics={};mergeOrderClinics(page.clinics);listHighlightCode=result.order?.orderCode||null;pendingFindListHighlightCode=listHighlightCode;pendingFindCalendarHighlightDate=null;renderOrders();syncLoadMoreButton();if(result.reason){ordersMsg.textContent=localizeFindText(result.reason);ordersMsg.className='msg warn'}else ordersMsg.classList.add('hidden');highlightOrderInList(listHighlightCode)}
    async function findOrder(){if(ordersFinding)return;const code=(orderFindInput.value||'').trim();if(!code){orderFindInput.focus();return}ordersFinding=true;orderFindBtn.disabled=true;orderFindCancelBtn.disabled=true;orderFindBtn.textContent='Търсене…';findOrderMsg.classList.add('hidden');ordersMsg.classList.add('hidden');try{const result=await ordersApi.findOrder(code,'50');const j=result.data;if(!result.ok){findOrderMsg.textContent=localizeFindText(j.error||'Поръчката не беше намерена.');findOrderMsg.className='msg err';return}const found=j.order;if(!found){findOrderMsg.textContent='Поръчката не е намерена.';findOrderMsg.className='msg err';return}ordersFinding=false;closeUiModal('find',findOrderPopup);if(ordersViewMode==='calendar'&&!j.listModeRecommended&&found.status!=='cancelled'){ordersCalendarMonth=Format.monthForIso(found.requestedDeliveryDate);pendingFindCalendarHighlightDate=found.requestedDeliveryDate;pendingFindListHighlightCode=null;setOrdersCalendarHighlight(found.requestedDeliveryDate);renderOrdersViewModeShell();await loadOrdersCalendar();await onOpenOrder(found.orderCode);return}applyFindListContext(j);await onOpenOrder(found.orderCode)}finally{ordersFinding=false;orderFindBtn.disabled=false;orderFindCancelBtn.disabled=false;orderFindBtn.textContent='Търси'}}
    async function show(){onShowShell();app.classList.add('hidden');reviewCard.classList.add('hidden');closeFindOrderPopup();closeOrdersDayPopup();onCloseCancelOrder();onCloseFlowModals();list.classList.remove('hidden');if(newOrderBtn)newOrderBtn.classList.toggle('hidden',ORDER_CREATION_TEMPORARILY_DISABLED);ordersViewMode=loadOrdersViewMode();renderOrdersViewModeShell();if(actor()?.isLab)await loadClinics();await reload();const routeError=consumeRouteError();if(routeError){ordersMsg.textContent=routeError;ordersMsg.className='msg err'}}
    function clearSession(){orders=[];ordersNextCursor=null;ordersHasMore=false;orderClinics={};ordersCalendarByDate=new Map();ordersCalendarCapacityByDate=new Map();ordersCalendarWeeklyCapacityByDate=new Map();clearFindHighlight();setOrdersCalendarHighlight(null)}
    function restoreFindHighlightAfterReview(restart=true){if(!restart)return clearFindHighlight();if(ordersViewMode==='list'&&pendingFindListHighlightCode){const code=pendingFindListHighlightCode;clearFindHighlight();highlightOrderInList(code)}else if(ordersViewMode==='calendar'&&pendingFindCalendarHighlightDate){const iso=pendingFindCalendarHighlightDate;clearFindHighlight();setOrdersCalendarHighlight(iso);loadOrdersCalendar()}}
    function markListHighlight(code){listHighlightCode=code}
    function isFinding(){return ordersFinding}

    function bind(){
      if(newOrderBtn)newOrderBtn.onclick=()=>onNewOrder(1);
      if(reloadOrdersBtn)reloadOrdersBtn.onclick=reload;
      if(loadMoreOrdersBtn)loadMoreOrdersBtn.onclick=()=>loadOrders(false);
      if(openFindOrderBtn)openFindOrderBtn.onclick=openFindOrderPopup;
      if(orderFindBtn)orderFindBtn.onclick=findOrder;
      if(orderFindCancelBtn)orderFindCancelBtn.onclick=closeFindOrderPopup;
      if(findOrderPopup)findOrderPopup.onclick=e=>{if(e.target===findOrderPopup)closeFindOrderPopup()};
      if(orderFindInput)orderFindInput.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();findOrder()}});
      if(ordersListModeBtn)ordersListModeBtn.onclick=()=>setOrdersViewMode('list');
      if(ordersCalendarModeBtn)ordersCalendarModeBtn.onclick=()=>setOrdersViewMode('calendar');
      if(ordersBody)ordersBody.onclick=e=>{const b=e.target.closest('[data-review-code]');if(b)onOpenOrder(b.dataset.reviewCode)};
      if(ordersDayPopupCloseBtn)ordersDayPopupCloseBtn.onclick=closeOrdersDayPopup;
      if(ordersDayPopup)ordersDayPopup.onclick=e=>{if(e.target===ordersDayPopup)closeOrdersDayPopup()};
    }

    bind();

    return {
      show,
      reload,
      loadMore: () => loadOrders(false),
      setViewMode: setOrdersViewMode,
      openFindOrder: openFindOrderPopup,
      closeFindOrder: closeFindOrderPopup,
      closeOrdersDay: closeOrdersDayPopup,
      openOrdersDay: openOrdersDayPopup,
      clearFindHighlight,
      clearSession,
      restoreFindHighlightAfterReview,
      markListHighlight,
      isFinding
    };
  }

  S3DOrders.RootView = { create: create };
})(typeof window !== 'undefined' ? window : globalThis);
