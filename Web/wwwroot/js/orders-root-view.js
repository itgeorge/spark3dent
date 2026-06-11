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
    const OrdersTeeth = S3DOrders.Teeth;
    const ORDERS_VIEW_MODE_KEY = 's3d.orders.viewMode';

    const app = $('app'), list = $('list'), reviewCard = $('reviewCard'), newOrderBtn = $('newOrderBtn');
    const ordersMsg = $('ordersMsg'), ordersBody = $('ordersBody'), loadMoreOrdersBtn = $('loadMoreOrdersBtn');
    const reloadOrdersBtn = $('reloadOrdersBtn'), openFindOrderBtn = $('openFindOrderBtn'), orderFindBtn = $('orderFindBtn'), orderFindCancelBtn = $('orderFindCancelBtn');
    const findOrderPopup = $('findOrderPopup'), findOrderMsg = $('findOrderMsg'), orderFindInput = $('orderFindInput');
    const ordersListModeBtn = $('ordersListModeBtn'), ordersCalendarModeBtn = $('ordersCalendarModeBtn'), ordersListWrap = $('ordersListWrap'), ordersCalendarWrap = $('ordersCalendarWrap'), ordersCalendar = $('ordersCalendar');
    const ordersDayPopup = $('ordersDayPopup'), ordersDayPopupCloseBtn = $('ordersDayPopupCloseBtn'), ordersDayPopupTitle = $('ordersDayPopupTitle'), ordersDayPopupSub = $('ordersDayPopupSub'), ordersDayPopupList = $('ordersDayPopupList');

    let orders = [], orderClinics = {}, listHighlightCode = null;
    let ordersNextCursor = null, ordersHasMore = false, ordersLoadingPage = false, ordersFinding = false;
    let ordersViewMode = 'list', ordersCalendarMonth = new Date(new Date().getFullYear(), new Date().getMonth(), 1), ordersCalendarRequest = 0, ordersCalendarInstance = null, ordersCalendarByDate = new Map(), ordersCalendarHighlightDate = null, ordersCalendarHighlightTimer = null;
    let pendingFindListHighlightCode = null, pendingFindCalendarHighlightDate = null;

    function actor(){return getActor()}
    function esc(v){return S3DDom.esc(v)}
    function constructionLabel(c){return ({crown:'Crown',bridge:'Bridge',inlayOverlay:'Inlay/Overlay'})[c]||'Construction'}
    function toIsoDate(d){return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`}
    function monthForIso(iso){const [y,m]=iso.split('-').map(Number);return new Date(y,m-1,1)}
    function fdiRangeTeeth(a,b){return OrdersTeeth.range(a,b)}
    function mergeOrderClinics(map){if(!map||typeof map!=='object')return;orderClinics={...orderClinics,...map}}
    function clinicMetaForOrder(o){const code=o?.clinicCode;return code?orderClinics[code]||null:null}
    function normalizeClinicColor(c){const raw=String(c||'').trim();if(/^#[0-9a-fA-F]{6}$/.test(raw))return raw;if(/^#[0-9a-fA-F]{3}$/.test(raw)){const h=raw.slice(1);return `#${h[0]}${h[0]}${h[1]}${h[1]}${h[2]}${h[2]}`}return null}
    function clinicColorForOrder(o){return normalizeClinicColor(o?.clinicDisplayColor||clinicMetaForOrder(o)?.clinicDisplayColor)}
    function clinicDisplayNameForOrder(o){return o?.clinicDisplayName||clinicMetaForOrder(o)?.clinicDisplayName||o?.clinicCode||'Clinic'}
    function clinicSwatchDotHtml(o){const name=clinicDisplayNameForOrder(o),color=clinicColorForOrder(o),cls=`clinic-swatch-dot-only${color?'':' clinic-swatch-neutral'}`,style=color?` style="--clinic-color:${color}"`:'';return `<span class="${cls}"${style} title="${esc(name)}" aria-label="Clinic ${esc(name)}"></span>`}
    function applyClinicAccent(el,o){const color=clinicColorForOrder(o);if(!color)return;el.style.setProperty('--clinic-accent',color);el.classList.add('orders-calendar-chip-clinic')}
    function clinicColorBarsForOrders(dayOrders){const order=[];const counts=new Map();for(const o of dayOrders){const code=o?.clinicCode||o?.orderCode||`__${order.length}`;if(!counts.has(code)){order.push(code);counts.set(code,{count:0,color:clinicColorForOrder(o)})}counts.get(code).count++}return order.map(code=>{const {count,color}=counts.get(code);return{count,color}})}
    function appendClinicColorBar(parent,dayOrders){if(!actor()?.isLab)return;const bars=clinicColorBarsForOrders(dayOrders);if(!bars.length)return;const bar=document.createElement('span');bar.className='orders-calendar-count-colors';bar.setAttribute('aria-hidden','true');bars.forEach(({count,color})=>{const seg=document.createElement('span');seg.style.flex=`${count} 1 0`;if(color)seg.style.setProperty('--clinic-color',color);else seg.classList.add('orders-calendar-count-color-neutral');bar.appendChild(seg)});parent.appendChild(bar)}
    function buildOrdersCalendarCountButton(dayOrders,onOpen){const count=document.createElement('button');count.type='button';count.className='orders-calendar-count';count.onclick=onOpen;const inner=document.createElement('span');inner.className='orders-calendar-count-inner';const label=document.createElement('span');label.className='orders-calendar-count-label';label.textContent=`#${dayOrders.length}`;inner.appendChild(label);appendClinicColorBar(inner,dayOrders);count.appendChild(inner);return count}
    function buildOrdersCalendarMoreButton(dayOrders,onOpen){const more=document.createElement('button');more.type='button';more.className='orders-calendar-more';more.onclick=onOpen;more.title=`View all ${dayOrders.length} · ${dayToothTotalText(dayOrders)}`;const inner=document.createElement('span');inner.className='orders-calendar-more-inner';const label=document.createElement('span');label.className='orders-calendar-more-label';label.textContent=`View all ${dayOrders.length}`;inner.appendChild(label);appendClinicColorBar(inner,dayOrders);more.appendChild(inner);return more}
    function formatDateBulgarian(iso){if(!iso)return '';const [y,m,d]=iso.split('-');const date=new Date(parseInt(y,10),parseInt(m,10)-1,parseInt(d,10));return new Intl.DateTimeFormat('bg-BG',{day:'2-digit',month:'2-digit',year:'numeric'}).format(date)}
    function formatDateBulgarianWithWeekday(iso){if(!iso)return '';const [y,m,d]=iso.split('-');const date=new Date(parseInt(y,10),parseInt(m,10)-1,parseInt(d,10));const weekday=new Intl.DateTimeFormat('bg-BG',{weekday:'long'}).format(date);const dateStr=formatDateBulgarian(iso);const cap=s=>s.charAt(0).toUpperCase()+s.slice(1);return `${cap(weekday)}, ${dateStr}`}
    function formatDeliveryShortBg(iso){if(!iso)return '—';const [y,m,d]=iso.split('-');const date=new Date(parseInt(y,10),parseInt(m,10)-1,parseInt(d,10));return new Intl.DateTimeFormat('bg-BG',{day:'numeric',month:'short'}).format(date).replace(/\.$/,'')}
    function statusText(s){return s==='cancelled'?'Cancelled':s==='created'?'Submitted':(s||'Submitted')}
    function statusIconHtml(s){const cancelled=s==='cancelled';return `<span class="status-icon ${cancelled?'status-cancelled':'status-created'}" title="${statusText(s)}" aria-label="${statusText(s)}">${cancelled?'×':'✓'}</span>`}
    function shadeShort(v){return !v||v==='unspecified'?'—':v}
    function shadeDisplay(v){return !v||v==='unspecified'?'—':v}
    function titleText(v){return String(v||'').replace(/([A-Z])/g,' $1').replace(/^./,c=>c.toUpperCase()).trim()}
    function orderMaterialShort(o){return ({fullContourZirconia:'Zr',pfzLayeredZrCrown:'Layered Zr',pfm:'Metal-ceramic',glassCeramics:'Glass ceramic',pmma:'Temporary PMMA'})[o.material]||titleText(o.material)||'Material'}
    function orderWorkItems(o){return Array.isArray(o.workItems)?o.workItems:[]}
    function orderWorkItemLabel(i){const c=i.constructionType||i.construction||'case';return +i.toothStart===+i.toothEnd?`${constructionLabel(c)} ${i.toothStart||'—'}`:`${constructionLabel(c)} ${i.toothStart||'—'}-${i.toothEnd||'—'}`}
    function orderTeethRange(o){const s=new Set();for(const i of orderWorkItems(o)){const start=+i.toothStart,end=+i.toothEnd;const range=Number.isFinite(start)&&Number.isFinite(end)?fdiRangeTeeth(start,end):null;(range||[]).forEach(t=>s.add(t))}return s.size?[...s]:null}

    function defaultOrdersViewMode(){return actor()?.isLab?'calendar':'list'}
    function loadOrdersViewMode(){try{const saved=localStorage.getItem(ORDERS_VIEW_MODE_KEY);if(saved==='calendar'||saved==='list')return saved}catch{}return defaultOrdersViewMode()}
    function saveOrdersViewMode(mode){try{localStorage.setItem(ORDERS_VIEW_MODE_KEY,mode)}catch{}}
    function renderOrdersViewModeShell(){const calendar=ordersViewMode==='calendar';ordersListWrap.classList.toggle('hidden',calendar);ordersCalendarWrap.classList.toggle('hidden',!calendar);ordersListModeBtn.classList.toggle('active',!calendar);ordersCalendarModeBtn.classList.toggle('active',calendar);ordersListModeBtn.setAttribute('aria-pressed',calendar?'false':'true');ordersCalendarModeBtn.setAttribute('aria-pressed',calendar?'true':'false');syncLoadMoreButton()}
    async function setOrdersViewMode(mode){if(mode!==ordersViewMode){ordersViewMode=mode;saveOrdersViewMode(mode)}renderOrdersViewModeShell();await reload()}
    async function reload(){return ordersViewMode==='calendar'?loadOrdersCalendar():loadOrders(true)}
    function syncLoadMoreButton(){if(!loadMoreOrdersBtn)return;loadMoreOrdersBtn.classList.toggle('hidden',ordersViewMode!=='list'||!ordersHasMore);loadMoreOrdersBtn.disabled=ordersLoadingPage||!ordersHasMore;loadMoreOrdersBtn.textContent=ordersLoadingPage?'Loading…':'Load more'}
    async function loadOrders(reset=true){if(ordersLoadingPage)return;ordersLoadingPage=true;syncLoadMoreButton();ordersMsg.classList.add('hidden');if(reset){orders=[];ordersNextCursor=null;ordersHasMore=false;orderClinics={};renderOrders()}const result=await ordersApi.listOrders({limit:'50',cursor:!reset?ordersNextCursor:null});const j=result.data;ordersLoadingPage=false;if(!result.ok){ordersMsg.textContent=j.error||'Could not load orders.';ordersMsg.classList.remove('hidden');syncLoadMoreButton();return}mergeOrderClinics(j.clinics);const incoming=j.items||[];if(reset)orders=incoming;else{const seen=new Set(orders.map(o=>o.orderCode));orders=orders.concat(incoming.filter(o=>!seen.has(o.orderCode)))}ordersNextCursor=j.nextCursor||null;ordersHasMore=!!j.hasMore;renderOrders();syncLoadMoreButton()}
    async function loadOrdersCalendar(){ordersMsg.classList.add('hidden');const request=++ordersCalendarRequest;const bounds=MonthCalendar.bounds(ordersCalendarMonth),startIso=toIsoDate(bounds.start),endIso=toIsoDate(bounds.end);const result=await ordersApi.calendarOrders(startIso,endIso);const j=result.data;if(request!==ordersCalendarRequest)return;if(!result.ok){ordersMsg.textContent=j.error||'Could not load calendar.';ordersMsg.classList.remove('hidden');return}orderClinics={};mergeOrderClinics(j.clinics);ordersCalendarByDate=new Map((j.days||[]).map(d=>[d.date,d.orders||[]]));renderOrdersCalendar()}
    function clearListHighlight(){listHighlightCode=null;ordersBody.querySelectorAll('.review-row-highlight').forEach(tr=>tr.classList.remove('review-row-highlight'))}
    function clearFindHighlight(){pendingFindListHighlightCode=null;pendingFindCalendarHighlightDate=null}
    function highlightOrderInList(code){if(!code)return;if(ordersViewMode!=='list')return;listHighlightCode=code;const tr=ordersBody.querySelector(`tr[data-code="${CSS.escape(code)}"]`);if(!tr)return;tr.classList.remove('review-row-highlight');void tr.offsetWidth;tr.classList.add('review-row-highlight');tr.scrollIntoView({block:'nearest',behavior:'smooth'});tr.addEventListener('animationend',()=>{if(listHighlightCode===code){listHighlightCode=null;tr.classList.remove('review-row-highlight')}},{once:true})}
    function renderOrders(){ordersBody.innerHTML='';for(const o of orders){const tr=document.createElement('tr');const cancelled=o.status==='cancelled';tr.className=`review-row${cancelled?' review-row-cancelled':''}${listHighlightCode===o.orderCode?' review-row-highlight':''}`;tr.tabIndex=0;tr.dataset.code=o.orderCode;const teethHtml=orderWorkItems(o).map(i=>`<div>${esc(orderWorkItemLabel(i))}</div>`).join('');const delivery=cancelled?'—':formatDeliveryShortBg(o.requestedDeliveryDate);const lab=!!actor()?.isLab;const caseCell=`<td class="col-case">${esc(o.caseName||'—')}</td>`;tr.innerHTML=`<td class="col-code"><span class="order-code-cell">${statusIconHtml(o.status)}${lab?clinicSwatchDotHtml(o):''}<b>${esc(o.shortenedOrderCode||o.orderCode)}</b></span></td>${caseCell}<td class="col-teeth">${teethHtml}<span class="mobile-shade"> · ${esc(shadeShort(o.shade))}</span></td><td class="col-shade">${esc(shadeDisplay(o.shade))}</td><td class="col-delivery">${esc(delivery)}</td><td class="col-action"><button class="btn" type="button" data-review-code="${esc(o.orderCode)}">View</button></td>`;tr.onclick=e=>{if(e.target.closest('button'))return;onOpenOrder(o.orderCode)};tr.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();onOpenOrder(o.orderCode)}};ordersBody.appendChild(tr)}}
    function orderTeethLabel(o){return orderWorkItems(o).map(i=>+i.toothStart===+i.toothEnd?String(i.toothStart):`${i.toothStart}-${i.toothEnd}`).join(', ')}
    function orderToothCount(o){const range=orderTeethRange(o);if(range?.length)return range.length;return orderWorkItems(o).length}
    function orderMaterialCalendarShort(o){return ({fullContourZirconia:'Zr',pfzLayeredZrCrown:'PFZ',pfm:'M',glassCeramics:'C',pmma:'PMMA'})[o.material]||orderMaterialShort(o)}
    function orderChipLabel(o){const teeth=orderTeethLabel(o);const prefix=`${orderMaterialCalendarShort(o)} · ${teeth}`;return prefix.length>28?`${orderToothCount(o)} teeth · ${orderMaterialCalendarShort(o)} · ${o.caseName||'—'}`:`${prefix} · ${o.caseName||'—'}`}
    function orderTeethCountWithRange(o){const count=orderToothCount(o);return `${count} ${count===1?'tooth':'teeth'} (${orderTeethLabel(o)})`}
    function orderPopupPrimaryLabel(o){return `${orderTeethCountWithRange(o)} · ${orderMaterialCalendarShort(o)} · ${o.caseName||'—'}`}
    function dayToothTotalText(dayOrders){const total=dayOrders.reduce((sum,o)=>sum+orderToothCount(o),0);return `${total} total ${total===1?'tooth':'teeth'}`}
    function renderOrdersCalendar(){const options={month:ordersCalendarMonth,renderCell:renderOrdersCalendarCell,onMonthChange:m=>{ordersCalendarMonth=m;loadOrdersCalendar()}};if(!ordersCalendarInstance)ordersCalendarInstance=MonthCalendar.create(ordersCalendar,options);else ordersCalendarInstance.setOptions(options)}
    function setOrdersCalendarHighlight(iso){if(ordersCalendarHighlightTimer)clearTimeout(ordersCalendarHighlightTimer);ordersCalendarHighlightDate=iso||null;ordersCalendarHighlightTimer=iso?setTimeout(()=>{if(ordersCalendarHighlightDate===iso){ordersCalendarHighlightDate=null;ordersCalendarHighlightTimer=null;if(ordersCalendarInstance)renderOrdersCalendar()}},5000):null}
    function renderOrdersCalendarCell({cell,content,iso}){if(iso===ordersCalendarHighlightDate)cell.classList.add('orders-calendar-date-highlight');const dayOrders=ordersCalendarByDate.get(iso)||[];if(!dayOrders.length)return;cell.classList.add('orders-calendar-cell-has-orders');const openDay=()=>openOrdersDayPopup(iso,dayOrders);cell.onclick=e=>{if(e.target.closest('.orders-calendar-chip,.orders-calendar-more,.orders-calendar-count'))return;openDay()};content.appendChild(buildOrdersCalendarCountButton(dayOrders,openDay));dayOrders.slice(0,3).forEach(o=>{const chip=document.createElement('button');chip.type='button';chip.className='orders-calendar-chip';chip.textContent=orderChipLabel(o);const clinicName=clinicDisplayNameForOrder(o);chip.title=`${o.caseName||''} ${clinicName}`.trim();if(actor()?.isLab)applyClinicAccent(chip,o);chip.onclick=e=>{e.stopPropagation();onOpenOrder(o.orderCode)};content.appendChild(chip)});if(dayOrders.length>3){content.appendChild(buildOrdersCalendarMoreButton(dayOrders,openDay))}}
    function openOrdersDayPopup(iso,dayOrders){ordersDayPopupTitle.textContent=formatDateBulgarianWithWeekday(iso);ordersDayPopupSub.textContent=`${dayOrders.length} active ${dayOrders.length===1?'order':'orders'} • ${dayToothTotalText(dayOrders)}`;ordersDayPopupList.innerHTML='';for(const o of dayOrders){const row=document.createElement('button');row.type='button';row.className='orders-day-row';if(actor()?.isLab)applyClinicAccent(row,o);const clinicMeta=actor()?.isLab?esc(clinicDisplayNameForOrder(o)):'';row.innerHTML=`<b>${esc(orderPopupPrimaryLabel(o))}</b><span>${esc(o.shortenedOrderCode||o.orderCode)} · ${esc(shadeDisplay(o.shade))}${clinicMeta?` · ${clinicMeta}`:''}</span>`;row.onclick=()=>{closeOrdersDayPopup();onOpenOrder(o.orderCode)};ordersDayPopupList.appendChild(row)}openUiModal('ordersDay',ordersDayPopup,ordersDayPopupCloseBtn)}
    function closeOrdersDayPopup(){closeUiModal('ordersDay',ordersDayPopup)}
    function openFindOrderPopup(){findOrderMsg.classList.add('hidden');openUiModal('find',findOrderPopup,()=>orderFindInput)}
    function closeFindOrderPopup(){if(ordersFinding)return;closeUiModal('find',findOrderPopup);findOrderMsg.classList.add('hidden')}
    function applyFindListContext(result){setOrdersCalendarHighlight(null);ordersViewMode='list';saveOrdersViewMode('list');renderOrdersViewModeShell();const page=result.listPage||{};orders=page.items||[];ordersNextCursor=page.nextCursor||null;ordersHasMore=!!page.hasMore;orderClinics={};mergeOrderClinics(page.clinics);listHighlightCode=result.order?.orderCode||null;pendingFindListHighlightCode=listHighlightCode;pendingFindCalendarHighlightDate=null;renderOrders();syncLoadMoreButton();if(result.reason){ordersMsg.textContent=result.reason;ordersMsg.className='msg warn'}else ordersMsg.classList.add('hidden');highlightOrderInList(listHighlightCode)}
    async function findOrder(){if(ordersFinding)return;const code=(orderFindInput.value||'').trim();if(!code){orderFindInput.focus();return}ordersFinding=true;orderFindBtn.disabled=true;orderFindCancelBtn.disabled=true;orderFindBtn.textContent='Searching…';findOrderMsg.classList.add('hidden');ordersMsg.classList.add('hidden');try{const result=await ordersApi.findOrder(code,'50');const j=result.data;if(!result.ok){findOrderMsg.textContent=j.error||'Could not find order.';findOrderMsg.className='msg err';return}const found=j.order;if(!found){findOrderMsg.textContent='Order not found.';findOrderMsg.className='msg err';return}ordersFinding=false;closeUiModal('find',findOrderPopup);if(ordersViewMode==='calendar'&&!j.listModeRecommended&&found.status!=='cancelled'){ordersCalendarMonth=monthForIso(found.requestedDeliveryDate);pendingFindCalendarHighlightDate=found.requestedDeliveryDate;pendingFindListHighlightCode=null;setOrdersCalendarHighlight(found.requestedDeliveryDate);renderOrdersViewModeShell();await loadOrdersCalendar();await onOpenOrder(found.orderCode);return}applyFindListContext(j);await onOpenOrder(found.orderCode)}finally{ordersFinding=false;orderFindBtn.disabled=false;orderFindCancelBtn.disabled=false;orderFindBtn.textContent='Search'}}
    async function show(){onShowShell();app.classList.add('hidden');reviewCard.classList.add('hidden');closeFindOrderPopup();closeOrdersDayPopup();onCloseCancelOrder();onCloseFlowModals();list.classList.remove('hidden');newOrderBtn.classList.remove('hidden');ordersViewMode=loadOrdersViewMode();renderOrdersViewModeShell();if(actor()?.isLab)await loadClinics();await reload();const routeError=consumeRouteError();if(routeError){ordersMsg.textContent=routeError;ordersMsg.className='msg err'}}
    function clearSession(){orders=[];ordersNextCursor=null;ordersHasMore=false;orderClinics={};ordersCalendarByDate=new Map();clearFindHighlight();setOrdersCalendarHighlight(null)}
    function restoreFindHighlightAfterReview(restart=true){if(!restart)return clearFindHighlight();if(ordersViewMode==='list'&&pendingFindListHighlightCode){const code=pendingFindListHighlightCode;clearFindHighlight();highlightOrderInList(code)}else if(ordersViewMode==='calendar'&&pendingFindCalendarHighlightDate){const iso=pendingFindCalendarHighlightDate;clearFindHighlight();setOrdersCalendarHighlight(iso);renderOrdersCalendar()}}
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
      clearFindHighlight,
      clearSession,
      restoreFindHighlightAfterReview,
      markListHighlight,
      isFinding
    };
  }

  S3DOrders.RootView = { create: create };
})(typeof window !== 'undefined' ? window : globalThis);
