(function(global){
  'use strict';
  var S3DOrders=global.S3DOrders=global.S3DOrders||{};

  function start(){
if(window.S3DIcons) S3DIcons.hydrate(document);
    const $=id=>document.getElementById(id);
    const ordersApi=S3DOrders.Api.create();
    let actor=null, orders=[], reviewOrder=null, clinics=[], orderClinics={}, listHighlightCode=null;
    let ordersNextCursor=null, ordersHasMore=false, ordersLoadingPage=false, ordersFinding=false;
    let pendingBeforeMinimumDate='';
    const ORDERS_VIEW_MODE_KEY='s3d.orders.viewMode';
    let ordersViewMode='list', ordersCalendarMonth=new Date(new Date().getFullYear(),new Date().getMonth(),1), ordersCalendarRequest=0, ordersCalendarInstance=null, ordersCalendarByDate=new Map(), ordersCalendarHighlightDate=null, ordersCalendarHighlightTimer=null;
    let pendingFindListHighlightCode=null, pendingFindCalendarHighlightDate=null;
    let ordersRouter=null, pendingRouteAfterDiscard=null, pendingOrdersRouteError='';
    const OrdersTeeth=S3DOrders.Teeth;
    const ordersUiModals=window.S3DModal?{
      find:S3DModal.bind({overlay:$('findOrderPopup'),initialFocus:()=>$('orderFindInput'),selectInitialFocus:true,closeWhenBusy:()=>ordersFinding,onClose:()=>{$('findOrderMsg').classList.add('hidden')}}),
      beforeMinimum:S3DModal.bind({overlay:$('beforeMinimumConfirmPopup'),initialFocus:()=>$('beforeMinimumConfirmBackBtn'),onClose:()=>{pendingBeforeMinimumDate=''}}),
      discard:S3DModal.bind({overlay:$('discardOrderFlowPopup'),initialFocus:()=>$('discardOrderFlowBackBtn'),onClose:()=>{pendingRouteAfterDiscard=null}}),
      ordersDay:S3DModal.bind({overlay:$('ordersDayPopup'),initialFocus:()=>$('ordersDayPopupCloseBtn')}),
      cancelOrder:S3DModal.bind({overlay:$('cancelOrderConfirmPopup'),initialFocus:()=>$('cancelOrderConfirmBackBtn'),onClose:()=>{$('cancelOrderConfirmYesBtn').disabled=false;$('cancelOrderConfirmYesBtn').textContent='Yes, cancel order'}})
    }:{};
    function openUiModal(name,overlay,focusTarget){const modal=ordersUiModals[name];if(modal){modal.open();return}S3DDom.setHidden(overlay,false);if(focusTarget)S3DDom.deferFocus(focusTarget)}
    function closeUiModal(name,overlay){const modal=ordersUiModals[name];if(modal)modal.close();else S3DDom.setHidden(overlay,true)}
    function constructionLabel(c){return ({crown:'Crown',bridge:'Bridge',inlayOverlay:'Inlay/Overlay'})[c]||'Construction'}
    function toIsoDate(d){return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`}
    function fdiRangeTeeth(a,b){return OrdersTeeth.range(a,b)}
    function mergeOrderClinics(map){if(!map||typeof map!=='object')return;orderClinics={...orderClinics,...map}}
    function clinicMetaForOrder(o){const code=o?.clinicCode;return code?orderClinics[code]||null:null}
    function normalizeClinicColor(c){const raw=String(c||'').trim();if(/^#[0-9a-fA-F]{6}$/.test(raw))return raw;if(/^#[0-9a-fA-F]{3}$/.test(raw)){const h=raw.slice(1);return `#${h[0]}${h[0]}${h[1]}${h[1]}${h[2]}${h[2]}`}return null}
    function clinicColorForOrder(o){return normalizeClinicColor(o?.clinicDisplayColor||clinicMetaForOrder(o)?.clinicDisplayColor)}
    function clinicDisplayNameForOrder(o){return o?.clinicDisplayName||clinicMetaForOrder(o)?.clinicDisplayName||o?.clinicCode||'Clinic'}
    function clinicSwatchDotHtml(o){const name=clinicDisplayNameForOrder(o),color=clinicColorForOrder(o),cls=`clinic-swatch-dot-only${color?'':' clinic-swatch-neutral'}`,style=color?` style="--clinic-color:${color}"`:'';return `<span class="${cls}"${style} title="${esc(name)}" aria-label="Clinic ${esc(name)}"></span>`}
    function clinicSwatchHtml(o){const name=clinicDisplayNameForOrder(o),color=clinicColorForOrder(o),cls=`clinic-swatch${color?'':' clinic-swatch-neutral'}`,style=color?` style="--clinic-color:${color}"`:'';return `<span class="${cls}"${style} title="${esc(name)}"><span class="clinic-swatch-dot" aria-hidden="true"></span><span class="clinic-swatch-label" title="${esc(name)}">${esc(name)}</span></span>`}
    function applyClinicAccent(el,o){const color=clinicColorForOrder(o);if(!color)return;el.style.setProperty('--clinic-accent',color);el.classList.add('orders-calendar-chip-clinic')}
    function clinicColorBarsForOrders(dayOrders){const order=[];const counts=new Map();for(const o of dayOrders){const code=o?.clinicCode||o?.orderCode||`__${order.length}`;if(!counts.has(code)){order.push(code);counts.set(code,{count:0,color:clinicColorForOrder(o)})}counts.get(code).count++}return order.map(code=>{const {count,color}=counts.get(code);return{count,color}})}
    function appendClinicColorBar(parent,dayOrders){if(!actor?.isLab)return;const bars=clinicColorBarsForOrders(dayOrders);if(!bars.length)return;const bar=document.createElement('span');bar.className='orders-calendar-count-colors';bar.setAttribute('aria-hidden','true');bars.forEach(({count,color})=>{const seg=document.createElement('span');seg.style.flex=`${count} 1 0`;if(color)seg.style.setProperty('--clinic-color',color);else seg.classList.add('orders-calendar-count-color-neutral');bar.appendChild(seg)});parent.appendChild(bar)}
    function buildOrdersCalendarCountButton(dayOrders,onOpen){const count=document.createElement('button');count.type='button';count.className='orders-calendar-count';count.onclick=onOpen;const inner=document.createElement('span');inner.className='orders-calendar-count-inner';const label=document.createElement('span');label.className='orders-calendar-count-label';label.textContent=`#${dayOrders.length}`;inner.appendChild(label);appendClinicColorBar(inner,dayOrders);count.appendChild(inner);return count}
    function buildOrdersCalendarMoreButton(dayOrders,onOpen){const more=document.createElement('button');more.type='button';more.className='orders-calendar-more';more.onclick=onOpen;more.title=`View all ${dayOrders.length} · ${dayToothTotalText(dayOrders)}`;const inner=document.createElement('span');inner.className='orders-calendar-more-inner';const label=document.createElement('span');label.className='orders-calendar-more-label';label.textContent=`View all ${dayOrders.length}`;inner.appendChild(label);appendClinicColorBar(inner,dayOrders);more.appendChild(inner);return more}
    function formatDateBulgarian(iso){if(!iso)return '';const [y,m,d]=iso.split('-');const date=new Date(parseInt(y,10),parseInt(m,10)-1,parseInt(d,10));return new Intl.DateTimeFormat('bg-BG',{day:'2-digit',month:'2-digit',year:'numeric'}).format(date)}
    function formatDateBulgarianWithWeekday(iso){if(!iso)return '';const [y,m,d]=iso.split('-');const date=new Date(parseInt(y,10),parseInt(m,10)-1,parseInt(d,10));const weekday=new Intl.DateTimeFormat('bg-BG',{weekday:'long'}).format(date);const dateStr=formatDateBulgarian(iso);const cap=s=>s.charAt(0).toUpperCase()+s.slice(1);return `${cap(weekday)}, ${dateStr}`}
    function reviewDateCompactMode(){return window.matchMedia('(max-width:900px)').matches&&!!reviewTeeth?.closest('.overview-body')?.classList.contains('overview-body-compact')}
    function formatReviewDeliveryDate(iso){if(!iso)return '';return reviewDateCompactMode()?formatDateBulgarian(iso):formatDateBulgarianWithWeekday(iso)}
    function formatDeliveryShortBg(iso){if(!iso)return '—';const [y,m,d]=iso.split('-');const date=new Date(parseInt(y,10),parseInt(m,10)-1,parseInt(d,10));return new Intl.DateTimeFormat('bg-BG',{day:'numeric',month:'short'}).format(date).replace(/\.$/,'')}
    function renderSelectedTeethPreview(container,range,items){S3DOrders.SelectedTeethPreview.render(container,{teeth:range||[],items:items||[],labelPrefix:'Selected teeth',getItemLabel:orderWorkItemLabel})}
    function setOverviewField(el,text){if(!el)return;if('value' in el)el.value=text;else el.textContent=text}
    function setOverviewShade(el,text){if(!el)return;const line=text||'';el.textContent=line;el.classList.toggle('hidden',!line)}
    function syncOverviewBodyLayout(bodyEl,range){if(!bodyEl)return;const count=range?.length||0;bodyEl.classList.toggle('overview-body-compact',count>0&&count<=2)}
    function closeBeforeMinimumConfirmPopup(){closeUiModal('beforeMinimum',beforeMinimumConfirmPopup);pendingBeforeMinimumDate=''}
    function monthForIso(iso){const [y,m]=iso.split('-').map(Number);return new Date(y,m-1,1)}
    function statusText(s){return s==='cancelled'?'Cancelled':s==='created'?'Submitted':(s||'Submitted')}
    function statusIconHtml(s){const cancelled=s==='cancelled';return `<span class="status-icon ${cancelled?'status-cancelled':'status-created'}" title="${statusText(s)}" aria-label="${statusText(s)}">${cancelled?'×':'✓'}</span>`}
    function shadeShort(v){return !v||v==='unspecified'?'—':v}
    function shadeDisplay(v){return !v||v==='unspecified'?'—':v}
    function esc(v){return S3DDom.esc(v)}
    function titleText(v){return String(v||'').replace(/([A-Z])/g,' $1').replace(/^./,c=>c.toUpperCase()).trim()}
    function orderMaterialShort(o){return ({fullContourZirconia:'Zr',pfzLayeredZrCrown:'Layered Zr',pfm:'Metal-ceramic',glassCeramics:'Glass ceramic',pmma:'Temporary PMMA'})[o.material]||titleText(o.material)||'Material'}
    function orderWorkItems(o){return Array.isArray(o.workItems)?o.workItems:[]}
    function orderWorkItemLabel(i){const c=i.constructionType||i.construction||'case';return +i.toothStart===+i.toothEnd?`${constructionLabel(c)} ${i.toothStart||'—'}`:`${constructionLabel(c)} ${i.toothStart||'—'}-${i.toothEnd||'—'}`}
    function orderOverviewBaseText(o){return `${orderMaterialShort(o)} · ${orderWorkItems(o).map(orderWorkItemLabel).join(', ')}`}
    function orderOverviewShadeLine(o){return o.shade&&o.shade!=='unspecified'?`shade ${o.shade}`:''}
    function orderTeethRange(o){const s=new Set();for(const i of orderWorkItems(o)){const start=+i.toothStart,end=+i.toothEnd;const range=Number.isFinite(start)&&Number.isFinite(end)?fdiRangeTeeth(start,end):null;(range||[]).forEach(t=>s.add(t))}return s.size?[...s]:null}
    function syncTopbar(){appChrome.sync(actor)}
    function resetLoginButton(){loginBtn.disabled=false;loginBtn.textContent='Enter'}
    function showAuthenticatedAppShell(){document.body.classList.remove('auth-locked');login.classList.add('hidden');resetLoginButton();syncTopbar()}
    let flowView;
    const ordersDirtyGuard=S3DDirtyNavigation.createGuard({isDirty:()=>flowView?flowView.isDirty():false,isSafeTransition:(from,to,navOptions)=>flowView?flowView.isSafeTransition(from,to,navOptions):false,showPrompt:to=>flowView?flowView.promptDiscard({path:to.path}):undefined});
    function openDiscardOrderFlowPopup(targetRoute=null){pendingRouteAfterDiscard=targetRoute;openUiModal('discard',discardOrderFlowPopup,discardOrderFlowBackBtn)}
    function closeDiscardOrderFlowPopup(){closeUiModal('discard',discardOrderFlowPopup);pendingRouteAfterDiscard=null}
    async function ordersBeforeLeave(from,to,navOptions={}){return ordersDirtyGuard.beforeLeave(from,to,navOptions)}
    function showLogin(){document.body.classList.add('auth-locked');login.classList.remove('hidden');list.classList.add('hidden');reviewCard.classList.add('hidden');app.classList.add('hidden');closeFindOrderPopup();closeOrdersDayPopup();closeCancelOrderConfirmPopup();if(flowView){flowView.closeDiscard();flowView.closeBeforeMinimum()}else{closeDiscardOrderFlowPopup();closeBeforeMinimumConfirmPopup()}actor=null;resetLoginButton();syncTopbar()}
    function defaultOrdersViewMode(){return actor?.isLab?'calendar':'list'}
    function loadOrdersViewMode(){try{const saved=localStorage.getItem(ORDERS_VIEW_MODE_KEY);if(saved==='calendar'||saved==='list')return saved}catch{}return defaultOrdersViewMode()}
    function saveOrdersViewMode(mode){try{localStorage.setItem(ORDERS_VIEW_MODE_KEY,mode)}catch{}}
    async function showList(){showAuthenticatedAppShell();app.classList.add('hidden');reviewCard.classList.add('hidden');closeFindOrderPopup();closeOrdersDayPopup();closeCancelOrderConfirmPopup();if(flowView){flowView.closeDiscard();flowView.closeBeforeMinimum()}else{closeDiscardOrderFlowPopup();closeBeforeMinimumConfirmPopup()}list.classList.remove('hidden');newOrderBtn.classList.remove('hidden');ordersViewMode=loadOrdersViewMode();renderOrdersViewModeShell();if(actor?.isLab)await loadClinics();await reloadCurrentOrdersView();if(pendingOrdersRouteError){ordersMsg.textContent=pendingOrdersRouteError;ordersMsg.className='msg err';pendingOrdersRouteError=''}}
    async function loadClinics(){if(!actor?.isLab)return;if(!clinics.length){const result=await ordersApi.clinics();const j=result.data;if(result.ok)clinics=j.items||[]}}
    async function loadOrderByCode(code){const result=await ordersApi.getOrder(code);const j=result.data;if(!result.ok)throw new Error(j.error||'Could not load order.');return j.order}
    function goOrdersRoot(opts){return ordersRouter.navigate('',opts)}
    function goOrderReview(code,opts){return ordersRouter.navigate(`order/${encodeURIComponent(code)}`,opts)}
    function goNewOrder(stepToOpen=1,opts){return ordersRouter.navigate(`new/${stepToOpen}`,opts)}
    function goEditOrder(codeToOpen,stepToOpen=1,opts){return ordersRouter.navigate(`edit/${encodeURIComponent(codeToOpen)}/${stepToOpen}`,opts)}
    function renderOrdersViewModeShell(){const calendar=ordersViewMode==='calendar';ordersListWrap.classList.toggle('hidden',calendar);ordersCalendarWrap.classList.toggle('hidden',!calendar);ordersListModeBtn.classList.toggle('active',!calendar);ordersCalendarModeBtn.classList.toggle('active',calendar);ordersListModeBtn.setAttribute('aria-pressed',calendar?'false':'true');ordersCalendarModeBtn.setAttribute('aria-pressed',calendar?'true':'false');syncLoadMoreButton()}
    async function setOrdersViewMode(mode){if(mode!==ordersViewMode){ordersViewMode=mode;saveOrdersViewMode(mode)}renderOrdersViewModeShell();await reloadCurrentOrdersView()}
    async function reloadCurrentOrdersView(){return ordersViewMode==='calendar'?loadOrdersCalendar():loadOrders(true)}
    function syncLoadMoreButton(){if(!loadMoreOrdersBtn)return;loadMoreOrdersBtn.classList.toggle('hidden',ordersViewMode!=='list'||!ordersHasMore);loadMoreOrdersBtn.disabled=ordersLoadingPage||!ordersHasMore;loadMoreOrdersBtn.textContent=ordersLoadingPage?'Loading…':'Load more'}
    async function loadOrders(reset=true){if(ordersLoadingPage)return;ordersLoadingPage=true;syncLoadMoreButton();ordersMsg.classList.add('hidden');if(reset){orders=[];ordersNextCursor=null;ordersHasMore=false;orderClinics={};renderOrders()}const result=await ordersApi.listOrders({limit:'50',cursor:!reset?ordersNextCursor:null});const j=result.data;ordersLoadingPage=false;if(!result.ok){ordersMsg.textContent=j.error||'Could not load orders.';ordersMsg.classList.remove('hidden');syncLoadMoreButton();return}mergeOrderClinics(j.clinics);const incoming=j.items||[];if(reset)orders=incoming;else{const seen=new Set(orders.map(o=>o.orderCode));orders=orders.concat(incoming.filter(o=>!seen.has(o.orderCode)))}ordersNextCursor=j.nextCursor||null;ordersHasMore=!!j.hasMore;renderOrders();syncLoadMoreButton()}
    async function loadOrdersCalendar(){ordersMsg.classList.add('hidden');const request=++ordersCalendarRequest;const bounds=MonthCalendar.bounds(ordersCalendarMonth),startIso=toIsoDate(bounds.start),endIso=toIsoDate(bounds.end);const result=await ordersApi.calendarOrders(startIso,endIso);const j=result.data;if(request!==ordersCalendarRequest)return;if(!result.ok){ordersMsg.textContent=j.error||'Could not load calendar.';ordersMsg.classList.remove('hidden');return}orderClinics={};mergeOrderClinics(j.clinics);ordersCalendarByDate=new Map((j.days||[]).map(d=>[d.date,d.orders||[]]));renderOrdersCalendar()}
    function clearListHighlight(){listHighlightCode=null;ordersBody.querySelectorAll('.review-row-highlight').forEach(tr=>tr.classList.remove('review-row-highlight'))}
    function clearPendingFindHighlight(){pendingFindListHighlightCode=null;pendingFindCalendarHighlightDate=null}
    function highlightOrderInList(code){if(!code)return;if(ordersViewMode!=='list')return;listHighlightCode=code;const tr=ordersBody.querySelector(`tr[data-code="${CSS.escape(code)}"]`);if(!tr)return;tr.classList.remove('review-row-highlight');void tr.offsetWidth;tr.classList.add('review-row-highlight');tr.scrollIntoView({block:'nearest',behavior:'smooth'});tr.addEventListener('animationend',()=>{if(listHighlightCode===code){listHighlightCode=null;tr.classList.remove('review-row-highlight')}},{once:true})}
    function renderOrders(){ordersBody.innerHTML='';for(const o of orders){const tr=document.createElement('tr');const cancelled=o.status==='cancelled';tr.className=`review-row${cancelled?' review-row-cancelled':''}${listHighlightCode===o.orderCode?' review-row-highlight':''}`;tr.tabIndex=0;tr.dataset.code=o.orderCode;const teethHtml=orderWorkItems(o).map(i=>`<div>${esc(orderWorkItemLabel(i))}</div>`).join('');const delivery=cancelled?'—':formatDeliveryShortBg(o.requestedDeliveryDate);const lab=!!actor?.isLab;const caseCell=`<td class="col-case">${esc(o.caseName||'—')}</td>`;tr.innerHTML=`<td class="col-code"><span class="order-code-cell">${statusIconHtml(o.status)}${lab?clinicSwatchDotHtml(o):''}<b>${esc(o.shortenedOrderCode||o.orderCode)}</b></span></td>${caseCell}<td class="col-teeth">${teethHtml}<span class="mobile-shade"> · ${esc(shadeShort(o.shade))}</span></td><td class="col-shade">${esc(shadeDisplay(o.shade))}</td><td class="col-delivery">${esc(delivery)}</td><td class="col-action"><button class="btn" type="button" data-review-code="${esc(o.orderCode)}">View</button></td>`;tr.onclick=e=>{if(e.target.closest('button'))return;goOrderReview(o.orderCode)};tr.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();goOrderReview(o.orderCode)}};ordersBody.appendChild(tr)}}
    function allCalendarOrders(){return [...ordersCalendarByDate.values()].flat()}
    function orderTeethLabel(o){return orderWorkItems(o).map(i=>+i.toothStart===+i.toothEnd?String(i.toothStart):`${i.toothStart}-${i.toothEnd}`).join(', ')}
    function orderToothCount(o){const range=orderTeethRange(o);if(range?.length)return range.length;return orderWorkItems(o).length}
    function orderMaterialCalendarShort(o){return ({fullContourZirconia:'Zr',pfzLayeredZrCrown:'PFZ',pfm:'M',glassCeramics:'C',pmma:'PMMA'})[o.material]||orderMaterialShort(o)}
    function orderChipLabel(o){const teeth=orderTeethLabel(o);const prefix=`${orderMaterialCalendarShort(o)} · ${teeth}`;return prefix.length>28?`${orderToothCount(o)} teeth · ${orderMaterialCalendarShort(o)} · ${o.caseName||'—'}`:`${prefix} · ${o.caseName||'—'}`}
    function orderTeethCountWithRange(o){const count=orderToothCount(o);return `${count} ${count===1?'tooth':'teeth'} (${orderTeethLabel(o)})`}
    function orderPopupPrimaryLabel(o){return `${orderTeethCountWithRange(o)} · ${orderMaterialCalendarShort(o)} · ${o.caseName||'—'}`}
    function dayToothTotalText(dayOrders){const total=dayOrders.reduce((sum,o)=>sum+orderToothCount(o),0);return `${total} total ${total===1?'tooth':'teeth'}`}
    function renderOrdersCalendar(){const options={month:ordersCalendarMonth,renderCell:renderOrdersCalendarCell,onMonthChange:m=>{ordersCalendarMonth=m;loadOrdersCalendar()}};if(!ordersCalendarInstance)ordersCalendarInstance=MonthCalendar.create(ordersCalendar,options);else ordersCalendarInstance.setOptions(options)}
    function setOrdersCalendarHighlight(iso){if(ordersCalendarHighlightTimer)clearTimeout(ordersCalendarHighlightTimer);ordersCalendarHighlightDate=iso||null;ordersCalendarHighlightTimer=iso?setTimeout(()=>{if(ordersCalendarHighlightDate===iso){ordersCalendarHighlightDate=null;ordersCalendarHighlightTimer=null;if(ordersCalendarInstance)renderOrdersCalendar()}},5000):null}
    function renderOrdersCalendarCell({cell,content,iso}){if(iso===ordersCalendarHighlightDate)cell.classList.add('orders-calendar-date-highlight');const dayOrders=ordersCalendarByDate.get(iso)||[];if(!dayOrders.length)return;cell.classList.add('orders-calendar-cell-has-orders');const openDay=()=>openOrdersDayPopup(iso,dayOrders);cell.onclick=e=>{if(e.target.closest('.orders-calendar-chip,.orders-calendar-more,.orders-calendar-count'))return;openDay()};content.appendChild(buildOrdersCalendarCountButton(dayOrders,openDay));dayOrders.slice(0,3).forEach(o=>{const chip=document.createElement('button');chip.type='button';chip.className='orders-calendar-chip';chip.textContent=orderChipLabel(o);const clinicName=clinicDisplayNameForOrder(o);chip.title=`${o.caseName||''} ${clinicName}`.trim();if(actor?.isLab)applyClinicAccent(chip,o);chip.onclick=e=>{e.stopPropagation();goOrderReview(o.orderCode)};content.appendChild(chip)});if(dayOrders.length>3){content.appendChild(buildOrdersCalendarMoreButton(dayOrders,openDay))}}
    function openOrdersDayPopup(iso,dayOrders){ordersDayPopupTitle.textContent=formatDateBulgarianWithWeekday(iso);ordersDayPopupSub.textContent=`${dayOrders.length} active ${dayOrders.length===1?'order':'orders'} • ${dayToothTotalText(dayOrders)}`;ordersDayPopupList.innerHTML='';for(const o of dayOrders){const row=document.createElement('button');row.type='button';row.className='orders-day-row';if(actor?.isLab)applyClinicAccent(row,o);const clinicMeta=actor?.isLab?esc(clinicDisplayNameForOrder(o)):'';row.innerHTML=`<b>${esc(orderPopupPrimaryLabel(o))}</b><span>${esc(o.shortenedOrderCode||o.orderCode)} · ${esc(shadeDisplay(o.shade))}${clinicMeta?` · ${clinicMeta}`:''}</span>`;row.onclick=()=>{closeOrdersDayPopup();goOrderReview(o.orderCode)};ordersDayPopupList.appendChild(row)}openUiModal('ordersDay',ordersDayPopup,ordersDayPopupCloseBtn)}
    function closeOrdersDayPopup(){closeUiModal('ordersDay',ordersDayPopup)}
    function openFindOrderPopup(){findOrderMsg.classList.add('hidden');openUiModal('find',findOrderPopup,()=>orderFindInput)}
    function closeFindOrderPopup(){if(ordersFinding)return;closeUiModal('find',findOrderPopup);findOrderMsg.classList.add('hidden')}
    function applyFindListContext(result){setOrdersCalendarHighlight(null);ordersViewMode='list';saveOrdersViewMode('list');renderOrdersViewModeShell();const page=result.listPage||{};orders=page.items||[];ordersNextCursor=page.nextCursor||null;ordersHasMore=!!page.hasMore;orderClinics={};mergeOrderClinics(page.clinics);listHighlightCode=result.order?.orderCode||null;pendingFindListHighlightCode=listHighlightCode;pendingFindCalendarHighlightDate=null;renderOrders();syncLoadMoreButton();if(result.reason){ordersMsg.textContent=result.reason;ordersMsg.className='msg warn'}else ordersMsg.classList.add('hidden');highlightOrderInList(listHighlightCode)}
    async function findOrder(){if(ordersFinding)return;const code=(orderFindInput.value||'').trim();if(!code){orderFindInput.focus();return}ordersFinding=true;orderFindBtn.disabled=true;orderFindCancelBtn.disabled=true;orderFindBtn.textContent='Searching…';findOrderMsg.classList.add('hidden');ordersMsg.classList.add('hidden');try{const result=await ordersApi.findOrder(code,'50');const j=result.data;if(!result.ok){findOrderMsg.textContent=j.error||'Could not find order.';findOrderMsg.className='msg err';return}const found=j.order;if(!found){findOrderMsg.textContent='Order not found.';findOrderMsg.className='msg err';return}ordersFinding=false;closeUiModal('find',findOrderPopup);if(ordersViewMode==='calendar'&&!j.listModeRecommended&&found.status!=='cancelled'){ordersCalendarMonth=monthForIso(found.requestedDeliveryDate);pendingFindCalendarHighlightDate=found.requestedDeliveryDate;pendingFindListHighlightCode=null;setOrdersCalendarHighlight(found.requestedDeliveryDate);renderOrdersViewModeShell();await loadOrdersCalendar();await goOrderReview(found.orderCode);return}applyFindListContext(j);await goOrderReview(found.orderCode)}finally{ordersFinding=false;orderFindBtn.disabled=false;orderFindCancelBtn.disabled=false;orderFindBtn.textContent='Search'}}
    
    async function showReview(codeToOpen){if(!actor)return showLogin();showAuthenticatedAppShell();closeOrdersDayPopup();closeCancelOrderConfirmPopup();reviewMsg.classList.add('hidden');try{reviewOrder=await loadOrderByCode(codeToOpen)}catch(err){pendingOrdersRouteError=err.message||'Could not load order.';await ordersRouter.replace('',{skipGuard:true});return}mergeOrderClinics(reviewOrder?.clinicCode?{[reviewOrder.clinicCode]:{clinicCode:reviewOrder.clinicCode,clinicDisplayName:reviewOrder.clinicDisplayName,clinicDisplayColor:reviewOrder.clinicDisplayColor}}:null);renderReview(reviewOrder);list.classList.add('hidden');app.classList.add('hidden');reviewCard.classList.remove('hidden');reviewCard.setAttribute('aria-hidden','false');reviewCard.scrollIntoView({block:'start'});reviewBackTopBtn.focus()}
    function closeReview(restartFindHighlight=true){closeCancelOrderConfirmPopup();reviewCard.classList.add('hidden');reviewCard.setAttribute('aria-hidden','true');list.classList.remove('hidden');if(!restartFindHighlight)return clearPendingFindHighlight();if(ordersViewMode==='list'&&pendingFindListHighlightCode){const code=pendingFindListHighlightCode;clearPendingFindHighlight();highlightOrderInList(code)}else if(ordersViewMode==='calendar'&&pendingFindCalendarHighlightDate){const iso=pendingFindCalendarHighlightDate;clearPendingFindHighlight();setOrdersCalendarHighlight(iso);renderOrdersCalendar()}}
    if(!window.__reviewDateResizeBound){window.__reviewDateResizeBound=true;window.addEventListener('resize',()=>{if(reviewOrder&&!reviewCard.classList.contains('hidden')){const iso=reviewOrder.requestedDeliveryDate;reviewOverviewDate.value=iso?formatReviewDeliveryDate(iso):''}})}
    function renderReview(o){reviewCode.textContent=o.shortenedOrderCode||o.orderCode||'—';reviewSub.textContent=`${statusText(o.status)}${actor?.isLab?'':` • ${o.clinicDisplayName||o.clinicCode||''}`}`;const reviewClinicMetaEl=$('reviewClinicMeta');if(reviewClinicMetaEl){if(actor?.isLab){reviewClinicMetaEl.classList.remove('hidden');reviewClinicMetaEl.innerHTML=clinicSwatchHtml(o)}else{reviewClinicMetaEl.classList.add('hidden');reviewClinicMetaEl.innerHTML=''}}reviewOverviewText.textContent=orderOverviewBaseText(o);setOverviewShade(reviewOverviewShade,orderOverviewShadeLine(o));reviewCaseName.textContent=o.caseName||'—';reviewExtraNote.textContent=o.notes?`Note: ${o.notes}`:'';reviewExtraNote.classList.toggle('hidden',!o.notes);const cancelled=o.status==='cancelled';reviewEditBtn.disabled=cancelled;reviewCancelBtn.disabled=cancelled;const range=orderTeethRange(o),previewItems=orderWorkItems(o);syncOverviewBodyLayout(reviewTeeth.closest('.overview-body'),range);reviewOverviewDate.value=o.requestedDeliveryDate?formatReviewDeliveryDate(o.requestedDeliveryDate):'';renderSelectedTeethPreview(reviewTeeth,range,previewItems)}
    function editReviewOrder(){if(!reviewOrder||reviewOrder.status==='cancelled')return;clearPendingFindHighlight();goEditOrder(reviewOrder.orderCode,1)}
    function openCancelOrderConfirmPopup(){if(!reviewOrder||reviewOrder.status==='cancelled')return;const code=reviewOrder.shortenedOrderCode||reviewOrder.orderCode||'—';cancelOrderConfirmText.innerHTML=`Are you sure you want to cancel order <span class="cancel-order-confirm-code">${esc(code)}</span>?`;openUiModal('cancelOrder',cancelOrderConfirmPopup,cancelOrderConfirmBackBtn)}
    function closeCancelOrderConfirmPopup(){closeUiModal('cancelOrder',cancelOrderConfirmPopup);cancelOrderConfirmYesBtn.disabled=false;cancelOrderConfirmYesBtn.textContent='Yes, cancel order'}
    function promptCancelReviewOrder(){if(!reviewOrder||reviewOrder.status==='cancelled')return;openCancelOrderConfirmPopup()}
    async function confirmCancelReviewOrder(){if(!reviewOrder||reviewOrder.status==='cancelled')return;cancelOrderConfirmYesBtn.disabled=true;cancelOrderConfirmYesBtn.textContent='Cancelling…';reviewMsg.classList.add('hidden');const result=await ordersApi.deleteOrder(reviewOrder.orderCode);const j=result.data;if(!result.ok){closeCancelOrderConfirmPopup();reviewMsg.textContent=j.error||'Could not cancel order.';reviewMsg.classList.remove('hidden');return}closeCancelOrderConfirmPopup();reviewOrder=null;clearPendingFindHighlight();await reloadCurrentOrdersView();await goOrdersRoot({skipDirtyGuard:true})}
    async function loadMe(){const result=await ordersApi.me();if(!result.ok)return showLogin();actor=result.data;await ordersRouter.refresh()}
    loginBtn.onclick=async()=>{if(loginBtn.disabled)return;await S3DActionButton.run(loginBtn,{busyText:'Signing in…',action:async()=>{loginMsg.classList.add('hidden');try{const result=await ordersApi.login({organizationCode:clinic.value,pin:pin.value});const j=result.data;if(!result.ok){loginMsg.textContent=j.error||'Login failed.';loginMsg.classList.remove('hidden');return}actor=j;loginMsg.classList.add('hidden');await ordersRouter.refresh()}catch{loginMsg.textContent='Login failed.';loginMsg.classList.remove('hidden')}}})};
    [clinic,pin].forEach(el=>el.addEventListener('keydown',e=>{if(e.key==='Enter')loginBtn.click()}));
    newOrderBtn.onclick=()=>goNewOrder(1);
    reloadOrdersBtn.onclick=reloadCurrentOrdersView;
    loadMoreOrdersBtn.onclick=()=>loadOrders(false);
    openFindOrderBtn.onclick=openFindOrderPopup;
    orderFindBtn.onclick=findOrder;
    orderFindCancelBtn.onclick=closeFindOrderPopup;
    findOrderPopup.onclick=e=>{if(e.target===findOrderPopup)closeFindOrderPopup()};
    orderFindInput.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();findOrder()}});
    ordersListModeBtn.onclick=()=>setOrdersViewMode('list');
    ordersCalendarModeBtn.onclick=()=>setOrdersViewMode('calendar');
    ordersBody.onclick=e=>{const b=e.target.closest('[data-review-code]');if(b)goOrderReview(b.dataset.reviewCode)};
    reviewBackTopBtn.onclick=()=>goOrdersRoot();reviewCloseTopBtn.onclick=()=>goOrdersRoot();
    reviewEditBtn.onclick=editReviewOrder;
    reviewCancelBtn.onclick=promptCancelReviewOrder;
    cancelOrderConfirmBackBtn.onclick=closeCancelOrderConfirmPopup;
    cancelOrderConfirmYesBtn.onclick=confirmCancelReviewOrder;
    cancelOrderConfirmPopup.onclick=e=>{if(e.target===cancelOrderConfirmPopup)closeCancelOrderConfirmPopup()};
    ordersDayPopupCloseBtn.onclick=closeOrdersDayPopup;
    ordersDayPopup.onclick=e=>{if(e.target===ordersDayPopup)closeOrdersDayPopup()};
    document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!reviewCard.classList.contains('hidden')){e.preventDefault();goOrdersRoot()}});
    const rootView=S3DOrders.RootView.create({show:showList,reload:reloadCurrentOrdersView,loadMore:()=>loadOrders(false),setViewMode:setOrdersViewMode,openFindOrder:openFindOrderPopup,closeFindOrder:closeFindOrderPopup,openOrdersDay:openOrdersDayPopup,closeOrdersDay:closeOrdersDayPopup,clearFindHighlight:clearPendingFindHighlight,clearSession:()=>{orders=[];ordersNextCursor=null;ordersHasMore=false;orderClinics={};ordersCalendarByDate=new Map();clearPendingFindHighlight();setOrdersCalendarHighlight(null)}});
    const reviewView=S3DOrders.ReviewView.create({show:showReview,close:closeReview,render:renderReview,edit:editReviewOrder,promptCancel:promptCancelReviewOrder,confirmCancel:confirmCancelReviewOrder,closeCancel:closeCancelOrderConfirmPopup});
    flowView=S3DOrders.FlowView.create({api:ordersApi,getActor:()=>actor,getClinics:()=>clinics,loadClinics:loadClinics,showLogin:showLogin,showAuthenticatedAppShell:showAuthenticatedAppShell,closeFindOrder:closeFindOrderPopup,closeOrdersDay:closeOrdersDayPopup,closeCancelOrder:closeCancelOrderConfirmPopup,openUiModal:openUiModal,closeUiModal:closeUiModal,navigate:(path,opts)=>ordersRouter.navigate(path,opts),replace:(path,opts)=>ordersRouter.replace(path,opts),onRouteError:msg=>{pendingOrdersRouteError=msg},onEditSaved:code=>{reviewOrder=null;listHighlightCode=code}});
    const createdConfirmationView=S3DOrders.CreatedConfirmationView.create({show:code=>flowView.showCreated(code),done:goOrdersRoot,render:()=>flowView.renderFinalOverview()});
    const appChrome=AppChrome.mount($('appChromeMount'),{product:'scheduler',logoSrc:'/images/logo.png',menuButtonClass:'btn',hideMenuWhenSignedOut:true,brandClick:()=>{if(actor)goOrdersRoot()},brandTitle:'Back to orders'});
    appChrome.onLogout(async()=>{await ordersApi.logout();actor=null;rootView.clearSession();showLogin()});
    ordersRouter=HashRouter.createHashRouter({
      routes:[
        {name:'root',pattern:'',handler:()=>actor?rootView.show():showLogin()},
        {name:'orderReview',pattern:'order/:code',handler:ctx=>reviewView.show(ctx.params.code)},
        {name:'newOrder',pattern:'new/:step',handler:ctx=>flowView.showNew(ctx.params.step)},
        {name:'createdOrder',pattern:'created/:code',handler:ctx=>createdConfirmationView.show(ctx.params.code)},
        {name:'editOrder',pattern:'edit/:code/:step',handler:ctx=>flowView.showEdit(ctx.params.code,ctx.params.step)}
      ],
      beforeLeave:ordersBeforeLeave,
      notFound:()=>ordersRouter.replace('',{skipGuard:true}),
      onError:(err)=>{console.error(err);ordersMsg.textContent=err?.message||'Navigation failed.';ordersMsg.className='msg err'}
    });
    ordersRouter.start();
    loadMe();
    
  }

  S3DOrders.Page={start:start};
})(typeof window !== 'undefined' ? window : globalThis);
